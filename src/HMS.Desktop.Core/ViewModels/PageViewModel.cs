using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Desktop.Services;
using HMS.Desktop.ViewModels.Setup;

namespace HMS.Desktop.ViewModels;

/// <summary>
/// Base for every screen in the main window: a title, a busy flag, and one place that turns API failures into
/// plain messages. Screens never show stack traces; the trace id is kept for support.
/// </summary>
public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _traceId;

    /// <summary>
    /// Buttons whose command checks <see cref="IsBusy"/> only look again when told to. Without this, a button that
    /// became usable while the screen was busy (for example Book after checking availability) stayed greyed out.
    /// </summary>
    partial void OnIsBusyChanged(bool value)
    {
        foreach (var property in GetType().GetProperties())
            if (typeof(IRelayCommand).IsAssignableFrom(property.PropertyType) && property.GetIndexParameters().Length == 0)
                (property.GetValue(this) as IRelayCommand)?.NotifyCanExecuteChanged();
    }

    /// <summary>Loads the screen's data. Called when the screen opens and by Refresh.</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;

    // ---- Actions: the task buttons above a screen, each opening a form beside it. ----

    private ApiClient? _actionsApi;

    /// <summary>The tasks this screen offers. The ones the user may not do are left out.</summary>
    protected virtual IEnumerable<ActionDefinition> ActionDefinitions => [];

    public ObservableCollection<ActionForm> Actions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActionOpen))]
    private ActionForm? _openAction;

    public bool IsActionOpen => OpenAction is not null;

    /// <summary>Called by the main window when the screen opens.</summary>
    public void UseActions(ApiClient api, SessionState session)
    {
        _actionsApi = api;
        Actions.Clear();
        foreach (var definition in ActionDefinitions.Where(d => session.HasPermission(d.Permission)))
            Actions.Add(new ActionForm(definition, action => RunAsync(action), () => AfterActionAsync(definition)));
    }

    public ActionForm Action(string title) => Actions.Single(a => a.Title == title);

    [RelayCommand]
    public async Task OpenActionAsync(ActionForm? form)
    {
        if (form is null || _actionsApi is null) return;
        OpenAction = form;
        await RunAsync(() => form.OpenAsync(_actionsApi));
    }

    [RelayCommand]
    private void CloseAction() => OpenAction = null;

    [RelayCommand]
    private async Task SubmitActionAsync()
    {
        if (OpenAction is { } form) await form.SubmitAsync();
    }

    private async Task AfterActionAsync(ActionDefinition definition)
    {
        OpenAction = null;
        await LoadAsync();
        StatusMessage = definition.SuccessMessage ?? $"{definition.Title}: done.";
    }

    /// <summary>Runs an API action with the busy flag and error handling. Returns true when it succeeded.</summary>
    protected async Task<bool> RunAsync(Func<Task> action, string? successMessage = null)
    {
        ErrorMessage = null;
        StatusMessage = null;
        TraceId = null;
        IsBusy = true;
        try
        {
            await action();
            StatusMessage = successMessage;
            return true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = Describe(ex);
            TraceId = ex.Error?.TraceId;
            return false;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Cannot reach the server. Check the network connection and try again.";
            return false;
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = "The server took too long to answer. Try again.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public static string Describe(ApiException ex)
    {
        var message = ex.Message;
        if (ex.Error?.Errors is { Count: > 0 } fields)
            message += " " + string.Join(" ", fields.SelectMany(f => f.Value));
        return ex.Status switch
        {
            HttpStatusCode.Forbidden => "You do not have permission for this. " + message,
            HttpStatusCode.Conflict when ex.Code == "CONCURRENCY_CONFLICT" => "Someone else changed this record. Refresh and try again.",
            _ => message,
        };
    }
}
