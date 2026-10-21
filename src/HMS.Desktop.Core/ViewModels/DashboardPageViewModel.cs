using CommunityToolkit.Mvvm.ComponentModel;
using HMS.Contracts.Analytics;
using HMS.Contracts.FrontDesk;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Today at a glance (front desk counts) and the last seven days of occupancy and revenue.</summary>
public sealed partial class DashboardPageViewModel(ApiClient api, SessionState session) : PageViewModel
{
    public override string Title => "Dashboard";

    [ObservableProperty] private FrontDeskSummaryDto? _today;
    [ObservableProperty] private DashboardDto? _week;

    public bool CanSeeFrontDesk => session.HasPermission(PermissionNames.FrontDeskCheckIn) || session.HasPermission(PermissionNames.ReservationsView);
    public bool CanSeeMetrics => session.HasPermission(PermissionNames.ReportsView);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        if (CanSeeFrontDesk) Today = await api.GetAsync<FrontDeskSummaryDto>(api.BranchPath("front-desk/summary"));
        if (CanSeeMetrics)
        {
            var to = Today?.BusinessDate ?? DateOnly.FromDateTime(DateTime.Today);
            Week = await api.GetAsync<DashboardDto>(Query.Build(api.BranchPath("analytics/dashboard"), ("from", to.AddDays(-6)), ("to", to)));
        }
    });
}
