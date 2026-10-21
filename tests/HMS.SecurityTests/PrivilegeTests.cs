using HMS.Application.Security;

namespace HMS.SecurityTests;

/// <summary>Who can hand out access, and how much.</summary>
public sealed class PrivilegeTests
{
    [Fact]
    public void Only_system_admins_manage_users_and_the_system()
    {
        foreach (var role in Roles.All.Where(r => r != Roles.SystemAdmin))
        {
            Assert.DoesNotContain(Permissions.UsersManage, RolePermissions.For(role));
            Assert.DoesNotContain(Permissions.SystemManage, RolePermissions.For(role));
        }
    }

    [Theory]
    [InlineData(new[] { "HotelManager" }, new[] { "Receptionist" }, true)]
    [InlineData(new[] { "HotelManager" }, new[] { "SystemAdmin" }, false)]
    [InlineData(new[] { "Receptionist" }, new[] { "Accountant" }, false)]
    [InlineData(new[] { "Receptionist", "Cashier" }, new[] { "Receptionist" }, true)]
    [InlineData(new[] { "SystemAdmin" }, new[] { "HotelManager", "Accountant", "Auditor" }, true)]
    [InlineData(new[] { "Accountant" }, new[] { "Accountant", "HumanResources" }, false)]
    public void A_role_can_only_be_granted_by_someone_holding_all_its_permissions(string[] caller, string[] target, bool allowed) =>
        Assert.Equal(allowed, RolePermissions.CanGrant(caller, target));

    [Fact]
    public void Unknown_roles_grant_nothing()
    {
        Assert.Empty(RolePermissions.For("Root"));
        Assert.True(RolePermissions.CanGrant([Roles.Receptionist], ["Root"]));
    }
}
