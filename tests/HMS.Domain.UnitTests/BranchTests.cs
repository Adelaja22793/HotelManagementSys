using HMS.Domain.Common;
using HMS.Domain.Entities;

namespace HMS.Domain.UnitTests;

public class BranchTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public void Business_date_uses_branch_time_zone()
    {
        var branch = new Branch(CompanyId, "Lagos", "los", "Africa/Lagos");

        // 23:30 UTC is 00:30 next day in Lagos (UTC+1).
        var date = branch.GetBusinessDate(new DateTime(2026, 11, 1, 23, 30, 0, DateTimeKind.Utc));

        Assert.Equal(new DateOnly(2026, 11, 2), date);
    }

    [Fact]
    public void Business_date_honours_cutoff_for_night_audit()
    {
        var branch = new Branch(CompanyId, "Lagos", "LOS", "Africa/Lagos");
        branch.SetBusinessDayCutoff(new TimeOnly(4, 0));

        // 02:00 local on 2 Nov is still business date 1 Nov.
        var date = branch.GetBusinessDate(new DateTime(2026, 11, 2, 1, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateOnly(2026, 11, 1), date);
    }

    [Fact]
    public void Code_is_normalised_to_upper_case()
    {
        var branch = new Branch(CompanyId, "Abuja", " abj-1 ");
        Assert.Equal("ABJ-1", branch.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad code")]
    [InlineData("x$")]
    public void Invalid_codes_are_rejected(string code)
    {
        Assert.Throws<DomainException>(() => new Branch(CompanyId, "Name", code));
    }

    [Fact]
    public void Unknown_time_zone_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() => new Branch(CompanyId, "Name", "X", "Mars/Olympus"));
        Assert.Equal("TIMEZONE_INVALID", ex.Code);
    }

    [Fact]
    public void Branch_requires_company()
    {
        Assert.Throws<DomainException>(() => new Branch(Guid.Empty, "Name", "X"));
    }

    [Theory]
    [InlineData("ngn", "NGN")]
    [InlineData(" usd ", "USD")]
    public void Currency_is_normalised(string input, string expected) =>
        Assert.Equal(expected, Currency.Normalize(input));

    [Theory]
    [InlineData("NG")]
    [InlineData("N1N")]
    public void Invalid_currency_is_rejected(string input) =>
        Assert.Throws<DomainException>(() => Currency.Normalize(input));
}
