using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Housekeeping;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Cleaning tasks: start, finish, and inspection by a supervisor.</summary>
public sealed partial class HousekeepingPageViewModel(ApiClient api, SessionState session) : PageViewModel
{
    public override string Title => "Housekeeping";
    protected override IEnumerable<HMS.Desktop.ViewModels.Setup.ActionDefinition> ActionDefinitions => HMS.Desktop.ViewModels.Setup.ActionCatalog.Housekeeping;

    public ObservableCollection<HousekeepingTaskDto> Tasks { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(CompleteCommand), nameof(PassCommand), nameof(FailCommand))]
    private HousekeepingTaskDto? _selected;

    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool _openOnly = true;

    public bool CanWork => session.HasPermission(PermissionNames.HousekeepingWork);
    public bool CanInspect => session.HasPermission(PermissionNames.HousekeepingInspect);

    public override Task LoadAsync() => RunAsync(async () =>
        FrontDeskPageViewModel.Replace(Tasks, await api.GetAsync<List<HousekeepingTaskDto>>(
            Query.Build(api.BranchPath("housekeeping/tasks"), ("openOnly", OpenOnly)))));

    partial void OnOpenOnlyChanged(bool value) => _ = LoadAsync();

    private bool CanStart() => CanWork && Selected?.Status == "Pending";
    private bool CanComplete() => CanWork && Selected?.Status == "InProgress";
    private bool CanJudge() => CanInspect && Selected?.Status == "AwaitingInspection";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => Act("start", new HousekeepingActionRequest(Selected!.ConcurrencyStamp, Notes), "Cleaning started.");

    [RelayCommand(CanExecute = nameof(CanComplete))]
    private Task CompleteAsync() => Act("complete", new HousekeepingActionRequest(Selected!.ConcurrencyStamp, Notes), "Marked as clean.");

    [RelayCommand(CanExecute = nameof(CanJudge))]
    private Task PassAsync() => Act("inspect", new InspectionRequest(true, null, Selected!.ConcurrencyStamp), "Inspection passed; the room is ready.");

    [RelayCommand(CanExecute = nameof(CanJudge))]
    private Task FailAsync()
    {
        if (string.IsNullOrWhiteSpace(Notes)) { ErrorMessage = "Say what needs redoing in the notes."; return Task.CompletedTask; }
        return Act("inspect", new InspectionRequest(false, Notes, Selected!.ConcurrencyStamp), "Sent back for cleaning.");
    }

    private async Task Act(string action, object body, string message)
    {
        var task = Selected!;
        if (await RunAsync(() => api.PostAsync<HousekeepingTaskDto>(api.BranchPath($"housekeeping/tasks/{task.Id}/{action}"), body)))
        {
            Notes = null;
            await LoadAsync();
            StatusMessage = $"Room {task.RoomNumber}: {message}";
        }
    }
}
