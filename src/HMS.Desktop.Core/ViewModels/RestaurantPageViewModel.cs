using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.FrontDesk;
using HMS.Contracts.Restaurant;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>
/// Point of sale for an outlet: open a table or walk-in order, add dishes, send them to the kitchen, then take payment
/// or charge the bill to an in-house guest's room. The kitchen tab moves tickets from New to Served.
/// </summary>
public sealed partial class RestaurantPageViewModel(ApiClient api, SessionState session, IDialogService dialogs) : PageViewModel
{
    public static readonly string[] Tenders = ["Cash", "PosCard", "BankTransfer"];
    public static readonly string[] TicketSteps = ["Preparing", "Ready", "Served"];

    public override string Title => "Restaurant / POS";

    public ObservableCollection<OutletDto> Outlets { get; } = [];
    public ObservableCollection<TableDto> Tables { get; } = [];
    public ObservableCollection<MenuItemDto> Menu { get; } = [];
    public ObservableCollection<OrderDto> OpenOrders { get; } = [];
    public ObservableCollection<StayDto> InHouse { get; } = [];
    public ObservableCollection<KitchenTicketDto> Tickets { get; } = [];

    [ObservableProperty] private OutletDto? _outlet;
    [ObservableProperty] private TableDto? _table;
    [ObservableProperty] private int _covers = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddItemCommand), nameof(SendCommand), nameof(SettleCommand), nameof(ChargeToRoomCommand), nameof(VoidOrderCommand))]
    private OrderDto? _order;

    [ObservableProperty] private MenuItemDto? _menuItem;
    [ObservableProperty] private decimal _quantity = 1;
    [ObservableProperty] private string? _lineNotes;
    [ObservableProperty] private string _tender = "Cash";
    [ObservableProperty] private string? _paymentReference;
    [ObservableProperty] private StayDto? _roomGuest;
    [ObservableProperty] private string? _voidReason;
    [ObservableProperty] private KitchenTicketDto? _ticket;

    private string _settleKey = Guid.NewGuid().ToString();

    public bool CanTakePayment => session.HasPermission(PermissionNames.PaymentsRecord);
    public bool CanChargeRoom => session.HasPermission(PermissionNames.FoliosPost);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        if (Outlets.Count == 0)
        {
            FrontDeskPageViewModel.Replace(Outlets, (await api.GetAsync<List<OutletDto>>(api.BranchPath("restaurant/outlets"))).Where(o => o.IsActive));
            _choosingOutlet = true;
            Outlet = Outlets.FirstOrDefault();
            _choosingOutlet = false;
        }
        await LoadOutletAsync();
    });

    private bool _choosingOutlet;

    partial void OnOutletChanged(OutletDto? value)
    {
        if (!_choosingOutlet) _ = RunAsync(LoadOutletAsync);
    }

    private async Task LoadOutletAsync()
    {
        if (Outlet is null) return;
        FrontDeskPageViewModel.Replace(Tables, (await api.GetAsync<List<TableDto>>(api.BranchPath($"restaurant/outlets/{Outlet.Id}/tables"))).Where(t => t.IsActive));
        FrontDeskPageViewModel.Replace(Menu, (await api.GetAsync<List<MenuItemDto>>(api.BranchPath($"restaurant/outlets/{Outlet.Id}/menu"))).Where(m => m.IsActive));
        FrontDeskPageViewModel.Replace(OpenOrders, await api.GetAsync<List<OrderDto>>(Query.Build(api.BranchPath("restaurant/orders"), ("outletId", Outlet.Id))));
        FrontDeskPageViewModel.Replace(Tickets, await api.GetAsync<List<KitchenTicketDto>>(Query.Build(api.BranchPath("restaurant/kitchen/tickets"), ("outletId", Outlet.Id))));
        if (CanChargeRoom && session.HasPermission(PermissionNames.GuestsView))
        {
            try { FrontDeskPageViewModel.Replace(InHouse, await api.GetAsync<List<StayDto>>(api.BranchPath("front-desk/in-house"))); }
            catch (ApiException) { InHouse.Clear(); } // room charging then needs the front desk
        }
        if (Order is not null) Order = OpenOrders.FirstOrDefault(o => o.Id == Order.Id);
    }

    [RelayCommand]
    private Task NewOrderAsync()
    {
        if (Outlet is null) { ErrorMessage = "Pick an outlet."; return Task.CompletedTask; }
        if (Table?.OpenOrderId is { } existing)
        {
            Order = OpenOrders.FirstOrDefault(o => o.Id == existing);
            StatusMessage = $"Table {Table.TableNumber} already has an open order.";
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            Order = await api.PostAsync<OrderDto>(api.BranchPath("restaurant/orders"), new OpenOrderRequest(Outlet.Id, Table?.Id, null, Math.Max(1, Covers)));
            await LoadOutletAsync();
        }, "Order opened.");
    }

    private bool OrderOpen() => Order is { Status: "Open" };

    [RelayCommand(CanExecute = nameof(OrderOpen))]
    private Task AddItemAsync()
    {
        if (MenuItem is null || Quantity <= 0) { ErrorMessage = "Pick a dish and a quantity."; return Task.CompletedTask; }
        var order = Order!;
        var item = MenuItem;
        return RunAsync(async () =>
        {
            Order = await api.PostAsync<OrderDto>(api.BranchPath($"restaurant/orders/{order.Id}/lines"),
                new AddOrderLineRequest(item.Id, Quantity, string.IsNullOrWhiteSpace(LineNotes) ? null : LineNotes.Trim(), order.ConcurrencyStamp));
            Quantity = 1;
            LineNotes = null;
            _settleKey = Guid.NewGuid().ToString();
        }, $"{item.Name} added.");
    }

    [RelayCommand(CanExecute = nameof(OrderOpen))]
    private Task SendAsync() => RunAsync(async () =>
    {
        Order = await api.PostAsync<OrderDto>(api.BranchPath($"restaurant/orders/{Order!.Id}/send"), new OrderActionRequest(Order.ConcurrencyStamp));
        await LoadOutletAsync();
    }, "Sent to the kitchen.");

    [RelayCommand(CanExecute = nameof(OrderOpen))]
    private async Task SettleAsync()
    {
        if (Tender != "Cash" && string.IsNullOrWhiteSpace(PaymentReference)) { ErrorMessage = "Enter the POS slip or transfer reference."; return; }
        var order = Order!;
        if (!dialogs.Confirm($"Take {order.Total:N2} by {Tender} for order {order.OrderNumber}?")) return;
        if (await RunAsync(async () => Order = await api.PostAsync<OrderDto>(api.BranchPath($"restaurant/orders/{order.Id}/settle"),
                new SettleOrderRequest(Tender, order.Total, string.IsNullOrWhiteSpace(PaymentReference) ? null : PaymentReference.Trim(), order.ConcurrencyStamp),
                _settleKey), $"Order {order.OrderNumber} paid."))
        {
            _settleKey = Guid.NewGuid().ToString();
            PaymentReference = null;
            await RunAsync(LoadOutletAsync, $"Order {order.OrderNumber} paid.");
        }
    }

    [RelayCommand(CanExecute = nameof(OrderOpen))]
    private async Task ChargeToRoomAsync()
    {
        var order = Order!;
        var stayId = RoomGuest?.Id ?? order.StayId;
        if (stayId is null) { ErrorMessage = "Pick the in-house guest to charge."; return; }
        if (!dialogs.Confirm($"Charge {order.Total:N2} to {RoomGuest?.GuestName ?? "the guest"}'s room?")) return;
        if (await RunAsync(async () => Order = await api.PostAsync<OrderDto>(api.BranchPath($"restaurant/orders/{order.Id}/charge-to-room"),
                new ChargeToRoomRequest(stayId, order.ConcurrencyStamp))))
            await RunAsync(LoadOutletAsync, $"Order {order.OrderNumber} charged to room {RoomGuest?.RoomNumber}.");
    }

    [RelayCommand(CanExecute = nameof(OrderOpen))]
    private async Task VoidOrderAsync()
    {
        if (string.IsNullOrWhiteSpace(VoidReason)) { ErrorMessage = "Give a reason to void the order."; return; }
        var order = Order!;
        if (!dialogs.Confirm($"Void order {order.OrderNumber}?")) return;
        if (await RunAsync(async () => Order = await api.PostAsync<OrderDto>(api.BranchPath($"restaurant/orders/{order.Id}/void"),
                new VoidRequest(VoidReason.Trim(), order.ConcurrencyStamp))))
        {
            VoidReason = null;
            await RunAsync(LoadOutletAsync, "Order voided.");
        }
    }

    [RelayCommand]
    private Task AdvanceTicketAsync(string? status)
    {
        if (Ticket is null || string.IsNullOrEmpty(status)) return Task.CompletedTask;
        var ticket = Ticket;
        return RunAsync(async () =>
        {
            await api.PostAsync<KitchenTicketDto>(api.BranchPath($"restaurant/kitchen/tickets/{ticket.Id}/status"), new AdvanceTicketRequest(status, ticket.ConcurrencyStamp));
            await LoadOutletAsync();
        }, $"Ticket {ticket.TicketNumber}: {status}.");
    }
}
