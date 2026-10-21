using HMS.Api.Authorization;
using HMS.Api.Services;
using HMS.Application.Security;
using HMS.Contracts.Operations;
using HMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HMS.Api.Controllers;

[Route(V1 + "/system")]
public sealed class SystemController(HmsDbContext db, IConfiguration configuration, TimeProvider time) : ApiControllerBase
{
    /// <summary>
    /// Version compatibility for desktop clients. Migration state is read from the database itself and
    /// shown only to administrators.
    /// </summary>
    [HttpGet("version")]
    [AllowAnonymous]
    public async Task<VersionInfoDto> Version(CancellationToken ct)
    {
        var apiVersion = typeof(SystemController).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        string? migration = null;
        var pending = 0;

        if (User.Identity?.IsAuthenticated == true && User.IsInRole(Roles.SystemAdmin) && !db.Database.IsSqlite())
        {
            migration = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();
            pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
        }

        return new VersionInfoDto(
            apiVersion,
            configuration["Desktop:MinimumVersion"] ?? "0.1.0",
            configuration["Desktop:LatestVersion"] ?? apiVersion,
            migration,
            pending,
            time.GetUtcNow().UtcDateTime);
    }

    /// <summary>Runs the notification and document-scan jobs now instead of waiting for the next timer tick.</summary>
    [HttpPost("jobs/run")]
    [HasPermission(Permissions.SystemManage)]
    public Task<JobRunResult> RunJobs([FromServices] BackgroundJobRunner runner, CancellationToken ct) => runner.RunOnceAsync(ct);
}
