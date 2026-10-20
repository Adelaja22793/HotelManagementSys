using System.Net.Http.Headers;
using System.Net.Http.Json;
using HMS.Contracts.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace HMS.Api.IntegrationTests;

/// <summary>
/// Boots the real API pipeline against an isolated in-memory SQLite database. SQL Server-specific
/// behaviour (locking, migrations) is covered separately by infrastructure tests against real SQL Server.
/// </summary>
public class HmsApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@hms.test";
    public const string AdminPassword = "Bootstrap-Pass-123";

    private readonly string _dbName = $"hms_{Guid.NewGuid():N}";
    private readonly SqliteConnection _keepAlive;

    public HmsApiFactory()
    {
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    protected virtual int AuthPermitPerMinute => 10_000;

    private string ConnectionString => $"DataSource=file:{_dbName}?mode=memory&cache=shared";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HotelManagementDb"] = ConnectionString,
                ["Database:Provider"] = "Sqlite",
                ["Jwt:SigningKey"] = "integration-tests-signing-key-0123456789abcdef",
                ["Bootstrap:Enabled"] = "true",
                ["Bootstrap:CompanyName"] = "Test Hotels",
                ["Bootstrap:CompanyCode"] = "TEST",
                ["Bootstrap:BranchName"] = "Lagos",
                ["Bootstrap:BranchCode"] = "LOS",
                ["Bootstrap:AdminEmail"] = AdminEmail,
                ["Bootstrap:AdminPassword"] = AdminPassword,
                ["RateLimiting:AuthPermitPerMinute"] = AuthPermitPerMinute.ToString(),
            });
        });
    }

    public async Task<(HttpClient Client, LoginResponse Login)> LoginAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, login);
    }

    public Task<(HttpClient Client, LoginResponse Login)> LoginAsAdminAsync() => LoginAsync(AdminEmail, AdminPassword);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }
}

public sealed class StrictRateLimitFactory : HmsApiFactory
{
    protected override int AuthPermitPerMinute => 3;
}
