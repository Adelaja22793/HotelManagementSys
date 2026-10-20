using Microsoft.AspNetCore.Identity;

namespace HMS.Infrastructure.Identity;

/// <summary>Staff account. Guests are not identity users.</summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Set for accounts created with a temporary password.</summary>
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}
