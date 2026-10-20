using HMS.Application.Abstractions;
using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Common;

/// <summary>Context for an operation inside one authorised branch.</summary>
public sealed record BranchScope(Branch Branch, string UserId, DateTime UtcNow)
{
    public Guid BranchId => Branch.Id;
    public Guid CompanyId => Branch.CompanyId;
    public DateOnly BusinessDate => Branch.GetBusinessDate(UtcNow);
}

public static class BranchOperations
{
    public static async Task<BranchScope> ScopeAsync(this IBranchAccessService access, ICurrentUser user, IClock clock,
        Guid branchId, CancellationToken ct)
    {
        var branch = await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return new BranchScope(branch, user.UserId ?? throw new UnauthenticatedException(), clock.UtcNow);
    }

    /// <summary>Loads a branch-owned record by id, treating other branches' records as not found.</summary>
    public static async Task<T> GetOwnedAsync<T>(this IQueryable<T> set, Guid branchId, Guid id, string name, CancellationToken ct)
        where T : Domain.Common.BranchEntity =>
        await set.SingleOrDefaultAsync(x => x.Id == id && x.BranchId == branchId, ct) ?? throw new NotFoundException(name, id);

    public static void RequireTransaction(this IApplicationDbContext db)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("This operation must run inside a database transaction.");
    }

    /// <summary>Marks a concurrency-tracked row as changed so the save fails if someone else changed it first.</summary>
    public static void Touch(this IApplicationDbContext db, Domain.Common.IConcurrencyTracked entity) =>
        ((DbContext)db).Entry(entity).Property(nameof(Domain.Common.IConcurrencyTracked.ConcurrencyStamp)).IsModified = true;

    public static void EnsureStamp(Domain.Common.IConcurrencyTracked entity, Guid expected, string what)
    {
        if (entity.ConcurrencyStamp != expected)
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, $"The {what} was changed by someone else. Reload and try again.");
    }

    public static async Task<decimal> SumClientAsync(this IQueryable<decimal> amounts, CancellationToken ct) =>
        (await amounts.ToListAsync(ct)).Sum(); // client-side: SQLite cannot aggregate decimals
}
