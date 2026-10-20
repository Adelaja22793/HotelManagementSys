using HMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HMS.Infrastructure.IntegrationTests;

/// <summary>
/// Runs only when HMS_SQLSERVER_TEST_CONNECTION points at a disposable SQL Server instance
/// (CI starts one in a container). Never point this at production.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(SqlServerFixture.BaseConnection))
            Skip = "Set HMS_SQLSERVER_TEST_CONNECTION to run SQL Server integration tests.";
    }
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    public static string? BaseConnection => Environment.GetEnvironmentVariable("HMS_SQLSERVER_TEST_CONNECTION");

    public string ConnectionString { get; } =
        $"{BaseConnection};Database=HmsTest_{Guid.NewGuid():N};TrustServerCertificate=True";

    public HmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<HmsDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseConnection)) return;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseConnection)) return;
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}
