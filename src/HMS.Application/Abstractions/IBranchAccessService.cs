using HMS.Domain.Entities;

namespace HMS.Application.Abstractions;

/// <summary>
/// Single place that decides whether the current user may act on a branch. Every branch-scoped
/// API route, export, report and background job must go through it.
/// </summary>
public interface IBranchAccessService
{
    /// <summary>Returns the branch if the caller may access it; throws <see cref="Common.ForbiddenException"/> otherwise.</summary>
    Task<Branch> EnsureAccessAsync(Guid branchId, bool allowInactiveBranch = false, CancellationToken cancellationToken = default);

    /// <summary>Branch ids the current user can access (active branches in an active company).</summary>
    Task<IReadOnlyList<Guid>> GetAccessibleBranchIdsAsync(CancellationToken cancellationToken = default);
}
