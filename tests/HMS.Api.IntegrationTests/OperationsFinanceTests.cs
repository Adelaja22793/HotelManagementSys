using System.Net;
using System.Net.Http.Json;
using HMS.Application.Security;
using HMS.Contracts.Accounting;
using HMS.Contracts.FrontDesk;
using HMS.Contracts.Inventory;
using HMS.Contracts.Maintenance;
using HMS.Contracts.Purchasing;
using HMS.Contracts.Reservations;
using HMS.Contracts.Restaurant;
using HMS.Contracts.Staff;
using static HMS.Api.IntegrationTests.HotelSetup;

namespace HMS.Api.IntegrationTests;

/// <summary>Phase 3: inventory, purchasing, restaurant, maintenance, staff and the general ledger.</summary>
public class OperationsFinanceTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    // ---------- helpers ----------

    private static async Task<(StockLocationDto Location, InventoryItemDto Item)> StockAsync(HotelSetup hotel, string sku = "RICE",
        decimal quantity = 0, decimal unitCost = 0, string expenseKey = "FoodCost")
    {
        var location = await ApiAssert.OkAsync<StockLocationDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("inventory/locations"),
            new SaveStockLocationRequest("LOC" + Guid.NewGuid().ToString("N")[..4], "Store")), HttpStatusCode.Created);
        var item = await ApiAssert.OkAsync<InventoryItemDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("inventory/items"),
            new SaveInventoryItemRequest(sku, sku, "kg", "Food", 5, expenseKey)), HttpStatusCode.Created);
        if (quantity > 0)
        {
            await ApiAssert.OkAsync<StockMovementDto>(await PostKeyedAsync(hotel.Admin, hotel.P("inventory/receipts"),
                new StockReceiptRequest(item.Id, location.Id, quantity, unitCost, "Opening balance")));
        }
        return (location, item);
    }

    private static async Task<TrialBalanceDto> TrialBalanceAsync(HotelSetup hotel) =>
        await hotel.GetAsync<TrialBalanceDto>($"accounting/trial-balance?from={hotel.Today.AddYears(-5):yyyy-MM-dd}&to={hotel.Today.AddYears(1):yyyy-MM-dd}");

    /// <summary>Signed debit-minus-credit balance of one account code in the branch trial balance.</summary>
    private static decimal Net(TrialBalanceDto tb, string code) =>
        tb.Lines.Where(l => l.Code == code).Sum(l => l.Debit - l.Credit);

    private static void AssertBalanced(TrialBalanceDto tb)
    {
        Assert.Equal(tb.TotalDebit, tb.TotalCredit);
        Assert.Equal(tb.Lines.Sum(l => l.Debit), tb.Lines.Sum(l => l.Credit));
    }

    // ---------- ledger from Phase 2 flows ----------

    [Fact]
    public async Task Guest_stay_posts_balanced_journals()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var booking = await hotel.BookAsync(nights: 2);
        var stay = await ApiAssert.OkAsync<StayDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("front-desk/check-in"),
            new CheckInRequest(booking.Id, hotel.Rooms[0].Id, booking.ConcurrencyStamp)));
        await hotel.OpenShiftAsync(hotel.Admin);
        await ApiAssert.OkAsync<HMS.Contracts.Billing.PaymentDto>(await hotel.PayAsync(hotel.Admin, stay.FolioId!.Value, 50_000));

        var tb = await TrialBalanceAsync(hotel);
        AssertBalanced(tb);
        Assert.Equal(-100_000m, Net(tb, "4000")); // two nights of room revenue
        Assert.Equal(-5_000m, Net(tb, "2200"));   // 5% VAT
        Assert.Equal(50_000m, Net(tb, "1000"));   // cash
        Assert.Equal(55_000m, Net(tb, "1100"));   // still owed by the guest
    }

    [Fact]
    public async Task Manual_journals_balance_avoid_control_accounts_and_reverse_once()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var accounts = await hotel.GetAsync<List<AccountDto>>("accounting/accounts");
        var cash = accounts.Single(a => a.Code == "1000");
        var equity = accounts.Single(a => a.Code == "3000");
        var ar = accounts.Single(a => a.Code == "1100");

        var unbalanced = await hotel.Admin.PostAsJsonAsync(hotel.P("accounting/journals"), new CreateJournalRequest(hotel.Today, "Bad",
            [new JournalLineRequest(cash.Id, 100, 0, null), new JournalLineRequest(equity.Id, 0, 90, null)]));
        await ApiAssert.ErrorAsync(unbalanced, HttpStatusCode.BadRequest, "JOURNAL_UNBALANCED");

        var control = await hotel.Admin.PostAsJsonAsync(hotel.P("accounting/journals"), new CreateJournalRequest(hotel.Today, "Into AR",
            [new JournalLineRequest(ar.Id, 100, 0, null), new JournalLineRequest(equity.Id, 0, 100, null)]));
        await ApiAssert.ErrorAsync(control, HttpStatusCode.Conflict, "CONTROL_ACCOUNT");

        var journal = await ApiAssert.OkAsync<JournalEntryDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("accounting/journals"),
            new CreateJournalRequest(hotel.Today, "Owner float", [new JournalLineRequest(cash.Id, 10_000, 0, null), new JournalLineRequest(equity.Id, 0, 10_000, null)])),
            HttpStatusCode.Created);
        Assert.StartsWith($"JE-{hotel.Branch.Code}-", journal.EntryNumber);
        Assert.Equal(10_000m, Net(await TrialBalanceAsync(hotel), "1000"));

        var reversal = await ApiAssert.OkAsync<JournalEntryDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"accounting/journals/{journal.Id}/reverse"),
            new ReverseJournalRequest("Posted in error")));
        Assert.Equal(journal.Id, reversal.ReversesEntryId);
        await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P($"accounting/journals/{journal.Id}/reverse"),
            new ReverseJournalRequest("Again")), HttpStatusCode.Conflict, "JOURNAL_ALREADY_REVERSED");
        Assert.Equal(0m, Net(await TrialBalanceAsync(hotel), "1000"));

        // A receptionist cannot see or post to the ledger.
        var reception = await hotel.StaffAsync(Roles.Receptionist);
        Assert.Equal(HttpStatusCode.Forbidden, (await reception.GetAsync(hotel.P("accounting/journals"))).StatusCode);
    }

    [Fact]
    public async Task Closed_period_rejects_postings_until_reopened()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var accounts = await hotel.GetAsync<List<AccountDto>>("accounting/accounts");
        var cash = accounts.Single(a => a.Code == "1000");
        var equity = accounts.Single(a => a.Code == "3000");
        // A month far in the past, so other tests in this company never post into it.
        var oldDate = new DateOnly(hotel.Today.Year - 3, 6, 15);
        CreateJournalRequest Journal() => new(oldDate, "Prior-year correction",
            [new JournalLineRequest(cash.Id, 1, 0, null), new JournalLineRequest(equity.Id, 0, 1, null)]);

        await ApiAssert.OkAsync<JournalEntryDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("accounting/journals"), Journal()), HttpStatusCode.Created);
        var period = (await hotel.GetAsync<List<AccountingPeriodDto>>("accounting/periods")).Single(p => p.Year == oldDate.Year && p.Month == 6);
        Assert.Equal("Open", period.Status);

        var current = (await hotel.GetAsync<List<AccountingPeriodDto>>("accounting/periods"))
            .FirstOrDefault(p => p.Year == hotel.Today.Year && p.Month == hotel.Today.Month);
        if (current is not null)
        {
            await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P($"accounting/periods/{current.Id}/close"),
                new PeriodActionRequest(current.ConcurrencyStamp)), HttpStatusCode.Conflict, "PERIOD_NOT_ENDED");
        }

        period = await ApiAssert.OkAsync<AccountingPeriodDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"accounting/periods/{period.Id}/close"),
            new PeriodActionRequest(period.ConcurrencyStamp)));
        try
        {
            Assert.Equal("Closed", period.Status);
            await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P("accounting/journals"), Journal()), HttpStatusCode.Conflict, "PERIOD_CLOSED");
        }
        finally
        {
            period = await ApiAssert.OkAsync<AccountingPeriodDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"accounting/periods/{period.Id}/reopen"),
                new PeriodActionRequest(period.ConcurrencyStamp)));
        }
        await ApiAssert.OkAsync<JournalEntryDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("accounting/journals"), Journal()), HttpStatusCode.Created);
    }

    // ---------- inventory ----------

    [Fact]
    public async Task Stock_uses_weighted_average_cost_and_never_goes_negative()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var (store, rice) = await StockAsync(hotel, "RICE", 10, 1_000);
        await ApiAssert.OkAsync<StockMovementDto>(await PostKeyedAsync(hotel.Admin, hotel.P("inventory/receipts"),
            new StockReceiptRequest(rice.Id, store.Id, 10, 1_200, "Donation")));
        var bar = await ApiAssert.OkAsync<StockLocationDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("inventory/locations"),
            new SaveStockLocationRequest("BAR", "Bar store")), HttpStatusCode.Created);

        var balance = (await hotel.GetAsync<List<StockBalanceDto>>("inventory/balances")).Single(b => b.ItemId == rice.Id);
        Assert.Equal(20m, balance.Quantity);
        Assert.Equal(1_100m, balance.AverageCost);

        // Transfer keeps the cost; wastage is costed at average.
        var transferKey = Guid.NewGuid().ToString();
        for (var i = 0; i < 2; i++)
        {
            await ApiAssert.OkAsync<List<StockMovementDto>>(await PostKeyedAsync(hotel.Admin, hotel.P("inventory/transfers"),
                new StockTransferRequest(rice.Id, store.Id, bar.Id, 5, null), transferKey));
        }
        var waste = await ApiAssert.OkAsync<StockMovementDto>(await PostKeyedAsync(hotel.Admin, hotel.P("inventory/wastage"),
            new StockWastageRequest(rice.Id, store.Id, 2, "Spoiled")));
        Assert.Equal(-2_200m, waste.TotalCost);

        var tooMuch = await PostKeyedAsync(hotel.Admin, hotel.P("inventory/wastage"), new StockWastageRequest(rice.Id, store.Id, 50, "Bad count"));
        await ApiAssert.ErrorAsync(tooMuch, HttpStatusCode.Conflict, "INSUFFICIENT_STOCK");

        var balances = await hotel.GetAsync<List<StockBalanceDto>>("inventory/balances");
        Assert.Equal(13m, balances.Single(b => b.LocationId == store.Id && b.ItemId == rice.Id).Quantity);
        Assert.Equal(5m, balances.Single(b => b.LocationId == bar.Id && b.ItemId == rice.Id).Quantity);

        // Count down by one: shrinkage posts to inventory adjustments.
        await ApiAssert.OkAsync<StockMovementDto>(await PostKeyedAsync(hotel.Admin, hotel.P("inventory/counts"),
            new StockCountRequest(rice.Id, store.Id, 12, null, "Monthly count")));

        var tb = await TrialBalanceAsync(hotel);
        AssertBalanced(tb);
        Assert.Equal(17m * 1_100m, Net(tb, "1200"));
        Assert.Equal(3_300m, Net(tb, "5600")); // wastage 2,200 + shrinkage 1,100
    }

    [Fact]
    public async Task Concurrent_issues_cannot_overdraw_stock()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var (store, rice) = await StockAsync(hotel, "RICE", 10, 1_000);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            PostKeyedAsync(hotel.Admin, hotel.P("inventory/wastage"), new StockWastageRequest(rice.Id, store.Id, 6, "Spoiled"))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(4m, (await hotel.GetAsync<List<StockBalanceDto>>("inventory/balances")).Single(b => b.ItemId == rice.Id).Quantity);
        var tb = await TrialBalanceAsync(hotel);
        AssertBalanced(tb);
        Assert.Equal(4_000m, Net(tb, "1200"));
    }

    // ---------- purchasing ----------

    [Fact]
    public async Task Purchase_to_pay_with_price_discrepancy()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var (store, oil) = await StockAsync(hotel, "OIL");
        var (clerk, _) = await hotel.StaffWithIdAsync(Roles.InventoryClerk);
        var manager = await hotel.StaffAsync(Roles.HotelManager);
        var accountant = await hotel.StaffAsync(Roles.Accountant);
        var supplier = await ApiAssert.OkAsync<SupplierDto>(await clerk.PostAsJsonAsync(hotel.P("inventory/suppliers"),
            new SaveSupplierRequest("SUP1", "Fresh Foods Ltd", null, null, null, null, 30)), HttpStatusCode.Created);

        // Requisition -> approved by someone else -> purchase order
        var req = await ApiAssert.OkAsync<RequisitionDto>(await clerk.PostAsJsonAsync(hotel.P("purchasing/requisitions"),
            new CreateRequisitionRequest([new RequisitionLineRequest(oil.Id, 10, 500)], "Weekly")), HttpStatusCode.Created);
        req = await ApiAssert.OkAsync<RequisitionDto>(await manager.PostAsJsonAsync(hotel.P($"purchasing/requisitions/{req.Id}/decision"),
            new DecisionRequest(true, null, req.ConcurrencyStamp)));
        Assert.Equal("Approved", req.Status);

        var po = await ApiAssert.OkAsync<PurchaseOrderDto>(await clerk.PostAsJsonAsync(hotel.P("purchasing/orders"),
            new CreatePurchaseOrderRequest(supplier.Id, store.Id, req.Id, [new PurchaseOrderLineRequest(oil.Id, 10, 500)])), HttpStatusCode.Created);
        Assert.Equal(5_000m, po.Total);

        // The clerk who raised it cannot receive goods against an unapproved order, nor approve it.
        await ApiAssert.ErrorAsync(await PostKeyedAsync(clerk, hotel.P($"purchasing/orders/{po.Id}/receipts"),
            new ReceiveGoodsRequest([new GoodsReceiptLineRequest(po.Lines[0].Id, 10)], null)), HttpStatusCode.Conflict, "PO_STATE");
        po = await ApiAssert.OkAsync<PurchaseOrderDto>(await manager.PostAsJsonAsync(hotel.P($"purchasing/orders/{po.Id}/approve"),
            new PurchaseOrderActionRequest(po.ConcurrencyStamp)));

        // Partial receipt (retry is safe), then over-receipt is refused.
        var grnKey = Guid.NewGuid().ToString();
        var grn = await ApiAssert.OkAsync<GoodsReceiptDto>(await PostKeyedAsync(clerk, hotel.P($"purchasing/orders/{po.Id}/receipts"),
            new ReceiveGoodsRequest([new GoodsReceiptLineRequest(po.Lines[0].Id, 6)], "DN-1"), grnKey));
        var grnRetry = await ApiAssert.OkAsync<GoodsReceiptDto>(await PostKeyedAsync(clerk, hotel.P($"purchasing/orders/{po.Id}/receipts"),
            new ReceiveGoodsRequest([new GoodsReceiptLineRequest(po.Lines[0].Id, 6)], "DN-1"), grnKey));
        Assert.Equal(grn.Id, grnRetry.Id);
        await ApiAssert.ErrorAsync(await PostKeyedAsync(clerk, hotel.P($"purchasing/orders/{po.Id}/receipts"),
            new ReceiveGoodsRequest([new GoodsReceiptLineRequest(po.Lines[0].Id, 5)], null)), HttpStatusCode.Conflict, "OVER_RECEIPT");
        await ApiAssert.OkAsync<GoodsReceiptDto>(await PostKeyedAsync(clerk, hotel.P($"purchasing/orders/{po.Id}/receipts"),
            new ReceiveGoodsRequest([new GoodsReceiptLineRequest(po.Lines[0].Id, 4)], "DN-2")));
        Assert.Equal("Received", (await hotel.GetAsync<PurchaseOrderDto>($"purchasing/orders/{po.Id}")).Status);
        Assert.Equal(10m, (await hotel.GetAsync<List<StockBalanceDto>>("inventory/balances")).Single(b => b.ItemId == oil.Id).Quantity);
        Assert.Equal(5_000m, (await hotel.GetAsync<List<UninvoicedReceiptDto>>("purchasing/uninvoiced-receipts")).Sum(u => u.UninvoicedValue));

        // Supplier bills 520 a unit: discrepancy, cannot be paid until someone else approves it.
        var invoice = await ApiAssert.OkAsync<SupplierInvoiceDto>(await clerk.PostAsJsonAsync(hotel.P("purchasing/invoices"),
            new CreateSupplierInvoiceRequest(po.Id, "FF-1001", hotel.Today, [new SupplierInvoiceLineRequest(po.Lines[0].Id, 10, 520)])), HttpStatusCode.Created);
        Assert.Equal("Discrepancy", invoice.Status);
        Assert.Equal(5_200m, invoice.Total);
        await ApiAssert.ErrorAsync(await clerk.PostAsJsonAsync(hotel.P("purchasing/invoices"),
            new CreateSupplierInvoiceRequest(po.Id, "FF-1001", hotel.Today, [new SupplierInvoiceLineRequest(po.Lines[0].Id, 1, 520)])),
            HttpStatusCode.Conflict, "DUPLICATE_SUPPLIER_INVOICE");
        await ApiAssert.ErrorAsync(await PostKeyedAsync(accountant, hotel.P($"purchasing/invoices/{invoice.Id}/payments"),
            new PaySupplierRequest(5_200, "BankTransfer", "TRF-1")), HttpStatusCode.Conflict, "INVOICE_STATE");
        invoice = await ApiAssert.OkAsync<SupplierInvoiceDto>(await manager.PostAsJsonAsync(hotel.P($"purchasing/invoices/{invoice.Id}/approve"),
            new PurchaseOrderActionRequest(invoice.ConcurrencyStamp)));
        Assert.Equal("Approved", invoice.Status);

        // Pay in two parts; overpayment refused.
        await ApiAssert.OkAsync<SupplierPaymentDto>(await PostKeyedAsync(accountant, hotel.P($"purchasing/invoices/{invoice.Id}/payments"),
            new PaySupplierRequest(3_000, "BankTransfer", "TRF-1")));
        await ApiAssert.ErrorAsync(await PostKeyedAsync(accountant, hotel.P($"purchasing/invoices/{invoice.Id}/payments"),
            new PaySupplierRequest(3_000, "BankTransfer", "TRF-2")), HttpStatusCode.Conflict, "OVERPAYMENT");
        await ApiAssert.OkAsync<SupplierPaymentDto>(await PostKeyedAsync(accountant, hotel.P($"purchasing/invoices/{invoice.Id}/payments"),
            new PaySupplierRequest(2_200, "BankTransfer", "TRF-2")));
        Assert.Equal("Paid", (await hotel.GetAsync<List<SupplierInvoiceDto>>("purchasing/invoices")).Single(i => i.Id == invoice.Id).Status);

        var tb = await TrialBalanceAsync(hotel);
        AssertBalanced(tb);
        Assert.Equal(5_000m, Net(tb, "1200"));
        Assert.Equal(0m, Net(tb, "2010"));
        Assert.Equal(0m, Net(tb, "2000"));
        Assert.Equal(200m, Net(tb, "5700"));
        Assert.Equal(-5_200m, Net(tb, "1010"));
    }

    [Fact]
    public async Task Purchase_order_cannot_be_approved_by_its_raiser()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var (store, oil) = await StockAsync(hotel, "OIL");
        var manager = await hotel.StaffAsync(Roles.HotelManager);
        var supplier = await ApiAssert.OkAsync<SupplierDto>(await manager.PostAsJsonAsync(hotel.P("inventory/suppliers"),
            new SaveSupplierRequest("SUP1", "Fresh Foods Ltd", null, null, null, null, 30)), HttpStatusCode.Created);
        var po = await ApiAssert.OkAsync<PurchaseOrderDto>(await manager.PostAsJsonAsync(hotel.P("purchasing/orders"),
            new CreatePurchaseOrderRequest(supplier.Id, store.Id, null, [new PurchaseOrderLineRequest(oil.Id, 1, 500)])), HttpStatusCode.Created);
        await ApiAssert.ErrorAsync(await manager.PostAsJsonAsync(hotel.P($"purchasing/orders/{po.Id}/approve"),
            new PurchaseOrderActionRequest(po.ConcurrencyStamp)), HttpStatusCode.Conflict, "SELF_APPROVAL");
    }

    // ---------- restaurant ----------

    private sealed record Kitchen(OutletDto Outlet, TableDto Table, MenuItemDto Jollof, StockLocationDto Store, InventoryItemDto Rice);

    private static async Task<Kitchen> KitchenAsync(HotelSetup hotel, decimal riceOnHand = 10)
    {
        var (store, rice) = await StockAsync(hotel, "RICE", riceOnHand, 1_000);
        var outlet = await ApiAssert.OkAsync<OutletDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("restaurant/outlets"),
            new SaveOutletRequest("REST", "Main Restaurant", "Restaurant", store.Id)), HttpStatusCode.Created);
        var table = await ApiAssert.OkAsync<TableDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"restaurant/outlets/{outlet.Id}/tables"),
            new SaveTableRequest("T1", 4)), HttpStatusCode.Created);
        var jollof = await ApiAssert.OkAsync<MenuItemDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"restaurant/outlets/{outlet.Id}/menu"),
            new SaveMenuItemRequest(null, "JOL", "Jollof rice", 3_000, true, "Kitchen", [new IngredientRequest(rice.Id, 0.5m)])), HttpStatusCode.Created);
        return new Kitchen(outlet, table, jollof, store, rice);
    }

    private static async Task<OrderDto> OrderAsync(HotelSetup hotel, HttpClient waiter, Kitchen k, decimal quantity, Guid? tableId = null, Guid? stayId = null)
    {
        var order = await ApiAssert.OkAsync<OrderDto>(await waiter.PostAsJsonAsync(hotel.P("restaurant/orders"),
            new OpenOrderRequest(k.Outlet.Id, tableId, stayId, 2)), HttpStatusCode.Created);
        order = await ApiAssert.OkAsync<OrderDto>(await waiter.PostAsJsonAsync(hotel.P($"restaurant/orders/{order.Id}/lines"),
            new AddOrderLineRequest(k.Jollof.Id, quantity, "Extra spicy", order.ConcurrencyStamp)));
        return await ApiAssert.OkAsync<OrderDto>(await waiter.PostAsJsonAsync(hotel.P($"restaurant/orders/{order.Id}/send"),
            new OrderActionRequest(order.ConcurrencyStamp)));
    }

    [Fact]
    public async Task Walk_in_order_consumes_stock_and_settles_once()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var k = await KitchenAsync(hotel);
        var waiter = await hotel.StaffAsync(Roles.RestaurantStaff);

        var order = await OrderAsync(hotel, waiter, k, 2, tableId: k.Table.Id);
        Assert.Equal(6_000m, order.Subtotal);
        Assert.Equal(6_300m, order.Total);
        Assert.All(order.Lines, l => Assert.Equal("Sent", l.Status));

        // The table is busy until the order is settled.
        await ApiAssert.ErrorAsync(await waiter.PostAsJsonAsync(hotel.P("restaurant/orders"), new OpenOrderRequest(k.Outlet.Id, k.Table.Id, null, 2)),
            HttpStatusCode.Conflict, "TABLE_OCCUPIED");

        // Kitchen sees one ticket; 1kg of rice was used.
        var ticket = (await ApiAssert.OkAsync<List<KitchenTicketDto>>(await waiter.GetAsync(hotel.P("restaurant/kitchen/tickets")))).Single(t => t.OrderId == order.Id);
        Assert.StartsWith($"KOT-{hotel.Branch.Code}-", ticket.TicketNumber);
        ticket = await ApiAssert.OkAsync<KitchenTicketDto>(await waiter.PostAsJsonAsync(hotel.P($"restaurant/kitchen/tickets/{ticket.Id}/status"),
            new AdvanceTicketRequest("Preparing", ticket.ConcurrencyStamp)));
        Assert.Equal("Preparing", ticket.Status);
        Assert.Equal(9m, (await hotel.GetAsync<List<StockBalanceDto>>("inventory/balances")).Single(b => b.ItemId == k.Rice.Id).Quantity);

        // A waiter cannot void food already sent to the kitchen.
        await ApiAssert.ErrorAsync(await waiter.PostAsJsonAsync(hotel.P($"restaurant/orders/{order.Id}/lines/{order.Lines[0].Id}/void"),
            new VoidRequest("Changed mind", order.ConcurrencyStamp)), HttpStatusCode.Forbidden);

        // Wrong amount refused; exact cash amount settles; a retry with the same key returns the same result.
        await hotel.OpenShiftAsync(waiter);
        await ApiAssert.ErrorAsync(await PostKeyedAsync(waiter, hotel.P($"restaurant/orders/{order.Id}/settle"),
            new SettleOrderRequest("Cash", 6_000, null, order.ConcurrencyStamp)), HttpStatusCode.Conflict, "AMOUNT_MISMATCH");
        var key = Guid.NewGuid().ToString();
        var settled = await ApiAssert.OkAsync<OrderDto>(await PostKeyedAsync(waiter, hotel.P($"restaurant/orders/{order.Id}/settle"),
            new SettleOrderRequest("Cash", 6_300, null, order.ConcurrencyStamp), key));
        Assert.Equal("Settled", settled.Status);
        Assert.Equal("Payment", settled.Settlement);
        var replay = await ApiAssert.OkAsync<OrderDto>(await PostKeyedAsync(waiter, hotel.P($"restaurant/orders/{order.Id}/settle"),
            new SettleOrderRequest("Cash", 6_300, null, order.ConcurrencyStamp), key));
        Assert.Equal(settled.FolioId, replay.FolioId);
        await ApiAssert.ErrorAsync(await PostKeyedAsync(waiter, hotel.P($"restaurant/orders/{order.Id}/settle"),
            new SettleOrderRequest("Cash", 6_300, null, settled.ConcurrencyStamp)), HttpStatusCode.Conflict);

        var walkIn = await hotel.FolioAsync(settled.FolioId!.Value);
        Assert.Equal("Closed", walkIn.Status);
        Assert.Null(walkIn.ReservationId);
        Assert.Equal(0m, walkIn.Balance);

        var tb = await TrialBalanceAsync(hotel);
        AssertBalanced(tb);
        Assert.Equal(-6_000m, Net(tb, "4100"));
        Assert.Equal(-300m, Net(tb, "2200"));
        Assert.Equal(6_300m, Net(tb, "1000"));
        Assert.Equal(0m, Net(tb, "1100"));
        Assert.Equal(1_000m, Net(tb, "5000"));
        Assert.Equal(9_000m, Net(tb, "1200"));
    }

    [Fact]
    public async Task Room_service_order_is_charged_to_the_guest_folio()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var k = await KitchenAsync(hotel);
        var booking = await hotel.BookAsync(nights: 1);
        var stay = await ApiAssert.OkAsync<StayDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("front-desk/check-in"),
            new CheckInRequest(booking.Id, hotel.Rooms[0].Id, booking.ConcurrencyStamp)));
        var waiter = await hotel.StaffAsync(Roles.RestaurantStaff);

        var order = await OrderAsync(hotel, waiter, k, 1, stayId: stay.Id);
        var charged = await ApiAssert.OkAsync<OrderDto>(await waiter.PostAsJsonAsync(hotel.P($"restaurant/orders/{order.Id}/charge-to-room"),
            new ChargeToRoomRequest(null, order.ConcurrencyStamp)));
        Assert.Equal("RoomCharge", charged.Settlement);
        Assert.Equal(stay.FolioId, charged.FolioId);

        var folio = await hotel.FolioAsync(stay.FolioId!.Value);
        Assert.Equal(52_500m + 3_150m, folio.Balance);
        Assert.Contains(folio.Charges, c => c.Type == "Restaurant" && c.Amount == 3_000m);

        // Charging again is refused because the order is no longer open.
        await ApiAssert.ErrorAsync(await waiter.PostAsJsonAsync(hotel.P($"restaurant/orders/{order.Id}/charge-to-room"),
            new ChargeToRoomRequest(null, charged.ConcurrencyStamp)), HttpStatusCode.Conflict);
        Assert.Equal(55_650m, (await hotel.FolioAsync(stay.FolioId!.Value)).Balance);
    }

    [Fact]
    public async Task Sending_more_than_is_in_stock_is_refused()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var k = await KitchenAsync(hotel, riceOnHand: 1);
        var order = await ApiAssert.OkAsync<OrderDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("restaurant/orders"),
            new OpenOrderRequest(k.Outlet.Id, null, null, 1)), HttpStatusCode.Created);
        order = await ApiAssert.OkAsync<OrderDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"restaurant/orders/{order.Id}/lines"),
            new AddOrderLineRequest(k.Jollof.Id, 3, null, order.ConcurrencyStamp)));
        await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P($"restaurant/orders/{order.Id}/send"),
            new OrderActionRequest(order.ConcurrencyStamp)), HttpStatusCode.Conflict, "INSUFFICIENT_STOCK");

        // Nothing was half-applied.
        Assert.Equal(1m, (await hotel.GetAsync<List<StockBalanceDto>>("inventory/balances")).Single(b => b.ItemId == k.Rice.Id).Quantity);
        Assert.All((await hotel.GetAsync<OrderDto>($"restaurant/orders/{order.Id}")).Lines, l => Assert.Equal("Pending", l.Status));
    }

    // ---------- maintenance ----------

    [Fact]
    public async Task Work_order_blocks_the_room_uses_parts_and_needs_independent_sign_off()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var (store, bulb) = await StockAsync(hotel, "BULB", 5, 800, expenseKey: "Repairs");
        var (tech, techId) = await hotel.StaffWithIdAsync(Roles.MaintenanceTechnician);
        var housekeeper = await hotel.StaffAsync(Roles.Housekeeping);
        var room = hotel.Rooms[0];

        var report = await ApiAssert.OkAsync<MaintenanceRequestDto>(await housekeeper.PostAsJsonAsync(hotel.P("maintenance/requests"),
            new CreateMaintenanceRequestRequest("AC leaking", "Water on the floor", "High", room.Id, null)), HttpStatusCode.Created);
        report = await ApiAssert.OkAsync<MaintenanceRequestDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"maintenance/requests/{report.Id}/triage"),
            new TriageRequest(true, null, "Urgent", techId, true, hotel.Today.AddDays(2), report.ConcurrencyStamp)));
        Assert.Equal("Converted", report.Status);

        var wo = await hotel.GetAsync<WorkOrderDto>($"maintenance/work-orders/{report.WorkOrderId}");
        Assert.NotNull(wo.RoomBlockId);
        Assert.Equal(techId, wo.AssignedToUserId);
        var availability = await hotel.GetAsync<List<RoomTypeAvailabilityDto>>(
            $"availability?arrivalDate={hotel.Today:yyyy-MM-dd}&departureDate={hotel.Today.AddDays(1):yyyy-MM-dd}&adults=1");
        Assert.Equal(2, availability.Single().AvailableRooms);

        wo = await ApiAssert.OkAsync<WorkOrderDto>(await tech.PostAsJsonAsync(hotel.P($"maintenance/work-orders/{wo.Id}/start"),
            new WorkOrderActionRequest(wo.ConcurrencyStamp)));
        var partKey = Guid.NewGuid().ToString();
        for (var i = 0; i < 2; i++)
        {
            wo = await ApiAssert.OkAsync<WorkOrderDto>(await PostKeyedAsync(tech, hotel.P($"maintenance/work-orders/{wo.Id}/parts"),
                new UsePartRequest(bulb.Id, store.Id, 2), partKey));
        }
        Assert.Equal(1_600m, wo.PartsCost);
        wo = await ApiAssert.OkAsync<WorkOrderDto>(await tech.PostAsJsonAsync(hotel.P($"maintenance/work-orders/{wo.Id}/finish"),
            new FinishWorkRequest(1.5m, 3_000, "Replaced drain pipe", wo.ConcurrencyStamp)));
        Assert.Equal("AwaitingInspection", wo.Status);

        // Technician cannot sign off their own work (they also lack the permission); a manager can.
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync(hotel.P($"maintenance/work-orders/{wo.Id}/inspect"),
            new InspectWorkRequest(true, wo.ConcurrencyStamp))).StatusCode);
        wo = await ApiAssert.OkAsync<WorkOrderDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"maintenance/work-orders/{wo.Id}/inspect"),
            new InspectWorkRequest(true, wo.ConcurrencyStamp)));
        Assert.Equal("Completed", wo.Status);

        availability = await hotel.GetAsync<List<RoomTypeAvailabilityDto>>(
            $"availability?arrivalDate={hotel.Today:yyyy-MM-dd}&departureDate={hotel.Today.AddDays(1):yyyy-MM-dd}&adults=1");
        Assert.Equal(3, availability.Single().AvailableRooms);
        Assert.Equal(3m, (await hotel.GetAsync<List<StockBalanceDto>>("inventory/balances")).Single(b => b.ItemId == bulb.Id).Quantity);

        var tb = await TrialBalanceAsync(hotel);
        AssertBalanced(tb);
        Assert.Equal(1_600m, Net(tb, "5400"));
    }

    [Fact]
    public async Task Preventive_plans_generate_work_orders_once()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var plan = await ApiAssert.OkAsync<PreventivePlanDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("maintenance/preventive-plans"),
            new SavePreventivePlanRequest("Generator service", null, null, 30, hotel.Today, "Normal")), HttpStatusCode.Created);
        var first = await ApiAssert.OkAsync<GeneratePreventiveResultDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("maintenance/preventive-plans/generate"),
            new GeneratePreventiveRequest(hotel.Today.AddDays(40))));
        Assert.Equal(2, first.Created);
        var again = await ApiAssert.OkAsync<GeneratePreventiveResultDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("maintenance/preventive-plans/generate"),
            new GeneratePreventiveRequest(hotel.Today.AddDays(40))));
        Assert.Equal(0, again.Created);
        var plans = await hotel.GetAsync<List<PreventivePlanDto>>("maintenance/preventive-plans");
        Assert.Equal(hotel.Today.AddDays(60), plans.Single(p => p.Id == plan.Id).NextDueDate);
    }

    // ---------- staff ----------

    [Fact]
    public async Task Roster_rules_and_attendance()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var hr = await hotel.StaffAsync(Roles.HumanResources);
        var (desk, deskUserId) = await hotel.StaffWithIdAsync(Roles.Receptionist);

        var dept = await ApiAssert.OkAsync<DepartmentDto>(await hr.PostAsJsonAsync(hotel.P("staff/departments"),
            new SaveDepartmentRequest("FO", "Front office")), HttpStatusCode.Created);
        var employee = await ApiAssert.OkAsync<EmployeeDto>(await hr.PostAsJsonAsync(hotel.P("staff/employees"),
            new SaveEmployeeRequest("E001", "Bola", "Ade", dept.Id, "Receptionist", deskUserId, null, null, hotel.Today.AddYears(-1))), HttpStatusCode.Created);
        var morning = await ApiAssert.OkAsync<WorkShiftDto>(await hr.PostAsJsonAsync(hotel.P("staff/shifts"),
            new SaveWorkShiftRequest("AM", "Morning", "07:00", "15:00", 30)), HttpStatusCode.Created);
        var evening = await ApiAssert.OkAsync<WorkShiftDto>(await hr.PostAsJsonAsync(hotel.P("staff/shifts"),
            new SaveWorkShiftRequest("EV", "Evening", "12:00", "20:00", 30)), HttpStatusCode.Created);
        var night = await ApiAssert.OkAsync<WorkShiftDto>(await hr.PostAsJsonAsync(hotel.P("staff/shifts"),
            new SaveWorkShiftRequest("NT", "Night", "19:00", "03:00", 0)), HttpStatusCode.Created);
        Assert.True(night.IsOvernight);
        Assert.Equal(7.5m, morning.PaidHours);

        var day = hotel.Today.AddDays(7);
        await ApiAssert.OkAsync<RosterEntryDto>(await hr.PostAsJsonAsync(hotel.P("staff/roster"),
            new CreateRosterEntryRequest(employee.Id, morning.Id, day, null)), HttpStatusCode.Created);
        await ApiAssert.ErrorAsync(await hr.PostAsJsonAsync(hotel.P("staff/roster"),
            new CreateRosterEntryRequest(employee.Id, evening.Id, day, null)), HttpStatusCode.Conflict, "ROSTER_OVERLAP");
        await ApiAssert.ErrorAsync(await hr.PostAsJsonAsync(hotel.P("staff/roster"),
            new CreateRosterEntryRequest(employee.Id, night.Id, day, null)), HttpStatusCode.Conflict, "REST_PERIOD");

        // Leave needs someone else's approval and blocks rostering.
        var leave = await ApiAssert.OkAsync<LeaveDto>(await hr.PostAsJsonAsync(hotel.P("staff/leave"),
            new CreateLeaveRequest(employee.Id, "Annual", day.AddDays(3), day.AddDays(5), "Family")), HttpStatusCode.Created);
        await ApiAssert.ErrorAsync(await hr.PostAsJsonAsync(hotel.P($"staff/leave/{leave.Id}/decision"),
            new StaffDecisionRequest(true, leave.ConcurrencyStamp)), HttpStatusCode.Conflict, "SELF_APPROVAL");
        leave = await ApiAssert.OkAsync<LeaveDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"staff/leave/{leave.Id}/decision"),
            new StaffDecisionRequest(true, leave.ConcurrencyStamp)));
        Assert.Equal("Approved", leave.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await hr.PostAsJsonAsync(hotel.P("staff/roster"),
            new CreateRosterEntryRequest(employee.Id, morning.Id, day.AddDays(4), null))).StatusCode);

        // Clock in and out as yourself; double clock-in refused; unlinked users cannot clock in.
        await ApiAssert.OkAsync<AttendanceDto>(await desk.PostAsJsonAsync(hotel.P("staff/attendance/clock-in"), new ClockRequest(null, null)));
        await ApiAssert.ErrorAsync(await desk.PostAsJsonAsync(hotel.P("staff/attendance/clock-in"), new ClockRequest(null, null)),
            HttpStatusCode.Conflict, "ALREADY_CLOCKED_IN");
        var record = await ApiAssert.OkAsync<AttendanceDto>(await desk.PostAsJsonAsync(hotel.P("staff/attendance/clock-out"), new ClockRequest(null, "Quiet shift")));
        Assert.NotNull(record.ClockOutUtc);
        await ApiAssert.ErrorAsync(await hr.PostAsJsonAsync(hotel.P("staff/attendance/clock-in"), new ClockRequest(null, null)),
            HttpStatusCode.Conflict, "NO_EMPLOYEE_RECORD");
        var colleague = await ApiAssert.OkAsync<EmployeeDto>(await hr.PostAsJsonAsync(hotel.P("staff/employees"),
            new SaveEmployeeRequest("E002", "Kemi", "Obi", dept.Id, null, null, null, null, hotel.Today)), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsJsonAsync(hotel.P("staff/attendance/clock-in"),
            new ClockRequest(colleague.Id, null))).StatusCode);
        var byManager = await ApiAssert.OkAsync<AttendanceDto>(await hr.PostAsJsonAsync(hotel.P("staff/attendance/clock-in"), new ClockRequest(colleague.Id, null)));
        Assert.Equal("Manager", byManager.Source);
    }

    [Fact]
    public async Task Phase_three_records_are_isolated_by_branch()
    {
        var a = await HotelSetup.CreateAsync(factory);
        var b = await HotelSetup.CreateAsync(factory);
        var (_, item) = await StockAsync(a, "RICE");
        var bManager = await b.StaffAsync(Roles.HotelManager);

        Assert.Equal(HttpStatusCode.Forbidden, (await bManager.GetAsync(a.P("inventory/items"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Admin.PutAsJsonAsync(b.P($"inventory/items/{item.Id}"),
            new SaveInventoryItemRequest("RICE", "Rice", "kg", null, 0, "FoodCost", true, item.ConcurrencyStamp))).StatusCode);
    }
}
