using HMS.Application.Security;

namespace HMS.Application.UnitTests;

public class RolePermissionsTests
{
    [Fact]
    public void System_admin_has_every_permission()
    {
        foreach (var p in Permissions.All)
            Assert.True(RolePermissions.HasPermission([Roles.SystemAdmin], p), p);
    }

    [Fact]
    public void Every_role_has_a_mapping()
    {
        foreach (var role in Roles.All)
            Assert.NotEmpty(RolePermissions.For(role));
    }

    [Fact]
    public void Auditor_is_read_only()
    {
        var perms = RolePermissions.For(Roles.Auditor);
        Assert.All(perms, p => Assert.True(p.EndsWith(".view", StringComparison.Ordinal), p));
    }

    [Fact]
    public void Only_system_admin_manages_users_and_system()
    {
        foreach (var role in Roles.All.Where(r => r != Roles.SystemAdmin))
        {
            Assert.DoesNotContain(Permissions.UsersManage, RolePermissions.For(role));
            Assert.DoesNotContain(Permissions.SystemManage, RolePermissions.For(role));
        }
    }

    [Fact]
    public void Housekeeping_cannot_take_payments()
    {
        Assert.False(RolePermissions.HasPermission([Roles.Housekeeping], Permissions.PaymentsRecord));
    }

    [Fact]
    public void Permission_names_are_unique()
    {
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct().Count());
    }

    [Fact]
    public void Unknown_role_has_no_permissions()
    {
        Assert.Empty(RolePermissions.For("Hacker"));
    }
}
