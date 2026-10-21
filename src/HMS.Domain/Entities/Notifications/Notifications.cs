using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

public static class NotificationEvents
{
    public const string ReservationConfirmed = "ReservationConfirmed";
    public const string GuestCheckedOut = "GuestCheckedOut";
    public const string PaymentConfirmed = "PaymentConfirmed";
    public const string RefundStatusChanged = "RefundStatusChanged";
    public const string LowStockDetected = "LowStockDetected";
    public const string MaintenanceAssigned = "MaintenanceAssigned";
    public const string ShiftVarianceDetected = "ShiftVarianceDetected";
    public const string DailyReportReady = "DailyReportReady";
    public const string OnlinePaymentNeedsReview = "OnlinePaymentNeedsReview";
    public const string CancellationRequested = "CancellationRequested";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ReservationConfirmed, GuestCheckedOut, PaymentConfirmed, RefundStatusChanged,
        LowStockDetected, MaintenanceAssigned, ShiftVarianceDetected, DailyReportReady, OnlinePaymentNeedsReview, CancellationRequested,
    };
}

/// <summary>
/// Message text for an event and channel. A branch template overrides the company default; the highest active
/// version wins. Placeholders look like <c>{{GuestName}}</c>.
/// </summary>
public class NotificationTemplate : AuditableEntity, IConcurrencyTracked
{
    public Guid CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; }
    public string Language { get; set; } = "en";
    public int Version { get; set; } = 1;
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>
/// A message waiting to be sent, written in the same transaction as the business event. Workers claim rows with a
/// lease so two workers never send the same message at once.
/// Recipient: an email address or phone number, or for in-app messages <c>user:{id}</c> or <c>perm:{permission}</c>.
/// </summary>
public class NotificationOutbox : Entity, IBranchOwned
{
    public const int MaxAttempts = 5;

    private NotificationOutbox() { }

    public NotificationOutbox(Guid companyId, Guid branchId, string eventType, string sourceType, string sourceId, NotificationChannel channel,
        string recipient, string? subject, string body, string idempotencyKey, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        EventType = eventType;
        SourceType = sourceType;
        SourceId = sourceId;
        Channel = channel;
        Recipient = Guard.Required(recipient, "RECIPIENT_REQUIRED", "A recipient is required", 320);
        Subject = subject;
        Body = body;
        IdempotencyKey = idempotencyKey;
        NextAttemptAtUtc = utcNow;
        CreatedAtUtc = utcNow;
    }

    // Not an auditable entity: delivery bookkeeping would flood the audit trail. Deliveries are the history.
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string SourceType { get; private set; } = string.Empty;
    public string SourceId { get; private set; } = string.Empty;
    public NotificationChannel Channel { get; private set; }
    public string Recipient { get; private set; } = string.Empty;
    public string? Subject { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public OutboxStatus Status { get; private set; } = OutboxStatus.Pending;
    public int Attempts { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public string? LastError { get; private set; }

    public void MarkSent(DateTime utcNow)
    {
        Status = OutboxStatus.Sent;
        SentAtUtc = utcNow;
        LeaseId = null;
        LeaseExpiresAtUtc = null;
        LastError = null;
    }

    /// <summary>Schedules a retry with exponential back-off, or gives up after <see cref="MaxAttempts"/> or a permanent error.</summary>
    public void MarkFailed(string error, bool permanent, DateTime utcNow)
    {
        LastError = error.Length > 500 ? error[..500] : error;
        LeaseId = null;
        LeaseExpiresAtUtc = null;
        if (permanent || Attempts >= MaxAttempts)
        {
            Status = OutboxStatus.Failed;
            return;
        }
        Status = OutboxStatus.Pending;
        NextAttemptAtUtc = utcNow.AddMinutes(Math.Pow(2, Attempts));
    }

    public void Retry(DateTime utcNow)
    {
        if (Status != OutboxStatus.Failed) throw new DomainConflictException("OUTBOX_STATE", "Only failed messages can be retried.");
        Status = OutboxStatus.Pending;
        Attempts = 0;
        NextAttemptAtUtc = utcNow;
    }

    public void Cancel()
    {
        if (Status is OutboxStatus.Sent or OutboxStatus.Processing)
            throw new DomainConflictException("OUTBOX_STATE", "A message already sent or being sent cannot be cancelled.");
        Status = OutboxStatus.Cancelled;
    }
}

/// <summary>One attempt to hand a message to a provider, kept separately from the outbox command.</summary>
public class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OutboxId { get; set; }
    public int Attempt { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public DeliveryStatus Status { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public string? Error { get; set; }
}

/// <summary>Opt-out (or opt-in) for an event and channel, for a staff user or a guest.</summary>
public class NotificationPreference : BranchEntity
{
    public string? UserId { get; set; }
    public Guid? GuestId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; }
    public bool Enabled { get; set; }
}

/// <summary>Marks an in-app message as read by one user.</summary>
public class NotificationRead
{
    public Guid OutboxId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime ReadAtUtc { get; set; }
}
