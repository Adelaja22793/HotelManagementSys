using HMS.Application.Common;
using HMS.Application.Security;
using HMS.Domain.Entities;
using HMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HMS.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<HmsDbContext>();
        var dbOptions = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("HMS.DatabaseInitializer");

        if (db.Database.IsSqlite())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
        else if (dbOptions.ApplyMigrationsOnStartup)
        {
            logger.LogWarning("Applying EF Core migrations at startup. Disable this in production.");
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (!await db.Database.CanConnectAsync(cancellationToken)) return;

        await SeedRolesAsync(sp.GetRequiredService<RoleManager<IdentityRole>>());
        await BootstrapAsync(sp, db, logger, cancellationToken);
        await EnsureSequencesAsync(sp, db, cancellationToken);
        await EnsureChartOfAccountsAsync(sp, db, cancellationToken);
    }

    private static async Task EnsureChartOfAccountsAsync(IServiceProvider sp, HmsDbContext db, CancellationToken ct)
    {
        var ledger = sp.GetRequiredService<HMS.Application.Accounting.ILedgerService>();
        foreach (var companyId in await db.Companies.Select(c => c.Id).ToListAsync(ct))
            await ledger.EnsureChartAsync(companyId, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureSequencesAsync(IServiceProvider sp, HmsDbContext db, CancellationToken ct)
    {
        var numbers = sp.GetRequiredService<INumberSequence>();
        foreach (var branchId in await db.Branches.Select(b => b.Id).ToListAsync(ct))
            await numbers.EnsureCountersAsync(branchId, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roles)
    {
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));
        }
    }

    private static async Task BootstrapAsync(IServiceProvider sp, HmsDbContext db, ILogger logger, CancellationToken ct)
    {
        var options = sp.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        if (!options.Enabled || await db.Users.AnyAsync(ct)) return;

        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            logger.LogWarning("Bootstrap is enabled but Bootstrap:AdminEmail / Bootstrap:AdminPassword are not configured. Skipping.");
            return;
        }

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var company = await db.Companies.SingleOrDefaultAsync(c => c.Code == options.CompanyCode.ToUpper(), ct)
            ?? db.Companies.Add(new Company(options.CompanyName, options.CompanyCode)).Entity;
        var branch = new Branch(company.Id, options.BranchName, options.BranchCode, options.TimeZoneId);
        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);

        var admin = new ApplicationUser
        {
            UserName = options.AdminEmail,
            Email = options.AdminEmail,
            EmailConfirmed = true,
            FullName = options.AdminFullName,
            CompanyId = company.Id,
            MustChangePassword = true,
            CreatedAtUtc = DateTime.UtcNow,
            LockoutEnabled = true,
        };
        var created = await users.CreateAsync(admin, options.AdminPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException("Bootstrap admin could not be created: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(admin, Roles.SystemAdmin);
        db.UserBranches.Add(new HotelUserBranch(admin.Id, company.Id, branch.Id, isDefault: true));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Bootstrap created company {Company}, branch {Branch} and the first SystemAdmin.", company.Code, branch.Code);
    }
}
