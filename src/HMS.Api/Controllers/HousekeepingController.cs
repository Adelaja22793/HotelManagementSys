using HMS.Api.Authorization;
using HMS.Application.Housekeeping;
using HMS.Application.Security;
using HMS.Contracts.Housekeeping;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/housekeeping/tasks")]
public sealed class HousekeepingController(IHousekeepingService housekeeping) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.HousekeepingView)]
    public Task<IReadOnlyList<HousekeepingTaskDto>> List(Guid branchId, [FromQuery] string? status, [FromQuery] bool openOnly = true, CancellationToken ct = default) =>
        housekeeping.ListAsync(branchId, status, openOnly, ct);

    [HttpPost]
    [HasPermission(Permissions.HousekeepingWork)]
    public async Task<ActionResult<HousekeepingTaskDto>> Create(Guid branchId, CreateHousekeepingTaskRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await housekeeping.CreateAsync(branchId, request, ct));

    [HttpPost("{taskId:guid}/assign")]
    [HasPermission(Permissions.HousekeepingInspect)]
    public Task<HousekeepingTaskDto> Assign(Guid branchId, Guid taskId, AssignHousekeepingTaskRequest request, CancellationToken ct) =>
        housekeeping.AssignAsync(branchId, taskId, request, ct);

    [HttpPost("{taskId:guid}/start")]
    [HasPermission(Permissions.HousekeepingWork)]
    public Task<HousekeepingTaskDto> Start(Guid branchId, Guid taskId, HousekeepingActionRequest request, CancellationToken ct) =>
        housekeeping.StartAsync(branchId, taskId, request, ct);

    [HttpPost("{taskId:guid}/complete")]
    [HasPermission(Permissions.HousekeepingWork)]
    public Task<HousekeepingTaskDto> Complete(Guid branchId, Guid taskId, HousekeepingActionRequest request, CancellationToken ct) =>
        housekeeping.CompleteAsync(branchId, taskId, request, ct);

    [HttpPost("{taskId:guid}/inspect")]
    [HasPermission(Permissions.HousekeepingInspect)]
    public Task<HousekeepingTaskDto> Inspect(Guid branchId, Guid taskId, InspectionRequest request, CancellationToken ct) =>
        housekeeping.InspectAsync(branchId, taskId, request, ct);

    [HttpPost("{taskId:guid}/cancel")]
    [HasPermission(Permissions.HousekeepingInspect)]
    public Task<HousekeepingTaskDto> Cancel(Guid branchId, Guid taskId, HousekeepingActionRequest request, CancellationToken ct) =>
        housekeeping.CancelAsync(branchId, taskId, request, ct);
}
