using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Common;
using HMS.Contracts.Guests;
using HMS.Contracts.Reservations;
using HMS.Contracts.Rooms;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Find, book, change and cancel reservations.</summary>
public sealed partial class ReservationsPageViewModel(ApiClient api, SessionState session, NavigationService navigation, IDialogService dialogs)
    : PageViewModel
{
    public static readonly string[] Statuses = ["", "Confirmed", "CheckedIn", "CheckedOut", "Cancelled", "NoShow"];
    public static readonly string[] Sources = ["FrontDesk", "Phone", "WalkIn", "Email"];

    public override string Title => "Reservations";

    // ---- search ----
    [ObservableProperty] private string? _search;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private DateTime? _arrivalFrom;
    [ObservableProperty] private DateTime? _arrivalTo;
    [ObservableProperty] private int _totalCount;
    public ObservableCollection<ReservationListItemDto> Results { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelReservationCommand), nameof(NoShowCommand), nameof(AssignRoomCommand), nameof(OpenFolioCommand))]
    private ReservationDto? _selected;

    [ObservableProperty] private ReservationListItemDto? _selectedResult;
    [ObservableProperty] private string? _cancelReason;
    public ObservableCollection<RoomDto> AssignableRooms { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignRoomCommand))]
    private RoomDto? _roomToAssign;

    public bool CanManage => session.HasPermission(PermissionNames.ReservationsManage);

    // ---- new booking ----
    [ObservableProperty] private DateTime _newArrival = DateTime.Today;
    [ObservableProperty] private DateTime _newDeparture = DateTime.Today.AddDays(1);
    [ObservableProperty] private int _adults = 1;
    [ObservableProperty] private int _children;
    [ObservableProperty] private decimal _discountPercent;
    [ObservableProperty] private string _source = "FrontDesk";
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string _firstName = "";
    [ObservableProperty] private string _lastName = "";
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private GuestSummaryDto? _existingGuest;
    public ObservableCollection<RoomTypeAvailabilityDto> Availability { get; } = [];
    public ObservableCollection<GuestSummaryDto> GuestMatches { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BookCommand))]
    private RoomTypeAvailabilityDto? _selectedRoomType;

    /// <summary>One key per booking attempt: a retry after a lost response cannot book twice.</summary>
    private string _bookingKey = Guid.NewGuid().ToString();

    public override Task LoadAsync() => SearchAsync();

    [RelayCommand]
    private Task SearchAsync() => RunAsync(async () =>
    {
        var page = await api.GetAsync<PagedResult<ReservationListItemDto>>(Query.Build(api.BranchPath("reservations"),
            ("search", Search), ("status", Status), ("arrivalFrom", ToDate(ArrivalFrom)), ("arrivalTo", ToDate(ArrivalTo)), ("pageSize", 100)));
        FrontDeskPageViewModel.Replace(Results, page.Items);
        TotalCount = page.TotalCount;
    });

    async partial void OnSelectedResultChanged(ReservationListItemDto? value)
    {
        if (value is null) { Selected = null; return; }
        await OpenAsync(value.Id);
    }

    /// <summary>Loads one reservation into the detail panel, with the rooms it could be given.</summary>
    public Task OpenAsync(Guid reservationId) => RunAsync(async () =>
    {
        Selected = await api.GetAsync<ReservationDto>(api.BranchPath($"reservations/{reservationId}"));
        AssignableRooms.Clear();
        if (Selected.Status == "Confirmed")
        {
            var rooms = await api.GetAsync<List<RoomDto>>(api.BranchPath("rooms"));
            foreach (var room in rooms.Where(r => r.IsActive && r.RoomTypeId == Selected.RoomTypeId).OrderBy(r => r.RoomNumber, StringComparer.Ordinal))
                AssignableRooms.Add(room);
        }
    });

    private bool IsConfirmed() => CanManage && Selected?.Status == "Confirmed" && !IsBusy;

    [RelayCommand(CanExecute = nameof(IsConfirmed))]
    private async Task CancelReservationAsync()
    {
        if (string.IsNullOrWhiteSpace(CancelReason)) { ErrorMessage = "Give a reason for the cancellation."; return; }
        if (!dialogs.Confirm($"Cancel reservation {Selected!.ReservationNumber} for {Selected.Guest.FullName}?")) return;
        var current = Selected;
        if (await RunAsync(async () => Selected = await api.PostAsync<ReservationDto>(api.BranchPath($"reservations/{current.Id}/cancel"),
                new CancelReservationRequest(CancelReason!, current.ConcurrencyStamp)), "Reservation cancelled."))
        {
            CancelReason = null;
            await SearchAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(IsConfirmed))]
    private async Task NoShowAsync()
    {
        if (!dialogs.Confirm($"Mark {Selected!.ReservationNumber} as a no-show?")) return;
        var current = Selected;
        await RunAsync(async () => Selected = await api.PostAsync<ReservationDto>(api.BranchPath($"reservations/{current.Id}/no-show"),
            new CancelReservationRequest(string.IsNullOrWhiteSpace(CancelReason) ? "Guest did not arrive." : CancelReason!, current.ConcurrencyStamp)),
            "Marked as no-show.");
    }

    private bool CanAssign() => IsConfirmed() && RoomToAssign is not null;

    [RelayCommand(CanExecute = nameof(CanAssign))]
    private Task AssignRoomAsync()
    {
        var current = Selected!;
        var room = RoomToAssign!;
        return RunAsync(async () => Selected = await api.PostAsync<ReservationDto>(api.BranchPath($"reservations/{current.Id}/assign-room"),
            new AssignRoomRequest(room.Id, current.ConcurrencyStamp)), $"Room {room.RoomNumber} assigned.");
    }

    private bool HasFolio() => Selected?.FolioId is not null;

    [RelayCommand(CanExecute = nameof(HasFolio))]
    private Task OpenFolioAsync() => navigation.OpenAsync<FolioPageViewModel>(p => p.OpenAsync(Selected!.FolioId!.Value));

    // ---- new booking ----

    [RelayCommand]
    private async Task CheckAvailabilityAsync()
    {
        if (NewDeparture.Date <= NewArrival.Date) { ErrorMessage = "Departure must be after arrival."; return; }
        await RunAsync(async () =>
        {
            var types = await api.GetAsync<List<RoomTypeAvailabilityDto>>(Query.Build(api.BranchPath("availability"),
                ("arrivalDate", DateOnly.FromDateTime(NewArrival)), ("departureDate", DateOnly.FromDateTime(NewDeparture)),
                ("adults", Adults), ("children", Children)));
            FrontDeskPageViewModel.Replace(Availability, types);
            SelectedRoomType = types.FirstOrDefault(t => t.AvailableRooms > 0);
            _bookingKey = Guid.NewGuid().ToString();
        });
    }

    [RelayCommand]
    private Task FindGuestAsync() => RunAsync(async () =>
    {
        var term = string.Join(' ', new[] { FirstName, LastName, Email, Phone }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var page = await api.GetAsync<PagedResult<GuestSummaryDto>>(Query.Build(api.BranchPath("guests"), ("search", term), ("pageSize", 20)));
        FrontDeskPageViewModel.Replace(GuestMatches, page.Items);
        if (page.Items.Count == 0) StatusMessage = "No matching guest. A new guest will be created.";
    });

    partial void OnExistingGuestChanged(GuestSummaryDto? value) => _bookingKey = Guid.NewGuid().ToString();

    private bool CanBook() => CanManage && SelectedRoomType is { AvailableRooms: > 0 } && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanBook))]
    private async Task BookAsync()
    {
        if (ExistingGuest is null && (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName)))
        {
            ErrorMessage = "Enter the guest's first and last name, or pick an existing guest.";
            return;
        }
        var request = new CreateReservationRequest(
            ExistingGuest?.Id,
            ExistingGuest is null ? new SaveGuestRequest(FirstName.Trim(), LastName.Trim(), Blank(Email), Blank(Phone), null, null, null, null, null) : null,
            SelectedRoomType!.RoomTypeId, SelectedRoomType.Quote?.RatePlanId,
            DateOnly.FromDateTime(NewArrival), DateOnly.FromDateTime(NewDeparture), Adults, Children, DiscountPercent, Source, Blank(Notes));

        ReservationDto? created = null;
        if (await RunAsync(async () => created = await api.PostAsync<ReservationDto>(api.BranchPath("reservations"), request, _bookingKey)))
        {
            _bookingKey = Guid.NewGuid().ToString();
            StatusMessage = $"Booked {created!.ReservationNumber} for {created.Guest.FullName}: {created.TotalAmount:N2} {created.Currency}.";
            ClearNewBooking();
            Selected = created;
            await SearchAsync();
            StatusMessage = $"Booked {created.ReservationNumber} for {created.Guest.FullName}.";
        }
    }

    private void ClearNewBooking()
    {
        FirstName = LastName = "";
        Email = Phone = Notes = null;
        ExistingGuest = null;
        DiscountPercent = 0;
        Availability.Clear();
        GuestMatches.Clear();
        SelectedRoomType = null;
    }

    private static DateOnly? ToDate(DateTime? value) => value is null ? null : DateOnly.FromDateTime(value.Value);
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
