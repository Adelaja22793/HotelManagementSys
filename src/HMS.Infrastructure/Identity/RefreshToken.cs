using HMS.Domain.Common;

namespace HMS.Infrastructure.Identity;

/// <summary>
/// Rotating refresh token. Only a SHA-256 hash is stored. Re-use of a rotated token revokes the
/// whole token family for the user (theft detection).
/// </summary>
public class RefreshToken : Entity
{
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive(DateTime utcNow) => RevokedAtUtc is null && ExpiresAtUtc > utcNow;
}
