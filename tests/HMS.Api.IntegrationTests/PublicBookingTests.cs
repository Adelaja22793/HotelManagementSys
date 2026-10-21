using System.Net;
using System.Net.Http.Json;
using System.Text;
using HMS.Api.Services;
using HMS.Application.PublicBooking;
using HMS.Application.Security;
using HMS.Contracts.Accounting;
using HMS.Contracts.Common;
using HMS.Contracts.Notifications;
using HMS.Contracts.PublicBooking;
using HMS.Contracts.Reservations;
using HMS.Contracts.Rooms;

namespace HMS.Api.IntegrationTests;

/// <summary>Phase 5: the public booking API used by the hotel website.</summary>
public class PublicBookingTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    private const string Token = "X-Booking-Token";

    // ---------- helpers ----------

    private async Task<HotelSetup> PublicHotelAsync(int rooms = 3)
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: rooms);
        var response = await hotel.Admin.PutAsJsonAsync(hotel.P("public-booking"), new PublicBookingSettingsRequest(true));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return hotel;
    }

    private HttpClient Guest(string? token = null)
    {
        var client = factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Add(Token, token);
        return client;
    }

    private static Task<HttpResponseMessage> HoldAsync(HttpClient guest, HotelSetup hotel, int nights = 2, int quantity = 1, DateOnly? arrival = null) =>
        guest.PostAsJsonAsync($"/api/v1/public/branches/{hotel.Branch.Code}/holds", new CreateHoldRequest(arrival ?? hotel.Today.AddDays(7),
            (arrival ?? hotel.Today.AddDays(7)).AddDays(nights), 2, 0, [new HoldRoomRequest(hotel.Standard.Id, quantity)]));

    private static async Task<HoldDto> HeldAsync(HttpClient guest, HotelSetup hotel, int nights = 2, int quantity = 1, DateOnly? arrival = null) =>
        await ApiAssert.OkAsync<HoldDto>(await HoldAsync(guest, hotel, nights, quantity, arrival), HttpStatusCode.Created);

    private static GuestDetailsRequest Details(string last = "Webguest") =>
        new("Chioma", last, $"{last.ToLowerInvariant()}@guest.test", "+2348011111111", "NG", "Late arrival", true);

    /// <summary>Hold, guest details and payment start; returns the guest client (with token), the hold and the payment.</summary>
    private async Task<(HttpClient Guest, HoldDto Hold, PaymentInitDto Payment)> ReadyToPayAsync(HotelSetup hotel, int nights = 2, int quantity = 1,
        string last = "Webguest")
    {
        var hold = await HeldAsync(Guest(), hotel, nights, quantity);
        var guest = Guest(hold.AccessToken);
        await ApiAssert.OkAsync<HoldDto>(await guest.PutAsJsonAsync($"/api/v1/public/holds/{hold.Reference}/guest", Details(last)));
        var payment = await ApiAssert.OkAsync<PaymentInitDto>(await guest.PostAsync($"/api/v1/public/holds/{hold.Reference}/payments", null));
        return (guest, hold, payment);
    }

    private static async Task<PublicBookingDto> VerifyAsync(HttpClient guest, string merchantReference) =>
        await ApiAssert.OkAsync<PublicBookingDto>(await guest.PostAsync($"/api/v1/public/payments/{merchantReference}/verify", null));

    private static async Task<InventoryDayDto> NightAsync(HotelSetup hotel, DateOnly date) =>
        (await hotel.GetAsync<List<InventoryDayDto>>($"inventory?start={date:yyyy-MM-dd}&end={date.AddDays(1):yyyy-MM-dd}"))
            .Single(d => d.RoomTypeId == hotel.Standard.Id);

    /// <summary>Uses the hotel's admin client: a sign-in after the test clock moves would issue a token the real clock rejects.</summary>
    private static async Task<JobRunResult> RunJobsAsync(HotelSetup hotel) =>
        await ApiAssert.OkAsync<JobRunResult>(await hotel.Admin.PostAsync("/api/v1/system/jobs/run", null));

    private Task<HttpResponseMessage> WebhookAsync(string reference, string? signature = null)
    {
        var body = Encoding.UTF8.GetBytes($"{{\"event\":\"charge.success\",\"reference\":\"{reference}\"}}");
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/public/payments/paystack/webhook") { Content = new ByteArrayContent(body) };
        message.Content.Headers.ContentType = new("application/json");
        message.Headers.Add("x-paystack-signature", signature ?? FakePaymentGateway.Sign(body));
        return Guest().SendAsync(message);
    }

    // ---------- the happy path ----------

    [Fact]
    public async Task Guest_searches_holds_pays_and_gets_a_confirmed_booking()
    {
        var hotel = await PublicHotelAsync();
        var anonymous = Guest();
        var branches = await ApiAssert.OkAsync<List<PublicBranchDto>>(await anonymous.GetAsync("/api/v1/public/branches"));
        Assert.Contains(branches, b => b.Code == hotel.Branch.Code);

        var arrival = hotel.Today.AddDays(7);
        var search = await ApiAssert.OkAsync<PublicAvailabilityDto>(await anonymous.GetAsync(
            $"/api/v1/public/branches/{hotel.Branch.Code}/availability?arrival={arrival:yyyy-MM-dd}&departure={arrival.AddDays(2):yyyy-MM-dd}&adults=2"));
        var offer = Assert.Single(search.Offers);
        Assert.Equal(3, offer.Available);
        Assert.Equal(100_000m, offer.RoomSubtotal);
        Assert.Equal(105_000m, offer.TotalAmount);
        Assert.Equal(105_000m, offer.AmountDueNow);

        var hold = await HeldAsync(anonymous, hotel);
        Assert.Equal("Active", hold.Status);
        Assert.NotNull(hold.AccessToken);
        Assert.Matches("^[A-Z2-9]{10}$", hold.Reference);
        Assert.Equal(1, (await NightAsync(hotel, arrival)).Held);

        // Without the token the booking does not exist; with the wrong one neither.
        await ApiAssert.ErrorAsync(await Guest().GetAsync($"/api/v1/public/holds/{hold.Reference}"), HttpStatusCode.NotFound);
        await ApiAssert.ErrorAsync(await Guest("wrong-token").GetAsync($"/api/v1/public/holds/{hold.Reference}"), HttpStatusCode.NotFound);

        var guest = Guest(hold.AccessToken);
        Assert.Null((await ApiAssert.OkAsync<HoldDto>(await guest.GetAsync($"/api/v1/public/holds/{hold.Reference}"))).AccessToken);
        await ApiAssert.ErrorAsync(await guest.PostAsync($"/api/v1/public/holds/{hold.Reference}/payments", null), HttpStatusCode.Conflict, "GUEST_DETAILS_REQUIRED");
        await ApiAssert.ErrorAsync(await guest.PutAsJsonAsync($"/api/v1/public/holds/{hold.Reference}/guest", Details() with { AcceptTerms = false }),
            HttpStatusCode.BadRequest);
        await ApiAssert.ErrorAsync(await guest.PutAsJsonAsync($"/api/v1/public/holds/{hold.Reference}/guest", Details() with { Email = "not-an-email" }),
            HttpStatusCode.BadRequest);
        var detailed = await ApiAssert.OkAsync<HoldDto>(await guest.PutAsJsonAsync($"/api/v1/public/holds/{hold.Reference}/guest", Details()));
        Assert.True(detailed.HasGuestDetails);

        var payment = await ApiAssert.OkAsync<PaymentInitDto>(await guest.PostAsync($"/api/v1/public/holds/{hold.Reference}/payments", null));
        Assert.Equal(105_000m, payment.Amount);
        Assert.StartsWith("https://pay.example/", payment.AuthorizationUrl);
        var again = await ApiAssert.OkAsync<PaymentInitDto>(await guest.PostAsync($"/api/v1/public/holds/{hold.Reference}/payments", null));
        Assert.Equal(payment.MerchantReference, again.MerchantReference); // double click does not start a second charge

        var booking = await VerifyAsync(guest, payment.MerchantReference);
        Assert.Equal("Converted", booking.Status);
        Assert.Equal("Succeeded", booking.PaymentStatus);
        Assert.Equal(105_000m, booking.AmountPaid);
        var reservation = Assert.Single(booking.Reservations);
        Assert.Equal("Confirmed", reservation.Status);

        // Verifying again, and the provider's webhook arriving late, change nothing.
        await VerifyAsync(guest, payment.MerchantReference);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(payment.MerchantReference)).StatusCode);
        booking = await ApiAssert.OkAsync<PublicBookingDto>(await guest.GetAsync($"/api/v1/public/bookings/{hold.Reference}"));
        Assert.Single(booking.Reservations);

        var night = await NightAsync(hotel, arrival);
        Assert.Equal(0, night.Held);
        Assert.Equal(1, night.Confirmed);

        // Staff see a normal website reservation, fully paid, with the money in the ledger.
        var staffView = (await hotel.GetAsync<PagedResult<ReservationListItemDto>>($"reservations?search={reservation.ReservationNumber}")).Items.Single();
        var full = await hotel.GetAsync<ReservationDto>($"reservations/{staffView.Id}");
        Assert.Equal("Website", full.Source);
        var folio = await hotel.GetAsync<HMS.Contracts.Billing.FolioDto>($"reservations/{full.Id}/folio");
        Assert.Equal(-105_000m, folio.Balance);
        var tb = await hotel.GetAsync<TrialBalanceDto>($"accounting/trial-balance?from={hotel.Today.AddYears(-1):yyyy-MM-dd}&to={hotel.Today.AddYears(1):yyyy-MM-dd}");
        Assert.Equal(105_000m, tb.Lines.Where(l => l.Code == "1010").Sum(l => l.Debit - l.Credit));

        var outbox = await hotel.GetAsync<List<OutboxMessageDto>>("notifications/outbox");
        Assert.Contains(outbox, m => m.EventType == "ReservationConfirmed" && m.Recipient == "webguest@guest.test");
        Assert.Contains(outbox, m => m.EventType == "PaymentConfirmed" && m.Recipient == "webguest@guest.test");
    }

    [Fact]
    public async Task Two_rooms_create_two_reservations_with_the_payment_split()
    {
        var hotel = await PublicHotelAsync();
        var (guest, _, payment) = await ReadyToPayAsync(hotel, nights: 1, quantity: 2, last: "Twoooms");
        Assert.Equal(105_000m, payment.Amount);
        var booking = await VerifyAsync(guest, payment.MerchantReference);
        Assert.Equal(2, booking.Reservations.Count);
        Assert.All(booking.Reservations, r => Assert.Equal(52_500m, r.TotalAmount));
    }

    // ---------- inventory protection ----------

    [Fact]
    public async Task Held_rooms_cannot_be_sold_twice_by_the_web_or_the_front_desk()
    {
        var hotel = await PublicHotelAsync(rooms: 1);
        var arrival = hotel.Today.AddDays(7);
        var first = await HeldAsync(Guest(), hotel, arrival: arrival);

        var search = await ApiAssert.OkAsync<PublicAvailabilityDto>(await Guest().GetAsync(
            $"/api/v1/public/branches/{hotel.Branch.Code}/availability?arrival={arrival:yyyy-MM-dd}&departure={arrival.AddDays(2):yyyy-MM-dd}&adults=2"));
        Assert.Empty(search.Offers);
        await ApiAssert.ErrorAsync(await HoldAsync(Guest(), hotel, arrival: arrival), HttpStatusCode.Conflict, "ROOM_NOT_AVAILABLE");
        await ApiAssert.ErrorAsync(await hotel.PostBookingAsync(hotel.Admin, HotelSetup.Booking(hotel.Standard.Id, arrival, 2)),
            HttpStatusCode.Conflict, "ROOM_NOT_AVAILABLE");

        // The guest gives up: the room is on sale again straight away.
        Assert.Equal(HttpStatusCode.NoContent, (await Guest(first.AccessToken).DeleteAsync($"/api/v1/public/holds/{first.Reference}")).StatusCode);
        await ApiAssert.OkAsync<ReservationDto>(await hotel.PostBookingAsync(hotel.Admin, HotelSetup.Booking(hotel.Standard.Id, arrival, 2)),
            HttpStatusCode.Created);

        // Inventory reconcile agrees with what happened.
        var reconcile = await ApiAssert.OkAsync<InventoryReconciliationDto>(await hotel.Admin.PostAsync(
            hotel.P($"inventory/reconcile?start={arrival:yyyy-MM-dd}&end={arrival.AddDays(2):yyyy-MM-dd}"), null));
        Assert.Equal(0, reconcile.RowsCorrected);
    }

    [Fact]
    public async Task Expired_holds_go_back_on_sale_and_cannot_be_continued()
    {
        var hotel = await PublicHotelAsync(rooms: 1);
        var hold = await HeldAsync(Guest(), hotel);
        factory.Clock.Advance(TimeSpan.FromMinutes(16));
        try
        {
            Assert.True((await RunJobsAsync(hotel)).Holds.Expired >= 1);
            Assert.Equal(0, (await NightAsync(hotel, hotel.Today.AddDays(7))).Held);
            var guest = Guest(hold.AccessToken);
            Assert.Equal("Expired", (await ApiAssert.OkAsync<HoldDto>(await guest.GetAsync($"/api/v1/public/holds/{hold.Reference}"))).Status);
            await ApiAssert.ErrorAsync(await guest.PutAsJsonAsync($"/api/v1/public/holds/{hold.Reference}/guest", Details()), HttpStatusCode.Conflict,
                "HOLD_EXPIRED");
        }
        finally
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(-16));
        }
    }

    // ---------- payments that go wrong ----------

    [Fact]
    public async Task Payment_after_expiry_rebooks_when_rooms_are_still_free()
    {
        var hotel = await PublicHotelAsync(rooms: 2);
        var (guest, _, payment) = await ReadyToPayAsync(hotel, last: "Latepayer");
        factory.Clock.Advance(TimeSpan.FromMinutes(40));
        try
        {
            await RunJobsAsync(hotel);
            var booking = await VerifyAsync(guest, payment.MerchantReference);
            Assert.Equal("Converted", booking.Status);
            Assert.Equal(1, (await NightAsync(hotel, hotel.Today.AddDays(7))).Confirmed);
            Assert.Equal(0, (await NightAsync(hotel, hotel.Today.AddDays(7))).Held);
        }
        finally
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(-40));
        }
    }

    [Fact]
    public async Task Payment_after_the_rooms_sold_out_goes_to_staff_review()
    {
        var hotel = await PublicHotelAsync(rooms: 1);
        var (guest, hold, payment) = await ReadyToPayAsync(hotel, last: "Unlucky");
        factory.Clock.Advance(TimeSpan.FromMinutes(40));
        try
        {
            await RunJobsAsync(hotel);
            await ApiAssert.OkAsync<ReservationDto>(await hotel.PostBookingAsync(hotel.Admin, HotelSetup.Booking(hotel.Standard.Id, hold.ArrivalDate, 2)),
                HttpStatusCode.Created);

            var booking = await VerifyAsync(guest, payment.MerchantReference);
            Assert.Equal("NeedsReview", booking.Status);
            Assert.Equal("NeedsReview", booking.PaymentStatus);
            Assert.Empty(booking.Reservations);
            Assert.Contains("contact you", booking.Message);
        }
        finally
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(-40));
        }

        var inbox = await hotel.GetAsync<List<InboxItemDto>>("notifications/inbox");
        Assert.Contains(inbox, i => i.EventType == "OnlinePaymentNeedsReview" && i.Body.Contains("Unlucky"));

        var review = Assert.Single(await hotel.GetAsync<List<OnlinePaymentDto>>("online-payments?status=NeedsReview"), p => p.HoldReference == hold.Reference);
        var cashier = await hotel.StaffAsync(Roles.Cashier);
        await ApiAssert.ErrorAsync(await cashier.PostAsJsonAsync(hotel.P($"online-payments/{review.Id}/resolve"),
            new ResolvePaymentRequest("refunded", review.ConcurrencyStamp)), HttpStatusCode.Forbidden);
        await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P($"online-payments/{review.Id}/resolve"),
            new ResolvePaymentRequest(" ", review.ConcurrencyStamp)), HttpStatusCode.BadRequest, "NOTE_REQUIRED");
        var resolved = await ApiAssert.OkAsync<OnlinePaymentDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"online-payments/{review.Id}/resolve"),
            new ResolvePaymentRequest("Refunded through Paystack dashboard, ref RF-123", review.ConcurrencyStamp)));
        Assert.Equal("Resolved", resolved.Status);
    }

    [Fact]
    public async Task Amount_mismatch_is_never_accepted_as_payment()
    {
        var hotel = await PublicHotelAsync();
        var (guest, _, payment) = await ReadyToPayAsync(hotel, last: "Tamperer");
        factory.Payments.Override = r => r == payment.MerchantReference
            ? new GatewayVerification(GatewayOutcome.Success, "success", "TX-1", 100m, "NGN", r) : null;
        try
        {
            var booking = await VerifyAsync(guest, payment.MerchantReference);
            Assert.Equal("NeedsReview", booking.Status);
            Assert.Equal(0m, booking.AmountPaid);
            Assert.Empty(booking.Reservations);
        }
        finally
        {
            factory.Payments.Override = null;
        }
        Assert.Equal(0, (await NightAsync(hotel, hotel.Today.AddDays(7))).Held);
    }

    [Fact]
    public async Task Failed_payment_keeps_the_hold_and_the_guest_can_try_again()
    {
        var hotel = await PublicHotelAsync();
        var (guest, hold, payment) = await ReadyToPayAsync(hotel, last: "Declined");
        factory.Payments.Override = r => r == payment.MerchantReference
            ? new GatewayVerification(GatewayOutcome.Failed, "failed", "TX-2", payment.Amount, "NGN", r) : null;
        try
        {
            var booking = await VerifyAsync(guest, payment.MerchantReference);
            Assert.Equal("Active", booking.Status);
            Assert.Equal("Failed", booking.PaymentStatus);
        }
        finally
        {
            factory.Payments.Override = null;
        }

        var retry = await ApiAssert.OkAsync<PaymentInitDto>(await guest.PostAsync($"/api/v1/public/holds/{hold.Reference}/payments", null));
        Assert.NotEqual(payment.MerchantReference, retry.MerchantReference);
        Assert.Equal("Converted", (await VerifyAsync(guest, retry.MerchantReference)).Status);
    }

    [Fact]
    public async Task Pending_payment_changes_nothing_until_the_provider_confirms()
    {
        var hotel = await PublicHotelAsync();
        var (guest, _, payment) = await ReadyToPayAsync(hotel, last: "Transfer");
        factory.Payments.Override = r => r == payment.MerchantReference
            ? new GatewayVerification(GatewayOutcome.Pending, "ongoing", null, null, null, r) : null;
        try
        {
            var waiting = await VerifyAsync(guest, payment.MerchantReference);
            Assert.Equal("Active", waiting.Status);
            Assert.Equal("Initialized", waiting.PaymentStatus);
        }
        finally
        {
            factory.Payments.Override = null;
        }

        // The provider's webhook later reports success; the booking is completed without the guest coming back.
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(payment.MerchantReference)).StatusCode);
        Assert.Equal("Converted", (await VerifyAsync(guest, payment.MerchantReference)).Status);
    }

    [Fact]
    public async Task Webhooks_with_a_bad_signature_are_rejected()
    {
        var hotel = await PublicHotelAsync();
        var (_, hold, payment) = await ReadyToPayAsync(hotel, last: "Forged");
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(payment.MerchantReference, "deadbeef")).StatusCode);
        var staff = await hotel.GetAsync<List<OnlinePaymentDto>>("online-payments");
        Assert.Equal("Initialized", staff.Single(p => p.HoldReference == hold.Reference).Status);
    }

    // ---------- other public rules ----------

    [Fact]
    public async Task Hidden_branches_and_bad_input_are_refused()
    {
        var hotel = await HotelSetup.CreateAsync(factory); // not enabled for the website
        var guest = Guest();
        Assert.DoesNotContain(await ApiAssert.OkAsync<List<PublicBranchDto>>(await guest.GetAsync("/api/v1/public/branches")), b => b.Code == hotel.Branch.Code);
        await ApiAssert.ErrorAsync(await guest.GetAsync(
            $"/api/v1/public/branches/{hotel.Branch.Code}/availability?arrival={hotel.Today.AddDays(1):yyyy-MM-dd}&departure={hotel.Today.AddDays(2):yyyy-MM-dd}"),
            HttpStatusCode.NotFound);

        var open = await PublicHotelAsync();
        await ApiAssert.ErrorAsync(await guest.GetAsync(
            $"/api/v1/public/branches/{open.Branch.Code}/availability?arrival={open.Today.AddDays(-1):yyyy-MM-dd}&departure={open.Today.AddDays(1):yyyy-MM-dd}"),
            HttpStatusCode.BadRequest);
        await ApiAssert.ErrorAsync(await HoldAsync(guest, open, quantity: 5), HttpStatusCode.BadRequest);
        await ApiAssert.ErrorAsync(await guest.PostAsJsonAsync($"/api/v1/public/branches/{open.Branch.Code}/holds",
            new CreateHoldRequest(open.Today.AddDays(3), open.Today.AddDays(2), 2, 0, [new HoldRoomRequest(open.Standard.Id, 1)])), HttpStatusCode.BadRequest);

        // Staff routes stay closed to anonymous callers.
        await ApiAssert.ErrorAsync(await guest.GetAsync(open.P("online-payments")), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bot_submissions_are_ignored()
    {
        var hotel = await PublicHotelAsync();
        var hold = await HeldAsync(Guest(), hotel);
        var result = await ApiAssert.OkAsync<HoldDto>(await Guest(hold.AccessToken).PutAsJsonAsync($"/api/v1/public/holds/{hold.Reference}/guest",
            Details() with { Website = "http://spam.example" }));
        Assert.False(result.HasGuestDetails);
    }

    [Fact]
    public async Task Guest_can_ask_to_cancel_and_staff_are_told()
    {
        var hotel = await PublicHotelAsync();
        var (guest, hold, payment) = await ReadyToPayAsync(hotel, last: "Changeplans");
        await VerifyAsync(guest, payment.MerchantReference);
        var booking = await ApiAssert.OkAsync<PublicBookingDto>(await guest.PostAsJsonAsync($"/api/v1/public/bookings/{hold.Reference}/cancellation-request",
            new CancellationRequest("Flight cancelled")));
        Assert.True(booking.CancellationRequested);
        var inbox = await hotel.GetAsync<List<InboxItemDto>>("notifications/inbox");
        Assert.Contains(inbox, i => i.EventType == "CancellationRequested" && i.Body.Contains("Flight cancelled"));
    }

    [Fact]
    public async Task Browser_access_is_limited_to_the_booking_site_and_responses_are_hardened()
    {
        var client = Guest();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/public/branches/ANY/holds");
        preflight.Headers.Add("Origin", HmsApiFactory.BookingSite);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-booking-token");
        var allowed = await client.SendAsync(preflight);
        Assert.True(allowed.Headers.Contains("Access-Control-Allow-Origin"), $"{(int)allowed.StatusCode} {allowed.Headers} {await allowed.Content.ReadAsStringAsync()}");
        Assert.Equal(HmsApiFactory.BookingSite, allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var other = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/branches");
        other.Headers.Add("Origin", "https://evil.example");
        var denied = await client.SendAsync(other);
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));

        // Staff routes never answer cross-origin browser calls.
        var staff = new HttpRequestMessage(HttpMethod.Get, "/api/v1/branches");
        staff.Headers.Add("Origin", HmsApiFactory.BookingSite);
        Assert.False((await client.SendAsync(staff)).Headers.Contains("Access-Control-Allow-Origin"));

        Assert.Equal("nosniff", denied.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", denied.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'none'", denied.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", denied.Headers.CacheControl?.ToString());
        Assert.False(denied.Headers.Contains("Server"));
    }

    [SqlServerOnlyFact]
    public async Task Parallel_holds_for_the_last_room_give_it_to_exactly_one_guest()
    {
        var hotel = await PublicHotelAsync(rooms: 1);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => HoldAsync(Guest(), hotel)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, (await NightAsync(hotel, hotel.Today.AddDays(7))).Held);
    }

    [SqlServerOnlyFact]
    public async Task Webhook_and_browser_confirming_at_once_book_only_once()
    {
        var hotel = await PublicHotelAsync();
        var (guest, hold, payment) = await ReadyToPayAsync(hotel, last: "Racer");
        var calls = Enumerable.Range(0, 6).Select(i => i % 2 == 0
            ? guest.PostAsync($"/api/v1/public/payments/{payment.MerchantReference}/verify", null)
            : WebhookAsync(payment.MerchantReference));
        var responses = await Task.WhenAll(calls);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var booking = await ApiAssert.OkAsync<PublicBookingDto>(await guest.GetAsync($"/api/v1/public/bookings/{hold.Reference}"));
        Assert.Single(booking.Reservations);
        Assert.Equal(1, (await NightAsync(hotel, hotel.Today.AddDays(7))).Confirmed);
    }
}

public class PublicRateLimitTests(StrictPublicRateLimitFactory factory) : IClassFixture<StrictPublicRateLimitFactory>
{
    [Fact]
    public async Task Hold_requests_are_rate_limited_per_client()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        await hotel.Admin.PutAsJsonAsync(hotel.P("public-booking"), new PublicBookingSettingsRequest(true));
        var guest = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await guest.PostAsJsonAsync($"/api/v1/public/branches/{hotel.Branch.Code}/holds", new CreateHoldRequest(hotel.Today.AddDays(5),
                hotel.Today.AddDays(6), 1, 0, [new HoldRoomRequest(hotel.Standard.Id, 1)]))).StatusCode);
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
    }
}
