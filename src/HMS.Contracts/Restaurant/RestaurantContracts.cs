namespace HMS.Contracts.Restaurant;

/// <summary>Type: Restaurant, Bar, RoomService.</summary>
public sealed record OutletDto(Guid Id, string Code, string Name, string Type, Guid StockLocationId, bool IsActive);

public sealed record SaveOutletRequest(string Code, string Name, string Type, Guid StockLocationId, bool IsActive = true);

public sealed record TableDto(Guid Id, Guid OutletId, string TableNumber, int Seats, bool IsActive, Guid? OpenOrderId);

public sealed record SaveTableRequest(string TableNumber, int Seats, bool IsActive = true);

public sealed record MenuCategoryDto(Guid Id, Guid OutletId, string Name, int SortOrder);

public sealed record SaveMenuCategoryRequest(string Name, int SortOrder);

public sealed record IngredientDto(Guid ItemId, string Sku, string ItemName, decimal Quantity);

public sealed record MenuItemDto(Guid Id, Guid OutletId, Guid? CategoryId, string Code, string Name, decimal Price, bool IsTaxable,
    string Station, bool IsActive, IReadOnlyList<IngredientDto> Ingredients, Guid ConcurrencyStamp);

public sealed record IngredientRequest(Guid ItemId, decimal Quantity);

public sealed record SaveMenuItemRequest(Guid? CategoryId, string Code, string Name, decimal Price, bool IsTaxable, string Station,
    IReadOnlyList<IngredientRequest>? Ingredients, bool IsActive = true, Guid? ConcurrencyStamp = null);

/// <summary>Room-service orders give StayId; restaurant orders usually give TableId.</summary>
public sealed record OpenOrderRequest(Guid OutletId, Guid? TableId, Guid? StayId, int Covers);

public sealed record AddOrderLineRequest(Guid MenuItemId, decimal Quantity, string? Notes, Guid ConcurrencyStamp);

public sealed record VoidRequest(string Reason, Guid ConcurrencyStamp);

public sealed record OrderActionRequest(Guid ConcurrencyStamp);

/// <summary>Charge the order to an in-house guest's folio. StayId defaults to the order's stay.</summary>
public sealed record ChargeToRoomRequest(Guid? StayId, Guid ConcurrencyStamp);

/// <summary>Walk-in settlement. Tender: Cash, PosCard or BankTransfer. Amount must equal the order total. Send an Idempotency-Key header.</summary>
public sealed record SettleOrderRequest(string Tender, decimal Amount, string? ExternalReference, Guid ConcurrencyStamp);

public sealed record OrderLineDto(Guid Id, Guid MenuItemId, string Name, string Station, decimal Quantity, decimal UnitPrice, decimal Amount,
    string Status, string? Notes, string? VoidReason);

/// <summary>Status: Open, Settled, Voided. Settlement: None, RoomCharge, Payment.</summary>
public sealed record OrderDto(Guid Id, string OrderNumber, Guid OutletId, Guid? TableId, Guid? StayId, int Covers, DateOnly BusinessDate,
    string Status, string Settlement, Guid? FolioId, decimal Subtotal, decimal Tax, decimal Total, IReadOnlyList<OrderLineDto> Lines,
    Guid ConcurrencyStamp);

public sealed record KitchenTicketLineDto(Guid OrderLineId, string Name, decimal Quantity, string? Notes, bool IsVoided);

/// <summary>Status: New, Preparing, Ready, Served, Cancelled.</summary>
public sealed record KitchenTicketDto(Guid Id, string TicketNumber, Guid OrderId, string OrderNumber, string Station, string Status,
    DateTime SentAtUtc, DateTime? ReadyAtUtc, IReadOnlyList<KitchenTicketLineDto> Lines, Guid ConcurrencyStamp);

public sealed record AdvanceTicketRequest(string Status, Guid ConcurrencyStamp);
