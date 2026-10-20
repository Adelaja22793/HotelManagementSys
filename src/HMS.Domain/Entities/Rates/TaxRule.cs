using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>
/// Configurable tax. Actual rates and treatment must come from the hotel's tax advisers;
/// nothing is hard-coded.
/// </summary>
public class TaxRule : BranchEntity, IConcurrencyTracked
{
    private TaxRule() { }

    public TaxRule(Guid companyId, Guid branchId, string code, string name, decimal ratePercent, TaxScope scope,
        bool isInclusive, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        CompanyId = companyId;
        BranchId = branchId;
        Code = Company.NormalizeCode(code);
        Update(name, ratePercent, scope, isInclusive, effectiveFrom, effectiveTo);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal RatePercent { get; private set; }
    public TaxScope Scope { get; private set; }
    /// <summary>True when the price already contains the tax (it is extracted, not added).</summary>
    public bool IsInclusive { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Update(string name, decimal ratePercent, TaxScope scope, bool isInclusive, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        Name = Guard.Required(name, "TAX_NAME_REQUIRED", "Tax name is required");
        if (ratePercent is <= 0 or > 100) throw new DomainException("TAX_RATE_INVALID", "Tax rate must be between 0 and 100 percent.");
        if (effectiveTo is { } to && to < effectiveFrom) throw new DomainException("DATES_INVALID", "Effective end must be after start.");
        RatePercent = ratePercent;
        Scope = scope;
        IsInclusive = isInclusive;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public bool AppliesTo(TaxScope scope, DateOnly date) =>
        IsActive && (Scope == TaxScope.All || Scope == scope) && date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
