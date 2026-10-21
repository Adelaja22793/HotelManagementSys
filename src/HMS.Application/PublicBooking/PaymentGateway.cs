namespace HMS.Application.PublicBooking;

public sealed class PublicBookingOptions
{
    public const string SectionName = "PublicBooking";

    /// <summary>How long rooms are held while the guest completes the booking.</summary>
    public int HoldMinutes { get; set; } = 15;
    /// <summary>Most holds one client (by IP address) may keep open at once.</summary>
    public int MaxActiveHoldsPerClient { get; set; } = 3;
    /// <summary>Full (pay the whole stay now) or Deposit (pay the rate plan's deposit percentage; full when it is zero).</summary>
    public string PaymentMode { get; set; } = "Full";
    /// <summary>Paystack or Sandbox (development only: approves every payment without real money).</summary>
    public string PaymentProvider { get; set; } = "Paystack";
    /// <summary>Base URL of the booking website; the provider sends the guest back to {SiteUrl}/booking/{reference}/payment-return.</summary>
    public string SiteUrl { get; set; } = "";
    /// <summary>Origins allowed to call the public API from a browser (CORS).</summary>
    public string[] AllowedOrigins { get; set; } = [];
    /// <summary>Optional: only show branches of this company code (when one database serves several companies).</summary>
    public string? CompanyCode { get; set; }
    /// <summary>How far ahead guests can book.</summary>
    public int MaxDaysAhead { get; set; } = 365;
}

public sealed record GatewayInitRequest(string MerchantReference, decimal Amount, string Currency, string Email, string CallbackUrl,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record GatewayInitResult(bool Success, string? AuthorizationUrl, string? Error);

/// <summary>Outcome is Success, Failed, Pending or Unknown (the provider has no such transaction or could not be reached).</summary>
public enum GatewayOutcome { Success, Failed, Pending, Unknown }

public sealed record GatewayVerification(GatewayOutcome Outcome, string ProviderStatus, string? TransactionId, decimal? Amount, string? Currency,
    string? Reference);

public sealed record GatewayWebhookEvent(string EventType, string? Reference);

/// <summary>An online card/bank payment provider. Implementations never see or store card numbers; the guest pays on the provider's page.</summary>
public interface IPaymentGateway
{
    string Name { get; }
    Task<GatewayInitResult> InitializeAsync(GatewayInitRequest request, CancellationToken ct);

    /// <summary>Asks the provider directly for the transaction status. Callback query strings and webhook bodies are never trusted alone.</summary>
    Task<GatewayVerification> VerifyAsync(string merchantReference, CancellationToken ct);

    /// <summary>Checks the webhook signature against the raw request body.</summary>
    bool IsValidWebhook(ReadOnlySpan<byte> body, string? signature);

    GatewayWebhookEvent? ParseWebhook(ReadOnlySpan<byte> body);
}
