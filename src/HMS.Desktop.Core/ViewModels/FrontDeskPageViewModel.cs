using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.FrontDesk;
using HMS.Contracts.Reservations;
using HMS.Contracts.Rooms;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Today's arrivals, in-house guests and departures, with check-in and check-out.</summary>
public sealed partial class FrontDeskPageViewModel(ApiClient api, SessionState session, NavigationService navigation, IDialogService dialogs)
    : PageViewModel
{
    public override string Title => "Front desk";

    [ObservableProperty] private FrontDeskSummaryDto? _summary;
    public ObservableCollection<ReservationListItemDto> Arrivals { get; } = [];
    public ObservableCollection<StayDto> InHouse { get; } = [];
    public ObservableCollection<StayDto> Departures { get; } = [];
    public ObservableCollection<RoomDto> RoomsForArrival { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckInCommand))]
    private ReservationListItemDto? _selectedArrival;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckInCommand))]
    private RoomDto? _selectedRoom;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckOutCommand), nameof(OpenFolioCommand))]
    private StayDto? _selectedStay;

    [ObservableProperty] private bool _allowBalance;
    [ObservableProperty] private string? _balanceReason;

    public bool CanCheckIn => session.HasPermission(PermissionNames.FrontDeskCheckIn);
    public bool CanCheckOut => session.HasPermission(PermissionNames.FrontDeskCheckOut);
    public bool CanOverrideBalance => session.HasPermission(PermissionNames.CheckOutWithBalance);

    private List<RoomDto> _rooms = [];

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Summary = await api.GetAsync<FrontDeskSummaryDto>(api.BranchPath("front-desk/summary"));
        Replace(Arrivals, (await api.GetAsync<List<ReservationListItemDto>>(api.BranchPath("front-desk/arrivals")))
            .Where(a => a.Status == "Confirmed"));
        Replace(InHouse, await api.GetAsync<List<StayDto>>(api.BranchPath("front-desk/in-house")));
        Replace(Departures, await api.GetAsync<List<StayDto>>(api.BranchPath("front-desk/departures")));
        _rooms = await api.GetAsync<List<RoomDto>>(api.BranchPath("rooms"));
        UpdateRoomChoices();
    });

    partial void OnSelectedArrivalChanged(ReservationListItemDto? value) => UpdateRoomChoices();

    /// <summary>Clean, vacant, unblocked rooms of the booked type; the already assigned room comes first.</summary>
    private void UpdateRoomChoices()
    {
        RoomsForArrival.Clear();
        SelectedRoom = null;
        if (SelectedArrival is not { } arrival) return;
        foreach (var room in _rooms
                     .Where(r => r.IsActive && !r.IsBlockedToday && r.RoomTypeCode == arrival.RoomTypeCode
                                 && (r.Status == "VacantClean" || r.RoomNumber == arrival.RoomNumber))
                     .OrderByDescending(r => r.RoomNumber == arrival.RoomNumber).ThenBy(r => r.RoomNumber, StringComparer.Ordinal))
            RoomsForArrival.Add(room);
        SelectedRoom = RoomsForArrival.FirstOrDefault();
    }

    private bool CanRunCheckIn() => CanCheckIn && SelectedArrival is not null && SelectedRoom is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunCheckIn))]
    private async Task CheckInAsync()
    {
        var arrival = SelectedArrival!;
        var room = SelectedRoom!;
        StayDto? stay = null;
        if (await RunAsync(async () =>
            {
                // The reservation's concurrency stamp makes a double click or a second desk fail cleanly.
                var reservation = await api.GetAsync<ReservationDto>(api.BranchPath($"reservations/{arrival.Id}"));
                stay = await api.PostAsync<StayDto>(api.BranchPath("front-desk/check-in"), new CheckInRequest(arrival.Id, room.Id, reservation.ConcurrencyStamp));
            }))
        {
            await LoadAsync();
            StatusMessage = $"{stay!.GuestName} checked in to room {stay.RoomNumber}.";
        }
    }

    private bool CanRunCheckOut() => CanCheckOut && SelectedStay is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunCheckOut))]
    private async Task CheckOutAsync()
    {
        var stay = SelectedStay!;
        if (stay.Balance > 0 && !AllowBalance)
        {
            ErrorMessage = $"{stay.GuestName} still owes {stay.Balance:N2}. Take payment on the folio first" +
                           (CanOverrideBalance ? ", or tick 'check out with balance' and give a reason." : ".");
            return;
        }
        if (!dialogs.Confirm($"Check out {stay.GuestName} from room {stay.RoomNumber}?")) return;

        CheckOutResultDto? result = null;
        if (await RunAsync(async () => result = await api.PostAsync<CheckOutResultDto>(api.BranchPath("front-desk/check-out"),
                new CheckOutRequest(stay.Id, AllowBalance, AllowBalance ? BalanceReason : null))))
        {
            AllowBalance = false;
            BalanceReason = null;
            await LoadAsync();
            StatusMessage = $"{stay.GuestName} checked out. Invoice {result!.InvoiceNumber}; room {stay.RoomNumber} sent to housekeeping.";
        }
    }

    private bool CanOpenFolio() => SelectedStay?.FolioId is not null;

    [RelayCommand(CanExecute = nameof(CanOpenFolio))]
    private Task OpenFolioAsync() => navigation.OpenAsync<FolioPageViewModel>(p => p.OpenAsync(SelectedStay!.FolioId!.Value));

    internal static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
