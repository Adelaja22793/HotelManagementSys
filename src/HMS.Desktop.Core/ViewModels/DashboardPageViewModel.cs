using CommunityToolkit.Mvvm.ComponentModel;
using HMS.Contracts.Analytics;
using HMS.Contracts.FrontDesk;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Today at a glance (front desk counts) and the last seven days of occupancy and revenue.</summary>
public sealed partial class DashboardPageViewModel(ApiClient api, SessionState session) : PageViewModel
{
    public override string Title => "Dashboard";
    protected override IEnumerable<HMS.Desktop.ViewModels.Setup.ActionDefinition> ActionDefinitions => HMS.Desktop.ViewModels.Setup.ActionCatalog.Account;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArrivalsToday), nameof(DeparturesToday))]
    private FrontDeskSummaryDto? _today;
    [ObservableProperty] private DashboardDto? _week;

    /// <summary>"3 of 5": arrivals checked in out of all arrivals due today (the API counts those still to come separately).</summary>
    public string ArrivalsToday => Today is { } t ? $"{t.ArrivalsCheckedIn} of {t.ArrivalsCheckedIn + t.ArrivalsExpected}" : "";

    /// <summary>"1 of 4": departures checked out out of all departures due today.</summary>
    public string DeparturesToday => Today is { } t ? $"{t.DeparturesCheckedOut} of {t.DeparturesCheckedOut + t.DeparturesExpected}" : "";

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
