namespace HMS.Domain.Enums;

/// <summary>Current operational state of a physical room. Not a historical occupancy ledger.</summary>
public enum RoomStatus
{
    VacantClean = 0,
    VacantDirty = 1,
    OccupiedClean = 2,
    OccupiedDirty = 3,
}

public enum ReservationStatus
{
    Confirmed = 0,
    CheckedIn = 1,
    CheckedOut = 2,
    Cancelled = 3,
    NoShow = 4,
}

public enum ReservationSource
{
    FrontDesk = 0,
    Phone = 1,
    WalkIn = 2,
    Email = 3,
    Website = 4,
    Channel = 5,
}

public enum StayStatus
{
    Active = 0,
    Closed = 1,
}

public enum HousekeepingTaskType
{
    Checkout = 0,
    Stayover = 1,
    Touchup = 2,
}

public enum HousekeepingTaskStatus
{
    Pending = 0,
    InProgress = 1,
    AwaitingInspection = 2,
    Completed = 3,
    Cancelled = 4,
}

public enum TaxScope
{
    Room = 0,
    Service = 1,
    All = 2,
}

public enum FolioStatus
{
    Open = 0,
    Closed = 1,
}

public enum ChargeType
{
    Room = 0,
    Tax = 1,
    Service = 2,
    Restaurant = 3,
    Adjustment = 4,
    Discount = 5,
}

public enum TenderType
{
    Cash = 0,
    PosCard = 1,
    BankTransfer = 2,
    Online = 3,
}

public enum PaymentStatus
{
    Pending = 0,
    Confirmed = 1,
    Failed = 2,
}

public enum RefundStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
    Executed = 3,
}

public enum CashierShiftStatus
{
    Open = 0,
    Closing = 1,
    Closed = 2,
}

public enum CashierMovementType
{
    OpeningFloat = 0,
    Payment = 1,
    Refund = 2,
}
