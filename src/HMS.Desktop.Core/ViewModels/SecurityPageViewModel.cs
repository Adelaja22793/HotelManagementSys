using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.SecurityOps;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Security desk: incidents, lost and found with chain of custody, visitors on site, and emergencies.</summary>
public sealed partial class SecurityPageViewModel(ApiClient api, SessionState session, IDialogService dialogs) : PageViewModel
{
    public static readonly string[] IncidentTypes = ["Theft", "Damage", "Injury", "Disturbance", "Fire", "Medical", "Trespass", "Other"];
    public static readonly string[] Severities = ["Low", "Medium", "High", "Critical"];
    public static readonly string[] EmergencyTypes = ["Fire", "Medical", "Security", "Evacuation", "PowerOutage", "Flood", "Other"];

    public override string Title => "Security log";

    public ObservableCollection<IncidentDto> Incidents { get; } = [];
    public ObservableCollection<LostItemDto> LostItems { get; } = [];
    public ObservableCollection<VisitorDto> Visitors { get; } = [];
    public ObservableCollection<EmergencyDto> Emergencies { get; } = [];

    // Incidents
    [ObservableProperty] private string _incidentType = "Other";
    [ObservableProperty] private string _severity = "Low";
    [ObservableProperty] private string _incidentTitle = "";
    [ObservableProperty] private string _incidentDescription = "";
    [ObservableProperty] private string? _incidentLocation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand), nameof(ResolveCommand), nameof(CloseIncidentCommand))]
    private IncidentDto? _selectedIncident;

    [ObservableProperty] private string? _caseNote;

    // Lost and found
    [ObservableProperty] private string _itemDescription = "";
    [ObservableProperty] private string _foundLocation = "";
    [ObservableProperty] private string _storageLocation = "Security office";
    [ObservableProperty] private bool _isValuable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReturnItemCommand), nameof(DisposeItemCommand))]
    private LostItemDto? _selectedItem;

    [ObservableProperty] private string? _receivedBy;
    [ObservableProperty] private string? _identityCheck;
    [ObservableProperty] private string? _disposalReason;

    // Visitors
    [ObservableProperty] private string _visitorName = "";
    [ObservableProperty] private string? _visitorPhone;
    [ObservableProperty] private string? _idChecked;
    [ObservableProperty] private string _purpose = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignOutVisitorCommand))]
    private VisitorDto? _selectedVisitor;

    // Emergencies
    [ObservableProperty] private string _emergencyType = "Fire";
    [ObservableProperty] private string _emergencyDescription = "";
    [ObservableProperty] private string? _emergencyAction;
    [ObservableProperty] private string? _emergencySummary;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogActionCommand), nameof(EndEmergencyCommand))]
    private EmergencyDto? _selectedEmergency;

    public bool CanManage => session.HasPermission(PermissionNames.SecurityManage);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        FrontDeskPageViewModel.Replace(Incidents, await api.GetAsync<List<IncidentDto>>(api.BranchPath("security/incidents")));
        FrontDeskPageViewModel.Replace(LostItems, await api.GetAsync<List<LostItemDto>>(api.BranchPath("security/lost-found")));
        FrontDeskPageViewModel.Replace(Visitors, await api.GetAsync<List<VisitorDto>>(api.BranchPath("security/visitors")));
        FrontDeskPageViewModel.Replace(Emergencies, (await api.GetAsync<List<EmergencyDto>>(api.BranchPath("security/emergencies"))).OrderByDescending(e => e.StartedAtUtc));
        if (SelectedIncident is not null) SelectedIncident = Incidents.FirstOrDefault(i => i.Id == SelectedIncident.Id);
        if (SelectedEmergency is not null) SelectedEmergency = Emergencies.FirstOrDefault(e => e.Id == SelectedEmergency.Id);
    });

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    [RelayCommand]
    private async Task ReportIncidentAsync()
    {
        if (string.IsNullOrWhiteSpace(IncidentTitle) || string.IsNullOrWhiteSpace(IncidentDescription)) { ErrorMessage = "Enter a title and what happened."; return; }
        IncidentDto? created = null;
        if (await RunAsync(async () => created = await api.PostAsync<IncidentDto>(api.BranchPath("security/incidents"),
                new CreateIncidentRequest(IncidentType, Severity, IncidentTitle.Trim(), IncidentDescription.Trim(), Clean(IncidentLocation), null, null, null))))
        {
            IncidentTitle = IncidentDescription = "";
            IncidentLocation = null;
            await LoadAsync();
            SelectedIncident = Incidents.FirstOrDefault(i => i.Id == created!.Id);
            StatusMessage = $"Incident {created!.IncidentNumber} logged.";
        }
    }

    private bool IncidentOpen() => CanManage && SelectedIncident is { Status: "Open" or "InProgress" };
    private bool IncidentResolved() => CanManage && SelectedIncident is { Status: "Resolved" };

    [RelayCommand(CanExecute = nameof(IncidentOpen))]
    private Task AddNoteAsync() => string.IsNullOrWhiteSpace(CaseNote) ? Fail("Write the note first.") : UpdateIncidentAsync("note", "Note added.");

    [RelayCommand(CanExecute = nameof(IncidentOpen))]
    private Task ResolveAsync() => string.IsNullOrWhiteSpace(CaseNote) ? Fail("Describe how it was resolved.") : UpdateIncidentAsync("resolve", "Incident resolved.");

    [RelayCommand(CanExecute = nameof(IncidentResolved))]
    private Task CloseIncidentAsync() => UpdateIncidentAsync("close", "Incident closed.");

    private Task Fail(string message) { ErrorMessage = message; return Task.CompletedTask; }

    private async Task UpdateIncidentAsync(string action, string done)
    {
        var incident = SelectedIncident!;
        if (await RunAsync(() => api.PostAsync<IncidentDto>(api.BranchPath($"security/incidents/{incident.Id}/actions"),
                new CaseUpdateRequest(action, null, Clean(CaseNote), incident.ConcurrencyStamp))))
        {
            CaseNote = null;
            await LoadAsync();
            StatusMessage = done;
        }
    }

    [RelayCommand]
    private async Task RecordItemAsync()
    {
        if (string.IsNullOrWhiteSpace(ItemDescription) || string.IsNullOrWhiteSpace(FoundLocation)) { ErrorMessage = "Describe the item and where it was found."; return; }
        LostItemDto? created = null;
        if (await RunAsync(async () => created = await api.PostAsync<LostItemDto>(api.BranchPath("security/lost-found"),
                new CreateLostItemRequest(ItemDescription.Trim(), null, FoundLocation.Trim(), null, null, StorageLocation.Trim(), IsValuable))))
        {
            ItemDescription = FoundLocation = "";
            IsValuable = false;
            await LoadAsync();
            StatusMessage = $"Item {created!.ItemNumber} logged into custody.";
        }
    }

    private bool ItemInCustody() => SelectedItem is { Status: "InCustody" };

    [RelayCommand(CanExecute = nameof(ItemInCustody))]
    private Task ReturnItemAsync() =>
        string.IsNullOrWhiteSpace(ReceivedBy) || string.IsNullOrWhiteSpace(IdentityCheck)
            ? Fail("Enter who collected the item and how their identity was checked.")
            : CustodyAsync(new CustodyRequest("return", null, ReceivedBy.Trim(), IdentityCheck.Trim(), null, SelectedItem!.ConcurrencyStamp), "returned");

    [RelayCommand(CanExecute = nameof(ItemInCustody))]
    private Task DisposeItemAsync() =>
        string.IsNullOrWhiteSpace(DisposalReason)
            ? Fail("Give the reason for disposal.")
            : CustodyAsync(new CustodyRequest("dispose", null, null, null, DisposalReason.Trim(), SelectedItem!.ConcurrencyStamp), "disposed of");

    private async Task CustodyAsync(CustodyRequest request, string done)
    {
        var item = SelectedItem!;
        if (request.Action == "dispose" && !dialogs.Confirm($"Dispose of {item.ItemNumber}?")) return;
        if (await RunAsync(() => api.PostAsync<LostItemDto>(api.BranchPath($"security/lost-found/{item.Id}/custody"), request)))
        {
            ReceivedBy = IdentityCheck = DisposalReason = null;
            await LoadAsync();
            StatusMessage = $"{item.ItemNumber} {done}.";
        }
    }

    [RelayCommand]
    private async Task SignInVisitorAsync()
    {
        if (string.IsNullOrWhiteSpace(VisitorName) || string.IsNullOrWhiteSpace(Purpose)) { ErrorMessage = "Enter the visitor's name and the purpose of the visit."; return; }
        if (await RunAsync(() => api.PostAsync<VisitorDto>(api.BranchPath("security/visitors"),
                new CreateVisitorRequest(VisitorName.Trim(), Clean(VisitorPhone), Clean(IdChecked), null, null, Purpose.Trim()))))
        {
            var name = VisitorName.Trim();
            VisitorName = Purpose = "";
            VisitorPhone = IdChecked = null;
            await LoadAsync();
            StatusMessage = $"{name} signed in.";
        }
    }

    private bool VisitorOnSite() => SelectedVisitor is { LeftAtUtc: null };

    [RelayCommand(CanExecute = nameof(VisitorOnSite))]
    private async Task SignOutVisitorAsync()
    {
        var visitor = SelectedVisitor!;
        if (await RunAsync(() => api.PostAsync<VisitorDto>(api.BranchPath($"security/visitors/{visitor.Id}/sign-out"), new { })))
        {
            await LoadAsync();
            StatusMessage = $"{visitor.VisitorName} signed out.";
        }
    }

    [RelayCommand]
    private async Task DeclareEmergencyAsync()
    {
        if (string.IsNullOrWhiteSpace(EmergencyDescription)) { ErrorMessage = "Describe the emergency."; return; }
        if (!dialogs.Confirm($"Declare a {EmergencyType} emergency? Managers are alerted at once.")) return;
        EmergencyDto? declared = null;
        if (await RunAsync(async () => declared = await api.PostAsync<EmergencyDto>(api.BranchPath("security/emergencies"),
                new DeclareEmergencyRequest(EmergencyType, EmergencyDescription.Trim()))))
        {
            EmergencyDescription = "";
            await LoadAsync();
            SelectedEmergency = Emergencies.FirstOrDefault(e => e.Id == declared!.Id);
            StatusMessage = "Emergency declared. Log each action as it happens.";
        }
    }

    private bool EmergencyActive() => SelectedEmergency is { EndedAtUtc: null };
    private bool CanEndEmergency() => CanManage && EmergencyActive();

    [RelayCommand(CanExecute = nameof(EmergencyActive))]
    private async Task LogActionAsync()
    {
        if (string.IsNullOrWhiteSpace(EmergencyAction)) { ErrorMessage = "Write what was done."; return; }
        var emergency = SelectedEmergency!;
        if (await RunAsync(() => api.PostAsync<EmergencyDto>(api.BranchPath($"security/emergencies/{emergency.Id}/actions"),
                new EmergencyActionRequest(EmergencyAction.Trim()))))
        {
            EmergencyAction = null;
            await LoadAsync();
            StatusMessage = "Action logged.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanEndEmergency))]
    private async Task EndEmergencyAsync()
    {
        if (string.IsNullOrWhiteSpace(EmergencySummary)) { ErrorMessage = "Write a short summary before ending the emergency."; return; }
        var emergency = SelectedEmergency!;
        if (await RunAsync(() => api.PostAsync<EmergencyDto>(api.BranchPath($"security/emergencies/{emergency.Id}/end"),
                new EndEmergencyRequest(EmergencySummary.Trim()))))
        {
            EmergencySummary = null;
            await LoadAsync();
            StatusMessage = "Emergency ended.";
        }
    }
}
