using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>
/// A cashier's working session. Expected totals are always computed from recorded movements; counted
/// amounts are what the cashier declares. Lifecycle: Open → Closing → Closed (variance needs approval).
/// </summary>
public class CashierShift : BranchEntity, IConcurrencyTracked
{
    private CashierShift() { }

    public CashierShift(Guid companyId, Guid branchId, string cashierUserId, decimal openingFloat, DateTime utcNow, DateOnly businessDate)
    {
        if (openingFloat < 0) throw new DomainException("FLOAT_INVALID", "Opening float cannot be negative.");
        CompanyId = companyId;
        BranchId = branchId;
        CashierUserId = cashierUserId;
        OpeningFloat = openingFloat;
        OpenedAtUtc = utcNow;
        BusinessDate = businessDate;
    }

    public string CashierUserId { get; private set; } = string.Empty;
    public CashierShiftStatus Status { get; private set; } = CashierShiftStatus.Open;
    public DateOnly BusinessDate { get; private set; }
    public DateTime OpenedAtUtc { get; private set; }
    public decimal OpeningFloat { get; private set; }
    public DateTime? ClosingStartedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }

    public decimal? ExpectedCash { get; private set; }
    public decimal? ExpectedPos { get; private set; }
    public decimal? ExpectedTransfer { get; private set; }
    public decimal? CountedCash { get; private set; }
    public decimal? VerifiedPos { get; private set; }
    public decimal? VerifiedTransfer { get; private set; }
    public decimal? CashVariance { get; private set; }
    public decimal? PosVariance { get; private set; }
    public decimal? TransferVariance { get; private set; }
    public string? VarianceExplanation { get; private set; }
    public string? ApprovedByUserId { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool HasVariance => (CashVariance ?? 0) != 0 || (PosVariance ?? 0) != 0 || (TransferVariance ?? 0) != 0;

    public void EnsureOpen()
    {
        if (Status != CashierShiftStatus.Open)
            throw new DomainConflictException("SHIFT_NOT_OPEN", "The cashier shift is not open for new transactions.");
    }

    /// <summary>
    /// Locks the shift against new transactions and records counts against expected totals.
    /// Returns true when the shift closed immediately (no variance).
    /// </summary>
    public bool SubmitCount(DateTime utcNow, decimal expectedCash, decimal expectedPos, decimal expectedTransfer,
        decimal countedCash, decimal verifiedPos, decimal verifiedTransfer, string? explanation)
    {
        EnsureOpen();
        if (countedCash < 0 || verifiedPos < 0 || verifiedTransfer < 0)
            throw new DomainException("COUNT_INVALID", "Counted amounts cannot be negative.");

        Status = CashierShiftStatus.Closing;
        ClosingStartedAtUtc = utcNow;
        ExpectedCash = expectedCash;
        ExpectedPos = expectedPos;
        ExpectedTransfer = expectedTransfer;
        CountedCash = countedCash;
        VerifiedPos = verifiedPos;
        VerifiedTransfer = verifiedTransfer;
        CashVariance = countedCash - expectedCash;
        PosVariance = verifiedPos - expectedPos;
        TransferVariance = verifiedTransfer - expectedTransfer;
        VarianceExplanation = string.IsNullOrWhiteSpace(explanation) ? null : explanation.Trim();

        if (HasVariance)
        {
            if (VarianceExplanation is null)
                throw new DomainException("VARIANCE_EXPLANATION_REQUIRED", "Explain the variance before submitting the count.");
            return false;
        }

        Status = CashierShiftStatus.Closed;
        ClosedAtUtc = utcNow;
        return true;
    }

    public void ApproveVariance(string approverId, DateTime utcNow)
    {
        if (Status != CashierShiftStatus.Closing) throw new DomainConflictException("SHIFT_STATE", "The shift is not awaiting variance approval.");
        if (approverId == CashierUserId) throw new DomainConflictException("SELF_APPROVAL", "A cashier cannot approve their own variance.");
        ApprovedByUserId = approverId;
        Status = CashierShiftStatus.Closed;
        ClosedAtUtc = utcNow;
    }
}

/// <summary>Immutable money movement within a shift. Signed: refunds are negative.</summary>
public class CashierMovement : BranchEntity
{
    private CashierMovement() { }

    public CashierMovement(Guid companyId, Guid branchId, Guid shiftId, CashierMovementType type, TenderType tender,
        decimal amount, DateTime utcNow, Guid? paymentId = null, Guid? refundId = null)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ShiftId = shiftId;
        Type = type;
        Tender = tender;
        Amount = amount;
        OccurredAtUtc = utcNow;
        PaymentId = paymentId;
        RefundId = refundId;
    }

    public Guid ShiftId { get; private set; }
    public CashierMovementType Type { get; private set; }
    public TenderType Tender { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? PaymentId { get; private set; }
    public Guid? RefundId { get; private set; }
}
