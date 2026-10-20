namespace HMS.Contracts.Billing;

public sealed record FolioChargeDto(Guid Id, string Type, string Description, decimal Quantity, decimal UnitPrice, decimal Amount,
    DateOnly BusinessDate, DateTime PostedAtUtc, Guid? ReversesChargeId, bool IsReversed, Guid? ParentChargeId);

/// <summary>Tender: Cash, PosCard, BankTransfer, Online. Status: Pending, Confirmed, Failed.</summary>
public sealed record PaymentDto(Guid Id, string Tender, decimal Amount, string Currency, string Status, string? ExternalReference,
    DateTime ReceivedAtUtc, DateTime? ConfirmedAtUtc, string? ReceiptNumber, Guid? ReceiptId);

/// <summary>Status: Requested, Approved, Rejected, Executed.</summary>
public sealed record RefundDto(Guid Id, string RefundNumber, decimal Amount, string Tender, string Reason, string Status,
    string RequestedByUserId, string? ApprovedByUserId, DateTime RequestedAtUtc, DateTime? ExecutedAtUtc, Guid ConcurrencyStamp);

public sealed record FolioDto(
    Guid Id,
    string FolioNumber,
    Guid ReservationId,
    string ReservationNumber,
    Guid? StayId,
    string GuestName,
    string Currency,
    string Status,
    decimal TotalCharges,
    decimal TotalTax,
    decimal ConfirmedPayments,
    decimal PendingPayments,
    decimal ExecutedRefunds,
    decimal Balance,
    IReadOnlyList<FolioChargeDto> Charges,
    IReadOnlyList<PaymentDto> Payments,
    IReadOnlyList<RefundDto> Refunds);

/// <summary>Post a catalogue service (ServiceItemId) or, with folios.adjust, a custom adjustment.</summary>
public sealed record PostChargeRequest(Guid? ServiceItemId, string? Description, decimal Quantity, decimal? UnitPrice);

public sealed record ReverseChargeRequest(string Reason);

/// <summary>Send an Idempotency-Key header. Cash requires an open cashier shift.</summary>
public sealed record RecordPaymentRequest(string Tender, decimal Amount, string? ExternalReference);

public sealed record VerifyPaymentRequest(bool Confirmed, string? FailureReason);

public sealed record RequestRefundRequest(Guid? PaymentId, decimal Amount, string Tender, string Reason);

public sealed record RefundDecisionRequest(bool Approved, string? RejectionReason, Guid ConcurrencyStamp);

public sealed record ExecuteRefundRequest(string? ExternalReference, Guid ConcurrencyStamp);

public sealed record ReceiptDto(Guid Id, string ReceiptNumber, Guid PaymentId, DateTime IssuedAtUtc, string SnapshotJson);

public sealed record InvoiceDto(Guid Id, string InvoiceNumber, int Version, Guid FolioId, DateTime IssuedAtUtc,
    decimal TotalCharges, decimal TotalTax, decimal TotalPaid, decimal Balance, string SnapshotJson);
