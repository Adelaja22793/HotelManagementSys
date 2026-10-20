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
