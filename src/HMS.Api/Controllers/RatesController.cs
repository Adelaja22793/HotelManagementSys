using HMS.Api.Authorization;
using HMS.Application.Rates;
using HMS.Application.Security;
using HMS.Contracts.Rates;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}")]
public sealed class RatesController(IRateService rates) : ApiControllerBase
{
    [HttpGet("rate-plans")]
    [HasPermission(Permissions.RoomsView)]
    public Task<IReadOnlyList<RatePlanDto>> RatePlans(Guid branchId, CancellationToken ct) => rates.ListRatePlansAsync(branchId, ct);

    [HttpPost("rate-plans")]
    [HasPermission(Permissions.RatesManage)]
    public async Task<ActionResult<RatePlanDto>> CreateRatePlan(Guid branchId, CreateRatePlanRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await rates.CreateRatePlanAsync(branchId, request, ct));

    [HttpPut("rate-plans/{ratePlanId:guid}")]
    [HasPermission(Permissions.RatesManage)]
    public Task<RatePlanDto> UpdateRatePlan(Guid branchId, Guid ratePlanId, UpdateRatePlanRequest request, CancellationToken ct) =>
        rates.UpdateRatePlanAsync(branchId, ratePlanId, request, ct);

    [HttpGet("room-rates")]
    [HasPermission(Permissions.RoomsView)]
    public Task<IReadOnlyList<RoomRateDto>> RoomRates(Guid branchId, [FromQuery] Guid? ratePlanId, [FromQuery] Guid? roomTypeId, CancellationToken ct) =>
        rates.ListRoomRatesAsync(branchId, ratePlanId, roomTypeId, ct);

    [HttpPost("room-rates")]
    [HasPermission(Permissions.RatesManage)]
    public async Task<ActionResult<RoomRateDto>> CreateRoomRate(Guid branchId, CreateRoomRateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await rates.CreateRoomRateAsync(branchId, request, ct));

    [HttpGet("tax-rules")]
    [HasPermission(Permissions.RoomsView)]
    public Task<IReadOnlyList<TaxRuleDto>> TaxRules(Guid branchId, CancellationToken ct) => rates.ListTaxRulesAsync(branchId, ct);

    [HttpPost("tax-rules")]
    [HasPermission(Permissions.RatesManage)]
    public async Task<ActionResult<TaxRuleDto>> CreateTaxRule(Guid branchId, CreateTaxRuleRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await rates.CreateTaxRuleAsync(branchId, request, ct));

    [HttpPut("tax-rules/{taxRuleId:guid}")]
    [HasPermission(Permissions.RatesManage)]
    public Task<TaxRuleDto> UpdateTaxRule(Guid branchId, Guid taxRuleId, UpdateTaxRuleRequest request, CancellationToken ct) =>
        rates.UpdateTaxRuleAsync(branchId, taxRuleId, request, ct);

    [HttpGet("service-items")]
    [HasPermission(Permissions.FoliosView)]
    public Task<IReadOnlyList<ServiceItemDto>> ServiceItems(Guid branchId, CancellationToken ct) => rates.ListServiceItemsAsync(branchId, ct);

    [HttpPost("service-items")]
    [HasPermission(Permissions.RatesManage)]
    public async Task<ActionResult<ServiceItemDto>> CreateServiceItem(Guid branchId, CreateServiceItemRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await rates.CreateServiceItemAsync(branchId, request, ct));

    [HttpPut("service-items/{serviceItemId:guid}")]
    [HasPermission(Permissions.RatesManage)]
    public Task<ServiceItemDto> UpdateServiceItem(Guid branchId, Guid serviceItemId, UpdateServiceItemRequest request, CancellationToken ct) =>
        rates.UpdateServiceItemAsync(branchId, serviceItemId, request, ct);

    [HttpPost("quotes")]
    [HasPermission(Permissions.ReservationsView)]
    public Task<QuoteDto> Quote(Guid branchId, QuoteRequest request, CancellationToken ct) => rates.QuoteAsync(branchId, request, ct);
}
