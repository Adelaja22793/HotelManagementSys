namespace HMS.Contracts.Accounting;

/// <summary>Type: Asset, Liability, Equity, Revenue, Expense.</summary>
public sealed record AccountDto(Guid Id, string Code, string Name, string Type, bool IsControl, bool IsActive, Guid ConcurrencyStamp);

public sealed record SaveAccountRequest(string Code, string Name, string Type, bool IsControl, bool IsActive = true, Guid? ConcurrencyStamp = null);

public sealed record AccountMappingDto(string Key, Guid AccountId, string AccountCode, string AccountName);

public sealed record SetAccountMappingRequest(string Key, Guid AccountId);

/// <summary>Status: Open, Closed.</summary>
public sealed record AccountingPeriodDto(Guid Id, int Year, int Month, DateOnly StartDate, DateOnly EndDate, string Status,
    string? ClosedByUserId, DateTime? ClosedAtUtc, Guid ConcurrencyStamp);

public sealed record PeriodActionRequest(Guid ConcurrencyStamp);

public sealed record JournalLineRequest(Guid AccountId, decimal Debit, decimal Credit, string? Memo);

/// <summary>Manual journal. It must balance, fall in an open period and avoid control accounts.</summary>
public sealed record CreateJournalRequest(DateOnly EntryDate, string Description, IReadOnlyList<JournalLineRequest> Lines);

public sealed record ReverseJournalRequest(string Reason);

public sealed record JournalLineDto(Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit, string? Memo);

public sealed record JournalEntryDto(Guid Id, string EntryNumber, Guid BranchId, DateOnly EntryDate, string Description, string? SourceType,
    string? SourceId, Guid? ReversesEntryId, Guid? ReversedByEntryId, string PostedByUserId, DateTime PostedAtUtc,
    decimal TotalDebit, decimal TotalCredit, IReadOnlyList<JournalLineDto> Lines);

public sealed record TrialBalanceLineDto(Guid AccountId, string Code, string Name, string Type, decimal Debit, decimal Credit, decimal Balance);

/// <summary>Consolidated = every branch of the company (needs access to the company's branches).</summary>
public sealed record TrialBalanceDto(DateOnly From, DateOnly To, bool Consolidated, IReadOnlyList<TrialBalanceLineDto> Lines,
    decimal TotalDebit, decimal TotalCredit);

public sealed record LedgerLineDto(Guid JournalEntryId, string EntryNumber, DateOnly EntryDate, string Description, decimal Debit,
    decimal Credit, decimal RunningBalance);
