using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Branches;
using HMS.Contracts.Common;
using HMS.Contracts.Users;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>A tick box in a list, such as a role or branch to grant.</summary>
public sealed partial class Choice<T>(T value, string label) : ObservableObject
{
    public T Value { get; } = value;
    public string Label { get; } = label;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Staff accounts: create a user with a one-time password, and change roles, branches or active state.
/// The API refuses to grant a role the signed-in user does not hold, so managers cannot create administrators.
/// </summary>
public sealed partial class UsersPageViewModel(ApiClient api, SessionState session, IDialogService dialogs) : PageViewModel
{
    public override string Title => "Users";

    public ObservableCollection<UserDto> Users { get; } = [];
    public ObservableCollection<Choice<string>> Roles { get; } = [];
    public ObservableCollection<Choice<BranchSummaryDto>> Branches { get; } = [];

    [ObservableProperty] private string? _search;
    [ObservableProperty] private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    [NotifyCanExecuteChangedFor(nameof(SaveAccessCommand))]
    private UserDto? _selected;

    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _temporaryPassword = "";
    [ObservableProperty] private bool _isActive = true;

    public bool IsNew => Selected is null;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        if (Roles.Count == 0)
            FrontDeskPageViewModel.Replace(Roles, (await api.GetAsync<List<string>>("api/v1/roles")).Select(r => new Choice<string>(r, r)));
        if (Branches.Count == 0)
        {
            FrontDeskPageViewModel.Replace(Branches, session.AvailableBranches.Select(b => new Choice<BranchSummaryDto>(b, $"{b.Code} {b.Name}")));
            foreach (var branch in Branches) branch.IsSelected = branch.Value.Id == session.Branch?.Id; // a new user starts in this branch
        }
        var page = await api.GetAsync<PagedResult<UserDto>>(Query.Build("api/v1/users", ("search", Search?.Trim()), ("pageSize", 200)));
        FrontDeskPageViewModel.Replace(Users, page.Items);
        TotalCount = page.TotalCount;
        if (Selected is not null) Selected = Users.FirstOrDefault(u => u.Id == Selected.Id);
    });

    [RelayCommand]
    private Task SearchAsync() => LoadAsync();

    partial void OnSelectedChanged(UserDto? value)
    {
        Email = value?.Email ?? "";
        FullName = value?.FullName ?? "";
        TemporaryPassword = "";
        IsActive = value?.IsActive ?? true;
        foreach (var role in Roles) role.IsSelected = value?.Roles.Contains(role.Value) == true;
        foreach (var branch in Branches)
            branch.IsSelected = value is null ? branch.Value.Id == session.Branch?.Id : value.Branches.Any(b => b.BranchId == branch.Value.Id);
    }

    [RelayCommand]
    private void New() => Selected = null;

    private (List<string> Roles, List<Guid> Branches, Guid? Default)? Access()
    {
        var roles = Roles.Where(r => r.IsSelected).Select(r => r.Value).ToList();
        var branches = Branches.Where(b => b.IsSelected).Select(b => b.Value.Id).ToList();
        if (roles.Count == 0) { ErrorMessage = "Tick at least one role."; return null; }
        if (branches.Count == 0) { ErrorMessage = "Tick at least one branch."; return null; }
        var current = session.Branch?.Id;
        return (roles, branches, current is { } c && branches.Contains(c) ? c : branches[0]);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(FullName)) { ErrorMessage = "Enter the email address and full name."; return; }
        if (TemporaryPassword.Length < 12) { ErrorMessage = "The one-time password must be at least 12 characters."; return; }
        if (Access() is not { } access) return;
        UserDto? created = null;
        if (await RunAsync(async () => created = await api.PostAsync<UserDto>("api/v1/users",
                new CreateUserRequest(Email.Trim(), FullName.Trim(), TemporaryPassword, access.Roles, access.Branches, access.Default))))
        {
            await LoadAsync();
            Selected = Users.FirstOrDefault(u => u.Id == created!.Id) ?? created;
            StatusMessage = $"{created!.FullName} can now sign in and must change the password at first sign-in.";
        }
    }

    [RelayCommand(CanExecute = nameof(IsExisting))]
    private async Task SaveAccessAsync()
    {
        if (Access() is not { } access) return;
        var user = Selected!;
        if (!IsActive && user.IsActive && !dialogs.Confirm($"Deactivate {user.FullName}? They are signed out everywhere.")) return;
        if (await RunAsync(() => api.PutAsync<UserDto>($"api/v1/users/{Uri.EscapeDataString(user.Id)}/access",
                new UpdateUserAccessRequest(access.Roles, access.Branches, access.Default, IsActive))))
        {
            await LoadAsync();
            StatusMessage = $"Access for {user.FullName} updated.";
        }
    }

    private bool IsExisting() => Selected is not null;
}
