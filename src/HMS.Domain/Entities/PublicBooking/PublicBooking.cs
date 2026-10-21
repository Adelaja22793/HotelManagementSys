using System.Security.Cryptography;
using System.Text;
using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>
/// A short-lived hold on rooms taken by the public website while the guest enters details and pays.
/// Held rooms are counted in <see cref="RoomNightInventory.HeldCount"/> so staff and web bookings never oversell.
/// The guest proves ownership with a random access token; only its SHA-256 hash is stored.
/// </summary>
public class BookingHold : BranchEntity, IConcurrencyTracked
{
    public const int MaxRooms = 4;

    private BookingHold() { }

    public BookingHold(Guid companyId, Guid branchId, string reference, string accessTokenHash, StayDates dates, int adults, int children,
        string currency, DateTime expiresAtUtc, string? clientFingerprint)
    {
        CompanyId = companyId;
        BranchId = branchId;
        Reference = reference;
        AccessTokenHash = accessTokenHash;
        ArrivalDate = dates.Arrival;
        DepartureDate = dates.Departure;
        Adults = adults;
        Children = children;
        Currency = currency;
        ExpiresAtUtc = expiresAtUtc;
        ClientFingerprint = clientFingerprint;
    }

    /// <summary>Public booking code shown to the guest, e.g. W7K3P9QX2M.</summary>
    public string Reference { get; private set; } = string.Empty;
    public string AccessTokenHash { get; private set; } = string.Empty;
    public DateOnly ArrivalDate { get; private set; }
    public DateOnly DepartureDate { get; private set; }
    public int Adults { get; private set; }
    public int Children { get; private set; }
    public string Currency { get; private set; } = Common.Currency.Default;
    public BookingHoldStatus Status { get; private set; } = BookingHoldStatus.Active;
    public DateTime ExpiresAtUtc { get; private set; }
    public bool ExpiryExtended { get; private set; }
    /// <summary>Hash of the caller's IP address, used to limit how many holds one client keeps open.</summary>
    public string? ClientFingerprint { get; private set; }

    public string? GuestFirstName { get; private set; }
    public string? GuestLastName { get; private set; }
    public string? GuestEmail { get; private set; }
    public string? GuestPhone { get; private set; }
    public string? GuestCountry { get; private set; }
    public string? SpecialRequests { get; private set; }
    public DateTime? TermsAcceptedAtUtc { get; private set; }

    public DateTime? ConvertedAtUtc { get; private set; }
    public DateTime? CancellationRequestedAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? ReviewReason { get; private set; }

    public List<BookingHoldRoom> Rooms { get; private set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public decimal TotalAmount => Rooms.Sum(r => r.TotalAmount * r.Quantity);
    public decimal DepositAmount => Rooms.Sum(r => r.DepositRequired * r.Quantity);
    public int RoomCount => Rooms.Sum(r => r.Quantity);
    public bool HasGuestDetails => GuestLastName is not null && (GuestEmail is not null || GuestPhone is not null) && TermsAcceptedAtUtc is not null;

    public bool IsLive(DateTime utcNow) => Status == BookingHoldStatus.Active && ExpiresAtUtc > utcNow;

    public BookingHoldRoom AddRoom(Guid roomTypeId, Guid ratePlanId, int quantity, decimal roomSubtotal, decimal taxAmount, decimal totalAmount,
        decimal depositRequired, string quoteJson)
    {
        if (quantity < 1) throw new DomainException("QUANTITY_INVALID", "Choose at least one room.");
        if (RoomCount + quantity > MaxRooms) throw new DomainException("TOO_MANY_ROOMS", $"Online bookings are limited to {MaxRooms} rooms. Please contact the hotel for groups.");
        var room = new BookingHoldRoom(Id, roomTypeId, ratePlanId, quantity, roomSubtotal, taxAmount, totalAmount, depositRequired, quoteJson);
        Rooms.Add(room);
        return room;
    }

    public void SetGuest(string firstName, string lastName, string? email, string? phone, string? country, string? specialRequests, DateTime utcNow)
    {
        EnsureLive(utcNow);
        GuestFirstName = Guard.Required(firstName, "FIRST_NAME_REQUIRED", "Enter your first name", 100);
        GuestLastName = Guard.Required(lastName, "LAST_NAME_REQUIRED", "Enter your last name", 100);
        GuestEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        GuestPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (GuestEmail is null) throw new DomainException("EMAIL_REQUIRED", "Enter an email address for your confirmation and receipt.");
        GuestCountry = string.IsNullOrWhiteSpace(country) ? null : country.Trim();
        SpecialRequests = string.IsNullOrWhiteSpace(specialRequests) ? null : specialRequests.Trim();
        if (SpecialRequests is { Length: > 500 }) throw new DomainException("REQUESTS_TOO_LONG", "Special requests are limited to 500 characters.");
        TermsAcceptedAtUtc = utcNow;
    }

    /// <summary>Gives the guest one more hold period when they go to pay, so a slow payment page does not lose the rooms.</summary>
    public void ExtendForPayment(DateTime utcNow, TimeSpan holdLength)
    {
        EnsureLive(utcNow);
        if (ExpiryExtended) return;
        ExpiresAtUtc = utcNow + holdLength;
        ExpiryExtended = true;
    }

    public void Expire()
    {
        if (Status == BookingHoldStatus.Active) Status = BookingHoldStatus.Expired;
    }

    public void Release()
    {
        if (Status != BookingHoldStatus.Active) throw new DomainConflictException("HOLD_NOT_ACTIVE", "This booking is no longer on hold.");
        Status = BookingHoldStatus.Released;
    }

    public void MarkConverted(DateTime utcNow)
    {
        if (Status is not (BookingHoldStatus.Active or BookingHoldStatus.Expired or BookingHoldStatus.NeedsReview))
            throw new DomainConflictException("HOLD_NOT_ACTIVE", "This booking can no longer be completed.");
        Status = BookingHoldStatus.Converted;
        ConvertedAtUtc = utcNow;
    }

    public void MarkNeedsReview(string reason)
    {
        Status = BookingHoldStatus.NeedsReview;
        ReviewReason = reason.Length > 300 ? reason[..300] : reason;
    }

    public void RequestCancellation(string? reason, DateTime utcNow)
    {
        if (Status != BookingHoldStatus.Converted) throw new DomainConflictException("BOOKING_NOT_CONFIRMED", "Only a confirmed booking can be cancelled.");
        if (CancellationRequestedAtUtc is not null) return;
        CancellationRequestedAtUtc = utcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
    }

    public void EnsureLive(DateTime utcNow)
    {
        if (Status != BookingHoldStatus.Active || ExpiresAtUtc <= utcNow)
            throw new DomainConflictException("HOLD_EXPIRED", "Your hold has expired. Please search again.");
    }

    public bool TokenMatches(string? token) =>
        !string.IsNullOrEmpty(token)
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashToken(token)), Encoding.ASCII.GetBytes(AccessTokenHash));

    public static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>One room type in a hold with the accepted price snapshot (per room).</summary>
public class BookingHoldRoom : Entity
{
    private BookingHoldRoom() { }

    internal BookingHoldRoom(Guid holdId, Guid roomTypeId, Guid ratePlanId, int quantity, decimal roomSubtotal, decimal taxAmount, decimal totalAmount,
        decimal depositRequired, string quoteJson)
    {
        HoldId = holdId;
        RoomTypeId = roomTypeId;
        RatePlanId = ratePlanId;
        Quantity = quantity;
        RoomSubtotal = roomSubtotal;
        TaxAmount = taxAmount;
        TotalAmount = totalAmount;
        DepositRequired = depositRequired;
        QuoteJson = quoteJson;
    }

    public Guid HoldId { get; private set; }
    public Guid RoomTypeId { get; private set; }
    public RoomType? RoomType { get; private set; }
    public Guid RatePlanId { get; private set; }
    public int Quantity { get; private set; }
    public decimal RoomSubtotal { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal DepositRequired { get; private set; }
    /// <summary>The full nightly quote accepted by the guest; reservations are created from it, not re-priced.</summary>
    public string QuoteJson { get; private set; } = string.Empty;
}

/// <summary>An attempt to collect payment through an online provider. Card details never reach this system.</summary>
public class BookingPaymentAttempt : BranchEntity, IConcurrencyTracked
{
    private BookingPaymentAttempt() { }

    public BookingPaymentAttempt(Guid companyId, Guid branchId, Guid holdId, string provider, string merchantReference, decimal amount, string currency)
    {
        CompanyId = companyId;
        BranchId = branchId;
        HoldId = holdId;
        Provider = provider;
        MerchantReference = merchantReference;
        Amount = amount;
        Currency = currency;
    }

    public Guid HoldId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    /// <summary>Our reference sent to the provider; unique, so a payment can only ever confirm one booking.</summary>
    public string MerchantReference { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = Common.Currency.Default;
    public OnlinePaymentStatus Status { get; private set; } = OnlinePaymentStatus.Initialized;
    public string? AuthorizationUrl { get; private set; }
    public string? ProviderTransactionId { get; private set; }
    public decimal? ReportedAmount { get; private set; }
    public string? ReportedCurrency { get; private set; }
    public string? ProviderStatus { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }
    public string? ResolvedByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsFinal => Status is OnlinePaymentStatus.Succeeded or OnlinePaymentStatus.Failed or OnlinePaymentStatus.NeedsReview or OnlinePaymentStatus.Resolved;

    public void SetAuthorizationUrl(string url) => AuthorizationUrl = url;

    public void RecordProviderResult(string providerStatus, string? transactionId, decimal? amount, string? currency, DateTime utcNow)
    {
        ProviderStatus = providerStatus.Length > 50 ? providerStatus[..50] : providerStatus;
        ProviderTransactionId = transactionId;
        ReportedAmount = amount;
        ReportedCurrency = currency;
        VerifiedAtUtc = utcNow;
    }

    /// <summary>True when the provider's figures match what we asked for, to the kobo.</summary>
    public bool Matches(decimal? amount, string? currency) =>
        amount == Amount && string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase);

    public void MarkSucceeded() => Status = OnlinePaymentStatus.Succeeded;

    public void MarkFailed(string reason)
    {
        if (Status != OnlinePaymentStatus.Initialized) return;
        Status = OnlinePaymentStatus.Failed;
        FailureReason = reason.Length > 300 ? reason[..300] : reason;
    }

    public void MarkNeedsReview(string reason)
    {
        Status = OnlinePaymentStatus.NeedsReview;
        FailureReason = reason.Length > 300 ? reason[..300] : reason;
    }

    public void Resolve(string userId, string note)
    {
        if (Status != OnlinePaymentStatus.NeedsReview) throw new DomainConflictException("PAYMENT_STATE", "Only payments waiting for review can be resolved.");
        ResolutionNote = Guard.Required(note, "NOTE_REQUIRED", "Explain how the payment was dealt with (refund reference or new booking)", 500);
        ResolvedByUserId = userId;
        Status = OnlinePaymentStatus.Resolved;
    }
}

/// <summary>A sales channel (the hotel website, or an online travel agent once a partner agreement exists).</summary>
public class BookingChannel : AuditableEntity
{
    public const string Website = "WEB";

    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>Links a reservation to the channel's own reference so a channel message is applied once and never loops back.</summary>
public class ChannelReservationMapping : BranchEntity
{
    public Guid ChannelId { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public Guid ReservationId { get; set; }
}
