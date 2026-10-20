using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HMS.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to build SQL Server migrations. No real connection is opened.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HmsDbContext>
{
    public HmsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HMS_MIGRATIONS_CONNECTION")
            ?? "Server=localhost;Database=HotelManagementDb;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<HmsDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(HmsDbContext).Assembly.FullName))
            .Options;
        return new HmsDbContext(options);
    }
}
