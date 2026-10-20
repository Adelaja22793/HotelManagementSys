using System.Net;
using System.Net.Http.Json;
using HMS.Contracts.Auth;
using HMS.Contracts.Branches;
using HMS.Contracts.Common;
using HMS.Desktop.Services;
using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Tests;

public class LoginViewModelTests
{
    private static readonly BranchSummaryDto Lagos = new(Guid.NewGuid(), "LOS", "Lagos", "Africa/Lagos", true);
    private static readonly BranchSummaryDto Abuja = new(Guid.NewGuid(), "ABJ", "Abuja", "Africa/Lagos", false);

    [Fact]
    public async Task Single_branch_completes_immediately()
    {
        var (vm, session) = Create(_ => Json(HttpStatusCode.OK, Login(Lagos)));
        var completed = false;
        vm.Completed += (_, _) => completed = true;
        vm.Email = "a@b.test";

        await vm.LoginCommand.ExecuteAsync("secret");

        Assert.True(completed);
        Assert.Equal(Lagos.Id, session.Branch?.Id);
    }

    [Fact]
    public async Task Multiple_branches_preselect_default_and_wait_for_choice()
    {
        var (vm, session) = Create(_ => Json(HttpStatusCode.OK, Login(Abuja, Lagos)));
        vm.Email = "a@b.test";

        await vm.LoginCommand.ExecuteAsync("secret");

        Assert.True(vm.IsSelectingBranch);
        Assert.Equal(Lagos.Id, vm.SelectedBranch?.Id);
        Assert.Null(session.Branch);
    }

    [Fact]
    public async Task Api_error_message_is_shown()
    {
        var (vm, session) = Create(_ => Json(HttpStatusCode.Unauthorized,
            ApiError.Create(ErrorCodes.InvalidCredentials, "Email or password is incorrect.", "t1")));
        vm.Email = "a@b.test";

        await vm.LoginCommand.ExecuteAsync("bad");

        Assert.Equal("Email or password is incorrect.", vm.ErrorMessage);
        Assert.Null(session.User);
    }

    [Fact]
    public async Task Network_failure_is_explained()
    {
        var (vm, _) = Create(_ => throw new HttpRequestException("down"));
        vm.Email = "a@b.test";

        await vm.LoginCommand.ExecuteAsync("secret");

        Assert.Contains("Cannot reach the hotel server", vm.ErrorMessage);
    }

    [Fact]
    public async Task User_without_branches_is_not_signed_in()
    {
        var (vm, session) = Create(_ => Json(HttpStatusCode.OK, Login()));
        vm.Email = "a@b.test";

        await vm.LoginCommand.ExecuteAsync("secret");

        Assert.NotNull(vm.ErrorMessage);
        Assert.Null(session.AccessToken);
    }

    private static (LoginViewModel, SessionState) Create(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var session = new SessionState();
        var http = new HttpClient(new StubHandler(responder)) { BaseAddress = new Uri("https://hms.test/") };
        return (new LoginViewModel(new ApiClient(http, session), session), session);
    }

    private static LoginResponse Login(params BranchSummaryDto[] branches) => new(
        "access", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(7),
        new CurrentUserDto("u1", "a@b.test", "A", Guid.NewGuid(), ["Receptionist"], ["branches.view"], false),
        branches);

    private static HttpResponseMessage Json<T>(HttpStatusCode status, T body) =>
        new(status) { Content = JsonContent.Create(body) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
