using HMS.Api.Authorization;
using HMS.Application.Purchasing;
using HMS.Application.Security;
using HMS.Contracts.Common;
using HMS.Contracts.Purchasing;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/purchasing")]
public sealed class PurchasingController(IPurchasingService purchasing) : ApiControllerBase
{
    [HttpGet("requisitions")]
    [HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<RequisitionDto>> Requisitions(Guid branchId, [FromQuery] string? status, CancellationToken ct) =>
        purchasing.ListRequisitionsAsync(branchId, status, ct);

    [HttpPost("requisitions")]
    [HasPermission(Permissions.PurchasingManage)]
    public async Task<ActionResult<RequisitionDto>> CreateRequisition(Guid branchId, CreateRequisitionRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await purchasing.CreateRequisitionAsync(branchId, request, ct));

    [HttpPost("requisitions/{requisitionId:guid}/decision")]
    [HasPermission(Permissions.PurchasingApprove)]
    public Task<RequisitionDto> DecideRequisition(Guid branchId, Guid requisitionId, DecisionRequest request, CancellationToken ct) =>
        purchasing.DecideRequisitionAsync(branchId, requisitionId, request, ct);

    [HttpGet("orders")]
    [HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<PurchaseOrderDto>> Orders(Guid branchId, [FromQuery] string? status, CancellationToken ct) => purchasing.ListOrdersAsync(branchId, status, ct);

    [HttpGet("orders/{orderId:guid}")]
    [HasPermission(Permissions.InventoryView)]
    public Task<PurchaseOrderDto> Order(Guid branchId, Guid orderId, CancellationToken ct) => purchasing.GetOrderAsync(branchId, orderId, ct);

    [HttpPost("orders")]
    [HasPermission(Permissions.PurchasingManage)]
    public async Task<ActionResult<PurchaseOrderDto>> CreateOrder(Guid branchId, CreatePurchaseOrderRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await purchasing.CreateOrderAsync(branchId, request, ct));

    [HttpPost("orders/{orderId:guid}/approve")]
    [HasPermission(Permissions.PurchasingApprove)]
    public Task<PurchaseOrderDto> ApproveOrder(Guid branchId, Guid orderId, PurchaseOrderActionRequest request, CancellationToken ct) =>
        purchasing.ApproveOrderAsync(branchId, orderId, request, ct);

    [HttpPost("orders/{orderId:guid}/cancel")]
    [HasPermission(Permissions.PurchasingManage)]
    public Task<PurchaseOrderDto> CancelOrder(Guid branchId, Guid orderId, PurchaseOrderActionRequest request, CancellationToken ct) =>
        purchasing.CancelOrderAsync(branchId, orderId, request, ct);

    [HttpPost("orders/{orderId:guid}/close")]
    [HasPermission(Permissions.PurchasingManage)]
    public Task<PurchaseOrderDto> CloseOrder(Guid branchId, Guid orderId, PurchaseOrderActionRequest request, CancellationToken ct) =>
        purchasing.CloseOrderAsync(branchId, orderId, request, ct);

    /// <summary>Goods received note. Idempotency-Key required; partial deliveries are allowed, over-delivery is not.</summary>
    [HttpPost("orders/{orderId:guid}/receipts")]
    [HasPermission(Permissions.InventoryManage)]
    public Task<GoodsReceiptDto> Receive(Guid branchId, Guid orderId, ReceiveGoodsRequest request, [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? key, CancellationToken ct) =>
        purchasing.ReceiveAsync(branchId, orderId, request, key, ct);

    [HttpGet("orders/{orderId:guid}/receipts")]
    [HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<GoodsReceiptDto>> Receipts(Guid branchId, Guid orderId, CancellationToken ct) => purchasing.ListReceiptsAsync(branchId, orderId, ct);

    [HttpGet("uninvoiced-receipts")]
    [HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<UninvoicedReceiptDto>> Uninvoiced(Guid branchId, CancellationToken ct) => purchasing.UninvoicedReceiptsAsync(branchId, ct);

    [HttpGet("invoices")]
    [HasPermission(Permissions.InventoryView)]
    public Task<IReadOnlyList<SupplierInvoiceDto>> Invoices(Guid branchId, [FromQuery] string? status, CancellationToken ct) => purchasing.ListInvoicesAsync(branchId, status, ct);

    [HttpPost("invoices")]
    [HasPermission(Permissions.PurchasingManage)]
    public async Task<ActionResult<SupplierInvoiceDto>> CreateInvoice(Guid branchId, CreateSupplierInvoiceRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await purchasing.CreateInvoiceAsync(branchId, request, ct));

    [HttpPost("invoices/{invoiceId:guid}/approve")]
    [HasPermission(Permissions.PurchasingApprove)]
    public Task<SupplierInvoiceDto> ApproveInvoice(Guid branchId, Guid invoiceId, PurchaseOrderActionRequest request, CancellationToken ct) =>
        purchasing.ApproveInvoiceAsync(branchId, invoiceId, request, ct);

    [HttpPost("invoices/{invoiceId:guid}/payments")]
    [HasPermission(Permissions.AccountingPost)]
    public Task<SupplierPaymentDto> Pay(Guid branchId, Guid invoiceId, PaySupplierRequest request, [FromHeader(Name = HmsHeaders.IdempotencyKey)] string? key, CancellationToken ct) =>
        purchasing.PayInvoiceAsync(branchId, invoiceId, request, key, ct);
}
