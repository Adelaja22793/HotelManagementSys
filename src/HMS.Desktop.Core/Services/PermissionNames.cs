namespace HMS.Desktop.Services;

/// <summary>
/// Permission names as issued by the API. The desktop only uses them to hide screens and buttons;
/// the API enforces every permission and branch check on the server. A test keeps this list in step with the server.
/// </summary>
public static class PermissionNames
{
    public const string BranchesView = "branches.view";
    public const string BranchesManage = "branches.manage";
    public const string UsersManage = "users.manage";
    public const string AuditView = "audit.view";
    public const string SystemManage = "system.manage";
    public const string RoomsView = "rooms.view";
    public const string RoomsManage = "rooms.manage";
    public const string RatesManage = "rates.manage";
    public const string GuestsView = "guests.view";
    public const string GuestsManage = "guests.manage";
    public const string ReservationsView = "reservations.view";
    public const string ReservationsManage = "reservations.manage";
    public const string OverbookingOverride = "reservations.overbook";
    public const string FrontDeskCheckIn = "frontdesk.checkin";
    public const string FrontDeskCheckOut = "frontdesk.checkout";
    public const string CheckOutWithBalance = "frontdesk.checkout.balance-override";
    public const string HousekeepingView = "housekeeping.view";
    public const string HousekeepingWork = "housekeeping.work";
    public const string HousekeepingInspect = "housekeeping.inspect";
    public const string FoliosView = "folios.view";
    public const string FoliosPost = "folios.post";
    public const string FolioAdjust = "folios.adjust";
    public const string PaymentsRecord = "payments.record";
    public const string PaymentsVerify = "payments.verify";
    public const string RefundsRequest = "refunds.request";
    public const string RefundsApprove = "refunds.approve";
    public const string DiscountsApprove = "discounts.approve";
    public const string CashierOperate = "cashier.operate";
    public const string CashierApproveVariance = "cashier.variance.approve";
    public const string InventoryView = "inventory.view";
    public const string InventoryManage = "inventory.manage";
    public const string PurchasingManage = "purchasing.manage";
    public const string PurchasingApprove = "purchasing.approve";
    public const string RestaurantOperate = "restaurant.operate";
    public const string RestaurantManage = "restaurant.manage";
    public const string MaintenanceView = "maintenance.view";
    public const string MaintenanceWork = "maintenance.work";
    public const string MaintenanceManage = "maintenance.manage";
    public const string StaffView = "staff.view";
    public const string StaffManage = "staff.manage";
    public const string SecurityView = "security.view";
    public const string SecurityManage = "security.manage";
    public const string AccountingView = "accounting.view";
    public const string AccountingPost = "accounting.post";
    public const string AccountingManage = "accounting.manage";
    public const string ReportsView = "reports.view";
    public const string DocumentsView = "documents.view";
    public const string DocumentsManage = "documents.manage";
    public const string DocumentsApprove = "documents.approve";
    public const string NotificationsManage = "notifications.manage";
    public const string BusinessDayClose = "businessday.close";
}
