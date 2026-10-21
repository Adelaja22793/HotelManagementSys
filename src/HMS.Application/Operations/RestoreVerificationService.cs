using System.Security.Cryptography;
using HMS.Application.Abstractions;
using HMS.Application.Documents;
using HMS.Contracts.Operations;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Operations;

public interface IRestoreVerificationService
{
    /// <summary>
    /// Checks that every document version has its file (with the stored SHA-256), lists stored files no record points to,
    /// and checks every journal entry still balances. Run it against a restored copy before trusting a backup.
    /// </summary>
    Task<RestoreCheckDto> CheckAsync(int pendingMigrations, bool verifyChecksums, CancellationToken ct);
}

internal sealed class RestoreVerificationService(IApplicationDbContext db, IDocumentStorage storage, IClock clock) : IRestoreVerificationService
{
    private const int MaxProblems = 200;

    public async Task<RestoreCheckDto> CheckAsync(int pendingMigrations, bool verifyChecksums, CancellationToken ct)
    {
        var problems = new List<string>();
        void Problem(string text)
        {
            if (problems.Count < MaxProblems) problems.Add(text);
        }

        // 1. Every live document version has its file in the area its scan status says.
        var versions = await db.DocumentVersions.AsNoTracking()
            .Join(db.Documents.AsNoTracking(), v => v.DocumentId, d => d.Id,
                (v, d) => new { v.Id, v.StorageKey, v.ScanStatus, v.Sha256, DocumentDeleted = d.Status == DocumentStatus.Deleted })
            .ToListAsync(ct);
        var expected = new Dictionary<string, bool>(StringComparer.Ordinal); // key -> released
        int missing = 0, mismatched = 0;
        foreach (var v in versions)
        {
            if (v.DocumentDeleted || v.ScanStatus == ScanStatus.Infected) continue; // files are removed on purpose
            var released = v.ScanStatus == ScanStatus.Clean;
            expected[v.StorageKey] = released;
            await using var file = released
                ? await storage.OpenReleasedAsync(v.StorageKey, ct)
                : await storage.OpenQuarantineAsync(v.StorageKey, ct);
            if (file is null)
            {
                missing++;
                Problem($"Document version {v.Id}: file {v.StorageKey} is missing from {(released ? "released" : "quarantine")}.");
                continue;
            }
            if (!verifyChecksums || string.IsNullOrEmpty(v.Sha256)) continue;
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            if (!hash.Equals(v.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                mismatched++;
                Problem($"Document version {v.Id}: file {v.StorageKey} does not match its recorded SHA-256.");
            }
        }

        // 2. Files nothing points to. Usually harmless (an upload interrupted before its record was saved), but they
        // can also mean the database is older than the file backup.
        var orphans = 0;
        await foreach (var file in storage.ListAsync(ct))
        {
            if (expected.TryGetValue(file.Key, out var released) && released == file.Released) continue;
            orphans++;
            Problem($"Stored file {(file.Released ? "released" : "quarantine")}/{file.Key} has no matching document record.");
        }

        // 3. Double entry still balances. Summed on the client so SQLite and SQL Server behave the same.
        var totals = new Dictionary<Guid, decimal>();
        await foreach (var line in db.JournalEntryLines.AsNoTracking()
                           .Select(l => new { l.JournalEntryId, l.Debit, l.Credit }).AsAsyncEnumerable().WithCancellation(ct))
        {
            totals[line.JournalEntryId] = totals.GetValueOrDefault(line.JournalEntryId) + line.Debit - line.Credit;
        }
        var unbalanced = totals.Where(t => t.Value != 0).ToList();
        foreach (var entry in unbalanced) Problem($"Journal entry {entry.Key} is out of balance by {entry.Value}.");

        if (pendingMigrations > 0) Problem($"{pendingMigrations} database migration(s) are not applied.");

        var counts = new Dictionary<string, int>
        {
            ["Guests"] = await db.Guests.CountAsync(ct),
            ["Reservations"] = await db.Reservations.CountAsync(ct),
            ["Folios"] = await db.Folios.CountAsync(ct),
            ["Payments"] = await db.Payments.CountAsync(ct),
            ["JournalEntries"] = totals.Count,
            ["Documents"] = await db.Documents.CountAsync(ct),
            ["DocumentVersions"] = versions.Count,
            ["AuditLogs"] = await db.AuditLogs.CountAsync(ct),
        };

        // Orphan files alone do not make a restore unusable; missing or altered files and broken books do.
        var consistent = missing == 0 && mismatched == 0 && unbalanced.Count == 0 && pendingMigrations == 0;
        return new RestoreCheckDto(clock.UtcNow, consistent, versions.Count, missing, mismatched, orphans,
            totals.Count, unbalanced.Count, pendingMigrations, counts, problems);
    }
}
