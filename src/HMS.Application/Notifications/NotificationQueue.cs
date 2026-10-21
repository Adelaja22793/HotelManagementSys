using System.Globalization;
using System.Text.RegularExpressions;
using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Notifications;

/// <summary>
/// Adds notifications to the outbox inside the caller's unit of work, so a message exists if and only if the business
/// change committed. Nothing is sent here; the dispatcher delivers later.
/// </summary>
public interface INotificationQueue
{
    /// <summary>Email if the guest has an address, otherwise SMS if they have a phone number; skipped if they opted out.</summary>
    Task EnqueueForGuestAsync(BranchScope scope, string eventType, string sourceType, string sourceId, Guest guest,
        IReadOnlyDictionary<string, string> values, CancellationToken ct, string discriminator = "");

    /// <summary>In-app message to <c>user:{id}</c> or to everyone holding a permission, <c>perm:{permission}</c>.</summary>
    Task EnqueueInAppAsync(BranchScope scope, string eventType, string sourceType, string sourceId, string recipient,
        IReadOnlyDictionary<string, string> values, CancellationToken ct, string discriminator = "");
}

internal sealed partial class NotificationQueue(IApplicationDbContext db) : INotificationQueue
{
    public async Task EnqueueForGuestAsync(BranchScope scope, string eventType, string sourceType, string sourceId, Guest guest,
        IReadOnlyDictionary<string, string> values, CancellationToken ct, string discriminator = "")
    {
        var (channel, recipient) = guest.Email is { Length: > 3 } email && email.Contains('@')
            ? (NotificationChannel.Email, email.Trim())
            : guest.Phone is { Length: > 5 } phone ? (NotificationChannel.Sms, phone.Trim()) : (NotificationChannel.InApp, "");
        if (recipient.Length == 0) return;
        if (await db.NotificationPreferences.AnyAsync(p => p.BranchId == scope.BranchId && p.GuestId == guest.Id && p.EventType == eventType
                && p.Channel == channel && !p.Enabled, ct))
            return;
        await AddAsync(scope, eventType, sourceType, sourceId, channel, recipient, values, discriminator, ct);
    }

    public async Task EnqueueInAppAsync(BranchScope scope, string eventType, string sourceType, string sourceId, string recipient,
        IReadOnlyDictionary<string, string> values, CancellationToken ct, string discriminator = "")
    {
        if (!recipient.StartsWith("user:", StringComparison.Ordinal) && !recipient.StartsWith("perm:", StringComparison.Ordinal))
            throw new ArgumentException("In-app recipients are user:{id} or perm:{permission}.", nameof(recipient));
        if (recipient.StartsWith("user:", StringComparison.Ordinal))
        {
            var userId = recipient[5..];
            if (await db.NotificationPreferences.AnyAsync(p => p.BranchId == scope.BranchId && p.UserId == userId && p.EventType == eventType
                    && p.Channel == NotificationChannel.InApp && !p.Enabled, ct))
                return;
        }
        await AddAsync(scope, eventType, sourceType, sourceId, NotificationChannel.InApp, recipient, values, discriminator, ct);
    }

    private async Task AddAsync(BranchScope scope, string eventType, string sourceType, string sourceId, NotificationChannel channel,
        string recipient, IReadOnlyDictionary<string, string> values, string discriminator, CancellationToken ct)
    {
        var key = $"{eventType}:{sourceId}:{discriminator}:{channel}:{recipient}";
        if (key.Length > 200) key = $"{eventType}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))}";
        if (db.NotificationOutbox.Local.Any(o => o.BranchId == scope.BranchId && o.IdempotencyKey == key)
            || await db.NotificationOutbox.AnyAsync(o => o.BranchId == scope.BranchId && o.IdempotencyKey == key, ct))
            return;

        var template = await db.NotificationTemplates.AsNoTracking()
            .Where(t => t.CompanyId == scope.CompanyId && t.EventType == eventType && t.Channel == channel && t.Language == "en" && t.IsActive
                && (t.BranchId == scope.BranchId || t.BranchId == null))
            .OrderByDescending(t => t.BranchId != null).ThenByDescending(t => t.Version)
            .FirstOrDefaultAsync(ct);
        var (subject, body) = template is not null
            ? (template.Subject, template.Body)
            : DefaultTemplates.For(eventType, channel);

        var all = new Dictionary<string, string>(values, StringComparer.Ordinal) { ["HotelName"] = scope.Branch.Name, ["BranchCode"] = scope.Branch.Code };
        var renderedSubject = subject is null ? null : SingleLine(Render(subject, all));
        var renderedBody = Render(body, all);
        if (channel == NotificationChannel.Sms && renderedBody.Length > 480) renderedBody = renderedBody[..480];

        db.NotificationOutbox.Add(new NotificationOutbox(scope.CompanyId, scope.BranchId, eventType, sourceType, sourceId, channel, recipient,
            renderedSubject, renderedBody, key, scope.UtcNow));
    }

    internal static string Render(string template, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : string.Empty);

    // Subjects become mail headers: no line breaks allowed.
    private static string SingleLine(string value)
    {
        var line = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length > 200 ? line[..200] : line;
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex Placeholder();

    internal static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}

/// <summary>Built-in English wording, used until a hotel saves its own template.</summary>
public static class DefaultTemplates
{
    private static readonly Dictionary<(string, NotificationChannel), (string? Subject, string Body)> Map = new()
    {
        [(NotificationEvents.ReservationConfirmed, NotificationChannel.Email)] = ("Your reservation {{ReservationNumber}} at {{HotelName}}",
            "Dear {{GuestName}},\n\nThank you for booking with {{HotelName}}. Reservation {{ReservationNumber}} is confirmed from {{ArrivalDate}} to {{DepartureDate}} ({{Nights}} nights).\nTotal: {{Currency}} {{Total}}\n\nCancellation policy: {{CancellationPolicy}}\n\nWe look forward to welcoming you.\n{{HotelName}}"),
        [(NotificationEvents.ReservationConfirmed, NotificationChannel.Sms)] = (null,
            "{{HotelName}}: reservation {{ReservationNumber}} confirmed, {{ArrivalDate}} to {{DepartureDate}}. Total {{Currency}} {{Total}}."),
        [(NotificationEvents.PaymentConfirmed, NotificationChannel.Email)] = ("Payment received - receipt {{ReceiptNumber}}",
            "Dear {{GuestName}},\n\nWe have received your payment of {{Currency}} {{Amount}} ({{Tender}}). Receipt number: {{ReceiptNumber}}.\n\n{{HotelName}}"),
        [(NotificationEvents.PaymentConfirmed, NotificationChannel.Sms)] = (null,
            "{{HotelName}}: payment of {{Currency}} {{Amount}} received. Receipt {{ReceiptNumber}}."),
        [(NotificationEvents.GuestCheckedOut, NotificationChannel.Email)] = ("Thank you for staying at {{HotelName}}",
            "Dear {{GuestName}},\n\nThank you for staying with us. Your invoice number is {{InvoiceNumber}}.\n\nWe hope to see you again soon.\n{{HotelName}}"),
        [(NotificationEvents.GuestCheckedOut, NotificationChannel.Sms)] = (null,
            "Thank you for staying at {{HotelName}}. Invoice {{InvoiceNumber}}."),
        [(NotificationEvents.RefundStatusChanged, NotificationChannel.Email)] = ("Refund {{RefundNumber}}: {{Status}}",
            "Dear {{GuestName}},\n\nYour refund {{RefundNumber}} of {{Currency}} {{Amount}} is now {{Status}}.\n\n{{HotelName}}"),
        [(NotificationEvents.RefundStatusChanged, NotificationChannel.Sms)] = (null,
            "{{HotelName}}: refund {{RefundNumber}} of {{Currency}} {{Amount}} is {{Status}}."),
        [(NotificationEvents.LowStockDetected, NotificationChannel.InApp)] = ("Low stock: {{ItemName}}",
            "{{ItemName}} ({{Sku}}) at {{Location}} is down to {{Quantity}} {{Unit}}. Reorder level: {{ReorderLevel}}."),
        [(NotificationEvents.MaintenanceAssigned, NotificationChannel.InApp)] = ("Work order {{WorkOrderNumber}} assigned to you",
            "{{Title}} ({{Priority}} priority){{RoomText}}."),
        [(NotificationEvents.ShiftVarianceDetected, NotificationChannel.InApp)] = ("Cashier variance needs approval",
            "A cashier shift for {{BusinessDate}} closed with a variance: {{Variances}}."),
        [(NotificationEvents.DailyReportReady, NotificationChannel.InApp)] = ("Night audit {{BusinessDate}} complete",
            "Occupancy {{Occupancy}}%, ADR {{Currency}} {{Adr}}, RevPAR {{Currency}} {{RevPar}}, room revenue {{Currency}} {{RoomRevenue}}, collections {{Currency}} {{Collections}}."),
    };

    public static (string? Subject, string Body) For(string eventType, NotificationChannel channel) =>
        Map.TryGetValue((eventType, channel), out var t) ? t : ("{{HotelName}} notification", "{{HotelName}}: " + eventType);
}
