namespace HMS.Desktop.Services;

/// <summary>
/// Permission names as issued by the API. The desktop only uses them to hide screens;
/// the API enforces every permission and branch check on the server.
/// </summary>
public static class PermissionNames
{
    public const string BranchesView = "branches.view";
    public const string UsersManage = "users.manage";
    public const string AuditView = "audit.view";
    public const string RoomsView = "rooms.view";
    public const string ReservationsView = "reservations.view";
    public const string FrontDeskCheckIn = "frontdesk.checkin";
    public const string HousekeepingView = "housekeeping.view";
    public const string FoliosView = "folios.view";
    public const string CashierOperate = "cashier.operate";
    public const string RestaurantOperate = "restaurant.operate";
    public const string InventoryView = "inventory.view";
    public const string MaintenanceView = "maintenance.view";
    public const string StaffView = "staff.view";
    public const string AccountingView = "accounting.view";
    public const string ReportsView = "reports.view";
    public const string DocumentsView = "documents.view";
}
