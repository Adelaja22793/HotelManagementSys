using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Contracts.Rates;
using HMS.Domain.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Rates;

public interface IRateService
{
    Task<IReadOnlyList<RatePlanDto>> ListRatePlansAsync(Guid branchId, CancellationToken ct = default);
    Task<RatePlanDto> CreateRatePlanAsync(Guid branchId, CreateRatePlanRequest request, CancellationToken ct = default);
    Task<RatePlanDto> UpdateRatePlanAsync(Guid branchId, Guid id, UpdateRatePlanRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<RoomRateDto>> ListRoomRatesAsync(Guid branchId, Guid? ratePlanId, Guid? roomTypeId, CancellationToken ct = default);
    Task<RoomRateDto> CreateRoomRateAsync(Guid branchId, CreateRoomRateRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<TaxRuleDto>> ListTaxRulesAsync(Guid branchId, CancellationToken ct = default);
    Task<TaxRuleDto> CreateTaxRuleAsync(Guid branchId, CreateTaxRuleRequest request, CancellationToken ct = default);
    Task<TaxRuleDto> UpdateTaxRuleAsync(Guid branchId, Guid id, UpdateTaxRuleRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceItemDto>> ListServiceItemsAsync(Guid branchId, CancellationToken ct = default);
    Task<ServiceItemDto> CreateServiceItemAsync(Guid branchId, CreateServiceItemRequest request, CancellationToken ct = default);
    Task<ServiceItemDto> UpdateServiceItemAsync(Guid branchId, Guid id, UpdateServiceItemRequest request, CancellationToken ct = default);

    Task<QuoteDto> QuoteAsync(Guid branchId, QuoteRequest request, CancellationToken ct = default);
}

internal sealed class RateService(IApplicationDbContext db, IBranchAccessService access, ICurrentUser user, IClock clock,
    IPricingService pricing) : IRateService
{
    public async Task<IReadOnlyList<RatePlanDto>> ListRatePlansAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return (await db.RatePlans.AsNoTracking().Where(p => p.BranchId == branchId).OrderBy(p => p.Code).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<RatePlanDto> CreateRatePlanAsync(Guid branchId, CreateRatePlanRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Code), request.Code, 20).ThrowIfInvalid();
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.RatePlans.AnyAsync(p => p.BranchId == branchId && p.Code == code, ct))
            throw new ConflictException("RATE_PLAN_CODE_EXISTS", $"Rate plan '{code}' already exists.");
        var plan = new RatePlan(scope.CompanyId, branchId, code, request.Name);
        plan.Update(request.Name, request.IsRefundable, request.DepositPercent, request.CancellationPolicy);
        db.RatePlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<RatePlanDto> UpdateRatePlanAsync(Guid branchId, Guid id, UpdateRatePlanRequest request, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var plan = await db.RatePlans.GetOwnedAsync(branchId, id, "Rate plan", ct);
        BranchOperations.EnsureStamp(plan, request.ConcurrencyStamp, "rate plan");
        plan.Update(request.Name, request.IsRefundable, request.DepositPercent, request.CancellationPolicy);
        if (request.IsActive) plan.Activate(); else plan.Deactivate();
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<IReadOnlyList<RoomRateDto>> ListRoomRatesAsync(Guid branchId, Guid? ratePlanId, Guid? roomTypeId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.RoomRates.AsNoTracking().Where(r => r.BranchId == branchId);
        if (ratePlanId is { } p) q = q.Where(r => r.RatePlanId == p);
        if (roomTypeId is { } t) q = q.Where(r => r.RoomTypeId == t);
        var rates = await q.OrderBy(r => r.StartDate).Take(1000).ToListAsync(ct);
        return rates.Select(ToDto).ToList();
    }

    public async Task<RoomRateDto> CreateRoomRateAsync(Guid branchId, CreateRoomRateRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        await db.RatePlans.GetOwnedAsync(branchId, request.RatePlanId, "Rate plan", ct);
        await db.RoomTypes.GetOwnedAsync(branchId, request.RoomTypeId, "Room type", ct);
        var rate = new RoomRate(scope.CompanyId, branchId, request.RatePlanId, request.RoomTypeId, request.StartDate, request.EndDate, request.NightlyRate);
        db.RoomRates.Add(rate);
        await db.SaveChangesAsync(ct);
        return ToDto(rate);
    }

    public async Task<IReadOnlyList<TaxRuleDto>> ListTaxRulesAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return (await db.TaxRules.AsNoTracking().Where(t => t.BranchId == branchId).OrderBy(t => t.Code).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<TaxRuleDto> CreateTaxRuleAsync(Guid branchId, CreateTaxRuleRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Code), request.Code, 20).ThrowIfInvalid();
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.TaxRules.AnyAsync(t => t.BranchId == branchId && t.Code == code, ct))
            throw new ConflictException("TAX_CODE_EXISTS", $"Tax '{code}' already exists.");
        var tax = new TaxRule(scope.CompanyId, branchId, code, request.Name, request.RatePercent,
            EnumParser.Parse<TaxScope>(request.Scope, nameof(request.Scope)), request.IsInclusive, request.EffectiveFrom, request.EffectiveTo);
        db.TaxRules.Add(tax);
        await db.SaveChangesAsync(ct);
        return ToDto(tax);
    }

    public async Task<TaxRuleDto> UpdateTaxRuleAsync(Guid branchId, Guid id, UpdateTaxRuleRequest request, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var tax = await db.TaxRules.GetOwnedAsync(branchId, id, "Tax rule", ct);
        BranchOperations.EnsureStamp(tax, request.ConcurrencyStamp, "tax rule");
        tax.Update(request.Name, request.RatePercent, EnumParser.Parse<TaxScope>(request.Scope, nameof(request.Scope)),
            request.IsInclusive, request.EffectiveFrom, request.EffectiveTo);
        if (request.IsActive) tax.Activate(); else tax.Deactivate();
        await db.SaveChangesAsync(ct);
        return ToDto(tax);
    }

    public async Task<IReadOnlyList<ServiceItemDto>> ListServiceItemsAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return (await db.ServiceItems.AsNoTracking().Where(s => s.BranchId == branchId).OrderBy(s => s.Code).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<ServiceItemDto> CreateServiceItemAsync(Guid branchId, CreateServiceItemRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Code), request.Code, 20).ThrowIfInvalid();
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.ServiceItems.AnyAsync(s => s.BranchId == branchId && s.Code == code, ct))
            throw new ConflictException("SERVICE_CODE_EXISTS", $"Service '{code}' already exists.");
        var item = new ServiceItem(scope.CompanyId, branchId, code, request.Name, request.UnitPrice, request.IsTaxable);
        db.ServiceItems.Add(item);
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public async Task<ServiceItemDto> UpdateServiceItemAsync(Guid branchId, Guid id, UpdateServiceItemRequest request, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var item = await db.ServiceItems.GetOwnedAsync(branchId, id, "Service item", ct);
        BranchOperations.EnsureStamp(item, request.ConcurrencyStamp, "service item");
        item.Update(request.Name, request.UnitPrice, request.IsTaxable);
        if (request.IsActive) item.Activate(); else item.Deactivate();
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public async Task<QuoteDto> QuoteAsync(Guid branchId, QuoteRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var priced = await pricing.PriceAsync(scope.Branch, request.RoomTypeId, request.RatePlanId,
            new StayDates(request.ArrivalDate, request.DepartureDate), request.Adults, request.Children, request.DiscountPercent, ct);
        return pricing.ToDto(priced);
    }

    private static RatePlanDto ToDto(RatePlan p) => new(p.Id, p.Code, p.Name, p.IsRefundable, p.DepositPercent, p.CancellationPolicy, p.IsActive, p.ConcurrencyStamp);
    private static RoomRateDto ToDto(RoomRate r) => new(r.Id, r.RatePlanId, r.RoomTypeId, r.StartDate, r.EndDate, r.NightlyRate);
    private static TaxRuleDto ToDto(TaxRule t) => new(t.Id, t.Code, t.Name, t.RatePercent, t.Scope.ToString(), t.IsInclusive, t.EffectiveFrom, t.EffectiveTo, t.IsActive, t.ConcurrencyStamp);
    private static ServiceItemDto ToDto(ServiceItem s) => new(s.Id, s.Code, s.Name, s.UnitPrice, s.IsTaxable, s.IsActive, s.ConcurrencyStamp);
}
