namespace HMS.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "hms-api";
    public string Audience { get; set; } = "hms-clients";
    /// <summary>HMAC signing key, at least 32 bytes. Supply via user-secrets or environment, never source control.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
