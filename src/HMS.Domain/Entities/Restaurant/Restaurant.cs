using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>A selling point (restaurant, bar or room service) with the store its ingredients come from.</summary>
public class RestaurantOutlet : BranchEntity, IConcurrencyTracked
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public OutletType Type { get; set; }
    public Guid StockLocationId { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public class RestaurantTable : BranchEntity
{
    public Guid OutletId { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public int Seats { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MenuCategory : BranchEntity
{
    public Guid OutletId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class MenuItem : BranchEntity, IConcurrencyTracked
{
    public Guid OutletId { get; set; }
    public Guid? CategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsTaxable { get; set; } = true;
    /// <summary>Kitchen station that prepares it, e.g. Grill, Bar, Pastry.</summary>
    public string Station { get; set; } = "Kitchen";
    public bool IsActive { get; set; } = true;
    public List<MenuItemIngredient> Ingredients { get; set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Recipe line: how much of a stock item one portion uses.</summary>
public class MenuItemIngredient : Entity
{
    public Guid MenuItemId { get; set; }
    public Guid ItemId { get; set; }
    public InventoryItem? Item { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>
/// An order at an outlet. Lines are priced from the menu when added. Sending to the kitchen creates
/// tickets and consumes recipe stock. Settlement posts once to a room folio or a walk-in folio with
/// immediate payment; the order's concurrency stamp stops a retry or a second device settling it twice.
/// </summary>
public class RestaurantOrder : BranchEntity, IConcurrencyTracked
{
    private RestaurantOrder() { }

    public RestaurantOrder(Guid companyId, Guid branchId, string number, Guid outletId, Guid? tableId, Guid? stayId, int covers,
        DateOnly businessDate, string userId)
    {
        CompanyId = companyId;
        BranchId = branchId;
        OrderNumber = number;
        OutletId = outletId;
        TableId = tableId;
        StayId = stayId;
        Covers = covers;
        BusinessDate = businessDate;
        OpenedByUserId = userId;
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public Guid OutletId { get; private set; }
    public RestaurantOutlet? Outlet { get; private set; }
    public Guid? TableId { get; private set; }
    /// <summary>Room-service orders belong to an in-house stay.</summary>
    public Guid? StayId { get; private set; }
    public int Covers { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public RestaurantOrderStatus Status { get; private set; } = RestaurantOrderStatus.Open;
    public OrderSettlement Settlement { get; private set; } = OrderSettlement.None;
    public Guid? FolioId { get; private set; }
    public string OpenedByUserId { get; private set; } = string.Empty;
    public DateTime? SettledAtUtc { get; private set; }
    public string? VoidReason { get; private set; }
    public List<RestaurantOrderLine> Lines { get; private set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public IEnumerable<RestaurantOrderLine> ActiveLines => Lines.Where(l => l.Status != OrderLineStatus.Voided);
    public decimal Subtotal => ActiveLines.Sum(l => l.Amount);

    public void EnsureOpen()
    {
        if (Status != RestaurantOrderStatus.Open) throw new DomainConflictException("ORDER_CLOSED", "The order is already settled or voided.");
    }

    public void Settle(OrderSettlement settlement, Guid folioId, DateTime utcNow)
    {
        EnsureOpen();
        if (!ActiveLines.Any()) throw new DomainConflictException("ORDER_EMPTY", "The order has no items to settle.");
        Status = RestaurantOrderStatus.Settled;
        Settlement = settlement;
        FolioId = folioId;
        SettledAtUtc = utcNow;
    }

    public void Void(string reason, string userId)
    {
        EnsureOpen();
        VoidReason = Guard.Required(reason, "REASON_REQUIRED", "A void reason is required", 300);
        Status = RestaurantOrderStatus.Voided;
        foreach (var line in Lines.Where(l => l.Status != OrderLineStatus.Voided)) line.Void(reason, userId);
    }
}

public class RestaurantOrderLine : Entity
{
    private RestaurantOrderLine() { }

    public RestaurantOrderLine(Guid orderId, MenuItem item, decimal quantity, string? notes)
    {
        if (quantity <= 0 || quantity > 100) throw new DomainException("QUANTITY_INVALID", "Quantity must be between 0 and 100.");
        OrderId = orderId;
        MenuItemId = item.Id;
        Name = item.Name;
        Station = item.Station;
        Quantity = quantity;
        UnitPrice = item.Price;
        IsTaxable = item.IsTaxable;
        Amount = Money.Round(quantity * item.Price);
        Notes = notes;
    }

    public Guid OrderId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Station { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public bool IsTaxable { get; private set; }
    public decimal Amount { get; private set; }
    public string? Notes { get; private set; }
    public OrderLineStatus Status { get; private set; } = OrderLineStatus.Pending;
    public Guid? KitchenTicketId { get; private set; }
    public string? VoidReason { get; private set; }
    public string? VoidedByUserId { get; private set; }

    public void MarkSent(Guid ticketId)
    {
        if (Status != OrderLineStatus.Pending) throw new DomainConflictException("LINE_STATE", "Only new lines can be sent to the kitchen.");
        Status = OrderLineStatus.Sent;
        KitchenTicketId = ticketId;
    }

    public void MarkReady() { if (Status == OrderLineStatus.Sent) Status = OrderLineStatus.Ready; }

    public void MarkServed() { if (Status is OrderLineStatus.Sent or OrderLineStatus.Ready) Status = OrderLineStatus.Served; }

    public void Void(string reason, string? userId)
    {
        if (Status == OrderLineStatus.Voided) throw new DomainConflictException("LINE_STATE", "The line is already voided.");
        VoidReason = Guard.Required(reason, "REASON_REQUIRED", "A void reason is required", 300);
        VoidedByUserId = userId;
        Status = OrderLineStatus.Voided;
    }
}

/// <summary>Kitchen order ticket for one station: New → Preparing → Ready → Served.</summary>
public class KitchenTicket : BranchEntity, IConcurrencyTracked
{
    private KitchenTicket() { }

    public KitchenTicket(Guid companyId, Guid branchId, string number, Guid orderId, Guid outletId, string station, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        TicketNumber = number;
        OrderId = orderId;
        OutletId = outletId;
        Station = station;
        SentAtUtc = utcNow;
    }

    public string TicketNumber { get; private set; } = string.Empty;
    public Guid OrderId { get; private set; }
    public RestaurantOrder? Order { get; private set; }
    public Guid OutletId { get; private set; }
    public string Station { get; private set; } = string.Empty;
    public KitchenTicketStatus Status { get; private set; } = KitchenTicketStatus.New;
    public DateTime SentAtUtc { get; private set; }
    public DateTime? ReadyAtUtc { get; private set; }
    public List<KitchenTicketLine> Lines { get; private set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Advance(KitchenTicketStatus to, DateTime utcNow)
    {
        var allowed = (Status, to) switch
        {
            (KitchenTicketStatus.New, KitchenTicketStatus.Preparing) => true,
            (KitchenTicketStatus.New or KitchenTicketStatus.Preparing, KitchenTicketStatus.Ready) => true,
            (KitchenTicketStatus.Ready, KitchenTicketStatus.Served) => true,
            _ => false,
        };
        if (!allowed) throw new DomainConflictException("TICKET_STATE", $"A {Status} ticket cannot move to {to}.");
        Status = to;
        if (to == KitchenTicketStatus.Ready) ReadyAtUtc = utcNow;
    }

    public void CancelIfEmpty()
    {
        if (Lines.All(l => l.IsVoided) && Status != KitchenTicketStatus.Served) Status = KitchenTicketStatus.Cancelled;
    }
}

public class KitchenTicketLine : Entity
{
    public Guid TicketId { get; set; }
    public Guid OrderLineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public bool IsVoided { get; set; }
}
