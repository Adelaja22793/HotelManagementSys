using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HMS.Infrastructure.IntegrationTests;

public class MigrationTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [SqlServerFact]
    public async Task Migrations_apply_cleanly_and_model_has_no_pending_changes()
    {
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges(), "Model changed without a migration. Run dotnet ef migrations add.");
    }

    [SqlServerFact]
    public async Task Branch_code_is_unique_per_company_in_the_database()
    {
        await using var db = fixture.CreateContext();
        var company = new Company("Unique Co", "U" + Guid.NewGuid().ToString("N")[..6]);
        db.Companies.Add(company);
        db.Branches.Add(new Branch(company.Id, "One", "DUP"));
        await db.SaveChangesAsync();

        await using var db2 = fixture.CreateContext();
        db2.Branches.Add(new Branch(company.Id, "Two", "DUP"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Concurrent_edits_are_detected()
    {
        await using var setup = fixture.CreateContext();
        var company = new Company("Concurrency Co", "C" + Guid.NewGuid().ToString("N")[..6]);
        setup.Companies.Add(company);
        await setup.SaveChangesAsync();

        await using var a = fixture.CreateContext();
        await using var b = fixture.CreateContext();
        var ca = await a.Companies.SingleAsync(c => c.Id == company.Id);
        var cb = await b.Companies.SingleAsync(c => c.Id == company.Id);

        ca.Rename("A wins"); ca.ConcurrencyStamp = Guid.NewGuid();
        await a.SaveChangesAsync();

        cb.Rename("B loses"); cb.ConcurrencyStamp = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }
}
