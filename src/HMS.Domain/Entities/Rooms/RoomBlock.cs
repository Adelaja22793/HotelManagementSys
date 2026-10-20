using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>
/// Takes a room out of sale for a date range (maintenance, renovation, owner use). Availability
/// subtracts active blocks independently of the room's current status.
/// </summary>
public class RoomBlock : BranchEntity
{
    private RoomBlock() { }

    public RoomBlock(Guid companyId, Guid branchId, Guid roomId, DateOnly startDate, DateOnly endDate, string reason)
    {
        if (endDate <= startDate) throw new DomainException("DATES_INVALID", "Block end must be after start.");
        CompanyId = companyId;
        BranchId = branchId;
        RoomId = roomId;
        StartDate = startDate;
        EndDate = endDate;
        Reason = Guard.Required(reason, "REASON_REQUIRED", "A reason is required", 500);
    }

    public Guid RoomId { get; private set; }
    public Room? Room { get; private set; }
    public DateOnly StartDate { get; private set; }
    /// <summary>Exclusive: the room is sellable again on this date.</summary>
    public DateOnly EndDate { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTime? ReleasedAtUtc { get; private set; }

    public void Release(DateTime utcNow)
    {
        if (!IsActive) throw new DomainConflictException("BLOCK_RELEASED", "The block has already been released.");
        IsActive = false;
        ReleasedAtUtc = utcNow;
    }
}
