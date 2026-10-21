using HMS.Application.Abstractions;
using HMS.Application.Accounting;
using HMS.Application.Common;
using HMS.Contracts.Inventory;
using HMS.Domain.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Inventory;

public interface IInventoryService
{
    Task<IReadOnlyList<SupplierDto>> ListSuppliersAsync(Guid branchId, CancellationToken ct = default);
    Task<SupplierDto> SaveSupplierAsync(Guid branchId, Guid? supplierId, SaveSupplierRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<StockLocationDto>> ListLocationsAsync(Guid branchId, CancellationToken ct = default);
    Task<StockLocationDto> SaveLocationAsync(Guid branchId, Guid? locationId, SaveStockLocationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<InventoryItemDto>> ListItemsAsync(Guid branchId, string? search, CancellationToken ct = default);
    Task<InventoryItemDto> SaveItemAsync(Guid branchId, Guid? itemId, SaveInventoryItemRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<StockBalanceDto>> ListBalancesAsync(Guid branchId, Guid? locationId, bool belowReorderOnly, CancellationToken ct = default);
    Task<IReadOnlyList<StockMovementDto>> ListMovementsAsync(Guid branchId, Guid? itemId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<StockMovementDto> ReceiveAsync(Guid branchId, StockReceiptRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<StockMovementDto?> CountAsync(Guid branchId, StockCountRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<StockMovementDto>> TransferAsync(Guid branchId, StockTransferRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<StockMovementDto> WasteAsync(Guid branchId, StockWastageRequest request, string? idempotencyKey, CancellationToken ct = default);
}

internal sealed class InventoryService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    IStockService stock,
    ILedgerService ledger) : IInventoryService
{
    public async Task<IReadOnlyList<SupplierDto>> ListSuppliersAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return (await db.Suppliers.AsNoTracking().Where(s => s.BranchId == branchId).OrderBy(s => s.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<SupplierDto> SaveSupplierAsync(Guid branchId, Guid? supplierId, SaveSupplierRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Code), request.Code, 20).Require(nameof(request.Name), request.Name, 200)
            .When(request.PaymentTermsDays is < 0 or > 365, nameof(request.PaymentTermsDays), "Payment terms must be 0–365 days.")
            .ThrowIfInvalid();
        var code = request.Code.Trim().ToUpperInvariant();
        var supplier = supplierId is { } id ? await db.Suppliers.GetOwnedAsync(branchId, id, "Supplier", ct) : null;
        if (supplier is not null && request.ConcurrencyStamp is { } stamp) BranchOperations.EnsureStamp(supplier, stamp, "supplier");
        if (await db.Suppliers.AnyAsync(s => s.BranchId == branchId && s.Code == code && s.Id != (supplierId ?? Guid.Empty), ct))
            throw new ConflictException("SUPPLIER_CODE_EXISTS", $"Supplier code {code} is already used.");
        if (supplier is null) db.Suppliers.Add(supplier = new Supplier { CompanyId = scope.CompanyId, BranchId = branchId });
        supplier.Code = code;
        supplier.Name = request.Name.Trim();
        supplier.ContactName = Clean(request.ContactName);
        supplier.Phone = Clean(request.Phone);
        supplier.Email = Clean(request.Email);
        supplier.Address = Clean(request.Address);
        supplier.PaymentTermsDays = request.PaymentTermsDays;
        supplier.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(supplier);
    }

    public async Task<IReadOnlyList<StockLocationDto>> ListLocationsAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return await db.StockLocations.AsNoTracking().Where(l => l.BranchId == branchId).OrderBy(l => l.Code)
            .Select(l => new StockLocationDto(l.Id, l.Code, l.Name, l.IsActive)).ToListAsync(ct);
    }

    public async Task<StockLocationDto> SaveLocationAsync(Guid branchId, Guid? locationId, SaveStockLocationRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Code), request.Code, 20).Require(nameof(request.Name), request.Name, 200).ThrowIfInvalid();
        var code = request.Code.Trim().ToUpperInvariant();
        var location = locationId is { } id ? await db.StockLocations.GetOwnedAsync(branchId, id, "Stock location", ct) : null;
        if (await db.StockLocations.AnyAsync(l => l.BranchId == branchId && l.Code == code && l.Id != (locationId ?? Guid.Empty), ct))
            throw new ConflictException("LOCATION_CODE_EXISTS", $"Location code {code} is already used.");
        if (location is not null && !request.IsActive && await db.StockBalances.AnyAsync(b => b.LocationId == location.Id && b.Quantity != 0, ct))
            throw new ConflictException("LOCATION_HAS_STOCK", "Move or write off the stock before deactivating the location.");
        if (location is null) db.StockLocations.Add(location = new StockLocation { CompanyId = scope.CompanyId, BranchId = branchId });
        location.Code = code;
        location.Name = request.Name.Trim();
        location.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return new StockLocationDto(location.Id, location.Code, location.Name, location.IsActive);
    }

    public async Task<IReadOnlyList<InventoryItemDto>> ListItemsAsync(Guid branchId, string? search, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.InventoryItems.AsNoTracking().Where(i => i.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(i => i.Sku.Contains(term) || i.Name.Contains(term));
        }
        var items = await q.OrderBy(i => i.Name).Take(500).ToListAsync(ct);
        var ids = items.Select(i => i.Id).ToList();
        var onHand = (await db.StockBalances.AsNoTracking().Where(b => ids.Contains(b.ItemId)).Select(b => new { b.ItemId, b.Quantity }).ToListAsync(ct))
            .GroupBy(b => b.ItemId).ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));
        return items.Select(i => ToDto(i, onHand.GetValueOrDefault(i.Id))).ToList();
    }

    public async Task<InventoryItemDto> SaveItemAsync(Guid branchId, Guid? itemId, SaveInventoryItemRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Sku), request.Sku, 40).Require(nameof(request.Name), request.Name, 200)
            .Require(nameof(request.Unit), request.Unit, 20)
            .When(request.ReorderLevel < 0, nameof(request.ReorderLevel), "Reorder level cannot be negative.")
            .When(!DefaultChart.Keys.Contains(request.ExpenseAccountKey ?? string.Empty), nameof(request.ExpenseAccountKey),
                $"Use a posting key such as {AccountKeys.FoodCost}, {AccountKeys.HousekeepingSupplies} or {AccountKeys.Repairs}.")
            .ThrowIfInvalid();
        var sku = request.Sku.Trim().ToUpperInvariant();
        var item = itemId is { } id ? await db.InventoryItems.GetOwnedAsync(branchId, id, "Inventory item", ct) : null;
        if (item is not null && request.ConcurrencyStamp is { } stamp) BranchOperations.EnsureStamp(item, stamp, "item");
        if (await db.InventoryItems.AnyAsync(i => i.BranchId == branchId && i.Sku == sku && i.Id != (itemId ?? Guid.Empty), ct))
            throw new ConflictException("SKU_EXISTS", $"SKU {sku} is already used.");
        if (item is null) db.InventoryItems.Add(item = new InventoryItem { CompanyId = scope.CompanyId, BranchId = branchId });
        item.Sku = sku;
        item.Name = request.Name.Trim();
        item.Unit = request.Unit.Trim();
        item.Category = Clean(request.Category);
        item.ReorderLevel = request.ReorderLevel;
        item.ExpenseAccountKey = request.ExpenseAccountKey!;
        item.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        var onHand = (await db.StockBalances.Where(b => b.ItemId == item.Id).Select(b => b.Quantity).ToListAsync(ct)).Sum();
        return ToDto(item, onHand);
    }

    public async Task<IReadOnlyList<StockBalanceDto>> ListBalancesAsync(Guid branchId, Guid? locationId, bool belowReorderOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.StockBalances.AsNoTracking().Include(b => b.Item).Include(b => b.Location).Where(b => b.BranchId == branchId);
        if (locationId is { } loc) q = q.Where(b => b.LocationId == loc);
        var balances = await q.ToListAsync(ct);
        var totals = balances.GroupBy(b => b.ItemId).ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));
        var result = balances
            .Select(b => new StockBalanceDto(b.ItemId, b.Item!.Sku, b.Item.Name, b.Item.Unit, b.LocationId, b.Location!.Code, b.Quantity, b.AverageCost,
                b.Value, totals[b.ItemId] <= b.Item.ReorderLevel))
            .Where(b => !belowReorderOnly || b.BelowReorderLevel)
            .OrderBy(b => b.ItemName).ThenBy(b => b.LocationCode)
            .ToList();
        return result;
    }

    public async Task<IReadOnlyList<StockMovementDto>> ListMovementsAsync(Guid branchId, Guid? itemId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.StockMovements.AsNoTracking().Include(m => m.Item).Include(m => m.Location).Where(m => m.BranchId == branchId);
        if (itemId is { } item) q = q.Where(m => m.ItemId == item);
        if (from is { } f) q = q.Where(m => m.BusinessDate >= f);
        if (to is { } t) q = q.Where(m => m.BusinessDate <= t);
        var movements = await q.OrderByDescending(m => m.CreatedAtUtc).Take(500).ToListAsync(ct);
        return movements.Select(ToDto).ToList();
    }

    public async Task<StockMovementDto> ReceiveAsync(Guid branchId, StockReceiptRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var key = RequireKey(idempotencyKey);
        new Validation().Require(nameof(request.Reason), request.Reason, 300)
            .When(request.Quantity <= 0, nameof(request.Quantity), "Quantity must be positive.")
            .When(request.UnitCost < 0, nameof(request.UnitCost), "Unit cost cannot be negative.")
            .ThrowIfInvalid();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var movement = await stock.AddAsync(scope, request.ItemId, request.LocationId, request.Quantity, request.UnitCost, StockMovementType.AdjustmentIn,
            "StockReceipt", key, request.Reason.Trim(), ct);
        await ledger.PostAsync(scope, "StockReceipt", key, $"Stock received without order: {request.Reason.Trim()}",
            [LedgerLine.Dr(AccountKeys.Inventory, movement.TotalCost), LedgerLine.Cr(AccountKeys.OpeningBalances, movement.TotalCost)], ct);
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await GetMovementAsync(movement.Id, ct);
    }

    public async Task<StockMovementDto?> CountAsync(Guid branchId, StockCountRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var key = RequireKey(idempotencyKey);
        new Validation().Require(nameof(request.Reason), request.Reason, 300)
            .When(request.CountedQuantity < 0, nameof(request.CountedQuantity), "Counted quantity cannot be negative.").ThrowIfInvalid();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.StockMovements.AsNoTracking().FirstOrDefaultAsync(m => m.BranchId == branchId && m.SourceType == "StockCount" && m.SourceId == key, ct);
        if (existing is not null) return await GetMovementAsync(existing.Id, ct);

        var balance = await db.StockBalances.SingleOrDefaultAsync(b => b.BranchId == branchId && b.ItemId == request.ItemId && b.LocationId == request.LocationId, ct);
        var current = balance?.Quantity ?? 0;
        var difference = request.CountedQuantity - current;
        if (difference == 0) return null;

        StockMovement movement;
        if (difference > 0)
        {
            var cost = request.UnitCostForIncrease ?? balance?.AverageCost
                ?? throw new ValidationAppException(nameof(request.UnitCostForIncrease), "Enter a unit cost for stock that has never been received.");
            movement = await stock.AddAsync(scope, request.ItemId, request.LocationId, difference, cost, StockMovementType.AdjustmentIn, "StockCount", key, request.Reason.Trim(), ct);
        }
        else
        {
            movement = await stock.RemoveAsync(scope, request.ItemId, request.LocationId, -difference, StockMovementType.AdjustmentOut, "StockCount", key, request.Reason.Trim(), ct);
        }
        // Gains credit and losses debit the shrinkage account.
        await ledger.PostAsync(scope, "StockCount", key, $"Stock count adjustment: {request.Reason.Trim()}",
            [LedgerLine.Dr(AccountKeys.Inventory, movement.TotalCost), LedgerLine.Cr(AccountKeys.InventoryAdjustments, movement.TotalCost)], ct);
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await GetMovementAsync(movement.Id, ct);
    }

    public async Task<IReadOnlyList<StockMovementDto>> TransferAsync(Guid branchId, StockTransferRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var key = RequireKey(idempotencyKey);
        new Validation().When(request.Quantity <= 0, nameof(request.Quantity), "Quantity must be positive.")
            .When(request.FromLocationId == request.ToLocationId, nameof(request.ToLocationId), "Choose a different destination.").ThrowIfInvalid();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reason = Clean(request.Reason);
        var outMove = await stock.RemoveAsync(scope, request.ItemId, request.FromLocationId, request.Quantity, StockMovementType.TransferOut, "StockTransfer", key, reason, ct);
        var inMove = await stock.AddAsync(scope, request.ItemId, request.ToLocationId, request.Quantity, outMove.UnitCost, StockMovementType.TransferIn, "StockTransfer", key, reason, ct);
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return [await GetMovementAsync(outMove.Id, ct), await GetMovementAsync(inMove.Id, ct)];
    }

    public async Task<StockMovementDto> WasteAsync(Guid branchId, StockWastageRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var key = RequireKey(idempotencyKey);
        new Validation().Require(nameof(request.Reason), request.Reason, 300)
            .When(request.Quantity <= 0, nameof(request.Quantity), "Quantity must be positive.").ThrowIfInvalid();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var movement = await stock.RemoveAsync(scope, request.ItemId, request.LocationId, request.Quantity, StockMovementType.Wastage, "StockWastage", key, request.Reason.Trim(), ct);
        await ledger.PostAsync(scope, "StockWastage", key, $"Stock written off: {request.Reason.Trim()}",
            [LedgerLine.Dr(AccountKeys.InventoryAdjustments, -movement.TotalCost), LedgerLine.Cr(AccountKeys.Inventory, -movement.TotalCost)], ct);
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await GetMovementAsync(movement.Id, ct);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "The stock balance changed while you were working. Try again.");
        }
    }

    private async Task<StockMovementDto> GetMovementAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.StockMovements.AsNoTracking().Include(m => m.Item).Include(m => m.Location).SingleAsync(m => m.Id == id, ct));

    internal static string RequireKey(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Length > 100
            ? throw new ValidationAppException("Idempotency-Key", "An Idempotency-Key header (max 100 characters) is required for stock postings.")
            : key.Trim();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SupplierDto ToDto(Supplier s) => new(s.Id, s.Code, s.Name, s.ContactName, s.Phone, s.Email, s.Address, s.PaymentTermsDays, s.IsActive, s.ConcurrencyStamp);

    private static InventoryItemDto ToDto(InventoryItem i, decimal onHand) =>
        new(i.Id, i.Sku, i.Name, i.Unit, i.Category, i.ReorderLevel, i.ExpenseAccountKey, i.IsActive, onHand, i.ConcurrencyStamp);

    internal static StockMovementDto ToDto(StockMovement m) => new(m.Id, m.ItemId, m.Item?.Sku ?? string.Empty, m.LocationId, m.Location?.Code ?? string.Empty,
        m.Type.ToString(), m.Quantity, m.UnitCost, m.TotalCost, m.BusinessDate, m.SourceType, m.SourceId, m.Reference, m.CreatedAtUtc);
}
