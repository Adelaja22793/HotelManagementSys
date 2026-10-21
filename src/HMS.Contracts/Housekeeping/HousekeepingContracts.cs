namespace HMS.Contracts.Housekeeping;

/// <summary>Status: Pending, InProgress, AwaitingInspection, Completed, Cancelled. Type: Checkout, Stayover, Touchup.</summary>
public sealed record HousekeepingTaskDto(Guid Id, Guid RoomId, string RoomNumber, string RoomStatus, string Type, string Status,
    DateOnly BusinessDate, string? AssignedToUserId, DateTime? StartedAtUtc, DateTime? CompletedAtUtc,
    string? RejectionReason, string? Notes, Guid ConcurrencyStamp);

public sealed record CreateHousekeepingTaskRequest(Guid RoomId, string Type, string? Notes);

public sealed record AssignHousekeepingTaskRequest(string UserId, Guid ConcurrencyStamp);

public sealed record HousekeepingActionRequest(Guid ConcurrencyStamp, string? Notes = null);

public sealed record InspectionRequest(bool Passed, string? Reason, Guid ConcurrencyStamp);
