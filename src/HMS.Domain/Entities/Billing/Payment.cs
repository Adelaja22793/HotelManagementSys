using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>
/// A payment record. Only <see cref="PaymentStatus.Confirmed"/> payments reduce the balance and get receipts.
/// The (BranchId, IdempotencyKey) unique index prevents double collection on retries.
/// </summary>
public class Payment : BranchEntity, IConcurrencyTracked
{
    private Payment() { }

    public Payment(Guid companyId, Guid branchId, Guid folioId, TenderType tender, decimal amount, string currency,
        string idempotencyKey, string receivedByUserId, DateTime receivedAtUtc, string? externalReference)
    {
        CompanyId = companyId;
        BranchId = branchId;
        FolioId = folioId;
        Tender = tender;
        Amount = Money.RequirePositive(amount);
        Currency = Common.Currency.Normalize(currency);
        IdempotencyKey = Guard.Required(idempotencyKey, "IDEMPOTENCY_KEY_REQUIRED", "Idempotency key is required", 100);
        ReceivedByUserId = receivedByUserId;
        ReceivedAtUtc = receivedAtUtc;
        ExternalReference = string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim();
    }

    public Guid FolioId { get; private set; }
    public TenderType Tender { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = Common.Currency.Default;
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    /// <summary>POS slip, transfer reference or provider transaction id.</summary>
    public string? ExternalReference { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid? CashierShiftId { get; private set; }
    public string ReceivedByUserId { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public string? ConfirmedByUserId { get; private set; }
    public string? FailureReason { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void LinkShift(Guid shiftId) => CashierShiftId = shiftId;

    public void Confirm(string userId, DateTime utcNow)
    {
        if (Status != PaymentStatus.Pending) throw new DomainConflictException("PAYMENT_STATE", "Only pending payments can be confirmed.");
        Status = PaymentStatus.Confirmed;
        ConfirmedByUserId = userId;
        ConfirmedAtUtc = utcNow;
    }

    public void Fail(string reason)
    {
        if (Status != PaymentStatus.Pending) throw new DomainConflictException("PAYMENT_STATE", "Only pending payments can be marked failed.");
        Status = PaymentStatus.Failed;
        FailureReason = Guard.Required(reason, "REASON_REQUIRED", "A reason is required", 500);
    }
}

/// <summary>Immutable receipt for a confirmed payment, with a branch-unique number and content snapshot.</summary>
public class Receipt : BranchEntity
{
    private Receipt() { }

    public Receipt(Guid companyId, Guid branchId, Guid paymentId, string number, DateTime issuedAtUtc, string snapshotJson)
    {
        CompanyId = companyId;
        BranchId = branchId;
        PaymentId = paymentId;
        ReceiptNumber = number;
        IssuedAtUtc = issuedAtUtc;
        SnapshotJson = snapshotJson;
    }

    public Guid PaymentId { get; private set; }
    public string ReceiptNumber { get; private set; } = string.Empty;
    public DateTime IssuedAtUtc { get; private set; }
    public string SnapshotJson { get; private set; } = string.Empty;
}

/// <summary>Immutable invoice snapshot. Re-issuing creates a new version; old versions are kept.</summary>
public class Invoice : BranchEntity
{
    private Invoice() { }

    public Invoice(Guid companyId, Guid branchId, Guid folioId, string number, int version, DateTime issuedAtUtc,
        decimal totalCharges, decimal totalTax, decimal totalPaid, decimal balance, string snapshotJson)
    {
        CompanyId = companyId;
        BranchId = branchId;
        FolioId = folioId;
        InvoiceNumber = number;
        Version = version;
        IssuedAtUtc = issuedAtUtc;
        TotalCharges = totalCharges;
        TotalTax = totalTax;
        TotalPaid = totalPaid;
        Balance = balance;
        SnapshotJson = snapshotJson;
    }

    public Guid FolioId { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public DateTime IssuedAtUtc { get; private set; }
    public decimal TotalCharges { get; private set; }
    public decimal TotalTax { get; private set; }
    public decimal TotalPaid { get; private set; }
    public decimal Balance { get; private set; }
    public string SnapshotJson { get; private set; } = string.Empty;
}
