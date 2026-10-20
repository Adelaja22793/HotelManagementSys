namespace HMS.Domain.Common;

/// <summary>
/// Starting convention for branch-scoped records (blueprint section 6). Company-level records,
/// immutable journal lines and other specialised entities may use a different base.
/// </summary>
public abstract class BranchEntity : AuditableEntity, IBranchOwned
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
}
