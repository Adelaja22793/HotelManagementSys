namespace HMS.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>"SqlServer" (production) or "Sqlite" (local demos and fast tests only).</summary>
    public string Provider { get; set; } = "SqlServer";

    /// <summary>
    /// Apply EF migrations at startup. Keep false in production: release reviewed idempotent scripts instead
    /// (see docs/DEPLOYMENT.md).
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}
