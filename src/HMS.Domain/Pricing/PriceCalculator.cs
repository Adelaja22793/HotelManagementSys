using HMS.Domain.Common;

namespace HMS.Domain.Pricing;

public sealed record NightRate(DateOnly Date, decimal Rate);

public sealed record TaxInput(string Code, string Name, decimal RatePercent, bool IsInclusive);

public sealed record QuotedNight(DateOnly Date, decimal Rate, decimal Discount, decimal Net, decimal ExclusiveTax);

public sealed record TaxLine(string Code, string Name, decimal RatePercent, bool IsInclusive, decimal Amount);

public sealed record PriceQuote(
    string Currency,
    IReadOnlyList<QuotedNight> Nights,
    decimal RoomSubtotal,
    decimal DiscountAmount,
    decimal NetRoomAmount,
    IReadOnlyList<TaxLine> Taxes,
    decimal ExclusiveTaxAmount,
    decimal Total,
    decimal DepositRequired);

/// <summary>
/// The single pricing implementation (no UI duplicates it). Policy, documented for finance sign-off:
/// <list type="bullet">
/// <item>Rates are priced per night, so a stay can span seasons.</item>
/// <item>Discount is applied per night before tax.</item>
/// <item>Exclusive taxes are added on the discounted nightly amount, rounded per night.</item>
/// <item>Inclusive taxes are already inside the price; they are extracted for display only.</item>
/// <item>All rounding is 2 decimals, half away from zero.</item>
/// </list>
/// </summary>
public static class PriceCalculator
{
    public const decimal MaxDiscountPercent = 100m;

    public static PriceQuote Calculate(string currency, IReadOnlyList<NightRate> nights, decimal discountPercent,
        IReadOnlyList<TaxInput> taxes, decimal depositPercent)
    {
        if (nights.Count == 0) throw new DomainException("NIGHTS_REQUIRED", "At least one night is required.");
        if (discountPercent is < 0 or > MaxDiscountPercent) throw new DomainException("DISCOUNT_INVALID", "Discount must be between 0 and 100 percent.");
        if (depositPercent is < 0 or > 100) throw new DomainException("DEPOSIT_INVALID", "Deposit must be between 0 and 100 percent.");
        if (nights.Any(n => n.Rate <= 0)) throw new DomainException("RATE_INVALID", "Every night needs a positive rate.");

        var inclusiveRateSum = taxes.Where(t => t.IsInclusive).Sum(t => t.RatePercent);
        decimal TaxFor(TaxInput t, decimal net) => t.IsInclusive
            ? Money.Round(net * t.RatePercent / (100m + inclusiveRateSum))
            : Money.Round(net * t.RatePercent / 100m);

        var quoted = nights
            .OrderBy(n => n.Date)
            .Select(n =>
            {
                var discount = Money.Round(n.Rate * discountPercent / 100m);
                var net = n.Rate - discount;
                var exclusive = taxes.Where(t => !t.IsInclusive).Sum(t => TaxFor(t, net));
                return new QuotedNight(n.Date, n.Rate, discount, net, exclusive);
            })
            .ToList();

        var subtotal = quoted.Sum(n => n.Rate);
        var discountTotal = quoted.Sum(n => n.Discount);
        var netTotal = subtotal - discountTotal;

        // Tax is rounded per night so the quote matches the nightly charges posted to the folio exactly.
        var lines = taxes
            .Select(t => new TaxLine(t.Code, t.Name, t.RatePercent, t.IsInclusive, quoted.Sum(n => TaxFor(t, n.Net))))
            .ToList();

        var exclusiveTax = quoted.Sum(n => n.ExclusiveTax);
        var total = netTotal + exclusiveTax;
        var deposit = Money.Round(total * depositPercent / 100m);

        return new PriceQuote(Currency.Normalize(currency), quoted, subtotal, discountTotal, netTotal, lines, exclusiveTax, total, deposit);
    }
}
