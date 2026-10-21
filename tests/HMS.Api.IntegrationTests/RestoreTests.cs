using System.Net;
using System.Net.Http.Headers;
using System.Text;
using HMS.Contracts.Documents;
using HMS.Contracts.Operations;
using Microsoft.Data.SqlClient;

namespace HMS.Api.IntegrationTests;

/// <summary>Opens the API on a restored copy of another factory's database and document folder.</summary>
internal sealed class RestoredCopyFactory(string databaseName, string dataRoot) : HmsApiFactory(databaseName, dataRoot);

/// <summary>
/// The blueprint asks for real restore tests, not only RESTORE VERIFYONLY: back up the database, restore it under a new
/// name next to a copy of the document folder, and prove the copy is complete and consistent.
/// </summary>
public sealed class RestoreTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    private static byte[] Pdf(string body) => Encoding.ASCII.GetBytes("%PDF-1.4\n" + body + "\n%%EOF");

    private static async Task<DocumentDto> UploadAsync(HotelSetup hotel, string name, string body)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Pdf(body));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", name);
        form.Add(new StringContent("Restore " + name), "title");
        form.Add(new StringContent("Incident"), "category");
        return await ApiAssert.OkAsync<DocumentDto>(await hotel.Admin.PostAsync(hotel.P("documents"), form), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Live_system_is_consistent_and_a_missing_file_is_reported()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var stay = await hotel.BookAsync(nights: 1);
        await hotel.OpenShiftAsync(hotel.Admin);
        (await hotel.PayAsync(hotel.Admin, stay.FolioId!.Value, 10_000)).EnsureSuccessStatusCode();
        var document = await UploadAsync(hotel, "live.pdf", "live check");
        (await hotel.Admin.PostAsync("/api/v1/system/jobs/run", null)).EnsureSuccessStatusCode();

        var check = await ApiAssert.OkAsync<RestoreCheckDto>(await hotel.Admin.GetAsync("/api/v1/system/restore-check"));
        Assert.True(check.Consistent, string.Join("\n", check.Problems));
        Assert.True(check.JournalEntriesChecked > 0);
        Assert.Equal(0, check.UnbalancedJournalEntries);

        var reception = await hotel.StaffAsync(HotelSetup.Receptionist);
        Assert.Equal(HttpStatusCode.Forbidden, (await reception.GetAsync("/api/v1/system/restore-check")).StatusCode);

        // Losing a file is caught.
        var released = Directory.EnumerateFiles(Path.Combine(factory.DataRoot, "documents", "released"), "*", SearchOption.AllDirectories).First();
        var bytes = await File.ReadAllBytesAsync(released);
        File.Delete(released);
        try
        {
            check = await ApiAssert.OkAsync<RestoreCheckDto>(await hotel.Admin.GetAsync("/api/v1/system/restore-check"));
            Assert.False(check.Consistent);
            Assert.True(check.FilesMissing >= 1);
            Assert.Contains(check.Problems, p => p.Contains("is missing", StringComparison.Ordinal));
        }
        finally
        {
            await File.WriteAllBytesAsync(released, bytes);
        }
        Assert.NotEqual(Guid.Empty, document.Id);
    }

    [SqlServerOnlyFact]
    public async Task Backup_restored_under_a_new_name_is_complete_and_consistent()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var stay = await hotel.BookAsync(nights: 2);
        await hotel.OpenShiftAsync(hotel.Admin);
        (await hotel.PayAsync(hotel.Admin, stay.FolioId!.Value, 25_000)).EnsureSuccessStatusCode();
        await UploadAsync(hotel, "before-backup.pdf", "restore me");
        (await hotel.Admin.PostAsync("/api/v1/system/jobs/run", null)).EnsureSuccessStatusCode();
        var original = await ApiAssert.OkAsync<RestoreCheckDto>(await hotel.Admin.GetAsync("/api/v1/system/restore-check"));
        Assert.True(original.Consistent, string.Join("\n", original.Problems));

        // Back up with checksums, verify the media, then restore under a new name, as an isolated environment would.
        var restoredName = factory.DatabaseName + "_restored";
        await using (var master = new SqlConnection($"{HmsApiFactory.SqlServerBase};Database=master;TrustServerCertificate=True"))
        {
            await master.OpenAsync();
            var backupFolder = (string)(await Scalar(master, "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))"))!;
            var dataFolder = (string)(await Scalar(master, "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000))"))!;
            var separator = backupFolder.Contains('\\') ? "\\" : "/";
            var backupFile = $"{backupFolder.TrimEnd('/', '\\')}{separator}{factory.DatabaseName}_{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
            await Exec(master, $"BACKUP DATABASE [{factory.DatabaseName}] TO DISK = @f WITH COPY_ONLY, CHECKSUM, INIT", backupFile);
            await Exec(master, "RESTORE VERIFYONLY FROM DISK = @f WITH CHECKSUM", backupFile);

            var moves = new List<string>();
            await using (var list = new SqlCommand("RESTORE FILELISTONLY FROM DISK = @f", master))
            {
                list.Parameters.AddWithValue("@f", backupFile);
                await using var reader = await list.ExecuteReaderAsync();
                var i = 0;
                while (await reader.ReadAsync())
                {
                    var logical = reader.GetString(reader.GetOrdinal("LogicalName"));
                    var ext = reader.GetString(reader.GetOrdinal("Type")) == "L" ? "ldf" : "mdf";
                    moves.Add($"MOVE N'{logical.Replace("'", "''")}' TO N'{dataFolder.TrimEnd('/', '\\')}{separator}{restoredName}_{i++}.{ext}'");
                }
            }
            await Exec(master, $"RESTORE DATABASE [{restoredName}] FROM DISK = @f WITH {string.Join(", ", moves)}, CHECKSUM, RECOVERY", backupFile);
        }

        // Copy the document folder the way a file backup would.
        var restoredRoot = Path.Combine(Path.GetTempPath(), "hms-tests", Guid.NewGuid().ToString("N"));
        CopyFolder(Path.Combine(factory.DataRoot, "documents"), Path.Combine(restoredRoot, "documents"));

        using var restored = new RestoredCopyFactory(restoredName, restoredRoot);
        var (admin, _) = await restored.LoginAsAdminAsync();
        var check = await ApiAssert.OkAsync<RestoreCheckDto>(await admin.GetAsync("/api/v1/system/restore-check"));
        Assert.True(check.Consistent, string.Join("\n", check.Problems));
        Assert.Equal(original.RowCounts, check.RowCounts);
        Assert.Equal(original.DocumentVersionsChecked, check.DocumentVersionsChecked);
        Assert.Equal(0, check.PendingMigrations);

        // The restored copy serves the guest's folio and the released document.
        var folio = await admin.GetAsync(hotel.P($"folios/{stay.FolioId}"));
        Assert.Equal(HttpStatusCode.OK, folio.StatusCode);

        // A stray file in the restored folder is reported without failing the restore.
        var stray = Path.Combine(restoredRoot, "documents", "released", Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
        await File.WriteAllTextAsync(stray, "stray");
        check = await ApiAssert.OkAsync<RestoreCheckDto>(await admin.GetAsync("/api/v1/system/restore-check"));
        Assert.True(check.Consistent);
        Assert.Equal(1, check.OrphanFiles);
    }

    private static async Task<object?> Scalar(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task Exec(SqlConnection connection, string sql, string file)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        command.Parameters.AddWithValue("@f", file);
        await command.ExecuteNonQueryAsync();
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
