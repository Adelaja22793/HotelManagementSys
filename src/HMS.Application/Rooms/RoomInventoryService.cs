using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Contracts.Rooms;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Rooms;

/// <summary>
/// The one authoritative inventory service used by staff and (later) public booking. Allocation is a
/// single conditional UPDATE across every night of the stay: it only succeeds when each night has
/// capacity, so concurrent requests for the last room cannot both win.
/// </summary>
public interface IRoomInventoryService
{
    Task EnsureRowsAsync(Branch branch, Guid roomTypeId, DateOnly start, DateOnly end, CancellationToken ct);

    /// <summary>Allocates <paramref name="quantity"/> rooms for every night in [start, end). Returns true if it overbooked.</summary>
    Task<bool> AllocateAsync(Branch branch, Guid roomTypeId, DateOnly start, DateOnly end, int quantity, bool allowOverbooking, CancellationToken ct);

    Task ReleaseAsync(Guid branchId, Guid roomTypeId, DateOnly start, DateOnly end, int quantity, CancellationToken ct);

    Task AdjustBlockedAsync(Branch branch, Guid roomTypeId, DateOnly start, DateOnly end, int delta, CancellationToken ct);

    Task RefreshPhysicalCountAsync(Guid branchId, Guid roomTypeId, CancellationToken ct);

    Task<IReadOnlyList<InventoryDayDto>> GetAsync(Branch branch, DateOnly start, DateOnly end, CancellationToken ct);

    Task<int> GetAvailableAsync(Branch branch, Guid roomTypeId, DateOnly start, DateOnly end, CancellationToken ct);

    Task<InventoryReconciliationDto> ReconcileAsync(Branch branch, DateOnly start, DateOnly end, CancellationToken ct);
}

internal sealed class RoomInventoryService(IApplicationDbContext db) : IRoomInventoryService
{
    public async Task EnsureRowsAsync(Branch branch, Guid roomTypeId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        // Call before starting the business transaction. Concurrent creators race on the unique index:
        // the loser re-reads and only adds whatever is still missing (a deadlock victim simply retries).
        for (var attempt = 1; ; attempt++)
        {
            var existing = await db.RoomNightInventory
                .Where(r => r.BranchId == branch.Id && r.RoomTypeId == roomTypeId && r.Date >= start && r.Date < end)
                .Select(r => r.Date)
                .ToListAsync(ct);
            var missing = Dates(start, end).Except(existing).ToList();
            if (missing.Count == 0) return;

            var physical = await db.Rooms.CountAsync(r => r.BranchId == branch.Id && r.RoomTypeId == roomTypeId && r.IsActive, ct);
            var blocks = await ActiveBlocksAsync(branch.Id, roomTypeId, start, end, ct);

            var rows = missing.Select(date => new RoomNightInventory
            {
                CompanyId = branch.CompanyId,
                BranchId = branch.Id,
                RoomTypeId = roomTypeId,
                Date = date,
                PhysicalCount = physical,
                BlockedCount = blocks.Count(b => b.StartDate <= date && date < b.EndDate),
            }).ToList();

            db.RoomNightInventory.AddRange(rows);
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (Exception ex) when (attempt < 5 && (ex is DbUpdateException || ex.InnerException is DbUpdateException))
            {
                // Another request created some of these nights first; loop and re-check.
            }
            finally
            {
                // Inventory is only ever changed through set-based updates; never keep these rows tracked.
                foreach (var entry in db.ChangeTracker.Entries<RoomNightInventory>().Where(e => rows.Contains(e.Entity)).ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }

    public async Task<bool> AllocateAsync(Branch branch, Guid roomTypeId, DateOnly start, DateOnly end, int quantity, bool allowOverbooking, CancellationToken ct)
    {
        db.RequireTransaction();
        var nights = end.DayNumber - start.DayNumber;
        var stamp = Guid.NewGuid();

        var updated = await db.RoomNightInventory
            .Where(r => r.BranchId == branch.Id && r.RoomTypeId == roomTypeId && r.Date >= start && r.Date < end)
            .Where(r => (r.SellLimit != null && r.SellLimit < r.PhysicalCount - r.BlockedCount ? r.SellLimit.Value : r.PhysicalCount - r.BlockedCount)
                        - r.ConfirmedCount - r.HeldCount >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ConfirmedCount, r => r.ConfirmedCount + quantity)
                .SetProperty(r => r.ConcurrencyStamp, stamp), ct);

        if (updated == nights) return false;

        if (!allowOverbooking)
            throw new ConflictException("ROOM_NOT_AVAILABLE", "The selected room type is no longer available for every night of the stay.");

        // Authorised overbooking: allocate the nights that had no capacity too.
        await db.RoomNightInventory
            .Where(r => r.BranchId == branch.Id && r.RoomTypeId == roomTypeId && r.Date >= start && r.Date < end && r.ConcurrencyStamp != stamp)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ConfirmedCount, r => r.ConfirmedCount + quantity)
                .SetProperty(r => r.ConcurrencyStamp, stamp), ct);
        return true;
    }

    public async Task ReleaseAsync(Guid branchId, Guid roomTypeId, DateOnly start, DateOnly end, int quantity, CancellationToken ct)
    {
        if (end <= start) return;
        await db.RoomNightInventory
            .Where(r => r.BranchId == branchId && r.RoomTypeId == roomTypeId && r.Date >= start && r.Date < end && r.ConfirmedCount >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ConfirmedCount, r => r.ConfirmedCount - quantity)
                .SetProperty(r => r.ConcurrencyStamp, Guid.NewGuid()), ct);
    }

    public async Task AdjustBlockedAsync(Branch branch, Guid roomTypeId, DateOnly start, DateOnly end, int delta, CancellationToken ct)
    {
        await EnsureRowsAsync(branch, roomTypeId, start, end, ct);
        await db.RoomNightInventory
            .Where(r => r.BranchId == branch.Id && r.RoomTypeId == roomTypeId && r.Date >= start && r.Date < end && r.BlockedCount + delta >= 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.BlockedCount, r => r.BlockedCount + delta)
                .SetProperty(r => r.ConcurrencyStamp, Guid.NewGuid()), ct);
    }

    public async Task RefreshPhysicalCountAsync(Guid branchId, Guid roomTypeId, CancellationToken ct)
    {
        var physical = await db.Rooms.CountAsync(r => r.BranchId == branchId && r.RoomTypeId == roomTypeId && r.IsActive, ct);
        await db.RoomNightInventory
            .Where(r => r.BranchId == branchId && r.RoomTypeId == roomTypeId && r.PhysicalCount != physical)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PhysicalCount, physical)
                .SetProperty(r => r.ConcurrencyStamp, Guid.NewGuid()), ct);
    }

    public async Task<int> GetAvailableAsync(Branch branch, Guid roomTypeId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var days = await GetAsync(branch, start, end, ct);
        var forType = days.Where(d => d.RoomTypeId == roomTypeId).ToList();
        return forType.Count == 0 ? 0 : Math.Max(0, forType.Min(d => d.Available));
    }

    public async Task<IReadOnlyList<InventoryDayDto>> GetAsync(Branch branch, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var types = await db.RoomTypes.AsNoTracking().Where(t => t.BranchId == branch.Id && t.IsActive).OrderBy(t => t.Code).ToListAsync(ct);
        var rows = await db.RoomNightInventory.AsNoTracking()
            .Where(r => r.BranchId == branch.Id && r.Date >= start && r.Date < end)
            .ToListAsync(ct);
        var physical = await db.Rooms.Where(r => r.BranchId == branch.Id && r.IsActive)
            .GroupBy(r => r.RoomTypeId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var blocks = await ActiveBlocksAsync(branch.Id, null, start, end, ct);

        var result = new List<InventoryDayDto>();
        foreach (var type in types)
        {
            foreach (var date in Dates(start, end))
            {
                // A night without a row has never been allocated; derive it from source records.
                var row = rows.FirstOrDefault(r => r.RoomTypeId == type.Id && r.Date == date) ?? new RoomNightInventory
                {
                    RoomTypeId = type.Id,
                    Date = date,
                    PhysicalCount = physical.GetValueOrDefault(type.Id),
                    BlockedCount = blocks.Count(b => b.RoomTypeId == type.Id && b.StartDate <= date && date < b.EndDate),
                };
                result.Add(new InventoryDayDto(type.Id, type.Code, date, row.PhysicalCount, row.BlockedCount, row.SellLimit,
                    row.ConfirmedCount, row.HeldCount, Math.Max(0, row.Available)));
            }
        }
        return result;
    }

    public async Task<InventoryReconciliationDto> ReconcileAsync(Branch branch, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var rows = await db.RoomNightInventory.Where(r => r.BranchId == branch.Id && r.Date >= start && r.Date < end).ToListAsync(ct);
        var physical = await db.Rooms.Where(r => r.BranchId == branch.Id && r.IsActive)
            .GroupBy(r => r.RoomTypeId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var blocks = await ActiveBlocksAsync(branch.Id, null, start, end, ct);
        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => r.BranchId == branch.Id && r.ArrivalDate < end && r.DepartureDate > start
                        && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.CheckedIn))
            .Select(r => new { r.RoomTypeId, r.ArrivalDate, r.DepartureDate })
            .ToListAsync(ct);

        var corrections = new List<string>();
        foreach (var row in rows)
        {
            var expectedPhysical = physical.GetValueOrDefault(row.RoomTypeId);
            var expectedBlocked = blocks.Count(b => b.RoomTypeId == row.RoomTypeId && b.StartDate <= row.Date && row.Date < b.EndDate);
            var expectedConfirmed = reservations.Count(r => r.RoomTypeId == row.RoomTypeId && r.ArrivalDate <= row.Date && row.Date < r.DepartureDate);

            if (row.PhysicalCount != expectedPhysical || row.BlockedCount != expectedBlocked || row.ConfirmedCount != expectedConfirmed)
            {
                corrections.Add($"{row.Date:yyyy-MM-dd} {row.RoomTypeId}: physical {row.PhysicalCount}->{expectedPhysical}, " +
                                $"blocked {row.BlockedCount}->{expectedBlocked}, confirmed {row.ConfirmedCount}->{expectedConfirmed}");
                row.PhysicalCount = expectedPhysical;
                row.BlockedCount = expectedBlocked;
                row.ConfirmedCount = expectedConfirmed;
            }
        }
        await db.SaveChangesAsync(ct);
        return new InventoryReconciliationDto(rows.Count, corrections.Count, corrections);
    }

    private async Task<List<(Guid RoomTypeId, DateOnly StartDate, DateOnly EndDate)>> ActiveBlocksAsync(Guid branchId, Guid? roomTypeId,
        DateOnly start, DateOnly end, CancellationToken ct)
    {
        var query = db.RoomBlocks.AsNoTracking()
            .Where(b => b.BranchId == branchId && b.IsActive && b.StartDate < end && b.EndDate > start && b.Room!.IsActive);
        if (roomTypeId is { } typeId) query = query.Where(b => b.Room!.RoomTypeId == typeId);
        var list = await query.Select(b => new { b.Room!.RoomTypeId, b.StartDate, b.EndDate }).ToListAsync(ct);
        return list.Select(b => (b.RoomTypeId, b.StartDate, b.EndDate)).ToList();
    }

    private static IEnumerable<DateOnly> Dates(DateOnly start, DateOnly end)
    {
        for (var d = start; d < end; d = d.AddDays(1)) yield return d;
    }
}
