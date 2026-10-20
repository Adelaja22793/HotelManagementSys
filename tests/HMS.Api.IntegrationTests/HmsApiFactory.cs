using System.Net.Http.Headers;
using System.Net.Http.Json;
using HMS.Application.Abstractions;
using HMS.Contracts.Auth;
using HMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HMS.Api.IntegrationTests;

/// <summary>Controllable clock so tests can move the hotel business day.</summary>
public sealed class TestClock : IClock
{
    private TimeSpan _offset;
    public DateTime UtcNow => DateTime.UtcNow + _offset;
    public void AdvanceDays(int days) => _offset += TimeSpan.FromDays(days);
}

/// <summary>
/// Boots the real API pipeline. By default it uses an isolated in-memory SQLite database. When
/// HMS_SQLSERVER_TEST_CONNECTION is set, every factory gets its own migrated SQL Server database so
/// locking and constraint behaviour is proven on the production engine.
/// </summary>
public class HmsApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@hms.test";
    public const string AdminPassword = "Bootstrap-Pass-123";

    private readonly string _dbName = $"hms_{Guid.NewGuid():N}";
    private readonly SqliteConnection? _keepAlive;

    public HmsApiFactory()
    {
        if (!UsesSqlServer)
        {
            _keepAlive = new SqliteConnection(ConnectionString);
            _keepAlive.Open();
        }
    }

    public static string? SqlServerBase => Environment.GetEnvironmentVariable("HMS_SQLSERVER_TEST_CONNECTION");
    public static bool UsesSqlServer => !string.IsNullOrWhiteSpace(SqlServerBase);

    public TestClock Clock { get; } = new();

    protected virtual int AuthPermitPerMinute => 10_000;

    private string ConnectionString => UsesSqlServer
        ? $"{SqlServerBase};Database={_dbName};TrustServerCertificate=True"
        : $"DataSource=file:{_dbName}?mode=memory&cache=shared";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HotelManagementDb"] = ConnectionString,
                ["Database:Provider"] = UsesSqlServer ? "SqlServer" : "Sqlite",
                ["Database:ApplyMigrationsOnStartup"] = "true",
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
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
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
        if (disposing && UsesSqlServer)
        {
            try
            {
                using var scope = Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<HmsDbContext>().Database.EnsureDeleted();
            }
            catch (Exception) { /* best effort cleanup of the throwaway database */ }
        }
        base.Dispose(disposing);
        if (disposing) _keepAlive?.Dispose();
    }
}

public sealed class StrictRateLimitFactory : HmsApiFactory
{
    protected override int AuthPermitPerMinute => 3;
}

/// <summary>Runs only against SQL Server: SQLite cannot model concurrent row locking.</summary>
public sealed class SqlServerOnlyFactAttribute : FactAttribute
{
    public SqlServerOnlyFactAttribute()
    {
        if (!HmsApiFactory.UsesSqlServer) Skip = "Set HMS_SQLSERVER_TEST_CONNECTION to run concurrency tests against SQL Server.";
    }
}
