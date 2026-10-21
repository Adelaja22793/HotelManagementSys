namespace HMS.Contracts.Inventory;

public sealed record SupplierDto(Guid Id, string Code, string Name, string? ContactName, string? Phone, string? Email, string? Address,
    int PaymentTermsDays, bool IsActive, Guid ConcurrencyStamp);

public sealed record SaveSupplierRequest(string Code, string Name, string? ContactName, string? Phone, string? Email, string? Address,
    int PaymentTermsDays, bool IsActive = true, Guid? ConcurrencyStamp = null);

public sealed record StockLocationDto(Guid Id, string Code, string Name, bool IsActive);

public sealed record SaveStockLocationRequest(string Code, string Name, bool IsActive = true);

/// <summary>ExpenseAccountKey: FoodCost, HousekeepingSupplies, Repairs or another account mapping key.</summary>
public sealed record InventoryItemDto(Guid Id, string Sku, string Name, string Unit, string? Category, decimal ReorderLevel,
    string ExpenseAccountKey, bool IsActive, decimal QuantityOnHand, Guid ConcurrencyStamp);

public sealed record SaveInventoryItemRequest(string Sku, string Name, string Unit, string? Category, decimal ReorderLevel,
    string ExpenseAccountKey, bool IsActive = true, Guid? ConcurrencyStamp = null);

public sealed record StockBalanceDto(Guid ItemId, string Sku, string ItemName, string Unit, Guid LocationId, string LocationCode,
    decimal Quantity, decimal AverageCost, decimal Value, bool BelowReorderLevel);

public sealed record StockMovementDto(Guid Id, Guid ItemId, string Sku, Guid LocationId, string LocationCode, string Type, decimal Quantity,
    decimal UnitCost, decimal TotalCost, DateOnly BusinessDate, string SourceType, string SourceId, string? Reference, DateTime CreatedAtUtc);

/// <summary>Count correction. CountedQuantity is the physical count; the difference is posted as an adjustment.</summary>
public sealed record StockCountRequest(Guid ItemId, Guid LocationId, decimal CountedQuantity, decimal? UnitCostForIncrease, string Reason);

public sealed record StockTransferRequest(Guid ItemId, Guid FromLocationId, Guid ToLocationId, decimal Quantity, string? Reason);

public sealed record StockWastageRequest(Guid ItemId, Guid LocationId, decimal Quantity, string Reason);

/// <summary>Opening balance or a receipt without a purchase order (e.g. donated stock). Requires inventory.manage.</summary>
public sealed record StockReceiptRequest(Guid ItemId, Guid LocationId, decimal Quantity, decimal UnitCost, string Reason);
