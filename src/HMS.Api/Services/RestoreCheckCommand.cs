using System.Text.Json;
using HMS.Application.Operations;
using HMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HMS.Api.Services;

/// <summary>
/// <c>dotnet HMS.Api.dll verify-restore [--skip-checksums]</c>: checks the configured database and document folder
/// against each other without starting the web server, applying migrations or creating any data.
/// Exit code 0 means consistent, 2 means problems were found, 3 means the database could not be opened. Used by scripts/restore-test.ps1.
/// </summary>
internal static class RestoreCheckCommand
{
    public const string Name = "verify-restore";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HmsDbContext>();
        if (!await db.Database.CanConnectAsync())
        {
            Console.Error.WriteLine("Cannot open the configured database. Check ConnectionStrings:HotelManagementDb.");
            return 3;
        }
        if (!(await db.Database.GetAppliedMigrationsAsync()).Any())
        {
            Console.Error.WriteLine("The database has no HMS tables. Was the restore pointed at the right database?");
            return 3;
        }
        var pending = (await db.Database.GetPendingMigrationsAsync()).Count();
        if (pending > 0)
        {
            // The data checks assume the current schema, so stop here rather than report misleading errors.
            Console.Error.WriteLine($"{pending} migration(s) are not applied. Restore a matching release or apply the reviewed migration script first.");
            return 2;
        }
        var result = await scope.ServiceProvider.GetRequiredService<IRestoreVerificationService>()
            .CheckAsync(pending, verifyChecksums: !args.Contains("--skip-checksums"), CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return result.Consistent ? 0 : 2;
    }
}
