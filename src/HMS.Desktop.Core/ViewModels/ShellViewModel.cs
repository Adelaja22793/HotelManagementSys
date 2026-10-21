using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Desktop.Services;
using P = HMS.Desktop.Services.PermissionNames;

namespace HMS.Desktop.ViewModels;

/// <summary>A module in the left-hand menu. <see cref="PageType"/> is null for modules whose screens are not built yet.</summary>
public sealed record ModuleItem(string Title, string Permission, Type? PageType)
{
    public bool IsAvailable => PageType is not null;
}

/// <summary>Main window: who is signed in, which branch, the modules this user may open, and the open screen.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private static readonly ModuleItem[] AllModules =
    [
        new("Dashboard", P.BranchesView, typeof(DashboardPageViewModel)),
        new("Front desk", P.FrontDeskCheckIn, typeof(FrontDeskPageViewModel)),
        new("Reservations", P.ReservationsView, typeof(ReservationsPageViewModel)),
        new("Folios and payments", P.FoliosView, typeof(FolioPageViewModel)),
        new("Cashier shift", P.CashierOperate, typeof(CashierPageViewModel)),
        new("Housekeeping", P.HousekeepingView, typeof(HousekeepingPageViewModel)),
        new("Rooms and rates", P.RoomsView, typeof(RoomsPageViewModel)),
        new("Guests", P.GuestsView, typeof(GuestsPageViewModel)),
        new("Restaurant / POS", P.RestaurantOperate, null),
        new("Inventory and purchasing", P.InventoryView, null),
        new("Maintenance", P.MaintenanceView, null),
        new("Staff and rosters", P.StaffView, null),
        new("Accounting", P.AccountingView, null),
        new("Night audit", P.ReportsView, null),
        new("Document centre", P.DocumentsView, null),
        new("Security log", P.SecurityView, null),
        new("Users", P.UsersManage, null),
        new("Audit log", P.AuditView, null),
    ];

    private readonly ApiClient _api;
    private readonly NavigationService _navigation;

    public ShellViewModel(ApiClient api, SessionState session, NavigationService navigation)
    {
        _api = api;
        _navigation = navigation;
        Session = session;
        navigation.Navigated += OnNavigated;
    }

    private void OnNavigated(object? sender, PageViewModel page)
    {
        CurrentPage = page;
        _selectedModule = Modules.FirstOrDefault(m => m.PageType == page.GetType());
        OnPropertyChanged(nameof(SelectedModule));
    }

    public SessionState Session { get; }

    public IReadOnlyList<ModuleItem> Modules => AllModules.Where(m => Session.HasPermission(m.Permission)).ToList();

    public string Title => $"HMS - {Session.Branch?.Name ?? "No branch"}";

    [ObservableProperty] private PageViewModel? _currentPage;

    private ModuleItem? _selectedModule;

    public ModuleItem? SelectedModule
    {
        get => _selectedModule;
        set
        {
            if (value == _selectedModule) return;
            _selectedModule = value;
            OnPropertyChanged();
            if (value?.PageType is { } type) _ = _navigation.OpenAsync(type);
        }
    }

    public event EventHandler? SignedOut;

    /// <summary>Opens the first module the user can use.</summary>
    public Task StartAsync() =>
        Modules.FirstOrDefault(m => m.IsAvailable)?.PageType is { } first ? _navigation.OpenAsync(first) : Task.CompletedTask;

    [RelayCommand]
    private Task RefreshAsync() => CurrentPage?.LoadAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private async Task SignOutAsync()
    {
        _navigation.Navigated -= OnNavigated;
        await _api.LogoutAsync();
        SignedOut?.Invoke(this, EventArgs.Empty);
    }
}
