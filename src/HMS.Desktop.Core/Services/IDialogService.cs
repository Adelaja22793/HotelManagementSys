namespace HMS.Desktop.Services;

public interface IDialogService
{
    void ShowError(string message, string? traceId = null);
    void ShowInfo(string message);
    bool Confirm(string message) => false;

    /// <summary>Asks for a file to upload. Null when the user cancels.</summary>
    string? PickFileToOpen() => null;

    /// <summary>Asks where to save a downloaded file. Null when the user cancels.</summary>
    string? PickFileToSave(string suggestedName) => null;
}
