using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Desktop.Services;
using HMS.Desktop.ViewModels.Setup;

namespace HMS.Desktop.ViewModels;

/// <summary>A record in the Setup list: the server's record and the text shown in each column.</summary>
public sealed record SetupRow(object Record, IReadOnlyList<string> Cells);

/// <summary>
/// Setup: everything a hotel configures once and maintains, from room types and taxes to departments,
/// stock items and menus. Pick what to set up on the left, then add a new record or select one to change it.
/// </summary>
public sealed partial class SetupPageViewModel(ApiClient api, SessionState session) : PageViewModel
{
    private readonly Dictionary<string, IReadOnlyList<FieldOption>> _lookups = [];

    public override string Title => "Setup";

    protected override IEnumerable<HMS.Desktop.ViewModels.Setup.ActionDefinition> ActionDefinitions => HMS.Desktop.ViewModels.Setup.ActionCatalog.Setup;

    public IReadOnlyList<SetupEntity> Entities { get; } = SetupCatalog.All;

    public ObservableCollection<FieldOption> ParentOptions { get; } = [];
    public ObservableCollection<SetupRow> Rows { get; } = [];
    public ObservableCollection<string> Columns { get; } = [];
    public ObservableCollection<FormField> Form { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParent), nameof(ParentTitle), nameof(CanManage), nameof(ReadOnlyNote))]
    [NotifyCanExecuteChangedFor(nameof(NewCommand))]
    private SetupEntity? _selectedEntity;

    [ObservableProperty] private FieldOption? _selectedParent;
    [ObservableProperty] private SetupRow? _selectedRow;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(IsFormOpen))]
    private string? _formTitle;

    private object? _editing;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    /// <summary>Completes when the loads started by picking a kind or parent have finished.</summary>
    public Task WhenLoaded { get; private set; } = Task.CompletedTask;

    public bool HasParent => SelectedEntity?.Parent is not null;
    public string? ParentTitle => SelectedEntity?.Parent?.Title switch { null => null, var t => t.TrimEnd('s') };
    public bool IsFormOpen => FormTitle is not null;
    public bool CanManage => SelectedEntity is not null && session.HasPermission(SelectedEntity.Permission);
    public string? ReadOnlyNote => SelectedEntity is not null && !CanManage ? "You can view these but not change them." : null;

    public override async Task LoadAsync()
    {
        if (SelectedEntity is null)
        {
            SelectedEntity = Entities[0];
            await WhenLoaded;
        }
        else await ReloadAsync();
    }

    partial void OnSelectedEntityChanged(SetupEntity? value)
    {
        CloseForm();
        Rows.Clear();
        ParentOptions.Clear();
        SelectedParent = null;
        if (value is not null) WhenLoaded = ReloadAsync();
    }

    partial void OnSelectedParentChanged(FieldOption? value)
    {
        CloseForm();
        if (value is not null && !_settingParent) WhenLoaded = LoadRowsAsync();
    }

    private bool _settingParent;

    partial void OnSelectedRowChanged(SetupRow? value)
    {
        if (value is not null && SelectedEntity?.CanEdit == true && CanManage) OpenForm(value.Record);
    }

    /// <summary>Reloads the pick lists and the records for the selected kind.</summary>
    public Task ReloadAsync() => RunAsync(async () =>
    {
        await _loadLock.WaitAsync();
        try { await ReloadCoreAsync(); }
        finally { _loadLock.Release(); }
    });

    private async Task ReloadCoreAsync()
    {
        var entity = SelectedEntity;
        if (entity is null) return;
        _lookups.Clear();
        if (entity.Parent is { } parent)
        {
            var options = await parent.LoadAsync(api, null);
            _lookups[parent.Title] = options;
            var keep = SelectedParent?.Value;
            FrontDeskPageViewModel.Replace(ParentOptions, options);
            _settingParent = true;
            try { SelectedParent = ParentOptions.FirstOrDefault(o => Equals(o.Value, keep)) ?? ParentOptions.FirstOrDefault(); }
            finally { _settingParent = false; }
            if (SelectedParent is null) StatusMessage = $"Add a {parent.Title.TrimEnd('s').ToLowerInvariant()} first.";
            else await LoadRowsCoreAsync(entity);
            return;
        }
        await LoadRowsCoreAsync(entity);
    }

    private Task LoadRowsAsync() => RunAsync(async () =>
    {
        await _loadLock.WaitAsync();
        try { if (SelectedEntity is { } entity) await LoadRowsCoreAsync(entity); }
        finally { _loadLock.Release(); }
    });

    private async Task LoadRowsCoreAsync(SetupEntity entity)
    {
        var parentId = SelectedParent?.Value?.ToString();
        foreach (var lookup in entity.Fields.Values.Select(f => f.Lookup).OfType<LookupSource>().Distinct())
            _lookups[lookup.Title] = await lookup.LoadAsync(api, parentId);

        var records = await api.GetListAsync(api.BranchPath(entity.Route(entity.ListPath, parentId, null)), entity.DtoType, entity.SingleRecord);
        if (entity != SelectedEntity) return; // the user moved on while this loaded
        var columns = entity.ColumnsFor();
        FrontDeskPageViewModel.Replace(Columns, columns.Select(c => c.Header));
        FrontDeskPageViewModel.Replace(Rows, records.Select(r => new SetupRow(r, [.. columns.Select(c => entity.Cell(r, c.Property, _lookups))])));
        if (records.Count == 0 && entity.CanCreate) StatusMessage = $"No {entity.Title.ToLowerInvariant()} yet. Click \"New\" to add one.";
    }

    private bool CanNew() => SelectedEntity is { CanCreate: true } && CanManage && (!HasParent || SelectedParent is not null);

    [RelayCommand(CanExecute = nameof(CanNew))]
    private void New()
    {
        SelectedRow = null;
        OpenForm(null);
    }

    private void OpenForm(object? record)
    {
        var entity = SelectedEntity!;
        _editing = record;
        FrontDeskPageViewModel.Replace(Form, entity.BuildForm(record, _lookups));
        FormTitle = record is null ? $"New {entity.Singular}" : $"Change {entity.Singular}: {SetupEntity.DefaultLabel(record)}";
        ErrorMessage = null;
    }

    [RelayCommand]
    private void CloseForm()
    {
        _editing = null;
        Form.Clear();
        FormTitle = null;
    }

    private bool CanSave() => IsFormOpen;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var entity = SelectedEntity!;
        var request = entity.BuildRequest(_editing, Form, out var error);
        if (request is null)
        {
            ErrorMessage = error;
            return;
        }
        var parentId = SelectedParent?.Value?.ToString();
        var editing = _editing;
        var saved = await RunAsync(async () =>
        {
            if (editing is null || entity.SaveCreatesVersion)
            {
                var path = api.BranchPath(entity.Route(entity.CreatePath, parentId, null));
                if (entity.CreateWithPut) await api.PutAsync<object>(path, request);
                else await api.PostAsync<object>(path, request);
            }
            else
                await api.PutAsync<object>(api.BranchPath(entity.Route(entity.UpdatePath!, parentId, editing)), request);
        });
        if (!saved) return;
        var title = FormTitle;
        CloseForm();
        await LoadRowsAsync();
        StatusMessage = $"Saved. {title}";
    }
}
