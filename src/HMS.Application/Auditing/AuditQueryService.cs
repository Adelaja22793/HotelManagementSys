using System.Globalization;
using System.Text;
using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Contracts.Common;
using HMS.Contracts.Operations;
using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Auditing;

/// <summary>Filters for the audit trail. Dates are UTC; To is exclusive.</summary>
public sealed record AuditFilter(string? EntityType = null, string? EntityId = null, string? UserId = null, string? Action = null,
    DateTime? FromUtc = null, DateTime? ToUtc = null);

public interface IAuditQueryService
{
    Task<PagedResult<AuditLogDto>> ListForBranchAsync(Guid branchId, PageQuery query, string? entityType, CancellationToken cancellationToken = default);
    Task<PagedResult<AuditLogDto>> SearchAsync(Guid branchId, PageQuery query, AuditFilter filter, CancellationToken cancellationToken = default);

    /// <summary>CSV of at most 50,000 rows for an external review. The export itself is recorded in the audit trail.</summary>
    Task<byte[]> ExportCsvAsync(Guid branchId, AuditFilter filter, CancellationToken cancellationToken = default);
}

internal sealed class AuditQueryService(IApplicationDbContext db, IBranchAccessService branchAccess, ICurrentUser user, IClock clock) : IAuditQueryService
{
    public const int MaxExportRows = 50_000;

    public Task<PagedResult<AuditLogDto>> ListForBranchAsync(Guid branchId, PageQuery query, string? entityType, CancellationToken cancellationToken = default) =>
        SearchAsync(branchId, query, new AuditFilter(EntityType: entityType), cancellationToken);

    public async Task<PagedResult<AuditLogDto>> SearchAsync(Guid branchId, PageQuery query, AuditFilter filter, CancellationToken cancellationToken = default)
    {
        await branchAccess.EnsureAccessAsync(branchId, allowInactiveBranch: true, cancellationToken);
        var logs = Filter(branchId, filter);
        var total = await logs.CountAsync(cancellationToken);
        var items = await logs
            .OrderByDescending(a => a.OccurredAtUtc)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(a => new AuditLogDto(a.Id, a.OccurredAtUtc, a.UserId, a.Action, a.EntityType, a.EntityId, a.ChangedProperties, a.TraceId))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditLogDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<byte[]> ExportCsvAsync(Guid branchId, AuditFilter filter, CancellationToken cancellationToken = default)
    {
        var branch = await branchAccess.EnsureAccessAsync(branchId, allowInactiveBranch: true, cancellationToken);
        var rows = await Filter(branchId, filter).OrderBy(a => a.OccurredAtUtc).Take(MaxExportRows + 1)
            .Select(a => new { a.OccurredAtUtc, a.UserId, a.Action, a.EntityType, a.EntityId, a.ChangedProperties, a.Details, a.TraceId })
            .ToListAsync(cancellationToken);
        if (rows.Count > MaxExportRows)
            throw new ValidationAppException("range", $"More than {MaxExportRows:N0} entries match. Narrow the date range.");

        var csv = new StringBuilder("OccurredAtUtc,UserId,Action,EntityType,EntityId,ChangedProperties,Details,TraceId\r\n");
        foreach (var r in rows)
            csv.AppendJoin(',', r.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture), Cell(r.UserId), Cell(r.Action), Cell(r.EntityType),
                Cell(r.EntityId), Cell(r.ChangedProperties), Cell(r.Details), Cell(r.TraceId)).Append("\r\n");

        db.AuditLogs.Add(new AuditLog
        {
            OccurredAtUtc = clock.UtcNow, CompanyId = branch.CompanyId, BranchId = branchId, UserId = user.UserId, Action = "Exported",
            EntityType = nameof(AuditLog), EntityId = branchId.ToString(), Details = $"{rows.Count} rows", TraceId = user.TraceId,
        });
        await db.SaveChangesAsync(cancellationToken);
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    private IQueryable<AuditLog> Filter(Guid branchId, AuditFilter f)
    {
        var logs = db.AuditLogs.AsNoTracking().Where(a => a.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(f.EntityType)) logs = logs.Where(a => a.EntityType == f.EntityType);
        if (!string.IsNullOrWhiteSpace(f.EntityId)) logs = logs.Where(a => a.EntityId == f.EntityId);
        if (!string.IsNullOrWhiteSpace(f.UserId)) logs = logs.Where(a => a.UserId == f.UserId);
        if (!string.IsNullOrWhiteSpace(f.Action)) logs = logs.Where(a => a.Action == f.Action);
        if (f.FromUtc is { } from) logs = logs.Where(a => a.OccurredAtUtc >= from);
        if (f.ToUtc is { } to) logs = logs.Where(a => a.OccurredAtUtc < to);
        return logs;
    }

    /// <summary>CSV-escapes a value and neutralises spreadsheet formulas (CSV injection).</summary>
    internal static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
