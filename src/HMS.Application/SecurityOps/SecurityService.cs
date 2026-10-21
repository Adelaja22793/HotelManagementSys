using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Contracts.SecurityOps;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HMS.Application.SecurityOps;

/// <summary>Retention and custody rules. Review them with the hotel's privacy and legal advisers.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    /// <summary>Found property is kept at least this long before disposal or donation.</summary>
    public int LostFoundHoldDays { get; set; } = 90;
    /// <summary>Visitor log entries older than this may be purged.</summary>
    public int VisitorRetentionDays { get; set; } = 365;
}

public interface ISecurityService
{
    Task<IReadOnlyList<IncidentDto>> ListIncidentsAsync(Guid branchId, bool openOnly, CancellationToken ct = default);
    Task<IncidentDto> GetIncidentAsync(Guid branchId, Guid incidentId, CancellationToken ct = default);
    Task<IncidentDto> ReportIncidentAsync(Guid branchId, CreateIncidentRequest request, CancellationToken ct = default);
    Task<IncidentDto> UpdateIncidentAsync(Guid branchId, Guid incidentId, CaseUpdateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<LostItemDto>> ListLostItemsAsync(Guid branchId, bool inCustodyOnly, CancellationToken ct = default);
    Task<LostItemDto> RecordLostItemAsync(Guid branchId, CreateLostItemRequest request, CancellationToken ct = default);
    Task<LostItemDto> CustodyAsync(Guid branchId, Guid itemId, CustodyRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<VisitorDto>> ListVisitorsAsync(Guid branchId, bool onSiteOnly, CancellationToken ct = default);
    Task<VisitorDto> SignInVisitorAsync(Guid branchId, CreateVisitorRequest request, CancellationToken ct = default);
    Task<VisitorDto> SignOutVisitorAsync(Guid branchId, Guid visitorId, CancellationToken ct = default);
    Task<PurgeResultDto> PurgeVisitorsAsync(Guid branchId, CancellationToken ct = default);

    Task<IReadOnlyList<ComplaintDto>> ListComplaintsAsync(Guid branchId, bool openOnly, CancellationToken ct = default);
    Task<ComplaintDto> LogComplaintAsync(Guid branchId, CreateComplaintRequest request, CancellationToken ct = default);
    Task<ComplaintDto> UpdateComplaintAsync(Guid branchId, Guid complaintId, CaseUpdateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<EmergencyDto>> ListEmergenciesAsync(Guid branchId, CancellationToken ct = default);
    Task<EmergencyDto> DeclareEmergencyAsync(Guid branchId, DeclareEmergencyRequest request, CancellationToken ct = default);
    Task<EmergencyDto> LogEmergencyActionAsync(Guid branchId, Guid emergencyId, EmergencyActionRequest request, CancellationToken ct = default);
    Task<EmergencyDto> EndEmergencyAsync(Guid branchId, Guid emergencyId, EndEmergencyRequest request, CancellationToken ct = default);
}

internal sealed class SecurityService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    INumberSequence numbers,
    IOptions<SecurityOptions> options) : ISecurityService
{
    // ---------- incidents ----------

    public async Task<IReadOnlyList<IncidentDto>> ListIncidentsAsync(Guid branchId, bool openOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.SecurityIncidents.AsNoTracking().Include(i => i.Actions).Where(i => i.BranchId == branchId);
        if (openOnly) q = q.Where(i => i.Status != CaseStatus.Closed);
        return (await q.OrderByDescending(i => i.ReportedAtUtc).Take(300).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<IncidentDto> GetIncidentAsync(Guid branchId, Guid incidentId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return ToDto(await db.SecurityIncidents.AsNoTracking().Include(i => i.Actions).GetOwnedAsync(branchId, incidentId, "Incident", ct));
    }

    public async Task<IncidentDto> ReportIncidentAsync(Guid branchId, CreateIncidentRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var type = EnumParser.Parse<IncidentType>(request.Type, nameof(request.Type));
        var severity = EnumParser.Parse<IncidentSeverity>(request.Severity, nameof(request.Severity));
        var occurred = request.OccurredAtUtc ?? scope.UtcNow;
        if (occurred > scope.UtcNow.AddMinutes(5)) throw new ValidationAppException(nameof(request.OccurredAtUtc), "The incident cannot be in the future.");
        await EnsureRefsAsync(branchId, request.RoomId, request.GuestId, null, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.Incident, ct);
        var incident = new SecurityIncident(scope.CompanyId, branchId, number, type, severity, request.Title, request.Description,
            Clean(request.Location), request.RoomId, request.GuestId, DateTime.SpecifyKind(occurred, DateTimeKind.Utc), scope.UserId, scope.UtcNow);
        db.SecurityIncidents.Add(incident);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(incident);
    }

    public async Task<IncidentDto> UpdateIncidentAsync(Guid branchId, Guid incidentId, CaseUpdateRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var incident = await db.SecurityIncidents.Include(i => i.Actions).GetOwnedAsync(branchId, incidentId, "Incident", ct);
        BranchOperations.EnsureStamp(incident, request.ConcurrencyStamp, "incident");
        switch (request.Action.Trim().ToLowerInvariant())
        {
            case "assign": incident.Assign(request.OwnerUserId ?? "", scope.UserId, scope.UtcNow); break;
            case "note": incident.AddNote(request.Note ?? "", scope.UserId, scope.UtcNow); break;
            case "resolve": incident.Resolve(request.Note ?? "", scope.UserId, scope.UtcNow); break;
            case "close": incident.Close(scope.UserId, scope.UtcNow); break;
            default: throw new ValidationAppException(nameof(request.Action), "Action must be assign, note, resolve or close.");
        }
        db.Touch(incident);
        await SaveAsync(ct);
        return ToDto(incident);
    }

    // ---------- lost and found ----------

    public async Task<IReadOnlyList<LostItemDto>> ListLostItemsAsync(Guid branchId, bool inCustodyOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.LostFoundItems.AsNoTracking().Include(i => i.Events).Where(i => i.BranchId == branchId);
        if (inCustodyOnly) q = q.Where(i => i.Status == LostFoundStatus.InCustody);
        return (await q.OrderByDescending(i => i.FoundAtUtc).Take(500).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<LostItemDto> RecordLostItemAsync(Guid branchId, CreateLostItemRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        await EnsureRefsAsync(branchId, request.RoomId, request.GuestId, null, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.LostFound, ct);
        var item = new LostFoundItem(scope.CompanyId, branchId, number, request.Description, Clean(request.Category), request.FoundLocation,
            request.RoomId, request.GuestId, request.StorageLocation, request.IsValuable, scope.UserId, scope.UtcNow);
        db.LostFoundItems.Add(item);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(item);
    }

    public async Task<LostItemDto> CustodyAsync(Guid branchId, Guid itemId, CustodyRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var item = await db.LostFoundItems.Include(i => i.Events).GetOwnedAsync(branchId, itemId, "Lost property item", ct);
        BranchOperations.EnsureStamp(item, request.ConcurrencyStamp, "item");
        var action = request.Action.Trim().ToLowerInvariant();
        if (action is "dispose" or "donate" or "return" && !Security.RolePermissions.HasPermission(user.Roles, Security.Permissions.SecurityManage))
            throw new ForbiddenException(message: "Releasing found property needs security manager permission.");
        switch (action)
        {
            case "move": item.Move(request.StorageLocation ?? "", scope.UserId, scope.UtcNow); break;
            case "return": item.Return(request.ReceivedBy ?? "", request.IdentityCheck ?? "", scope.UserId, scope.UtcNow); break;
            case "dispose" or "donate":
                var heldDays = (int)(scope.UtcNow - item.FoundAtUtc).TotalDays;
                item.Discard(action == "donate", request.Reason ?? "", heldDays, options.Value.LostFoundHoldDays, scope.UserId, scope.UtcNow);
                break;
            case "note": item.AddNote(request.Reason ?? "", scope.UserId, scope.UtcNow); break;
            default: throw new ValidationAppException(nameof(request.Action), "Action must be move, return, dispose, donate or note.");
        }
        db.Touch(item);
        await SaveAsync(ct);
        return ToDto(item);
    }

    // ---------- visitors ----------

    public async Task<IReadOnlyList<VisitorDto>> ListVisitorsAsync(Guid branchId, bool onSiteOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.VisitorLogs.AsNoTracking().Where(v => v.BranchId == branchId);
        if (onSiteOnly) q = q.Where(v => v.LeftAtUtc == null);
        return (await q.OrderByDescending(v => v.ArrivedAtUtc).Take(500).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<VisitorDto> SignInVisitorAsync(Guid branchId, CreateVisitorRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.VisitorName), request.VisitorName).Require(nameof(request.Purpose), request.Purpose, 300)
            .When(request.IdTypeChecked is { Length: > 50 }, nameof(request.IdTypeChecked), "Record only the type of ID checked, not its number.")
            .ThrowIfInvalid();
        await EnsureRefsAsync(branchId, request.RoomId, request.HostGuestId, null, ct);
        var visitor = new VisitorLog
        {
            CompanyId = scope.CompanyId, BranchId = branchId, VisitorName = request.VisitorName.Trim(), Phone = Clean(request.Phone),
            IdTypeChecked = Clean(request.IdTypeChecked), HostGuestId = request.HostGuestId, RoomId = request.RoomId, Purpose = request.Purpose.Trim(),
            ArrivedAtUtc = scope.UtcNow, RecordedByUserId = scope.UserId,
        };
        db.VisitorLogs.Add(visitor);
        await db.SaveChangesAsync(ct);
        return ToDto(visitor);
    }

    public async Task<VisitorDto> SignOutVisitorAsync(Guid branchId, Guid visitorId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var visitor = await db.VisitorLogs.GetOwnedAsync(branchId, visitorId, "Visitor", ct);
        if (visitor.LeftAtUtc is not null) throw new ConflictException("VISITOR_LEFT", "The visitor has already signed out.");
        visitor.LeftAtUtc = scope.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(visitor);
    }

    public async Task<PurgeResultDto> PurgeVisitorsAsync(Guid branchId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var cutoff = scope.UtcNow.AddDays(-options.Value.VisitorRetentionDays);
        var removed = await db.VisitorLogs.Where(v => v.BranchId == branchId && v.ArrivedAtUtc < cutoff && v.LeftAtUtc != null).ExecuteDeleteAsync(ct);
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAtUtc = scope.UtcNow, CompanyId = scope.CompanyId, BranchId = branchId, UserId = scope.UserId, Action = "Purged",
            EntityType = nameof(VisitorLog), EntityId = branchId.ToString(), Details = $"{removed} visitor records older than {cutoff:yyyy-MM-dd}", TraceId = user.TraceId,
        });
        await db.SaveChangesAsync(ct);
        return new PurgeResultDto(removed, DateOnly.FromDateTime(cutoff));
    }

    // ---------- complaints ----------

    public async Task<IReadOnlyList<ComplaintDto>> ListComplaintsAsync(Guid branchId, bool openOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.GuestComplaints.AsNoTracking().Where(c => c.BranchId == branchId);
        if (openOnly) q = q.Where(c => c.Status == CaseStatus.Open || c.Status == CaseStatus.InProgress);
        return (await q.OrderByDescending(c => c.ReportedAtUtc).Take(500).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<ComplaintDto> LogComplaintAsync(Guid branchId, CreateComplaintRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var severity = EnumParser.Parse<IncidentSeverity>(request.Severity, nameof(request.Severity));
        await EnsureRefsAsync(branchId, null, request.GuestId, request.StayId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.Complaint, ct);
        var complaint = new GuestComplaint(scope.CompanyId, branchId, number, request.GuestId, request.StayId, request.Category, request.Description,
            severity, scope.UserId, scope.UtcNow, scope.BusinessDate);
        db.GuestComplaints.Add(complaint);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(complaint);
    }

    public async Task<ComplaintDto> UpdateComplaintAsync(Guid branchId, Guid complaintId, CaseUpdateRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var complaint = await db.GuestComplaints.GetOwnedAsync(branchId, complaintId, "Complaint", ct);
        BranchOperations.EnsureStamp(complaint, request.ConcurrencyStamp, "complaint");
        switch (request.Action.Trim().ToLowerInvariant())
        {
            case "assign": complaint.Assign(request.OwnerUserId ?? ""); break;
            case "resolve": complaint.Resolve(request.Note ?? "", scope.UtcNow, scope.BusinessDate); break;
            case "close": complaint.Close(); break;
            default: throw new ValidationAppException(nameof(request.Action), "Action must be assign, resolve or close.");
        }
        await SaveAsync(ct);
        return ToDto(complaint);
    }

    // ---------- emergencies ----------

    public async Task<IReadOnlyList<EmergencyDto>> ListEmergenciesAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return (await db.EmergencyEvents.AsNoTracking().Include(e => e.Actions).Where(e => e.BranchId == branchId)
            .OrderByDescending(e => e.StartedAtUtc).Take(100).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<EmergencyDto> DeclareEmergencyAsync(Guid branchId, DeclareEmergencyRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var type = EnumParser.Parse<EmergencyType>(request.Type, nameof(request.Type));
        var emergency = new EmergencyEvent(scope.CompanyId, branchId, type, request.Description, scope.UserId, scope.UtcNow);
        db.EmergencyEvents.Add(emergency);
        await db.SaveChangesAsync(ct);
        return ToDto(emergency);
    }

    public async Task<EmergencyDto> LogEmergencyActionAsync(Guid branchId, Guid emergencyId, EmergencyActionRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var emergency = await db.EmergencyEvents.Include(e => e.Actions).GetOwnedAsync(branchId, emergencyId, "Emergency", ct);
        emergency.Log(request.Action, scope.UserId, scope.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToDto(emergency);
    }

    public async Task<EmergencyDto> EndEmergencyAsync(Guid branchId, Guid emergencyId, EndEmergencyRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var emergency = await db.EmergencyEvents.Include(e => e.Actions).GetOwnedAsync(branchId, emergencyId, "Emergency", ct);
        emergency.End(request.Summary, scope.UserId, scope.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToDto(emergency);
    }

    // ---------- helpers ----------

    private async Task EnsureRefsAsync(Guid branchId, Guid? roomId, Guid? guestId, Guid? stayId, CancellationToken ct)
    {
        if (roomId is { } r && !await db.Rooms.AnyAsync(x => x.Id == r && x.BranchId == branchId, ct)) throw new NotFoundException("Room", r);
        if (guestId is { } g && !await db.Guests.AnyAsync(x => x.Id == g && x.BranchId == branchId, ct)) throw new NotFoundException("Guest", g);
        if (stayId is { } s && !await db.Stays.AnyAsync(x => x.Id == s && x.BranchId == branchId, ct)) throw new NotFoundException("Stay", s);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "The record was changed by someone else. Reload and try again.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IncidentDto ToDto(SecurityIncident i) => new(i.Id, i.IncidentNumber, i.Type.ToString(), i.Severity.ToString(), i.Title, i.Description,
        i.Location, i.RoomId, i.GuestId, i.OccurredAtUtc, i.ReportedByUserId, i.Status.ToString(), i.OwnerUserId, i.Resolution, i.ClosedAtUtc,
        i.Actions.OrderBy(a => a.AtUtc).Select(a => new CaseActionDto(a.Action, a.Note, a.UserId, a.AtUtc)).ToList(), i.ConcurrencyStamp);

    private static LostItemDto ToDto(LostFoundItem i) => new(i.Id, i.ItemNumber, i.Description, i.Category, i.FoundLocation, i.RoomId, i.GuestId,
        i.StorageLocation, i.IsValuable, i.FoundByUserId, i.FoundAtUtc, i.Status.ToString(),
        i.Events.OrderBy(e => e.AtUtc).Select(e => new CustodyEventDto(e.Type.ToString(), e.Note, e.Counterparty, e.UserId, e.AtUtc)).ToList(),
        i.ConcurrencyStamp);

    private static VisitorDto ToDto(VisitorLog v) => new(v.Id, v.VisitorName, v.Phone, v.IdTypeChecked, v.HostGuestId, v.RoomId, v.Purpose,
        v.ArrivedAtUtc, v.LeftAtUtc, v.RecordedByUserId);

    private static ComplaintDto ToDto(GuestComplaint c) => new(c.Id, c.ComplaintNumber, c.GuestId, c.StayId, c.Category, c.Description,
        c.Severity.ToString(), c.Status.ToString(), c.ReportedByUserId, c.ReportedAtUtc, c.OwnerUserId, c.Resolution, c.ResolvedAtUtc, c.ConcurrencyStamp);

    private static EmergencyDto ToDto(EmergencyEvent e) => new(e.Id, e.Type.ToString(), e.Description, e.Status.ToString(), e.StartedAtUtc,
        e.StartedByUserId, e.EndedAtUtc, e.Summary, e.Actions.OrderBy(a => a.AtUtc).Select(a => new CaseActionDto(a.Action, null, a.UserId, a.AtUtc)).ToList());
}
