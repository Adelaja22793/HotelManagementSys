using HMS.Api.Authorization;
using HMS.Application.Security;
using HMS.Application.SecurityOps;
using HMS.Contracts.SecurityOps;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

/// <summary>Security incidents, lost property, visitors and emergencies.</summary>
[Route(V1 + "/branches/{branchId:guid}/security")]
public sealed class SecurityOpsController(ISecurityService security) : ApiControllerBase
{
    [HttpGet("incidents")]
    [HasPermission(Permissions.SecurityView)]
    public Task<IReadOnlyList<IncidentDto>> Incidents(Guid branchId, [FromQuery] bool openOnly = true, CancellationToken ct = default) =>
        security.ListIncidentsAsync(branchId, openOnly, ct);

    [HttpGet("incidents/{incidentId:guid}")]
    [HasPermission(Permissions.SecurityView)]
    public Task<IncidentDto> Incident(Guid branchId, Guid incidentId, CancellationToken ct) => security.GetIncidentAsync(branchId, incidentId, ct);

    [HttpPost("incidents")]
    [HasPermission(Permissions.SecurityView)]
    public async Task<ActionResult<IncidentDto>> Report(Guid branchId, CreateIncidentRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await security.ReportIncidentAsync(branchId, request, ct));

    [HttpPost("incidents/{incidentId:guid}/actions")]
    [HasPermission(Permissions.SecurityManage)]
    public Task<IncidentDto> UpdateIncident(Guid branchId, Guid incidentId, CaseUpdateRequest request, CancellationToken ct) =>
        security.UpdateIncidentAsync(branchId, incidentId, request, ct);

    [HttpGet("lost-found")]
    [HasPermission(Permissions.SecurityView)]
    public Task<IReadOnlyList<LostItemDto>> LostItems(Guid branchId, [FromQuery] bool inCustodyOnly = true, CancellationToken ct = default) =>
        security.ListLostItemsAsync(branchId, inCustodyOnly, ct);

    [HttpPost("lost-found")]
    [HasPermission(Permissions.SecurityView)]
    public async Task<ActionResult<LostItemDto>> RecordLostItem(Guid branchId, CreateLostItemRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await security.RecordLostItemAsync(branchId, request, ct));

    /// <summary>move and note need security.view; return, dispose and donate need security.manage.</summary>
    [HttpPost("lost-found/{itemId:guid}/custody")]
    [HasPermission(Permissions.SecurityView)]
    public Task<LostItemDto> Custody(Guid branchId, Guid itemId, CustodyRequest request, CancellationToken ct) =>
        security.CustodyAsync(branchId, itemId, request, ct);

    [HttpGet("visitors")]
    [HasPermission(Permissions.SecurityView)]
    public Task<IReadOnlyList<VisitorDto>> Visitors(Guid branchId, [FromQuery] bool onSiteOnly = true, CancellationToken ct = default) =>
        security.ListVisitorsAsync(branchId, onSiteOnly, ct);

    [HttpPost("visitors")]
    [HasPermission(Permissions.SecurityView)]
    public async Task<ActionResult<VisitorDto>> SignIn(Guid branchId, CreateVisitorRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await security.SignInVisitorAsync(branchId, request, ct));

    [HttpPost("visitors/{visitorId:guid}/sign-out")]
    [HasPermission(Permissions.SecurityView)]
    public Task<VisitorDto> SignOut(Guid branchId, Guid visitorId, CancellationToken ct) => security.SignOutVisitorAsync(branchId, visitorId, ct);

    /// <summary>Deletes visitor records older than the retention period (Security:VisitorRetentionDays).</summary>
    [HttpPost("visitors/purge")]
    [HasPermission(Permissions.SecurityManage)]
    public Task<PurgeResultDto> Purge(Guid branchId, CancellationToken ct) => security.PurgeVisitorsAsync(branchId, ct);

    [HttpGet("emergencies")]
    [HasPermission(Permissions.SecurityView)]
    public Task<IReadOnlyList<EmergencyDto>> Emergencies(Guid branchId, CancellationToken ct) => security.ListEmergenciesAsync(branchId, ct);

    [HttpPost("emergencies")]
    [HasPermission(Permissions.SecurityView)]
    public async Task<ActionResult<EmergencyDto>> Declare(Guid branchId, DeclareEmergencyRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await security.DeclareEmergencyAsync(branchId, request, ct));

    [HttpPost("emergencies/{emergencyId:guid}/actions")]
    [HasPermission(Permissions.SecurityView)]
    public Task<EmergencyDto> LogAction(Guid branchId, Guid emergencyId, EmergencyActionRequest request, CancellationToken ct) =>
        security.LogEmergencyActionAsync(branchId, emergencyId, request, ct);

    [HttpPost("emergencies/{emergencyId:guid}/end")]
    [HasPermission(Permissions.SecurityManage)]
    public Task<EmergencyDto> End(Guid branchId, Guid emergencyId, EndEmergencyRequest request, CancellationToken ct) =>
        security.EndEmergencyAsync(branchId, emergencyId, request, ct);
}

/// <summary>Guest complaints, handled by front office and guest relations.</summary>
[Route(V1 + "/branches/{branchId:guid}/complaints")]
public sealed class ComplaintsController(ISecurityService security) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.GuestsView)]
    public Task<IReadOnlyList<ComplaintDto>> List(Guid branchId, [FromQuery] bool openOnly = true, CancellationToken ct = default) =>
        security.ListComplaintsAsync(branchId, openOnly, ct);

    [HttpPost]
    [HasPermission(Permissions.GuestsManage)]
    public async Task<ActionResult<ComplaintDto>> Log(Guid branchId, CreateComplaintRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await security.LogComplaintAsync(branchId, request, ct));

    [HttpPost("{complaintId:guid}/actions")]
    [HasPermission(Permissions.GuestsManage)]
    public Task<ComplaintDto> Update(Guid branchId, Guid complaintId, CaseUpdateRequest request, CancellationToken ct) =>
        security.UpdateComplaintAsync(branchId, complaintId, request, ct);
}
