using HMS.Api.Authorization;
using HMS.Application.Guests;
using HMS.Application.Security;
using HMS.Contracts.Common;
using HMS.Contracts.Guests;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/guests")]
public sealed class GuestsController(IGuestService guests) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.GuestsView)]
    public Task<PagedResult<GuestSummaryDto>> Search(Guid branchId, [FromQuery] PageQuery query, CancellationToken ct) => guests.SearchAsync(branchId, query, ct);

    [HttpGet("{guestId:guid}")]
    [HasPermission(Permissions.GuestsView)]
    public Task<GuestDto> Get(Guid branchId, Guid guestId, CancellationToken ct) => guests.GetAsync(branchId, guestId, ct);

    [HttpPost]
    [HasPermission(Permissions.GuestsManage)]
    public async Task<ActionResult<GuestDto>> Create(Guid branchId, SaveGuestRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await guests.CreateAsync(branchId, request, ct));

    [HttpPut("{guestId:guid}")]
    [HasPermission(Permissions.GuestsManage)]
    public Task<GuestDto> Update(Guid branchId, Guid guestId, SaveGuestRequest request, CancellationToken ct) => guests.UpdateAsync(branchId, guestId, request, ct);
}
