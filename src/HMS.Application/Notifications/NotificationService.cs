using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Application.Security;
using HMS.Contracts.Notifications;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Notifications;

public interface INotificationService
{
    Task<IReadOnlyList<OutboxMessageDto>> ListOutboxAsync(Guid branchId, string? status, string? eventType, CancellationToken ct = default);
    Task<OutboxMessageDto> RetryAsync(Guid branchId, Guid outboxId, CancellationToken ct = default);
    Task<OutboxMessageDto> CancelAsync(Guid branchId, Guid outboxId, CancellationToken ct = default);
    Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(Guid branchId, CancellationToken ct = default);
    Task<TemplateDto> SaveTemplateAsync(Guid branchId, SaveTemplateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<InboxItemDto>> InboxAsync(Guid branchId, bool unreadOnly, CancellationToken ct = default);
    Task MarkReadAsync(Guid branchId, Guid outboxId, CancellationToken ct = default);
    Task<IReadOnlyList<PreferenceDto>> MyPreferencesAsync(Guid branchId, CancellationToken ct = default);
    Task<PreferenceDto> SetMyPreferenceAsync(Guid branchId, SetPreferenceRequest request, CancellationToken ct = default);
}

internal sealed class NotificationService(IApplicationDbContext db, IBranchAccessService access, ICurrentUser user, IClock clock) : INotificationService
{
    public async Task<IReadOnlyList<OutboxMessageDto>> ListOutboxAsync(Guid branchId, string? status, string? eventType, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.NotificationOutbox.AsNoTracking().Where(o => o.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status)) { var s = EnumParser.Parse<OutboxStatus>(status, nameof(status)); q = q.Where(o => o.Status == s); }
        if (!string.IsNullOrWhiteSpace(eventType)) q = q.Where(o => o.EventType == eventType);
        var items = await q.OrderByDescending(o => o.CreatedAtUtc).Take(300).ToListAsync(ct);
        var ids = items.Select(i => i.Id).ToList();
        var deliveries = (await db.NotificationDeliveries.AsNoTracking().Where(d => ids.Contains(d.OutboxId)).ToListAsync(ct)).ToLookup(d => d.OutboxId);
        return items.Select(i => ToDto(i, deliveries[i.Id])).ToList();
    }

    public Task<OutboxMessageDto> RetryAsync(Guid branchId, Guid outboxId, CancellationToken ct = default) =>
        MutateAsync(branchId, outboxId, o => o.Retry(clock.UtcNow), ct);

    public Task<OutboxMessageDto> CancelAsync(Guid branchId, Guid outboxId, CancellationToken ct = default) =>
        MutateAsync(branchId, outboxId, o => o.Cancel(), ct);

    private async Task<OutboxMessageDto> MutateAsync(Guid branchId, Guid outboxId, Action<NotificationOutbox> change, CancellationToken ct)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var message = await db.NotificationOutbox.SingleOrDefaultAsync(o => o.Id == outboxId && o.BranchId == branchId, ct)
            ?? throw new NotFoundException("Notification", outboxId);
        change(message);
        await db.SaveChangesAsync(ct);
        var deliveries = await db.NotificationDeliveries.AsNoTracking().Where(d => d.OutboxId == outboxId).ToListAsync(ct);
        return ToDto(message, deliveries);
    }

    public async Task<IReadOnlyList<TemplateDto>> ListTemplatesAsync(Guid branchId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var templates = await db.NotificationTemplates.AsNoTracking()
            .Where(t => t.CompanyId == scope.CompanyId && (t.BranchId == branchId || t.BranchId == null))
            .OrderBy(t => t.EventType).ThenBy(t => t.Channel).ThenByDescending(t => t.Version).ToListAsync(ct);
        return templates.Select(ToDto).ToList();
    }

    public async Task<TemplateDto> SaveTemplateAsync(Guid branchId, SaveTemplateRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var v = new Validation().Require(nameof(request.Body), request.Body, 4000)
            .When(!NotificationEvents.All.Contains(request.EventType), nameof(request.EventType), "Unknown event type.")
            .When(request.Subject is { Length: > 200 }, nameof(request.Subject), "The subject is limited to 200 characters.")
            .When(string.IsNullOrWhiteSpace(request.Language) || request.Language.Length > 10, nameof(request.Language), "Give a language code such as en.");
        v.ThrowIfInvalid();
        var channel = EnumParser.Parse<NotificationChannel>(request.Channel, nameof(request.Channel));
        Guid? templateBranch = request.BranchSpecific ? branchId : null;
        var language = request.Language.Trim().ToLowerInvariant();

        // Templates are versioned, never edited in place, so the wording used for past messages stays known.
        var latest = await db.NotificationTemplates.Where(t => t.CompanyId == scope.CompanyId && t.BranchId == templateBranch
                && t.EventType == request.EventType && t.Channel == channel && t.Language == language)
            .OrderByDescending(t => t.Version).FirstOrDefaultAsync(ct);
        var template = new NotificationTemplate
        {
            CompanyId = scope.CompanyId, BranchId = templateBranch, EventType = request.EventType, Channel = channel, Language = language,
            Version = (latest?.Version ?? 0) + 1, Subject = request.Subject?.Trim(), Body = request.Body, IsActive = request.IsActive,
        };
        db.NotificationTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return ToDto(template);
    }

    public async Task<IReadOnlyList<InboxItemDto>> InboxAsync(Guid branchId, bool unreadOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var userId = user.UserId ?? throw new UnauthenticatedException();
        var recipients = RolePermissions.ForRoles(user.Roles).Select(p => "perm:" + p).Append("user:" + userId).ToList();
        var items = await db.NotificationOutbox.AsNoTracking()
            .Where(o => o.BranchId == branchId && o.Channel == NotificationChannel.InApp && o.Status != OutboxStatus.Cancelled
                && recipients.Contains(o.Recipient))
            .OrderByDescending(o => o.CreatedAtUtc).Take(200).ToListAsync(ct);
        var ids = items.Select(i => i.Id).ToList();
        var read = (await db.NotificationReads.AsNoTracking().Where(r => r.UserId == userId && ids.Contains(r.OutboxId)).Select(r => r.OutboxId).ToListAsync(ct)).ToHashSet();
        return items.Where(i => !unreadOnly || !read.Contains(i.Id))
            .Select(i => new InboxItemDto(i.Id, i.EventType, i.Subject, i.Body, i.CreatedAtUtc, read.Contains(i.Id))).ToList();
    }

    public async Task MarkReadAsync(Guid branchId, Guid outboxId, CancellationToken ct = default)
    {
        var visible = await InboxAsync(branchId, false, ct);
        if (visible.All(i => i.Id != outboxId)) throw new NotFoundException("Notification", outboxId);
        var userId = user.UserId!;
        if (await db.NotificationReads.AnyAsync(r => r.OutboxId == outboxId && r.UserId == userId, ct)) return;
        db.NotificationReads.Add(new NotificationRead { OutboxId = outboxId, UserId = userId, ReadAtUtc = clock.UtcNow });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Marked read by a parallel request: same outcome.
        }
    }

    public async Task<IReadOnlyList<PreferenceDto>> MyPreferencesAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var userId = user.UserId ?? throw new UnauthenticatedException();
        var saved = await db.NotificationPreferences.AsNoTracking().Where(p => p.BranchId == branchId && p.UserId == userId).ToListAsync(ct);
        return NotificationEvents.All.Order(StringComparer.Ordinal)
            .Select(e => new PreferenceDto(e, nameof(NotificationChannel.InApp),
                saved.SingleOrDefault(p => p.EventType == e && p.Channel == NotificationChannel.InApp)?.Enabled ?? true))
            .ToList();
    }

    public async Task<PreferenceDto> SetMyPreferenceAsync(Guid branchId, SetPreferenceRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (!NotificationEvents.All.Contains(request.EventType)) throw new ValidationAppException(nameof(request.EventType), "Unknown event type.");
        var channel = EnumParser.Parse<NotificationChannel>(request.Channel, nameof(request.Channel));
        if (channel != NotificationChannel.InApp) throw new ValidationAppException(nameof(request.Channel), "Staff preferences apply to in-app messages.");
        var pref = await db.NotificationPreferences.SingleOrDefaultAsync(p => p.BranchId == branchId && p.UserId == scope.UserId
            && p.EventType == request.EventType && p.Channel == channel, ct);
        if (pref is null)
            db.NotificationPreferences.Add(pref = new NotificationPreference
            {
                CompanyId = scope.CompanyId, BranchId = branchId, UserId = scope.UserId, EventType = request.EventType, Channel = channel,
            });
        pref.Enabled = request.Enabled;
        await db.SaveChangesAsync(ct);
        return new PreferenceDto(pref.EventType, pref.Channel.ToString(), pref.Enabled);
    }

    private static OutboxMessageDto ToDto(NotificationOutbox o, IEnumerable<NotificationDelivery> deliveries) => new(o.Id, o.EventType, o.SourceType,
        o.SourceId, o.Channel.ToString(), o.Recipient, o.Subject, o.Status.ToString(), o.Attempts, o.NextAttemptAtUtc, o.SentAtUtc, o.LastError,
        o.CreatedAtUtc, deliveries.OrderBy(d => d.Attempt).Select(d => new DeliveryDto(d.Attempt, d.Provider, d.ProviderMessageId,
            d.Status.ToString(), d.SubmittedAtUtc, d.Error)).ToList());

    private static TemplateDto ToDto(NotificationTemplate t) => new(t.Id, t.BranchId, t.EventType, t.Channel.ToString(), t.Language, t.Version,
        t.Subject, t.Body, t.IsActive);
}
