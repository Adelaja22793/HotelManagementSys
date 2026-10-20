using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>Commercial terms a room is sold under (e.g. Best Available Rate, Non-refundable).</summary>
public class RatePlan : BranchEntity, IConcurrencyTracked
{
    private RatePlan() { }

    public RatePlan(Guid companyId, Guid branchId, string code, string name)
    {
        CompanyId = companyId;
        BranchId = branchId;
        Code = Company.NormalizeCode(code);
        Update(name, true, 0, "Free cancellation until 24 hours before arrival.");
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsRefundable { get; private set; }
    /// <summary>Share of the stay total required as deposit, 0-100.</summary>
    public decimal DepositPercent { get; private set; }
    public string CancellationPolicy { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Update(string name, bool isRefundable, decimal depositPercent, string cancellationPolicy)
    {
        Name = Guard.Required(name, "RATE_PLAN_NAME_REQUIRED", "Rate plan name is required");
        if (depositPercent is < 0 or > 100) throw new DomainException("DEPOSIT_INVALID", "Deposit must be between 0 and 100 percent.");
        IsRefundable = isRefundable;
        DepositPercent = depositPercent;
        CancellationPolicy = Guard.Required(cancellationPolicy, "POLICY_REQUIRED", "Cancellation policy is required", 1000);
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
