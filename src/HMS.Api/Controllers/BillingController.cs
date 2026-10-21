using HMS.Api.Authorization;
using HMS.Application.Billing;
using HMS.Application.Security;
using HMS.Contracts.Billing;
using HMS.Contracts.Common;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}")]
public sealed class BillingController(IFolioService folios, IPaymentService payments) : ApiControllerBase
{
    [HttpGet("folios/{folioId:guid}")]
    [HasPermission(Permissions.FoliosView)]
    public Task<FolioDto> Folio(Guid branchId, Guid folioId, CancellationToken ct) => folios.GetAsync(branchId, folioId, ct);

    [HttpGet("reservations/{reservationId:guid}/folio")]
    [HasPermission(Permissions.FoliosView)]
    public Task<FolioDto> FolioForReservation(Guid branchId, Guid reservationId, CancellationToken ct) =>
        folios.GetByReservationAsync(branchId, reservationId, ct);

    [HttpPost("folios/{folioId:guid}/charges")]
    [HasPermission(Permissions.FoliosPost)]
    public Task<FolioDto> PostCharge(Guid branchId, Guid folioId, PostChargeRequest request,
        [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? idempotencyKey, CancellationToken ct) =>
        folios.PostChargeAsync(branchId, folioId, request, idempotencyKey, ct);

    [HttpPost("folios/{folioId:guid}/charges/{chargeId:guid}/reverse")]
    [HasPermission(Permissions.FolioAdjust)]
    public Task<FolioDto> ReverseCharge(Guid branchId, Guid folioId, Guid chargeId, ReverseChargeRequest request, CancellationToken ct) =>
        folios.ReverseChargeAsync(branchId, folioId, chargeId, request, ct);

    [HttpGet("folios/{folioId:guid}/invoices")]
    [HasPermission(Permissions.FoliosView)]
    public Task<IReadOnlyList<InvoiceDto>> Invoices(Guid branchId, Guid folioId, CancellationToken ct) => folios.ListInvoicesAsync(branchId, folioId, ct);

    [HttpPost("folios/{folioId:guid}/invoices")]
    [HasPermission(Permissions.FoliosPost)]
    public async Task<ActionResult<InvoiceDto>> IssueInvoice(Guid branchId, Guid folioId, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await folios.IssueInvoiceAsync(branchId, folioId, ct));

    /// <summary>Records a payment. An Idempotency-Key header is required; a retry returns the original payment.</summary>
    [HttpPost("folios/{folioId:guid}/payments")]
    [HasPermission(Permissions.PaymentsRecord)]
    public Task<PaymentDto> RecordPayment(Guid branchId, Guid folioId, RecordPaymentRequest request,
        [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? idempotencyKey, CancellationToken ct) =>
        payments.RecordAsync(branchId, folioId, request, idempotencyKey, ct);

    [HttpPost("payments/{paymentId:guid}/verify")]
    [HasPermission(Permissions.PaymentsVerify)]
    public Task<PaymentDto> VerifyPayment(Guid branchId, Guid paymentId, VerifyPaymentRequest request, CancellationToken ct) =>
        payments.VerifyAsync(branchId, paymentId, request, ct);

    [HttpGet("receipts/{receiptId:guid}")]
    [HasPermission(Permissions.FoliosView)]
    public Task<ReceiptDto> Receipt(Guid branchId, Guid receiptId, CancellationToken ct) => payments.GetReceiptAsync(branchId, receiptId, ct);

    [HttpGet("refunds")]
    [HasPermission(Permissions.FoliosView)]
    public Task<IReadOnlyList<RefundDto>> Refunds(Guid branchId, [FromQuery] string? status, CancellationToken ct) => payments.ListRefundsAsync(branchId, status, ct);

    [HttpPost("folios/{folioId:guid}/refunds")]
    [HasPermission(Permissions.RefundsRequest)]
    public async Task<ActionResult<RefundDto>> RequestRefund(Guid branchId, Guid folioId, RequestRefundRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await payments.RequestRefundAsync(branchId, folioId, request, ct));

    [HttpPost("refunds/{refundId:guid}/decision")]
    [HasPermission(Permissions.RefundsApprove)]
    public Task<RefundDto> DecideRefund(Guid branchId, Guid refundId, RefundDecisionRequest request, CancellationToken ct) =>
        payments.DecideRefundAsync(branchId, refundId, request, ct);

    [HttpPost("refunds/{refundId:guid}/execute")]
    [HasPermission(Permissions.PaymentsRecord)]
    public Task<RefundDto> ExecuteRefund(Guid branchId, Guid refundId, ExecuteRefundRequest request, CancellationToken ct) =>
        payments.ExecuteRefundAsync(branchId, refundId, request, ct);
}
