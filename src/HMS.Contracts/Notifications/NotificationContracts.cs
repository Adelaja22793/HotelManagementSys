namespace HMS.Contracts.Notifications;

/// <summary>Channel: Email, Sms, InApp. Status: Pending, Processing, Sent, Failed, Cancelled.</summary>
public sealed record OutboxMessageDto(Guid Id, string EventType, string SourceType, string SourceId, string Channel, string Recipient,
    string? Subject, string Status, int Attempts, DateTime NextAttemptAtUtc, DateTime? SentAtUtc, string? LastError, DateTime CreatedAtUtc,
    IReadOnlyList<DeliveryDto> Deliveries);

public sealed record DeliveryDto(int Attempt, string Provider, string? ProviderMessageId, string Status, DateTime SubmittedAtUtc, string? Error);

/// <summary>Leave BranchSpecific false to edit the company-wide default. Saving creates a new version.</summary>
public sealed record SaveTemplateRequest(string EventType, string Channel, string Language, string? Subject, string Body, bool BranchSpecific, bool IsActive = true);

public sealed record TemplateDto(Guid Id, Guid? BranchId, string EventType, string Channel, string Language, int Version, string? Subject,
    string Body, bool IsActive);

public sealed record InboxItemDto(Guid Id, string EventType, string? Subject, string Body, DateTime CreatedAtUtc, bool IsRead);

public sealed record PreferenceDto(string EventType, string Channel, bool Enabled);

public sealed record SetPreferenceRequest(string EventType, string Channel, bool Enabled);

public sealed record DispatchResultDto(int Claimed, int Sent, int Failed, int Retrying);
