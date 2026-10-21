using HMS.Application.Abstractions;
using HMS.Application.Accounting;
using HMS.Application.Billing;
using HMS.Application.Common;
using HMS.Application.Inventory;
using HMS.Application.Rates;
using HMS.Application.Security;
using HMS.Contracts.Restaurant;
using HMS.Domain.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Restaurant;

public interface IRestaurantService
{
    Task<IReadOnlyList<OutletDto>> ListOutletsAsync(Guid branchId, CancellationToken ct = default);
    Task<OutletDto> SaveOutletAsync(Guid branchId, Guid? outletId, SaveOutletRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TableDto>> ListTablesAsync(Guid branchId, Guid outletId, CancellationToken ct = default);
    Task<TableDto> SaveTableAsync(Guid branchId, Guid outletId, Guid? tableId, SaveTableRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<MenuCategoryDto>> ListCategoriesAsync(Guid branchId, Guid outletId, CancellationToken ct = default);
    Task<MenuCategoryDto> SaveCategoryAsync(Guid branchId, Guid outletId, Guid? categoryId, SaveMenuCategoryRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<MenuItemDto>> ListMenuAsync(Guid branchId, Guid outletId, bool activeOnly, CancellationToken ct = default);
    Task<MenuItemDto> SaveMenuItemAsync(Guid branchId, Guid outletId, Guid? menuItemId, SaveMenuItemRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<OrderDto>> ListOrdersAsync(Guid branchId, Guid? outletId, bool openOnly, CancellationToken ct = default);
    Task<OrderDto> GetOrderAsync(Guid branchId, Guid orderId, CancellationToken ct = default);
    Task<OrderDto> OpenOrderAsync(Guid branchId, OpenOrderRequest request, CancellationToken ct = default);
    Task<OrderDto> AddLineAsync(Guid branchId, Guid orderId, AddOrderLineRequest request, CancellationToken ct = default);
    Task<OrderDto> VoidLineAsync(Guid branchId, Guid orderId, Guid lineId, VoidRequest request, CancellationToken ct = default);
    Task<OrderDto> SendToKitchenAsync(Guid branchId, Guid orderId, OrderActionRequest request, CancellationToken ct = default);
    Task<OrderDto> VoidOrderAsync(Guid branchId, Guid orderId, VoidRequest request, CancellationToken ct = default);
    Task<OrderDto> ChargeToRoomAsync(Guid branchId, Guid orderId, ChargeToRoomRequest request, CancellationToken ct = default);
    Task<OrderDto> SettleAsync(Guid branchId, Guid orderId, SettleOrderRequest request, string? idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<KitchenTicketDto>> ListTicketsAsync(Guid branchId, Guid? outletId, string? station, bool activeOnly, CancellationToken ct = default);
    Task<KitchenTicketDto> AdvanceTicketAsync(Guid branchId, Guid ticketId, AdvanceTicketRequest request, CancellationToken ct = default);
}

internal sealed class RestaurantService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    INumberSequence numbers,
    IStockService stock,
    ILedgerService ledger,
    IFolioService folios,
    IPaymentService payments,
    IPricingService pricing) : IRestaurantService
{
    // ---------- set-up ----------

    public async Task<IReadOnlyList<OutletDto>> ListOutletsAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return (await db.RestaurantOutlets.AsNoTracking().Where(o => o.BranchId == branchId).OrderBy(o => o.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<OutletDto> SaveOutletAsync(Guid branchId, Guid? outletId, SaveOutletRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Code), request.Code, 20).Require(nameof(request.Name), request.Name, 200).ThrowIfInvalid();
        var type = EnumParser.Parse<OutletType>(request.Type, nameof(request.Type));
        await db.StockLocations.GetOwnedAsync(branchId, request.StockLocationId, "Stock location", ct);
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.RestaurantOutlets.AnyAsync(o => o.BranchId == branchId && o.Code == code && o.Id != (outletId ?? Guid.Empty), ct))
            throw new ConflictException("OUTLET_CODE_EXISTS", $"Outlet code {code} is already used.");
        var outlet = outletId is { } id ? await db.RestaurantOutlets.GetOwnedAsync(branchId, id, "Outlet", ct) : null;
        if (outlet is null) db.RestaurantOutlets.Add(outlet = new RestaurantOutlet { CompanyId = scope.CompanyId, BranchId = branchId });
        outlet.Code = code;
        outlet.Name = request.Name.Trim();
        outlet.Type = type;
        outlet.StockLocationId = request.StockLocationId;
        outlet.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(outlet);
    }

    public async Task<IReadOnlyList<TableDto>> ListTablesAsync(Guid branchId, Guid outletId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var tables = await db.RestaurantTables.AsNoTracking().Where(t => t.BranchId == branchId && t.OutletId == outletId).OrderBy(t => t.TableNumber).ToListAsync(ct);
        var ids = tables.Select(t => t.Id).ToList();
        var open = await db.RestaurantOrders.Where(o => o.TableId != null && ids.Contains(o.TableId.Value) && o.Status == RestaurantOrderStatus.Open)
            .Select(o => new { TableId = o.TableId!.Value, o.Id }).ToListAsync(ct);
        return tables.Select(t => new TableDto(t.Id, t.OutletId, t.TableNumber, t.Seats, t.IsActive, open.FirstOrDefault(o => o.TableId == t.Id)?.Id)).ToList();
    }

    public async Task<TableDto> SaveTableAsync(Guid branchId, Guid outletId, Guid? tableId, SaveTableRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        await db.RestaurantOutlets.GetOwnedAsync(branchId, outletId, "Outlet", ct);
        new Validation().Require(nameof(request.TableNumber), request.TableNumber, 20)
            .When(request.Seats is < 1 or > 100, nameof(request.Seats), "Seats must be 1–100.").ThrowIfInvalid();
        var number = request.TableNumber.Trim();
        if (await db.RestaurantTables.AnyAsync(t => t.OutletId == outletId && t.TableNumber == number && t.Id != (tableId ?? Guid.Empty), ct))
            throw new ConflictException("TABLE_EXISTS", $"Table {number} already exists in this outlet.");
        var table = tableId is { } id ? await db.RestaurantTables.GetOwnedAsync(branchId, id, "Table", ct) : null;
        if (table is null) db.RestaurantTables.Add(table = new RestaurantTable { CompanyId = scope.CompanyId, BranchId = branchId, OutletId = outletId });
        table.TableNumber = number;
        table.Seats = request.Seats;
        table.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return new TableDto(table.Id, table.OutletId, table.TableNumber, table.Seats, table.IsActive, null);
    }

    public async Task<IReadOnlyList<MenuCategoryDto>> ListCategoriesAsync(Guid branchId, Guid outletId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return await db.MenuCategories.AsNoTracking().Where(c => c.BranchId == branchId && c.OutletId == outletId).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new MenuCategoryDto(c.Id, c.OutletId, c.Name, c.SortOrder)).ToListAsync(ct);
    }

    public async Task<MenuCategoryDto> SaveCategoryAsync(Guid branchId, Guid outletId, Guid? categoryId, SaveMenuCategoryRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        await db.RestaurantOutlets.GetOwnedAsync(branchId, outletId, "Outlet", ct);
        new Validation().Require(nameof(request.Name), request.Name, 100).ThrowIfInvalid();
        var category = categoryId is { } id ? await db.MenuCategories.GetOwnedAsync(branchId, id, "Menu category", ct) : null;
        if (category is null) db.MenuCategories.Add(category = new MenuCategory { CompanyId = scope.CompanyId, BranchId = branchId, OutletId = outletId });
        category.Name = request.Name.Trim();
        category.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(ct);
        return new MenuCategoryDto(category.Id, category.OutletId, category.Name, category.SortOrder);
    }

    public async Task<IReadOnlyList<MenuItemDto>> ListMenuAsync(Guid branchId, Guid outletId, bool activeOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.MenuItems.AsNoTracking().Include(m => m.Ingredients).ThenInclude(i => i.Item).Where(m => m.BranchId == branchId && m.OutletId == outletId);
        if (activeOnly) q = q.Where(m => m.IsActive);
        return (await q.OrderBy(m => m.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<MenuItemDto> SaveMenuItemAsync(Guid branchId, Guid outletId, Guid? menuItemId, SaveMenuItemRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        await db.RestaurantOutlets.GetOwnedAsync(branchId, outletId, "Outlet", ct);
        new Validation().Require(nameof(request.Code), request.Code, 20).Require(nameof(request.Name), request.Name, 200)
            .Require(nameof(request.Station), request.Station, 50)
            .When(request.Price < 0 || Money.Round(request.Price) != request.Price, nameof(request.Price), "Price must be zero or more with at most 2 decimals.")
            .When(request.Ingredients?.Any(i => i.Quantity <= 0) == true, nameof(request.Ingredients), "Ingredient quantities must be positive.")
            .ThrowIfInvalid();
        if (request.CategoryId is { } cat) await db.MenuCategories.GetOwnedAsync(branchId, cat, "Menu category", ct);
        var ingredientIds = request.Ingredients?.Select(i => i.ItemId).Distinct().ToList() ?? [];
        if (await db.InventoryItems.CountAsync(i => i.BranchId == branchId && ingredientIds.Contains(i.Id), ct) != ingredientIds.Count)
            throw new ValidationAppException(nameof(request.Ingredients), "One or more ingredients are not stock items of this branch.");
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.MenuItems.AnyAsync(m => m.OutletId == outletId && m.Code == code && m.Id != (menuItemId ?? Guid.Empty), ct))
            throw new ConflictException("MENU_CODE_EXISTS", $"Menu code {code} is already used in this outlet.");

        var item = menuItemId is { } id ? await db.MenuItems.Include(m => m.Ingredients).GetOwnedAsync(branchId, id, "Menu item", ct) : null;
        if (item is not null && request.ConcurrencyStamp is { } stamp) BranchOperations.EnsureStamp(item, stamp, "menu item");
        if (item is null) db.MenuItems.Add(item = new MenuItem { CompanyId = scope.CompanyId, BranchId = branchId, OutletId = outletId });
        item.CategoryId = request.CategoryId;
        item.Code = code;
        item.Name = request.Name.Trim();
        item.Price = request.Price;
        item.IsTaxable = request.IsTaxable;
        item.Station = request.Station.Trim();
        item.IsActive = request.IsActive;
        // Recipe changes only affect future sends; past consumption stays as posted.
        db.MenuItemIngredients.RemoveRange(item.Ingredients);
        item.Ingredients = (request.Ingredients ?? []).GroupBy(i => i.ItemId)
            .Select(g => new MenuItemIngredient { MenuItemId = item.Id, ItemId = g.Key, Quantity = g.Sum(i => i.Quantity) }).ToList();
        await db.SaveChangesAsync(ct);
        return ToDto(await db.MenuItems.AsNoTracking().Include(m => m.Ingredients).ThenInclude(i => i.Item).SingleAsync(m => m.Id == item.Id, ct));
    }

    // ---------- orders ----------

    public async Task<IReadOnlyList<OrderDto>> ListOrdersAsync(Guid branchId, Guid? outletId, bool openOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.RestaurantOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.BranchId == branchId);
        if (outletId is { } outlet) q = q.Where(o => o.OutletId == outlet);
        if (openOnly) q = q.Where(o => o.Status == RestaurantOrderStatus.Open);
        var orders = await q.OrderByDescending(o => o.CreatedAtUtc).Take(200).ToListAsync(ct);
        var result = new List<OrderDto>(orders.Count);
        foreach (var o in orders) result.Add(await ToDtoAsync(o, ct));
        return result;
    }

    public async Task<OrderDto> GetOrderAsync(Guid branchId, Guid orderId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var order = await db.RestaurantOrders.AsNoTracking().Include(o => o.Lines).GetOwnedAsync(branchId, orderId, "Order", ct);
        return await ToDtoAsync(order, ct);
    }

    public async Task<OrderDto> OpenOrderAsync(Guid branchId, OpenOrderRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var outlet = await db.RestaurantOutlets.GetOwnedAsync(branchId, request.OutletId, "Outlet", ct);
        if (!outlet.IsActive) throw new ConflictException("OUTLET_INACTIVE", $"{outlet.Name} is closed.");
        if (request.Covers is < 0 or > 100) throw new ValidationAppException(nameof(request.Covers), "Covers must be 0–100.");
        if (outlet.Type == OutletType.RoomService && request.StayId is null)
            throw new ValidationAppException(nameof(request.StayId), "Room-service orders must be for an in-house guest.");
        if (request.TableId is { } tableId)
        {
            var table = await db.RestaurantTables.GetOwnedAsync(branchId, tableId, "Table", ct);
            if (table.OutletId != outlet.Id || !table.IsActive) throw new ValidationAppException(nameof(request.TableId), "The table is not available in this outlet.");
            if (await db.RestaurantOrders.AnyAsync(o => o.TableId == tableId && o.Status == RestaurantOrderStatus.Open, ct))
                throw new ConflictException("TABLE_OCCUPIED", $"Table {table.TableNumber} already has an open order.");
        }
        if (request.StayId is { } stayId) await RequireActiveStayAsync(branchId, stayId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.RestaurantOrder, ct);
        var order = new RestaurantOrder(scope.CompanyId, branchId, number, outlet.Id, request.TableId, request.StayId, request.Covers, scope.BusinessDate, scope.UserId);
        db.RestaurantOrders.Add(order);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflictException("TABLE_OCCUPIED", "The table already has an open order.");
        }
        await tx.CommitAsync(ct);
        return await GetOrderAsync(branchId, order.Id, ct);
    }

    public Task<OrderDto> AddLineAsync(Guid branchId, Guid orderId, AddOrderLineRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, orderId, request.ConcurrencyStamp, async (order, _) =>
        {
            order.EnsureOpen();
            var item = await db.MenuItems.AsNoTracking().GetOwnedAsync(branchId, request.MenuItemId, "Menu item", ct);
            if (item.OutletId != order.OutletId || !item.IsActive) throw new ConflictException("ITEM_UNAVAILABLE", $"{item.Name} is not available here.");
            var line = new RestaurantOrderLine(order.Id, item, request.Quantity, string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim());
            order.Lines.Add(line);
            db.RestaurantOrderLines.Add(line);
        }, ct);

    public Task<OrderDto> VoidLineAsync(Guid branchId, Guid orderId, Guid lineId, VoidRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, orderId, request.ConcurrencyStamp, async (order, scope) =>
        {
            order.EnsureOpen();
            var line = order.Lines.SingleOrDefault(l => l.Id == lineId) ?? throw new NotFoundException("Order line", lineId);
            if (line.Status != OrderLineStatus.Pending)
            {
                // Food already sent is a manager decision; its ingredients stay consumed (recorded as used).
                if (!RolePermissions.HasPermission(user.Roles, Permissions.RestaurantManage))
                    throw new ForbiddenException(message: "Voiding an item already sent to the kitchen needs a restaurant manager.");
                if (line.KitchenTicketId is { } ticketId)
                {
                    var ticket = await db.KitchenTickets.Include(t => t.Lines).SingleAsync(t => t.Id == ticketId, ct);
                    foreach (var tl in ticket.Lines.Where(t => t.OrderLineId == line.Id)) tl.IsVoided = true;
                    ticket.CancelIfEmpty();
                }
            }
            line.Void(request.Reason, scope.UserId);
        }, ct);

    public Task<OrderDto> SendToKitchenAsync(Guid branchId, Guid orderId, OrderActionRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, orderId, request.ConcurrencyStamp, async (order, scope) =>
        {
            order.EnsureOpen();
            var pending = order.Lines.Where(l => l.Status == OrderLineStatus.Pending).ToList();
            if (pending.Count == 0) throw new ConflictException("NOTHING_TO_SEND", "There are no new items to send.");
            var outlet = await db.RestaurantOutlets.AsNoTracking().SingleAsync(o => o.Id == order.OutletId, ct);
            var menuIds = pending.Select(l => l.MenuItemId).Distinct().ToList();
            var recipes = await db.MenuItemIngredients.AsNoTracking().Include(i => i.Item).Where(i => menuIds.Contains(i.MenuItemId)).ToListAsync(ct);

            var costLines = new List<LedgerLine>();
            Guid? firstTicketId = null;
            foreach (var station in pending.GroupBy(l => l.Station))
            {
                var number = await numbers.NextAsync(scope.Branch, SequenceNames.KitchenTicket, ct);
                var ticket = new KitchenTicket(scope.CompanyId, branchId, number, order.Id, order.OutletId, station.Key, scope.UtcNow);
                firstTicketId ??= ticket.Id;
                foreach (var line in station)
                {
                    line.MarkSent(ticket.Id);
                    ticket.Lines.Add(new KitchenTicketLine { TicketId = ticket.Id, OrderLineId = line.Id, Name = line.Name, Quantity = line.Quantity, Notes = line.Notes });

                    // Ingredients are consumed when the kitchen is asked to prepare the dish.
                    foreach (var ingredient in recipes.Where(r => r.MenuItemId == line.MenuItemId))
                    {
                        var movement = await stock.RemoveAsync(scope, ingredient.ItemId, outlet.StockLocationId, ingredient.Quantity * line.Quantity,
                            StockMovementType.Consumption, "OrderLine", line.Id.ToString(), order.OrderNumber, ct);
                        costLines.Add(LedgerLine.Dr(ingredient.Item!.ExpenseAccountKey, -movement.TotalCost));
                        costLines.Add(LedgerLine.Cr(AccountKeys.Inventory, -movement.TotalCost));
                    }
                }
                db.KitchenTickets.Add(ticket);
            }
            await ledger.PostAsync(scope, "KitchenSend", firstTicketId!.Value.ToString(), $"Ingredients used for {order.OrderNumber}", costLines, ct);
        }, ct, transactional: true);

    public Task<OrderDto> VoidOrderAsync(Guid branchId, Guid orderId, VoidRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, orderId, request.ConcurrencyStamp, async (order, scope) =>
        {
            if (order.Lines.Any(l => l.Status is not (OrderLineStatus.Pending or OrderLineStatus.Voided))
                && !RolePermissions.HasPermission(user.Roles, Permissions.RestaurantManage))
                throw new ForbiddenException(message: "Voiding an order with items already sent needs a restaurant manager.");
            var ticketIds = order.Lines.Where(l => l.KitchenTicketId != null).Select(l => l.KitchenTicketId!.Value).Distinct().ToList();
            foreach (var ticket in await db.KitchenTickets.Include(t => t.Lines).Where(t => ticketIds.Contains(t.Id)).ToListAsync(ct))
            {
                foreach (var tl in ticket.Lines) tl.IsVoided = true;
                ticket.CancelIfEmpty();
            }
            order.Void(request.Reason, scope.UserId);
        }, ct);

    public Task<OrderDto> ChargeToRoomAsync(Guid branchId, Guid orderId, ChargeToRoomRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, orderId, request.ConcurrencyStamp, async (order, scope) =>
        {
            order.EnsureOpen();
            var stayId = request.StayId ?? order.StayId ?? throw new ValidationAppException(nameof(request.StayId), "Choose the in-house guest to charge.");
            var stay = await RequireActiveStayAsync(branchId, stayId, ct);
            var folio = await db.Folios.Include(f => f.Reservation).Include(f => f.Guest).SingleAsync(f => f.ReservationId == stay.ReservationId, ct);
            folio.EnsureOpen();
            await PostOrderChargesAsync(scope, order, folio, ct);
            order.Settle(OrderSettlement.RoomCharge, folio.Id, scope.UtcNow);
        }, ct, transactional: true);

    public async Task<OrderDto> SettleAsync(Guid branchId, Guid orderId, SettleOrderRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new ValidationAppException("Idempotency-Key", "An Idempotency-Key header (max 100 characters) is required for payments.");
        var tender = EnumParser.Parse<TenderType>(request.Tender, nameof(request.Tender));

        var order = await db.RestaurantOrders.Include(o => o.Lines).Include(o => o.Outlet).GetOwnedAsync(branchId, orderId, "Order", ct);
        var previous = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BranchId == branchId && p.IdempotencyKey == idempotencyKey, ct);
        if (previous is not null)
        {
            // A retry after a timeout: the order was already settled by this payment.
            if (order.FolioId != previous.FolioId) throw new ConflictException("IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was used for another payment.");
            return await GetOrderAsync(branchId, orderId, ct);
        }
        BranchOperations.EnsureStamp(order, request.ConcurrencyStamp, "order");
        order.EnsureOpen();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.Folio, ct);
        var folio = Folio.WalkIn(scope.CompanyId, branchId, number, $"{order.Outlet!.Name} {order.OrderNumber}", scope.Branch.Currency);
        db.Folios.Add(folio);
        await PostOrderChargesAsync(scope, order, folio, ct);
        var total = (await folios.GetBalanceAsync(folio.Id, ct)).TotalCharges;
        if (request.Amount != total)
            throw new ConflictException("AMOUNT_MISMATCH", $"The order total is {folio.Currency} {total:N2}. Collect exactly that amount.");
        var payment = await payments.CollectAsync(scope, folio, tender, request.Amount, idempotencyKey, request.ExternalReference, ct);
        if (payment.Status != PaymentStatus.Confirmed)
            throw new ConflictException("PAYMENT_NOT_IMMEDIATE", "Walk-in orders must be paid by cash or card. Charge in-house guests to their room instead.");
        folio.Close(scope.UtcNow);
        order.Settle(OrderSettlement.Payment, folio.Id, scope.UtcNow);
        db.Touch(order);
        await SaveOrConflictAsync(ct);
        await tx.CommitAsync(ct);
        return await GetOrderAsync(branchId, orderId, ct);
    }

    // ---------- kitchen ----------

    public async Task<IReadOnlyList<KitchenTicketDto>> ListTicketsAsync(Guid branchId, Guid? outletId, string? station, bool activeOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.KitchenTickets.AsNoTracking().Include(t => t.Lines).Include(t => t.Order).Where(t => t.BranchId == branchId);
        if (outletId is { } o) q = q.Where(t => t.OutletId == o);
        if (!string.IsNullOrWhiteSpace(station)) q = q.Where(t => t.Station == station);
        if (activeOnly) q = q.Where(t => t.Status == KitchenTicketStatus.New || t.Status == KitchenTicketStatus.Preparing || t.Status == KitchenTicketStatus.Ready);
        return (await q.OrderBy(t => t.SentAtUtc).Take(300).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<KitchenTicketDto> AdvanceTicketAsync(Guid branchId, Guid ticketId, AdvanceTicketRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var ticket = await db.KitchenTickets.Include(t => t.Lines).GetOwnedAsync(branchId, ticketId, "Kitchen ticket", ct);
        BranchOperations.EnsureStamp(ticket, request.ConcurrencyStamp, "ticket");
        var to = EnumParser.Parse<KitchenTicketStatus>(request.Status, nameof(request.Status));
        ticket.Advance(to, scope.UtcNow);
        var lineIds = ticket.Lines.Where(l => !l.IsVoided).Select(l => l.OrderLineId).ToList();
        foreach (var line in await db.RestaurantOrderLines.Where(l => lineIds.Contains(l.Id)).ToListAsync(ct))
        {
            if (to == KitchenTicketStatus.Ready) line.MarkReady();
            if (to == KitchenTicketStatus.Served) line.MarkServed();
        }
        await SaveOrConflictAsync(ct);
        return ToDto(await db.KitchenTickets.AsNoTracking().Include(t => t.Lines).Include(t => t.Order).SingleAsync(t => t.Id == ticket.Id, ct));
    }

    // ---------- helpers ----------

    /// <summary>Posts the order as at most two folio charges (taxable and non-taxable) with stable keys, so a retry cannot double charge.</summary>
    private async Task PostOrderChargesAsync(BranchScope scope, RestaurantOrder order, Folio folio, CancellationToken ct)
    {
        var outlet = order.Outlet ?? await db.RestaurantOutlets.AsNoTracking().SingleAsync(o => o.Id == order.OutletId, ct);
        var type = outlet.Type == OutletType.Bar ? ChargeType.Bar : ChargeType.Restaurant;
        var lines = order.ActiveLines.ToList();
        if (lines.Count == 0) throw new ConflictException("ORDER_EMPTY", "The order has no items to settle.");
        var taxable = lines.Where(l => l.IsTaxable).Sum(l => l.Amount);
        var exempt = lines.Where(l => !l.IsTaxable).Sum(l => l.Amount);
        if (taxable > 0)
            await folios.PostAsync(scope, folio, type, $"{outlet.Name} {order.OrderNumber}", 1, taxable, true, $"ORDER:{order.Id}:T", null, null, ct);
        if (exempt > 0)
            await folios.PostAsync(scope, folio, type, $"{outlet.Name} {order.OrderNumber} (tax exempt)", 1, exempt, false, $"ORDER:{order.Id}:N", null, null, ct);
    }

    private async Task<Stay> RequireActiveStayAsync(Guid branchId, Guid stayId, CancellationToken ct)
    {
        var stay = await db.Stays.AsNoTracking().GetOwnedAsync(branchId, stayId, "Stay", ct);
        if (stay.Status != StayStatus.Active) throw new ConflictException("STAY_CLOSED", "The guest has checked out; take payment instead.");
        return stay;
    }

    private async Task<OrderDto> MutateAsync(Guid branchId, Guid orderId, Guid stamp, Func<RestaurantOrder, BranchScope, Task> action,
        CancellationToken ct, bool transactional = false)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var order = await db.RestaurantOrders.Include(o => o.Lines).Include(o => o.Outlet).GetOwnedAsync(branchId, orderId, "Order", ct);
        BranchOperations.EnsureStamp(order, stamp, "order");
        await using var tx = transactional ? await db.Database.BeginTransactionAsync(ct) : null;
        await action(order, scope);
        db.Touch(order);
        await SaveOrConflictAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetOrderAsync(branchId, orderId, ct);
    }

    private async Task SaveOrConflictAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "The order was changed on another device. Reload and try again.");
        }
    }

    private async Task<OrderDto> ToDtoAsync(RestaurantOrder o, CancellationToken ct)
    {
        decimal tax;
        if (o.FolioId is { } folioId && o.Status == RestaurantOrderStatus.Settled)
        {
            var key = $"ORDER:{o.Id}:T:TAX:";
            tax = (await db.FolioCharges.Where(c => c.FolioId == folioId && c.Type == ChargeType.Tax && c.IdempotencyKey != null && c.IdempotencyKey.StartsWith(key))
                .Select(c => c.Amount).ToListAsync(ct)).Sum();
        }
        else
        {
            var taxable = o.ActiveLines.Where(l => l.IsTaxable).Sum(l => l.Amount);
            var rates = await pricing.TaxesForAsync(o.BranchId, TaxScope.Service, o.BusinessDate, ct);
            tax = taxable <= 0 ? 0 : rates.Where(t => !t.IsInclusive).Sum(t => Money.Round(taxable * t.RatePercent / 100m));
        }
        var subtotal = o.Subtotal;
        return new OrderDto(o.Id, o.OrderNumber, o.OutletId, o.TableId, o.StayId, o.Covers, o.BusinessDate, o.Status.ToString(), o.Settlement.ToString(),
            o.FolioId, subtotal, tax, subtotal + tax,
            o.Lines.OrderBy(l => l.Status == OrderLineStatus.Voided).Select(l => new OrderLineDto(l.Id, l.MenuItemId, l.Name, l.Station, l.Quantity, l.UnitPrice,
                l.Amount, l.Status.ToString(), l.Notes, l.VoidReason)).ToList(), o.ConcurrencyStamp);
    }

    private static OutletDto ToDto(RestaurantOutlet o) => new(o.Id, o.Code, o.Name, o.Type.ToString(), o.StockLocationId, o.IsActive);

    private static MenuItemDto ToDto(MenuItem m) => new(m.Id, m.OutletId, m.CategoryId, m.Code, m.Name, m.Price, m.IsTaxable, m.Station, m.IsActive,
        m.Ingredients.Select(i => new IngredientDto(i.ItemId, i.Item?.Sku ?? string.Empty, i.Item?.Name ?? string.Empty, i.Quantity)).ToList(), m.ConcurrencyStamp);

    private static KitchenTicketDto ToDto(KitchenTicket t) => new(t.Id, t.TicketNumber, t.OrderId, t.Order?.OrderNumber ?? string.Empty, t.Station,
        t.Status.ToString(), t.SentAtUtc, t.ReadyAtUtc, t.Lines.Select(l => new KitchenTicketLineDto(l.OrderLineId, l.Name, l.Quantity, l.Notes, l.IsVoided)).ToList(),
        t.ConcurrencyStamp);
}
