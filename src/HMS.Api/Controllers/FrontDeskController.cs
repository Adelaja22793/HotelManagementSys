using HMS.Api.Authorization;
using HMS.Application.FrontDesk;
using HMS.Application.Security;
using HMS.Contracts.FrontDesk;
using HMS.Contracts.Reservations;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}")]
public sealed class FrontDeskController(IFrontDeskService frontDesk) : ApiControllerBase
{
    [HttpGet("front-desk/summary")]
    [HasPermission(Permissions.ReservationsView)]
    public Task<FrontDeskSummaryDto> Summary(Guid branchId, CancellationToken ct) => frontDesk.GetSummaryAsync(branchId, ct);

    [HttpGet("front-desk/arrivals")]
    [HasPermission(Permissions.ReservationsView)]
    public Task<IReadOnlyList<ReservationListItemDto>> Arrivals(Guid branchId, [FromQuery] DateOnly? date, CancellationToken ct) =>
        frontDesk.GetArrivalsAsync(branchId, date, ct);

    [HttpGet("front-desk/in-house")]
    [HasPermission(Permissions.ReservationsView)]
    public Task<IReadOnlyList<StayDto>> InHouse(Guid branchId, CancellationToken ct) => frontDesk.GetInHouseAsync(branchId, ct);

    [HttpGet("front-desk/departures")]
    [HasPermission(Permissions.ReservationsView)]
    public Task<IReadOnlyList<StayDto>> Departures(Guid branchId, [FromQuery] DateOnly? date, CancellationToken ct) =>
        frontDesk.GetDeparturesAsync(branchId, date, ct);

    [HttpGet("stays/{stayId:guid}")]
    [HasPermission(Permissions.ReservationsView)]
    public Task<StayDto> Stay(Guid branchId, Guid stayId, CancellationToken ct) => frontDesk.GetStayAsync(branchId, stayId, ct);

    [HttpPost("front-desk/check-in")]
    [HasPermission(Permissions.FrontDeskCheckIn)]
    public Task<StayDto> CheckIn(Guid branchId, CheckInRequest request, CancellationToken ct) => frontDesk.CheckInAsync(branchId, request, ct);

    [HttpPost("front-desk/check-out")]
    [HasPermission(Permissions.FrontDeskCheckOut)]
    public Task<CheckOutResultDto> CheckOut(Guid branchId, CheckOutRequest request, CancellationToken ct) => frontDesk.CheckOutAsync(branchId, request, ct);
}
