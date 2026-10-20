namespace HMS.Contracts.FrontDesk;

/// <summary>RoomId is optional when a room is already assigned.</summary>
public sealed record CheckInRequest(Guid ReservationId, Guid? RoomId, Guid ConcurrencyStamp);

/// <summary>Set AllowBalance (requires permission) with a reason to check out with an unpaid balance.</summary>
public sealed record CheckOutRequest(Guid StayId, bool AllowBalance = false, string? BalanceOverrideReason = null);

public sealed record StayDto(Guid Id, Guid ReservationId, string ReservationNumber, Guid GuestId, string GuestName,
    Guid RoomId, string RoomNumber, string Status, DateOnly ArrivalDate, DateOnly DepartureDate,
    DateTime CheckedInAtUtc, DateTime? CheckedOutAtUtc, Guid? FolioId, decimal Balance);

public sealed record CheckOutResultDto(StayDto Stay, Guid InvoiceId, string InvoiceNumber, Guid HousekeepingTaskId);

public sealed record FrontDeskSummaryDto(DateOnly BusinessDate, int ArrivalsExpected, int ArrivalsCheckedIn,
    int DeparturesExpected, int DeparturesCheckedOut, int InHouse, int VacantClean, int VacantDirty, int Blocked);
