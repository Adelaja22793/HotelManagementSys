namespace HMS.Domain.Entities;

/// <summary>
/// One row per physical room per night it is assigned. The unique (RoomId, Date) index makes it
/// impossible for two reservations to hold the same room on the same night.
/// </summary>
public class AssignedRoomNight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public Guid RoomId { get; set; }
    public DateOnly Date { get; set; }
    public Guid ReservationId { get; set; }
}
