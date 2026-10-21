using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Contracts.Rooms;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Rooms;

public interface IRoomService
{
    Task<IReadOnlyList<RoomTypeDto>> ListRoomTypesAsync(Guid branchId, CancellationToken ct = default);
    Task<RoomTypeDto> CreateRoomTypeAsync(Guid branchId, CreateRoomTypeRequest request, CancellationToken ct = default);
    Task<RoomTypeDto> UpdateRoomTypeAsync(Guid branchId, Guid roomTypeId, UpdateRoomTypeRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<RoomDto>> ListRoomsAsync(Guid branchId, string? status, CancellationToken ct = default);
    Task<RoomDto> CreateRoomAsync(Guid branchId, CreateRoomRequest request, CancellationToken ct = default);
    Task<RoomDto> UpdateRoomAsync(Guid branchId, Guid roomId, UpdateRoomRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<RoomBlockDto>> ListBlocksAsync(Guid branchId, bool activeOnly, CancellationToken ct = default);
    Task<RoomBlockDto> CreateBlockAsync(Guid branchId, CreateRoomBlockRequest request, CancellationToken ct = default);
    Task<RoomBlockDto> ReleaseBlockAsync(Guid branchId, Guid blockId, CancellationToken ct = default);

    /// <summary>Validates and adds a block for other modules (e.g. maintenance). Call EnsureBlockRowsAsync first, then this inside a transaction.</summary>
    Task<RoomBlock> AddBlockAsync(BranchScope scope, Room room, DateOnly start, DateOnly end, string reason, CancellationToken ct);
    Task EnsureBlockRowsAsync(BranchScope scope, Room room, DateOnly start, DateOnly end, CancellationToken ct);
    Task ReleaseBlockAsync(BranchScope scope, RoomBlock block, CancellationToken ct);

    Task<IReadOnlyList<InventoryDayDto>> GetInventoryAsync(Guid branchId, DateOnly start, DateOnly end, CancellationToken ct = default);
    Task SetSellLimitAsync(Guid branchId, SetSellLimitRequest request, CancellationToken ct = default);
    Task<InventoryReconciliationDto> ReconcileAsync(Guid branchId, DateOnly start, DateOnly end, CancellationToken ct = default);
}

internal sealed class RoomService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    IRoomInventoryService inventory) : IRoomService
{
    private const int MaxInventoryWindowDays = 370;

    public async Task<IReadOnlyList<RoomTypeDto>> ListRoomTypesAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var counts = await db.Rooms.Where(r => r.BranchId == branchId && r.IsActive)
            .GroupBy(r => r.RoomTypeId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var types = await db.RoomTypes.AsNoTracking().Where(t => t.BranchId == branchId).OrderBy(t => t.Code).ToListAsync(ct);
        return types.Select(t => ToDto(t, counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<RoomTypeDto> CreateRoomTypeAsync(Guid branchId, CreateRoomTypeRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Code), request.Code, 20).Require(nameof(request.Name), request.Name).ThrowIfInvalid();
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.RoomTypes.AnyAsync(t => t.BranchId == branchId && t.Code == code, ct))
            throw new ConflictException("ROOM_TYPE_CODE_EXISTS", $"Room type code '{code}' already exists.");

        var type = new RoomType(scope.CompanyId, branchId, code, request.Name, request.MaxAdults, request.MaxChildren, request.BaseRate);
        type.Update(request.Name, request.Description, request.MaxAdults, request.MaxChildren, request.BaseRate);
        db.RoomTypes.Add(type);
        await db.SaveChangesAsync(ct);
        return ToDto(type, 0);
    }

    public async Task<RoomTypeDto> UpdateRoomTypeAsync(Guid branchId, Guid roomTypeId, UpdateRoomTypeRequest request, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var type = await db.RoomTypes.GetOwnedAsync(branchId, roomTypeId, "Room type", ct);
        BranchOperations.EnsureStamp(type, request.ConcurrencyStamp, "room type");
        type.Update(request.Name, request.Description, request.MaxAdults, request.MaxChildren, request.BaseRate);
        if (request.IsActive) type.Activate(); else type.Deactivate();
        await db.SaveChangesAsync(ct);
        var count = await db.Rooms.CountAsync(r => r.RoomTypeId == type.Id && r.IsActive, ct);
        return ToDto(type, count);
    }

    public async Task<IReadOnlyList<RoomDto>> ListRoomsAsync(Guid branchId, string? status, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var query = db.Rooms.AsNoTracking().Include(r => r.RoomType).Where(r => r.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = EnumParser.Parse<RoomStatus>(status, nameof(status));
            query = query.Where(r => r.Status == parsed);
        }
        var rooms = await query.OrderBy(r => r.RoomNumber).ToListAsync(ct);
        var today = scope.BusinessDate;
        var blocked = await db.RoomBlocks.Where(b => b.BranchId == branchId && b.IsActive && b.StartDate <= today && b.EndDate > today)
            .Select(b => b.RoomId).ToListAsync(ct);
        return rooms.Select(r => ToDto(r, blocked.Contains(r.Id))).ToList();
    }

    public async Task<RoomDto> CreateRoomAsync(Guid branchId, CreateRoomRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.RoomNumber), request.RoomNumber, 20).ThrowIfInvalid();
        var type = await db.RoomTypes.GetOwnedAsync(branchId, request.RoomTypeId, "Room type", ct);
        var number = request.RoomNumber.Trim().ToUpperInvariant();
        if (await db.Rooms.AnyAsync(r => r.BranchId == branchId && r.RoomNumber == number, ct))
            throw new ConflictException("ROOM_NUMBER_EXISTS", $"Room {number} already exists.");

        var room = new Room(scope.CompanyId, branchId, type.Id, number, request.Floor);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        await inventory.RefreshPhysicalCountAsync(branchId, type.Id, ct);
        await tx.CommitAsync(ct);
        room = await db.Rooms.Include(r => r.RoomType).SingleAsync(r => r.Id == room.Id, ct);
        return ToDto(room, false);
    }

    public async Task<RoomDto> UpdateRoomAsync(Guid branchId, Guid roomId, UpdateRoomRequest request, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var room = await db.Rooms.Include(r => r.RoomType).GetOwnedAsync(branchId, roomId, "Room", ct);
        BranchOperations.EnsureStamp(room, request.ConcurrencyStamp, "room");
        var newType = await db.RoomTypes.GetOwnedAsync(branchId, request.RoomTypeId, "Room type", ct);
        var oldTypeId = room.RoomTypeId;

        if ((newType.Id != oldTypeId || !request.IsActive) &&
            await db.AssignedRoomNights.AnyAsync(a => a.RoomId == room.Id, ct))
            throw new ConflictException("ROOM_HAS_ASSIGNMENTS", "Unassign this room from upcoming reservations before changing its type or deactivating it.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        room.Update(newType.Id, request.Floor);
        if (request.IsActive) room.Activate(); else room.Deactivate();
        await db.SaveChangesAsync(ct);
        await inventory.RefreshPhysicalCountAsync(branchId, oldTypeId, ct);
        if (newType.Id != oldTypeId) await inventory.RefreshPhysicalCountAsync(branchId, newType.Id, ct);
        await tx.CommitAsync(ct);

        room = await db.Rooms.AsNoTracking().Include(r => r.RoomType).SingleAsync(r => r.Id == room.Id, ct);
        return ToDto(room, false);
    }

    public async Task<IReadOnlyList<RoomBlockDto>> ListBlocksAsync(Guid branchId, bool activeOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var query = db.RoomBlocks.AsNoTracking().Include(b => b.Room).Where(b => b.BranchId == branchId);
        if (activeOnly) query = query.Where(b => b.IsActive);
        var blocks = await query.OrderBy(b => b.StartDate).Take(500).ToListAsync(ct);
        return blocks.Select(ToDto).ToList();
    }

    public async Task<RoomBlockDto> CreateBlockAsync(Guid branchId, CreateRoomBlockRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var room = await db.Rooms.GetOwnedAsync(branchId, request.RoomId, "Room", ct);
        await EnsureBlockRowsAsync(scope, room, request.StartDate, request.EndDate, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var block = await AddBlockAsync(scope, room, request.StartDate, request.EndDate, request.Reason, ct);
        await tx.CommitAsync(ct);

        block = await db.RoomBlocks.Include(b => b.Room).SingleAsync(b => b.Id == block.Id, ct);
        return ToDto(block);
    }

    public async Task EnsureBlockRowsAsync(BranchScope scope, Room room, DateOnly start, DateOnly end, CancellationToken ct)
    {
        if (end <= start) throw new ValidationAppException("EndDate", "Block end must be after start.");
        if (end.DayNumber - start.DayNumber > MaxInventoryWindowDays)
            throw new ValidationAppException("EndDate", "Blocks can cover at most one year. Deactivate the room instead.");
        await inventory.EnsureRowsAsync(scope.Branch, room.RoomTypeId, start, end, ct);
    }

    public async Task<RoomBlock> AddBlockAsync(BranchScope scope, Room room, DateOnly start, DateOnly end, string reason, CancellationToken ct)
    {
        db.RequireTransaction();
        var block = new RoomBlock(scope.CompanyId, scope.BranchId, room.Id, start, end, reason);
        if (await db.RoomBlocks.AnyAsync(b => b.RoomId == room.Id && b.IsActive && b.StartDate < block.EndDate && b.EndDate > block.StartDate, ct))
            throw new ConflictException("ROOM_ALREADY_BLOCKED", "The room already has a block overlapping these dates.");

        // A block may not silently take a room from a guest; reassign them first.
        if (await db.AssignedRoomNights.AnyAsync(a => a.RoomId == room.Id && a.Date >= block.StartDate && a.Date < block.EndDate, ct))
            throw new ConflictException("ROOM_ASSIGNED", "The room is assigned to a reservation during these dates. Move the guest first.");

        db.RoomBlocks.Add(block);
        await db.SaveChangesAsync(ct);
        if (room.IsActive) await inventory.AdjustBlockedAsync(scope.Branch, room.RoomTypeId, block.StartDate, block.EndDate, +1, ct);
        return block;
    }

    public async Task<RoomBlockDto> ReleaseBlockAsync(Guid branchId, Guid blockId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var block = await db.RoomBlocks.Include(b => b.Room).GetOwnedAsync(branchId, blockId, "Room block", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ReleaseBlockAsync(scope, block, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(block);
    }

    public async Task ReleaseBlockAsync(BranchScope scope, RoomBlock block, CancellationToken ct)
    {
        db.RequireTransaction();
        var room = block.Room ?? await db.Rooms.SingleAsync(r => r.Id == block.RoomId, ct);
        block.Release(scope.UtcNow);
        await db.SaveChangesAsync(ct);
        if (room.IsActive) await inventory.AdjustBlockedAsync(scope.Branch, room.RoomTypeId, block.StartDate, block.EndDate, -1, ct);
    }

    public async Task<IReadOnlyList<InventoryDayDto>> GetInventoryAsync(Guid branchId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        ValidateWindow(start, end, 62);
        return await inventory.GetAsync(scope.Branch, start, end, ct);
    }

    public async Task SetSellLimitAsync(Guid branchId, SetSellLimitRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        ValidateWindow(request.StartDate, request.EndDate, MaxInventoryWindowDays);
        if (request.SellLimit is < 0) throw new ValidationAppException(nameof(request.SellLimit), "Sell limit cannot be negative.");
        await db.RoomTypes.GetOwnedAsync(branchId, request.RoomTypeId, "Room type", ct);

        await inventory.EnsureRowsAsync(scope.Branch, request.RoomTypeId, request.StartDate, request.EndDate, ct);
        await db.RoomNightInventory
            .Where(r => r.BranchId == branchId && r.RoomTypeId == request.RoomTypeId && r.Date >= request.StartDate && r.Date < request.EndDate)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SellLimit, request.SellLimit).SetProperty(r => r.ConcurrencyStamp, Guid.NewGuid()), ct);
    }

    public async Task<InventoryReconciliationDto> ReconcileAsync(Guid branchId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        ValidateWindow(start, end, MaxInventoryWindowDays);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await inventory.ReconcileAsync(scope.Branch, start, end, ct);
        if (result.RowsCorrected > 0)
        {
            db.AuditLogs.Add(new AuditLog
            {
                OccurredAtUtc = scope.UtcNow, CompanyId = scope.CompanyId, BranchId = branchId, UserId = scope.UserId,
                Action = "InventoryReconciled", EntityType = nameof(RoomNightInventory), EntityId = $"{start:yyyy-MM-dd}..{end:yyyy-MM-dd}",
                Details = string.Join("\n", result.Corrections.Take(50)), TraceId = user.TraceId,
            });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return result;
    }

    private static void ValidateWindow(DateOnly start, DateOnly end, int maxDays)
    {
        if (end <= start) throw new ValidationAppException("end", "End date must be after start date.");
        if (end.DayNumber - start.DayNumber > maxDays) throw new ValidationAppException("end", $"The date window can be at most {maxDays} days.");
    }

    private static RoomTypeDto ToDto(RoomType t, int count) =>
        new(t.Id, t.Code, t.Name, t.Description, t.MaxAdults, t.MaxChildren, t.BaseRate, t.IsActive, count, t.ConcurrencyStamp);

    internal static RoomDto ToDto(Room r, bool blockedToday) =>
        new(r.Id, r.RoomNumber, r.RoomTypeId, r.RoomType?.Code ?? string.Empty, r.Floor, r.Status.ToString(), r.IsActive, blockedToday, r.ConcurrencyStamp);

    private static RoomBlockDto ToDto(RoomBlock b) =>
        new(b.Id, b.RoomId, b.Room?.RoomNumber ?? string.Empty, b.StartDate, b.EndDate, b.Reason, b.IsActive);
}
