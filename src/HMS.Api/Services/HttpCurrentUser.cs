using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HMS.Application.Abstractions;
using HMS.Infrastructure.Identity;

namespace HMS.Api.Services;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? UserId => IsAuthenticated
        ? Principal!.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? Principal!.FindFirstValue(ClaimTypes.NameIdentifier)
        : null;

    public Guid? CompanyId =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(HmsClaimTypes.CompanyId), out var id) ? id : null;

    public IReadOnlyCollection<string> Roles =>
        IsAuthenticated ? Principal!.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() : [];

    public string? TraceId => accessor.HttpContext?.TraceIdentifier;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}
