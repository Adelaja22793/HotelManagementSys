using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using HMS.Desktop.Services;

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

    /// <summary>Loads the screen's data. Called when the screen opens and by Refresh.</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;

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
