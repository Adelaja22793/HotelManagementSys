using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HMS.Application.PublicBooking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HMS.Infrastructure.Payments;

public sealed class PaystackOptions
{
    public const string SectionName = "Paystack";
    public string BaseUrl { get; set; } = "https://api.paystack.co";
    /// <summary>Secret key (sk_live_... or sk_test_...). Also signs webhooks. Keep it in the secret store.</summary>
    public string? SecretKey { get; set; }
}

/// <summary>
/// Paystack (Nigeria). The guest pays on Paystack's own page, so card details never reach the hotel's servers.
/// Amounts are sent in kobo. Results are always re-checked with the verify endpoint before a booking is confirmed.
/// </summary>
internal sealed class PaystackGateway(HttpClient http, IOptions<PaystackOptions> options, ILogger<PaystackGateway> logger) : IPaymentGateway
{
    public string Name => "paystack";

    private string BaseUrl => options.Value.BaseUrl.TrimEnd('/');

    public async Task<GatewayInitResult> InitializeAsync(GatewayInitRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.SecretKey)) return new GatewayInitResult(false, null, "Paystack:SecretKey is not configured.");
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/transaction/initialize")
        {
            Content = JsonContent.Create(new InitBody(request.Email, ToKobo(request.Amount), request.Currency, request.MerchantReference,
                request.CallbackUrl, request.Metadata)),
        };
        Authorize(message);
        try
        {
            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadFromJsonAsync<Envelope<InitData>>(ct);
            if (response.IsSuccessStatusCode && body is { Status: true, Data.AuthorizationUrl: { } url })
                return new GatewayInitResult(true, url, null);
            logger.LogWarning("Paystack initialize returned {Status}: {Message}", (int)response.StatusCode, body?.Message);
            return new GatewayInitResult(false, null, $"Paystack returned {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Paystack initialize failed");
            return new GatewayInitResult(false, null, "Paystack could not be reached.");
        }
    }

    public async Task<GatewayVerification> VerifyAsync(string merchantReference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.SecretKey)) return new GatewayVerification(GatewayOutcome.Unknown, "not-configured", null, null, null, null);
        using var message = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/transaction/verify/{Uri.EscapeDataString(merchantReference)}");
        Authorize(message);
        try
        {
            using var response = await http.SendAsync(message, ct);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                return new GatewayVerification(GatewayOutcome.Unknown, "not-found", null, null, null, null);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<Envelope<VerifyData>>(ct);
            if (body?.Data is not { } data) return new GatewayVerification(GatewayOutcome.Unknown, "empty", null, null, null, null);
            var outcome = data.Status switch
            {
                "success" => GatewayOutcome.Success,
                "failed" or "abandoned" or "reversed" => GatewayOutcome.Failed,
                _ => GatewayOutcome.Pending, // ongoing, pending, processing, queued
            };
            return new GatewayVerification(outcome, data.Status ?? "unknown", data.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                data.Amount is { } kobo ? kobo / 100m : null, data.Currency, data.Reference);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Paystack verify failed for {Reference}", merchantReference);
            return new GatewayVerification(GatewayOutcome.Unknown, "unreachable", null, null, null, null);
        }
    }

    /// <summary>Paystack signs the raw body with HMAC-SHA512 using the secret key (header x-paystack-signature).</summary>
    public bool IsValidWebhook(ReadOnlySpan<byte> body, string? signature)
    {
        if (string.IsNullOrWhiteSpace(options.Value.SecretKey) || string.IsNullOrWhiteSpace(signature)) return false;
        var expected = HMACSHA512.HashData(Encoding.UTF8.GetBytes(options.Value.SecretKey), body);
        byte[] given;
        try { given = Convert.FromHexString(signature.Trim()); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    public GatewayWebhookEvent? ParseWebhook(ReadOnlySpan<byte> body)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<WebhookBody>(body);
            return evt?.Event is null ? null : new GatewayWebhookEvent(evt.Event, evt.Data?.Reference);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Authorize(HttpRequestMessage message) =>
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.SecretKey);

    private static long ToKobo(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private sealed record InitBody(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("callback_url")] string CallbackUrl,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata);

    private sealed record Envelope<T>(
        [property: JsonPropertyName("status")] bool Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("data")] T? Data);

    private sealed record InitData([property: JsonPropertyName("authorization_url")] string? AuthorizationUrl);

    private sealed record VerifyData(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("reference")] string? Reference,
        [property: JsonPropertyName("amount")] long? Amount,
        [property: JsonPropertyName("currency")] string? Currency);

    private sealed record WebhookBody(
        [property: JsonPropertyName("event")] string? Event,
        [property: JsonPropertyName("data")] WebhookData? Data);

    private sealed record WebhookData([property: JsonPropertyName("reference")] string? Reference);
}

/// <summary>
/// Development stand-in that "approves" every payment without moving money. The API refuses to start with it outside
/// Development and Testing.
/// </summary>
internal sealed class SandboxPaymentGateway(IOptions<PublicBookingOptions> options) : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, (decimal Amount, string Currency)> _started = new(StringComparer.Ordinal);

    public string Name => "sandbox";

    public Task<GatewayInitResult> InitializeAsync(GatewayInitRequest request, CancellationToken ct)
    {
        _started[request.MerchantReference] = (request.Amount, request.Currency);
        // Sends the guest straight back as if they had paid.
        return Task.FromResult(new GatewayInitResult(true, request.CallbackUrl, null));
    }

    public Task<GatewayVerification> VerifyAsync(string merchantReference, CancellationToken ct) =>
        Task.FromResult(_started.TryGetValue(merchantReference, out var started)
            ? new GatewayVerification(GatewayOutcome.Success, "success", "sandbox-" + merchantReference, started.Amount, started.Currency, merchantReference)
            : new GatewayVerification(GatewayOutcome.Unknown, "not-found", null, null, null, null));

    public bool IsValidWebhook(ReadOnlySpan<byte> body, string? signature) => false;

    public GatewayWebhookEvent? ParseWebhook(ReadOnlySpan<byte> body) => null;

    internal string SiteUrl => options.Value.SiteUrl;
}

/// <summary>Picks the provider from PublicBooking:PaymentProvider at call time.</summary>
internal sealed class ConfiguredPaymentGateway(IOptions<PublicBookingOptions> options, PaystackGateway paystack, SandboxPaymentGateway sandbox)
    : IPaymentGateway
{
    private IPaymentGateway Current =>
        string.Equals(options.Value.PaymentProvider, "Sandbox", StringComparison.OrdinalIgnoreCase) ? sandbox : paystack;

    public string Name => Current.Name;
    public Task<GatewayInitResult> InitializeAsync(GatewayInitRequest request, CancellationToken ct) => Current.InitializeAsync(request, ct);
    public Task<GatewayVerification> VerifyAsync(string merchantReference, CancellationToken ct) => Current.VerifyAsync(merchantReference, ct);
    public bool IsValidWebhook(ReadOnlySpan<byte> body, string? signature) => Current.IsValidWebhook(body, signature);
    public GatewayWebhookEvent? ParseWebhook(ReadOnlySpan<byte> body) => Current.ParseWebhook(body);
}
