namespace HMS.Contracts.Maintenance;

/// <summary>Status: InService, UnderMaintenance, Retired.</summary>
public sealed record AssetDto(Guid Id, string Code, string Name, string? Category, Guid? RoomId, string? Location, string? SerialNumber,
    DateOnly? PurchaseDate, decimal? PurchaseCost, string Status, decimal MaintenanceCost, Guid ConcurrencyStamp);

public sealed record SaveAssetRequest(string Code, string Name, string? Category, Guid? RoomId, string? Location, string? SerialNumber,
    DateOnly? PurchaseDate, decimal? PurchaseCost, string Status = "InService", Guid? ConcurrencyStamp = null);

/// <summary>Priority: Low, Normal, High, Urgent.</summary>
public sealed record CreateMaintenanceRequestRequest(string Title, string? Description, string Priority, Guid? RoomId, Guid? AssetId);

/// <summary>Status: Open, Converted, Rejected.</summary>
public sealed record MaintenanceRequestDto(Guid Id, string Title, string? Description, string Priority, Guid? RoomId, Guid? AssetId,
    string Status, string ReportedByUserId, DateTime ReportedAtUtc, Guid? WorkOrderId, string? RejectionReason, Guid ConcurrencyStamp);

/// <summary>
/// Accept a request as a work order. With TakeRoomOutOfService the room is blocked from sale from today
/// until ExpectedReadyDate (exclusive) or until the work order ends, whichever comes first.
/// </summary>
public sealed record TriageRequest(bool Accept, string? Reason, string? Priority, string? AssignToUserId, bool TakeRoomOutOfService,
    DateOnly? ExpectedReadyDate, Guid ConcurrencyStamp);

public sealed record CreateWorkOrderRequest(string Title, string? Description, string Priority, Guid? RoomId, Guid? AssetId,
    DateOnly? DueDate, string? AssignToUserId, bool TakeRoomOutOfService, DateOnly? ExpectedReadyDate);

/// <summary>Status: Open, Assigned, InProgress, AwaitingInspection, Completed, Cancelled.</summary>
public sealed record WorkOrderDto(Guid Id, string WorkOrderNumber, string Title, string? Description, string Priority, Guid? RoomId,
    string? RoomNumber, Guid? AssetId, Guid? RequestId, Guid? PreventivePlanId, DateOnly? DueDate, string Status, string? AssignedToUserId,
    Guid? RoomBlockId, DateTime? StartedAtUtc, DateTime? CompletedAtUtc, decimal LabourHours, decimal LabourCost, decimal PartsCost,
    string? CompletionNotes, IReadOnlyList<PartUsageDto> Parts, Guid ConcurrencyStamp);

public sealed record PartUsageDto(Guid Id, Guid ItemId, string Sku, Guid LocationId, decimal Quantity, decimal UnitCost, decimal TotalCost);

public sealed record AssignWorkOrderRequest(string UserId, Guid ConcurrencyStamp);

public sealed record WorkOrderActionRequest(Guid ConcurrencyStamp);

/// <summary>Issues parts from stock. Send an Idempotency-Key header.</summary>
public sealed record UsePartRequest(Guid ItemId, Guid LocationId, decimal Quantity);

public sealed record FinishWorkRequest(decimal LabourHours, decimal LabourCost, string? Notes, Guid ConcurrencyStamp);

public sealed record InspectWorkRequest(bool Passed, Guid ConcurrencyStamp);

public sealed record PreventivePlanDto(Guid Id, string Title, Guid? AssetId, Guid? RoomId, int IntervalDays, DateOnly NextDueDate,
    string Priority, bool IsActive, Guid ConcurrencyStamp);

public sealed record SavePreventivePlanRequest(string Title, Guid? AssetId, Guid? RoomId, int IntervalDays, DateOnly NextDueDate,
    string Priority, bool IsActive = true, Guid? ConcurrencyStamp = null);

/// <summary>Creates any work orders due up to ThroughDate. Safe to run repeatedly.</summary>
public sealed record GeneratePreventiveRequest(DateOnly ThroughDate);

public sealed record GeneratePreventiveResultDto(int Created, IReadOnlyList<Guid> WorkOrderIds);

public sealed record AssetHistoryDto(AssetDto Asset, IReadOnlyList<WorkOrderDto> WorkOrders, decimal TotalCost, decimal DowntimeHours);
