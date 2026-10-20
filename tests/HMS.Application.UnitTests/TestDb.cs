using HMS.Application.Abstractions;
using HMS.Infrastructure.Persistence;
using HMS.Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.UnitTests;

internal sealed class FakeCurrentUser : ICurrentUser
{
    public string? UserId { get; set; } = "user-1";
    public Guid? CompanyId { get; set; }
    public List<string> RoleList { get; } = [];
    public bool IsAuthenticated => UserId is not null;
    public IReadOnlyCollection<string> Roles => RoleList;
    public string? TraceId => "test";
    public bool IsInRole(string role) => RoleList.Contains(role);
}

internal sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}

/// <summary>Real relational database (SQLite in memory) so constraints and SQL translation are exercised.</summary>
internal sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestDb(ICurrentUser user, IClock? clock = null)
    {
        _connection.Open();
        Clock = clock ?? new FixedClock(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc));
        var options = new DbContextOptionsBuilder<HmsDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditingInterceptor(user, Clock))
            .Options;
        Context = new HmsDbContext(options);
        Context.Database.EnsureCreated();
    }

    public HmsDbContext Context { get; }
    public IClock Clock { get; }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
