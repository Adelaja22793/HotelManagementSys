using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

public class Asset : BranchEntity, IConcurrencyTracked
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public Guid? RoomId { get; set; }
    public string? Location { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.InService;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>A reported problem. Triage either rejects it or converts it into a work order.</summary>
public class MaintenanceRequest : BranchEntity, IConcurrencyTracked
{
    private MaintenanceRequest() { }

    public MaintenanceRequest(Guid companyId, Guid branchId, string title, string? description, MaintenancePriority priority,
        Guid? roomId, Guid? assetId, string reportedByUserId, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        Title = Guard.Required(title, "TITLE_REQUIRED", "A title is required", 200);
        Description = description;
        Priority = priority;
        RoomId = roomId;
        AssetId = assetId;
        ReportedByUserId = reportedByUserId;
        ReportedAtUtc = utcNow;
    }

    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public MaintenancePriority Priority { get; private set; }
    public Guid? RoomId { get; private set; }
    public Guid? AssetId { get; private set; }
    public MaintenanceRequestStatus Status { get; private set; } = MaintenanceRequestStatus.Open;
    public string ReportedByUserId { get; private set; } = string.Empty;
    public DateTime ReportedAtUtc { get; private set; }
    public Guid? WorkOrderId { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Convert(Guid workOrderId)
    {
        if (Status != MaintenanceRequestStatus.Open) throw new DomainConflictException("REQUEST_STATE", "The request has already been triaged.");
        Status = MaintenanceRequestStatus.Converted;
        WorkOrderId = workOrderId;
    }

    public void Reject(string reason)
    {
        if (Status != MaintenanceRequestStatus.Open) throw new DomainConflictException("REQUEST_STATE", "The request has already been triaged.");
        RejectionReason = Guard.Required(reason, "REASON_REQUIRED", "A reason is required", 500);
        Status = MaintenanceRequestStatus.Rejected;
    }
}

/// <summary>
/// Work to do: Open → Assigned → InProgress → AwaitingInspection → Completed (or Cancelled).
/// A work order that takes a room out of service owns a <see cref="RoomBlock"/>, released when it ends.
/// </summary>
public class WorkOrder : BranchEntity, IConcurrencyTracked
{
    private WorkOrder() { }

    public WorkOrder(Guid companyId, Guid branchId, string number, string title, string? description, MaintenancePriority priority,
        Guid? roomId, Guid? assetId, Guid? requestId, Guid? preventivePlanId, DateOnly? dueDate)
    {
        CompanyId = companyId;
        BranchId = branchId;
        WorkOrderNumber = number;
        Title = Guard.Required(title, "TITLE_REQUIRED", "A title is required", 200);
        Description = description;
        Priority = priority;
        RoomId = roomId;
        AssetId = assetId;
        RequestId = requestId;
        PreventivePlanId = preventivePlanId;
        DueDate = dueDate;
    }

    public string WorkOrderNumber { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public MaintenancePriority Priority { get; private set; }
    public Guid? RoomId { get; private set; }
    public Guid? AssetId { get; private set; }
    public Guid? RequestId { get; private set; }
    public Guid? PreventivePlanId { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public WorkOrderStatus Status { get; private set; } = WorkOrderStatus.Open;
    public string? AssignedToUserId { get; private set; }
    public Guid? RoomBlockId { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public decimal LabourHours { get; private set; }
    public decimal LabourCost { get; private set; }
    public decimal PartsCost { get; private set; }
    public string? CompletionNotes { get; private set; }
    public string? InspectedByUserId { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsOpen => Status is not (WorkOrderStatus.Completed or WorkOrderStatus.Cancelled);

    public void LinkBlock(Guid blockId) => RoomBlockId = blockId;

    public void Assign(string userId)
    {
        EnsureOpen();
        AssignedToUserId = Guard.Required(userId, "USER_REQUIRED", "Select a technician", 450);
        if (Status == WorkOrderStatus.Open) Status = WorkOrderStatus.Assigned;
    }

    public void Start(string userId, DateTime utcNow)
    {
        if (Status is not (WorkOrderStatus.Open or WorkOrderStatus.Assigned))
            throw new DomainConflictException("WORK_ORDER_STATE", "Only open or assigned work orders can be started.");
        AssignedToUserId ??= userId;
        Status = WorkOrderStatus.InProgress;
        StartedAtUtc = utcNow;
    }

    public void AddPartsCost(decimal cost)
    {
        if (Status != WorkOrderStatus.InProgress) throw new DomainConflictException("WORK_ORDER_STATE", "Parts can only be used while work is in progress.");
        PartsCost = Money.Round(PartsCost + cost);
    }

    public void FinishWork(decimal labourHours, decimal labourCost, string? notes)
    {
        if (Status != WorkOrderStatus.InProgress) throw new DomainConflictException("WORK_ORDER_STATE", "Only work in progress can be finished.");
        if (labourHours < 0 || labourCost < 0) throw new DomainException("LABOUR_INVALID", "Labour hours and cost cannot be negative.");
        LabourHours = labourHours;
        LabourCost = Money.Round(labourCost);
        CompletionNotes = notes;
        Status = WorkOrderStatus.AwaitingInspection;
    }

    public void Inspect(bool passed, string userId, DateTime utcNow)
    {
        if (Status != WorkOrderStatus.AwaitingInspection) throw new DomainConflictException("WORK_ORDER_STATE", "The work order is not awaiting inspection.");
        InspectedByUserId = userId;
        if (passed)
        {
            Status = WorkOrderStatus.Completed;
            CompletedAtUtc = utcNow;
        }
        else
        {
            Status = WorkOrderStatus.InProgress;
        }
    }

    public void Cancel()
    {
        EnsureOpen();
        if (PartsCost > 0) throw new DomainConflictException("WORK_ORDER_STATE", "Parts were used on this work order; complete it instead of cancelling.");
        Status = WorkOrderStatus.Cancelled;
    }

    private void EnsureOpen()
    {
        if (!IsOpen) throw new DomainConflictException("WORK_ORDER_CLOSED", "The work order is already closed.");
    }
}

public class WorkOrderPartUsage : BranchEntity
{
    public Guid WorkOrderId { get; set; }
    public Guid ItemId { get; set; }
    public Guid LocationId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Recurring maintenance every <see cref="IntervalDays"/> days starting at <see cref="NextDueDate"/>.</summary>
public class PreventiveMaintenancePlan : BranchEntity, IConcurrencyTracked
{
    public string Title { get; set; } = string.Empty;
    public Guid? AssetId { get; set; }
    public Guid? RoomId { get; set; }
    public int IntervalDays { get; set; }
    public DateOnly NextDueDate { get; set; }
    public MaintenancePriority Priority { get; set; } = MaintenancePriority.Normal;
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>One generated due date of a plan. Unique (BranchId, PlanId, DueDate) makes generation idempotent.</summary>
public class PreventiveMaintenanceOccurrence : BranchEntity
{
    public Guid PlanId { get; set; }
    public DateOnly DueDate { get; set; }
    public Guid WorkOrderId { get; set; }
}
