using CommunityToolkit.Mvvm.ComponentModel;
using HMS.Contracts.Auth;
using HMS.Contracts.Branches;

namespace HMS.Desktop.Services;

/// <summary>In-memory session for the signed-in workstation user. Tokens are never written to disk.</summary>
public sealed partial class SessionState : ObservableObject
{
    [ObservableProperty] private CurrentUserDto? _user;
    [ObservableProperty] private BranchSummaryDto? _branch;
    [ObservableProperty] private IReadOnlyList<BranchSummaryDto> _availableBranches = [];

    public string? AccessToken { get; private set; }
    public DateTime AccessTokenExpiresAtUtc { get; private set; }
    public string? RefreshToken { get; private set; }

    public bool HasPermission(string permission) => User?.Permissions.Contains(permission) == true;

    public void Apply(LoginResponse login)
    {
        AccessToken = login.AccessToken;
        AccessTokenExpiresAtUtc = login.AccessTokenExpiresAtUtc;
        RefreshToken = login.RefreshToken;
        User = login.User;
        AvailableBranches = login.Branches;
        if (Branch is not null && login.Branches.All(b => b.Id != Branch.Id)) Branch = null;
    }

    public void Clear()
    {
        AccessToken = null;
        RefreshToken = null;
        User = null;
        Branch = null;
        AvailableBranches = [];
    }
}
