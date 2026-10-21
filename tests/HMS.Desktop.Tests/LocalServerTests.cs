using HMS.Desktop.Services;
using HMS.Desktop.Services.LocalServer;
using HMS.Desktop.ViewModels;
using Microsoft.Data.SqlClient;

namespace HMS.Desktop.Tests;

/// <summary>Leaves the key as is; DPAPI only exists on Windows and is covered by the desktop app itself.</summary>
internal sealed class PlainProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => data;
    public byte[] Unprotect(byte[] data) => data;
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public static string? BaseConnection => Environment.GetEnvironmentVariable("HMS_SQLSERVER_TEST_CONNECTION");

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(BaseConnection))
            Skip = "Set HMS_SQLSERVER_TEST_CONNECTION to run the built-in server test.";
    }
}

public sealed class LocalServerTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "hms-local-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    [Fact]
    public void Server_listens_on_loopback_and_only_gets_the_admin_password_on_the_first_run()
    {
        var options = new LocalServerOptions { DataDirectory = _dataDirectory };
        var setup = new LocalSetup("Palm Court Hotel", "Ikeja", "Ada Obi", "ada@palmcourt.ng", "Palm-Court-2026");

        var first = LocalServer.BuildEnvironment(options, "key", 5123, 42, setup);
        Assert.Equal("http://127.0.0.1:5123", first["ASPNETCORE_URLS"]);
        Assert.Equal("Production", first["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal("42", first["Hosting__ParentProcessId"]);
        Assert.Equal("true", first["Database__ApplyMigrationsOnStartup"]);
        Assert.Equal("true", first["Bootstrap__Enabled"]);
        Assert.Equal("Palm Court Hotel", first["Bootstrap__CompanyName"]);
        Assert.Equal("Palm-Court-2026", first["Bootstrap__AdminPassword"]);

        var later = LocalServer.BuildEnvironment(options, "key", 5123, 42, null);
        Assert.Equal("false", later["Bootstrap__Enabled"]);
        Assert.False(later.ContainsKey("Bootstrap__AdminPassword"));
    }

    [Fact]
    public async Task First_run_form_checks_its_fields_before_starting_anything()
    {
        var server = new LocalServer(new LocalServerOptions { DataDirectory = _dataDirectory }, new PlainProtector());
        var startup = new StartupViewModel(server) { HotelName = "Palm Court Hotel", AdminFullName = "Ada Obi", AdminEmail = "not-an-email" };
        Assert.True(startup.IsSetupNeeded);

        await startup.SubmitSetupAsync("Palm-Court-2026", "Palm-Court-2026");
        Assert.Contains("valid email", startup.ErrorMessage);

        startup.AdminEmail = "ada@palmcourt.ng";
        await startup.SubmitSetupAsync("short", "short");
        Assert.Contains("12 characters", startup.ErrorMessage);

        await startup.SubmitSetupAsync("palm-court-2026", "palm-court-2026");
        Assert.Contains("uppercase", startup.ErrorMessage);

        await startup.SubmitSetupAsync("Palm-Court-2026", "Palm-Court-2027");
        Assert.Contains("do not match", startup.ErrorMessage);
        Assert.Null(server.BaseAddress);
    }

    [Fact]
    public async Task Missing_server_program_shows_an_error_with_a_retry()
    {
        var server = new LocalServer(new LocalServerOptions { DataDirectory = _dataDirectory, ServerDirectory = _dataDirectory }, new PlainProtector());
        var startup = new StartupViewModel(server) { HotelName = "Palm Court Hotel", AdminFullName = "Ada Obi", AdminEmail = "ada@palmcourt.ng" };

        await startup.SubmitSetupAsync("Palm-Court-2026", "Palm-Court-2026");

        Assert.Contains("missing", startup.ErrorMessage);
        Assert.True(startup.CanRetry);
        Assert.True(startup.IsSetupNeeded);
    }

    [SqlServerFact]
    public async Task App_starts_its_own_server_sets_up_the_hotel_and_signs_in()
    {
        var database = "HmsLocal_" + Guid.NewGuid().ToString("N");
        var options = new LocalServerOptions
        {
            DataDirectory = _dataDirectory,
            ServerDirectory = ApiBuildOutput(),
            ConnectionString = $"{SqlServerFactAttribute.BaseConnection};Database={database};TrustServerCertificate=True",
        };
        try
        {
            // First run: the setup form creates the hotel and the administrator.
            await using (var server = new LocalServer(options, new PlainProtector()))
            {
                var startup = new StartupViewModel(server)
                {
                    HotelName = "Palm Court Hotel", BranchName = "Ikeja", AdminFullName = "Ada Obi", AdminEmail = "ada@palmcourt.ng",
                };
                var started = false;
                startup.Started += (_, _) => started = true;

                await startup.SubmitSetupAsync("Palm-Court-2026", "Palm-Court-2026");

                Assert.True(started, startup.ErrorMessage + Environment.NewLine + startup.Details);
                Assert.Equal("ada@palmcourt.ng", startup.SignInEmail);
                Assert.Equal("127.0.0.1", server.BaseAddress!.Host);
                var login = await SignInAsync(server.BaseAddress, "ada@palmcourt.ng", "Palm-Court-2026");
                Assert.Equal("Ikeja", Assert.Single(login.Branches).Name);
            }

            // Every later start skips the form and signs in to the same hotel.
            await using (var server = new LocalServer(options, new PlainProtector()))
            {
                Assert.False(server.NeedsSetup);
                var startup = new StartupViewModel(server);
                await startup.StartAsync();
                Assert.Null(startup.ErrorMessage);
                var login = await SignInAsync(server.BaseAddress!, "ada@palmcourt.ng", "Palm-Court-2026");
                Assert.Equal("Ikeja", Assert.Single(login.Branches).Name);
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection($"{SqlServerFactAttribute.BaseConnection};Database=master;TrustServerCertificate=True");
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<HMS.Contracts.Auth.LoginResponse> SignInAsync(Uri baseAddress, string email, string password)
    {
        using var http = new HttpClient { BaseAddress = baseAddress };
        return await new ApiClient(http, new SessionState()).LoginAsync(email, password);
    }

    /// <summary>The API as built next to these tests (src/HMS.Api/bin/&lt;configuration&gt;/net10.0).</summary>
    private static string ApiBuildOutput()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = here.Parent!.Name; // bin/<configuration>/net10.0
        var root = here;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "HotelManagementSystem.sln"))) root = root.Parent;
        return Path.Combine(root!.FullName, "src", "HMS.Api", "bin", configuration, "net10.0");
    }
}
