using HMS.Contracts.Guests;
using HMS.Contracts.Rates;

namespace HMS.Contracts.Reservations;

public sealed record AvailabilityQuery(DateOnly ArrivalDate, DateOnly DepartureDate, int Adults = 1, int Children = 0);

public sealed record RoomTypeAvailabilityDto(Guid RoomTypeId, string Code, string Name, int MaxAdults, int MaxChildren,
    int AvailableRooms, QuoteDto? Quote);

/// <summary>
/// Supply either an existing GuestId or NewGuest. Source: FrontDesk, Phone, WalkIn, Email, Website, Channel.
/// Send an Idempotency-Key header so a retried request never books twice.
/// </summary>
public sealed record CreateReservationRequest(
    Guid? GuestId,
    SaveGuestRequest? NewGuest,
    Guid RoomTypeId,
    Guid? RatePlanId,
    DateOnly ArrivalDate,
    DateOnly DepartureDate,
    int Adults,
    int Children,
    decimal DiscountPercent,
    string Source,
    string? Notes,
    bool AllowOverbooking = false);

public sealed record ModifyReservationRequest(Guid RoomTypeId, DateOnly ArrivalDate, DateOnly DepartureDate, int Adults,
    int Children, decimal DiscountPercent, Guid ConcurrencyStamp, bool AllowOverbooking = false);

public sealed record CancelReservationRequest(string Reason, Guid ConcurrencyStamp);

public sealed record AssignRoomRequest(Guid RoomId, Guid ConcurrencyStamp);

public sealed record ReservationNightDto(DateOnly Date, decimal Rate, decimal Discount);

public sealed record ReservationDto(
    Guid Id,
    string ReservationNumber,
    string Status,
    GuestSummaryDto Guest,
    Guid RoomTypeId,
    string RoomTypeCode,
    Guid RatePlanId,
    string RatePlanName,
    Guid? RoomId,
    string? RoomNumber,
    DateOnly ArrivalDate,
    DateOnly DepartureDate,
    int Nights,
    int Adults,
    int Children,
    string Source,
    bool IsOverbooked,
    string Currency,
    decimal RoomSubtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    decimal DepositRequired,
    bool IsRefundable,
    string CancellationPolicy,
    Guid? FolioId,
    string? Notes,
    IReadOnlyList<ReservationNightDto> NightlyRates,
    Guid ConcurrencyStamp);

public sealed record ReservationListItemDto(Guid Id, string ReservationNumber, string Status, string GuestName,
    string RoomTypeCode, string? RoomNumber, DateOnly ArrivalDate, DateOnly DepartureDate, decimal TotalAmount);

public sealed class ReservationSearchQuery
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public DateOnly? ArrivalFrom { get; set; }
    public DateOnly? ArrivalTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
