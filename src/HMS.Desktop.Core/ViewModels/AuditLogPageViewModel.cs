using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Common;
using HMS.Contracts.Operations;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Searches the branch audit trail and exports the result to CSV for auditors. Exports are themselves audited.</summary>
public sealed partial class AuditLogPageViewModel(ApiClient api, IDialogService dialogs) : PageViewModel
{
    public const int PageSize = 100;

    public override string Title => "Audit log";

    public ObservableCollection<AuditLogDto> Entries { get; } = [];

    [ObservableProperty] private string? _entityType;
    [ObservableProperty] private string? _entityId;
    [ObservableProperty] private string? _userId;
    [ObservableProperty] private string? _action;
    [ObservableProperty] private DateTime? _from = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime? _to;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand), nameof(PreviousPageCommand))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages;

    [ObservableProperty] private int _totalCount;

    private (string Name, object? Value)[] Filters() =>
    [
        ("entityType", EntityType?.Trim()), ("entityId", EntityId?.Trim()), ("userId", UserId?.Trim()), ("action", Action?.Trim()),
        ("fromUtc", From?.ToUniversalTime()), ("toUtc", To?.AddDays(1).ToUniversalTime()),
    ];

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var result = await api.GetAsync<PagedResult<AuditLogDto>>(Query.Build(api.BranchPath("audit-logs"),
            [.. Filters(), ("page", Page), ("pageSize", PageSize)]));
        FrontDeskPageViewModel.Replace(Entries, result.Items);
        TotalCount = result.TotalCount;
        TotalPages = result.TotalPages;
    });

    [RelayCommand]
    private Task SearchAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    private bool HasNext() => Page < TotalPages;
    private bool HasPrevious() => Page > 1;

    [RelayCommand(CanExecute = nameof(HasNext))]
    private Task NextPageAsync() { Page++; return LoadAsync(); }

    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private Task PreviousPageAsync() { Page--; return LoadAsync(); }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var path = dialogs.PickFileToSave($"audit-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        if (path is null) return;
        await RunAsync(async () =>
        {
            var (content, _) = await api.DownloadAsync(Query.Build(api.BranchPath("audit-logs/export"), Filters()));
            await File.WriteAllBytesAsync(path, content);
        }, $"Exported to {path}.");
    }
}
