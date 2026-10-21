using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HMS.Contracts.Rates;
using HMS.Contracts.Rooms;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Room board by status, room types and rate plans. Set-up changes are made by managers in the API or a later admin screen.</summary>
public sealed partial class RoomsPageViewModel(ApiClient api) : PageViewModel
{
    public static readonly string[] StatusFilters = ["", "VacantClean", "VacantDirty", "OccupiedClean", "OccupiedDirty"];

    public override string Title => "Rooms and rates";

    public ObservableCollection<RoomDto> Rooms { get; } = [];
    public ObservableCollection<RoomTypeDto> RoomTypes { get; } = [];
    public ObservableCollection<RatePlanDto> RatePlans { get; } = [];
    [ObservableProperty] private string _statusFilter = "";
    [ObservableProperty] private string? _summary;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var rooms = await api.GetAsync<List<RoomDto>>(Query.Build(api.BranchPath("rooms"), ("status", StatusFilter)));
        FrontDeskPageViewModel.Replace(Rooms, rooms.OrderBy(r => r.Floor).ThenBy(r => r.RoomNumber, StringComparer.Ordinal));
        FrontDeskPageViewModel.Replace(RoomTypes, await api.GetAsync<List<RoomTypeDto>>(api.BranchPath("room-types")));
        FrontDeskPageViewModel.Replace(RatePlans, await api.GetAsync<List<RatePlanDto>>(api.BranchPath("rate-plans")));
        Summary = string.Join("   ", rooms.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}"))
                  + $"   Blocked today: {rooms.Count(r => r.IsBlockedToday)}";
    });

    partial void OnStatusFilterChanged(string value) => _ = LoadAsync();
}
