using HMS.Api.Authorization;
using HMS.Application.Analytics;
using HMS.Application.Security;
using HMS.Contracts.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/analytics")]
public sealed class AnalyticsController(IAnalyticsService analytics) : ApiControllerBase
{
    [HttpGet("dashboard")]
    [HasPermission(Permissions.ReportsView)]
    public Task<DashboardDto> Dashboard(Guid branchId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        analytics.DashboardAsync(branchId, from, to, ct);

    /// <summary>Recomputes snapshots that are not final (days not yet closed by the night audit).</summary>
    [HttpPost("refresh")]
    [HasPermission(Permissions.ReportsView)]
    public Task<RefreshResultDto> Refresh(Guid branchId, RefreshMetricsRequest request, CancellationToken ct) => analytics.RefreshAsync(branchId, request, ct);

    [HttpGet("forecast")]
    [HasPermission(Permissions.ReportsView)]
    public Task<ForecastDto> Forecast(Guid branchId, [FromQuery] int days = 30, CancellationToken ct = default) => analytics.ForecastAsync(branchId, days, ct);
}

/// <summary>Company view across every branch the caller can access.</summary>
[Route(V1 + "/analytics")]
public sealed class CompanyAnalyticsController(IAnalyticsService analytics) : ApiControllerBase
{
    [HttpGet("branches")]
    [HasPermission(Permissions.ReportsView)]
    public Task<BranchComparisonDto> Compare([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        analytics.CompareBranchesAsync(from, to, ct);
}
