using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Desktop.Services;
using P = HMS.Desktop.Services.PermissionNames;

namespace HMS.Desktop.ViewModels;

public sealed record ModuleItem(string Title, string Permission, string Phase, bool IsAvailable);

/// <summary>Main window: who is signed in, which branch, and the modules this user may open.</summary>
public sealed partial class ShellViewModel(ApiClient api, SessionState session) : ObservableObject
{
    private static readonly ModuleItem[] AllModules =
    [
        new("Dashboard", P.BranchesView, "1", true),
        new("Reservations", P.ReservationsView, "2", false),
        new("Front desk", P.FrontDeskCheckIn, "2", false),
        new("Rooms and rates", P.RoomsView, "2", false),
        new("Housekeeping", P.HousekeepingView, "2", false),
        new("Folios and payments", P.FoliosView, "2", false),
        new("Cashier shift", P.CashierOperate, "2", false),
        new("Restaurant / POS", P.RestaurantOperate, "3", false),
        new("Inventory and purchasing", P.InventoryView, "3", false),
        new("Maintenance", P.MaintenanceView, "3", false),
        new("Staff and rosters", P.StaffView, "3", false),
        new("Accounting", P.AccountingView, "3", false),
        new("Reports and analytics", P.ReportsView, "4", false),
        new("Document centre", P.DocumentsView, "4", false),
        new("Users and branches", P.UsersManage, "1", false),
        new("Audit log", P.AuditView, "1", false),
    ];

    public SessionState Session => session;

    public IReadOnlyList<ModuleItem> Modules =>
        AllModules.Where(m => session.HasPermission(m.Permission)).ToList();

    public string Title => $"HMS - {session.Branch?.Name ?? "No branch"}";

    public event EventHandler? SignedOut;

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await api.LogoutAsync();
        SignedOut?.Invoke(this, EventArgs.Empty);
    }
}
