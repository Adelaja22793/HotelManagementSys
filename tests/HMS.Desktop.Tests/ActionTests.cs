using System.Net.Http.Json;
using HMS.Api.IntegrationTests;
using HMS.Application.Security;
using HMS.Contracts.Billing;
using HMS.Contracts.Inventory;
using HMS.Contracts.Purchasing;
using HMS.Contracts.Staff;
using HMS.Desktop.Services;
using HMS.Desktop.ViewModels;
using HMS.Desktop.ViewModels.Setup;

namespace HMS.Desktop.Tests;

/// <summary>The task buttons above each screen, filled in and sent to the in-memory API.</summary>
public sealed class ActionTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    private static async Task<T> PostAsync<T>(HotelSetup hotel, string relative, object body)
    {
        var response = await hotel.Admin.PostAsJsonAsync(hotel.P(relative), body);
        Assert.True(response.IsSuccessStatusCode, $"{relative}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<ActionForm> OpenAsync(PageViewModel page, ApiClient api, SessionState session, string title)
    {
        page.UseActions(api, session);
        var form = page.Action(title);
        await page.OpenActionCommand.ExecuteAsync(form);
        TestDesk.Ok(page);
        Assert.Same(form, page.OpenAction);
        return form;
    }

    private static FormField F(IEnumerable<FormField> fields, string name) => fields.Single(f => f.Name == name);

    private static void Pick(IEnumerable<FormField> fields, string name, string labelPart) =>
        F(fields, name).Selected = F(fields, name).Options.First(o => o.Label.Contains(labelPart, StringComparison.OrdinalIgnoreCase));

    private static async Task SubmitAsync(PageViewModel page)
    {
        var form = page.OpenAction!;
        await page.SubmitActionCommand.ExecuteAsync(null);
        Assert.True(form.Error is null, $"{form.Title}: {form.Error}");
        TestDesk.Ok(page);
        Assert.Null(page.OpenAction);
    }

    [Fact]
    public void Every_task_form_can_be_built()
    {
        var all = typeof(ActionCatalog).GetProperties().Where(p => p.PropertyType == typeof(IReadOnlyList<ActionDefinition>))
            .SelectMany(p => (IReadOnlyList<ActionDefinition>)p.GetValue(null)!).ToList();
        Assert.True(all.Count >= 24, $"Only {all.Count} tasks.");
        foreach (var d in all)
        {
            var fields = FormBuilder.BuildFields(d.RequestType, d.Fields, null, new Dictionary<string, IReadOnlyList<FieldOption>>());
            foreach (var p in FormBuilder.Constructor(d.RequestType).GetParameters())
            {
                var name = char.ToUpperInvariant(p.Name![0]) + p.Name[1..];
                if ((Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType) == typeof(Guid) && name != "ConcurrencyStamp")
                    Assert.True(d.Fields.TryGetValue(name, out var spec) && (spec.Lookup is not null || spec.TargetId || spec.FromTarget is not null),
                        $"{d.Title}: {name} needs a pick list.");
            }
            if (FormBuilder.ListParameter(d.RequestType) is { } list)
                foreach (var p in FormBuilder.Constructor(list.ItemType).GetParameters())
                    if (p.ParameterType == typeof(Guid))
                        Assert.True(d.LineFields.TryGetValue(char.ToUpperInvariant(p.Name![0]) + p.Name[1..], out var spec) && (spec.Lookup ?? (object?)spec.FromTarget) is not null,
                            $"{d.Title}: line {p.Name} needs a pick list.");
            Assert.NotNull(fields);
        }
    }

    [Fact]
    public async Task Stock_is_received_moved_bought_invoiced_and_paid_from_the_inventory_screen()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var store = await PostAsync<StockLocationDto>(hotel, "inventory/locations", new SaveStockLocationRequest("MAIN", "Main store"));
        var kitchen = await PostAsync<StockLocationDto>(hotel, "inventory/locations", new SaveStockLocationRequest("KIT", "Kitchen"));
        await PostAsync<InventoryItemDto>(hotel, "inventory/items", new SaveInventoryItemRequest("RICE", "Rice", "kg", "Food", 5, "FoodCost"));
        var supplier = await PostAsync<SupplierDto>(hotel, "inventory/suppliers", new SaveSupplierRequest("AGRO", "Agro Foods", null, null, null, null, 30));
        var (api, session) = await TestDesk.SignInAsync(factory, hotel);
        var inventory = new InventoryPageViewModel(api, session, new YesDialogs());
        await inventory.LoadAsync();
        Assert.Empty(inventory.Balances);

        // Receive opening stock.
        var receive = await OpenAsync(inventory, api, session, "Receive stock");
        Pick(receive.Fields, "ItemId", "RICE");
        Pick(receive.Fields, "LocationId", "Main store");
        F(receive.Fields, "Quantity").Text = "20";
        F(receive.Fields, "UnitCost").Text = "1500";
        await SubmitAsync(inventory);
        Assert.Equal("Stock received.", inventory.StatusMessage);
        Assert.Equal(20, inventory.Balances.Single().Quantity);

        // Move some to the kitchen; the same store twice is refused before sending.
        var move = await OpenAsync(inventory, api, session, "Move stock");
        Pick(move.Fields, "ItemId", "RICE");
        Pick(move.Fields, "FromLocationId", "Main store");
        Pick(move.Fields, "ToLocationId", "Main store");
        F(move.Fields, "Quantity").Text = "5";
        await inventory.SubmitActionCommand.ExecuteAsync(null);
        Assert.Equal("Choose two different stores.", move.Error);
        Pick(move.Fields, "ToLocationId", "Kitchen");
        await SubmitAsync(inventory);
        Assert.Equal(5, inventory.Balances.Single(b => b.LocationId == kitchen.Id).Quantity);

        // Requisition with a line, approved by someone else (segregation of duties), then ordered.
        var requisition = await OpenAsync(inventory, api, session, "New requisition");
        await inventory.SubmitActionCommand.ExecuteAsync(null);
        Assert.Contains("at least one line", requisition.Error);
        Pick(requisition.LineFields, "ItemId", "RICE");
        F(requisition.LineFields, "Quantity").Text = "50";
        F(requisition.LineFields, "EstimatedUnitCost").Text = "1400";
        requisition.AddLineCommand.Execute(null);
        Assert.Single(requisition.Lines);
        await SubmitAsync(inventory);
        var raised = Assert.Single(inventory.Requisitions);
        var approver = await hotel.StaffAsync(Roles.HotelManager);
        var decided = await approver.PostAsJsonAsync(hotel.P($"purchasing/requisitions/{raised.Id}/decision"), new DecisionRequest(true, null, raised.ConcurrencyStamp));
        Assert.True(decided.IsSuccessStatusCode, await decided.Content.ReadAsStringAsync());
        await inventory.LoadAsync();

        var order = await OpenAsync(inventory, api, session, "New purchase order");
        Pick(order.Fields, "SupplierId", "Agro");
        Pick(order.Fields, "DeliveryLocationId", "Main store");
        Pick(order.Fields, "RequisitionId", raised.RequisitionNumber);
        await SubmitAsync(inventory);
        var po = Assert.Single(inventory.Orders);
        Assert.Equal(supplier.Id, po.SupplierId);
        Assert.Equal(50, po.Lines.Single().Quantity);

        // Approve and receive with the existing screen buttons, then invoice and pay with the new tasks.
        var approved = await approver.PostAsJsonAsync(hotel.P($"purchasing/orders/{po.Id}/approve"), new PurchaseOrderActionRequest(po.ConcurrencyStamp));
        Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
        po = await hotel.GetAsync<PurchaseOrderDto>($"purchasing/orders/{po.Id}");
        var received = await HotelSetup.PostKeyedAsync(hotel.Admin, hotel.P($"purchasing/orders/{po.Id}/receipts"),
            new ReceiveGoodsRequest([new GoodsReceiptLineRequest(po.Lines.Single().Id, 50)], "DN-1"));
        Assert.True(received.IsSuccessStatusCode, await received.Content.ReadAsStringAsync());

        var invoice = await OpenAsync(inventory, api, session, "Record supplier invoice");
        F(invoice.Fields, "__target").Selected = F(invoice.Fields, "__target").Options.Single();
        F(invoice.Fields, "SupplierReference").Text = "AGRO-77";
        F(invoice.LineFields, "PurchaseOrderLineId").Selected = F(invoice.LineFields, "PurchaseOrderLineId").Options.Single();
        F(invoice.LineFields, "Quantity").Text = "50";
        F(invoice.LineFields, "UnitPrice").Text = po.Lines.Single().UnitCost.ToString(System.Globalization.CultureInfo.CurrentCulture);
        await SubmitAsync(inventory); // one filled-in line is added for the user

        var pay = await OpenAsync(inventory, api, session, "Pay supplier");
        var unpaid = F(pay.Fields, "__target").Options.Single();
        F(pay.Fields, "__target").Selected = unpaid;
        var outstanding = ((SupplierInvoiceDto)unpaid.Record!).Outstanding;
        Assert.True(outstanding > 0);
        F(pay.Fields, "Amount").Text = outstanding.ToString(System.Globalization.CultureInfo.CurrentCulture);
        F(pay.Fields, "Reference").Text = "TRF-1001";
        await SubmitAsync(inventory);
        var paid = (await hotel.GetAsync<List<SupplierInvoiceDto>>("purchasing/invoices")).Single();
        Assert.Equal("Paid", paid.Status);
    }

    [Fact]
    public async Task Rooms_staff_housekeeping_maintenance_and_account_tasks_work()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 2);
        var (api, session) = await TestDesk.SignInAsync(factory, hotel);
        var room = hotel.Rooms[0].RoomNumber;

        var rooms = new RoomsPageViewModel(api);
        var block = await OpenAsync(rooms, api, session, "Block a room");
        Pick(block.Fields, "RoomId", room);
        F(block.Fields, "EndDate").Date = hotel.Today.AddDays(2).ToDateTime(TimeOnly.MinValue);
        F(block.Fields, "Reason").Text = "Leaking pipe";
        await SubmitAsync(rooms);
        var release = await OpenAsync(rooms, api, session, "Release a room block");
        F(release.Fields, "__target").Selected = F(release.Fields, "__target").Options.Single();
        await SubmitAsync(rooms);
        var limit = await OpenAsync(rooms, api, session, "Limit rooms for sale");
        Pick(limit.Fields, "RoomTypeId", hotel.Standard.Code);
        F(limit.Fields, "SellLimit").Text = "1";
        await SubmitAsync(rooms);

        var housekeeping = new HousekeepingPageViewModel(api, session);
        var task = await OpenAsync(housekeeping, api, session, "New cleaning task");
        Pick(task.Fields, "RoomId", room);
        await SubmitAsync(housekeeping);
        Assert.Contains(housekeeping.Tasks, t => t.RoomNumber == room && t.Type == "Touchup");

        var maintenance = new MaintenancePageViewModel(api, session);
        var order = await OpenAsync(maintenance, api, session, "New work order");
        F(order.Fields, "Title").Text = "Fix the shower";
        Pick(order.Fields, "RoomId", room);
        await SubmitAsync(maintenance);
        Assert.Contains(maintenance.WorkOrders, w => w.Title == "Fix the shower");

        var department = await PostAsync<DepartmentDto>(hotel, "staff/departments", new SaveDepartmentRequest("FO", "Front office"));
        await PostAsync<EmployeeDto>(hotel, "staff/employees",
            new SaveEmployeeRequest("E1", "Ada", "Obi", department.Id, "Receptionist", null, null, null, hotel.Today));
        var staff = new StaffPageViewModel(api, session, new YesDialogs());
        var overtime = await OpenAsync(staff, api, session, "Request overtime");
        Pick(overtime.Fields, "EmployeeId", "Ada");
        F(overtime.Fields, "Hours").Text = "2";
        F(overtime.Fields, "Reason").Text = "Conference check-ins";
        await SubmitAsync(staff);
        // Someone else approves it (segregation of duties).
        var (managerApi, managerSession) = await TestDesk.SignInAsync(factory, hotel, await hotel.CreateStaffAsync(Roles.HotelManager));
        var managerStaff = new StaffPageViewModel(managerApi, managerSession, new YesDialogs());
        var decide = await OpenAsync(managerStaff, managerApi, managerSession, "Approve or reject overtime");
        F(decide.Fields, "__target").Selected = F(decide.Fields, "__target").Options.Single();
        await SubmitAsync(managerStaff);
        Assert.Equal("Approved", Assert.Single(managerStaff.Overtime).Status);

        var setup = new SetupPageViewModel(api, session);
        var online = await OpenAsync(setup, api, session, "Online booking on or off");
        F(online.Fields, "Enabled").Flag = true;
        await SubmitAsync(setup);

        var dashboard = new DashboardPageViewModel(api, session);
        var password = await OpenAsync(dashboard, api, session, "Change my password");
        F(password.Fields, "CurrentPassword").Text = HmsApiFactory.AdminPassword;
        F(password.Fields, "NewPassword").Text = "short";
        await dashboard.SubmitActionCommand.ExecuteAsync(null);
        Assert.Contains("12 characters", password.Error);
    }

    [Fact]
    public async Task Refunds_are_approved_and_paid_out_from_the_folio_screen()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var booking = await hotel.BookAsync(nights: 1);
        var (api, session) = await TestDesk.SignInAsync(factory, hotel);
        await hotel.OpenShiftAsync(hotel.Admin);
        var folio = await hotel.GetAsync<FolioDto>($"reservations/{booking.Id}/folio");
        var paid = await hotel.PayAsync(hotel.Admin, folio.Id, 10_000);
        Assert.True(paid.IsSuccessStatusCode, await paid.Content.ReadAsStringAsync());
        var requester = await hotel.StaffAsync(Roles.Receptionist);
        var asked = await requester.PostAsJsonAsync(hotel.P($"folios/{folio.Id}/refunds"), new RequestRefundRequest(null, 4_000, "Cash", "Overcharged"));
        Assert.True(asked.IsSuccessStatusCode, await asked.Content.ReadAsStringAsync());

        var folios = new FolioPageViewModel(api, session, new YesDialogs());
        var approve = await OpenAsync(folios, api, session, "Approve or reject a refund");
        F(approve.Fields, "__target").Selected = F(approve.Fields, "__target").Options.Single();
        await SubmitAsync(folios);
        var payout = await OpenAsync(folios, api, session, "Pay out a refund");
        F(payout.Fields, "__target").Selected = F(payout.Fields, "__target").Options.Single();
        await SubmitAsync(folios);
        Assert.Equal("Executed", (await hotel.GetAsync<List<RefundDto>>("refunds")).Single().Status);
    }
}
