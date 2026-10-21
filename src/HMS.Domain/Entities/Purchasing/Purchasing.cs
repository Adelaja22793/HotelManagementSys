using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>Internal request to buy. Draft → Submitted → Approved/Rejected → Ordered. The approver must not be the requester.</summary>
public class PurchaseRequisition : BranchEntity, IConcurrencyTracked
{
    private PurchaseRequisition() { }

    public PurchaseRequisition(Guid companyId, Guid branchId, string number, string requestedByUserId, string? notes)
    {
        CompanyId = companyId;
        BranchId = branchId;
        RequisitionNumber = number;
        RequestedByUserId = requestedByUserId;
        Notes = notes;
    }

    public string RequisitionNumber { get; private set; } = string.Empty;
    public RequisitionStatus Status { get; private set; } = RequisitionStatus.Submitted;
    public string RequestedByUserId { get; private set; } = string.Empty;
    public string? DecidedByUserId { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? Notes { get; private set; }
    public Guid? PurchaseOrderId { get; private set; }
    public List<PurchaseRequisitionLine> Lines { get; private set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Approve(string userId, DateTime utcNow)
    {
        if (Status != RequisitionStatus.Submitted) throw new DomainConflictException("REQUISITION_STATE", "Only submitted requisitions can be approved.");
        if (userId == RequestedByUserId) throw new DomainConflictException("SELF_APPROVAL", "You cannot approve your own requisition.");
        Status = RequisitionStatus.Approved;
        DecidedByUserId = userId;
        DecidedAtUtc = utcNow;
    }

    public void Reject(string userId, string reason, DateTime utcNow)
    {
        if (Status != RequisitionStatus.Submitted) throw new DomainConflictException("REQUISITION_STATE", "Only submitted requisitions can be rejected.");
        Status = RequisitionStatus.Rejected;
        RejectionReason = Guard.Required(reason, "REASON_REQUIRED", "A rejection reason is required", 500);
        DecidedByUserId = userId;
        DecidedAtUtc = utcNow;
    }

    public void MarkOrdered(Guid purchaseOrderId)
    {
        if (Status != RequisitionStatus.Approved) throw new DomainConflictException("REQUISITION_STATE", "Only approved requisitions can be ordered.");
        Status = RequisitionStatus.Ordered;
        PurchaseOrderId = purchaseOrderId;
    }
}

public class PurchaseRequisitionLine : Entity
{
    public Guid RequisitionId { get; set; }
    public Guid ItemId { get; set; }
    public InventoryItem? Item { get; set; }
    public decimal Quantity { get; set; }
    public decimal? EstimatedUnitCost { get; set; }
}

/// <summary>Order to a supplier. Draft → Approved → PartiallyReceived → Received → Closed (or Cancelled before any receipt).</summary>
public class PurchaseOrder : BranchEntity, IConcurrencyTracked
{
    private PurchaseOrder() { }

    public PurchaseOrder(Guid companyId, Guid branchId, string number, Guid supplierId, Guid deliveryLocationId, DateOnly orderDate,
        string createdByUserId, Guid? requisitionId)
    {
        CompanyId = companyId;
        BranchId = branchId;
        OrderNumber = number;
        SupplierId = supplierId;
        DeliveryLocationId = deliveryLocationId;
        OrderDate = orderDate;
        OrderedByUserId = createdByUserId;
        RequisitionId = requisitionId;
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Supplier? Supplier { get; private set; }
    public Guid DeliveryLocationId { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public Guid? RequisitionId { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public string OrderedByUserId { get; private set; } = string.Empty;
    public string? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public List<PurchaseOrderLine> Lines { get; private set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public decimal Total => Lines.Sum(l => Money.Round(l.Quantity * l.UnitCost));

    public void Approve(string userId, DateTime utcNow)
    {
        if (Status != PurchaseOrderStatus.Draft) throw new DomainConflictException("PO_STATE", "Only draft purchase orders can be approved.");
        if (userId == OrderedByUserId) throw new DomainConflictException("SELF_APPROVAL", "You cannot approve a purchase order you raised.");
        Status = PurchaseOrderStatus.Approved;
        ApprovedByUserId = userId;
        ApprovedAtUtc = utcNow;
    }

    public void EnsureReceivable()
    {
        if (Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived))
            throw new DomainConflictException("PO_STATE", "Goods can only be received against an approved, open purchase order.");
    }

    public void RefreshReceiptStatus() =>
        Status = Lines.All(l => l.ReceivedQuantity >= l.Quantity) ? PurchaseOrderStatus.Received
            : Lines.Any(l => l.ReceivedQuantity > 0) ? PurchaseOrderStatus.PartiallyReceived
            : PurchaseOrderStatus.Approved;

    public void Cancel()
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Approved) || Lines.Any(l => l.ReceivedQuantity > 0))
            throw new DomainConflictException("PO_STATE", "A purchase order with received goods cannot be cancelled; close it instead.");
        Status = PurchaseOrderStatus.Cancelled;
    }

    public void Close()
    {
        if (Status is not (PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received))
            throw new DomainConflictException("PO_STATE", "Only received or partially received orders can be closed.");
        Status = PurchaseOrderStatus.Closed;
    }
}

public class PurchaseOrderLine : Entity
{
    public Guid PurchaseOrderId { get; set; }
    public Guid ItemId { get; set; }
    public InventoryItem? Item { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal InvoicedQuantity { get; set; }
}

/// <summary>Goods received note. Posting it increases stock once; the idempotency key stops a retry posting twice.</summary>
public class GoodsReceipt : BranchEntity
{
    private GoodsReceipt() { }

    public GoodsReceipt(Guid companyId, Guid branchId, string number, Guid purchaseOrderId, Guid supplierId, Guid locationId,
        DateOnly businessDate, string? deliveryNote, string idempotencyKey, string userId, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ReceiptNumber = number;
        PurchaseOrderId = purchaseOrderId;
        SupplierId = supplierId;
        LocationId = locationId;
        BusinessDate = businessDate;
        DeliveryNote = deliveryNote;
        IdempotencyKey = idempotencyKey;
        ReceivedByUserId = userId;
        ReceivedAtUtc = utcNow;
    }

    public string ReceiptNumber { get; private set; } = string.Empty;
    public Guid PurchaseOrderId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid LocationId { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string? DeliveryNote { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string ReceivedByUserId { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }
    public List<GoodsReceiptLine> Lines { get; private set; } = [];

    public decimal Total => Lines.Sum(l => Money.Round(l.Quantity * l.UnitCost));
}

public class GoodsReceiptLine : Entity
{
    public Guid GoodsReceiptId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

/// <summary>
/// Supplier's bill, matched against what was received. Entering it never changes stock (the goods
/// receipt already did). Lines that bill more than was received, or at a different price, put the
/// invoice in Discrepancy until a manager approves it.
/// </summary>
public class SupplierInvoice : BranchEntity, IConcurrencyTracked
{
    private SupplierInvoice() { }

    public SupplierInvoice(Guid companyId, Guid branchId, string number, Guid supplierId, Guid purchaseOrderId, string supplierReference,
        DateOnly invoiceDate, DateOnly dueDate, string userId)
    {
        CompanyId = companyId;
        BranchId = branchId;
        InvoiceNumber = number;
        SupplierId = supplierId;
        PurchaseOrderId = purchaseOrderId;
        SupplierReference = Guard.Required(supplierReference, "REFERENCE_REQUIRED", "Supplier invoice number is required", 100);
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        EnteredByUserId = userId;
    }

    public string InvoiceNumber { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Supplier? Supplier { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public string SupplierReference { get; private set; } = string.Empty;
    public DateOnly InvoiceDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public SupplierInvoiceStatus Status { get; private set; } = SupplierInvoiceStatus.Matched;
    public decimal Total { get; private set; }
    /// <summary>Value of the billed goods at the price they were received at.</summary>
    public decimal ReceivedValue { get; private set; }
    public decimal AmountPaid { get; private set; }
    public string? DiscrepancyNotes { get; private set; }
    public string EnteredByUserId { get; private set; } = string.Empty;
    public string? ApprovedByUserId { get; private set; }
    public List<SupplierInvoiceLine> Lines { get; private set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public decimal Outstanding => Total - AmountPaid;

    public void SetTotals(decimal total, decimal receivedValue, string? discrepancy)
    {
        Total = total;
        ReceivedValue = receivedValue;
        DiscrepancyNotes = discrepancy;
        Status = discrepancy is null ? SupplierInvoiceStatus.Matched : SupplierInvoiceStatus.Discrepancy;
    }

    public void ApproveDiscrepancy(string userId)
    {
        if (Status != SupplierInvoiceStatus.Discrepancy) throw new DomainConflictException("INVOICE_STATE", "Only invoices with discrepancies need approval.");
        if (userId == EnteredByUserId) throw new DomainConflictException("SELF_APPROVAL", "Someone other than the person who entered the invoice must approve it.");
        Status = SupplierInvoiceStatus.Approved;
        ApprovedByUserId = userId;
    }

    public void RecordPayment(decimal amount)
    {
        if (Status == SupplierInvoiceStatus.Discrepancy) throw new DomainConflictException("INVOICE_STATE", "Resolve the invoice discrepancy before paying.");
        if (Status == SupplierInvoiceStatus.Paid) throw new DomainConflictException("INVOICE_STATE", "The invoice is already paid.");
        Money.RequirePositive(amount);
        if (amount > Outstanding) throw new DomainConflictException("OVERPAYMENT", $"Only {Outstanding:N2} is outstanding on this invoice.");
        AmountPaid += amount;
        Status = AmountPaid == Total ? SupplierInvoiceStatus.Paid : SupplierInvoiceStatus.PartiallyPaid;
    }
}

public class SupplierInvoiceLine : Entity
{
    public Guid SupplierInvoiceId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

public class SupplierPayment : BranchEntity
{
    private SupplierPayment() { }

    public SupplierPayment(Guid companyId, Guid branchId, string number, Guid supplierInvoiceId, decimal amount, TenderType tender,
        string reference, DateOnly businessDate, string idempotencyKey, string userId, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        PaymentNumber = number;
        SupplierInvoiceId = supplierInvoiceId;
        Amount = Money.RequirePositive(amount);
        Tender = tender;
        Reference = Guard.Required(reference, "REFERENCE_REQUIRED", "Payment reference is required", 100);
        BusinessDate = businessDate;
        IdempotencyKey = idempotencyKey;
        PaidByUserId = userId;
        PaidAtUtc = utcNow;
    }

    public string PaymentNumber { get; private set; } = string.Empty;
    public Guid SupplierInvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public TenderType Tender { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    public DateOnly BusinessDate { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PaidByUserId { get; private set; } = string.Empty;
    public DateTime PaidAtUtc { get; private set; }
}
