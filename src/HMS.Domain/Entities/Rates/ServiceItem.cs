using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>Billable service catalogue entry (laundry, airport pickup, minibar...).</summary>
public class ServiceItem : BranchEntity, IConcurrencyTracked
{
    private ServiceItem() { }

    public ServiceItem(Guid companyId, Guid branchId, string code, string name, decimal unitPrice, bool isTaxable)
    {
        CompanyId = companyId;
        BranchId = branchId;
        Code = Company.NormalizeCode(code);
        Update(name, unitPrice, isTaxable);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public bool IsTaxable { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Update(string name, decimal unitPrice, bool isTaxable)
    {
        Name = Guard.Required(name, "SERVICE_NAME_REQUIRED", "Service name is required");
        UnitPrice = Money.RequirePositive(unitPrice);
        IsTaxable = isTaxable;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
