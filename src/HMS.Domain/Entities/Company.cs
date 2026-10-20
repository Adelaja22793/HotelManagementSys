using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>The hotel company that owns one or more branches.</summary>
public class Company : AuditableEntity, IConcurrencyTracked
{
    private Company() { }

    public Company(string name, string code, string defaultCurrency = "NGN")
    {
        Rename(name);
        Code = NormalizeCode(code);
        DefaultCurrency = Currency.Normalize(defaultCurrency);
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string DefaultCurrency { get; private set; } = "NGN";
    public bool IsActive { get; private set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public ICollection<Branch> Branches { get; private set; } = new List<Branch>();

    public void Rename(string name) => Name = Guard.Required(name, "COMPANY_NAME_REQUIRED", "Company name is required");
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    internal static string NormalizeCode(string code)
    {
        var value = Guard.Required(code, "CODE_REQUIRED", "Code is required", 20).ToUpperInvariant();
        if (!value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new DomainException("CODE_INVALID", "Code may contain only letters, digits and '-'.");
        return value;
    }
}
