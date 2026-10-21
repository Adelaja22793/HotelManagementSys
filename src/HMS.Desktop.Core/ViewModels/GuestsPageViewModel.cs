using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Common;
using HMS.Contracts.Guests;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Search guest profiles and correct their details.</summary>
public sealed partial class GuestsPageViewModel(ApiClient api, SessionState session) : PageViewModel
{
    public override string Title => "Guests";

    [ObservableProperty] private string? _search;
    public ObservableCollection<GuestSummaryDto> Results { get; } = [];
    [ObservableProperty] private GuestSummaryDto? _selectedResult;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private GuestDto? _guest;

    [ObservableProperty] private string _firstName = "";
    [ObservableProperty] private string _lastName = "";
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _nationality;
    [ObservableProperty] private string? _idDocumentType;
    [ObservableProperty] private string? _idDocumentNumber;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _notes;

    public bool CanEdit => session.HasPermission(PermissionNames.GuestsManage);

    public override Task LoadAsync() => SearchAsync();

    [RelayCommand]
    private Task SearchAsync() => RunAsync(async () =>
    {
        var page = await api.GetAsync<PagedResult<GuestSummaryDto>>(Query.Build(api.BranchPath("guests"), ("search", Search), ("pageSize", 100)));
        FrontDeskPageViewModel.Replace(Results, page.Items);
    });

    async partial void OnSelectedResultChanged(GuestSummaryDto? value)
    {
        if (value is null) return;
        await RunAsync(async () => Show(await api.GetAsync<GuestDto>(api.BranchPath($"guests/{value.Id}"))));
    }

    private void Show(GuestDto guest)
    {
        Guest = guest;
        FirstName = guest.FirstName;
        LastName = guest.LastName;
        Email = guest.Email;
        Phone = guest.Phone;
        Nationality = guest.Nationality;
        IdDocumentType = guest.IdDocumentType;
        IdDocumentNumber = guest.IdDocumentNumber;
        Address = guest.Address;
        Notes = guest.Notes;
    }

    private bool CanSave() => CanEdit && Guest is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName)) { ErrorMessage = "First and last name are required."; return Task.CompletedTask; }
        var guest = Guest!;
        return RunAsync(async () => Show(await api.PutAsync<GuestDto>(api.BranchPath($"guests/{guest.Id}"),
            new SaveGuestRequest(FirstName.Trim(), LastName.Trim(), Email, Phone, Nationality, IdDocumentType, IdDocumentNumber, Address, Notes,
                guest.ConcurrencyStamp))), "Guest saved.");
    }
}
