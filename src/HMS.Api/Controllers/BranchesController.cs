using HMS.Api.Authorization;
using HMS.Application.Auditing;
using HMS.Application.Branches;
using HMS.Application.Security;
using HMS.Contracts.Branches;
using HMS.Contracts.Common;
using HMS.Contracts.Operations;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches")]
public sealed class BranchesController(IBranchService branches, IAuditQueryService audit) : ApiControllerBase
{
    /// <summary>Branches the caller may work in (for the branch picker).</summary>
    [HttpGet]
    [HasPermission(Permissions.BranchesView)]
    public Task<IReadOnlyList<BranchSummaryDto>> Mine(CancellationToken ct) => branches.GetMyBranchesAsync(ct);

    [HttpGet("{branchId:guid}")]
    [HasPermission(Permissions.BranchesView)]
    [AllowInactiveBranch]
    public Task<BranchDto> Get(Guid branchId, CancellationToken ct) => branches.GetAsync(branchId, ct);

    [HttpPost]
    [HasPermission(Permissions.SystemManage)]
    public async Task<ActionResult<BranchDto>> Create(CreateBranchRequest request, CancellationToken ct)
    {
        var created = await branches.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { branchId = created.Id }, created);
    }

    [HttpPut("{branchId:guid}")]
    [HasPermission(Permissions.BranchesManage)]
    [AllowInactiveBranch]
    public Task<BranchDto> Update(Guid branchId, UpdateBranchRequest request, CancellationToken ct) =>
        branches.UpdateAsync(branchId, request, ct);

    [HttpGet("{branchId:guid}/audit-logs")]
    [HasPermission(Permissions.AuditView)]
    [AllowInactiveBranch]
    public Task<PagedResult<AuditLogDto>> AuditLogs(Guid branchId, [FromQuery] PageQuery query, [FromQuery] string? entityType, [FromQuery] string? entityId,
        [FromQuery] string? userId, [FromQuery] string? action, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct) =>
        audit.SearchAsync(branchId, query, new AuditFilter(entityType, entityId, userId, action, fromUtc, toUtc), ct);

    /// <summary>CSV export for auditors (max 50,000 rows). The export is itself audited.</summary>
    [HttpGet("{branchId:guid}/audit-logs/export")]
    [HasPermission(Permissions.AuditView)]
    [AllowInactiveBranch]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportAuditLogs(Guid branchId, [FromQuery] string? entityType, [FromQuery] string? entityId, [FromQuery] string? userId,
        [FromQuery] string? action, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var csv = await audit.ExportCsvAsync(branchId, new AuditFilter(entityType, entityId, userId, action, fromUtc, toUtc), ct);
        Response.Headers.CacheControl = "no-store";
        return File(csv, "text/csv", $"audit-{branchId:N}.csv");
    }
}
