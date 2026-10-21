namespace HMS.Contracts.Purchasing;

public sealed record RequisitionLineRequest(Guid ItemId, decimal Quantity, decimal? EstimatedUnitCost);

public sealed record CreateRequisitionRequest(IReadOnlyList<RequisitionLineRequest> Lines, string? Notes);

public sealed record RequisitionLineDto(Guid ItemId, string Sku, string ItemName, decimal Quantity, decimal? EstimatedUnitCost);

/// <summary>Status: Submitted, Approved, Rejected, Ordered, Cancelled.</summary>
public sealed record RequisitionDto(Guid Id, string RequisitionNumber, string Status, string RequestedByUserId, string? DecidedByUserId,
    string? RejectionReason, string? Notes, Guid? PurchaseOrderId, IReadOnlyList<RequisitionLineDto> Lines, Guid ConcurrencyStamp);

public sealed record DecisionRequest(bool Approved, string? Reason, Guid ConcurrencyStamp);

public sealed record PurchaseOrderLineRequest(Guid ItemId, decimal Quantity, decimal UnitCost);

/// <summary>Raise from an approved requisition (RequisitionId) or directly with lines.</summary>
public sealed record CreatePurchaseOrderRequest(Guid SupplierId, Guid DeliveryLocationId, Guid? RequisitionId,
    IReadOnlyList<PurchaseOrderLineRequest> Lines);

public sealed record PurchaseOrderLineDto(Guid Id, Guid ItemId, string Sku, string ItemName, decimal Quantity, decimal UnitCost,
    decimal ReceivedQuantity, decimal InvoicedQuantity);

/// <summary>Status: Draft, Approved, PartiallyReceived, Received, Closed, Cancelled.</summary>
public sealed record PurchaseOrderDto(Guid Id, string OrderNumber, Guid SupplierId, string SupplierName, Guid DeliveryLocationId,
    DateOnly OrderDate, string Status, decimal Total, string OrderedByUserId, string? ApprovedByUserId, Guid? RequisitionId,
    IReadOnlyList<PurchaseOrderLineDto> Lines, Guid ConcurrencyStamp);

public sealed record PurchaseOrderActionRequest(Guid ConcurrencyStamp);

public sealed record GoodsReceiptLineRequest(Guid PurchaseOrderLineId, decimal Quantity);

/// <summary>Send an Idempotency-Key header; a retry returns the original receipt.</summary>
public sealed record ReceiveGoodsRequest(IReadOnlyList<GoodsReceiptLineRequest> Lines, string? DeliveryNote);

public sealed record GoodsReceiptLineDto(Guid PurchaseOrderLineId, Guid ItemId, decimal Quantity, decimal UnitCost);

public sealed record GoodsReceiptDto(Guid Id, string ReceiptNumber, Guid PurchaseOrderId, DateOnly BusinessDate, string? DeliveryNote,
    decimal Total, IReadOnlyList<GoodsReceiptLineDto> Lines);

public sealed record SupplierInvoiceLineRequest(Guid PurchaseOrderLineId, decimal Quantity, decimal UnitPrice);

public sealed record CreateSupplierInvoiceRequest(Guid PurchaseOrderId, string SupplierReference, DateOnly InvoiceDate,
    IReadOnlyList<SupplierInvoiceLineRequest> Lines);

public sealed record SupplierInvoiceLineDto(Guid PurchaseOrderLineId, Guid ItemId, decimal Quantity, decimal UnitPrice, decimal Amount);

/// <summary>Status: Matched, Discrepancy, Approved, PartiallyPaid, Paid.</summary>
public sealed record SupplierInvoiceDto(Guid Id, string InvoiceNumber, Guid SupplierId, string SupplierName, Guid PurchaseOrderId,
    string SupplierReference, DateOnly InvoiceDate, DateOnly DueDate, string Status, decimal Total, decimal ReceivedValue,
    decimal AmountPaid, decimal Outstanding, string? DiscrepancyNotes, IReadOnlyList<SupplierInvoiceLineDto> Lines, Guid ConcurrencyStamp);

/// <summary>Tender: BankTransfer or Cash. Send an Idempotency-Key header.</summary>
public sealed record PaySupplierRequest(decimal Amount, string Tender, string Reference);

public sealed record SupplierPaymentDto(Guid Id, string PaymentNumber, Guid SupplierInvoiceId, decimal Amount, string Tender,
    string Reference, DateOnly BusinessDate);

/// <summary>Goods received but not yet invoiced, by purchase order line.</summary>
public sealed record UninvoicedReceiptDto(Guid PurchaseOrderId, string OrderNumber, string SupplierName, Guid PurchaseOrderLineId,
    string Sku, decimal ReceivedQuantity, decimal InvoicedQuantity, decimal UnitCost, decimal UninvoicedValue);
