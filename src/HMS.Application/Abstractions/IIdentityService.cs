using HMS.Contracts.Auth;
using HMS.Contracts.Common;
using HMS.Contracts.Users;

namespace HMS.Application.Abstractions;

/// <summary>Staff account operations backed by ASP.NET Core Identity.</summary>
public interface IIdentityService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<UserDto>> ListUsersAsync(PageQuery query, CancellationToken cancellationToken = default);
    Task<UserDto> GetUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<UserDto> UpdateUserAccessAsync(string userId, UpdateUserAccessRequest request, CancellationToken cancellationToken = default);
}
