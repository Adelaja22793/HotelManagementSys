using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>
/// Cleaning work for a room. Lifecycle: Pending → InProgress → AwaitingInspection → Completed
/// (inspection rejection returns it to InProgress). The (BranchId, SourceType, SourceId) key makes
/// task creation idempotent so one check-out never creates two tasks.
/// </summary>
public class HousekeepingTask : BranchEntity, IConcurrencyTracked
{
    private HousekeepingTask() { }

    public HousekeepingTask(Guid companyId, Guid branchId, Guid roomId, HousekeepingTaskType type, string sourceType, string sourceId, DateOnly businessDate)
    {
        CompanyId = companyId;
        BranchId = branchId;
        RoomId = roomId;
        Type = type;
        SourceType = sourceType;
        SourceId = sourceId;
        BusinessDate = businessDate;
    }

    public Guid RoomId { get; private set; }
    public Room? Room { get; private set; }
    public HousekeepingTaskType Type { get; private set; }
    public HousekeepingTaskStatus Status { get; private set; } = HousekeepingTaskStatus.Pending;
    public string SourceType { get; private set; } = string.Empty;
    public string SourceId { get; private set; } = string.Empty;
    public DateOnly BusinessDate { get; private set; }
    public string? AssignedToUserId { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? InspectedByUserId { get; private set; }
    public DateTime? InspectedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? Notes { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsOpen => Status is not (HousekeepingTaskStatus.Completed or HousekeepingTaskStatus.Cancelled);

    public void Assign(string userId)
    {
        if (!IsOpen) throw new DomainConflictException("TASK_CLOSED", "The task is already closed.");
        AssignedToUserId = userId;
    }

    public void Start(string userId, DateTime utcNow)
    {
        if (Status != HousekeepingTaskStatus.Pending) throw new DomainConflictException("TASK_STATE", "Only pending tasks can be started.");
        AssignedToUserId ??= userId;
        Status = HousekeepingTaskStatus.InProgress;
        StartedAtUtc = utcNow;
    }

    /// <summary>Marks cleaning done. Returns true when the room is ready (no inspection needed).</summary>
    public bool FinishCleaning(DateTime utcNow, bool inspectionRequired)
    {
        if (Status != HousekeepingTaskStatus.InProgress) throw new DomainConflictException("TASK_STATE", "Only tasks in progress can be completed.");
        if (inspectionRequired)
        {
            Status = HousekeepingTaskStatus.AwaitingInspection;
            return false;
        }
        Status = HousekeepingTaskStatus.Completed;
        CompletedAtUtc = utcNow;
        return true;
    }

    public void PassInspection(string inspectorId, DateTime utcNow)
    {
        if (Status != HousekeepingTaskStatus.AwaitingInspection) throw new DomainConflictException("TASK_STATE", "The task is not awaiting inspection.");
        InspectedByUserId = inspectorId;
        InspectedAtUtc = utcNow;
        CompletedAtUtc = utcNow;
        RejectionReason = null;
        Status = HousekeepingTaskStatus.Completed;
    }

    public void FailInspection(string inspectorId, string reason, DateTime utcNow)
    {
        if (Status != HousekeepingTaskStatus.AwaitingInspection) throw new DomainConflictException("TASK_STATE", "The task is not awaiting inspection.");
        InspectedByUserId = inspectorId;
        InspectedAtUtc = utcNow;
        RejectionReason = Guard.Required(reason, "REASON_REQUIRED", "A rejection reason is required", 500);
        Status = HousekeepingTaskStatus.InProgress;
    }

    public void Cancel()
    {
        if (!IsOpen) throw new DomainConflictException("TASK_CLOSED", "The task is already closed.");
        Status = HousekeepingTaskStatus.Cancelled;
    }
}
