using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

public class Supplier : BranchEntity, IConcurrencyTracked
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public int PaymentTermsDays { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class StockLocation : BranchEntity, IConcurrencyTracked
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>A stock-keeping item. <see cref="ExpenseAccountKey"/> chooses the cost account when it is consumed.</summary>
public class InventoryItem : BranchEntity, IConcurrencyTracked
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = "each";
    public string? Category { get; set; }
    public decimal ReorderLevel { get; set; }
    /// <summary>Account mapping key used when the item is consumed or wasted, e.g. FoodCost or HousekeepingSupplies.</summary>
    public string ExpenseAccountKey { get; set; } = "FoodCost";
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Quantity and weighted-average cost of one item at one location. Changed only through the stock
/// service, which appends a <see cref="StockMovement"/> in the same transaction. The concurrency
/// stamp makes two simultaneous changes to the same balance fail cleanly instead of losing one.
/// </summary>
public class StockBalance : IConcurrencyTracked, IBranchOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ItemId { get; set; }
    public InventoryItem? Item { get; set; }
    public Guid LocationId { get; set; }
    public StockLocation? Location { get; set; }
    public decimal Quantity { get; private set; }
    /// <summary>Weighted-average unit cost, 4 decimals.</summary>
    public decimal AverageCost { get; private set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public decimal Value => Money.Round(Quantity * AverageCost);

    /// <summary>Adds stock and re-averages the cost.</summary>
    public void Receive(decimal quantity, decimal unitCost)
    {
        if (quantity <= 0) throw new DomainException("QUANTITY_INVALID", "Quantity must be positive.");
        if (unitCost < 0) throw new DomainException("COST_INVALID", "Unit cost cannot be negative.");
        var newQuantity = Quantity + quantity;
        AverageCost = newQuantity == 0 ? unitCost : Math.Round((Quantity * AverageCost + quantity * unitCost) / newQuantity, 4, MidpointRounding.AwayFromZero);
        Quantity = newQuantity;
    }

    /// <summary>Removes stock at the current average cost and returns that cost. Stock may not go negative.</summary>
    public decimal Remove(decimal quantity)
    {
        if (quantity <= 0) throw new DomainException("QUANTITY_INVALID", "Quantity must be positive.");
        if (quantity > Quantity)
            throw new DomainConflictException("INSUFFICIENT_STOCK", $"Only {Quantity:0.###} in stock; {quantity:0.###} requested.");
        Quantity -= quantity;
        return AverageCost;
    }
}

/// <summary>Immutable stock history. Quantity is signed: positive adds stock, negative removes it.</summary>
public class StockMovement : IBranchOwned
{
    private StockMovement() { }

    public StockMovement(Guid companyId, Guid branchId, Guid itemId, Guid locationId, StockMovementType type, decimal quantity,
        decimal unitCost, DateOnly businessDate, string sourceType, string sourceId, string? reference, string userId, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ItemId = itemId;
        LocationId = locationId;
        Type = type;
        Quantity = quantity;
        UnitCost = unitCost;
        TotalCost = Money.Round(quantity * unitCost);
        BusinessDate = businessDate;
        SourceType = sourceType;
        SourceId = sourceId;
        Reference = reference;
        CreatedByUserId = userId;
        CreatedAtUtc = utcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ItemId { get; private set; }
    public InventoryItem? Item { get; private set; }
    public Guid LocationId { get; private set; }
    public StockLocation? Location { get; private set; }
    public StockMovementType Type { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal TotalCost { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    /// <summary>What caused the movement, e.g. GoodsReceiptLine, KitchenTicket, WorkOrderPart, StockAdjustment.</summary>
    public string SourceType { get; private set; } = string.Empty;
    public string SourceId { get; private set; } = string.Empty;
    public string? Reference { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
}
