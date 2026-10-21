using System.Windows;

namespace HMS.Desktop.Services;

internal sealed class DialogService : IDialogService
{
    public void ShowError(string message, string? traceId = null) =>
        MessageBox.Show(traceId is null ? message : $"{message}\n\nReference: {traceId}", "HMS", MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInfo(string message) =>
        MessageBox.Show(message, "HMS", MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string message) =>
        MessageBox.Show(message, "HMS", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
