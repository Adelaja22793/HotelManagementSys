namespace HMS.Contracts.PublicBooking;

// ----- what the public website sees (no internal ids beyond room types, no staff data) -----

public sealed record PublicBranchDto(string Code, string Name, string? City, string? AddressLine, string? Phone, string Currency, string TimeZoneId);

public sealed record PublicNightDto(DateOnly Date, decimal Rate);

/// <summary>Available is capped at 5 so competitors cannot read exact inventory.</summary>
public sealed record PublicRoomOfferDto(Guid RoomTypeId, string Code, string Name, string? Description, int MaxAdults, int MaxChildren,
    int Available, string RatePlanName, bool IsRefundable, string CancellationPolicy, string Currency, IReadOnlyList<PublicNightDto> Nights,
    decimal RoomSubtotal, decimal TaxAmount, decimal TotalAmount, decimal AmountDueNow);

public sealed record PublicAvailabilityDto(string BranchCode, DateOnly ArrivalDate, DateOnly DepartureDate, int Nights, int Adults, int Children,
    IReadOnlyList<PublicRoomOfferDto> Offers);

public sealed record HoldRoomRequest(Guid RoomTypeId, int Quantity);

public sealed record CreateHoldRequest(DateOnly ArrivalDate, DateOnly DepartureDate, int Adults, int Children, IReadOnlyList<HoldRoomRequest> Rooms);

public sealed record HoldRoomDto(Guid RoomTypeId, string RoomTypeName, int Quantity, string RatePlanName, bool IsRefundable, string CancellationPolicy,
    decimal TotalPerRoom, decimal DepositPerRoom);

/// <summary>Status: Active, Converted, Expired, Released, NeedsReview. AccessToken is only returned when the hold is created.</summary>
public sealed record HoldDto(string Reference, string? AccessToken, string BranchCode, string BranchName, DateOnly ArrivalDate, DateOnly DepartureDate,
    int Adults, int Children, string Status, DateTime ExpiresAtUtc, string Currency, decimal TotalAmount, decimal AmountDueNow,
    IReadOnlyList<HoldRoomDto> Rooms, bool HasGuestDetails, string? GuestName, string? GuestEmail);

/// <summary>Website must be empty: it is a hidden field that only bots fill in.</summary>
public sealed record GuestDetailsRequest(string FirstName, string LastName, string Email, string? Phone, string? Country, string? SpecialRequests,
    bool AcceptTerms, string? Website = null);

public sealed record PaymentInitDto(string MerchantReference, string Provider, string AuthorizationUrl, decimal Amount, string Currency, DateTime HoldExpiresAtUtc);

/// <summary>PaymentStatus: Initialized (still waiting), Succeeded, Failed, NeedsReview (the hotel will contact you).</summary>
public sealed record PublicBookingDto(string Reference, string Status, string? PaymentStatus, string BranchName, string? BranchPhone,
    DateOnly ArrivalDate, DateOnly DepartureDate, string? GuestName, string Currency, decimal TotalAmount, decimal AmountPaid,
    IReadOnlyList<PublicReservationDto> Reservations, bool CancellationRequested, string? Message);

public sealed record PublicReservationDto(string ReservationNumber, string RoomTypeName, string Status, decimal TotalAmount, string CancellationPolicy);

public sealed record CancellationRequest(string? Reason);

// ----- staff side -----

public sealed record PublicBookingSettingsRequest(bool Enabled);

/// <summary>Status: Initialized, Succeeded, Failed, NeedsReview, Resolved.</summary>
public sealed record OnlinePaymentDto(Guid Id, string HoldReference, string Provider, string MerchantReference, decimal Amount, string Currency,
    string Status, string? ProviderStatus, string? ProviderTransactionId, decimal? ReportedAmount, string? ReportedCurrency, string? FailureReason,
    DateTime CreatedAtUtc, DateTime? VerifiedAtUtc, string? GuestName, string? GuestEmail, string? ResolutionNote, Guid ConcurrencyStamp);

public sealed record ResolvePaymentRequest(string Note, Guid ConcurrencyStamp);

public sealed record HoldExpiryResultDto(int Expired);
