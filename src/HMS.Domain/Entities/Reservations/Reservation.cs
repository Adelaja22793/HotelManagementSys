using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>A booking for one room of a room type. The accepted price and policy are snapshotted.</summary>
public class Reservation : BranchEntity, IConcurrencyTracked
{
    private readonly List<ReservationNight> _nights = [];

    private Reservation() { }

    public Reservation(Guid companyId, Guid branchId, string number, Guid guestId, Guid roomTypeId, Guid ratePlanId,
        StayDates dates, int adults, int children, ReservationSource source, string? idempotencyKey)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ReservationNumber = number;
        GuestId = guestId;
        RoomTypeId = roomTypeId;
        RatePlanId = ratePlanId;
        ArrivalDate = dates.Arrival;
        DepartureDate = dates.Departure;
        Adults = adults;
        Children = children;
        Source = source;
        IdempotencyKey = idempotencyKey;
    }

    public string ReservationNumber { get; private set; } = string.Empty;
    public Guid GuestId { get; private set; }
    public Guest? Guest { get; private set; }
    public Guid RoomTypeId { get; private set; }
    public RoomType? RoomType { get; private set; }
    public Guid RatePlanId { get; private set; }
    public Guid? RoomId { get; private set; }
    public Room? Room { get; private set; }
    public DateOnly ArrivalDate { get; private set; }
    public DateOnly DepartureDate { get; private set; }
    public int Adults { get; private set; }
    public int Children { get; private set; }
    public ReservationStatus Status { get; private set; } = ReservationStatus.Confirmed;
    public ReservationSource Source { get; private set; }
    public bool IsOverbooked { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? Notes { get; set; }

    // Accepted price snapshot
    public string Currency { get; private set; } = Common.Currency.Default;
    public decimal RoomSubtotal { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal DepositRequired { get; private set; }
    public string RatePlanName { get; private set; } = string.Empty;
    public string CancellationPolicy { get; private set; } = string.Empty;
    public bool IsRefundable { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public IReadOnlyList<ReservationNight> Nights => _nights;
    public StayDates Dates => new(ArrivalDate, DepartureDate);
    public int NightCount => DepartureDate.DayNumber - ArrivalDate.DayNumber;

    public void ApplyPrice(Pricing.PriceQuote quote, RatePlan plan)
    {
        Currency = quote.Currency;
        RoomSubtotal = quote.RoomSubtotal;
        DiscountAmount = quote.DiscountAmount;
        TaxAmount = quote.ExclusiveTaxAmount;
        TotalAmount = quote.Total;
        DepositRequired = quote.DepositRequired;
        RatePlanName = plan.Name;
        CancellationPolicy = plan.CancellationPolicy;
        IsRefundable = plan.IsRefundable;
        _nights.Clear();
        _nights.AddRange(quote.Nights.Select(n => new ReservationNight(Id, n.Date, n.Rate, n.Discount, n.ExclusiveTax)));
    }

    public void MarkOverbooked() => IsOverbooked = true;

    public void ChangeStay(Guid roomTypeId, StayDates dates, int adults, int children)
    {
        EnsureStatus(ReservationStatus.Confirmed, "Only confirmed reservations can be changed.");
        RoomTypeId = roomTypeId;
        ArrivalDate = dates.Arrival;
        DepartureDate = dates.Departure;
        Adults = adults;
        Children = children;
        RoomId = null;
        IsOverbooked = false;
    }

    public void AssignRoom(Guid roomId)
    {
        if (Status is not (ReservationStatus.Confirmed or ReservationStatus.CheckedIn))
            throw new DomainConflictException("RESERVATION_STATE", "Rooms can be assigned only to confirmed or in-house reservations.");
        RoomId = roomId;
    }

    public void UnassignRoom()
    {
        EnsureStatus(ReservationStatus.Confirmed, "Rooms can be unassigned only before check-in.");
        RoomId = null;
    }

    public void MarkCheckedIn()
    {
        EnsureStatus(ReservationStatus.Confirmed, "Only confirmed reservations can be checked in.");
        if (RoomId is null) throw new DomainConflictException("ROOM_NOT_ASSIGNED", "Assign a room before check-in.");
        Status = ReservationStatus.CheckedIn;
    }

    public void MarkCheckedOut(DateOnly actualDeparture)
    {
        EnsureStatus(ReservationStatus.CheckedIn, "Only in-house reservations can be checked out.");
        if (actualDeparture < DepartureDate && actualDeparture > ArrivalDate) DepartureDate = actualDeparture;
        Status = ReservationStatus.CheckedOut;
    }

    public void Cancel(string reason, DateTime utcNow)
    {
        EnsureStatus(ReservationStatus.Confirmed, "Only confirmed reservations can be cancelled.");
        CancellationReason = Guard.Required(reason, "REASON_REQUIRED", "A cancellation reason is required", 500);
        CancelledAtUtc = utcNow;
        Status = ReservationStatus.Cancelled;
    }

    public void MarkNoShow(DateOnly businessDate)
    {
        EnsureStatus(ReservationStatus.Confirmed, "Only confirmed reservations can be marked no-show.");
        if (businessDate < ArrivalDate) throw new DomainConflictException("NO_SHOW_TOO_EARLY", "A reservation can be marked no-show only on or after its arrival date.");
        Status = ReservationStatus.NoShow;
    }

    private void EnsureStatus(ReservationStatus expected, string message)
    {
        if (Status != expected) throw new DomainConflictException("RESERVATION_STATE", message);
    }
}

/// <summary>Nightly price snapshot accepted at booking time.</summary>
public class ReservationNight
{
    private ReservationNight() { }

    public ReservationNight(Guid reservationId, DateOnly date, decimal rate, decimal discount, decimal taxAmount)
    {
        ReservationId = reservationId;
        Date = date;
        Rate = rate;
        Discount = discount;
        TaxAmount = taxAmount;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ReservationId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Rate { get; private set; }
    public decimal Discount { get; private set; }
    /// <summary>Exclusive tax accepted for this night.</summary>
    public decimal TaxAmount { get; private set; }
    public decimal Net => Rate - Discount;
}
