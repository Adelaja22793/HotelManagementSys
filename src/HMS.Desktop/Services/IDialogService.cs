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

    public string? PickFileToOpen()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Documents and images|*.pdf;*.png;*.jpg;*.jpeg;*.docx;*.xlsx;*.txt;*.csv|All files|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFileToSave(string suggestedName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = suggestedName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
