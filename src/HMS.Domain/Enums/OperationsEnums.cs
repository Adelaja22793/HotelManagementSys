namespace HMS.Domain.Enums;

public enum StockMovementType
{
    Receipt = 0,
    Issue = 1,
    AdjustmentIn = 2,
    AdjustmentOut = 3,
    TransferIn = 4,
    TransferOut = 5,
    Wastage = 6,
    Consumption = 7,
    MaintenanceUse = 8,
    ReturnToSupplier = 9,
}

public enum RequisitionStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Ordered = 4,
    Cancelled = 5,
}

public enum PurchaseOrderStatus
{
    Draft = 0,
    Approved = 1,
    PartiallyReceived = 2,
    Received = 3,
    Closed = 4,
    Cancelled = 5,
}

public enum SupplierInvoiceStatus
{
    /// <summary>Matches the received goods; ready to pay.</summary>
    Matched = 0,
    /// <summary>Quantity or price differs from what was received; needs approval before payment.</summary>
    Discrepancy = 1,
    Approved = 2,
    PartiallyPaid = 3,
    Paid = 4,
}

public enum OutletType
{
    Restaurant = 0,
    Bar = 1,
    RoomService = 2,
}

public enum RestaurantOrderStatus
{
    Open = 0,
    Settled = 1,
    Voided = 2,
}

public enum OrderLineStatus
{
    Pending = 0,
    Sent = 1,
    Ready = 2,
    Served = 3,
    Voided = 4,
}

public enum KitchenTicketStatus
{
    New = 0,
    Preparing = 1,
    Ready = 2,
    Served = 3,
    Cancelled = 4,
}

public enum OrderSettlement
{
    None = 0,
    RoomCharge = 1,
    Payment = 2,
}

public enum AssetStatus
{
    InService = 0,
    UnderMaintenance = 1,
    Retired = 2,
}

public enum MaintenancePriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3,
}

public enum MaintenanceRequestStatus
{
    Open = 0,
    Converted = 1,
    Rejected = 2,
}

public enum WorkOrderStatus
{
    Open = 0,
    Assigned = 1,
    InProgress = 2,
    AwaitingInspection = 3,
    Completed = 4,
    Cancelled = 5,
}

public enum AttendanceSource
{
    Desktop = 0,
    Manager = 1,
}

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

public enum LeaveType
{
    Annual = 0,
    Sick = 1,
    Unpaid = 2,
    Other = 3,
}

public enum AccountType
{
    Asset = 0,
    Liability = 1,
    Equity = 2,
    Revenue = 3,
    Expense = 4,
}

public enum PeriodStatus
{
    Open = 0,
    Closed = 1,
}
