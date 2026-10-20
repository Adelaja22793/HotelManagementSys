namespace HMS.Infrastructure.Identity;

/// <summary>
/// Controlled first-run setup: creates the company, first branch and first SystemAdmin when the
/// database has no users. Credentials come from configuration (user-secrets / environment), never code.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public bool Enabled { get; set; }
    public string CompanyName { get; set; } = "Hotel Company";
    public string CompanyCode { get; set; } = "HQ";
    public string BranchName { get; set; } = "Main Branch";
    public string BranchCode { get; set; } = "MAIN";
    public string TimeZoneId { get; set; } = "Africa/Lagos";
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminFullName { get; set; } = "System Administrator";
    public string AdminPassword { get; set; } = string.Empty;
}
