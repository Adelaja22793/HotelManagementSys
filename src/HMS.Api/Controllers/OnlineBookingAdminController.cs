using HMS.Api.Authorization;
using HMS.Application.PublicBooking;
using HMS.Application.Security;
using HMS.Contracts.PublicBooking;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}")]
public sealed class OnlineBookingAdminController(IOnlinePaymentAdminService admin) : ApiControllerBase
{
    /// <summary>Shows or hides the branch on the public booking website.</summary>
    [HttpPut("public-booking")]
    [HasPermission(Permissions.BranchesManage)]
    public async Task<IActionResult> Settings(Guid branchId, PublicBookingSettingsRequest request, CancellationToken ct)
    {
        await admin.SetSettingsAsync(branchId, request, ct);
        return NoContent();
    }

    [HttpGet("online-payments")]
    [HasPermission(Permissions.PaymentsVerify)]
    public Task<IReadOnlyList<OnlinePaymentDto>> Payments(Guid branchId, [FromQuery] string? status, CancellationToken ct) =>
        admin.ListAsync(branchId, status, ct);

    /// <summary>Closes a payment that needed review once the guest has been refunded or rebooked.</summary>
    [HttpPost("online-payments/{paymentId:guid}/resolve")]
    [HasPermission(Permissions.RefundsApprove)]
    public Task<OnlinePaymentDto> Resolve(Guid branchId, Guid paymentId, ResolvePaymentRequest request, CancellationToken ct) =>
        admin.ResolveAsync(branchId, paymentId, request, ct);
}
