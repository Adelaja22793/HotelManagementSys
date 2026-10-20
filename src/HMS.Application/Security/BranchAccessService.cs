using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Contracts.Common;
using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Security;

/// <summary>
/// Enforces company and branch isolation. Unknown branches and branches the caller may not use
/// both return 403 so the API does not reveal which branch ids exist.
/// </summary>
internal sealed class BranchAccessService(IApplicationDbContext db, ICurrentUser currentUser) : IBranchAccessService
{
    private readonly Dictionary<(Guid, bool), Branch> _cache = new();

    public async Task<Branch> EnsureAccessAsync(Guid branchId, bool allowInactiveBranch = false, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue((branchId, allowInactiveBranch), out var cached)) return cached;

        var (userId, companyId) = RequireUser();

        var branch = await db.Branches
            .Include(b => b.Company)
            .SingleOrDefaultAsync(b => b.Id == branchId, cancellationToken);

        if (branch is null || branch.CompanyId != companyId)
            throw new ForbiddenException(ErrorCodes.BranchAccessDenied, "You do not have access to this branch.");

        if (branch.Company is { IsActive: false })
            throw new ForbiddenException(ErrorCodes.BranchInactive, "The company is inactive.");

        if (!branch.IsActive && !allowInactiveBranch)
            throw new ForbiddenException(ErrorCodes.BranchInactive, "The branch is inactive.");

        if (!currentUser.IsInRole(Roles.SystemAdmin))
        {
            var isMember = await db.UserBranches.AnyAsync(
                m => m.UserId == userId && m.BranchId == branchId && m.IsActive, cancellationToken);
            if (!isMember)
                throw new ForbiddenException(ErrorCodes.BranchAccessDenied, "You do not have access to this branch.");
        }

        _cache[(branchId, allowInactiveBranch)] = branch;
        return branch;
    }

    public async Task<IReadOnlyList<Guid>> GetAccessibleBranchIdsAsync(CancellationToken cancellationToken = default)
    {
        var (userId, companyId) = RequireUser();

        var branches = db.Branches.Where(b => b.CompanyId == companyId && b.IsActive && b.Company!.IsActive);
        if (!currentUser.IsInRole(Roles.SystemAdmin))
        {
            branches = branches.Where(b => db.UserBranches.Any(m => m.UserId == userId && m.BranchId == b.Id && m.IsActive));
        }
        return await branches.Select(b => b.Id).ToListAsync(cancellationToken);
    }

    private (string UserId, Guid CompanyId) RequireUser()
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null || currentUser.CompanyId is null)
            throw new UnauthenticatedException();
        return (currentUser.UserId, currentUser.CompanyId.Value);
    }
}
