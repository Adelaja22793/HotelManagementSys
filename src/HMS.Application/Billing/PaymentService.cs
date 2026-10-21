using System.Text.Json;
using HMS.Application.Abstractions;
using HMS.Application.Accounting;
using HMS.Application.Cashier;
using HMS.Application.Common;
using HMS.Contracts.Billing;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Billing;

public interface IPaymentService
{
    Task<PaymentDto> RecordAsync(Guid branchId, Guid folioId, RecordPaymentRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<PaymentDto> VerifyAsync(Guid branchId, Guid paymentId, VerifyPaymentRequest request, CancellationToken ct = default);
    Task<ReceiptDto> GetReceiptAsync(Guid branchId, Guid receiptId, CancellationToken ct = default);

    Task<RefundDto> RequestRefundAsync(Guid branchId, Guid folioId, RequestRefundRequest request, CancellationToken ct = default);
    Task<RefundDto> DecideRefundAsync(Guid branchId, Guid refundId, RefundDecisionRequest request, CancellationToken ct = default);
    Task<RefundDto> ExecuteRefundAsync(Guid branchId, Guid refundId, ExecuteRefundRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<RefundDto>> ListRefundsAsync(Guid branchId, string? status, CancellationToken ct = default);

    /// <summary>
    /// Records a payment inside the caller's transaction (cash and POS need an open shift and are
    /// confirmed with a receipt at once; bank transfers stay pending). Used by other modules such as the restaurant.
    /// </summary>
    Task<Payment> CollectAsync(BranchScope scope, Folio folio, TenderType tender, decimal amount, string idempotencyKey,
        string? externalReference, CancellationToken ct);
}

internal sealed class PaymentService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    INumberSequence numbers,
    ICashierService cashier,
    ILedgerService ledger) : IPaymentService
{
    public async Task<PaymentDto> RecordAsync(Guid branchId, Guid folioId, RecordPaymentRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new ValidationAppException("Idempotency-Key", "An Idempotency-Key header (max 100 characters) is required for payments.");
        var tender = EnumParser.Parse<TenderType>(request.Tender, nameof(request.Tender));
        if (tender == TenderType.Online)
            throw new ValidationAppException(nameof(request.Tender), "Online payments are recorded by the payment provider integration, not by staff.");

        // Retried request: return the original result instead of collecting twice.
        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BranchId == branchId && p.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null) return await ReplayAsync(existing, folioId, request.Amount, tender, ct);

        var folio = await db.Folios.Include(f => f.Guest).Include(f => f.Reservation).GetOwnedAsync(branchId, folioId, "Folio", ct);
        folio.EnsureOpen();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await CollectAsync(scope, folio, tender, request.Amount, idempotencyKey, request.ExternalReference, ct);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // A concurrent retry with the same key won the unique index.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BranchId == branchId && p.IdempotencyKey == idempotencyKey, ct);
            if (winner is null) throw;
            return await ReplayAsync(winner, folioId, request.Amount, tender, ct);
        }
        return await ToDtoAsync(payment, ct);
    }

    public async Task<Payment> CollectAsync(BranchScope scope, Folio folio, TenderType tender, decimal amount, string idempotencyKey,
        string? externalReference, CancellationToken ct)
    {
        db.RequireTransaction();
        folio.EnsureOpen();
        if (tender == TenderType.Online)
            throw new ValidationAppException("Tender", "Online payments are recorded by the payment provider integration, not by staff.");
        var payment = new Payment(scope.CompanyId, scope.BranchId, folio.Id, tender, amount, folio.Currency, idempotencyKey,
            scope.UserId, scope.UtcNow, externalReference);

        if (tender is TenderType.Cash or TenderType.PosCard)
        {
            if (tender == TenderType.PosCard && string.IsNullOrWhiteSpace(externalReference))
                throw new ValidationAppException("ExternalReference", "Enter the POS terminal approval/slip reference.");

            // Payment and cashier movement commit together; touching the shift blocks a concurrent close.
            var shift = await cashier.RequireOpenShiftAsync(scope, ct);
            payment.LinkShift(shift.Id);
            payment.Confirm(scope.UserId, scope.UtcNow);
            db.CashierMovements.Add(new CashierMovement(scope.CompanyId, scope.BranchId, shift.Id, CashierMovementType.Payment, tender,
                payment.Amount, scope.UtcNow, paymentId: payment.Id));
        }
        else if (string.IsNullOrWhiteSpace(externalReference))
        {
            throw new ValidationAppException("ExternalReference", "Enter the bank transfer reference.");
        }
        // Bank transfers stay Pending until verified against the bank statement.

        db.Payments.Add(payment);
        db.Touch(folio);
        if (payment.Status == PaymentStatus.Confirmed) await IssueReceiptAsync(scope, folio, payment, ct);
        return payment;
    }

    public async Task<PaymentDto> VerifyAsync(Guid branchId, Guid paymentId, VerifyPaymentRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var payment = await db.Payments.GetOwnedAsync(branchId, paymentId, "Payment", ct);
        if (payment.Tender != TenderType.BankTransfer)
            throw new ConflictException("PAYMENT_STATE", "Only bank transfers need verification.");
        var folio = await db.Folios.Include(f => f.Guest).Include(f => f.Reservation).SingleAsync(f => f.Id == payment.FolioId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (request.Confirmed)
        {
            payment.Confirm(scope.UserId, scope.UtcNow);
            await IssueReceiptAsync(scope, folio, payment, ct);
        }
        else
        {
            payment.Fail(request.FailureReason ?? string.Empty);
        }
        db.Touch(folio);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ToDtoAsync(payment, ct);
    }

    public async Task<ReceiptDto> GetReceiptAsync(Guid branchId, Guid receiptId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var r = await db.Receipts.AsNoTracking().GetOwnedAsync(branchId, receiptId, "Receipt", ct);
        return new ReceiptDto(r.Id, r.ReceiptNumber, r.PaymentId, r.IssuedAtUtc, r.SnapshotJson);
    }

    public async Task<RefundDto> RequestRefundAsync(Guid branchId, Guid folioId, RequestRefundRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var tender = EnumParser.Parse<TenderType>(request.Tender, nameof(request.Tender));
        var folio = await db.Folios.GetOwnedAsync(branchId, folioId, "Folio", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Touch(folio); // serialises concurrent refund requests on the same folio

        if (request.PaymentId is { } paymentId)
        {
            var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == paymentId && p.FolioId == folio.Id, ct)
                ?? throw new NotFoundException("Payment", paymentId);
            if (payment.Status != PaymentStatus.Confirmed) throw new ConflictException("PAYMENT_STATE", "Only confirmed payments can be refunded.");
            var refundedAgainst = await db.Refunds.Where(r => r.PaymentId == paymentId && r.Status != RefundStatus.Rejected).Select(r => r.Amount).SumClientAsync(ct);
            if (refundedAgainst + request.Amount > payment.Amount)
                throw new ConflictException("REFUND_EXCEEDS_PAYMENT", "The refund exceeds the refundable amount of the payment.");
        }

        var confirmed = await db.Payments.Where(p => p.FolioId == folio.Id && p.Status == PaymentStatus.Confirmed).Select(p => p.Amount).SumClientAsync(ct);
        var outstanding = await db.Refunds.Where(r => r.FolioId == folio.Id && r.Status != RefundStatus.Rejected).Select(r => r.Amount).SumClientAsync(ct);
        if (outstanding + request.Amount > confirmed)
            throw new ConflictException("REFUND_EXCEEDS_PAYMENTS", "Refunds cannot exceed the confirmed payments on this folio.");

        var number = await numbers.NextAsync(scope.Branch, SequenceNames.Refund, ct);
        var refund = new Refund(scope.CompanyId, branchId, folio.Id, request.PaymentId, number, request.Amount, tender, request.Reason, scope.UserId, scope.UtcNow);
        db.Refunds.Add(refund);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(refund);
    }

    public async Task<RefundDto> DecideRefundAsync(Guid branchId, Guid refundId, RefundDecisionRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var refund = await db.Refunds.GetOwnedAsync(branchId, refundId, "Refund", ct);
        BranchOperations.EnsureStamp(refund, request.ConcurrencyStamp, "refund");
        if (request.Approved) refund.Approve(scope.UserId, scope.UtcNow);
        else refund.Reject(scope.UserId, request.RejectionReason ?? string.Empty, scope.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToDto(refund);
    }

    public async Task<RefundDto> ExecuteRefundAsync(Guid branchId, Guid refundId, ExecuteRefundRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var refund = await db.Refunds.GetOwnedAsync(branchId, refundId, "Refund", ct);
        BranchOperations.EnsureStamp(refund, request.ConcurrencyStamp, "refund");
        var folio = await db.Folios.SingleAsync(f => f.Id == refund.FolioId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        Guid? shiftId = null;
        if (refund.Tender is TenderType.Cash or TenderType.PosCard)
        {
            var shift = await cashier.RequireOpenShiftAsync(scope, ct);
            shiftId = shift.Id;
            db.CashierMovements.Add(new CashierMovement(scope.CompanyId, branchId, shift.Id, CashierMovementType.Refund, refund.Tender,
                -refund.Amount, scope.UtcNow, refundId: refund.Id));
        }
        else if (string.IsNullOrWhiteSpace(request.ExternalReference))
        {
            throw new ValidationAppException(nameof(request.ExternalReference), "Enter the transfer reference for the refund.");
        }

        refund.Execute(scope.UserId, scope.UtcNow, request.ExternalReference, shiftId);
        await ledger.PostRefundAsync(scope, folio, refund, ct);
        db.Touch(folio);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(refund);
    }

    public async Task<IReadOnlyList<RefundDto>> ListRefundsAsync(Guid branchId, string? status, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.Refunds.AsNoTracking().Where(r => r.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = EnumParser.Parse<RefundStatus>(status, nameof(status));
            q = q.Where(r => r.Status == parsed);
        }
        return (await q.OrderByDescending(r => r.RequestedAtUtc).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    private async Task IssueReceiptAsync(BranchScope scope, Folio folio, Payment payment, CancellationToken ct)
    {
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.Receipt, ct);
        var snapshot = JsonSerializer.Serialize(new
        {
            receiptNumber = number,
            branch = new { scope.Branch.Code, scope.Branch.Name, scope.Branch.AddressLine, scope.Branch.Phone },
            folio = folio.FolioNumber,
            reservation = folio.Reservation?.ReservationNumber,
            guest = folio.Guest?.FullName,
            tender = payment.Tender.ToString(),
            reference = payment.ExternalReference,
            amount = payment.Amount,
            currency = payment.Currency,
            confirmedAtUtc = payment.ConfirmedAtUtc,
        });
        db.Receipts.Add(new Receipt(scope.CompanyId, scope.BranchId, payment.Id, number, scope.UtcNow, snapshot));
        await ledger.PostPaymentAsync(scope, folio, payment, ct); // only confirmed money reaches the ledger
    }

    private async Task<PaymentDto> ReplayAsync(Payment existing, Guid folioId, decimal amount, TenderType tender, CancellationToken ct)
    {
        if (existing.FolioId != folioId || existing.Amount != amount || existing.Tender != tender)
            throw new ConflictException("IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was already used for a different payment.");
        return await ToDtoAsync(existing, ct);
    }

    private async Task<PaymentDto> ToDtoAsync(Payment p, CancellationToken ct)
    {
        var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(r => r.PaymentId == p.Id, ct);
        return new PaymentDto(p.Id, p.Tender.ToString(), p.Amount, p.Currency, p.Status.ToString(), p.ExternalReference,
            p.ReceivedAtUtc, p.ConfirmedAtUtc, receipt?.ReceiptNumber, receipt?.Id);
    }

    internal static RefundDto ToDto(Refund r) => new(r.Id, r.RefundNumber, r.Amount, r.Tender.ToString(), r.Reason, r.Status.ToString(),
        r.RequestedByUserId, r.ApprovedByUserId, r.RequestedAtUtc, r.ExecutedAtUtc, r.ConcurrencyStamp);
}
