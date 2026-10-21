namespace HMS.Domain.Entities;

/// <summary>Per-branch counter for human-readable numbers (reservations, folios, receipts, invoices).</summary>
public class BranchSequence
{
    public Guid BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long NextValue { get; set; }
}

public static class SequenceNames
{
    public const string Reservation = "RES";
    public const string Folio = "FOL";
    public const string Receipt = "RCT";
    public const string Invoice = "INV";
    public const string Refund = "RFD";
    public const string Journal = "JE";
    public const string Requisition = "REQ";
    public const string PurchaseOrder = "PO";
    public const string GoodsReceipt = "GRN";
    public const string SupplierInvoice = "SINV";
    public const string SupplierPayment = "SPAY";
    public const string RestaurantOrder = "ORD";
    public const string KitchenTicket = "KOT";
    public const string WorkOrder = "WO";
    public const string StockDocument = "STK";
}
