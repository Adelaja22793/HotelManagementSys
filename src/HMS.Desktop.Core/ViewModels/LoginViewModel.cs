using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Branches;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Sign-in followed by branch selection. Raises <see cref="Completed"/> when a branch is chosen.</summary>
public sealed partial class LoginViewModel(ApiClient api, SessionState session) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _email = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isSelectingBranch;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private BranchSummaryDto? _selectedBranch;

    public ObservableCollection<BranchSummaryDto> Branches { get; } = [];

    public event EventHandler? Completed;

    private bool CanLogin(string? password) => !IsBusy && !string.IsNullOrWhiteSpace(Email);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(string? password)
    {
        ErrorMessage = null;
        if (string.IsNullOrEmpty(password))
        {
            ErrorMessage = "Enter your password.";
            return;
        }

        IsBusy = true;
        try
        {
            var login = await api.LoginAsync(Email.Trim(), password);
            Branches.Clear();
            foreach (var b in login.Branches) Branches.Add(b);

            if (Branches.Count == 0)
            {
                ErrorMessage = "Your account is not assigned to any active branch. Contact your administrator.";
                session.Clear();
                return;
            }

            SelectedBranch = Branches.FirstOrDefault(b => b.IsDefault) ?? Branches[0];
            if (Branches.Count == 1) Continue();
            else IsSelectingBranch = true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Cannot reach the hotel server. Check the network connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanContinue() => SelectedBranch is not null;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        session.Branch = SelectedBranch;
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
