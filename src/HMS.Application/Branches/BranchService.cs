using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Application.Security;
using HMS.Contracts.Branches;
using HMS.Contracts.Common;
using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Branches;

public interface IBranchService
{
    Task<IReadOnlyList<BranchSummaryDto>> GetMyBranchesAsync(CancellationToken cancellationToken = default);
    Task<BranchDto> GetAsync(Guid branchId, CancellationToken cancellationToken = default);
    Task<BranchDto> CreateAsync(CreateBranchRequest request, CancellationToken cancellationToken = default);
    Task<BranchDto> UpdateAsync(Guid branchId, UpdateBranchRequest request, CancellationToken cancellationToken = default);
}

internal sealed class BranchService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IBranchAccessService branchAccess,
    IClock clock,
    INumberSequence numbers) : IBranchService
{
    public async Task<IReadOnlyList<BranchSummaryDto>> GetMyBranchesAsync(CancellationToken cancellationToken = default)
    {
        var ids = await branchAccess.GetAccessibleBranchIdsAsync(cancellationToken);
        var userId = currentUser.UserId!;
        var defaults = await db.UserBranches
            .Where(m => m.UserId == userId && m.IsDefault && m.IsActive)
            .Select(m => m.BranchId)
            .ToListAsync(cancellationToken);

        var branches = await db.Branches.Where(b => ids.Contains(b.Id)).OrderBy(b => b.Name).ToListAsync(cancellationToken);
        return branches.Select(b => new BranchSummaryDto(b.Id, b.Code, b.Name, b.TimeZoneId, defaults.Contains(b.Id))).ToList();
    }

    public async Task<BranchDto> GetAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        var branch = await branchAccess.EnsureAccessAsync(branchId, allowInactiveBranch: CanManageInactive(), cancellationToken);
        return ToDto(branch);
    }

    public async Task<BranchDto> CreateAsync(CreateBranchRequest request, CancellationToken cancellationToken = default)
    {
        new Validation()
            .Require(nameof(request.Code), request.Code, 20)
            .Require(nameof(request.Name), request.Name)
            .ThrowIfInvalid();

        var companyId = currentUser.CompanyId ?? throw new UnauthenticatedException();
        var code = request.Code.Trim().ToUpperInvariant();

        if (await db.Branches.AnyAsync(b => b.CompanyId == companyId && b.Code == code, cancellationToken))
            throw new ConflictException("BRANCH_CODE_EXISTS", $"Branch code '{code}' already exists.");

        var branch = new Branch(companyId, request.Name, code, request.TimeZoneId ?? Branch.DefaultTimeZoneId, request.Currency);
        branch.Update(branch.Name, branch.TimeZoneId, branch.Currency, request.AddressLine, request.City, request.Phone);
        db.Branches.Add(branch);
        await numbers.EnsureCountersAsync(branch.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(branch);
    }

    public async Task<BranchDto> UpdateAsync(Guid branchId, UpdateBranchRequest request, CancellationToken cancellationToken = default)
    {
        new Validation()
            .Require(nameof(request.Name), request.Name)
            .Require(nameof(request.TimeZoneId), request.TimeZoneId, 64)
            .Require(nameof(request.Currency), request.Currency, 3)
            .ThrowIfInvalid();

        var branch = await branchAccess.EnsureAccessAsync(branchId, allowInactiveBranch: true, cancellationToken);
        if (branch.ConcurrencyStamp != request.ConcurrencyStamp)
            throw new ConflictException(ErrorCodes.ConcurrencyConflict, "The branch was changed by someone else. Reload and try again.");

        branch.Update(request.Name, request.TimeZoneId, request.Currency, request.AddressLine, request.City, request.Phone);
        if (request.BusinessDayCutoffLocal is { } cutoff) branch.SetBusinessDayCutoff(cutoff);
        if (request.HousekeepingRequiresInspection is { } inspect) branch.SetHousekeepingInspection(inspect);
        if (request.IsActive) branch.Activate(); else branch.Deactivate();

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(branch);
    }

    private bool CanManageInactive() =>
        RolePermissions.HasPermission(currentUser.Roles, Permissions.BranchesManage);

    private BranchDto ToDto(Branch b) => new(
        b.Id, b.CompanyId, b.Code, b.Name, b.TimeZoneId, b.Currency, b.AddressLine, b.City, b.Phone,
        b.BusinessDayCutoffLocal, b.GetBusinessDate(clock.UtcNow), b.HousekeepingRequiresInspection, b.IsActive, b.ConcurrencyStamp);
}
