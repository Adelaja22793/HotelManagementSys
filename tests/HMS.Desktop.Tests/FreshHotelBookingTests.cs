using System.Net;
using System.Net.Http.Json;
using HMS.Api.IntegrationTests;
using HMS.Contracts.Branches;
using HMS.Desktop.Services;
using HMS.Desktop.ViewModels;
using HMS.Desktop.ViewModels.Setup;

namespace HMS.Desktop.Tests;

/// <summary>A hotel set up only through the Setup screen can take its first booking on the Reservations screen.</summary>
public sealed class FreshHotelBookingTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    [Fact]
    public async Task A_room_set_up_on_the_setup_screen_can_be_booked()
    {
        var (admin, _) = await factory.LoginAsAdminAsync();
        var code = "F" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var branch = await ApiAssert.OkAsync<BranchDto>(await admin.PostAsJsonAsync("/api/v1/branches",
            new CreateBranchRequest(code, "Fresh " + code, "Africa/Lagos", "NGN", null, null, null)), HttpStatusCode.Created);
        var session = new SessionState();
        var api = new ApiClient(factory.CreateClient(), session);
        var login = await api.LoginAsync(HmsApiFactory.AdminEmail, HmsApiFactory.AdminPassword);
        session.Branch = login.Branches.Single(b => b.Id == branch.Id);

        var setup = new SetupPageViewModel(api, session);
        await setup.LoadAsync();
        await AddAsync(setup, "Room types", ("Code", "DLX"), ("Name", "Deluxe"), ("MaxAdults", "2"), ("MaxChildren", "1"), ("BaseRate", "40000"));
        await AddAsync(setup, "Rooms", ("RoomNumber", "201"), ("RoomTypeId", "Deluxe"), ("Floor", "2"));

        var reservations = new ReservationsPageViewModel(api, session, new NavigationService(_ => throw new NotSupportedException()), new YesDialogs());
        await reservations.LoadAsync();
        TestDesk.Ok(reservations);
        reservations.NewArrival = DateTime.Today.AddDays(1);
        reservations.NewDeparture = DateTime.Today.AddDays(2);
        await reservations.CheckAvailabilityCommand.ExecuteAsync(null);
        TestDesk.Ok(reservations);
        Assert.NotNull(reservations.SelectedRoomType);
        Assert.True(reservations.BookCommand.CanExecute(null));
        reservations.FirstName = "Bola";
        reservations.LastName = "Ade";
        await reservations.BookCommand.ExecuteAsync(null);
        TestDesk.Ok(reservations);
        Assert.NotNull(reservations.Selected);
    }

    private static async Task AddAsync(SetupPageViewModel setup, string title, params (string Name, string Value)[] values)
    {
        setup.SelectedEntity = setup.Entities.Single(e => e.Title == title);
        await setup.WhenLoaded;
        setup.NewCommand.Execute(null);
        foreach (var (name, value) in values)
        {
            var field = setup.Form.Single(f => f.Name == name);
            if (field.IsPicker) field.Selected = field.Options.First(o => o.Label.Contains(value, StringComparison.OrdinalIgnoreCase));
            else field.Text = value;
        }
        await setup.SaveCommand.ExecuteAsync(null);
        TestDesk.Ok(setup);
    }
}
