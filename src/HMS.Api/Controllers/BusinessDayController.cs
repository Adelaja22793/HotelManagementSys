using HMS.Api.Authorization;
using HMS.Application.BusinessDay;
using HMS.Application.Security;
using HMS.Contracts.BusinessDay;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

/// <summary>Night audit: checks, exceptions, daily snapshot and moving the branch to the next business date.</summary>
[Route(V1 + "/branches/{branchId:guid}/business-day")]
public sealed class BusinessDayController(IBusinessDayService businessDay) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.ReportsView)]
    public Task<BusinessDayStatusDto> Status(Guid branchId, CancellationToken ct) => businessDay.StatusAsync(branchId, ct);

    [HttpGet("closes")]
    [HasPermission(Permissions.ReportsView)]
    public Task<IReadOnlyList<BusinessDayCloseDto>> Closes(Guid branchId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        businessDay.ListAsync(branchId, from, to, ct);

    /// <summary>Runs the checks and closes the day if nothing blocks it. Returns Status "Blocked" with the exceptions otherwise.</summary>
    [HttpPost("close")]
    [HasPermission(Permissions.BusinessDayClose)]
    public Task<BusinessDayCloseDto> Close(Guid branchId, CloseBusinessDayRequest request, CancellationToken ct) =>
        businessDay.CloseAsync(branchId, request, ct);

    [HttpPost("exceptions/{exceptionId:guid}/accept")]
    [HasPermission(Permissions.BusinessDayClose)]
    public Task<BusinessDayCloseDto> Accept(Guid branchId, Guid exceptionId, AcceptExceptionRequest request, CancellationToken ct) =>
        businessDay.AcceptExceptionAsync(branchId, exceptionId, request, ct);
}
