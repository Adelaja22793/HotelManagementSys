using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>Append-only audit evidence. Never updated or deleted by the application.</summary>
public class AuditLog : Entity
{
    public DateTime OccurredAtUtc { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    /// <summary>JSON list of changed property names (values are intentionally not stored for sensitive data).</summary>
    public string? ChangedProperties { get; set; }
    public string? Details { get; set; }
    public string? TraceId { get; set; }
}
