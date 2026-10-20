using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Contracts.Common;
using HMS.Contracts.Guests;
using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Guests;

public interface IGuestService
{
    Task<PagedResult<GuestSummaryDto>> SearchAsync(Guid branchId, PageQuery query, CancellationToken ct = default);
    Task<GuestDto> GetAsync(Guid branchId, Guid guestId, CancellationToken ct = default);
    Task<GuestDto> CreateAsync(Guid branchId, SaveGuestRequest request, CancellationToken ct = default);
    Task<GuestDto> UpdateAsync(Guid branchId, Guid guestId, SaveGuestRequest request, CancellationToken ct = default);

    /// <summary>Reuses an existing profile matched by email or phone, otherwise adds a new one (not saved).</summary>
    Task<Guest> FindOrAddAsync(BranchScope scope, SaveGuestRequest request, CancellationToken ct);
}

internal sealed class GuestService(IApplicationDbContext db, IBranchAccessService access, ICurrentUser user, IClock clock) : IGuestService
{
    public async Task<PagedResult<GuestSummaryDto>> SearchAsync(Guid branchId, PageQuery query, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var guests = db.Guests.AsNoTracking().Where(g => g.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            guests = guests.Where(g => g.FirstName.ToLower().Contains(s) || g.LastName.ToLower().Contains(s)
                                       || (g.Email != null && g.Email.Contains(s)) || (g.Phone != null && g.Phone.Contains(s)));
        }
        var total = await guests.CountAsync(ct);
        var items = await guests.OrderBy(g => g.LastName).ThenBy(g => g.FirstName).Skip(query.Skip).Take(query.PageSize)
            .Select(g => new GuestSummaryDto(g.Id, g.FirstName + " " + g.LastName, g.Email, g.Phone))
            .ToListAsync(ct);
        return new PagedResult<GuestSummaryDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<GuestDto> GetAsync(Guid branchId, Guid guestId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return ToDto(await db.Guests.AsNoTracking().GetOwnedAsync(branchId, guestId, "Guest", ct));
    }

    public async Task<GuestDto> CreateAsync(Guid branchId, SaveGuestRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var guest = new Guest(scope.CompanyId, branchId, request.FirstName, request.LastName);
        Apply(guest, request);
        db.Guests.Add(guest);
        await db.SaveChangesAsync(ct);
        return ToDto(guest);
    }

    public async Task<GuestDto> UpdateAsync(Guid branchId, Guid guestId, SaveGuestRequest request, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var guest = await db.Guests.GetOwnedAsync(branchId, guestId, "Guest", ct);
        if (request.ConcurrencyStamp is { } stamp) BranchOperations.EnsureStamp(guest, stamp, "guest");
        Apply(guest, request);
        await db.SaveChangesAsync(ct);
        return ToDto(guest);
    }

    public async Task<Guest> FindOrAddAsync(BranchScope scope, SaveGuestRequest request, CancellationToken ct)
    {
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        var phone = Guest.NormalizePhone(request.Phone);
        if (email is not null || phone is not null)
        {
            var existing = await db.Guests.FirstOrDefaultAsync(g => g.BranchId == scope.BranchId
                && g.LastName == request.LastName.Trim()
                && ((email != null && g.Email == email) || (phone != null && g.Phone == phone)), ct);
            if (existing is not null) return existing;
        }

        var guest = new Guest(scope.CompanyId, scope.BranchId, request.FirstName, request.LastName);
        Apply(guest, request);
        db.Guests.Add(guest);
        return guest;
    }

    private static void Apply(Guest guest, SaveGuestRequest r) =>
        guest.Update(r.FirstName, r.LastName, r.Email, r.Phone, r.Nationality, r.IdDocumentType, r.IdDocumentNumber, r.Address, r.Notes);

    internal static GuestDto ToDto(Guest g) => new(g.Id, g.FirstName, g.LastName, g.Email, g.Phone, g.Nationality,
        g.IdDocumentType, g.IdDocumentNumber, g.Address, g.Notes, g.ConcurrencyStamp);
}
