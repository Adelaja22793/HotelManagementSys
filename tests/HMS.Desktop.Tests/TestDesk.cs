using HMS.Api.IntegrationTests;
using HMS.Desktop.Services;
using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Tests;

/// <summary>Answers yes to every question and uses fixed files for open and save dialogs.</summary>
internal sealed class YesDialogs : IDialogService
{
    public List<string> Errors { get; } = [];
    public string? FileToOpen { get; set; }
    public string? FileToSave { get; set; }
    public void ShowError(string message, string? traceId = null) => Errors.Add(message);
    public void ShowInfo(string message) { }
    public bool Confirm(string message) => true;
    public string? PickFileToOpen() => FileToOpen;
    public string? PickFileToSave(string suggestedName) => FileToSave;
}

/// <summary>A signed-in desktop session against the in-memory API.</summary>
internal static class TestDesk
{
    public static async Task<(ApiClient Api, SessionState Session)> SignInAsync(HmsApiFactory factory, HotelSetup hotel, string? email = null)
    {
        var session = new SessionState();
        var api = new ApiClient(factory.CreateClient(), session);
        var login = email is null
            ? await api.LoginAsync(HmsApiFactory.AdminEmail, HmsApiFactory.AdminPassword)
            : await api.LoginAsync(email, HotelSetup.StaffPassword);
        session.Branch = login.Branches.Single(b => b.Id == hotel.Branch.Id);
        return (api, session);
    }

    public static void Ok(PageViewModel page) => Assert.True(page.ErrorMessage is null, $"{page.Title}: {page.ErrorMessage}");
}
