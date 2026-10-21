using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Application.Security;
using HMS.Contracts.Rates;
using HMS.Domain.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using HMS.Domain.Pricing;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Rates;

public sealed record PricedStay(RoomType RoomType, RatePlan RatePlan, PriceQuote Quote);

public interface IPricingService
{
    Task<PricedStay> PriceAsync(Branch branch, Guid roomTypeId, Guid? ratePlanId, StayDates dates, int adults, int children,
        decimal discountPercent, CancellationToken ct);

    Task<RatePlan> ResolveRatePlanAsync(Guid branchId, Guid? ratePlanId, CancellationToken ct);

    Task<IReadOnlyList<TaxInput>> TaxesForAsync(Guid branchId, TaxScope scope, DateOnly date, CancellationToken ct);

    QuoteDto ToDto(PricedStay priced);
}

internal sealed class PricingService(IApplicationDbContext db, ICurrentUser user) : IPricingService
{
    public const string DefaultRatePlanCode = "BAR";

    public async Task<PricedStay> PriceAsync(Branch branch, Guid roomTypeId, Guid? ratePlanId, StayDates dates, int adults, int children,
        decimal discountPercent, CancellationToken ct)
    {
        if (discountPercent > 0 && !RolePermissions.HasPermission(user.Roles, Permissions.DiscountsApprove))
            throw new ForbiddenException(message: "Applying a discount requires manager approval permission.");

        var type = await db.RoomTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == roomTypeId && t.BranchId == branch.Id, ct)
            ?? throw new NotFoundException("Room type", roomTypeId);
        if (!type.IsActive) throw new ConflictException("ROOM_TYPE_INACTIVE", $"{type.Name} is not available for sale.");
        type.EnsureFits(adults, children);

        var plan = await ResolveRatePlanAsync(branch.Id, ratePlanId, ct);

        var rates = await db.RoomRates.AsNoTracking()
            .Where(r => r.BranchId == branch.Id && r.RatePlanId == plan.Id && r.RoomTypeId == type.Id
                        && r.StartDate < dates.Departure && r.EndDate >= dates.Arrival)
            .ToListAsync(ct);

        // When seasons overlap, the most recently created rate wins.
        var nights = dates.EachNight()
            .Select(n => new NightRate(n, rates.Where(r => r.Covers(n)).OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault()?.NightlyRate ?? type.BaseRate))
            .ToList();

        var taxes = await TaxesForAsync(branch.Id, TaxScope.Room, dates.Arrival, ct);
        var quote = PriceCalculator.Calculate(branch.Currency, nights, discountPercent, taxes, plan.DepositPercent);
        return new PricedStay(type, plan, quote);
    }

    public async Task<RatePlan> ResolveRatePlanAsync(Guid branchId, Guid? ratePlanId, CancellationToken ct)
    {
        var plans = db.RatePlans.AsNoTracking().Where(p => p.BranchId == branchId && p.IsActive);
        var plan = ratePlanId is { } id
            ? await plans.SingleOrDefaultAsync(p => p.Id == id, ct)
            : await plans.OrderBy(p => p.Code == DefaultRatePlanCode ? 0 : 1).ThenBy(p => p.Code).FirstOrDefaultAsync(ct);
        return plan ?? throw new ConflictException("RATE_PLAN_UNAVAILABLE", "No active rate plan is configured for this branch.");
    }

    public async Task<IReadOnlyList<TaxInput>> TaxesForAsync(Guid branchId, TaxScope scope, DateOnly date, CancellationToken ct)
    {
        var rules = await db.TaxRules.AsNoTracking()
            .Where(t => t.BranchId == branchId && t.IsActive && (t.Scope == scope || t.Scope == TaxScope.All)
                        && t.EffectiveFrom <= date && (t.EffectiveTo == null || t.EffectiveTo >= date))
            .OrderBy(t => t.Code)
            .ToListAsync(ct);
        return rules.Select(t => new TaxInput(t.Code, t.Name, t.RatePercent, t.IsInclusive)).ToList();
    }

    public QuoteDto ToDto(PricedStay p) => new(
        p.RoomType.Id, p.RatePlan.Id, p.RatePlan.Name, p.Quote.Currency, p.Quote.Nights.Count,
        p.Quote.Nights.Select(n => new QuoteNightDto(n.Date, n.Rate, n.Discount, n.Net)).ToList(),
        p.Quote.RoomSubtotal, p.Quote.DiscountAmount,
        p.Quote.Taxes.Select(t => new QuoteTaxDto(t.Code, t.Name, t.RatePercent, t.IsInclusive, t.Amount)).ToList(),
        p.Quote.ExclusiveTaxAmount, p.Quote.Total, p.Quote.DepositRequired, p.RatePlan.IsRefundable, p.RatePlan.CancellationPolicy);
}
