using HMS.Api.Authorization;
using HMS.Application.Rooms;
using HMS.Application.Security;
using HMS.Contracts.Rooms;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}")]
public sealed class RoomsController(IRoomService rooms) : ApiControllerBase
{
    [HttpGet("room-types")]
    [HasPermission(Permissions.RoomsView)]
    public Task<IReadOnlyList<RoomTypeDto>> RoomTypes(Guid branchId, CancellationToken ct) => rooms.ListRoomTypesAsync(branchId, ct);

    [HttpPost("room-types")]
    [HasPermission(Permissions.RoomsManage)]
    public async Task<ActionResult<RoomTypeDto>> CreateRoomType(Guid branchId, CreateRoomTypeRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await rooms.CreateRoomTypeAsync(branchId, request, ct));

    [HttpPut("room-types/{roomTypeId:guid}")]
    [HasPermission(Permissions.RoomsManage)]
    public Task<RoomTypeDto> UpdateRoomType(Guid branchId, Guid roomTypeId, UpdateRoomTypeRequest request, CancellationToken ct) =>
        rooms.UpdateRoomTypeAsync(branchId, roomTypeId, request, ct);

    [HttpGet("rooms")]
    [HasPermission(Permissions.RoomsView)]
    public Task<IReadOnlyList<RoomDto>> Rooms(Guid branchId, [FromQuery] string? status, CancellationToken ct) => rooms.ListRoomsAsync(branchId, status, ct);

    [HttpPost("rooms")]
    [HasPermission(Permissions.RoomsManage)]
    public async Task<ActionResult<RoomDto>> CreateRoom(Guid branchId, CreateRoomRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await rooms.CreateRoomAsync(branchId, request, ct));

    [HttpPut("rooms/{roomId:guid}")]
    [HasPermission(Permissions.RoomsManage)]
    public Task<RoomDto> UpdateRoom(Guid branchId, Guid roomId, UpdateRoomRequest request, CancellationToken ct) =>
        rooms.UpdateRoomAsync(branchId, roomId, request, ct);

    [HttpGet("room-blocks")]
    [HasPermission(Permissions.RoomsView)]
    public Task<IReadOnlyList<RoomBlockDto>> Blocks(Guid branchId, [FromQuery] bool activeOnly = true, CancellationToken ct = default) =>
        rooms.ListBlocksAsync(branchId, activeOnly, ct);

    [HttpPost("room-blocks")]
    [HasPermission(Permissions.RoomsManage)]
    public async Task<ActionResult<RoomBlockDto>> CreateBlock(Guid branchId, CreateRoomBlockRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await rooms.CreateBlockAsync(branchId, request, ct));

    [HttpPost("room-blocks/{blockId:guid}/release")]
    [HasPermission(Permissions.RoomsManage)]
    public Task<RoomBlockDto> ReleaseBlock(Guid branchId, Guid blockId, CancellationToken ct) => rooms.ReleaseBlockAsync(branchId, blockId, ct);

    [HttpGet("inventory")]
    [HasPermission(Permissions.RoomsView)]
    public Task<IReadOnlyList<InventoryDayDto>> Inventory(Guid branchId, [FromQuery] DateOnly start, [FromQuery] DateOnly end, CancellationToken ct) =>
        rooms.GetInventoryAsync(branchId, start, end, ct);

    [HttpPut("inventory/sell-limit")]
    [HasPermission(Permissions.RatesManage)]
    public async Task<IActionResult> SetSellLimit(Guid branchId, SetSellLimitRequest request, CancellationToken ct)
    {
        await rooms.SetSellLimitAsync(branchId, request, ct);
        return NoContent();
    }

    [HttpPost("inventory/reconcile")]
    [HasPermission(Permissions.RoomsManage)]
    public Task<InventoryReconciliationDto> Reconcile(Guid branchId, [FromQuery] DateOnly start, [FromQuery] DateOnly end, CancellationToken ct) =>
        rooms.ReconcileAsync(branchId, start, end, ct);
}
