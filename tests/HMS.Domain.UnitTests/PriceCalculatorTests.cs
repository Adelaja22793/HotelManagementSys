using HMS.Domain.Common;
using HMS.Domain.Pricing;

namespace HMS.Domain.UnitTests;

public class PriceCalculatorTests
{
    private static readonly DateOnly Nov1 = new(2026, 11, 1);
    private static readonly TaxInput Vat5 = new("VAT", "VAT", 5m, false);

    private static List<NightRate> Nights(params decimal[] rates) =>
        rates.Select((r, i) => new NightRate(Nov1.AddDays(i), r)).ToList();

    [Fact]
    public void Blueprint_example_three_nights_with_five_percent_tax()
    {
        var quote = PriceCalculator.Calculate("NGN", Nights(50_000, 50_000, 50_000), 0, [Vat5], 0);

        Assert.Equal(150_000m, quote.RoomSubtotal);
        Assert.Equal(7_500m, quote.ExclusiveTaxAmount);
        Assert.Equal(157_500m, quote.Total);
    }

    [Fact]
    public void Stay_across_seasons_is_priced_per_night()
    {
        var quote = PriceCalculator.Calculate("NGN", Nights(40_000, 60_000), 0, [], 0);
        Assert.Equal(100_000m, quote.Total);
        Assert.Equal([40_000m, 60_000m], quote.Nights.Select(n => n.Rate));
    }

    [Fact]
    public void Discount_applies_before_tax()
    {
        var quote = PriceCalculator.Calculate("NGN", Nights(50_000, 50_000), 10, [Vat5], 0);

        Assert.Equal(10_000m, quote.DiscountAmount);
        Assert.Equal(90_000m, quote.NetRoomAmount);
        Assert.Equal(4_500m, quote.ExclusiveTaxAmount);
        Assert.Equal(94_500m, quote.Total);
    }

    [Fact]
    public void Inclusive_tax_is_extracted_not_added()
    {
        var quote = PriceCalculator.Calculate("NGN", Nights(10_500), 0, [new TaxInput("VAT", "VAT", 5, true)], 0);

        Assert.Equal(10_500m, quote.Total);
        Assert.Equal(500m, quote.Taxes.Single().Amount);
        Assert.Equal(0m, quote.ExclusiveTaxAmount);
    }

    [Fact]
    public void Deposit_is_a_share_of_the_total()
    {
        var quote = PriceCalculator.Calculate("NGN", Nights(50_000, 50_000, 50_000), 0, [Vat5], 50);
        Assert.Equal(78_750m, quote.DepositRequired);
    }

    [Fact]
    public void Nightly_tax_rounding_matches_total()
    {
        var quote = PriceCalculator.Calculate("NGN", Nights(333.33m, 333.33m, 333.33m), 0, [new TaxInput("T", "T", 7.5m, false)], 0);
        Assert.Equal(quote.Nights.Sum(n => n.ExclusiveTax), quote.ExclusiveTaxAmount);
        Assert.Equal(quote.NetRoomAmount + quote.ExclusiveTaxAmount, quote.Total);
        Assert.Equal(75m, quote.ExclusiveTaxAmount); // 25.00 per night
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Invalid_discount_is_rejected(decimal discount) =>
        Assert.Throws<DomainException>(() => PriceCalculator.Calculate("NGN", Nights(100), discount, [], 0));

    [Fact]
    public void Empty_stay_is_rejected() =>
        Assert.Throws<DomainException>(() => PriceCalculator.Calculate("NGN", [], 0, [], 0));
}
