using System.Net.Http.Json;
using HMS.Api.IntegrationTests;
using HMS.Application.Security;
using HMS.Contracts.Inventory;
using HMS.Contracts.Purchasing;
using HMS.Contracts.Restaurant;
using HMS.Desktop.Services;
using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Tests;

/// <summary>Restaurant, maintenance and inventory screens driven against the real API running in memory.</summary>
public sealed class OperationsScreenTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    private Task<(ApiClient Api, SessionState Session)> SignInAsync(HotelSetup hotel, string? email = null) =>
        TestDesk.SignInAsync(factory, hotel, email);

    private static async Task<T> PostAsync<T>(HotelSetup hotel, string relative, object body, bool keyed = false)
    {
        var response = keyed
            ? await HotelSetup.PostKeyedAsync(hotel.Admin, hotel.P(relative), body)
            : await hotel.Admin.PostAsJsonAsync(hotel.P(relative), body);
        Assert.True(response.IsSuccessStatusCode, $"{relative}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<(StockLocationDto Store, InventoryItemDto Rice)> StockAsync(HotelSetup hotel, decimal onHand)
    {
        var store = await PostAsync<StockLocationDto>(hotel, "inventory/locations", new SaveStockLocationRequest("STORE", "Main store"));
        var rice = await PostAsync<InventoryItemDto>(hotel, "inventory/items", new SaveInventoryItemRequest("RICE", "Rice", "kg", "Food", 5, "FoodCost"));
        await PostAsync<StockMovementDto>(hotel, "inventory/receipts", new StockReceiptRequest(rice.Id, store.Id, onHand, 1_000, "Opening balance"), keyed: true);
        return (store, rice);
    }

    private static void Ok(PageViewModel page) => TestDesk.Ok(page);

    [Fact]
    public async Task Restaurant_order_is_sent_cooked_and_paid_through_the_screen()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var (store, rice) = await StockAsync(hotel, 10);
        var outlet = await PostAsync<OutletDto>(hotel, "restaurant/outlets", new SaveOutletRequest("REST", "Main Restaurant", "Restaurant", store.Id));
        await PostAsync<TableDto>(hotel, $"restaurant/outlets/{outlet.Id}/tables", new SaveTableRequest("T1", 4));
        await PostAsync<MenuItemDto>(hotel, $"restaurant/outlets/{outlet.Id}/menu",
            new SaveMenuItemRequest(null, "JOL", "Jollof rice", 3_000, true, "Kitchen", [new IngredientRequest(rice.Id, 0.5m)]));
        var (api, session) = await SignInAsync(hotel);
        var dialogs = new YesDialogs();

        var cashier = new CashierPageViewModel(api, dialogs);
        await cashier.LoadAsync();
        await cashier.OpenShiftCommand.ExecuteAsync(null);
        Ok(cashier);

        var pos = new RestaurantPageViewModel(api, session, dialogs);
        await pos.LoadAsync();
        Ok(pos);
        Assert.Equal(outlet.Id, pos.Outlet?.Id);
        pos.Table = pos.Tables.Single();
        pos.Covers = 2;
        await pos.NewOrderCommand.ExecuteAsync(null);
        Ok(pos);
        Assert.NotNull(pos.Order);

        pos.MenuItem = pos.Menu.Single();
        pos.Quantity = 2;
        await pos.AddItemCommand.ExecuteAsync(null);
        Ok(pos);
        Assert.Equal(6_300m, pos.Order!.Total); // 6,000 plus tax
        await pos.SendCommand.ExecuteAsync(null);
        Ok(pos);
        Assert.All(pos.Order!.Lines, l => Assert.Equal("Sent", l.Status));

        // Kitchen tab: the ticket moves through to served.
        pos.Ticket = pos.Tickets.Single();
        foreach (var step in RestaurantPageViewModel.TicketSteps)
        {
            await pos.AdvanceTicketCommand.ExecuteAsync(step);
            Ok(pos);
            pos.Ticket = pos.Tickets.SingleOrDefault() ?? pos.Ticket;
        }

        pos.Tender = "Cash";
        await pos.SettleCommand.ExecuteAsync(null);
        Ok(pos);
        Assert.Empty(pos.OpenOrders);
        Assert.Null(pos.Tables.Single().OpenOrderId);
    }

    [Fact]
    public async Task Fault_becomes_a_work_order_that_is_finished_and_signed_off()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 2);
        var (api, session) = await SignInAsync(hotel);
        var page = new MaintenancePageViewModel(api, session);
        await page.LoadAsync();
        Ok(page);

        page.NewTitle = "Air conditioner leaking";
        page.NewPriority = "High";
        page.NewRoom = page.Rooms[0];
        await page.ReportCommand.ExecuteAsync(null);
        Ok(page);
        page.SelectedRequest = page.Requests.Single();
        page.TakeRoomOutOfService = true;
        page.ExpectedReady = hotel.Today.AddDays(2).ToDateTime(TimeOnly.MinValue);
        await page.AcceptCommand.ExecuteAsync(null);
        Ok(page);

        Assert.Equal(page.Rooms[0].RoomNumber, page.WorkOrders.Single().RoomNumber);
        Assert.NotNull(page.WorkOrders.Single().RoomBlockId);

        // The technician does the work on their own screen; someone else signs it off.
        var (techApi, techSession) = await SignInAsync(hotel, await hotel.CreateStaffAsync(Roles.MaintenanceTechnician));
        var tech = new MaintenancePageViewModel(techApi, techSession);
        await tech.LoadAsync();
        Ok(tech);
        Assert.False(tech.CanManage);
        tech.SelectedWorkOrder = tech.WorkOrders.Single();
        await tech.StartCommand.ExecuteAsync(null);
        Ok(tech);
        tech.SelectedWorkOrder = tech.WorkOrders.Single();
        tech.LabourHours = 2;
        tech.LabourCost = 8_000;
        tech.CompletionNotes = "Cleared the drain line";
        await tech.FinishCommand.ExecuteAsync(null);
        Ok(tech);

        await page.LoadAsync();
        page.SelectedWorkOrder = page.WorkOrders.Single();
        Assert.Equal("AwaitingInspection", page.SelectedWorkOrder.Status);
        await page.SignOffCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Empty(page.WorkOrders); // only open work orders are listed
        Assert.EndsWith("Signed off.", page.StatusMessage);
    }

    [Fact]
    public async Task Stock_is_wasted_counted_ordered_and_received_through_the_screen()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var (store, rice) = await StockAsync(hotel, 10);
        var supplier = await PostAsync<SupplierDto>(hotel, "inventory/suppliers", new SaveSupplierRequest("SUP1", "Fresh Foods Ltd", null, null, null, null, 30));
        // A clerk asks for stock; the manager on the screen approves it.
        var clerk = await hotel.StaffAsync(Roles.InventoryClerk);
        var raised = await clerk.PostAsJsonAsync(hotel.P("purchasing/requisitions"),
            new CreateRequisitionRequest([new RequisitionLineRequest(rice.Id, 20, 1_100)], "Weekend stock"));
        Assert.True(raised.IsSuccessStatusCode, await raised.Content.ReadAsStringAsync());
        var requisition = (await raised.Content.ReadFromJsonAsync<RequisitionDto>())!;
        var (api, session) = await SignInAsync(hotel);
        var page = new InventoryPageViewModel(api, session, new YesDialogs());
        await page.LoadAsync();
        Ok(page);

        page.SelectedBalance = page.Balances.Single(b => b.ItemId == rice.Id);
        page.AdjustQuantity = 1.5m;
        page.AdjustReason = "Spilled bag";
        await page.RecordWastageCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Equal(8.5m, page.Balances.Single(b => b.ItemId == rice.Id).Quantity);

        page.SelectedBalance = page.Balances.Single(b => b.ItemId == rice.Id);
        page.AdjustQuantity = 4;
        page.AdjustReason = "Monthly count";
        await page.RecordCountCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Equal(4m, page.Balances.Single(b => b.ItemId == rice.Id).Quantity);
        page.BelowReorderOnly = true;
        await page.LoadAsync();
        Assert.Contains(page.Balances, b => b.ItemId == rice.Id); // reorder level is 5

        page.SelectedRequisition = page.Requisitions.Single(r => r.Id == requisition.Id);
        await page.ApproveRequisitionCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Equal("Approved", page.Requisitions.Single(r => r.Id == requisition.Id).Status);

        var placed = await clerk.PostAsJsonAsync(hotel.P("purchasing/orders"), new CreatePurchaseOrderRequest(supplier.Id, store.Id, requisition.Id, []));
        Assert.True(placed.IsSuccessStatusCode, await placed.Content.ReadAsStringAsync());
        var order = (await placed.Content.ReadFromJsonAsync<PurchaseOrderDto>())!;
        await page.LoadAsync();
        page.SelectedOrder = page.Orders.Single(o => o.Id == order.Id);
        await page.ApproveOrderCommand.ExecuteAsync(null);
        Ok(page);
        page.SelectedOrder = page.Orders.Single(o => o.Id == order.Id);
        page.DeliveryNote = "DN-551";
        await page.ReceiveAllCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Equal("Received", page.Orders.Single(o => o.Id == order.Id).Status);
        page.BelowReorderOnly = false;
        await page.LoadAsync();
        Assert.Equal(24m, page.Balances.Single(b => b.ItemId == rice.Id).Quantity);
    }
}
