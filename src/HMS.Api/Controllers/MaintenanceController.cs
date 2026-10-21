using HMS.Api.Authorization;
using HMS.Application.Maintenance;
using HMS.Application.Security;
using HMS.Contracts.Common;
using HMS.Contracts.Maintenance;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/maintenance")]
public sealed class MaintenanceController(IMaintenanceService maintenance) : ApiControllerBase
{
    [HttpGet("assets")]
    [HasPermission(Permissions.MaintenanceView)]
    public Task<IReadOnlyList<AssetDto>> Assets(Guid branchId, CancellationToken ct) => maintenance.ListAssetsAsync(branchId, ct);

    [HttpGet("assets/{assetId:guid}/history")]
    [HasPermission(Permissions.MaintenanceView)]
    public Task<AssetHistoryDto> AssetHistory(Guid branchId, Guid assetId, CancellationToken ct) => maintenance.AssetHistoryAsync(branchId, assetId, ct);

    [HttpPost("assets")]
    [HasPermission(Permissions.MaintenanceManage)]
    public async Task<ActionResult<AssetDto>> CreateAsset(Guid branchId, SaveAssetRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await maintenance.SaveAssetAsync(branchId, null, request, ct));

    [HttpPut("assets/{assetId:guid}")]
    [HasPermission(Permissions.MaintenanceManage)]
    public Task<AssetDto> UpdateAsset(Guid branchId, Guid assetId, SaveAssetRequest request, CancellationToken ct) =>
        maintenance.SaveAssetAsync(branchId, assetId, request, ct);

    [HttpGet("requests")]
    [HasPermission(Permissions.MaintenanceView)]
    public Task<IReadOnlyList<MaintenanceRequestDto>> Requests(Guid branchId, [FromQuery] bool openOnly = true, CancellationToken ct = default) =>
        maintenance.ListRequestsAsync(branchId, openOnly, ct);

    /// <summary>Any staff member with maintenance view can report a problem.</summary>
    [HttpPost("requests")]
    [HasPermission(Permissions.MaintenanceView)]
    public async Task<ActionResult<MaintenanceRequestDto>> Report(Guid branchId, CreateMaintenanceRequestRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await maintenance.ReportAsync(branchId, request, ct));

    [HttpPost("requests/{requestId:guid}/triage")]
    [HasPermission(Permissions.MaintenanceManage)]
    public Task<MaintenanceRequestDto> Triage(Guid branchId, Guid requestId, TriageRequest request, CancellationToken ct) =>
        maintenance.TriageAsync(branchId, requestId, request, ct);

    [HttpGet("work-orders")]
    [HasPermission(Permissions.MaintenanceView)]
    public Task<IReadOnlyList<WorkOrderDto>> WorkOrders(Guid branchId, [FromQuery] bool openOnly = true, [FromQuery] string? assignedTo = null, CancellationToken ct = default) =>
        maintenance.ListWorkOrdersAsync(branchId, openOnly, assignedTo, ct);

    [HttpGet("work-orders/{workOrderId:guid}")]
    [HasPermission(Permissions.MaintenanceView)]
    public Task<WorkOrderDto> WorkOrder(Guid branchId, Guid workOrderId, CancellationToken ct) => maintenance.GetWorkOrderAsync(branchId, workOrderId, ct);

    [HttpPost("work-orders")]
    [HasPermission(Permissions.MaintenanceManage)]
    public async Task<ActionResult<WorkOrderDto>> CreateWorkOrder(Guid branchId, CreateWorkOrderRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await maintenance.CreateWorkOrderAsync(branchId, request, ct));

    [HttpPost("work-orders/{workOrderId:guid}/assign")]
    [HasPermission(Permissions.MaintenanceManage)]
    public Task<WorkOrderDto> Assign(Guid branchId, Guid workOrderId, AssignWorkOrderRequest request, CancellationToken ct) =>
        maintenance.AssignAsync(branchId, workOrderId, request, ct);

    [HttpPost("work-orders/{workOrderId:guid}/start")]
    [HasPermission(Permissions.MaintenanceWork)]
    public Task<WorkOrderDto> Start(Guid branchId, Guid workOrderId, WorkOrderActionRequest request, CancellationToken ct) =>
        maintenance.StartAsync(branchId, workOrderId, request, ct);

    /// <summary>Issues parts from stock to the work order. Idempotency-Key required.</summary>
    [HttpPost("work-orders/{workOrderId:guid}/parts")]
    [HasPermission(Permissions.MaintenanceWork)]
    public Task<WorkOrderDto> UsePart(Guid branchId, Guid workOrderId, UsePartRequest request, [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? key, CancellationToken ct) =>
        maintenance.UsePartAsync(branchId, workOrderId, request, key, ct);

    [HttpPost("work-orders/{workOrderId:guid}/finish")]
    [HasPermission(Permissions.MaintenanceWork)]
    public Task<WorkOrderDto> Finish(Guid branchId, Guid workOrderId, FinishWorkRequest request, CancellationToken ct) =>
        maintenance.FinishAsync(branchId, workOrderId, request, ct);

    [HttpPost("work-orders/{workOrderId:guid}/inspect")]
    [HasPermission(Permissions.MaintenanceManage)]
    public Task<WorkOrderDto> Inspect(Guid branchId, Guid workOrderId, InspectWorkRequest request, CancellationToken ct) =>
        maintenance.InspectAsync(branchId, workOrderId, request, ct);

    [HttpPost("work-orders/{workOrderId:guid}/cancel")]
    [HasPermission(Permissions.MaintenanceManage)]
    public Task<WorkOrderDto> Cancel(Guid branchId, Guid workOrderId, WorkOrderActionRequest request, CancellationToken ct) =>
        maintenance.CancelAsync(branchId, workOrderId, request, ct);

    [HttpGet("preventive-plans")]
    [HasPermission(Permissions.MaintenanceView)]
    public Task<IReadOnlyList<PreventivePlanDto>> Plans(Guid branchId, CancellationToken ct) => maintenance.ListPlansAsync(branchId, ct);

    [HttpPost("preventive-plans")]
    [HasPermission(Permissions.MaintenanceManage)]
    public async Task<ActionResult<PreventivePlanDto>> CreatePlan(Guid branchId, SavePreventivePlanRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await maintenance.SavePlanAsync(branchId, null, request, ct));

    [HttpPut("preventive-plans/{planId:guid}")]
    [HasPermission(Permissions.MaintenanceManage)]
    public Task<PreventivePlanDto> UpdatePlan(Guid branchId, Guid planId, SavePreventivePlanRequest request, CancellationToken ct) =>
        maintenance.SavePlanAsync(branchId, planId, request, ct);

    [HttpPost("preventive-plans/generate")]
    [HasPermission(Permissions.MaintenanceManage)]
    public Task<GeneratePreventiveResultDto> Generate(Guid branchId, GeneratePreventiveRequest request, CancellationToken ct) =>
        maintenance.GeneratePreventiveAsync(branchId, request, ct);
}
