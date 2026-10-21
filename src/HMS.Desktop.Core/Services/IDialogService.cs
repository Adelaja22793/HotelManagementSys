namespace HMS.Desktop.Services;

public interface IDialogService
{
    void ShowError(string message, string? traceId = null);
    void ShowInfo(string message);
    bool Confirm(string message) => false;
}
