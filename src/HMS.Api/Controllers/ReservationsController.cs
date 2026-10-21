using HMS.Api.Authorization;
using HMS.Application.Reservations;
using HMS.Application.Security;
using HMS.Contracts.Common;
using HMS.Contracts.Reservations;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}")]
public sealed class ReservationsController(IReservationService reservations) : ApiControllerBase
{
    [HttpGet("availability")]
    [HasPermission(Permissions.ReservationsView)]
    public Task<IReadOnlyList<RoomTypeAvailabilityDto>> Availability(Guid branchId, [FromQuery] AvailabilityQuery query, CancellationToken ct) =>
        reservations.GetAvailabilityAsync(branchId, query, ct);

    [HttpGet("reservations")]
    [HasPermission(Permissions.ReservationsView)]
    public Task<PagedResult<ReservationListItemDto>> Search(Guid branchId, [FromQuery] ReservationSearchQuery query, CancellationToken ct) =>
        reservations.SearchAsync(branchId, query, ct);

    [HttpGet("reservations/{reservationId:guid}")]
    [HasPermission(Permissions.ReservationsView)]
    public Task<ReservationDto> Get(Guid branchId, Guid reservationId, CancellationToken ct) => reservations.GetAsync(branchId, reservationId, ct);

    /// <summary>Creates a booking. Send an Idempotency-Key header so retries never double-book.</summary>
    [HttpPost("reservations")]
    [HasPermission(Permissions.ReservationsManage)]
    public async Task<ActionResult<ReservationDto>> Create(Guid branchId, CreateReservationRequest request,
        [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? idempotencyKey, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await reservations.CreateAsync(branchId, request, idempotencyKey, ct));

    [HttpPut("reservations/{reservationId:guid}")]
    [HasPermission(Permissions.ReservationsManage)]
    public Task<ReservationDto> Modify(Guid branchId, Guid reservationId, ModifyReservationRequest request, CancellationToken ct) =>
        reservations.ModifyAsync(branchId, reservationId, request, ct);

    [HttpPost("reservations/{reservationId:guid}/cancel")]
    [HasPermission(Permissions.ReservationsManage)]
    public Task<ReservationDto> Cancel(Guid branchId, Guid reservationId, CancelReservationRequest request, CancellationToken ct) =>
        reservations.CancelAsync(branchId, reservationId, request, ct);

    [HttpPost("reservations/{reservationId:guid}/no-show")]
    [HasPermission(Permissions.ReservationsManage)]
    public Task<ReservationDto> NoShow(Guid branchId, Guid reservationId, CancelReservationRequest request, CancellationToken ct) =>
        reservations.MarkNoShowAsync(branchId, reservationId, request, ct);

    [HttpPost("reservations/{reservationId:guid}/assign-room")]
    [HasPermission(Permissions.ReservationsManage)]
    public Task<ReservationDto> AssignRoom(Guid branchId, Guid reservationId, AssignRoomRequest request, CancellationToken ct) =>
        reservations.AssignRoomAsync(branchId, reservationId, request, ct);

    [HttpPost("reservations/{reservationId:guid}/unassign-room")]
    [HasPermission(Permissions.ReservationsManage)]
    public Task<ReservationDto> UnassignRoom(Guid branchId, Guid reservationId, [FromQuery] Guid concurrencyStamp, CancellationToken ct) =>
        reservations.UnassignRoomAsync(branchId, reservationId, concurrencyStamp, ct);
}
