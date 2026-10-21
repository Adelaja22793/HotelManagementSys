using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Cashier;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Open a cashier shift with a float, watch running totals, and close it with counted amounts.</summary>
public sealed partial class CashierPageViewModel(ApiClient api, IDialogService dialogs) : PageViewModel
{
    public override string Title => "Cashier shift";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenShift))]
    private CashierShiftDto? _shift;

    [ObservableProperty] private decimal _openingFloat;
    [ObservableProperty] private decimal? _countedCash;
    [ObservableProperty] private decimal? _verifiedPos;
    [ObservableProperty] private decimal? _verifiedTransfer;
    [ObservableProperty] private string? _varianceExplanation;

    public bool HasOpenShift => Shift?.Status == "Open";

    public override Task LoadAsync() => RunAsync(async () =>
        Shift = await api.GetAsync<CashierShiftDto?>(api.BranchPath("cashier/shifts/current")));

    [RelayCommand]
    private Task OpenShiftAsync()
    {
        if (OpeningFloat < 0) { ErrorMessage = "The float cannot be negative."; return Task.CompletedTask; }
        return RunAsync(async () => Shift = await api.PostAsync<CashierShiftDto>(api.BranchPath("cashier/shifts"), new OpenShiftRequest(OpeningFloat)),
            "Shift opened.");
    }

    [RelayCommand]
    private async Task CloseShiftAsync()
    {
        if (Shift is null) return;
        if (CountedCash is null || VerifiedPos is null || VerifiedTransfer is null)
        {
            ErrorMessage = "Enter the counted cash and the POS and transfer totals you verified (0 if none).";
            return;
        }
        if (!dialogs.Confirm("Close the shift with these counts? They cannot be changed afterwards.")) return;
        var shift = Shift;
        if (await RunAsync(async () => Shift = await api.PostAsync<CashierShiftDto>(api.BranchPath($"cashier/shifts/{shift.Id}/close"),
                new CloseShiftRequest(CountedCash.Value, VerifiedPos.Value, VerifiedTransfer.Value, VarianceExplanation, shift.ConcurrencyStamp))))
        {
            StatusMessage = Shift!.Status == "Closed"
                ? "Shift closed and balanced."
                : $"Shift closed with a cash variance of {Shift.CashVariance:N2}. A supervisor must approve it.";
            CountedCash = VerifiedPos = VerifiedTransfer = null;
            VarianceExplanation = null;
        }
    }
}
