using static HMS.Application.Security.Permissions;

namespace HMS.Application.Security;

/// <summary>
/// Default role-to-permission mapping. Kept in code so it is reviewed and tested; a later phase can
/// move it to configurable data if the hotel needs custom roles.
/// </summary>
public static class RolePermissions
{
    private static readonly string[] ViewAll =
    [
        BranchesView, RoomsView, GuestsView, ReservationsView, HousekeepingView, FoliosView,
        InventoryView, MaintenanceView, StaffView, SecurityView, AccountingView, ReportsView, DocumentsView,
    ];

    private static readonly Dictionary<string, HashSet<string>> Map = new(StringComparer.Ordinal)
    {
        [Roles.SystemAdmin] = [.. Permissions.All],

        [Roles.HotelManager] =
        [
            .. ViewAll, BranchesManage, AuditView,
            RoomsManage, RatesManage, GuestsManage, ReservationsManage, OverbookingOverride,
            FrontDeskCheckIn, FrontDeskCheckOut, CheckOutWithBalance,
            HousekeepingWork, HousekeepingInspect,
            FoliosPost, FolioAdjust, PaymentsRecord, RefundsRequest, RefundsApprove, DiscountsApprove,
            CashierApproveVariance,
            InventoryManage, PurchasingManage, PurchasingApprove, RestaurantManage, RestaurantOperate,
            MaintenanceManage, MaintenanceWork, StaffManage, SecurityManage,
            DocumentsManage, DocumentsApprove, NotificationsManage, BusinessDayClose,
        ],

        [Roles.Receptionist] =
        [
            BranchesView, RoomsView, GuestsView, GuestsManage, ReservationsView, ReservationsManage,
            FrontDeskCheckIn, FrontDeskCheckOut, HousekeepingView,
            FoliosView, FoliosPost, PaymentsRecord, RefundsRequest, CashierOperate, DocumentsView,
            SecurityView,
        ],

        [Roles.Housekeeping] = [BranchesView, RoomsView, HousekeepingView, HousekeepingWork, MaintenanceView],

        [Roles.Cashier] =
        [
            BranchesView, GuestsView, ReservationsView, FoliosView, FoliosPost, PaymentsRecord,
            RefundsRequest, CashierOperate,
        ],

        [Roles.Accountant] =
        [
            BranchesView, FoliosView, ReportsView, AccountingView, AccountingPost, AccountingManage,
            AuditView, InventoryView, PurchasingManage, DocumentsView,
        ],

        // Auditor is strictly read-only.
        [Roles.Auditor] = [.. ViewAll, AuditView],

        [Roles.InventoryClerk] = [BranchesView, InventoryView, InventoryManage, PurchasingManage],
        [Roles.RestaurantStaff] = [BranchesView, GuestsView, RestaurantOperate, FoliosPost, PaymentsRecord, CashierOperate],
        [Roles.MaintenanceTechnician] = [BranchesView, RoomsView, MaintenanceView, MaintenanceWork, InventoryView],
        [Roles.HumanResources] = [BranchesView, StaffView, StaffManage],
    };

    public static IReadOnlySet<string> For(string role) =>
        Map.TryGetValue(role, out var set) ? set : new HashSet<string>();

    public static IReadOnlyList<string> ForRoles(IEnumerable<string> roles) =>
        roles.SelectMany(For).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static bool HasPermission(IEnumerable<string> roles, string permission) =>
        roles.Any(r => For(r).Contains(permission));
}
