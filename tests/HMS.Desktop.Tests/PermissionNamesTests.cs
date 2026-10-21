using System.Reflection;
using HMS.Application.Security;
using HMS.Desktop.Services;

namespace HMS.Desktop.Tests;

public class PermissionNamesTests
{
    [Fact]
    public void Desktop_permission_names_match_the_server()
    {
        var desktop = typeof(PermissionNames).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetValue(null)!).Order().ToArray();
        Assert.Equal(Permissions.All.Order().ToArray(), desktop);
    }
}
