namespace HMS.Domain.Common;

/// <summary>A record that belongs to exactly one branch of one company.</summary>
public interface IBranchOwned
{
    Guid CompanyId { get; }
    Guid BranchId { get; }
}
