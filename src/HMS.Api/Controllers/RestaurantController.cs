using HMS.Api.Authorization;
using HMS.Application.Restaurant;
using HMS.Application.Security;
using HMS.Contracts.Common;
using HMS.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/restaurant")]
public sealed class RestaurantController(IRestaurantService restaurant) : ApiControllerBase
{
    [HttpGet("outlets")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<IReadOnlyList<OutletDto>> Outlets(Guid branchId, CancellationToken ct) => restaurant.ListOutletsAsync(branchId, ct);

    [HttpPost("outlets")]
    [HasPermission(Permissions.RestaurantManage)]
    public async Task<ActionResult<OutletDto>> CreateOutlet(Guid branchId, SaveOutletRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await restaurant.SaveOutletAsync(branchId, null, request, ct));

    [HttpPut("outlets/{outletId:guid}")]
    [HasPermission(Permissions.RestaurantManage)]
    public Task<OutletDto> UpdateOutlet(Guid branchId, Guid outletId, SaveOutletRequest request, CancellationToken ct) =>
        restaurant.SaveOutletAsync(branchId, outletId, request, ct);

    [HttpGet("outlets/{outletId:guid}/tables")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<IReadOnlyList<TableDto>> Tables(Guid branchId, Guid outletId, CancellationToken ct) => restaurant.ListTablesAsync(branchId, outletId, ct);

    [HttpPost("outlets/{outletId:guid}/tables")]
    [HasPermission(Permissions.RestaurantManage)]
    public async Task<ActionResult<TableDto>> CreateTable(Guid branchId, Guid outletId, SaveTableRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await restaurant.SaveTableAsync(branchId, outletId, null, request, ct));

    [HttpPut("outlets/{outletId:guid}/tables/{tableId:guid}")]
    [HasPermission(Permissions.RestaurantManage)]
    public Task<TableDto> UpdateTable(Guid branchId, Guid outletId, Guid tableId, SaveTableRequest request, CancellationToken ct) =>
        restaurant.SaveTableAsync(branchId, outletId, tableId, request, ct);

    [HttpGet("outlets/{outletId:guid}/categories")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<IReadOnlyList<MenuCategoryDto>> Categories(Guid branchId, Guid outletId, CancellationToken ct) => restaurant.ListCategoriesAsync(branchId, outletId, ct);

    [HttpPost("outlets/{outletId:guid}/categories")]
    [HasPermission(Permissions.RestaurantManage)]
    public async Task<ActionResult<MenuCategoryDto>> CreateCategory(Guid branchId, Guid outletId, SaveMenuCategoryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await restaurant.SaveCategoryAsync(branchId, outletId, null, request, ct));

    [HttpGet("outlets/{outletId:guid}/menu")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<IReadOnlyList<MenuItemDto>> Menu(Guid branchId, Guid outletId, [FromQuery] bool activeOnly = true, CancellationToken ct = default) =>
        restaurant.ListMenuAsync(branchId, outletId, activeOnly, ct);

    [HttpPost("outlets/{outletId:guid}/menu")]
    [HasPermission(Permissions.RestaurantManage)]
    public async Task<ActionResult<MenuItemDto>> CreateMenuItem(Guid branchId, Guid outletId, SaveMenuItemRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await restaurant.SaveMenuItemAsync(branchId, outletId, null, request, ct));

    [HttpPut("outlets/{outletId:guid}/menu/{menuItemId:guid}")]
    [HasPermission(Permissions.RestaurantManage)]
    public Task<MenuItemDto> UpdateMenuItem(Guid branchId, Guid outletId, Guid menuItemId, SaveMenuItemRequest request, CancellationToken ct) =>
        restaurant.SaveMenuItemAsync(branchId, outletId, menuItemId, request, ct);

    [HttpGet("orders")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<IReadOnlyList<OrderDto>> Orders(Guid branchId, [FromQuery] Guid? outletId, [FromQuery] bool openOnly = true, CancellationToken ct = default) =>
        restaurant.ListOrdersAsync(branchId, outletId, openOnly, ct);

    [HttpGet("orders/{orderId:guid}")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<OrderDto> Order(Guid branchId, Guid orderId, CancellationToken ct) => restaurant.GetOrderAsync(branchId, orderId, ct);

    [HttpPost("orders")]
    [HasPermission(Permissions.RestaurantOperate)]
    public async Task<ActionResult<OrderDto>> OpenOrder(Guid branchId, OpenOrderRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await restaurant.OpenOrderAsync(branchId, request, ct));

    [HttpPost("orders/{orderId:guid}/lines")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<OrderDto> AddLine(Guid branchId, Guid orderId, AddOrderLineRequest request, CancellationToken ct) =>
        restaurant.AddLineAsync(branchId, orderId, request, ct);

    [HttpPost("orders/{orderId:guid}/lines/{lineId:guid}/void")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<OrderDto> VoidLine(Guid branchId, Guid orderId, Guid lineId, VoidRequest request, CancellationToken ct) =>
        restaurant.VoidLineAsync(branchId, orderId, lineId, request, ct);

    [HttpPost("orders/{orderId:guid}/send")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<OrderDto> Send(Guid branchId, Guid orderId, OrderActionRequest request, CancellationToken ct) =>
        restaurant.SendToKitchenAsync(branchId, orderId, request, ct);

    [HttpPost("orders/{orderId:guid}/void")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<OrderDto> VoidOrder(Guid branchId, Guid orderId, VoidRequest request, CancellationToken ct) =>
        restaurant.VoidOrderAsync(branchId, orderId, request, ct);

    [HttpPost("orders/{orderId:guid}/charge-to-room")]
    [HasPermission(Permissions.RestaurantOperate)]
    [HasPermission(Permissions.FoliosPost)]
    public Task<OrderDto> ChargeToRoom(Guid branchId, Guid orderId, ChargeToRoomRequest request, CancellationToken ct) =>
        restaurant.ChargeToRoomAsync(branchId, orderId, request, ct);

    /// <summary>Walk-in payment for the full order total. Idempotency-Key required.</summary>
    [HttpPost("orders/{orderId:guid}/settle")]
    [HasPermission(Permissions.RestaurantOperate)]
    [HasPermission(Permissions.PaymentsRecord)]
    public Task<OrderDto> Settle(Guid branchId, Guid orderId, SettleOrderRequest request, [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? key, CancellationToken ct) =>
        restaurant.SettleAsync(branchId, orderId, request, key, ct);

    [HttpGet("kitchen/tickets")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<IReadOnlyList<KitchenTicketDto>> Tickets(Guid branchId, [FromQuery] Guid? outletId, [FromQuery] string? station, [FromQuery] bool activeOnly = true, CancellationToken ct = default) =>
        restaurant.ListTicketsAsync(branchId, outletId, station, activeOnly, ct);

    [HttpPost("kitchen/tickets/{ticketId:guid}/status")]
    [HasPermission(Permissions.RestaurantOperate)]
    public Task<KitchenTicketDto> AdvanceTicket(Guid branchId, Guid ticketId, AdvanceTicketRequest request, CancellationToken ct) =>
        restaurant.AdvanceTicketAsync(branchId, ticketId, request, ct);
}
