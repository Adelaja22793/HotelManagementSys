using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.BusinessDay;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>
/// Night audit: shows which business day must close next, runs the close, and lists the exceptions it found.
/// Blocking exceptions must be fixed or accepted with a reason before the day closes; the run can be repeated.
/// </summary>
public sealed partial class NightAuditPageViewModel(ApiClient api, SessionState session, IDialogService dialogs) : PageViewModel
{
    public override string Title => "Night audit";

    public ObservableCollection<BusinessDayExceptionDto> Exceptions { get; } = [];
    public ObservableCollection<BusinessDayCloseDto> History { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseDayCommand))]
    private BusinessDayStatusDto? _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptExceptionCommand))]
    private BusinessDayExceptionDto? _selectedException;

    [ObservableProperty] private string? _acceptReason;

    public bool CanClose => session.HasPermission(PermissionNames.BusinessDayClose);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Status = await api.GetAsync<BusinessDayStatusDto>(api.BranchPath("business-day"));
        FrontDeskPageViewModel.Replace(Exceptions, Status.PendingClose?.Exceptions ?? []);
        FrontDeskPageViewModel.Replace(History, (await api.GetAsync<List<BusinessDayCloseDto>>(Query.Build(api.BranchPath("business-day/closes"),
            ("from", Status.CurrentBusinessDate.AddDays(-30)), ("to", Status.CurrentBusinessDate)))).OrderByDescending(c => c.BusinessDate));
    });

    private bool CanRun() => CanClose && Status is { CanCloseNow: true };

    /// <summary>Runs the close for the day the server says is next, so a stale screen cannot close the wrong day.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CloseDayAsync()
    {
        var date = Status!.NextDateToClose;
        if (!dialogs.Confirm($"Run the night audit and close {date:dd MMM yyyy}?")) return;
        BusinessDayCloseDto? result = null;
        if (!await RunAsync(async () => result = await api.PostAsync<BusinessDayCloseDto>(api.BranchPath("business-day/close"), new CloseBusinessDayRequest(date))))
            return;
        await LoadAsync();
        FrontDeskPageViewModel.Replace(Exceptions, result!.Exceptions);
        StatusMessage = result.Status == "Closed"
            ? $"{date:dd MMM yyyy} is closed."
            : $"{date:dd MMM yyyy} is not closed: {result.Exceptions.Count(e => e is { Severity: "Blocking", Status: "Open" })} blocking item(s) need attention.";
    }

    private bool CanAccept() => CanClose && SelectedException is { Status: "Open" };

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private async Task AcceptExceptionAsync()
    {
        if (string.IsNullOrWhiteSpace(AcceptReason)) { ErrorMessage = "Explain why this exception is accepted."; return; }
        var exception = SelectedException!;
        BusinessDayCloseDto? result = null;
        if (await RunAsync(async () => result = await api.PostAsync<BusinessDayCloseDto>(api.BranchPath($"business-day/exceptions/{exception.Id}/accept"),
                new AcceptExceptionRequest(AcceptReason.Trim()))))
        {
            AcceptReason = null;
            await LoadAsync();
            FrontDeskPageViewModel.Replace(Exceptions, result!.Exceptions);
            StatusMessage = $"Accepted: {exception.Message}";
        }
    }
}
