using HMS.Api.Authorization;
using HMS.Application.Abstractions;
using HMS.Application.Security;
using HMS.Contracts.Common;
using HMS.Contracts.Users;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/users")]
[HasPermission(Permissions.UsersManage)]
public sealed class UsersController(IIdentityService identity) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserDto>> List([FromQuery] PageQuery query, CancellationToken ct) => identity.ListUsersAsync(query, ct);

    [HttpGet("{userId}")]
    public Task<UserDto> Get(string userId, CancellationToken ct) => identity.GetUserAsync(userId, ct);

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await identity.CreateUserAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { userId = user.Id }, user);
    }

    [HttpPut("{userId}/access")]
    public Task<UserDto> UpdateAccess(string userId, UpdateUserAccessRequest request, CancellationToken ct) =>
        identity.UpdateUserAccessAsync(userId, request, ct);

    [HttpGet("~/" + V1 + "/roles")]
    public IReadOnlyList<string> Roles() => HMS.Application.Security.Roles.All;
}
