namespace HMS.Domain.Common;

/// <summary>
/// Optimistic concurrency token. The persistence layer issues a new stamp on every update,
/// so two users editing the same record cannot both win silently. Works on SQL Server and SQLite.
/// </summary>
public interface IConcurrencyTracked
{
    Guid ConcurrencyStamp { get; set; }
}
