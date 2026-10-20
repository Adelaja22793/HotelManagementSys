namespace HMS.Application.Security;

/// <summary>Fine-grained capabilities checked by API policies. Roles map to sets of these.</summary>
public static class Permissions
{
    // Organisation and access
    public const string BranchesView = "branches.view";
    public const string BranchesManage = "branches.manage";
    public const string UsersManage = "users.manage";
    public const string AuditView = "audit.view";
    public const string SystemManage = "system.manage";

    // Rooms, rates and reservations
    public const string RoomsView = "rooms.view";
    public const string RoomsManage = "rooms.manage";
    public const string RatesManage = "rates.manage";
    public const string GuestsView = "guests.view";
    public const string GuestsManage = "guests.manage";
    public const string ReservationsView = "reservations.view";
    public const string ReservationsManage = "reservations.manage";
    public const string OverbookingOverride = "reservations.overbook";

    // Front desk and housekeeping
    public const string FrontDeskCheckIn = "frontdesk.checkin";
    public const string FrontDeskCheckOut = "frontdesk.checkout";
    public const string CheckOutWithBalance = "frontdesk.checkout.balance-override";
    public const string HousekeepingView = "housekeeping.view";
    public const string HousekeepingWork = "housekeeping.work";
    public const string HousekeepingInspect = "housekeeping.inspect";

    // Billing and cashier
    public const string FoliosView = "folios.view";
    public const string FoliosPost = "folios.post";
    public const string FolioAdjust = "folios.adjust";
    public const string PaymentsRecord = "payments.record";
    public const string RefundsRequest = "refunds.request";
    public const string RefundsApprove = "refunds.approve";
    public const string DiscountsApprove = "discounts.approve";
    public const string CashierOperate = "cashier.operate";
    public const string CashierApproveVariance = "cashier.variance.approve";

    // Operations and finance
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

    // Integration
    public const string ReportsView = "reports.view";
    public const string DocumentsView = "documents.view";
    public const string DocumentsManage = "documents.manage";
    public const string DocumentsApprove = "documents.approve";
    public const string NotificationsManage = "notifications.manage";
    public const string BusinessDayClose = "businessday.close";

    public static IReadOnlyList<string> All { get; } = typeof(Permissions)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();
}
