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
}
