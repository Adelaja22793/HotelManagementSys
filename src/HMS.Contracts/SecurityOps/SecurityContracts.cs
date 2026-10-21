namespace HMS.Contracts.SecurityOps;

/// <summary>Type: Theft, Damage, Injury, Disturbance, Fire, Medical, Trespass, Other. Severity: Low, Medium, High, Critical.</summary>
public sealed record CreateIncidentRequest(string Type, string Severity, string Title, string Description, string? Location, Guid? RoomId,
    Guid? GuestId, DateTime? OccurredAtUtc);

public sealed record CaseActionDto(string Action, string? Note, string UserId, DateTime AtUtc);

/// <summary>Status: Open, InProgress, Resolved, Closed.</summary>
public sealed record IncidentDto(Guid Id, string IncidentNumber, string Type, string Severity, string Title, string Description, string? Location,
    Guid? RoomId, Guid? GuestId, DateTime OccurredAtUtc, string ReportedByUserId, string Status, string? OwnerUserId, string? Resolution,
    DateTime? ClosedAtUtc, IReadOnlyList<CaseActionDto> Actions, Guid ConcurrencyStamp);

/// <summary>Action: assign (OwnerUserId), note (Note), resolve (Note = resolution), close.</summary>
public sealed record CaseUpdateRequest(string Action, string? OwnerUserId, string? Note, Guid ConcurrencyStamp);

public sealed record CreateLostItemRequest(string Description, string? Category, string FoundLocation, Guid? RoomId, Guid? GuestId,
    string StorageLocation, bool IsValuable);

/// <summary>Status: InCustody, Returned, Disposed, Donated.</summary>
public sealed record LostItemDto(Guid Id, string ItemNumber, string Description, string? Category, string FoundLocation, Guid? RoomId,
    Guid? GuestId, string StorageLocation, bool IsValuable, string FoundByUserId, DateTime FoundAtUtc, string Status,
    IReadOnlyList<CustodyEventDto> Events, Guid ConcurrencyStamp);

public sealed record CustodyEventDto(string Type, string? Note, string? Counterparty, string UserId, DateTime AtUtc);

/// <summary>Action: move (StorageLocation), return (ReceivedBy, IdentityCheck), dispose or donate (Reason), note (Reason).</summary>
public sealed record CustodyRequest(string Action, string? StorageLocation, string? ReceivedBy, string? IdentityCheck, string? Reason,
    Guid ConcurrencyStamp);

public sealed record CreateVisitorRequest(string VisitorName, string? Phone, string? IdTypeChecked, Guid? HostGuestId, Guid? RoomId, string Purpose);

public sealed record VisitorDto(Guid Id, string VisitorName, string? Phone, string? IdTypeChecked, Guid? HostGuestId, Guid? RoomId,
    string Purpose, DateTime ArrivedAtUtc, DateTime? LeftAtUtc, string RecordedByUserId);

public sealed record PurgeResultDto(int Removed, DateOnly OlderThan);

public sealed record CreateComplaintRequest(Guid? GuestId, Guid? StayId, string Category, string Description, string Severity);

public sealed record ComplaintDto(Guid Id, string ComplaintNumber, Guid? GuestId, Guid? StayId, string Category, string Description,
    string Severity, string Status, string ReportedByUserId, DateTime ReportedAtUtc, string? OwnerUserId, string? Resolution,
    DateTime? ResolvedAtUtc, Guid ConcurrencyStamp);

/// <summary>Type: Fire, Medical, Security, Evacuation, PowerOutage, Flood, Other.</summary>
public sealed record DeclareEmergencyRequest(string Type, string Description);

public sealed record EmergencyDto(Guid Id, string Type, string Description, string Status, DateTime StartedAtUtc, string StartedByUserId,
    DateTime? EndedAtUtc, string? Summary, IReadOnlyList<CaseActionDto> Actions);

public sealed record EmergencyActionRequest(string Action);

public sealed record EndEmergencyRequest(string Summary);
