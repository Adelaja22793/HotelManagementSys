using HMS.Api.Authorization;
using HMS.Application.Inventory;
using HMS.Application.Security;
using HMS.Contracts.Common;
using HMS.Contracts.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/inventory")]
public sealed class InventoryController(IInventoryService inventory) : ApiControllerBase
{
    [HttpGet("suppliers")]
    [HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<SupplierDto>> Suppliers(Guid branchId, CancellationToken ct) => inventory.ListSuppliersAsync(branchId, ct);

    [HttpPost("suppliers")]
    [HasPermission(Permissions.PurchasingManage)]
    public async Task<ActionResult<SupplierDto>> CreateSupplier(Guid branchId, SaveSupplierRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await inventory.SaveSupplierAsync(branchId, null, request, ct));

    [HttpPut("suppliers/{supplierId:guid}")]
    [HasPermission(Permissions.PurchasingManage)]
    public Task<SupplierDto> UpdateSupplier(Guid branchId, Guid supplierId, SaveSupplierRequest request, CancellationToken ct) =>
        inventory.SaveSupplierAsync(branchId, supplierId, request, ct);

    [HttpGet("locations")]
    [HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<StockLocationDto>> Locations(Guid branchId, CancellationToken ct) => inventory.ListLocationsAsync(branchId, ct);

    [HttpPost("locations")]
    [HasPermission(Permissions.InventoryManage)]
    public async Task<ActionResult<StockLocationDto>> CreateLocation(Guid branchId, SaveStockLocationRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await inventory.SaveLocationAsync(branchId, null, request, ct));

    [HttpPut("locations/{locationId:guid}")]
    [HasPermission(Permissions.InventoryManage)]
    public Task<StockLocationDto> UpdateLocation(Guid branchId, Guid locationId, SaveStockLocationRequest request, CancellationToken ct) =>
        inventory.SaveLocationAsync(branchId, locationId, request, ct);

    [HttpGet("items")]
    [HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<InventoryItemDto>> Items(Guid branchId, [FromQuery] string? search, CancellationToken ct) => inventory.ListItemsAsync(branchId, search, ct);

    [HttpPost("items")]
    [HasPermission(Permissions.InventoryManage)]
    public async Task<ActionResult<InventoryItemDto>> CreateItem(Guid branchId, SaveInventoryItemRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await inventory.SaveItemAsync(branchId, null, request, ct));

    [HttpPut("items/{itemId:guid}")]
    [HasPermission(Permissions.InventoryManage)]
    public Task<InventoryItemDto> UpdateItem(Guid branchId, Guid itemId, SaveInventoryItemRequest request, CancellationToken ct) =>
        inventory.SaveItemAsync(branchId, itemId, request, ct);

    [HttpGet("balances")]
    [HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<StockBalanceDto>> Balances(Guid branchId, [FromQuery] Guid? locationId, [FromQuery] bool belowReorderOnly, CancellationToken ct) =>
        inventory.ListBalancesAsync(branchId, locationId, belowReorderOnly, ct);

    [HttpGet("movements")]
    [HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<StockMovementDto>> Movements(Guid branchId, [FromQuery] Guid? itemId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        inventory.ListMovementsAsync(branchId, itemId, from, to, ct);

    /// <summary>Opening balance or receipt without an order. Idempotency-Key required.</summary>
    [HttpPost("receipts")]
    [HasPermission(Permissions.InventoryManage)]
    public Task<StockMovementDto> Receive(Guid branchId, StockReceiptRequest request, [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? key, CancellationToken ct) =>
        inventory.ReceiveAsync(branchId, request, key, ct);

    /// <summary>Physical count; posts the difference. Returns 204 when the count matches. Idempotency-Key required.</summary>
    [HttpPost("counts")]
    [HasPermission(Permissions.InventoryManage)]
    public async Task<ActionResult<StockMovementDto>> Count(Guid branchId, StockCountRequest request, [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? key, CancellationToken ct) =>
        await inventory.CountAsync(branchId, request, key, ct) is { } movement ? Ok(movement) : NoContent();

    [HttpPost("transfers")]
    [HasPermission(Permissions.InventoryManage)]
    public Task<IReadOnlyList<StockMovementDto>> Transfer(Guid branchId, StockTransferRequest request, [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? key, CancellationToken ct) =>
        inventory.TransferAsync(branchId, request, key, ct);

    [HttpPost("wastage")]
    [HasPermission(Permissions.InventoryManage)]
    public Task<StockMovementDto> Waste(Guid branchId, StockWastageRequest request, [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? key, CancellationToken ct) =>
        inventory.WasteAsync(branchId, request, key, ct);
}
