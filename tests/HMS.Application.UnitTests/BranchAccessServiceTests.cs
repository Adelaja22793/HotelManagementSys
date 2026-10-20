using HMS.Application.Common;
using HMS.Application.Security;
using HMS.Contracts.Common;
using HMS.Domain.Entities;
using HMS.Infrastructure.Identity;

namespace HMS.Application.UnitTests;

public class BranchAccessServiceTests : IDisposable
{
    private readonly FakeCurrentUser _user = new();
    private readonly TestDb _db;
    private readonly Company _company = new("Acme Hotels", "ACME");
    private readonly Company _otherCompany = new("Other Hotels", "OTH");
    private readonly Branch _lagos;
    private readonly Branch _abuja;
    private readonly Branch _foreign;

    public BranchAccessServiceTests()
    {
        _db = new TestDb(_user);
        _lagos = new Branch(_company.Id, "Lagos", "LOS");
        _abuja = new Branch(_company.Id, "Abuja", "ABJ");
        _foreign = new Branch(_otherCompany.Id, "Foreign", "FOR");
        _user.CompanyId = _company.Id;

        var ctx = _db.Context;
        ctx.Companies.AddRange(_company, _otherCompany);
        ctx.Branches.AddRange(_lagos, _abuja, _foreign);
        ctx.Users.Add(new ApplicationUser { Id = "user-1", UserName = "u1", Email = "u1@x.test", FullName = "U1", CompanyId = _company.Id });
        ctx.UserBranches.Add(new HotelUserBranch("user-1", _company.Id, _lagos.Id));
        ctx.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private BranchAccessService Sut() => new(_db.Context, _user);

    [Fact]
    public async Task Member_can_access_assigned_branch()
    {
        var branch = await Sut().EnsureAccessAsync(_lagos.Id);
        Assert.Equal(_lagos.Id, branch.Id);
    }

    [Fact]
    public async Task Member_cannot_access_unassigned_branch_in_same_company()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Sut().EnsureAccessAsync(_abuja.Id));
        Assert.Equal(ErrorCodes.BranchAccessDenied, ex.Code);
    }

    [Fact]
    public async Task System_admin_cannot_cross_company_boundary()
    {
        _user.RoleList.Add(Roles.SystemAdmin);
        await Sut().EnsureAccessAsync(_abuja.Id); // any branch in own company
        await Assert.ThrowsAsync<ForbiddenException>(() => Sut().EnsureAccessAsync(_foreign.Id));
    }

    [Fact]
    public async Task Unknown_branch_looks_the_same_as_forbidden()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Sut().EnsureAccessAsync(Guid.NewGuid()));
        Assert.Equal(ErrorCodes.BranchAccessDenied, ex.Code);
    }

    [Fact]
    public async Task Revoked_membership_denies_access()
    {
        var membership = _db.Context.UserBranches.Single();
        membership.Revoke();
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Sut().EnsureAccessAsync(_lagos.Id));
    }

    [Fact]
    public async Task Inactive_branch_is_blocked_unless_explicitly_allowed()
    {
        _lagos.Deactivate();
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Sut().EnsureAccessAsync(_lagos.Id));
        Assert.Equal(ErrorCodes.BranchInactive, ex.Code);
        Assert.NotNull(await Sut().EnsureAccessAsync(_lagos.Id, allowInactiveBranch: true));
    }

    [Fact]
    public async Task Inactive_company_blocks_everything()
    {
        _company.Deactivate();
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Sut().EnsureAccessAsync(_lagos.Id, allowInactiveBranch: true));
    }

    [Fact]
    public async Task Accessible_branches_lists_only_memberships()
    {
        var ids = await Sut().GetAccessibleBranchIdsAsync();
        Assert.Equal([_lagos.Id], ids);
    }

    [Fact]
    public async Task Unauthenticated_caller_is_rejected()
    {
        _user.UserId = null;
        await Assert.ThrowsAsync<UnauthenticatedException>(() => Sut().EnsureAccessAsync(_lagos.Id));
    }

    [Fact]
    public async Task Changes_are_audited_and_concurrency_stamp_rotates()
    {
        var before = _lagos.ConcurrencyStamp;
        _lagos.Update("Lagos Island", "Africa/Lagos", "NGN", null, "Lagos", null);
        await _db.Context.SaveChangesAsync();

        Assert.NotEqual(before, _lagos.ConcurrencyStamp);
        var audit = _db.Context.AuditLogs.Where(a => a.EntityId == _lagos.Id.ToString() && a.Action == "Updated").ToList();
        Assert.Single(audit);
        Assert.Contains("Name", audit[0].ChangedProperties);
        Assert.Equal(_lagos.Id, audit[0].BranchId);
    }
}
