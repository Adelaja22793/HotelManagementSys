using System.Net;
using System.Net.Http.Json;
using HMS.Application.Security;
using HMS.Contracts.Auth;
using HMS.Contracts.Billing;
using HMS.Contracts.Branches;
using HMS.Contracts.Cashier;
using HMS.Contracts.Guests;
using HMS.Contracts.Rates;
using HMS.Contracts.Reservations;
using HMS.Contracts.Rooms;
using HMS.Contracts.Users;

namespace HMS.Api.IntegrationTests;

/// <summary>Builds a small working hotel through the public API, the same way staff would.</summary>
internal sealed class HotelSetup
{
    public const string StaffPassword = "Temp-Password-1";

    private HotelSetup(HmsApiFactory factory, HttpClient admin, BranchSummaryDto branch)
    {
        Factory = factory;
        Admin = admin;
        Branch = branch;
    }

    public HmsApiFactory Factory { get; }
    public HttpClient Admin { get; }
    public BranchSummaryDto Branch { get; }
    public RoomTypeDto Standard { get; private set; } = null!;
    public RatePlanDto Bar { get; private set; } = null!;
    public ServiceItemDto Laundry { get; private set; } = null!;
    public List<RoomDto> Rooms { get; } = [];
    public DateOnly Today { get; private set; }

    public string P(string relative) => $"/api/v1/branches/{Branch.Id}/{relative}";

    /// <summary>Creates a new branch with a room type, <paramref name="roomCount"/> rooms, a rate plan and 5% VAT.</summary>
    public static async Task<HotelSetup> CreateAsync(HmsApiFactory factory, int roomCount = 3, decimal rate = 50_000, bool inspection = true)
    {
        var (admin, _) = await factory.LoginAsAdminAsync();
        var code = "B" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var branch = await ApiAssert.OkAsync<BranchDto>(await admin.PostAsJsonAsync("/api/v1/branches",
            new CreateBranchRequest(code, "Branch " + code, "Africa/Lagos", "NGN", null, null, null)), HttpStatusCode.Created);
        if (!inspection)
        {
            await ApiAssert.OkAsync<BranchDto>(await admin.PutAsJsonAsync($"/api/v1/branches/{branch.Id}",
                new UpdateBranchRequest(branch.Name, branch.TimeZoneId, branch.Currency, null, null, null, null, false, true, branch.ConcurrencyStamp)));
        }

        var setup = new HotelSetup(factory, admin, new BranchSummaryDto(branch.Id, branch.Code, branch.Name, branch.TimeZoneId, false));
        await setup.RefreshTodayAsync();

        setup.Standard = await ApiAssert.OkAsync<RoomTypeDto>(await admin.PostAsJsonAsync(setup.P("room-types"),
            new CreateRoomTypeRequest("STD", "Standard", null, 2, 1, rate)), HttpStatusCode.Created);
        for (var i = 1; i <= roomCount; i++)
        {
            setup.Rooms.Add(await ApiAssert.OkAsync<RoomDto>(await admin.PostAsJsonAsync(setup.P("rooms"),
                new CreateRoomRequest($"{100 + i}", setup.Standard.Id, "1")), HttpStatusCode.Created));
        }
        setup.Bar = await ApiAssert.OkAsync<RatePlanDto>(await admin.PostAsJsonAsync(setup.P("rate-plans"),
            new CreateRatePlanRequest("BAR", "Best Available Rate", true, 0, "Free cancellation until 24h before arrival.")), HttpStatusCode.Created);
        await ApiAssert.OkAsync<TaxRuleDto>(await admin.PostAsJsonAsync(setup.P("tax-rules"),
            new CreateTaxRuleRequest("VAT", "VAT", 5, "All", false, new DateOnly(2020, 1, 1), null)), HttpStatusCode.Created);
        setup.Laundry = await ApiAssert.OkAsync<ServiceItemDto>(await admin.PostAsJsonAsync(setup.P("service-items"),
            new CreateServiceItemRequest("LAUNDRY", "Laundry", 2_000, true)), HttpStatusCode.Created);
        return setup;
    }

    public async Task RefreshTodayAsync()
    {
        var branch = await ApiAssert.OkAsync<BranchDto>(await Admin.GetAsync($"/api/v1/branches/{Branch.Id}"));
        Today = branch.CurrentBusinessDate;
    }

    public async Task<HttpClient> StaffAsync(params string[] roles) => (await StaffWithIdAsync(roles)).Client;

    /// <summary>Creates a staff account for this branch and returns its email; the password is <see cref="StaffPassword"/>.</summary>
    public async Task<string> CreateStaffAsync(params string[] roles)
    {
        var email = $"staff{Guid.NewGuid():N}@hms.test";
        await ApiAssert.OkAsync<UserDto>(await Admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest(email, "Staff", StaffPassword, roles, [Branch.Id], Branch.Id)), HttpStatusCode.Created);
        return email;
    }

    public async Task<(HttpClient Client, string UserId)> StaffWithIdAsync(params string[] roles)
    {
        var email = $"staff{Guid.NewGuid():N}@hms.test";
        var created = await ApiAssert.OkAsync<UserDto>(await Admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest(email, "Staff", StaffPassword, roles, [Branch.Id], Branch.Id)), HttpStatusCode.Created);
        var (client, _) = await Factory.LoginAsync(email, StaffPassword);
        return (client, created.Id);
    }

    /// <summary>POST with a fresh (or given) Idempotency-Key header.</summary>
    public static Task<HttpResponseMessage> PostKeyedAsync<T>(HttpClient client, string url, T body, string? key = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        message.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(message);
    }

    public static CreateReservationRequest Booking(Guid roomTypeId, DateOnly arrival, int nights, string lastName = "Okafor", decimal discount = 0) =>
        new(null, new SaveGuestRequest("Ada", lastName, $"{lastName.ToLowerInvariant()}@guest.test", "+2348012345678", "NG", null, null, null, null),
            roomTypeId, null, arrival, arrival.AddDays(nights), 2, 0, discount, "FrontDesk", null);

    public async Task<HttpResponseMessage> PostBookingAsync(HttpClient client, CreateReservationRequest request, string? key = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, P("reservations")) { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await client.SendAsync(message);
    }

    public async Task<ReservationDto> BookAsync(int nights = 3, DateOnly? arrival = null, string lastName = "Okafor") =>
        await ApiAssert.OkAsync<ReservationDto>(await PostBookingAsync(Admin, Booking(Standard.Id, arrival ?? Today, nights, lastName)), HttpStatusCode.Created);

    public async Task<CashierShiftDto> OpenShiftAsync(HttpClient client, decimal openingFloat = 0)
    {
        var response = await client.PostAsJsonAsync(P("cashier/shifts"), new OpenShiftRequest(openingFloat));
        return await ApiAssert.OkAsync<CashierShiftDto>(response, HttpStatusCode.Created);
    }

    public async Task<HttpResponseMessage> PayAsync(HttpClient client, Guid folioId, decimal amount, string tender = "Cash", string? key = null, string? reference = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, P($"folios/{folioId}/payments"))
        {
            Content = JsonContent.Create(new RecordPaymentRequest(tender, amount, reference)),
        };
        message.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await client.SendAsync(message);
    }

    public Task<FolioDto> FolioAsync(Guid folioId) => GetAsync<FolioDto>($"folios/{folioId}");

    public async Task<T> GetAsync<T>(string relative) => await ApiAssert.OkAsync<T>(await Admin.GetAsync(P(relative)));

    public static string[] Receptionist => [Roles.Receptionist];
}
