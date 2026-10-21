using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using HMS.Api.IntegrationTests;
using HMS.Application.Security;
using Microsoft.IdentityModel.Tokens;

namespace HMS.SecurityTests;

/// <summary>Access tokens that were edited, unsigned or signed with another key are refused.</summary>
public sealed class TokenTamperingTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    private async Task<HttpStatusCode> CallWithAsync(string token)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync("/api/v1/users")).StatusCode;
    }

    private async Task<(string Token, string UserId)> ReceptionistTokenAsync()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var (client, userId) = await hotel.StaffWithIdAsync(Roles.Receptionist);
        return (client.DefaultRequestHeaders.Authorization!.Parameter!, userId);
    }

    private static string B64(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));

    [Fact]
    public async Task Genuine_token_is_accepted_but_lacks_the_permission()
    {
        var (token, _) = await ReceptionistTokenAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await CallWithAsync(token));
    }

    [Fact]
    public async Task Adding_a_role_to_the_payload_breaks_the_signature()
    {
        var (token, _) = await ReceptionistTokenAsync();
        var parts = token.Split('.');
        var payload = JsonNode.Parse(Base64UrlEncoder.Decode(parts[1]))!.AsObject();
        payload[ClaimTypes.Role] = new JsonArray(Roles.Receptionist, Roles.SystemAdmin);
        var forged = $"{parts[0]}.{B64(payload.ToJsonString())}.{parts[2]}";
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWithAsync(forged));
    }

    [Fact]
    public async Task Unsigned_token_is_refused()
    {
        var (token, _) = await ReceptionistTokenAsync();
        var parts = token.Split('.');
        var payload = JsonNode.Parse(Base64UrlEncoder.Decode(parts[1]))!.AsObject();
        payload[ClaimTypes.Role] = Roles.SystemAdmin;
        var unsigned = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64(payload.ToJsonString())}.";
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWithAsync(unsigned));
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_refused()
    {
        var (token, _) = await ReceptionistTokenAsync();
        var original = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var claims = original.Claims.Where(c => c.Type is not ("aud" or "iss" or "exp" or "nbf" or "iat")).ToList();
        claims.Add(new Claim(ClaimTypes.Role, Roles.SystemAdmin));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("an-attacker-chosen-key-0123456789abcdef!!"));
        var forged = new JwtSecurityToken(original.Issuer, original.Audiences.First(), claims, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWithAsync(new JwtSecurityTokenHandler().WriteToken(forged)));
    }

    [Fact]
    public async Task Garbage_and_empty_tokens_are_refused_without_errors()
    {
        foreach (var token in new[] { "x", "a.b.c", "....", new string('A', 5000) })
            Assert.Equal(HttpStatusCode.Unauthorized, await CallWithAsync(token));
    }
}
