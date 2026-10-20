namespace HMS.Domain.Common;

/// <summary>Records who created/changed a row and when (UTC). Filled in by the persistence layer.</summary>
public interface IAuditableEntity
{
    DateTime CreatedAtUtc { get; set; }
    DateTime? UpdatedAtUtc { get; set; }
    string? CreatedByUserId { get; set; }
    string? UpdatedByUserId { get; set; }
}
