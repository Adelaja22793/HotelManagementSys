namespace HMS.Contracts.Rooms;

public sealed record RoomTypeDto(Guid Id, string Code, string Name, string? Description, int MaxAdults, int MaxChildren,
    decimal BaseRate, bool IsActive, int RoomCount, Guid ConcurrencyStamp);

public sealed record CreateRoomTypeRequest(string Code, string Name, string? Description, int MaxAdults, int MaxChildren, decimal BaseRate);

public sealed record UpdateRoomTypeRequest(string Name, string? Description, int MaxAdults, int MaxChildren, decimal BaseRate,
    bool IsActive, Guid ConcurrencyStamp);

/// <summary>Status values: VacantClean, VacantDirty, OccupiedClean, OccupiedDirty.</summary>
public sealed record RoomDto(Guid Id, string RoomNumber, Guid RoomTypeId, string RoomTypeCode, string? Floor, string Status,
    bool IsActive, bool IsBlockedToday, Guid ConcurrencyStamp);

public sealed record CreateRoomRequest(string RoomNumber, Guid RoomTypeId, string? Floor);

public sealed record UpdateRoomRequest(Guid RoomTypeId, string? Floor, bool IsActive, Guid ConcurrencyStamp);

public sealed record RoomBlockDto(Guid Id, Guid RoomId, string RoomNumber, DateOnly StartDate, DateOnly EndDate, string Reason, bool IsActive);

/// <summary>End date is exclusive: the room is sellable again on that date.</summary>
public sealed record CreateRoomBlockRequest(Guid RoomId, DateOnly StartDate, DateOnly EndDate, string Reason);

public sealed record InventoryDayDto(Guid RoomTypeId, string RoomTypeCode, DateOnly Date, int Physical, int Blocked,
    int? SellLimit, int Confirmed, int Held, int Available);

public sealed record SetSellLimitRequest(Guid RoomTypeId, DateOnly StartDate, DateOnly EndDate, int? SellLimit);

public sealed record InventoryReconciliationDto(int RowsChecked, int RowsCorrected, IReadOnlyList<string> Corrections);
