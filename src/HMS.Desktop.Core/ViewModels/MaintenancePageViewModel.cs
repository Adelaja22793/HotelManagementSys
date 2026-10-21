using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Maintenance;
using HMS.Contracts.Rooms;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Report faults, turn them into work orders, and take work orders through start, finish and sign-off.</summary>
public sealed partial class MaintenancePageViewModel(ApiClient api, SessionState session) : PageViewModel
{
    public static readonly string[] Priorities = ["Low", "Normal", "High", "Urgent"];

    public override string Title => "Maintenance";

    protected override IEnumerable<HMS.Desktop.ViewModels.Setup.ActionDefinition> ActionDefinitions => HMS.Desktop.ViewModels.Setup.ActionCatalog.Maintenance;

    public ObservableCollection<MaintenanceRequestDto> Requests { get; } = [];
    public ObservableCollection<WorkOrderDto> WorkOrders { get; } = [];
    public ObservableCollection<RoomDto> Rooms { get; } = [];

    [ObservableProperty] private string _newTitle = "";
    [ObservableProperty] private string? _newDescription;
    [ObservableProperty] private string _newPriority = "Normal";
    [ObservableProperty] private RoomDto? _newRoom;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand), nameof(RejectCommand))]
    private MaintenanceRequestDto? _selectedRequest;

    [ObservableProperty] private bool _takeRoomOutOfService;
    [ObservableProperty] private DateTime? _expectedReady;
    [ObservableProperty] private string? _rejectReason;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(FinishCommand), nameof(SignOffCommand), nameof(SendBackCommand))]
    private WorkOrderDto? _selectedWorkOrder;

    [ObservableProperty] private decimal _labourHours;
    [ObservableProperty] private decimal _labourCost;
    [ObservableProperty] private string? _completionNotes;

    public bool CanManage => session.HasPermission(PermissionNames.MaintenanceManage);
    public bool CanWork => session.HasPermission(PermissionNames.MaintenanceWork);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        FrontDeskPageViewModel.Replace(Requests, await api.GetAsync<List<MaintenanceRequestDto>>(api.BranchPath("maintenance/requests")));
        FrontDeskPageViewModel.Replace(WorkOrders, await api.GetAsync<List<WorkOrderDto>>(api.BranchPath("maintenance/work-orders")));
        if (Rooms.Count == 0 && session.HasPermission(PermissionNames.RoomsView))
            FrontDeskPageViewModel.Replace(Rooms, (await api.GetAsync<List<RoomDto>>(api.BranchPath("rooms"))).OrderBy(r => r.RoomNumber, StringComparer.Ordinal));
    });

    [RelayCommand]
    private async Task ReportAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTitle)) { ErrorMessage = "Say briefly what is wrong."; return; }
        if (await RunAsync(() => api.PostAsync<MaintenanceRequestDto>(api.BranchPath("maintenance/requests"),
                new CreateMaintenanceRequestRequest(NewTitle.Trim(), NewDescription, NewPriority, NewRoom?.Id, null))))
        {
            NewTitle = "";
            NewDescription = null;
            NewRoom = null;
            await LoadAsync();
            StatusMessage = "Fault reported.";
        }
    }

    private bool CanTriage() => CanManage && SelectedRequest?.Status == "Open";

    [RelayCommand(CanExecute = nameof(CanTriage))]
    private Task AcceptAsync() => Triage(true);

    [RelayCommand(CanExecute = nameof(CanTriage))]
    private Task RejectAsync()
    {
        if (string.IsNullOrWhiteSpace(RejectReason)) { ErrorMessage = "Give a reason for rejecting the request."; return Task.CompletedTask; }
        return Triage(false);
    }

    private async Task Triage(bool accept)
    {
        var request = SelectedRequest!;
        if (await RunAsync(() => api.PostAsync<MaintenanceRequestDto>(api.BranchPath($"maintenance/requests/{request.Id}/triage"),
                new TriageRequest(accept, accept ? null : RejectReason, null, null, accept && TakeRoomOutOfService,
                    accept && TakeRoomOutOfService && ExpectedReady is { } d ? DateOnly.FromDateTime(d) : null, request.ConcurrencyStamp))))
        {
            RejectReason = null;
            TakeRoomOutOfService = false;
            await LoadAsync();
            StatusMessage = accept ? "Work order created." : "Request rejected.";
        }
    }

    private bool CanStart() => CanWork && SelectedWorkOrder?.Status is "Open" or "Assigned";
    private bool CanFinish() => CanWork && SelectedWorkOrder?.Status == "InProgress";
    private bool CanSignOff() => CanManage && SelectedWorkOrder?.Status == "AwaitingInspection";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => Act("start", new WorkOrderActionRequest(SelectedWorkOrder!.ConcurrencyStamp), "Work started.");

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private Task FinishAsync() => Act("finish", new FinishWorkRequest(LabourHours, LabourCost, CompletionNotes, SelectedWorkOrder!.ConcurrencyStamp),
        "Finished; waiting for sign-off.");

    [RelayCommand(CanExecute = nameof(CanSignOff))]
    private Task SignOffAsync() => Act("inspect", new InspectWorkRequest(true, SelectedWorkOrder!.ConcurrencyStamp), "Signed off.");

    [RelayCommand(CanExecute = nameof(CanSignOff))]
    private Task SendBackAsync() => Act("inspect", new InspectWorkRequest(false, SelectedWorkOrder!.ConcurrencyStamp), "Sent back to the technician.");

    private async Task Act(string action, object body, string message)
    {
        var order = SelectedWorkOrder!;
        if (await RunAsync(() => api.PostAsync<WorkOrderDto>(api.BranchPath($"maintenance/work-orders/{order.Id}/{action}"), body)))
        {
            LabourHours = LabourCost = 0;
            CompletionNotes = null;
            await LoadAsync();
            StatusMessage = $"{order.WorkOrderNumber}: {message}";
        }
    }
}
