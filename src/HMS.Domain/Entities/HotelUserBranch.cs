using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>
/// Grants a staff user access to a branch. Branch membership is separate from roles:
/// a user with a valid role is still unauthorised for branches they are not assigned to.
/// </summary>
public class HotelUserBranch : AuditableEntity
{
    private HotelUserBranch() { }

    public HotelUserBranch(string userId, Guid companyId, Guid branchId, bool isDefault = false)
    {
        UserId = Guard.Required(userId, "USER_REQUIRED", "User is required", 450);
        CompanyId = companyId;
        BranchId = branchId;
        IsDefault = isDefault;
    }

    public string UserId { get; private set; } = string.Empty;
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Branch? Branch { get; private set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; private set; } = true;

    public void Revoke() => IsActive = false;
    public void Restore() => IsActive = true;
}
