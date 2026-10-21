using System.Net;
using System.Security.Cryptography;
using System.Text;
using HMS.Application.PublicBooking;
using HMS.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HMS.Infrastructure.IntegrationTests;

/// <summary>The Paystack adapter against canned provider responses (no network).</summary>
public class PaystackGatewayTests
{
    private const string Secret = "sk_test_unit";

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (PaystackGateway Gateway, Handler Handler) Create(HttpStatusCode status, string body, string? secret = Secret)
    {
        var handler = new Handler(status, body);
        var gateway = new PaystackGateway(new HttpClient(handler), Options.Create(new PaystackOptions { SecretKey = secret }),
            NullLogger<PaystackGateway>.Instance);
        return (gateway, handler);
    }

    [Fact]
    public async Task Initialize_sends_kobo_and_the_secret_key()
    {
        var (gateway, handler) = Create(HttpStatusCode.OK, """{"status":true,"message":"ok","data":{"authorization_url":"https://checkout.paystack.com/abc"}}""");
        var result = await gateway.InitializeAsync(new GatewayInitRequest("HMS-REF-1", 105_000.50m, "NGN", "guest@test", "https://book/return",
            new Dictionary<string, string>()), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("https://checkout.paystack.com/abc", result.AuthorizationUrl);
        Assert.Equal("Bearer", handler.Last!.Headers.Authorization!.Scheme);
        Assert.Equal(Secret, handler.Last.Headers.Authorization.Parameter);
        Assert.Contains("\"amount\":10500050", handler.LastBody);
        Assert.Contains("\"reference\":\"HMS-REF-1\"", handler.LastBody);
    }

    [Theory]
    [InlineData("success", GatewayOutcome.Success)]
    [InlineData("failed", GatewayOutcome.Failed)]
    [InlineData("abandoned", GatewayOutcome.Failed)]
    [InlineData("reversed", GatewayOutcome.Failed)]
    [InlineData("ongoing", GatewayOutcome.Pending)]
    [InlineData("pending", GatewayOutcome.Pending)]
    public async Task Verify_maps_provider_status_and_converts_kobo(string status, GatewayOutcome expected)
    {
        var (gateway, _) = Create(HttpStatusCode.OK,
            $$$"""{"status":true,"data":{"id":987,"status":"{{{status}}}","reference":"HMS-REF-1","amount":10500000,"currency":"NGN"}}""");
        var result = await gateway.VerifyAsync("HMS-REF-1", CancellationToken.None);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(105_000m, result.Amount);
        Assert.Equal("NGN", result.Currency);
        Assert.Equal("987", result.TransactionId);
    }

    [Fact]
    public async Task Unknown_reference_or_missing_key_never_counts_as_paid()
    {
        Assert.Equal(GatewayOutcome.Unknown, (await Create(HttpStatusCode.NotFound, """{"status":false}""").Gateway.VerifyAsync("X", CancellationToken.None)).Outcome);
        Assert.Equal(GatewayOutcome.Unknown, (await Create(HttpStatusCode.OK, "{}", secret: null).Gateway.VerifyAsync("X", CancellationToken.None)).Outcome);
        Assert.Equal(GatewayOutcome.Unknown, (await Create(HttpStatusCode.InternalServerError, "oops").Gateway.VerifyAsync("X", CancellationToken.None)).Outcome);
    }

    [Fact]
    public void Webhook_signature_is_hmac_sha512_of_the_raw_body()
    {
        var (gateway, _) = Create(HttpStatusCode.OK, "{}");
        var body = Encoding.UTF8.GetBytes("""{"event":"charge.success","data":{"reference":"HMS-REF-1"}}""");
        var good = Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(Secret), body));
        Assert.True(gateway.IsValidWebhook(body, good));
        Assert.True(gateway.IsValidWebhook(body, good.ToUpperInvariant()));
        Assert.False(gateway.IsValidWebhook(body, null));
        Assert.False(gateway.IsValidWebhook(body, "not-hex"));
        var tampered = Encoding.UTF8.GetBytes("""{"event":"charge.success","data":{"reference":"HMS-REF-2"}}""");
        Assert.False(gateway.IsValidWebhook(tampered, good));

        var evt = gateway.ParseWebhook(body);
        Assert.Equal("charge.success", evt!.EventType);
        Assert.Equal("HMS-REF-1", evt.Reference);
        Assert.Null(gateway.ParseWebhook("not json"u8));
    }
}
