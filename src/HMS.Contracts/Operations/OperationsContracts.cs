namespace HMS.Contracts.Operations;

public sealed record VersionInfoDto(
    string ApiVersion,
    string MinimumDesktopVersion,
    string LatestDesktopVersion,
    string? DatabaseMigration,
    int PendingMigrations,
    DateTime ServerTimeUtc);

public sealed record AuditLogDto(
    Guid Id,
    DateTime OccurredAtUtc,
    string? UserId,
    string Action,
    string EntityType,
    string EntityId,
    string? ChangedProperties,
    string? TraceId);

/// <summary>
/// Result of checking a database and document store against each other, for example after a restore.
/// Problems lists at most 200 entries; the counts are complete.
/// </summary>
public sealed record RestoreCheckDto(
    DateTime CheckedAtUtc,
    bool Consistent,
    int DocumentVersionsChecked,
    int FilesMissing,
    int ChecksumMismatches,
    int OrphanFiles,
    int JournalEntriesChecked,
    int UnbalancedJournalEntries,
    int PendingMigrations,
    IReadOnlyDictionary<string, int> RowCounts,
    IReadOnlyList<string> Problems);
