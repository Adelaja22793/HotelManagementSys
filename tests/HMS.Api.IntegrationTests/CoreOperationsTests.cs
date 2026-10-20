using System.Net;
using System.Net.Http.Json;
using HMS.Application.Security;
using HMS.Contracts.Billing;
using HMS.Contracts.Cashier;
using HMS.Contracts.FrontDesk;
using HMS.Contracts.Housekeeping;
using HMS.Contracts.Rates;
using HMS.Contracts.Reservations;
using HMS.Contracts.Rooms;

namespace HMS.Api.IntegrationTests;

/// <summary>Blueprint workflow A end to end, plus the high-priority failure cases from section 24.</summary>
public class CoreOperationsTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    [Fact]
    public async Task Reservation_to_checkout_full_workflow()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var reception = await hotel.StaffAsync(Roles.Receptionist);
        var housekeeper = await hotel.StaffAsync(Roles.Housekeeping);

        // Availability and quote
        var availability = await ApiAssert.OkAsync<List<RoomTypeAvailabilityDto>>(await reception.GetAsync(
            hotel.P($"availability?arrivalDate={hotel.Today:yyyy-MM-dd}&departureDate={hotel.Today.AddDays(3):yyyy-MM-dd}&adults=2")));
        var standard = availability.Single();
        Assert.Equal(3, standard.AvailableRooms);
        Assert.Equal(157_500m, standard.Quote!.Total); // 3 x 50,000 + 5% tax

        // Reservation (snapshot price) and folio
        var booking = await ApiAssert.OkAsync<ReservationDto>(
            await hotel.PostBookingAsync(reception, HotelSetup.Booking(hotel.Standard.Id, hotel.Today, 3)), HttpStatusCode.Created);
        Assert.Equal("Confirmed", booking.Status);
        Assert.StartsWith($"RES-{hotel.Branch.Code}-", booking.ReservationNumber);
        Assert.Equal(157_500m, booking.TotalAmount);
        Assert.NotNull(booking.FolioId);

        // Check-in requires a clean room: dirty the first room through a stayover task.
        var room = hotel.Rooms[0];
        await ApiAssert.OkAsync<HousekeepingTaskDto>(await housekeeper.PostAsJsonAsync(hotel.P("housekeeping/tasks"),
            new CreateHousekeepingTaskRequest(room.Id, "Touchup", null)), HttpStatusCode.Created);
        var dirty = await reception.PostAsJsonAsync(hotel.P("front-desk/check-in"), new CheckInRequest(booking.Id, room.Id, booking.ConcurrencyStamp));
        await ApiAssert.ErrorAsync(dirty, HttpStatusCode.Conflict, "ROOM_NOT_READY");

        // Check in to a clean room
        var stay = await ApiAssert.OkAsync<StayDto>(await reception.PostAsJsonAsync(hotel.P("front-desk/check-in"),
            new CheckInRequest(booking.Id, hotel.Rooms[1].Id, booking.ConcurrencyStamp)));
        Assert.Equal("Active", stay.Status);
        Assert.Equal(157_500m, stay.Balance);

        // Extra service charge with tax, posted twice with the same key -> once
        var chargeKey = Guid.NewGuid().ToString();
        for (var i = 0; i < 2; i++)
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, hotel.P($"folios/{stay.FolioId}/charges"))
            {
                Content = JsonContent.Create(new PostChargeRequest(hotel.Laundry.Id, null, 1, null)),
            };
            msg.Headers.Add("Idempotency-Key", chargeKey);
            Assert.Equal(HttpStatusCode.OK, (await reception.SendAsync(msg)).StatusCode);
        }
        var folio = await hotel.FolioAsync(stay.FolioId!.Value);
        Assert.Equal(157_500m + 2_000m + 100m, folio.Balance);
        Assert.Equal(3, folio.Charges.Count(c => c.Type == "Room"));

        // Check-out blocked while money is owed
        var blocked = await reception.PostAsJsonAsync(hotel.P("front-desk/check-out"), new CheckOutRequest(stay.Id));
        await ApiAssert.ErrorAsync(blocked, HttpStatusCode.Conflict, "BALANCE_DUE");

        // Cash needs a shift; then pay in full (retry with same key is safe)
        var noShift = await hotel.PayAsync(reception, folio.Id, folio.Balance);
        await ApiAssert.ErrorAsync(noShift, HttpStatusCode.Conflict, "SHIFT_REQUIRED");
        await hotel.OpenShiftAsync(reception, 5_000);
        var payKey = Guid.NewGuid().ToString();
        var payment = await ApiAssert.OkAsync<PaymentDto>(await hotel.PayAsync(reception, folio.Id, folio.Balance, key: payKey));
        var retry = await ApiAssert.OkAsync<PaymentDto>(await hotel.PayAsync(reception, folio.Id, folio.Balance, key: payKey));
        Assert.Equal(payment.Id, retry.Id);
        Assert.Equal("Confirmed", payment.Status);
        Assert.StartsWith($"RCT-{hotel.Branch.Code}-", payment.ReceiptNumber);
        Assert.Equal(0m, (await hotel.FolioAsync(folio.Id)).Balance);

        factory.Clock.AdvanceDays(3); // departure day
        try
        {
            // Check-out: room dirty, invoice issued, single housekeeping task
            var result = await ApiAssert.OkAsync<CheckOutResultDto>(await reception.PostAsJsonAsync(hotel.P("front-desk/check-out"), new CheckOutRequest(stay.Id)));
            Assert.Equal("Closed", result.Stay.Status);
            Assert.StartsWith($"INV-{hotel.Branch.Code}-", result.InvoiceNumber);
            var again = await reception.PostAsJsonAsync(hotel.P("front-desk/check-out"), new CheckOutRequest(stay.Id));
            await ApiAssert.ErrorAsync(again, HttpStatusCode.Conflict, "STAY_CLOSED");
            Assert.Equal("Closed", (await hotel.FolioAsync(folio.Id)).Status);

            var rooms = await hotel.GetAsync<List<RoomDto>>("rooms");
            Assert.Equal("VacantDirty", rooms.Single(r => r.Id == hotel.Rooms[1].Id).Status);

            // Housekeeping: clean -> awaiting inspection -> only an inspector makes it VacantClean
            var tasks = await ApiAssert.OkAsync<List<HousekeepingTaskDto>>(await housekeeper.GetAsync(hotel.P("housekeeping/tasks")));
            var task = tasks.Single(t => t.Id == result.HousekeepingTaskId);
            task = await ApiAssert.OkAsync<HousekeepingTaskDto>(await housekeeper.PostAsJsonAsync(hotel.P($"housekeeping/tasks/{task.Id}/start"), new HousekeepingActionRequest(task.ConcurrencyStamp)));
            task = await ApiAssert.OkAsync<HousekeepingTaskDto>(await housekeeper.PostAsJsonAsync(hotel.P($"housekeeping/tasks/{task.Id}/complete"), new HousekeepingActionRequest(task.ConcurrencyStamp)));
            Assert.Equal("AwaitingInspection", task.Status);
            Assert.Equal("VacantDirty", task.RoomStatus);
            await ApiAssert.ErrorAsync(await housekeeper.PostAsJsonAsync(hotel.P($"housekeeping/tasks/{task.Id}/inspect"),
                new InspectionRequest(true, null, task.ConcurrencyStamp)), HttpStatusCode.Forbidden);

            task = await ApiAssert.OkAsync<HousekeepingTaskDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"housekeeping/tasks/{task.Id}/inspect"),
                new InspectionRequest(true, null, task.ConcurrencyStamp)));
            Assert.Equal("Completed", task.Status);
            Assert.Equal("VacantClean", task.RoomStatus);
        }
        finally
        {
            factory.Clock.AdvanceDays(-3);
        }
    }

    [Fact]
    public async Task Last_room_cannot_be_sold_twice()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        await hotel.BookAsync(nights: 2, lastName: "First");

        var second = await hotel.PostBookingAsync(hotel.Admin, HotelSetup.Booking(hotel.Standard.Id, hotel.Today.AddDays(1), 2, "Second"));
        await ApiAssert.ErrorAsync(second, HttpStatusCode.Conflict, "ROOM_NOT_AVAILABLE");

        // The night after the first stay is still free
        await hotel.BookAsync(nights: 1, arrival: hotel.Today.AddDays(2), lastName: "Third");

        var inventory = await hotel.GetAsync<List<InventoryDayDto>>($"inventory?start={hotel.Today:yyyy-MM-dd}&end={hotel.Today.AddDays(3):yyyy-MM-dd}");
        Assert.All(inventory, d => Assert.Equal(1, d.Confirmed));
        Assert.All(inventory, d => Assert.Equal(0, d.Available));
    }

    [Fact]
    public async Task Same_idempotency_key_books_once()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 2);
        var key = Guid.NewGuid().ToString();
        var request = HotelSetup.Booking(hotel.Standard.Id, hotel.Today, 2);

        var first = await ApiAssert.OkAsync<ReservationDto>(await hotel.PostBookingAsync(hotel.Admin, request, key), HttpStatusCode.Created);
        var second = await ApiAssert.OkAsync<ReservationDto>(await hotel.PostBookingAsync(hotel.Admin, request, key), HttpStatusCode.Created);

        Assert.Equal(first.Id, second.Id);
        var inventory = await hotel.GetAsync<List<InventoryDayDto>>($"inventory?start={hotel.Today:yyyy-MM-dd}&end={hotel.Today.AddDays(2):yyyy-MM-dd}");
        Assert.All(inventory, d => Assert.Equal(1, d.Confirmed));
    }

    [Fact]
    public async Task Cancel_releases_inventory_and_overbooking_needs_permission()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var booking = await hotel.BookAsync(nights: 2);

        var reception = await hotel.StaffAsync(Roles.Receptionist);
        var overbook = HotelSetup.Booking(hotel.Standard.Id, hotel.Today, 2, "Over") with { AllowOverbooking = true };
        await ApiAssert.ErrorAsync(await hotel.PostBookingAsync(reception, overbook), HttpStatusCode.Forbidden);

        var managerOverbook = await ApiAssert.OkAsync<ReservationDto>(await hotel.PostBookingAsync(hotel.Admin, overbook), HttpStatusCode.Created);
        Assert.True(managerOverbook.IsOverbooked);

        await ApiAssert.OkAsync<ReservationDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"reservations/{booking.Id}/cancel"),
            new CancelReservationRequest("Guest changed plans", booking.ConcurrencyStamp)));
        await ApiAssert.OkAsync<ReservationDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"reservations/{managerOverbook.Id}/cancel"),
            new CancelReservationRequest("Overbooking resolved", managerOverbook.ConcurrencyStamp)));

        var inventory = await hotel.GetAsync<List<InventoryDayDto>>($"inventory?start={hotel.Today:yyyy-MM-dd}&end={hotel.Today.AddDays(2):yyyy-MM-dd}");
        Assert.All(inventory, d => Assert.Equal(1, d.Available));

        var reconcile = await ApiAssert.OkAsync<InventoryReconciliationDto>(await hotel.Admin.PostAsync(
            hotel.P($"inventory/reconcile?start={hotel.Today:yyyy-MM-dd}&end={hotel.Today.AddDays(30):yyyy-MM-dd}"), null));
        Assert.Equal(0, reconcile.RowsCorrected);
    }

    [Fact]
    public async Task Discount_requires_manager_permission()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var reception = await hotel.StaffAsync(Roles.Receptionist);
        var discounted = HotelSetup.Booking(hotel.Standard.Id, hotel.Today, 1, discount: 10);

        await ApiAssert.ErrorAsync(await hotel.PostBookingAsync(reception, discounted), HttpStatusCode.Forbidden);
        var ok = await ApiAssert.OkAsync<ReservationDto>(await hotel.PostBookingAsync(hotel.Admin, discounted), HttpStatusCode.Created);
        Assert.Equal(5_000m, ok.DiscountAmount);
        Assert.Equal(47_250m, ok.TotalAmount);
    }

    [Fact]
    public async Task Maintenance_block_removes_room_from_sale_and_protects_assigned_rooms()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 2);
        var booking = await hotel.BookAsync(nights: 2);
        await ApiAssert.OkAsync<ReservationDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"reservations/{booking.Id}/assign-room"),
            new AssignRoomRequest(hotel.Rooms[0].Id, booking.ConcurrencyStamp)));

        var clash = await hotel.Admin.PostAsJsonAsync(hotel.P("room-blocks"),
            new CreateRoomBlockRequest(hotel.Rooms[0].Id, hotel.Today, hotel.Today.AddDays(1), "AC repair"));
        await ApiAssert.ErrorAsync(clash, HttpStatusCode.Conflict, "ROOM_ASSIGNED");

        var block = await ApiAssert.OkAsync<RoomBlockDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("room-blocks"),
            new CreateRoomBlockRequest(hotel.Rooms[1].Id, hotel.Today, hotel.Today.AddDays(1), "AC repair")), HttpStatusCode.Created);

        var full = await hotel.PostBookingAsync(hotel.Admin, HotelSetup.Booking(hotel.Standard.Id, hotel.Today, 1, "Blocked"));
        await ApiAssert.ErrorAsync(full, HttpStatusCode.Conflict, "ROOM_NOT_AVAILABLE");

        await ApiAssert.OkAsync<RoomBlockDto>(await hotel.Admin.PostAsync(hotel.P($"room-blocks/{block.Id}/release"), null));
        await ApiAssert.OkAsync<ReservationDto>(await hotel.PostBookingAsync(hotel.Admin, HotelSetup.Booking(hotel.Standard.Id, hotel.Today, 1, "Freed")), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Same_room_cannot_be_assigned_to_overlapping_stays()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 2);
        var a = await hotel.BookAsync(nights: 2, lastName: "Alpha");
        var b = await hotel.BookAsync(nights: 2, arrival: hotel.Today.AddDays(1), lastName: "Bravo");

        await ApiAssert.OkAsync<ReservationDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"reservations/{a.Id}/assign-room"),
            new AssignRoomRequest(hotel.Rooms[0].Id, a.ConcurrencyStamp)));
        var clash = await hotel.Admin.PostAsJsonAsync(hotel.P($"reservations/{b.Id}/assign-room"), new AssignRoomRequest(hotel.Rooms[0].Id, b.ConcurrencyStamp));
        await ApiAssert.ErrorAsync(clash, HttpStatusCode.Conflict, "ROOM_ALREADY_ASSIGNED");
    }

    [Fact]
    public async Task Early_departure_reverses_unused_nights_and_releases_inventory()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var booking = await hotel.BookAsync(nights: 3);
        var stay = await ApiAssert.OkAsync<StayDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("front-desk/check-in"),
            new CheckInRequest(booking.Id, hotel.Rooms[0].Id, booking.ConcurrencyStamp)));

        factory.Clock.AdvanceDays(1); // guest leaves after one night
        try
        {
            var folio = await hotel.FolioAsync(stay.FolioId!.Value);
            await hotel.OpenShiftAsync(hotel.Admin);
            await ApiAssert.OkAsync<PaymentDto>(await hotel.PayAsync(hotel.Admin, folio.Id, 52_500m));

            var result = await ApiAssert.OkAsync<CheckOutResultDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("front-desk/check-out"), new CheckOutRequest(stay.Id)));
            folio = await hotel.FolioAsync(folio.Id);
            Assert.Equal(0m, folio.Balance);
            Assert.Equal(52_500m, folio.TotalCharges);
            Assert.Equal(4, folio.Charges.Count(c => c.ReversesChargeId is not null)); // 2 nights + their tax

            var tomorrow = hotel.Today.AddDays(1);
            var inventory = await hotel.GetAsync<List<InventoryDayDto>>($"inventory?start={tomorrow:yyyy-MM-dd}&end={tomorrow.AddDays(2):yyyy-MM-dd}");
            Assert.All(inventory, d => Assert.Equal(0, d.Confirmed));
            Assert.Equal(tomorrow, result.Stay.DepartureDate);
        }
        finally
        {
            factory.Clock.AdvanceDays(-1);
        }
    }

    [Fact]
    public async Task Manager_can_check_out_with_balance_only_with_a_reason()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var booking = await hotel.BookAsync(nights: 1);
        var stay = await ApiAssert.OkAsync<StayDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("front-desk/check-in"),
            new CheckInRequest(booking.Id, hotel.Rooms[0].Id, booking.ConcurrencyStamp)));

        var reception = await hotel.StaffAsync(Roles.Receptionist);
        await ApiAssert.ErrorAsync(await reception.PostAsJsonAsync(hotel.P("front-desk/check-out"), new CheckOutRequest(stay.Id, true, "company pays")),
            HttpStatusCode.Forbidden);
        await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P("front-desk/check-out"), new CheckOutRequest(stay.Id, true, null)),
            HttpStatusCode.BadRequest);

        await ApiAssert.OkAsync<CheckOutResultDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("front-desk/check-out"),
            new CheckOutRequest(stay.Id, true, "Company account, invoice to follow")));
        var folio = await hotel.FolioAsync(stay.FolioId!.Value);
        Assert.Equal("Open", folio.Status); // stays open for collection
    }

    [Fact]
    public async Task Refund_requires_independent_approval_and_cash_movement()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var booking = await hotel.BookAsync(nights: 1);
        var cashier = await hotel.StaffAsync(Roles.Cashier);
        await hotel.OpenShiftAsync(cashier, 10_000);
        await ApiAssert.OkAsync<PaymentDto>(await hotel.PayAsync(cashier, booking.FolioId!.Value, 60_000)); // deposit, overpaid

        var tooMuch = await cashier.PostAsJsonAsync(hotel.P($"folios/{booking.FolioId}/refunds"), new RequestRefundRequest(null, 70_000, "Cash", "Overpaid"));
        await ApiAssert.ErrorAsync(tooMuch, HttpStatusCode.Conflict, "REFUND_EXCEEDS_PAYMENTS");

        var refund = await ApiAssert.OkAsync<RefundDto>(await cashier.PostAsJsonAsync(hotel.P($"folios/{booking.FolioId}/refunds"),
            new RequestRefundRequest(null, 7_500, "Cash", "Overpaid deposit")), HttpStatusCode.Created);
        await ApiAssert.ErrorAsync(await cashier.PostAsJsonAsync(hotel.P($"refunds/{refund.Id}/decision"),
            new RefundDecisionRequest(true, null, refund.ConcurrencyStamp)), HttpStatusCode.Forbidden);
        await ApiAssert.ErrorAsync(await cashier.PostAsJsonAsync(hotel.P($"refunds/{refund.Id}/execute"),
            new ExecuteRefundRequest(null, refund.ConcurrencyStamp)), HttpStatusCode.Conflict, "REFUND_STATE");

        refund = await ApiAssert.OkAsync<RefundDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"refunds/{refund.Id}/decision"),
            new RefundDecisionRequest(true, null, refund.ConcurrencyStamp)));
        refund = await ApiAssert.OkAsync<RefundDto>(await cashier.PostAsJsonAsync(hotel.P($"refunds/{refund.Id}/execute"),
            new ExecuteRefundRequest(null, refund.ConcurrencyStamp)));
        Assert.Equal("Executed", refund.Status);

        var folio = await hotel.FolioAsync(booking.FolioId!.Value);
        Assert.Equal(-52_500m, folio.Balance); // no charges yet: 60,000 deposit - 7,500 refund leaves a credit
        var shift = await ApiAssert.OkAsync<CashierShiftDto>(await cashier.GetAsync(hotel.P("cashier/shifts/current")));
        Assert.Equal(10_000m + 60_000m - 7_500m, shift.RunningCash);
    }

    [Fact]
    public async Task Cashier_close_detects_variance_and_needs_another_person_to_approve()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var booking = await hotel.BookAsync(nights: 1);
        var cashier = await hotel.StaffAsync(Roles.Cashier);
        var shift = await hotel.OpenShiftAsync(cashier, 5_000);
        await ApiAssert.OkAsync<PaymentDto>(await hotel.PayAsync(cashier, booking.FolioId!.Value, 20_000));
        await ApiAssert.OkAsync<PaymentDto>(await hotel.PayAsync(cashier, booking.FolioId!.Value, 32_500, "PosCard", reference: "POS-7781"));

        shift = await ApiAssert.OkAsync<CashierShiftDto>(await cashier.GetAsync(hotel.P("cashier/shifts/current")));
        var close = await cashier.PostAsJsonAsync(hotel.P($"cashier/shifts/{shift.Id}/close"),
            new CloseShiftRequest(24_000, 32_500, 0, "Short by 1,000 - change error", shift.ConcurrencyStamp));
        shift = await ApiAssert.OkAsync<CashierShiftDto>(close);
        Assert.Equal("Closing", shift.Status);
        Assert.Equal(25_000m, shift.ExpectedCash);
        Assert.Equal(-1_000m, shift.CashVariance);

        // Shift is locked: no more cash on it
        await ApiAssert.ErrorAsync(await hotel.PayAsync(cashier, booking.FolioId!.Value, 100), HttpStatusCode.Conflict, "SHIFT_NOT_OPEN");

        shift = await ApiAssert.OkAsync<CashierShiftDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"cashier/shifts/{shift.Id}/approve-variance"),
            new ApproveVarianceRequest(shift.ConcurrencyStamp)));
        Assert.Equal("Closed", shift.Status);
    }

    [Fact]
    public async Task Bank_transfer_is_pending_until_verified()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var booking = await hotel.BookAsync(nights: 1);
        var payment = await ApiAssert.OkAsync<PaymentDto>(await hotel.PayAsync(hotel.Admin, booking.FolioId!.Value, 52_500, "BankTransfer", reference: "TRF-001"));
        Assert.Equal("Pending", payment.Status);
        Assert.Null(payment.ReceiptNumber);
        Assert.Equal(52_500m, (await hotel.FolioAsync(booking.FolioId!.Value)).PendingPayments);

        payment = await ApiAssert.OkAsync<PaymentDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"payments/{payment.Id}/verify"), new VerifyPaymentRequest(true, null)));
        Assert.Equal("Confirmed", payment.Status);
        Assert.NotNull(payment.ReceiptNumber);
    }

    [Fact]
    public async Task Records_of_another_branch_are_not_reachable_through_my_branch()
    {
        var mine = await HotelSetup.CreateAsync(factory);
        var other = await HotelSetup.CreateAsync(factory);
        var otherBooking = await other.BookAsync(nights: 1);

        // Admin can access both branches, but a record must belong to the branch in the route.
        await ApiAssert.ErrorAsync(await mine.Admin.GetAsync(mine.P($"folios/{otherBooking.FolioId}")), HttpStatusCode.NotFound);
        await ApiAssert.ErrorAsync(await mine.Admin.GetAsync(mine.P($"reservations/{otherBooking.Id}")), HttpStatusCode.NotFound);
        await ApiAssert.ErrorAsync(await mine.PayAsync(mine.Admin, otherBooking.FolioId!.Value, 1_000, "BankTransfer", reference: "x"), HttpStatusCode.NotFound);

        var reception = await mine.StaffAsync(Roles.Receptionist);
        await ApiAssert.ErrorAsync(await reception.GetAsync(other.P($"reservations/{otherBooking.Id}")), HttpStatusCode.Forbidden, "BRANCH_ACCESS_DENIED");
    }

    [Fact]
    public async Task Seasonal_rate_overrides_base_rate()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        await ApiAssert.OkAsync<RoomRateDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("room-rates"),
            new CreateRoomRateRequest(hotel.Bar.Id, hotel.Standard.Id, hotel.Today.AddDays(1), hotel.Today.AddDays(1), 80_000)), HttpStatusCode.Created);

        var quote = await ApiAssert.OkAsync<QuoteDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("quotes"),
            new QuoteRequest(hotel.Standard.Id, null, hotel.Today, hotel.Today.AddDays(3), 2, 0)));
        Assert.Equal([50_000m, 80_000m, 50_000m], quote.NightlyRates.Select(n => n.Rate));
        Assert.Equal(189_000m, quote.Total);
    }
}

/// <summary>Real concurrency against SQL Server: many clerks race for the last room.</summary>
public class ConcurrencyTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    [SqlServerOnlyFact]
    public async Task Concurrent_bookings_for_the_last_room_allocate_exactly_once()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var attempts = Enumerable.Range(0, 12)
            .Select(i => hotel.PostBookingAsync(hotel.Admin, HotelSetup.Booking(hotel.Standard.Id, hotel.Today, 3, $"Guest{i}")))
            .ToList();
        var responses = await Task.WhenAll(attempts);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        var inventory = await hotel.GetAsync<List<InventoryDayDto>>($"inventory?start={hotel.Today:yyyy-MM-dd}&end={hotel.Today.AddDays(3):yyyy-MM-dd}");
        Assert.All(inventory, d => Assert.Equal(1, d.Confirmed));
    }

    [SqlServerOnlyFact]
    public async Task Concurrent_duplicate_payment_requests_collect_once()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var booking = await hotel.BookAsync(nights: 1);
        await hotel.OpenShiftAsync(hotel.Admin);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => hotel.PayAsync(hotel.Admin, booking.FolioId!.Value, 52_500, key: key)));

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        var folio = await hotel.FolioAsync(booking.FolioId!.Value);
        Assert.Single(folio.Payments);
        Assert.Equal(-52_500m, folio.Balance); // paid in advance, nothing charged yet
    }
}
