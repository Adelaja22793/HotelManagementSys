using HMS.Application.Abstractions;
using HMS.Contracts.Common;
using HMS.Contracts.Operations;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Auditing;

public interface IAuditQueryService
{
    Task<PagedResult<AuditLogDto>> ListForBranchAsync(Guid branchId, PageQuery query, string? entityType, CancellationToken cancellationToken = default);
}

internal sealed class AuditQueryService(IApplicationDbContext db, IBranchAccessService branchAccess) : IAuditQueryService
{
    public async Task<PagedResult<AuditLogDto>> ListForBranchAsync(Guid branchId, PageQuery query, string? entityType, CancellationToken cancellationToken = default)
    {
        await branchAccess.EnsureAccessAsync(branchId, allowInactiveBranch: true, cancellationToken);

        var logs = db.AuditLogs.AsNoTracking().Where(a => a.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(entityType)) logs = logs.Where(a => a.EntityType == entityType);

        var total = await logs.CountAsync(cancellationToken);
        var items = await logs
            .OrderByDescending(a => a.OccurredAtUtc)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(a => new AuditLogDto(a.Id, a.OccurredAtUtc, a.UserId, a.Action, a.EntityType, a.EntityId, a.ChangedProperties, a.TraceId))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(items, query.Page, query.PageSize, total);
    }
}
