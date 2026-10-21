using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>A physical room. Maintenance blocks are modelled separately in <see cref="RoomBlock"/>.</summary>
public class Room : BranchEntity, IConcurrencyTracked
{
    private Room() { }

    public Room(Guid companyId, Guid branchId, Guid roomTypeId, string roomNumber, string? floor = null)
    {
        CompanyId = companyId;
        BranchId = branchId;
        RoomTypeId = roomTypeId;
        RoomNumber = Guard.Required(roomNumber, "ROOM_NUMBER_REQUIRED", "Room number is required", 20).ToUpperInvariant();
        Floor = string.IsNullOrWhiteSpace(floor) ? null : floor.Trim();
    }

    public string RoomNumber { get; private set; } = string.Empty;
    public Guid RoomTypeId { get; private set; }
    public RoomType? RoomType { get; private set; }
    public string? Floor { get; private set; }
    public RoomStatus Status { get; private set; } = RoomStatus.VacantClean;
    public bool IsActive { get; private set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsOccupied => Status is RoomStatus.OccupiedClean or RoomStatus.OccupiedDirty;

    public void Update(Guid roomTypeId, string? floor)
    {
        RoomTypeId = roomTypeId;
        Floor = string.IsNullOrWhiteSpace(floor) ? null : floor.Trim();
    }

    public void MarkOccupied()
    {
        if (IsOccupied) throw new DomainConflictException("ROOM_OCCUPIED", $"Room {RoomNumber} is already occupied.");
        if (Status != RoomStatus.VacantClean)
            throw new DomainConflictException("ROOM_NOT_READY", $"Room {RoomNumber} is not clean and inspected.");
        Status = RoomStatus.OccupiedClean;
    }

    public void MarkVacantDirty() => Status = RoomStatus.VacantDirty;

    public void MarkDirty() => Status = IsOccupied ? RoomStatus.OccupiedDirty : RoomStatus.VacantDirty;

    public void MarkClean() => Status = IsOccupied ? RoomStatus.OccupiedClean : RoomStatus.VacantClean;

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        if (IsOccupied) throw new DomainConflictException("ROOM_OCCUPIED", "An occupied room cannot be deactivated.");
        IsActive = false;
    }
}
