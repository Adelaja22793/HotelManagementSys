using HMS.Application.Abstractions;
using HMS.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HMS.Api.Controllers;

[Route(V1 + "/auth")]
public sealed class AuthController(IIdentityService identity) : ApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public Task<LoginResponse> Login(LoginRequest request, CancellationToken ct) => identity.LoginAsync(request, ct);

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public Task<LoginResponse> Refresh(RefreshRequest request, CancellationToken ct) => identity.RefreshAsync(request, ct);

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        await identity.LogoutAsync(request, ct);
        return NoContent();
    }

    [HttpGet("me")]
    public Task<CurrentUserDto> Me(CancellationToken ct) => identity.GetCurrentUserAsync(ct);

    [HttpPost("change-password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await identity.ChangePasswordAsync(request, ct);
        return NoContent();
    }
}
