using HMS.Domain.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;

namespace HMS.Domain.UnitTests;

public class StateTransitionTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Stay_dates_count_nights_and_reject_bad_ranges()
    {
        var dates = new StayDates(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 4));
        Assert.Equal(3, dates.Nights);
        Assert.Equal(3, dates.EachNight().Count());
        Assert.Throws<DomainException>(() => new StayDates(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4)));
        Assert.Throws<DomainException>(() => new StayDates(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Room_must_be_clean_before_occupancy()
    {
        var room = new Room(Company, BranchId, Guid.NewGuid(), "101");
        room.MarkVacantDirty();
        Assert.Throws<DomainConflictException>(room.MarkOccupied);

        room.MarkClean();
        room.MarkOccupied();
        Assert.Equal(RoomStatus.OccupiedClean, room.Status);
        Assert.Throws<DomainConflictException>(room.MarkOccupied);
        Assert.ThrowsAny<DomainException>(room.Deactivate);
    }

    [Fact]
    public void Housekeeping_lifecycle_with_inspection_and_rejection()
    {
        var task = new HousekeepingTask(Company, BranchId, Guid.NewGuid(), HousekeepingTaskType.Checkout, "CheckOut", "s1", new DateOnly(2026, 11, 1));
        Assert.Throws<DomainConflictException>(() => task.FinishCleaning(Now, true));

        task.Start("hk1", Now);
        Assert.False(task.FinishCleaning(Now, inspectionRequired: true));
        Assert.Equal(HousekeepingTaskStatus.AwaitingInspection, task.Status);

        task.FailInspection("sup", "Bathroom not done", Now);
        Assert.Equal(HousekeepingTaskStatus.InProgress, task.Status);

        task.FinishCleaning(Now, true);
        task.PassInspection("sup", Now);
        Assert.Equal(HousekeepingTaskStatus.Completed, task.Status);
        Assert.Throws<DomainConflictException>(task.Cancel);
    }

    [Fact]
    public void Housekeeping_without_inspection_completes_directly()
    {
        var task = new HousekeepingTask(Company, BranchId, Guid.NewGuid(), HousekeepingTaskType.Stayover, "Manual", "m1", new DateOnly(2026, 11, 1));
        task.Start("hk1", Now);
        Assert.True(task.FinishCleaning(Now, inspectionRequired: false));
        Assert.Equal(HousekeepingTaskStatus.Completed, task.Status);
    }

    [Fact]
    public void Refund_cannot_be_self_approved_and_needs_approval_before_execution()
    {
        var refund = new Refund(Company, BranchId, Guid.NewGuid(), null, "RFD-1", 5000, TenderType.Cash, "Overcharge", "cashier", Now);
        Assert.Throws<DomainConflictException>(() => refund.Execute("cashier", Now, null, null));
        Assert.Throws<DomainConflictException>(() => refund.Approve("cashier", Now));

        refund.Approve("manager", Now);
        refund.Execute("cashier", Now, null, Guid.NewGuid());
        Assert.Equal(RefundStatus.Executed, refund.Status);
    }

    [Fact]
    public void Cashier_shift_closes_immediately_without_variance()
    {
        var shift = new CashierShift(Company, BranchId, "c1", 10_000, Now, new DateOnly(2026, 11, 1));
        Assert.True(shift.SubmitCount(Now, 60_000, 20_000, 0, 60_000, 20_000, 0, null));
        Assert.Equal(CashierShiftStatus.Closed, shift.Status);
        Assert.Throws<DomainConflictException>(shift.EnsureOpen);
    }

    [Fact]
    public void Cashier_shift_variance_needs_explanation_and_independent_approval()
    {
        var shift = new CashierShift(Company, BranchId, "c1", 0, Now, new DateOnly(2026, 11, 1));
        Assert.Throws<DomainException>(() => shift.SubmitCount(Now, 50_000, 0, 0, 49_000, 0, 0, null));

        var fresh = new CashierShift(Company, BranchId, "c1", 0, Now, new DateOnly(2026, 11, 1));
        Assert.False(fresh.SubmitCount(Now, 50_000, 0, 0, 49_000, 0, 0, "Gave wrong change"));
        Assert.Equal(-1_000m, fresh.CashVariance);
        Assert.Equal(CashierShiftStatus.Closing, fresh.Status);

        Assert.Throws<DomainConflictException>(() => fresh.ApproveVariance("c1", Now));
        fresh.ApproveVariance("manager", Now);
        Assert.Equal(CashierShiftStatus.Closed, fresh.Status);
    }

    [Fact]
    public void Charges_are_reversed_not_edited()
    {
        var charge = new FolioCharge(Company, BranchId, Guid.NewGuid(), ChargeType.Service, "Laundry", 2, 1_500, new DateOnly(2026, 11, 1), "k1");
        var reversal = charge.CreateReversal(new DateOnly(2026, 11, 2), "posted in error");

        Assert.Equal(-3_000m, reversal.Amount);
        Assert.Equal(charge.Id, reversal.ReversesChargeId);
        Assert.True(charge.IsReversed);
        Assert.Throws<DomainConflictException>(() => charge.CreateReversal(new DateOnly(2026, 11, 2), "again"));
        Assert.Throws<DomainConflictException>(() => reversal.CreateReversal(new DateOnly(2026, 11, 2), "undo"));
    }

    [Fact]
    public void Payment_is_confirmed_once()
    {
        var p = new Payment(Company, BranchId, Guid.NewGuid(), TenderType.BankTransfer, 1000, "NGN", "key", "u", Now, "TRF-1");
        p.Confirm("mgr", Now);
        Assert.Throws<DomainConflictException>(() => p.Confirm("mgr", Now));
        Assert.Throws<DomainConflictException>(() => p.Fail("late"));
    }

    [Fact]
    public void Room_type_enforces_occupancy()
    {
        var type = new RoomType(Company, BranchId, "DLX", "Deluxe", 2, 1, 50_000);
        type.EnsureFits(2, 1);
        Assert.Throws<DomainException>(() => type.EnsureFits(3, 0));
        Assert.Throws<DomainException>(() => type.EnsureFits(0, 1));
    }
}
