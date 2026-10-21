using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Inventory;
using HMS.Contracts.Purchasing;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Stock on hand by location, movements, wastage and counts; requisitions and purchase orders with approval and receiving.</summary>
public sealed partial class InventoryPageViewModel(ApiClient api, SessionState session, IDialogService dialogs) : PageViewModel
{
    public override string Title => "Inventory and purchasing";

    public ObservableCollection<StockBalanceDto> Balances { get; } = [];
    public ObservableCollection<StockMovementDto> Movements { get; } = [];
    public ObservableCollection<RequisitionDto> Requisitions { get; } = [];
    public ObservableCollection<PurchaseOrderDto> Orders { get; } = [];

    [ObservableProperty] private bool _belowReorderOnly;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecordWastageCommand), nameof(RecordCountCommand))]
    private StockBalanceDto? _selectedBalance;

    [ObservableProperty] private decimal? _adjustQuantity;
    [ObservableProperty] private string? _adjustReason;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveRequisitionCommand), nameof(RejectRequisitionCommand))]
    private RequisitionDto? _selectedRequisition;

    [ObservableProperty] private string? _rejectReason;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveOrderCommand), nameof(ReceiveAllCommand))]
    private PurchaseOrderDto? _selectedOrder;

    [ObservableProperty] private string? _deliveryNote;

    public bool CanManageStock => session.HasPermission(PermissionNames.InventoryManage);
    public bool CanApprove => session.HasPermission(PermissionNames.PurchasingApprove);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        FrontDeskPageViewModel.Replace(Balances, await api.GetAsync<List<StockBalanceDto>>(
            Query.Build(api.BranchPath("inventory/balances"), ("belowReorderOnly", BelowReorderOnly))));
        FrontDeskPageViewModel.Replace(Requisitions, await api.GetAsync<List<RequisitionDto>>(api.BranchPath("purchasing/requisitions")));
        FrontDeskPageViewModel.Replace(Orders, await api.GetAsync<List<PurchaseOrderDto>>(api.BranchPath("purchasing/orders")));
    });

    partial void OnBelowReorderOnlyChanged(bool value) => _ = LoadAsync();

    async partial void OnSelectedBalanceChanged(StockBalanceDto? value)
    {
        Movements.Clear();
        if (value is null) return;
        await RunAsync(async () => FrontDeskPageViewModel.Replace(Movements, await api.GetAsync<List<StockMovementDto>>(
            Query.Build(api.BranchPath("inventory/movements"), ("itemId", value.ItemId)))));
    }

    private bool HasBalance() => CanManageStock && SelectedBalance is not null;

    [RelayCommand(CanExecute = nameof(HasBalance))]
    private async Task RecordWastageAsync()
    {
        if (AdjustQuantity is not > 0 || string.IsNullOrWhiteSpace(AdjustReason)) { ErrorMessage = "Enter the quantity wasted and why."; return; }
        var b = SelectedBalance!;
        if (await RunAsync(() => api.PostAsync<StockMovementDto>(api.BranchPath("inventory/wastage"),
                new StockWastageRequest(b.ItemId, b.LocationId, AdjustQuantity.Value, AdjustReason.Trim()), Guid.NewGuid().ToString())))
            await AfterAdjustment($"Wastage of {AdjustQuantity:N2} {b.Unit} {b.ItemName} recorded.");
    }

    [RelayCommand(CanExecute = nameof(HasBalance))]
    private async Task RecordCountAsync()
    {
        if (AdjustQuantity is not >= 0 || string.IsNullOrWhiteSpace(AdjustReason)) { ErrorMessage = "Enter the counted quantity and a note."; return; }
        var b = SelectedBalance!;
        if (await RunAsync(() => api.PostAsync<StockMovementDto?>(api.BranchPath("inventory/counts"),
                new StockCountRequest(b.ItemId, b.LocationId, AdjustQuantity.Value, null, AdjustReason.Trim()), Guid.NewGuid().ToString())))
            await AfterAdjustment($"Count of {b.ItemName} recorded.");
    }

    private async Task AfterAdjustment(string message)
    {
        AdjustQuantity = null;
        AdjustReason = null;
        await LoadAsync();
        StatusMessage = message;
    }

    private bool RequisitionWaiting() => CanApprove && SelectedRequisition?.Status == "Submitted";

    [RelayCommand(CanExecute = nameof(RequisitionWaiting))]
    private Task ApproveRequisitionAsync() => Decide(true);

    [RelayCommand(CanExecute = nameof(RequisitionWaiting))]
    private Task RejectRequisitionAsync()
    {
        if (string.IsNullOrWhiteSpace(RejectReason)) { ErrorMessage = "Give a reason for rejecting it."; return Task.CompletedTask; }
        return Decide(false);
    }

    private async Task Decide(bool approved)
    {
        var r = SelectedRequisition!;
        if (await RunAsync(() => api.PostAsync<RequisitionDto>(api.BranchPath($"purchasing/requisitions/{r.Id}/decision"),
                new DecisionRequest(approved, approved ? null : RejectReason, r.ConcurrencyStamp))))
        {
            RejectReason = null;
            await LoadAsync();
            StatusMessage = $"{r.RequisitionNumber} {(approved ? "approved" : "rejected")}.";
        }
    }

    private bool OrderDraft() => CanApprove && SelectedOrder?.Status == "Draft";
    private bool OrderReceivable() => CanManageStock && SelectedOrder?.Status is "Approved" or "PartiallyReceived";

    [RelayCommand(CanExecute = nameof(OrderDraft))]
    private async Task ApproveOrderAsync()
    {
        var o = SelectedOrder!;
        if (await RunAsync(() => api.PostAsync<PurchaseOrderDto>(api.BranchPath($"purchasing/orders/{o.Id}/approve"), new PurchaseOrderActionRequest(o.ConcurrencyStamp))))
        {
            await LoadAsync();
            StatusMessage = $"{o.OrderNumber} approved.";
        }
    }

    /// <summary>Receives everything still outstanding on the order. Partial deliveries are recorded line by line in the API.</summary>
    [RelayCommand(CanExecute = nameof(OrderReceivable))]
    private async Task ReceiveAllAsync()
    {
        var o = SelectedOrder!;
        var lines = o.Lines.Where(l => l.Quantity > l.ReceivedQuantity).Select(l => new GoodsReceiptLineRequest(l.Id, l.Quantity - l.ReceivedQuantity)).ToList();
        if (lines.Count == 0) { StatusMessage = "Everything on this order has been received."; return; }
        if (!dialogs.Confirm($"Receive all outstanding items on {o.OrderNumber} from {o.SupplierName}?")) return;
        if (await RunAsync(() => api.PostAsync<GoodsReceiptDto>(api.BranchPath($"purchasing/orders/{o.Id}/receipts"),
                new ReceiveGoodsRequest(lines, DeliveryNote), Guid.NewGuid().ToString())))
        {
            DeliveryNote = null;
            await LoadAsync();
            StatusMessage = $"{o.OrderNumber} received into stock.";
        }
    }
}
