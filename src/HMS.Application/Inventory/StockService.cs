using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Inventory;

/// <summary>
/// The single place stock changes. Each change updates the balance and appends an immutable movement
/// in the caller's transaction. A movement is keyed by its source, so a retried operation never moves
/// stock twice. The balance's concurrency stamp turns two simultaneous changes into a clean conflict.
/// </summary>
public interface IStockService
{
    Task<StockMovement> AddAsync(BranchScope scope, Guid itemId, Guid locationId, decimal quantity, decimal unitCost, StockMovementType type,
        string sourceType, string sourceId, string? reference, CancellationToken ct);

    Task<StockMovement> RemoveAsync(BranchScope scope, Guid itemId, Guid locationId, decimal quantity, StockMovementType type,
        string sourceType, string sourceId, string? reference, CancellationToken ct);
}

internal sealed class StockService(IApplicationDbContext db) : IStockService
{
    public async Task<StockMovement> AddAsync(BranchScope scope, Guid itemId, Guid locationId, decimal quantity, decimal unitCost,
        StockMovementType type, string sourceType, string sourceId, string? reference, CancellationToken ct)
    {
        db.RequireTransaction();
        if (await FindAsync(scope, itemId, locationId, type, sourceType, sourceId, ct) is { } existing) return existing;
        var balance = await BalanceAsync(scope, itemId, locationId, ct);
        balance.Receive(quantity, unitCost);
        return Append(scope, balance, type, quantity, unitCost, sourceType, sourceId, reference);
    }

    public async Task<StockMovement> RemoveAsync(BranchScope scope, Guid itemId, Guid locationId, decimal quantity, StockMovementType type,
        string sourceType, string sourceId, string? reference, CancellationToken ct)
    {
        db.RequireTransaction();
        if (await FindAsync(scope, itemId, locationId, type, sourceType, sourceId, ct) is { } existing) return existing;
        var balance = await BalanceAsync(scope, itemId, locationId, ct);
        var cost = balance.Remove(quantity);
        return Append(scope, balance, type, -quantity, cost, sourceType, sourceId, reference);
    }

    private StockMovement Append(BranchScope scope, StockBalance balance, StockMovementType type, decimal signedQuantity, decimal unitCost,
        string sourceType, string sourceId, string? reference)
    {
        balance.UpdatedAtUtc = scope.UtcNow;
        if (db.ChangeTracker.Entries<StockBalance>().Any(e => e.Entity == balance && e.State != EntityState.Added)) db.Touch(balance);
        var movement = new StockMovement(scope.CompanyId, scope.BranchId, balance.ItemId, balance.LocationId, type, signedQuantity, unitCost,
            scope.BusinessDate, sourceType, sourceId, reference, scope.UserId, scope.UtcNow);
        db.StockMovements.Add(movement);
        return movement;
    }

    private async Task<StockMovement?> FindAsync(BranchScope scope, Guid itemId, Guid locationId, StockMovementType type, string sourceType,
        string sourceId, CancellationToken ct) =>
        db.StockMovements.Local.FirstOrDefault(m => Matches(m, scope.BranchId, itemId, locationId, type, sourceType, sourceId))
        ?? await db.StockMovements.SingleOrDefaultAsync(m => m.BranchId == scope.BranchId && m.ItemId == itemId && m.LocationId == locationId
            && m.Type == type && m.SourceType == sourceType && m.SourceId == sourceId, ct);

    private static bool Matches(StockMovement m, Guid branchId, Guid itemId, Guid locationId, StockMovementType type, string sourceType, string sourceId) =>
        m.BranchId == branchId && m.ItemId == itemId && m.LocationId == locationId && m.Type == type && m.SourceType == sourceType && m.SourceId == sourceId;

    private async Task<StockBalance> BalanceAsync(BranchScope scope, Guid itemId, Guid locationId, CancellationToken ct)
    {
        var balance = db.StockBalances.Local.FirstOrDefault(b => b.BranchId == scope.BranchId && b.ItemId == itemId && b.LocationId == locationId)
            ?? await db.StockBalances.SingleOrDefaultAsync(b => b.BranchId == scope.BranchId && b.ItemId == itemId && b.LocationId == locationId, ct);
        if (balance is not null) return balance;

        var item = await db.InventoryItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.BranchId == scope.BranchId, ct)
            ?? throw new NotFoundException("Inventory item", itemId);
        var location = await db.StockLocations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == locationId && l.BranchId == scope.BranchId, ct)
            ?? throw new NotFoundException("Stock location", locationId);
        if (!item.IsActive) throw new ConflictException("ITEM_INACTIVE", $"{item.Name} is inactive.");
        if (!location.IsActive) throw new ConflictException("LOCATION_INACTIVE", $"{location.Name} is inactive.");
        balance = new StockBalance { CompanyId = scope.CompanyId, BranchId = scope.BranchId, ItemId = itemId, LocationId = locationId };
        db.StockBalances.Add(balance);
        return balance;
    }
}
