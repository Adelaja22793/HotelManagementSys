using HMS.Api.Authorization;
using HMS.Application.Notifications;
using HMS.Application.Security;
using HMS.Contracts.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/notifications")]
public sealed class NotificationsController(INotificationService notifications) : ApiControllerBase
{
    [HttpGet("outbox")]
    [HasPermission(Permissions.NotificationsManage)]
    public Task<IReadOnlyList<OutboxMessageDto>> Outbox(Guid branchId, [FromQuery] string? status, [FromQuery] string? eventType, CancellationToken ct) =>
        notifications.ListOutboxAsync(branchId, status, eventType, ct);

    [HttpPost("outbox/{outboxId:guid}/retry")]
    [HasPermission(Permissions.NotificationsManage)]
    public Task<OutboxMessageDto> Retry(Guid branchId, Guid outboxId, CancellationToken ct) => notifications.RetryAsync(branchId, outboxId, ct);

    [HttpPost("outbox/{outboxId:guid}/cancel")]
    [HasPermission(Permissions.NotificationsManage)]
    public Task<OutboxMessageDto> Cancel(Guid branchId, Guid outboxId, CancellationToken ct) => notifications.CancelAsync(branchId, outboxId, ct);

    [HttpGet("templates")]
    [HasPermission(Permissions.NotificationsManage)]
    public Task<IReadOnlyList<TemplateDto>> Templates(Guid branchId, CancellationToken ct) => notifications.ListTemplatesAsync(branchId, ct);

    /// <summary>Saves a new template version. Placeholders look like {{GuestName}}.</summary>
    [HttpPost("templates")]
    [HasPermission(Permissions.NotificationsManage)]
    public async Task<ActionResult<TemplateDto>> SaveTemplate(Guid branchId, SaveTemplateRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await notifications.SaveTemplateAsync(branchId, request, ct));

    [HttpGet("inbox")]
    [HasPermission(Permissions.BranchesView)]
    public Task<IReadOnlyList<InboxItemDto>> Inbox(Guid branchId, [FromQuery] bool unreadOnly = false, CancellationToken ct = default) =>
        notifications.InboxAsync(branchId, unreadOnly, ct);

    [HttpPost("inbox/{outboxId:guid}/read")]
    [HasPermission(Permissions.BranchesView)]
    public async Task<IActionResult> MarkRead(Guid branchId, Guid outboxId, CancellationToken ct)
    {
        await notifications.MarkReadAsync(branchId, outboxId, ct);
        return NoContent();
    }

    [HttpGet("preferences")]
    [HasPermission(Permissions.BranchesView)]
    public Task<IReadOnlyList<PreferenceDto>> Preferences(Guid branchId, CancellationToken ct) => notifications.MyPreferencesAsync(branchId, ct);

    [HttpPut("preferences")]
    [HasPermission(Permissions.BranchesView)]
    public Task<PreferenceDto> SetPreference(Guid branchId, SetPreferenceRequest request, CancellationToken ct) =>
        notifications.SetMyPreferenceAsync(branchId, request, ct);
}
