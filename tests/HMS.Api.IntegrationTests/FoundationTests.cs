using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HMS.Application.Security;
using HMS.Contracts.Auth;
using HMS.Contracts.Branches;
using HMS.Contracts.Common;
using HMS.Contracts.Operations;
using HMS.Contracts.Users;

namespace HMS.Api.IntegrationTests;

public class FoundationTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    private const string StaffPassword = "Temp-Password-1";

    [Fact]
    public async Task Liveness_and_readiness_are_anonymous()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Correlation_id_is_echoed()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(HmsHeaders.CorrelationId, "abc-123");
        var response = await client.SendAsync(request);
        Assert.Equal("abc-123", response.Headers.GetValues(HmsHeaders.CorrelationId).Single());
    }

    [Fact]
    public async Task Protected_endpoints_require_authentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/branches");
        await ApiAssert.ErrorAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task Wrong_password_returns_generic_error()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(HmsApiFactory.AdminEmail, "wrong-password"));
        await ApiAssert.ErrorAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);

        var unknown = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("nobody@hms.test", "wrong-password"));
        await ApiAssert.ErrorAsync(unknown, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Bootstrap_admin_can_sign_in_and_see_branch()
    {
        var (client, login) = await factory.LoginAsAdminAsync();

        Assert.Contains(Roles.SystemAdmin, login.User.Roles);
        Assert.True(login.User.MustChangePassword);
        Assert.Contains(login.Branches, b => b.Code == "LOS" && b.IsDefault);

        var me = await ApiAssert.OkAsync<CurrentUserDto>(await client.GetAsync("/api/v1/auth/me"));
        Assert.Equal(HmsApiFactory.AdminEmail, me.Email);
        Assert.Contains(Permissions.UsersManage, me.Permissions);
    }

    [Fact]
    public async Task Staff_cannot_access_a_branch_they_are_not_assigned_to()
    {
        var (admin, adminLogin) = await factory.LoginAsAdminAsync();
        var lagos = adminLogin.Branches.Single(b => b.Code == "LOS");
        var abuja = await CreateBranchAsync(admin, "ABJ" + Suffix(), "Abuja");

        var email = $"reception{Suffix()}@hms.test";
        await CreateUserAsync(admin, email, [Roles.Receptionist], [lagos.Id]);
        var (staff, staffLogin) = await factory.LoginAsync(email, StaffPassword);

        Assert.Single(staffLogin.Branches);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/v1/branches/{lagos.Id}")).StatusCode);
        await ApiAssert.ErrorAsync(await staff.GetAsync($"/api/v1/branches/{abuja.Id}"), HttpStatusCode.Forbidden, ErrorCodes.BranchAccessDenied);
        await ApiAssert.ErrorAsync(await staff.GetAsync($"/api/v1/branches/{Guid.NewGuid()}"), HttpStatusCode.Forbidden, ErrorCodes.BranchAccessDenied);

        var mine = await ApiAssert.OkAsync<List<BranchSummaryDto>>(await staff.GetAsync("/api/v1/branches"));
        Assert.DoesNotContain(mine, b => b.Id == abuja.Id);
    }

    [Fact]
    public async Task Role_without_permission_is_forbidden()
    {
        var (admin, adminLogin) = await factory.LoginAsAdminAsync();
        var email = $"housekeeper{Suffix()}@hms.test";
        await CreateUserAsync(admin, email, [Roles.Housekeeping], [adminLogin.Branches[0].Id]);
        var (staff, _) = await factory.LoginAsync(email, StaffPassword);

        await ApiAssert.ErrorAsync(await staff.GetAsync("/api/v1/users"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await ApiAssert.ErrorAsync(await staff.GetAsync($"/api/v1/branches/{adminLogin.Branches[0].Id}/audit-logs"), HttpStatusCode.Forbidden);
        var create = await staff.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest("HK" + Suffix(), "Nope", null, null, null, null, null));
        await ApiAssert.ErrorAsync(create, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Refresh_tokens_rotate_and_reuse_revokes_the_family()
    {
        var (_, login) = await factory.LoginAsAdminAsync();
        var client = factory.CreateClient();

        var first = await ApiAssert.OkAsync<LoginResponse>(await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken)));
        Assert.NotEqual(login.RefreshToken, first.RefreshToken);

        // Re-using the rotated token is treated as theft.
        await ApiAssert.ErrorAsync(await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login.RefreshToken)),
            HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
        await ApiAssert.ErrorAsync(await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(first.RefreshToken)),
            HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
    }

    [Fact]
    public async Task Deactivated_user_tokens_stop_working_immediately()
    {
        var (admin, adminLogin) = await factory.LoginAsAdminAsync();
        var email = $"cashier{Suffix()}@hms.test";
        var user = await CreateUserAsync(admin, email, [Roles.Cashier], [adminLogin.Branches[0].Id]);
        var (staff, _) = await factory.LoginAsync(email, StaffPassword);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/auth/me")).StatusCode);

        var update = await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/access",
            new UpdateUserAccessRequest([Roles.Cashier], [adminLogin.Branches[0].Id], null, IsActive: false));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        await ApiAssert.ErrorAsync(await staff.GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized);
        var relogin = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, StaffPassword));
        await ApiAssert.ErrorAsync(relogin, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Stale_branch_update_returns_conflict()
    {
        var (admin, _) = await factory.LoginAsAdminAsync();
        var branch = await CreateBranchAsync(admin, "KAN" + Suffix(), "Kano");

        var ok = await admin.PutAsJsonAsync($"/api/v1/branches/{branch.Id}", ToUpdate(branch, "Kano Central"));
        var updated = await ApiAssert.OkAsync<BranchDto>(ok);
        Assert.NotEqual(branch.ConcurrencyStamp, updated.ConcurrencyStamp);

        var stale = await admin.PutAsJsonAsync($"/api/v1/branches/{branch.Id}", ToUpdate(branch, "Kano Old"));
        await ApiAssert.ErrorAsync(stale, HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Duplicate_branch_code_is_rejected()
    {
        var (admin, _) = await factory.LoginAsAdminAsync();
        var code = "DUP" + Suffix();
        await CreateBranchAsync(admin, code, "First");
        var again = await admin.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(code, "Second", null, null, null, null, null));
        await ApiAssert.ErrorAsync(again, HttpStatusCode.Conflict, "BRANCH_CODE_EXISTS");
    }

    [Fact]
    public async Task Validation_errors_use_the_error_contract()
    {
        var (admin, _) = await factory.LoginAsAdminAsync();
        var response = await admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest("", "", "", ["NotARole"], [], null));
        var error = await ApiAssert.ErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Contains("Email", error.Errors.Keys);
        Assert.Contains("Roles", error.Errors.Keys);
    }

    [Fact]
    public async Task User_cannot_be_assigned_to_another_company_branch()
    {
        var (admin, _) = await factory.LoginAsAdminAsync();
        var response = await admin.PostAsJsonAsync("/api/v1/users",
            new CreateUserRequest($"x{Suffix()}@hms.test", "X", StaffPassword, [Roles.Receptionist], [Guid.NewGuid()], null));
        var error = await ApiAssert.ErrorAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Contains("BranchIds", error.Errors.Keys);
    }

    [Fact]
    public async Task Branch_changes_are_audited()
    {
        var (admin, _) = await factory.LoginAsAdminAsync();
        var branch = await CreateBranchAsync(admin, "AUD" + Suffix(), "Audited");
        var logs = await ApiAssert.OkAsync<PagedResult<AuditLogDto>>(await admin.GetAsync($"/api/v1/branches/{branch.Id}/audit-logs"));
        Assert.Contains(logs.Items, l => l.EntityType == "Branch" && l.Action == "Created");
    }

    [Fact]
    public async Task Change_password_clears_flag_and_invalidates_old_sessions()
    {
        var (admin, adminLogin) = await factory.LoginAsAdminAsync();
        var email = $"pw{Suffix()}@hms.test";
        await CreateUserAsync(admin, email, [Roles.Receptionist], [adminLogin.Branches[0].Id]);
        var (staff, login) = await factory.LoginAsync(email, StaffPassword);
        Assert.True(login.User.MustChangePassword);

        var change = await staff.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest(StaffPassword, "Brand-New-Pass-9"));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        await ApiAssert.ErrorAsync(await staff.GetAsync("/api/v1/auth/me"), HttpStatusCode.Unauthorized);

        var (_, again) = await factory.LoginAsync(email, "Brand-New-Pass-9");
        Assert.False(again.User.MustChangePassword);
    }

    [Fact]
    public async Task Version_endpoint_is_public()
    {
        var info = await ApiAssert.OkAsync<VersionInfoDto>(await factory.CreateClient().GetAsync("/api/v1/system/version"));
        Assert.False(string.IsNullOrEmpty(info.ApiVersion));
        Assert.Null(info.DatabaseMigration);
    }

    private static UpdateBranchRequest ToUpdate(BranchDto b, string name) =>
        new(name, b.TimeZoneId, b.Currency, b.AddressLine, b.City, b.Phone, null, null, true, b.ConcurrencyStamp);

    private static async Task<BranchDto> CreateBranchAsync(HttpClient admin, string code, string name) =>
        await ApiAssert.OkAsync<BranchDto>(
            await admin.PostAsJsonAsync("/api/v1/branches", new CreateBranchRequest(code, name, "Africa/Lagos", "NGN", null, null, null)),
            HttpStatusCode.Created);

    private static async Task<UserDto> CreateUserAsync(HttpClient admin, string email, IReadOnlyList<string> roles, IReadOnlyList<Guid> branches) =>
        await ApiAssert.OkAsync<UserDto>(
            await admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest(email, "Staff " + email, StaffPassword, roles, branches, branches.FirstOrDefault())),
            HttpStatusCode.Created);

    private static string Suffix() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
}

public class RateLimitTests(StrictRateLimitFactory factory) : IClassFixture<StrictRateLimitFactory>
{
    [Fact]
    public async Task Login_is_rate_limited()
    {
        var client = factory.CreateClient();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 5; i++)
            last = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("x@hms.test", "nope"));
        await ApiAssert.ErrorAsync(last!, HttpStatusCode.TooManyRequests, ErrorCodes.RateLimited);
    }
}
