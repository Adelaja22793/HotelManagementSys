using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>Billing container for a reservation/stay. Its balance is always computed from charge, payment and refund records.</summary>
public class Folio : BranchEntity, IConcurrencyTracked
{
    private Folio() { }

    public Folio(Guid companyId, Guid branchId, string number, Guid reservationId, Guid guestId, string currency)
    {
        CompanyId = companyId;
        BranchId = branchId;
        FolioNumber = number;
        ReservationId = reservationId;
        GuestId = guestId;
        Currency = Common.Currency.Normalize(currency);
    }

    public string FolioNumber { get; private set; } = string.Empty;
    public Guid ReservationId { get; private set; }
    public Reservation? Reservation { get; private set; }
    public Guid? StayId { get; private set; }
    public Guid GuestId { get; private set; }
    public Guest? Guest { get; private set; }
    public string Currency { get; private set; } = Common.Currency.Default;
    public FolioStatus Status { get; private set; } = FolioStatus.Open;
    public DateTime? ClosedAtUtc { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void AttachStay(Guid stayId) => StayId = stayId;

    public void EnsureOpen()
    {
        if (Status != FolioStatus.Open) throw new DomainConflictException("FOLIO_CLOSED", "The folio is closed.");
    }

    public void Close(DateTime utcNow)
    {
        EnsureOpen();
        Status = FolioStatus.Closed;
        ClosedAtUtc = utcNow;
    }

    public void Reopen()
    {
        Status = FolioStatus.Open;
        ClosedAtUtc = null;
    }
}

/// <summary>
/// Immutable posted charge. Corrections are made by posting a reversal (negative) charge that points
/// at the original, never by editing or deleting.
/// </summary>
public class FolioCharge : BranchEntity
{
    private FolioCharge() { }

    public FolioCharge(Guid companyId, Guid branchId, Guid folioId, ChargeType type, string description, decimal quantity,
        decimal unitPrice, DateOnly businessDate, string? idempotencyKey, Guid? serviceItemId = null)
    {
        if (quantity == 0) throw new DomainException("QUANTITY_INVALID", "Quantity cannot be zero.");
        CompanyId = companyId;
        BranchId = branchId;
        FolioId = folioId;
        Type = type;
        Description = Guard.Required(description, "DESCRIPTION_REQUIRED", "Description is required", 300);
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = Money.Round(quantity * unitPrice);
        BusinessDate = businessDate;
        IdempotencyKey = idempotencyKey;
        ServiceItemId = serviceItemId;
    }

    public Guid FolioId { get; private set; }
    public ChargeType Type { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    /// <summary>Signed amount; reversals are negative.</summary>
    public decimal Amount { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public Guid? ServiceItemId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public Guid? ReversesChargeId { get; private set; }
    public Guid? ReversedByChargeId { get; private set; }
    /// <summary>Tax charge generated for this charge, so reversing one reverses both.</summary>
    public Guid? ParentChargeId { get; private set; }

    public bool IsReversed => ReversedByChargeId is not null;

    public void LinkToParent(Guid parentChargeId) => ParentChargeId = parentChargeId;

    public FolioCharge CreateReversal(DateOnly businessDate, string reason)
    {
        if (ReversesChargeId is not null) throw new DomainConflictException("CHARGE_IS_REVERSAL", "A reversal cannot itself be reversed.");
        if (IsReversed) throw new DomainConflictException("CHARGE_ALREADY_REVERSED", "The charge has already been reversed.");
        var text = $"Reversal: {Description} ({reason})";
        var reversal = new FolioCharge(CompanyId, BranchId, FolioId, Type, text.Length > 300 ? text[..300] : text,
            -Quantity, UnitPrice, businessDate, $"REV:{Id}")
        {
            ReversesChargeId = Id,
            ServiceItemId = ServiceItemId,
        };
        ReversedByChargeId = reversal.Id;
        return reversal;
    }
}
