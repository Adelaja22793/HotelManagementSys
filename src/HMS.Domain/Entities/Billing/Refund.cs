using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>Refund with separate request, approval and execution. The approver cannot be the requester.</summary>
public class Refund : BranchEntity, IConcurrencyTracked
{
    private Refund() { }

    public Refund(Guid companyId, Guid branchId, Guid folioId, Guid? paymentId, string number, decimal amount, TenderType tender,
        string reason, string requestedByUserId, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        FolioId = folioId;
        PaymentId = paymentId;
        RefundNumber = number;
        Amount = Money.RequirePositive(amount);
        Tender = tender;
        Reason = Guard.Required(reason, "REASON_REQUIRED", "A refund reason is required", 500);
        RequestedByUserId = requestedByUserId;
        RequestedAtUtc = utcNow;
    }

    public Guid FolioId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string RefundNumber { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public TenderType Tender { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public RefundStatus Status { get; private set; } = RefundStatus.Requested;
    public string RequestedByUserId { get; private set; } = string.Empty;
    public DateTime RequestedAtUtc { get; private set; }
    public string? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? ExecutedByUserId { get; private set; }
    public DateTime? ExecutedAtUtc { get; private set; }
    public string? ExternalReference { get; private set; }
    public Guid? CashierShiftId { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Approve(string approverId, DateTime utcNow)
    {
        if (Status != RefundStatus.Requested) throw new DomainConflictException("REFUND_STATE", "Only requested refunds can be approved.");
        if (approverId == RequestedByUserId) throw new DomainConflictException("SELF_APPROVAL", "A refund must be approved by someone other than the requester.");
        Status = RefundStatus.Approved;
        ApprovedByUserId = approverId;
        ApprovedAtUtc = utcNow;
    }

    public void Reject(string approverId, string reason, DateTime utcNow)
    {
        if (Status != RefundStatus.Requested) throw new DomainConflictException("REFUND_STATE", "Only requested refunds can be rejected.");
        Status = RefundStatus.Rejected;
        ApprovedByUserId = approverId;
        ApprovedAtUtc = utcNow;
        RejectionReason = Guard.Required(reason, "REASON_REQUIRED", "A rejection reason is required", 500);
    }

    public void Execute(string userId, DateTime utcNow, string? externalReference, Guid? shiftId)
    {
        if (Status != RefundStatus.Approved) throw new DomainConflictException("REFUND_STATE", "Only approved refunds can be paid out.");
        Status = RefundStatus.Executed;
        ExecutedByUserId = userId;
        ExecutedAtUtc = utcNow;
        ExternalReference = string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim();
        CashierShiftId = shiftId;
    }
}
