using HMS.Api.Authorization;
using HMS.Application.Cashier;
using HMS.Application.Security;
using HMS.Contracts.Cashier;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/cashier/shifts")]
public sealed class CashierController(ICashierService cashier) : ApiControllerBase
{
    [HttpPost]
    [HasPermission(Permissions.CashierOperate)]
    public async Task<ActionResult<CashierShiftDto>> Open(Guid branchId, OpenShiftRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await cashier.OpenAsync(branchId, request, ct));

    [HttpGet("current")]
    [HasPermission(Permissions.CashierOperate)]
    public async Task<ActionResult<CashierShiftDto>> Current(Guid branchId, CancellationToken ct) =>
        await cashier.GetCurrentAsync(branchId, ct) is { } shift ? shift : NoContent();

    [HttpGet]
    [HasPermission(Permissions.CashierOperate)]
    public Task<IReadOnlyList<CashierShiftDto>> List(Guid branchId, [FromQuery] string? status, CancellationToken ct) => cashier.ListAsync(branchId, status, ct);

    [HttpGet("{shiftId:guid}")]
    [HasPermission(Permissions.CashierOperate)]
    public Task<CashierShiftDto> Get(Guid branchId, Guid shiftId, CancellationToken ct) => cashier.GetAsync(branchId, shiftId, ct);

    [HttpPost("{shiftId:guid}/close")]
    [HasPermission(Permissions.CashierOperate)]
    public Task<CashierShiftDto> Close(Guid branchId, Guid shiftId, CloseShiftRequest request, CancellationToken ct) =>
        cashier.CloseAsync(branchId, shiftId, request, ct);

    [HttpPost("{shiftId:guid}/approve-variance")]
    [HasPermission(Permissions.CashierApproveVariance)]
    public Task<CashierShiftDto> ApproveVariance(Guid branchId, Guid shiftId, ApproveVarianceRequest request, CancellationToken ct) =>
        cashier.ApproveVarianceAsync(branchId, shiftId, request, ct);
}
