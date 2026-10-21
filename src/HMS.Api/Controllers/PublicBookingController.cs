using System.Security.Cryptography;
using System.Text;
using HMS.Application.PublicBooking;
using HMS.Contracts.PublicBooking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HMS.Api.Controllers;

/// <summary>
/// The only routes the public website can call. They are anonymous, rate limited per client, and never expose
/// internal ids beyond room types. Calls on an existing booking need the X-Booking-Token returned when it was created.
/// </summary>
[Route(V1 + "/public")]
[AllowAnonymous]
[EnableCors(PublicCors.Policy)]
[EnableRateLimiting(RateLimitPolicies.PublicRead)]
public sealed class PublicBookingController(IPublicBookingService booking) : ApiControllerBase
{
    public const string TokenHeader = "X-Booking-Token";

    private string? Token => Request.Headers[TokenHeader].FirstOrDefault();

    [HttpGet("branches")]
    public Task<IReadOnlyList<PublicBranchDto>> Branches(CancellationToken ct) => booking.ListBranchesAsync(ct);

    [HttpGet("branches/{code}/availability")]
    public Task<PublicAvailabilityDto> Availability(string code, [FromQuery] DateOnly arrival, [FromQuery] DateOnly departure,
        [FromQuery] int adults = 1, [FromQuery] int children = 0, CancellationToken ct = default) =>
        booking.AvailabilityAsync(code, arrival, departure, adults, children, ct);

    [HttpPost("branches/{code}/holds")]
    [EnableRateLimiting(RateLimitPolicies.PublicWrite)]
    public async Task<ActionResult<HoldDto>> CreateHold(string code, CreateHoldRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await booking.CreateHoldAsync(code, request, ClientFingerprint(), ct));

    [HttpGet("holds/{reference}")]
    public Task<HoldDto> GetHold(string reference, CancellationToken ct) => booking.GetHoldAsync(reference, Token, ct);

    [HttpPut("holds/{reference}/guest")]
    [EnableRateLimiting(RateLimitPolicies.PublicWrite)]
    public Task<HoldDto> SetGuest(string reference, GuestDetailsRequest request, CancellationToken ct) => booking.SetGuestAsync(reference, Token, request, ct);

    [HttpDelete("holds/{reference}")]
    public async Task<IActionResult> Release(string reference, CancellationToken ct)
    {
        await booking.ReleaseHoldAsync(reference, Token, ct);
        return NoContent();
    }

    [HttpPost("holds/{reference}/payments")]
    [EnableRateLimiting(RateLimitPolicies.PublicWrite)]
    public Task<PaymentInitDto> StartPayment(string reference, CancellationToken ct) => booking.StartPaymentAsync(reference, Token, ct);

    /// <summary>Called by the website when the guest returns from the payment page. The result is checked with the provider.</summary>
    [HttpPost("payments/{merchantReference}/verify")]
    public Task<PublicBookingDto> Verify(string merchantReference, CancellationToken ct) => booking.VerifyPaymentAsync(merchantReference, Token, ct);

    /// <summary>Paystack webhook. The signature is checked against the raw body; the result is then re-checked with Paystack.</summary>
    [HttpPost("payments/paystack/webhook")]
    [DisableCors]
    [DisableRateLimiting]
    [RequestSizeLimit(64 * 1024)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> PaystackWebhook(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var ok = await booking.HandleWebhookAsync(buffer.ToArray(), Request.Headers["x-paystack-signature"].FirstOrDefault(), ct);
        return ok ? Ok() : Unauthorized();
    }

    [HttpGet("bookings/{reference}")]
    public Task<PublicBookingDto> GetBooking(string reference, CancellationToken ct) => booking.GetBookingAsync(reference, Token, ct);

    [HttpPost("bookings/{reference}/cancellation-request")]
    [EnableRateLimiting(RateLimitPolicies.PublicWrite)]
    public Task<PublicBookingDto> RequestCancellation(string reference, CancellationRequest request, CancellationToken ct) =>
        booking.RequestCancellationAsync(reference, Token, request, ct);

    /// <summary>Hashed client address, used only to limit open holds per client. The raw IP is not stored.</summary>
    private string? ClientFingerprint()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        return ip is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hms-hold:" + ip)));
    }
}

public static class PublicCors
{
    public const string Policy = "public-booking";
}
