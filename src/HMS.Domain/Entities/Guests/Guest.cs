using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>Guest profile. Reuse an existing profile instead of creating duplicates.</summary>
public class Guest : BranchEntity, IConcurrencyTracked
{
    private Guest() { }

    public Guest(Guid companyId, Guid branchId, string firstName, string lastName)
    {
        CompanyId = companyId;
        BranchId = branchId;
        Update(firstName, lastName, null, null, null, null, null, null, null);
    }

    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Nationality { get; private set; }
    public string? IdDocumentType { get; private set; }
    /// <summary>Personal data: never log, export only with permission.</summary>
    public string? IdDocumentNumber { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Update(string firstName, string lastName, string? email, string? phone, string? nationality,
        string? idDocumentType, string? idDocumentNumber, string? address, string? notes)
    {
        FirstName = Guard.Required(firstName, "GUEST_NAME_REQUIRED", "First name is required", 100);
        LastName = Guard.Required(lastName, "GUEST_NAME_REQUIRED", "Last name is required", 100);
        Email = Clean(email)?.ToLowerInvariant();
        if (Email is not null && (!Email.Contains('@') || Email.Length > 256))
            throw new DomainException("EMAIL_INVALID", "Email address is not valid.");
        Phone = NormalizePhone(phone);
        Nationality = Clean(nationality);
        IdDocumentType = Clean(idDocumentType);
        IdDocumentNumber = Clean(idDocumentNumber);
        Address = Clean(address);
        Notes = Clean(notes);
    }

    public static string? NormalizePhone(string? phone)
    {
        var value = Clean(phone);
        if (value is null) return null;
        var digits = new string(value.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (digits.Length < 7) throw new DomainException("PHONE_INVALID", "Phone number is not valid.");
        return digits;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
