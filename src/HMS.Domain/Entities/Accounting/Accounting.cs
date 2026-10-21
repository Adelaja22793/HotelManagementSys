using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>Company-wide ledger account. Control accounts are only posted by sub-ledgers, never by manual journals.</summary>
public class Account : AuditableEntity, IConcurrencyTracked
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public bool IsControl { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    /// <summary>Assets and expenses grow with debits; the rest grow with credits.</summary>
    public bool IsDebitNormal => Type is AccountType.Asset or AccountType.Expense;
}

/// <summary>Maps a posting purpose (e.g. RoomRevenue) to the company's account for it.</summary>
public class AccountMapping : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public string Key { get; set; } = string.Empty;
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
}

/// <summary>Monthly company accounting period. Postings are only accepted while it is open.</summary>
public class AccountingPeriod : AuditableEntity, IConcurrencyTracked
{
    public Guid CompanyId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public PeriodStatus Status { get; set; } = PeriodStatus.Open;
    public string? ClosedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>
/// A posted, balanced, immutable journal. Corrections are new reversing journals. The unique
/// (BranchId, SourceType, SourceId) key means an operational event can only ever be posted once.
/// </summary>
public class JournalEntry : IBranchOwned
{
    private JournalEntry() { }

    public JournalEntry(Guid companyId, Guid branchId, string number, Guid periodId, DateOnly entryDate, string description,
        string? sourceType, string? sourceId, string userId, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        EntryNumber = number;
        PeriodId = periodId;
        EntryDate = entryDate;
        Description = Guard.Required(description, "DESCRIPTION_REQUIRED", "Description is required", 300);
        SourceType = sourceType;
        SourceId = sourceId;
        PostedByUserId = userId;
        PostedAtUtc = utcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public string EntryNumber { get; private set; } = string.Empty;
    public Guid PeriodId { get; private set; }
    public DateOnly EntryDate { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? SourceType { get; private set; }
    public string? SourceId { get; private set; }
    public Guid? ReversesEntryId { get; private set; }
    public Guid? ReversedByEntryId { get; private set; }
    public string PostedByUserId { get; private set; } = string.Empty;
    public DateTime PostedAtUtc { get; private set; }
    public List<JournalEntryLine> Lines { get; private set; } = [];

    public void AddLine(Guid accountId, decimal debit, decimal credit, string? memo)
    {
        if (debit < 0 || credit < 0 || (debit == 0) == (credit == 0))
            throw new DomainException("JOURNAL_LINE_INVALID", "Each journal line needs either a debit or a credit amount.");
        if (Money.Round(debit) != debit || Money.Round(credit) != credit)
            throw new DomainException("JOURNAL_LINE_INVALID", "Journal amounts can have at most 2 decimal places.");
        Lines.Add(new JournalEntryLine { JournalEntryId = Id, AccountId = accountId, Debit = debit, Credit = credit, Memo = memo, LineNumber = Lines.Count + 1 });
    }

    public void EnsureBalanced()
    {
        if (Lines.Count < 2) throw new DomainException("JOURNAL_UNBALANCED", "A journal needs at least two lines.");
        var debits = Lines.Sum(l => l.Debit);
        var credits = Lines.Sum(l => l.Credit);
        if (debits != credits)
            throw new DomainException("JOURNAL_UNBALANCED", $"Debits ({debits:N2}) must equal credits ({credits:N2}).");
    }

    public void MarkReversal(Guid originalId) => ReversesEntryId = originalId;

    public void MarkReversedBy(Guid reversalId)
    {
        if (ReversedByEntryId is not null) throw new DomainConflictException("JOURNAL_ALREADY_REVERSED", "The journal has already been reversed.");
        if (ReversesEntryId is not null) throw new DomainConflictException("JOURNAL_IS_REVERSAL", "A reversal cannot itself be reversed.");
        ReversedByEntryId = reversalId;
    }
}

public class JournalEntryLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JournalEntryId { get; set; }
    public int LineNumber { get; set; }
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Memo { get; set; }
}
