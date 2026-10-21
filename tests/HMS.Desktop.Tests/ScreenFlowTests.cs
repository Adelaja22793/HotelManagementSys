using HMS.Api.IntegrationTests;
using HMS.Application.Security;
using HMS.Desktop.Services;
using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Tests;

/// <summary>
/// Drives the desktop screens' view models against the real API running in memory: a booking made on the
/// Reservations screen is checked in on Front desk, billed and paid on Folios, checked out, and its room cleaned
/// and inspected on Housekeeping. This proves every route, body and DTO the screens use.
/// </summary>
public sealed class ScreenFlowTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    private sealed record Desk(ApiClient Api, SessionState Session, NavigationService Navigation, YesDialogs Dialogs);

    private async Task<Desk> SignInAsync(HotelSetup hotel, string email, string password)
    {
        var session = new SessionState();
        var api = new ApiClient(factory.CreateClient(), session);
        var login = await api.LoginAsync(email, password);
        session.Branch = login.Branches.Single(b => b.Id == hotel.Branch.Id);
        var dialogs = new YesDialogs();
        NavigationService? navigation = null;
        navigation = new NavigationService(type => type.Name switch
        {
            nameof(FolioPageViewModel) => new FolioPageViewModel(api, session, dialogs),
            nameof(FrontDeskPageViewModel) => new FrontDeskPageViewModel(api, session, navigation!, dialogs),
            _ => throw new NotSupportedException(type.Name),
        });
        return new Desk(api, session, navigation, dialogs);
    }

    private static void Ok(PageViewModel page) => Assert.True(page.ErrorMessage is null, $"{page.Title}: {page.ErrorMessage}");

    [Fact]
    public async Task Booking_to_check_out_and_clean_room_through_the_screens()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 2);
        var desk = await SignInAsync(hotel, HmsApiFactory.AdminEmail, HmsApiFactory.AdminPassword);
        var today = hotel.Today.ToDateTime(TimeOnly.MinValue);

        // Reservations: availability, new guest, book.
        var reservations = new ReservationsPageViewModel(desk.Api, desk.Session, desk.Navigation, desk.Dialogs);
        await reservations.LoadAsync();
        Ok(reservations);
        reservations.NewArrival = today;
        reservations.NewDeparture = today.AddDays(2);
        reservations.Adults = 2;
        // Like a WPF button: enabled state is only re-read when the command says it changed.
        var bookEnabled = reservations.BookCommand.CanExecute(null);
        reservations.BookCommand.CanExecuteChanged += (_, _) => bookEnabled = reservations.BookCommand.CanExecute(null);
        await reservations.CheckAvailabilityCommand.ExecuteAsync(null);
        Ok(reservations);
        Assert.Equal(hotel.Standard.Id, reservations.SelectedRoomType?.RoomTypeId);
        reservations.FirstName = "Chidi";
        reservations.LastName = "Eze";
        reservations.Phone = "+2348011111111";
        Assert.True(bookEnabled, "The Book button must be enabled once a room type is picked.");
        await reservations.BookCommand.ExecuteAsync(null);
        Ok(reservations);
        var booking = reservations.Selected!;
        Assert.Equal("Confirmed", booking.Status);
        Assert.Contains(reservations.Results, r => r.Id == booking.Id);

        // Cashier: open a shift so cash can be taken.
        var cashier = new CashierPageViewModel(desk.Api, desk.Dialogs);
        await cashier.LoadAsync();
        Assert.Null(cashier.Shift);
        cashier.OpeningFloat = 5_000;
        await cashier.OpenShiftCommand.ExecuteAsync(null);
        Ok(cashier);
        Assert.True(cashier.HasOpenShift);

        // Front desk: the arrival is listed, a clean room is offered, check in.
        var frontDesk = new FrontDeskPageViewModel(desk.Api, desk.Session, desk.Navigation, desk.Dialogs);
        await frontDesk.LoadAsync();
        Ok(frontDesk);
        frontDesk.SelectedArrival = frontDesk.Arrivals.Single(a => a.Id == booking.Id);
        Assert.NotNull(frontDesk.SelectedRoom);
        await frontDesk.CheckInCommand.ExecuteAsync(null);
        Ok(frontDesk);
        var stay = frontDesk.InHouse.Single(s => s.ReservationId == booking.Id);

        // Folio: open from the stay, post laundry, pay the balance in cash.
        frontDesk.SelectedStay = stay;
        await frontDesk.OpenFolioCommand.ExecuteAsync(null);
        var folio = Assert.IsType<FolioPageViewModel>(desk.Navigation.Current);
        Ok(folio);
        folio.SelectedService = folio.ServiceItems.Single(s => s.Code == "LAUNDRY");
        folio.Quantity = 2;
        await folio.PostChargeCommand.ExecuteAsync(null);
        Ok(folio);
        Assert.Contains(folio.Folio!.Charges, c => c.Description.Contains("Laundry", StringComparison.Ordinal));
        Assert.True(folio.Folio.Balance > 0);
        folio.Tender = "Cash";
        folio.PaymentAmount = folio.Folio.Balance;
        await folio.RecordPaymentCommand.ExecuteAsync(null);
        Ok(folio);
        Assert.Equal(0, folio.Folio!.Balance);

        // Front desk: check out.
        await frontDesk.LoadAsync();
        frontDesk.SelectedStay = frontDesk.InHouse.Single(s => s.ReservationId == booking.Id);
        await frontDesk.CheckOutCommand.ExecuteAsync(null);
        Ok(frontDesk);
        Assert.DoesNotContain(frontDesk.InHouse, s => s.ReservationId == booking.Id);
        Assert.Contains("checked out", frontDesk.StatusMessage);

        // Housekeeping: clean and inspect the room.
        var housekeeping = new HousekeepingPageViewModel(desk.Api, desk.Session);
        await housekeeping.LoadAsync();
        Ok(housekeeping);
        housekeeping.Selected = housekeeping.Tasks.Single(t => t.RoomNumber == stay.RoomNumber);
        await housekeeping.StartCommand.ExecuteAsync(null);
        housekeeping.Selected = housekeeping.Tasks.Single(t => t.RoomNumber == stay.RoomNumber);
        await housekeeping.CompleteCommand.ExecuteAsync(null);
        housekeeping.Selected = housekeeping.Tasks.Single(t => t.RoomNumber == stay.RoomNumber);
        Assert.Equal("AwaitingInspection", housekeeping.Selected.Status);
        await housekeeping.PassCommand.ExecuteAsync(null);
        Ok(housekeeping);
        Assert.DoesNotContain(housekeeping.Tasks, t => t.RoomNumber == stay.RoomNumber);

        // Cashier: close balanced.
        await cashier.LoadAsync();
        cashier.CountedCash = cashier.Shift!.RunningCash; // the running cash total already includes the float
        cashier.VerifiedPos = 0;
        cashier.VerifiedTransfer = 0;
        await cashier.CloseShiftCommand.ExecuteAsync(null);
        Ok(cashier);
        Assert.Equal("Closed", cashier.Shift!.Status);
    }

    [Fact]
    public async Task Overview_screens_load_and_cancellation_works()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 2);
        var desk = await SignInAsync(hotel, HmsApiFactory.AdminEmail, HmsApiFactory.AdminPassword);
        var booking = await hotel.BookAsync(nights: 1, arrival: hotel.Today.AddDays(3), lastName: "Bello");

        var dashboard = new DashboardPageViewModel(desk.Api, desk.Session);
        await dashboard.LoadAsync();
        Ok(dashboard);
        Assert.NotNull(dashboard.Today);
        Assert.NotNull(dashboard.Week);
        Assert.Equal("0 of 0", dashboard.DeparturesToday);
        Assert.Equal($"0 of {dashboard.Today!.ArrivalsExpected}", dashboard.ArrivalsToday);

        var rooms = new RoomsPageViewModel(desk.Api);
        await rooms.LoadAsync();
        Ok(rooms);
        Assert.Equal(2, rooms.Rooms.Count);
        Assert.Single(rooms.RatePlans);

        var guests = new GuestsPageViewModel(desk.Api, desk.Session) { Search = "Bello" };
        await guests.SearchCommand.ExecuteAsync(null);
        Ok(guests);
        guests.SelectedResult = guests.Results.Single();
        await WaitFor(() => guests.Guest is not null);
        guests.Phone = "+2348099999999";
        await guests.SaveCommand.ExecuteAsync(null);
        Ok(guests);
        Assert.Equal("+2348099999999", guests.Guest!.Phone);

        var reservations = new ReservationsPageViewModel(desk.Api, desk.Session, desk.Navigation, desk.Dialogs);
        await reservations.OpenAsync(booking.Id);
        Ok(reservations);
        Assert.Equal(2, reservations.AssignableRooms.Count);
        reservations.RoomToAssign = reservations.AssignableRooms[0];
        await reservations.AssignRoomCommand.ExecuteAsync(null);
        Ok(reservations);
        Assert.Equal(reservations.AssignableRooms[0].RoomNumber, reservations.Selected!.RoomNumber);

        reservations.CancelReason = "Guest changed plans";
        await reservations.CancelReservationCommand.ExecuteAsync(null);
        Ok(reservations);
        Assert.Equal("Cancelled", reservations.Selected!.Status);
    }

    [Fact]
    public async Task Screens_explain_refusals_in_plain_words()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var email = await hotel.CreateStaffAsync(Roles.Housekeeping);
        var desk = await SignInAsync(hotel, email, HotelSetup.StaffPassword);

        // A housekeeper does not get the money screens in the menu.
        var shell = new ShellViewModel(desk.Api, desk.Session, desk.Navigation);
        Assert.Contains(shell.Modules, m => m.Title == "Housekeeping");
        Assert.DoesNotContain(shell.Modules, m => m.Title == "Folios and payments");
        Assert.DoesNotContain(shell.Modules, m => m.Title == "Reservations");

        // And if a screen is reached anyway, the API refusal is shown as a message, not a crash.
        var reservations = new ReservationsPageViewModel(desk.Api, desk.Session, desk.Navigation, desk.Dialogs);
        await reservations.LoadAsync();
        Assert.StartsWith("You do not have permission", reservations.ErrorMessage);
        Assert.False(reservations.CanManage);
    }

    [Fact]
    public async Task Every_module_opens_a_screen_for_an_administrator()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var desk = await SignInAsync(hotel, HmsApiFactory.AdminEmail, HmsApiFactory.AdminPassword);
        var shell = new ShellViewModel(desk.Api, desk.Session, desk.Navigation);
        Assert.Equal(19, shell.Modules.Count);
        Assert.All(shell.Modules, m => Assert.True(m.IsAvailable, m.Title));
        Assert.All(shell.Modules, m => Assert.True(typeof(PageViewModel).IsAssignableFrom(m.PageType), m.Title));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }
}
