namespace HMS.Contracts.Rates;

public sealed record RatePlanDto(Guid Id, string Code, string Name, bool IsRefundable, decimal DepositPercent,
    string CancellationPolicy, bool IsActive, Guid ConcurrencyStamp);

public sealed record CreateRatePlanRequest(string Code, string Name, bool IsRefundable, decimal DepositPercent, string CancellationPolicy);

public sealed record UpdateRatePlanRequest(string Name, bool IsRefundable, decimal DepositPercent, string CancellationPolicy,
    bool IsActive, Guid ConcurrencyStamp);

/// <summary>EndDate is the inclusive last night.</summary>
public sealed record RoomRateDto(Guid Id, Guid RatePlanId, Guid RoomTypeId, DateOnly StartDate, DateOnly EndDate, decimal NightlyRate);

public sealed record CreateRoomRateRequest(Guid RatePlanId, Guid RoomTypeId, DateOnly StartDate, DateOnly EndDate, decimal NightlyRate);

/// <summary>Scope: Room, Service or All.</summary>
public sealed record TaxRuleDto(Guid Id, string Code, string Name, decimal RatePercent, string Scope, bool IsInclusive,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, Guid ConcurrencyStamp);

public sealed record CreateTaxRuleRequest(string Code, string Name, decimal RatePercent, string Scope, bool IsInclusive,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record UpdateTaxRuleRequest(string Name, decimal RatePercent, string Scope, bool IsInclusive,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, Guid ConcurrencyStamp);

public sealed record ServiceItemDto(Guid Id, string Code, string Name, decimal UnitPrice, bool IsTaxable, bool IsActive, Guid ConcurrencyStamp);

public sealed record CreateServiceItemRequest(string Code, string Name, decimal UnitPrice, bool IsTaxable);

public sealed record UpdateServiceItemRequest(string Name, decimal UnitPrice, bool IsTaxable, bool IsActive, Guid ConcurrencyStamp);

public sealed record QuoteRequest(Guid RoomTypeId, Guid? RatePlanId, DateOnly ArrivalDate, DateOnly DepartureDate,
    int Adults, int Children, decimal DiscountPercent = 0);

public sealed record QuoteNightDto(DateOnly Date, decimal Rate, decimal Discount, decimal Net);

public sealed record QuoteTaxDto(string Code, string Name, decimal RatePercent, bool IsInclusive, decimal Amount);

public sealed record QuoteDto(
    Guid RoomTypeId,
    Guid RatePlanId,
    string RatePlanName,
    string Currency,
    int Nights,
    IReadOnlyList<QuoteNightDto> NightlyRates,
    decimal RoomSubtotal,
    decimal DiscountAmount,
    IReadOnlyList<QuoteTaxDto> Taxes,
    decimal TaxAmount,
    decimal Total,
    decimal DepositRequired,
    bool IsRefundable,
    string CancellationPolicy);
