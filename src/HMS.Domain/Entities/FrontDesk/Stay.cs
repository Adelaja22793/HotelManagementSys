using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>An in-house stay created at check-in. History is preserved after check-out.</summary>
public class Stay : BranchEntity, IConcurrencyTracked
{
    private Stay() { }

    public Stay(Guid companyId, Guid branchId, Guid reservationId, Guid guestId, Guid roomId, DateTime checkedInAtUtc, string userId)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ReservationId = reservationId;
        GuestId = guestId;
        RoomId = roomId;
        CheckedInAtUtc = checkedInAtUtc;
        CheckedInByUserId = userId;
    }

    public Guid ReservationId { get; private set; }
    public Reservation? Reservation { get; private set; }
    public Guid GuestId { get; private set; }
    public Guid RoomId { get; private set; }
    public Room? Room { get; private set; }
    public StayStatus Status { get; private set; } = StayStatus.Active;
    public DateTime CheckedInAtUtc { get; private set; }
    public string CheckedInByUserId { get; private set; } = string.Empty;
    public DateTime? CheckedOutAtUtc { get; private set; }
    public string? CheckedOutByUserId { get; private set; }
    public bool CheckedOutWithBalance { get; private set; }
    public string? BalanceOverrideReason { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Close(DateTime utcNow, string userId, bool withBalance, string? overrideReason)
    {
        if (Status != StayStatus.Active) throw new DomainConflictException("STAY_CLOSED", "The stay is already closed.");
        Status = StayStatus.Closed;
        CheckedOutAtUtc = utcNow;
        CheckedOutByUserId = userId;
        CheckedOutWithBalance = withBalance;
        BalanceOverrideReason = overrideReason;
    }
}
