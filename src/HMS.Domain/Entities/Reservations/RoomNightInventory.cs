using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>
/// Authoritative sellable count for one room type on one night. Staff and public bookings both
/// allocate through atomic conditional updates on these rows, so the last room cannot be sold twice.
/// </summary>
public class RoomNightInventory : IConcurrencyTracked
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid RoomTypeId { get; set; }
    public DateOnly Date { get; set; }
    /// <summary>Active rooms of this type.</summary>
    public int PhysicalCount { get; set; }
    /// <summary>Rooms of this type under an active maintenance/out-of-service block.</summary>
    public int BlockedCount { get; set; }
    /// <summary>Optional cap below physical (yield control).</summary>
    public int? SellLimit { get; set; }
    public int ConfirmedCount { get; set; }
    public int HeldCount { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public int Sellable => Math.Max(0, Math.Min(PhysicalCount - BlockedCount, SellLimit ?? int.MaxValue));
    public int Available => Sellable - ConfirmedCount - HeldCount;
}
