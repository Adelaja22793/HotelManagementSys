using System.Net.Mail;
using CommunityToolkit.Mvvm.ComponentModel;
using HMS.Desktop.Services.LocalServer;

namespace HMS.Desktop.ViewModels;

/// <summary>
/// The window shown when HMS opens in Local mode: the one-time hotel setup on the first run, then a short
/// "starting" screen while the built-in server comes up. Raises <see cref="Started"/> when staff can sign in.
/// </summary>
public sealed partial class StartupViewModel(LocalServer server) : ObservableObject
{
    public const string LocalDbDownloadUrl = "https://learn.microsoft.com/sql/database-engine/configure-windows/sql-server-express-localdb";

    [ObservableProperty] private bool _isSetupNeeded = server.NeedsSetup;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _canRetry;
    [ObservableProperty] private bool _showLocalDbHelp;
    [ObservableProperty] private string? _details;

    [ObservableProperty] private string _hotelName = string.Empty;
    [ObservableProperty] private string _branchName = "Main Branch";
    [ObservableProperty] private string _adminFullName = string.Empty;
    [ObservableProperty] private string _adminEmail = string.Empty;

    private LocalSetup? _pendingSetup;

    public event EventHandler? Started;

    /// <summary>The administrator's email after a first run, so the sign-in screen can fill it in.</summary>
    public string? SignInEmail { get; private set; }

    /// <summary>Checks the first-run form and starts the server with it. The passwords come from PasswordBoxes.</summary>
    public Task SubmitSetupAsync(string password, string confirmPassword)
    {
        var error = Validate(password, confirmPassword);
        if (error is not null)
        {
            ErrorMessage = error;
            return Task.CompletedTask;
        }
        _pendingSetup = new LocalSetup(HotelName.Trim(), BranchName.Trim(), AdminFullName.Trim(), AdminEmail.Trim(), password);
        return StartAsync();
    }

    /// <summary>Starts the server (again, after a failure). On the first run, use <see cref="SubmitSetupAsync"/>.</summary>
    public async Task StartAsync()
    {
        if (IsBusy) return;
        if (IsSetupNeeded && _pendingSetup is null) return;

        IsBusy = true;
        CanRetry = false;
        ErrorMessage = null;
        ShowLocalDbHelp = false;
        Details = null;
        Status = IsSetupNeeded ? "Setting up your hotel. The first start can take a minute..." : "Starting HMS...";
        try
        {
            await server.StartAsync(IsSetupNeeded ? _pendingSetup : null);
            if (IsSetupNeeded) SignInEmail = _pendingSetup!.AdminEmail;
            _pendingSetup = null;
            IsSetupNeeded = false;
            Status = "Ready.";
            Started?.Invoke(this, EventArgs.Empty);
        }
        catch (LocalServerException ex)
        {
            Status = null;
            ShowLocalDbHelp = ex.IsDatabaseUnavailable;
            ErrorMessage = ex.IsDatabaseUnavailable
                ? "HMS keeps its data in Microsoft SQL Server Express LocalDB, which was not found on this PC. Install LocalDB, then try again."
                : ex.Message;
            Details = string.Join(Environment.NewLine, ex.Output.TakeLast(15));
            CanRetry = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? Validate(string password, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(HotelName)) return "Enter the hotel's name.";
        if (string.IsNullOrWhiteSpace(BranchName)) return "Enter a name for this branch.";
        if (string.IsNullOrWhiteSpace(AdminFullName)) return "Enter the administrator's full name.";
        if (!MailAddress.TryCreate(AdminEmail.Trim(), out _)) return "Enter a valid email address for the administrator.";
        if (password.Length < 12) return "The password must be at least 12 characters.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            return "The password needs an uppercase letter, a lowercase letter and a number.";
        if (password != confirmPassword) return "The two passwords do not match.";
        return null;
    }
}
