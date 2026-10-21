using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Branches;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels.Setup;

/// <summary>A line added to an action's list, such as one item on a purchase order.</summary>
public sealed record ActionLine(object Request, string Label);

/// <summary>
/// A task on a screen (Receive stock, Approve refund...): a button that opens a form built from the server's
/// request record. Actions about an existing record start with a pick list of those records (the target).
/// </summary>
public sealed partial class ActionForm : ObservableObject
{
    private readonly Func<Func<Task>, Task<bool>> _run;
    private readonly Func<Task> _afterSuccess;
    private ApiClient? _api;
    private readonly Dictionary<string, IReadOnlyList<FieldOption>> _lookups = [];
    private string _idempotencyKey = Guid.NewGuid().ToString();

    internal ActionForm(ActionDefinition definition, Func<Func<Task>, Task<bool>> run, Func<Task> afterSuccess)
    {
        Definition = definition;
        _run = run;
        _afterSuccess = afterSuccess;
        LinesTitle = FormBuilder.ListParameter(definition.RequestType) is { } list ? SetupEntity.Humanize(FormBuilder.Pascal(list.Parameter.Name!)) : null;
    }

    public ActionDefinition Definition { get; }
    public string Title => Definition.Title;
    public string? Description => Definition.Description;

    public ObservableCollection<FormField> Fields { get; } = [];
    public ObservableCollection<FormField> LineFields { get; } = [];
    public ObservableCollection<ActionLine> Lines { get; } = [];

    /// <summary>The record the action is about, when it needs one (the first field on the form).</summary>
    public FormField? TargetField { get; private set; }

    public string? LinesTitle { get; }
    public bool HasLines => LinesTitle is not null;

    [ObservableProperty] private string? _error;
    [ObservableProperty] private ActionLine? _selectedLine;

    /// <summary>Loads the pick lists and shows an empty form.</summary>
    public async Task OpenAsync(ApiClient api)
    {
        _api = api;
        _idempotencyKey = Guid.NewGuid().ToString();
        Error = null;
        Lines.Clear();
        _lookups.Clear();
        var sources = Definition.Fields.Values.Concat(Definition.LineFields.Values).Select(f => f.Lookup).Append(Definition.Target)
            .OfType<LookupSource>().Distinct();
        foreach (var source in sources) _lookups[source.Title] = await source.LoadAsync(api, null);

        Fields.Clear();
        if (Definition.Target is { } target)
        {
            if (TargetField is not null) TargetField.PropertyChanged -= OnTargetChanged;
            TargetField = new FormField("__target", target.Title.TrimEnd('s'), FieldKind.Lookup, typeof(Guid), false);
            FormBuilder.SetOptions(TargetField, _lookups[target.Title]);
            TargetField.PropertyChanged += OnTargetChanged;
            Fields.Add(TargetField);
            if (TargetField.Options.Count == 0) Error = Definition.EmptyTargetMessage ?? $"There are no {target.Title.ToLowerInvariant()} to choose from.";
        }
        var today = await BusinessDateAsync(api);
        foreach (var f in FormBuilder.BuildFields(Definition.RequestType, Definition.Fields, null, _lookups, today)) Fields.Add(f);
        if (FormBuilder.ListParameter(Definition.RequestType) is { } list)
            FrontDeskPageViewModel.Replace(LineFields, FormBuilder.BuildFields(list.ItemType, Definition.LineFields, null, _lookups, today));
        RefreshFromTarget();
    }

    /// <summary>Dates start on the hotel's business date, which can differ from this PC's date before the night audit runs.</summary>
    internal static async Task<DateOnly?> BusinessDateAsync(ApiClient api)
    {
        try
        {
            return (await api.GetAsync<BranchDto>(api.BranchPath(""))).CurrentBusinessDate;
        }
        catch (ApiException)
        {
            return null; // not allowed to read the branch: fall back to today's date
        }
    }

    private void OnTargetChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FormField.Selected)) RefreshFromTarget();
    }

    private object? TargetRecord => TargetField?.Selected?.Record;

    // Pick lists that come from the chosen record, such as its order lines, and values to start from.
    private void RefreshFromTarget()
    {
        Lines.Clear();
        var target = TargetRecord;
        foreach (var field in Fields.Concat(LineFields))
        {
            var spec = Definition.Fields.GetValueOrDefault(field.Name) ?? Definition.LineFields.GetValueOrDefault(field.Name);
            if (spec?.FromTarget is { } from) FormBuilder.SetOptions(field, target is null ? [] : from(target));
            if (target is not null && Definition.PrefillFromTarget && field != TargetField && SetupEntity.HasProperty(target, field.Name))
                field.SetValue(SetupEntity.Read(target, field.Name));
        }
    }

    [RelayCommand]
    private void AddLine()
    {
        var list = FormBuilder.ListParameter(Definition.RequestType)!.Value;
        var line = FormBuilder.BuildRequest(list.ItemType, Definition.LineFields, null, LineFields, null, out var error);
        if (line is null)
        {
            Error = error;
            return;
        }
        Error = null;
        var label = string.Join(" · ", LineFields.Select(f => f.IsPicker ? f.Selected?.Label : f.IsYesNo ? null : f.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        Lines.Add(new ActionLine(line, label));
        foreach (var f in LineFields) if (!f.IsPicker) f.SetValue(null);
    }

    [RelayCommand]
    private void RemoveLine()
    {
        if (SelectedLine is not null) Lines.Remove(SelectedLine);
    }

    /// <summary>Sends the form. Returns true when the server accepted it.</summary>
    public async Task<bool> SubmitAsync()
    {
        Error = null;
        if (Definition.Target is not null && TargetRecord is null)
        {
            Error = $"Choose the {TargetField!.Label.ToLowerInvariant()}.";
            return false;
        }
        IList? list = null;
        if (FormBuilder.ListParameter(Definition.RequestType) is { } lp)
        {
            if (Lines.Count == 0 && LineFields.Count > 0 && FormBuilder.BuildRequest(lp.ItemType, Definition.LineFields, null, LineFields, null, out _) is not null)
                AddLine(); // the user filled in one line and pressed the main button
            list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(lp.ItemType))!;
            foreach (var line in Lines) list.Add(line.Request);
            if (list.Count == 0 && !Definition.LinesOptional)
            {
                Error = $"Add at least one line to {LinesTitle!.ToLowerInvariant()}.";
                return false;
            }
        }
        var request = FormBuilder.BuildRequest(Definition.RequestType, Definition.Fields, TargetRecord, Fields.Where(f => f != TargetField).ToList(), list, out var error);
        if (request is null)
        {
            Error = error;
            return false;
        }
        if (Definition.Validate?.Invoke(request) is { } invalid)
        {
            Error = invalid;
            return false;
        }

        var api = _api ?? throw new InvalidOperationException("Open the form first.");
        var path = Definition.Path.Replace("{id}", TargetRecord is null ? null : Convert.ToString(SetupEntity.Read(TargetRecord, "Id"), System.Globalization.CultureInfo.InvariantCulture));
        path = path.StartsWith("api/", StringComparison.Ordinal) ? path : api.BranchPath(path);
        var ok = await _run(async () =>
        {
            if (Definition.UsePut) await api.PutAsync<object>(path, request);
            else await api.PostAsync<object>(path, request, _idempotencyKey);
        });
        if (ok) await _afterSuccess();
        return ok;
    }
}

/// <summary>What an action sends, where, and how its form looks.</summary>
public sealed class ActionDefinition
{
    public required string Title { get; init; }
    public required string Permission { get; init; }
    public required Type RequestType { get; init; }
    /// <summary>Branch-relative route, or one starting with "api/". "{id}" is the target's id.</summary>
    public required string Path { get; init; }
    public string? Description { get; init; }
    public string? SuccessMessage { get; init; }
    public bool UsePut { get; init; }
    public LookupSource? Target { get; init; }
    public string? EmptyTargetMessage { get; init; }
    public bool PrefillFromTarget { get; init; }
    public bool LinesOptional { get; init; }
    public Func<object, string?>? Validate { get; init; }
    public Dictionary<string, FieldSpec> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FieldSpec> LineFields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ActionDefinition Field(string name, Action<FieldSpec> configure) => Configure(Fields, name, configure);
    public ActionDefinition Line(string name, Action<FieldSpec> configure) => Configure(LineFields, name, configure);

    private ActionDefinition Configure(Dictionary<string, FieldSpec> specs, string name, Action<FieldSpec> configure)
    {
        if (!specs.TryGetValue(name, out var spec)) specs[name] = spec = new FieldSpec();
        configure(spec);
        return this;
    }
}
