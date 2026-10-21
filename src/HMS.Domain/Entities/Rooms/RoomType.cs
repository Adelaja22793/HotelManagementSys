using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>Bookable room category (e.g. Standard, Deluxe). Inventory is sold per room type.</summary>
public class RoomType : BranchEntity, IConcurrencyTracked
{
    private RoomType() { }

    public RoomType(Guid companyId, Guid branchId, string code, string name, int maxAdults, int maxChildren, decimal baseRate)
    {
        CompanyId = companyId;
        BranchId = branchId;
        Code = Company.NormalizeCode(code);
        Update(name, null, maxAdults, maxChildren, baseRate);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int MaxAdults { get; private set; }
    public int MaxChildren { get; private set; }
    public int MaxOccupancy => MaxAdults + MaxChildren;
    /// <summary>Fallback nightly rate when no rate-plan price covers a night.</summary>
    public decimal BaseRate { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Update(string name, string? description, int maxAdults, int maxChildren, decimal baseRate)
    {
        Name = Guard.Required(name, "ROOM_TYPE_NAME_REQUIRED", "Room type name is required");
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (maxAdults < 1) throw new DomainException("OCCUPANCY_INVALID", "A room type must allow at least one adult.");
        if (maxChildren < 0) throw new DomainException("OCCUPANCY_INVALID", "Children cannot be negative.");
        MaxAdults = maxAdults;
        MaxChildren = maxChildren;
        BaseRate = Money.RequirePositive(baseRate, "RATE_INVALID");
    }

    public void EnsureFits(int adults, int children)
    {
        if (adults < 1) throw new DomainException("OCCUPANCY_INVALID", "At least one adult is required.");
        if (children < 0) throw new DomainException("OCCUPANCY_INVALID", "Children cannot be negative.");
        if (adults > MaxAdults || children > MaxChildren)
            throw new DomainException("OCCUPANCY_EXCEEDED", $"{Name} allows up to {MaxAdults} adults and {MaxChildren} children.");
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
