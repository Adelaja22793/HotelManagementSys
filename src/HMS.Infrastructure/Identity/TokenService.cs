using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HMS.Infrastructure.Identity;

public sealed class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user, IEnumerable<string> roles, DateTime utcNow)
    {
        var expires = utcNow.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(HmsClaimTypes.CompanyId, user.CompanyId.ToString()),
            new(HmsClaimTypes.SecurityStamp, user.SecurityStamp ?? string.Empty),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var credentials = new SigningCredentials(GetSigningKey(_options), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, utcNow, expires, credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public (string Token, string Hash, DateTime ExpiresAtUtc) CreateRefreshToken(DateTime utcNow)
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return (token, Hash(token), utcNow.AddDays(_options.RefreshTokenDays));
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static SymmetricSecurityKey GetSigningKey(JwtOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(options.SigningKey ?? string.Empty);
        if (bytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be configured with at least 32 bytes (use user-secrets or an environment variable).");
        return new SymmetricSecurityKey(bytes);
    }
}
