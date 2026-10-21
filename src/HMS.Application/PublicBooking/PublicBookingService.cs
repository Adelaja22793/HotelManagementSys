using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using HMS.Application.Abstractions;
using HMS.Application.Billing;
using HMS.Application.Common;
using HMS.Application.Guests;
using HMS.Application.Notifications;
using HMS.Application.Rates;
using HMS.Application.Rooms;
using HMS.Application.Security;
using HMS.Contracts.Guests;
using HMS.Contracts.PublicBooking;
using HMS.Domain.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using HMS.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HMS.Application.PublicBooking;

/// <summary>
/// The public website's booking flow: search, quote, temporary hold, guest details, payment, provider verification,
/// confirmation. It uses the same inventory, pricing, folio and ledger services as the front desk.
/// Every call that touches a hold needs the hold's access token; a wrong token looks the same as an unknown booking.
/// </summary>
public interface IPublicBookingService
{
    Task<IReadOnlyList<PublicBranchDto>> ListBranchesAsync(CancellationToken ct = default);
    Task<PublicAvailabilityDto> AvailabilityAsync(string branchCode, DateOnly arrival, DateOnly departure, int adults, int children, CancellationToken ct = default);
    Task<HoldDto> CreateHoldAsync(string branchCode, CreateHoldRequest request, string? clientFingerprint, CancellationToken ct = default);
    Task<HoldDto> GetHoldAsync(string reference, string? token, CancellationToken ct = default);
    Task<HoldDto> SetGuestAsync(string reference, string? token, GuestDetailsRequest request, CancellationToken ct = default);
    Task ReleaseHoldAsync(string reference, string? token, CancellationToken ct = default);
    Task<PaymentInitDto> StartPaymentAsync(string reference, string? token, CancellationToken ct = default);

    /// <summary>Called when the guest returns from the payment page. Checks the result with the provider before confirming anything.</summary>
    Task<PublicBookingDto> VerifyPaymentAsync(string merchantReference, string? token, CancellationToken ct = default);

    /// <summary>Returns false when the signature is wrong. Valid events are confirmed by asking the provider directly.</summary>
    Task<bool> HandleWebhookAsync(byte[] body, string? signature, CancellationToken ct = default);

    Task<PublicBookingDto> GetBookingAsync(string reference, string? token, CancellationToken ct = default);
    Task<PublicBookingDto> RequestCancellationAsync(string reference, string? token, CancellationRequest request, CancellationToken ct = default);

    /// <summary>Background job: puts rooms from timed-out holds back on sale.</summary>
    Task<HoldExpiryResultDto> ExpireDueAsync(int max, CancellationToken ct = default);
}

internal sealed class PublicBookingService(
    IApplicationDbContext db,
    IClock clock,
    IPricingService pricing,
    IRoomInventoryService inventory,
    IGuestService guests,
    IFolioService folios,
    IPaymentService payments,
    INumberSequence numbers,
    INotificationQueue notifications,
    IPaymentGateway gateway,
    IOptions<PublicBookingOptions> options,
    ILogger<PublicBookingService> logger) : IPublicBookingService
{
    /// <summary>User id recorded on everything the website creates.</summary>
    public const string WebsiteUser = "public-website";
    private const int MaxShownAvailability = 5;
    private const string ReferenceAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private PublicBookingOptions Settings => options.Value;

    // ---------- search ----------

    public async Task<IReadOnlyList<PublicBranchDto>> ListBranchesAsync(CancellationToken ct = default) =>
        (await PublicBranches().OrderBy(b => b.Name).ToListAsync(ct))
            .Select(b => new PublicBranchDto(b.Code, b.Name, b.City, b.AddressLine, b.Phone, b.Currency, b.TimeZoneId)).ToList();

    public async Task<PublicAvailabilityDto> AvailabilityAsync(string branchCode, DateOnly arrival, DateOnly departure, int adults, int children,
        CancellationToken ct = default)
    {
        var branch = await FindBranchAsync(branchCode, ct);
        var dates = ValidateStay(branch, arrival, departure, adults, children);
        var types = await db.RoomTypes.AsNoTracking()
            .Where(t => t.BranchId == branch.Id && t.IsActive && t.MaxAdults >= adults && t.MaxChildren >= children)
            .OrderBy(t => t.BaseRate).ToListAsync(ct);
        var days = await inventory.GetAsync(branch, dates.Arrival, dates.Departure, ct);

        var offers = new List<PublicRoomOfferDto>();
        foreach (var type in types)
        {
            var available = days.Where(d => d.RoomTypeId == type.Id).Select(d => d.Available).DefaultIfEmpty(0).Min();
            if (available <= 0) continue;
            PricedStay priced;
            try { priced = await pricing.PriceAsync(branch, type.Id, null, dates, adults, children, 0, ct); }
            catch (ConflictException) { continue; } // no rate plan yet: do not offer it
            offers.Add(new PublicRoomOfferDto(type.Id, type.Code, type.Name, type.Description, type.MaxAdults, type.MaxChildren,
                Math.Min(available, MaxShownAvailability), priced.RatePlan.Name, priced.RatePlan.IsRefundable, priced.RatePlan.CancellationPolicy,
                priced.Quote.Currency, priced.Quote.Nights.Select(n => new PublicNightDto(n.Date, n.Rate)).ToList(),
                priced.Quote.RoomSubtotal, priced.Quote.ExclusiveTaxAmount, priced.Quote.Total, DueNow(priced.Quote)));
        }
        return new PublicAvailabilityDto(branch.Code, dates.Arrival, dates.Departure, dates.Departure.DayNumber - dates.Arrival.DayNumber,
            adults, children, offers);
    }

    // ---------- hold ----------

    public async Task<HoldDto> CreateHoldAsync(string branchCode, CreateHoldRequest request, string? clientFingerprint, CancellationToken ct = default)
    {
        var branch = await FindBranchAsync(branchCode, ct);
        var dates = ValidateStay(branch, request.ArrivalDate, request.DepartureDate, request.Adults, request.Children);
        if (request.Rooms is not { Count: > 0 } || request.Rooms.Count > 3)
            throw new ValidationAppException(nameof(request.Rooms), "Choose between one and three room types.");
        if (request.Rooms.Select(r => r.RoomTypeId).Distinct().Count() != request.Rooms.Count)
            throw new ValidationAppException(nameof(request.Rooms), "List each room type once with the number of rooms.");

        var now = clock.UtcNow;
        if (clientFingerprint is not null)
        {
            var open = await db.BookingHolds.CountAsync(h => h.ClientFingerprint == clientFingerprint && h.Status == BookingHoldStatus.Active
                && h.ExpiresAtUtc > now, ct);
            if (open >= Settings.MaxActiveHoldsPerClient)
                throw new ConflictException("TOO_MANY_HOLDS", "You already have bookings in progress. Finish or cancel one before starting another.");
        }

        // Price first (outside the transaction): the guest accepts exactly this quote.
        var priced = new List<(HoldRoomRequest Line, PricedStay Price)>();
        foreach (var line in request.Rooms)
        {
            if (line.Quantity is < 1 or > BookingHold.MaxRooms)
                throw new ValidationAppException(nameof(line.Quantity), $"Choose between 1 and {BookingHold.MaxRooms} rooms.");
            priced.Add((line, await pricing.PriceAsync(branch, line.RoomTypeId, null, dates, request.Adults, request.Children, 0, ct)));
            await inventory.EnsureRowsAsync(branch, line.RoomTypeId, dates.Arrival, dates.Departure, ct);
        }

        var token = NewToken();
        var hold = new BookingHold(branch.CompanyId, branch.Id, NewReference(), BookingHold.HashToken(token), dates, request.Adults, request.Children,
            branch.Currency, now.AddMinutes(Settings.HoldMinutes), clientFingerprint);
        foreach (var (line, price) in priced)
            hold.AddRoom(price.RoomType.Id, price.RatePlan.Id, line.Quantity, price.Quote.RoomSubtotal, price.Quote.ExclusiveTaxAmount, price.Quote.Total,
                DueNow(price.Quote), JsonSerializer.Serialize(price.Quote, Json));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var room in hold.Rooms)
            await inventory.HoldAsync(branch, room.RoomTypeId, dates.Arrival, dates.Departure, room.Quantity, ct);
        db.BookingHolds.Add(hold);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await HoldDtoAsync(hold.Id, token, ct);
    }

    public async Task<HoldDto> GetHoldAsync(string reference, string? token, CancellationToken ct = default) =>
        await HoldDtoAsync((await FindHoldAsync(reference, token, tracked: false, ct)).Id, null, ct);

    public async Task<HoldDto> SetGuestAsync(string reference, string? token, GuestDetailsRequest request, CancellationToken ct = default)
    {
        var hold = await FindHoldAsync(reference, token, tracked: true, ct);
        // Bots fill in every field; people never see this one. Pretend nothing happened.
        if (!string.IsNullOrEmpty(request.Website))
        {
            logger.LogInformation("Ignored guest details with the honeypot field filled for hold {Reference}", hold.Reference);
            return await HoldDtoAsync(hold.Id, null, ct);
        }
        if (!request.AcceptTerms) throw new ValidationAppException(nameof(request.AcceptTerms), "Please accept the booking terms and cancellation policy.");
        if (!IsEmail(request.Email)) throw new ValidationAppException(nameof(request.Email), "Enter a valid email address.");
        hold.SetGuest(request.FirstName, request.LastName, request.Email, request.Phone, request.Country, request.SpecialRequests, clock.UtcNow);
        db.Touch(hold);
        await SaveConcurrentAsync(ct);
        return await HoldDtoAsync(hold.Id, null, ct);
    }

    public async Task ReleaseHoldAsync(string reference, string? token, CancellationToken ct = default)
    {
        var hold = await FindHoldAsync(reference, token, tracked: true, ct);
        if (await db.BookingPaymentAttempts.AnyAsync(p => p.HoldId == hold.Id && p.Status == OnlinePaymentStatus.Initialized, ct))
            throw new ConflictException("PAYMENT_IN_PROGRESS", "A payment has been started for this booking. Wait for it to finish.");
        hold.Release();
        db.Touch(hold);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var room in hold.Rooms)
            await inventory.ReleaseHoldAsync(hold.BranchId, room.RoomTypeId, hold.ArrivalDate, hold.DepartureDate, room.Quantity, ct);
        await SaveConcurrentAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ---------- payment ----------

    public async Task<PaymentInitDto> StartPaymentAsync(string reference, string? token, CancellationToken ct = default)
    {
        var hold = await FindHoldAsync(reference, token, tracked: true, ct);
        var now = clock.UtcNow;
        hold.EnsureLive(now);
        if (!hold.HasGuestDetails) throw new ConflictException("GUEST_DETAILS_REQUIRED", "Enter your details before paying.");

        // Reuse an attempt that is still open so double-clicking "Pay" does not create two charges.
        var open = await db.BookingPaymentAttempts.Where(p => p.HoldId == hold.Id && p.Status == OnlinePaymentStatus.Initialized)
            .OrderByDescending(p => p.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (open is { AuthorizationUrl: not null })
            return new PaymentInitDto(open.MerchantReference, open.Provider, open.AuthorizationUrl, open.Amount, open.Currency, hold.ExpiresAtUtc);

        var amount = hold.DepositAmount;
        var attempt = new BookingPaymentAttempt(hold.CompanyId, hold.BranchId, hold.Id, gateway.Name, $"HMS-{hold.Reference}-{NewReference()[..6]}", amount,
            hold.Currency);
        hold.ExtendForPayment(now, TimeSpan.FromMinutes(Settings.HoldMinutes));
        db.Touch(hold);
        db.BookingPaymentAttempts.Add(attempt);
        await SaveConcurrentAsync(ct);

        var callback = $"{Settings.SiteUrl.TrimEnd('/')}/booking/{hold.Reference}/payment-return?ref={Uri.EscapeDataString(attempt.MerchantReference)}";
        var init = await gateway.InitializeAsync(new GatewayInitRequest(attempt.MerchantReference, amount, hold.Currency, hold.GuestEmail!, callback,
            new Dictionary<string, string> { ["hold"] = hold.Reference }), ct);
        if (!init.Success || init.AuthorizationUrl is null)
        {
            attempt.MarkFailed(init.Error ?? "The payment provider did not accept the request.");
            db.Touch(attempt);
            await db.SaveChangesAsync(ct);
            throw new ConflictException("PAYMENT_UNAVAILABLE", "Online payment is not available right now. Please try again in a few minutes.");
        }
        attempt.SetAuthorizationUrl(init.AuthorizationUrl);
        db.Touch(attempt);
        await db.SaveChangesAsync(ct);
        return new PaymentInitDto(attempt.MerchantReference, attempt.Provider, init.AuthorizationUrl, amount, hold.Currency, hold.ExpiresAtUtc);
    }

    public async Task<PublicBookingDto> VerifyPaymentAsync(string merchantReference, string? token, CancellationToken ct = default)
    {
        var attempt = await db.BookingPaymentAttempts.AsNoTracking().SingleOrDefaultAsync(p => p.MerchantReference == merchantReference, ct)
            ?? throw new NotFoundException("Payment", merchantReference);
        var hold = await db.BookingHolds.AsNoTracking().SingleAsync(h => h.Id == attempt.HoldId, ct);
        if (!hold.TokenMatches(token)) throw new NotFoundException("Payment", merchantReference);
        await ConfirmAsync(attempt.Id, ct);
        return await BookingDtoAsync(hold.Id, ct);
    }

    public async Task<bool> HandleWebhookAsync(byte[] body, string? signature, CancellationToken ct = default)
    {
        if (!gateway.IsValidWebhook(body, signature)) return false;
        var evt = gateway.ParseWebhook(body);
        if (evt?.Reference is null) return true;
        var attempt = await db.BookingPaymentAttempts.AsNoTracking().SingleOrDefaultAsync(p => p.MerchantReference == evt.Reference, ct);
        if (attempt is null)
        {
            logger.LogWarning("Payment webhook {Event} for unknown reference {Reference}", evt.EventType, evt.Reference);
            return true;
        }
        await ConfirmAsync(attempt.Id, ct);
        return true;
    }

    /// <summary>
    /// The single path that turns a payment into reservations. Safe to call many times and from the webhook and the
    /// guest's browser at once: the hold and attempt concurrency stamps let only one caller convert.
    /// </summary>
    private async Task ConfirmAsync(Guid attemptId, CancellationToken ct)
    {
        var snapshot = await db.BookingPaymentAttempts.AsNoTracking().SingleAsync(p => p.Id == attemptId, ct);
        if (snapshot.IsFinal) return;

        var result = await gateway.VerifyAsync(snapshot.MerchantReference, ct);
        if (result.Outcome is GatewayOutcome.Pending or GatewayOutcome.Unknown) return; // try again later; nothing changes

        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var payment = await db.BookingPaymentAttempts.SingleAsync(p => p.Id == attemptId, ct);
                if (payment.IsFinal) return;
                var hold = await db.BookingHolds.Include(h => h.Rooms).ThenInclude(r => r.RoomType).SingleAsync(h => h.Id == payment.HoldId, ct);
                var branch = await db.Branches.SingleAsync(b => b.Id == hold.BranchId, ct);
                var scope = new BranchScope(branch, WebsiteUser, clock.UtcNow);
                payment.RecordProviderResult(result.ProviderStatus, result.TransactionId, result.Amount, result.Currency, scope.UtcNow);
                db.Touch(payment);
                db.Touch(hold);

                if (result.Outcome == GatewayOutcome.Failed)
                {
                    payment.MarkFailed($"The payment was not completed ({result.ProviderStatus}).");
                }
                else if (!string.Equals(result.Reference, payment.MerchantReference, StringComparison.Ordinal) || !payment.Matches(result.Amount, result.Currency))
                {
                    payment.MarkNeedsReview($"Provider reported {result.Currency} {result.Amount} for {result.Reference}; expected {payment.Currency} {payment.Amount}.");
                    await FlagForReviewAsync(scope, hold, payment, "the amount or reference did not match", ct);
                }
                else
                {
                    await CompleteAsync(scope, hold, payment, ct);
                }

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return;
            }
            catch (Exception ex) when (attempt < 3 && ex is DbUpdateException or ConflictException)
            {
                // The webhook and the guest's browser raced. Roll back and look again: the winner's result stands.
                await tx.RollbackAsync(ct);
            }
        }
    }

    private async Task CompleteAsync(BranchScope scope, BookingHold hold, BookingPaymentAttempt payment, CancellationToken ct)
    {
        if (hold.Status == BookingHoldStatus.Active)
        {
            foreach (var room in hold.Rooms)
                await inventory.ConvertHoldAsync(hold.BranchId, room.RoomTypeId, hold.ArrivalDate, hold.DepartureDate, room.Quantity, ct);
        }
        else
        {
            // The hold ran out before the money arrived: take the rooms again if they are still free.
            try
            {
                foreach (var room in hold.Rooms)
                {
                    await inventory.EnsureRowsAsync(scope.Branch, room.RoomTypeId, hold.ArrivalDate, hold.DepartureDate, ct);
                    await inventory.AllocateAsync(scope.Branch, room.RoomTypeId, hold.ArrivalDate, hold.DepartureDate, room.Quantity, false, ct);
                }
            }
            catch (ConflictException)
            {
                payment.MarkNeedsReview("Paid after the hold expired and the rooms had been sold.");
                await FlagForReviewAsync(scope, hold, payment, "the rooms sold out before the payment arrived", ct);
                return;
            }
        }

        var guest = await guests.FindOrAddAsync(scope, new SaveGuestRequest(hold.GuestFirstName!, hold.GuestLastName!, hold.GuestEmail, hold.GuestPhone,
            hold.GuestCountry, null, null, null, null), ct);
        var channel = await WebsiteChannelAsync(scope.CompanyId, ct);
        var created = new List<(Reservation Reservation, Folio Folio, decimal Due)>();
        var dates = new StayDates(hold.ArrivalDate, hold.DepartureDate);
        foreach (var room in hold.Rooms)
        {
            var quote = JsonSerializer.Deserialize<PriceQuote>(room.QuoteJson, Json)!;
            var plan = await db.RatePlans.AsNoTracking().SingleAsync(p => p.Id == room.RatePlanId, ct);
            for (var i = 0; i < room.Quantity; i++)
            {
                var number = await numbers.NextAsync(scope.Branch, SequenceNames.Reservation, ct);
                var reservation = new Reservation(scope.CompanyId, scope.BranchId, number, guest.Id, room.RoomTypeId, room.RatePlanId, dates,
                    hold.Adults, hold.Children, ReservationSource.Website, $"web:{hold.Reference}:{room.Id:N}:{i}")
                { Notes = hold.SpecialRequests };
                reservation.ApplyPrice(quote, plan);
                db.Reservations.Add(reservation);
                db.ChannelReservationMappings.Add(new ChannelReservationMapping
                {
                    CompanyId = scope.CompanyId, BranchId = scope.BranchId, ChannelId = channel.Id, ExternalReference = hold.Reference, ReservationId = reservation.Id,
                });
                var folio = await folios.CreateForReservationAsync(scope, reservation, ct);
                created.Add((reservation, folio, room.DepositRequired));
                await notifications.EnqueueForGuestAsync(scope, NotificationEvents.ReservationConfirmed, "Reservation", reservation.Id.ToString(), guest,
                    new Dictionary<string, string>
                    {
                        ["GuestName"] = guest.FullName,
                        ["ReservationNumber"] = reservation.ReservationNumber,
                        ["ArrivalDate"] = reservation.ArrivalDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
                        ["DepartureDate"] = reservation.DepartureDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
                        ["Nights"] = dates.Nights.ToString(CultureInfo.InvariantCulture),
                        ["Currency"] = reservation.Currency,
                        ["Total"] = reservation.TotalAmount.ToString("N2", CultureInfo.InvariantCulture),
                        ["CancellationPolicy"] = reservation.CancellationPolicy,
                    }, ct);
            }
        }
        // Folios and reservations must exist before money is posted against them.
        await db.SaveChangesAsync(ct);

        // Split the payment across the rooms so each folio shows what was paid for it; any rounding goes on the last one.
        var remaining = payment.Amount;
        for (var i = 0; i < created.Count; i++)
        {
            var share = i == created.Count - 1 ? remaining : Math.Min(remaining, created[i].Due);
            remaining -= share;
            if (share > 0)
                await payments.RecordOnlineAsync(scope, created[i].Folio, share, payment.ProviderTransactionId ?? payment.MerchantReference,
                    $"{payment.MerchantReference}:{i}", ct);
        }

        payment.MarkSucceeded();
        hold.MarkConverted(scope.UtcNow);
    }

    private async Task FlagForReviewAsync(BranchScope scope, BookingHold hold, BookingPaymentAttempt payment, string why, CancellationToken ct)
    {
        if (hold.Status == BookingHoldStatus.Active)
            foreach (var room in hold.Rooms)
                await inventory.ReleaseHoldAsync(hold.BranchId, room.RoomTypeId, hold.ArrivalDate, hold.DepartureDate, room.Quantity, ct);
        hold.MarkNeedsReview(why);
        await notifications.EnqueueInAppAsync(scope, NotificationEvents.OnlinePaymentNeedsReview, "BookingPaymentAttempt", payment.Id.ToString(),
            "perm:" + Permissions.RefundsApprove, new Dictionary<string, string>
            {
                ["Reference"] = hold.Reference, ["MerchantReference"] = payment.MerchantReference, ["Reason"] = why,
                ["Currency"] = payment.Currency, ["Amount"] = NotificationQueue.Money(payment.Amount),
                ["GuestName"] = $"{hold.GuestFirstName} {hold.GuestLastName}".Trim(), ["GuestEmail"] = hold.GuestEmail ?? "",
            }, ct);
    }

    // ---------- after booking ----------

    public async Task<PublicBookingDto> GetBookingAsync(string reference, string? token, CancellationToken ct = default) =>
        await BookingDtoAsync((await FindHoldAsync(reference, token, tracked: false, ct)).Id, ct);

    public async Task<PublicBookingDto> RequestCancellationAsync(string reference, string? token, CancellationRequest request, CancellationToken ct = default)
    {
        var hold = await FindHoldAsync(reference, token, tracked: true, ct);
        var first = hold.CancellationRequestedAtUtc is null;
        hold.RequestCancellation(request.Reason, clock.UtcNow);
        db.Touch(hold);
        if (first)
        {
            var branch = await db.Branches.SingleAsync(b => b.Id == hold.BranchId, ct);
            await notifications.EnqueueInAppAsync(new BranchScope(branch, WebsiteUser, clock.UtcNow), NotificationEvents.CancellationRequested,
                "BookingHold", hold.Id.ToString(), "perm:" + Permissions.ReservationsManage, new Dictionary<string, string>
                {
                    ["Reference"] = hold.Reference, ["GuestName"] = $"{hold.GuestFirstName} {hold.GuestLastName}".Trim(),
                    ["ArrivalDate"] = hold.ArrivalDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), ["Reason"] = hold.CancellationReason ?? "(none given)",
                }, ct);
        }
        await SaveConcurrentAsync(ct);
        return await BookingDtoAsync(hold.Id, ct);
    }

    public async Task<HoldExpiryResultDto> ExpireDueAsync(int max, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var due = await db.BookingHolds.Where(h => h.Status == BookingHoldStatus.Active && h.ExpiresAtUtc <= now)
            .OrderBy(h => h.ExpiresAtUtc).Select(h => h.Id).Take(max).ToListAsync(ct);
        var expired = 0;
        foreach (var id in due)
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var hold = await db.BookingHolds.Include(h => h.Rooms).SingleAsync(h => h.Id == id, ct);
            if (hold.Status != BookingHoldStatus.Active || hold.ExpiresAtUtc > now) continue;
            hold.Expire();
            db.Touch(hold);
            foreach (var room in hold.Rooms)
                await inventory.ReleaseHoldAsync(hold.BranchId, room.RoomTypeId, hold.ArrivalDate, hold.DepartureDate, room.Quantity, ct);
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                expired++;
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(ct); // the guest's payment arrived at the same moment; it wins
            }
        }
        return new HoldExpiryResultDto(expired);
    }

    // ---------- helpers ----------

    private IQueryable<Branch> PublicBranches()
    {
        var q = db.Branches.AsNoTracking().Where(b => b.PublicBookingEnabled && b.IsActive && b.Company!.IsActive);
        if (!string.IsNullOrWhiteSpace(Settings.CompanyCode)) q = q.Where(b => b.Company!.Code == Settings.CompanyCode);
        return q;
    }

    private async Task<Branch> FindBranchAsync(string code, CancellationToken ct)
    {
        var normalized = (code ?? "").Trim().ToUpperInvariant();
        return await PublicBranches().OrderBy(b => b.CreatedAtUtc).FirstOrDefaultAsync(b => b.Code == normalized, ct) ?? throw new NotFoundException("Hotel", normalized);
    }

    private StayDates ValidateStay(Branch branch, DateOnly arrival, DateOnly departure, int adults, int children)
    {
        if (adults < 1 || adults > 10) throw new ValidationAppException(nameof(adults), "Choose between 1 and 10 adults.");
        if (children < 0 || children > 10) throw new ValidationAppException(nameof(children), "Choose up to 10 children.");
        var today = branch.GetBusinessDate(clock.UtcNow);
        if (arrival < today) throw new ValidationAppException(nameof(arrival), "Arrival cannot be in the past.");
        if (arrival > today.AddDays(Settings.MaxDaysAhead))
            throw new ValidationAppException(nameof(arrival), $"Bookings open {Settings.MaxDaysAhead} days ahead.");
        try { return new StayDates(arrival, departure); }
        catch (DomainException ex) { throw new ValidationAppException(nameof(departure), ex.Message); }
    }

    private decimal DueNow(PriceQuote quote) =>
        string.Equals(Settings.PaymentMode, "Deposit", StringComparison.OrdinalIgnoreCase) && quote.DepositRequired > 0 ? quote.DepositRequired : quote.Total;

    private async Task<BookingHold> FindHoldAsync(string reference, string? token, bool tracked, CancellationToken ct)
    {
        var normalized = (reference ?? "").Trim().ToUpperInvariant();
        var q = db.BookingHolds.Include(h => h.Rooms).Where(h => h.Reference == normalized);
        var hold = await (tracked ? q : q.AsNoTracking()).SingleOrDefaultAsync(ct);
        // Same answer for "no such booking" and "wrong token", so references cannot be probed.
        if (hold is null || !hold.TokenMatches(token)) throw new NotFoundException("Booking", normalized);
        return hold;
    }

    private async Task<BookingChannel> WebsiteChannelAsync(Guid companyId, CancellationToken ct)
    {
        var channel = await db.BookingChannels.SingleOrDefaultAsync(c => c.CompanyId == companyId && c.Code == BookingChannel.Website, ct);
        if (channel is not null) return channel;
        channel = new BookingChannel { CompanyId = companyId, Code = BookingChannel.Website, Name = "Hotel website" };
        db.BookingChannels.Add(channel);
        return channel;
    }

    private async Task SaveConcurrentAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "Your booking changed in another window. Reload the page.");
        }
    }

    private async Task<HoldDto> HoldDtoAsync(Guid holdId, string? token, CancellationToken ct)
    {
        var hold = await db.BookingHolds.AsNoTracking().Include(h => h.Rooms).ThenInclude(r => r.RoomType).SingleAsync(h => h.Id == holdId, ct);
        var branch = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == hold.BranchId, ct);
        var planIds = hold.Rooms.Select(r => r.RatePlanId).ToList();
        var plans = await db.RatePlans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var status = hold.Status == BookingHoldStatus.Active && hold.ExpiresAtUtc <= clock.UtcNow ? BookingHoldStatus.Expired : hold.Status;
        return new HoldDto(hold.Reference, token, branch.Code, branch.Name, hold.ArrivalDate, hold.DepartureDate, hold.Adults, hold.Children,
            status.ToString(), hold.ExpiresAtUtc, hold.Currency, hold.TotalAmount, hold.DepositAmount,
            hold.Rooms.Select(r => new HoldRoomDto(r.RoomTypeId, r.RoomType?.Name ?? "", r.Quantity, plans[r.RatePlanId].Name, plans[r.RatePlanId].IsRefundable,
                plans[r.RatePlanId].CancellationPolicy, r.TotalAmount, r.DepositRequired)).ToList(),
            hold.HasGuestDetails, hold.GuestLastName is null ? null : $"{hold.GuestFirstName} {hold.GuestLastName}", hold.GuestEmail);
    }

    private async Task<PublicBookingDto> BookingDtoAsync(Guid holdId, CancellationToken ct)
    {
        var hold = await db.BookingHolds.AsNoTracking().SingleAsync(h => h.Id == holdId, ct);
        var branch = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == hold.BranchId, ct);
        var payment = await db.BookingPaymentAttempts.AsNoTracking().Where(p => p.HoldId == holdId)
            .OrderByDescending(p => p.Status == OnlinePaymentStatus.Succeeded).ThenByDescending(p => p.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var reservationIds = await db.ChannelReservationMappings.AsNoTracking()
            .Where(m => m.BranchId == hold.BranchId && m.ExternalReference == hold.Reference).Select(m => m.ReservationId).ToListAsync(ct);
        var reservations = await db.Reservations.AsNoTracking().Include(r => r.RoomType).Where(r => reservationIds.Contains(r.Id))
            .OrderBy(r => r.ReservationNumber).ToListAsync(ct);
        var paid = payment?.Status == OnlinePaymentStatus.Succeeded ? payment.Amount : 0;
        var message = hold.Status switch
        {
            BookingHoldStatus.Converted => hold.CancellationRequestedAtUtc is null ? "Your booking is confirmed. A confirmation email is on its way."
                : "We have your cancellation request. The hotel will confirm it and any refund by email.",
            BookingHoldStatus.NeedsReview => "We received your payment but could not complete the booking automatically. The hotel will contact you shortly.",
            BookingHoldStatus.Expired => "Your hold expired before payment was completed.",
            BookingHoldStatus.Released => "This booking was cancelled before payment.",
            _ when payment?.Status == OnlinePaymentStatus.Failed => "The payment was not completed. You can try again while your rooms are held.",
            _ when payment?.Status == OnlinePaymentStatus.Initialized => "Waiting for confirmation from the payment provider.",
            _ => null,
        };
        return new PublicBookingDto(hold.Reference, hold.Status.ToString(), payment?.Status.ToString(), branch.Name, branch.Phone, hold.ArrivalDate,
            hold.DepartureDate, hold.GuestLastName is null ? null : $"{hold.GuestFirstName} {hold.GuestLastName}", hold.Currency, hold.TotalAmount, paid,
            reservations.Select(r => new PublicReservationDto(r.ReservationNumber, r.RoomType?.Name ?? "", r.Status.ToString(), r.TotalAmount,
                r.CancellationPolicy)).ToList(),
            hold.CancellationRequestedAtUtc is not null, message);
    }

    private static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 254 && System.Net.Mail.MailAddress.TryCreate(value.Trim(), out var address)
        && address.Address == value.Trim() && address.Host.Contains('.');

    private static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string NewReference() =>
        new(Enumerable.Range(0, 10).Select(_ => ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)]).ToArray());

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
