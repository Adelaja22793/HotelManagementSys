using System.Net.Http.Headers;
using System.Net.Http.Json;
using HMS.Application.Abstractions;
using HMS.Application.Documents;
using HMS.Application.PublicBooking;
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
    public void Advance(TimeSpan by) => _offset += by;
}

/// <summary>Scanner the tests control: clean by default, or flags any file containing the EICAR marker.</summary>
public sealed class FakeMalwareScanner : IMalwareScanner
{
    public bool Unavailable { get; set; }
    public string Name => "fake";

    public async Task<ScanVerdict> ScanAsync(Stream content, CancellationToken ct)
    {
        if (Unavailable) return new ScanVerdict(ScanOutcome.Error, "scanner offline");
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var text = System.Text.Encoding.ASCII.GetString(buffer.ToArray());
        return text.Contains("EICAR-TEST", StringComparison.Ordinal)
            ? new ScanVerdict(ScanOutcome.Infected, "Eicar-Test-Signature")
            : new ScanVerdict(ScanOutcome.Clean, null);
    }
}

/// <summary>
/// Payment provider the tests control. Payments succeed for the amount that was started unless a test overrides the
/// result. Webhooks are signed with HMAC-SHA256 over the body using <see cref="WebhookSecret"/>.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public const string WebhookSecret = "test-webhook-secret";
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (decimal Amount, string Currency)> _started = new();

    public string Name => "fake";
    public Func<string, GatewayVerification?>? Override { get; set; }
    public bool InitializeFails { get; set; }
    public int VerifyCalls;

    public Task<GatewayInitResult> InitializeAsync(GatewayInitRequest request, CancellationToken ct)
    {
        if (InitializeFails) return Task.FromResult(new GatewayInitResult(false, null, "provider down"));
        _started[request.MerchantReference] = (request.Amount, request.Currency);
        return Task.FromResult(new GatewayInitResult(true, "https://pay.example/checkout/" + request.MerchantReference, null));
    }

    public Task<GatewayVerification> VerifyAsync(string merchantReference, CancellationToken ct)
    {
        Interlocked.Increment(ref VerifyCalls);
        if (Override?.Invoke(merchantReference) is { } custom) return Task.FromResult(custom);
        return Task.FromResult(_started.TryGetValue(merchantReference, out var s)
            ? new GatewayVerification(GatewayOutcome.Success, "success", "TX-" + merchantReference, s.Amount, s.Currency, merchantReference)
            : new GatewayVerification(GatewayOutcome.Unknown, "not-found", null, null, null, null));
    }

    public static string Sign(byte[] body) =>
        Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(WebhookSecret), body));

    public bool IsValidWebhook(ReadOnlySpan<byte> body, string? signature) => signature is not null && signature == Sign(body.ToArray());

    public GatewayWebhookEvent? ParseWebhook(ReadOnlySpan<byte> body)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body.ToArray());
        return new GatewayWebhookEvent(doc.RootElement.GetProperty("event").GetString()!, doc.RootElement.GetProperty("reference").GetString());
    }
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

    private readonly string _dbName;
    private readonly SqliteConnection? _keepAlive;

    public HmsApiFactory() : this($"hms_{Guid.NewGuid():N}", Path.Combine(Path.GetTempPath(), "hms-tests", Guid.NewGuid().ToString("N")))
    {
    }

    /// <summary>Points the API at an existing database and data folder, for example a restored copy.</summary>
    protected HmsApiFactory(string databaseName, string dataRoot)
    {
        _dbName = databaseName;
        DataRoot = dataRoot;
        if (!UsesSqlServer)
        {
            _keepAlive = new SqliteConnection(ConnectionString);
            _keepAlive.Open();
        }
    }

    public static string? SqlServerBase => Environment.GetEnvironmentVariable("HMS_SQLSERVER_TEST_CONNECTION");
    public static bool UsesSqlServer => !string.IsNullOrWhiteSpace(SqlServerBase);

    public TestClock Clock { get; } = new();

    /// <summary>Private folder for this factory's documents and picked-up emails; deleted on dispose.</summary>
    public string DataRoot { get; }
    public string MailPickupPath => Path.Combine(DataRoot, "mail");
    public FakeMalwareScanner Scanner { get; } = new();

    protected virtual int AuthPermitPerMinute => 10_000;
    protected virtual int PublicWritePerMinute => 10_000;
    public const string BookingSite = "https://book.hms.test";
    public FakePaymentGateway Payments { get; } = new();

    public string DatabaseName => _dbName;

    public string ConnectionString => UsesSqlServer
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
                // Jobs are run on demand through /system/jobs/run so tests stay deterministic.
                ["BackgroundJobs:Enabled"] = "false",
                ["Documents:StoragePath"] = Path.Combine(DataRoot, "documents"),
                ["Email:Mode"] = "Pickup",
                ["Email:PickupDirectory"] = MailPickupPath,
                ["Email:FromAddress"] = "noreply@hms.test",
                ["RateLimiting:PublicReadPerMinute"] = "10000",
                ["RateLimiting:PublicWritePerMinute"] = PublicWritePerMinute.ToString(),
                ["RateLimiting:UploadsPerMinute"] = "10000",
                ["PublicBooking:SiteUrl"] = BookingSite,
                ["PublicBooking:AllowedOrigins:0"] = BookingSite,
                ["PublicBooking:MaxActiveHoldsPerClient"] = "1000",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
            services.RemoveAll<IMalwareScanner>();
            services.AddSingleton<IMalwareScanner>(Scanner);
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Payments);
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
        if (disposing)
        {
            _keepAlive?.Dispose();
            try { if (Directory.Exists(DataRoot)) Directory.Delete(DataRoot, recursive: true); } catch (IOException) { }
        }
    }
}

public sealed class StrictPublicRateLimitFactory : HmsApiFactory
{
    protected override int PublicWritePerMinute => 2;
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
