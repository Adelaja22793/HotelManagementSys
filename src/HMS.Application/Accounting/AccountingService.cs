using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Contracts.Accounting;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Accounting;

public interface IAccountingService
{
    Task<IReadOnlyList<AccountDto>> ListAccountsAsync(Guid branchId, CancellationToken ct = default);
    Task<AccountDto> CreateAccountAsync(Guid branchId, SaveAccountRequest request, CancellationToken ct = default);
    Task<AccountDto> UpdateAccountAsync(Guid branchId, Guid accountId, SaveAccountRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AccountMappingDto>> ListMappingsAsync(Guid branchId, CancellationToken ct = default);
    Task<AccountMappingDto> SetMappingAsync(Guid branchId, SetAccountMappingRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AccountingPeriodDto>> ListPeriodsAsync(Guid branchId, CancellationToken ct = default);
    Task<AccountingPeriodDto> ClosePeriodAsync(Guid branchId, Guid periodId, PeriodActionRequest request, CancellationToken ct = default);
    Task<AccountingPeriodDto> ReopenPeriodAsync(Guid branchId, Guid periodId, PeriodActionRequest request, CancellationToken ct = default);
    Task<PagedResultLite<JournalEntryDto>> ListJournalsAsync(Guid branchId, DateOnly? from, DateOnly? to, string? sourceType, int page, int pageSize, CancellationToken ct = default);
    Task<JournalEntryDto> GetJournalAsync(Guid branchId, Guid journalId, CancellationToken ct = default);
    Task<JournalEntryDto> CreateManualJournalAsync(Guid branchId, CreateJournalRequest request, CancellationToken ct = default);
    Task<JournalEntryDto> ReverseJournalAsync(Guid branchId, Guid journalId, ReverseJournalRequest request, CancellationToken ct = default);
    Task<TrialBalanceDto> TrialBalanceAsync(Guid branchId, DateOnly from, DateOnly to, bool consolidated, CancellationToken ct = default);
    Task<IReadOnlyList<LedgerLineDto>> AccountLedgerAsync(Guid branchId, Guid accountId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>Minimal paged list for journals.</summary>
public sealed record PagedResultLite<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal sealed class AccountingService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    ILedgerService ledger,
    INumberSequence numbers) : IAccountingService
{
    public async Task<IReadOnlyList<AccountDto>> ListAccountsAsync(Guid branchId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId).OrderBy(a => a.Code).ToListAsync(ct);
        return accounts.Select(ToDto).ToList();
    }

    public async Task<AccountDto> CreateAccountAsync(Guid branchId, SaveAccountRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        Validate(request);
        var code = request.Code.Trim();
        if (await db.Accounts.AnyAsync(a => a.CompanyId == scope.CompanyId && a.Code == code, ct))
            throw new ConflictException("ACCOUNT_CODE_EXISTS", $"Account {code} already exists.");
        var account = new Account
        {
            CompanyId = scope.CompanyId, Code = code, Name = request.Name.Trim(),
            Type = EnumParser.Parse<AccountType>(request.Type, nameof(request.Type)), IsControl = request.IsControl, IsActive = request.IsActive,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return ToDto(account);
    }

    public async Task<AccountDto> UpdateAccountAsync(Guid branchId, Guid accountId, SaveAccountRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        Validate(request);
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId && a.CompanyId == scope.CompanyId, ct)
            ?? throw new NotFoundException("Account", accountId);
        if (request.ConcurrencyStamp is { } stamp) BranchOperations.EnsureStamp(account, stamp, "account");
        var type = EnumParser.Parse<AccountType>(request.Type, nameof(request.Type));
        var used = await db.JournalEntryLines.AnyAsync(l => l.AccountId == account.Id, ct);
        if (used && (type != account.Type || request.Code.Trim() != account.Code))
            throw new ConflictException("ACCOUNT_IN_USE", "An account with postings cannot change its code or type. Create a new account instead.");
        if (!request.IsActive && await db.AccountMappings.AnyAsync(m => m.AccountId == account.Id, ct))
            throw new ConflictException("ACCOUNT_MAPPED", "Remap the posting keys that use this account before deactivating it.");
        account.Code = request.Code.Trim();
        account.Name = request.Name.Trim();
        account.Type = type;
        account.IsControl = request.IsControl;
        account.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(account);
    }

    public async Task<IReadOnlyList<AccountMappingDto>> ListMappingsAsync(Guid branchId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var mappings = await db.AccountMappings.AsNoTracking().Include(m => m.Account).Where(m => m.CompanyId == scope.CompanyId)
            .OrderBy(m => m.Key).ToListAsync(ct);
        return mappings.Select(m => new AccountMappingDto(m.Key, m.AccountId, m.Account!.Code, m.Account.Name)).ToList();
    }

    public async Task<AccountMappingDto> SetMappingAsync(Guid branchId, SetAccountMappingRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (!DefaultChart.Keys.Contains(request.Key))
            throw new ValidationAppException(nameof(request.Key), $"Unknown posting key. Use one of: {string.Join(", ", DefaultChart.Keys.Order())}.");
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == request.AccountId && a.CompanyId == scope.CompanyId && a.IsActive, ct)
            ?? throw new NotFoundException("Account", request.AccountId);
        var mapping = await db.AccountMappings.SingleOrDefaultAsync(m => m.CompanyId == scope.CompanyId && m.Key == request.Key, ct);
        if (mapping is null) db.AccountMappings.Add(mapping = new AccountMapping { CompanyId = scope.CompanyId, Key = request.Key });
        mapping.AccountId = account.Id;
        await db.SaveChangesAsync(ct);
        return new AccountMappingDto(mapping.Key, account.Id, account.Code, account.Name);
    }

    public async Task<IReadOnlyList<AccountingPeriodDto>> ListPeriodsAsync(Guid branchId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(p => p.CompanyId == scope.CompanyId)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToListAsync(ct);
        return periods.Select(ToDto).ToList();
    }

    public async Task<AccountingPeriodDto> ClosePeriodAsync(Guid branchId, Guid periodId, PeriodActionRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var period = await LoadPeriodAsync(scope, periodId, request.ConcurrencyStamp, ct);
        if (period.Status == PeriodStatus.Closed) throw new ConflictException("PERIOD_CLOSED", "The period is already closed.");
        if (period.EndDate >= scope.BusinessDate)
            throw new ConflictException("PERIOD_NOT_ENDED", "A period can only be closed after its last day has passed.");
        period.Status = PeriodStatus.Closed;
        period.ClosedByUserId = scope.UserId;
        period.ClosedAtUtc = scope.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(period);
    }

    public async Task<AccountingPeriodDto> ReopenPeriodAsync(Guid branchId, Guid periodId, PeriodActionRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var period = await LoadPeriodAsync(scope, periodId, request.ConcurrencyStamp, ct);
        if (period.Status == PeriodStatus.Open) throw new ConflictException("PERIOD_OPEN", "The period is already open.");
        period.Status = PeriodStatus.Open;
        period.ClosedByUserId = null;
        period.ClosedAtUtc = null;
        await db.SaveChangesAsync(ct);
        return ToDto(period);
    }

    public async Task<PagedResultLite<JournalEntryDto>> ListJournalsAsync(Guid branchId, DateOnly? from, DateOnly? to, string? sourceType,
        int page, int pageSize, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.JournalEntries.AsNoTracking().Where(j => j.BranchId == branchId);
        if (from is { } f) q = q.Where(j => j.EntryDate >= f);
        if (to is { } t) q = q.Where(j => j.EntryDate <= t);
        if (!string.IsNullOrWhiteSpace(sourceType)) q = q.Where(j => j.SourceType == sourceType);
        var total = await q.CountAsync(ct);
        var entries = await q.OrderByDescending(j => j.EntryDate).ThenByDescending(j => j.EntryNumber)
            .Skip((page - 1) * pageSize).Take(pageSize).Include(j => j.Lines).ThenInclude(l => l.Account).ToListAsync(ct);
        return new PagedResultLite<JournalEntryDto>(entries.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<JournalEntryDto> GetJournalAsync(Guid branchId, Guid journalId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var entry = await db.JournalEntries.AsNoTracking().Include(j => j.Lines).ThenInclude(l => l.Account)
            .SingleOrDefaultAsync(j => j.Id == journalId && j.BranchId == branchId, ct) ?? throw new NotFoundException("Journal", journalId);
        return ToDto(entry);
    }

    public async Task<JournalEntryDto> CreateManualJournalAsync(Guid branchId, CreateJournalRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var v = new Validation().Require(nameof(request.Description), request.Description, 300)
            .When(request.Lines is null || request.Lines.Count < 2, nameof(request.Lines), "A journal needs at least two lines.");
        v.ThrowIfInvalid();

        var accountIds = request.Lines!.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.Where(a => a.CompanyId == scope.CompanyId && accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var id in accountIds)
        {
            if (!accounts.TryGetValue(id, out var account)) throw new NotFoundException("Account", id);
            if (!account.IsActive) throw new ConflictException("ACCOUNT_INACTIVE", $"Account {account.Code} is inactive.");
            if (account.IsControl)
                throw new ConflictException("CONTROL_ACCOUNT", $"Account {account.Code} ({account.Name}) is a control account and is only posted by its sub-ledger.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var period = await ledger.OpenPeriodForAsync(scope.CompanyId, request.EntryDate, ct);
        var number = await NextJournalNumberAsync(scope, ct);
        var entry = new JournalEntry(scope.CompanyId, branchId, number, period.Id, request.EntryDate, request.Description, "Manual",
            Guid.NewGuid().ToString(), scope.UserId, scope.UtcNow);
        foreach (var line in request.Lines!) entry.AddLine(line.AccountId, line.Debit, line.Credit, line.Memo?.Trim());
        entry.EnsureBalanced();
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetJournalAsync(branchId, entry.Id, ct);
    }

    public async Task<JournalEntryDto> ReverseJournalAsync(Guid branchId, Guid journalId, ReverseJournalRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Reason), request.Reason, 200).ThrowIfInvalid();
        var original = await db.JournalEntries.Include(j => j.Lines).SingleOrDefaultAsync(j => j.Id == journalId && j.BranchId == branchId, ct)
            ?? throw new NotFoundException("Journal", journalId);
        if (original.SourceType != "Manual")
            throw new ConflictException("SYSTEM_JOURNAL", "This journal was posted by an operational module. Correct it there (for example by reversing the folio charge).");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var period = await ledger.OpenPeriodForAsync(scope.CompanyId, scope.BusinessDate, ct);
        var number = await NextJournalNumberAsync(scope, ct);
        var reversal = new JournalEntry(scope.CompanyId, branchId, number, period.Id, scope.BusinessDate,
            $"Reversal of {original.EntryNumber}: {request.Reason.Trim()}", "Reversal", original.Id.ToString(), scope.UserId, scope.UtcNow);
        foreach (var line in original.Lines.OrderBy(l => l.LineNumber)) reversal.AddLine(line.AccountId, line.Credit, line.Debit, line.Memo);
        reversal.EnsureBalanced();
        reversal.MarkReversal(original.Id);
        original.MarkReversedBy(reversal.Id);
        db.JournalEntries.Add(reversal);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetJournalAsync(branchId, reversal.Id, ct);
    }

    public async Task<TrialBalanceDto> TrialBalanceAsync(Guid branchId, DateOnly from, DateOnly to, bool consolidated, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (to < from) throw new ValidationAppException(nameof(to), "The end date must be on or after the start date.");
        var branchIds = consolidated
            ? (await access.GetAccessibleBranchIdsAsync(ct)).ToList()
            : [branchId];
        if (consolidated)
            branchIds = await db.Branches.Where(b => b.CompanyId == scope.CompanyId && branchIds.Contains(b.Id)).Select(b => b.Id).ToListAsync(ct);

        var lines = await db.JournalEntryLines.AsNoTracking()
            .Where(l => db.JournalEntries.Any(j => j.Id == l.JournalEntryId && branchIds.Contains(j.BranchId) && j.EntryDate >= from && j.EntryDate <= to))
            .Select(l => new { l.AccountId, l.Debit, l.Credit })
            .ToListAsync(ct);
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == scope.CompanyId).ToDictionaryAsync(a => a.Id, ct);

        var result = lines.GroupBy(l => l.AccountId)
            .Select(g =>
            {
                var a = accounts[g.Key];
                var debit = g.Sum(x => x.Debit);
                var credit = g.Sum(x => x.Credit);
                return new TrialBalanceLineDto(a.Id, a.Code, a.Name, a.Type.ToString(), debit, credit, a.IsDebitNormal ? debit - credit : credit - debit);
            })
            .OrderBy(l => l.Code)
            .ToList();
        return new TrialBalanceDto(from, to, consolidated, result, result.Sum(l => l.Debit), result.Sum(l => l.Credit));
    }

    public async Task<IReadOnlyList<LedgerLineDto>> AccountLedgerAsync(Guid branchId, Guid accountId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId && a.CompanyId == scope.CompanyId, ct)
            ?? throw new NotFoundException("Account", accountId);
        var rows = await db.JournalEntryLines.AsNoTracking()
            .Where(l => l.AccountId == accountId)
            .Join(db.JournalEntries.Where(j => j.BranchId == branchId && j.EntryDate >= from && j.EntryDate <= to),
                l => l.JournalEntryId, j => j.Id, (l, j) => new { j.Id, j.EntryNumber, j.EntryDate, j.Description, l.Debit, l.Credit })
            .ToListAsync(ct);
        decimal running = 0;
        return rows.OrderBy(r => r.EntryDate).ThenBy(r => r.EntryNumber).Select(r =>
        {
            running += account.IsDebitNormal ? r.Debit - r.Credit : r.Credit - r.Debit;
            return new LedgerLineDto(r.Id, r.EntryNumber, r.EntryDate, r.Description, r.Debit, r.Credit, running);
        }).Take(2000).ToList();
    }

    private async Task<string> NextJournalNumberAsync(BranchScope scope, CancellationToken ct) =>
        await numbers.NextAsync(scope.Branch, SequenceNames.Journal, ct);

    private async Task<AccountingPeriod> LoadPeriodAsync(BranchScope scope, Guid periodId, Guid stamp, CancellationToken ct)
    {
        var period = await db.AccountingPeriods.SingleOrDefaultAsync(p => p.Id == periodId && p.CompanyId == scope.CompanyId, ct)
            ?? throw new NotFoundException("Accounting period", periodId);
        BranchOperations.EnsureStamp(period, stamp, "period");
        return period;
    }

    private static void Validate(SaveAccountRequest request) =>
        new Validation().Require(nameof(request.Code), request.Code, 20).Require(nameof(request.Name), request.Name, 200).ThrowIfInvalid();

    internal static AccountDto ToDto(Account a) => new(a.Id, a.Code, a.Name, a.Type.ToString(), a.IsControl, a.IsActive, a.ConcurrencyStamp);

    private static AccountingPeriodDto ToDto(AccountingPeriod p) =>
        new(p.Id, p.Year, p.Month, p.StartDate, p.EndDate, p.Status.ToString(), p.ClosedByUserId, p.ClosedAtUtc, p.ConcurrencyStamp);

    internal static JournalEntryDto ToDto(JournalEntry j) => new(j.Id, j.EntryNumber, j.BranchId, j.EntryDate, j.Description, j.SourceType,
        j.SourceId, j.ReversesEntryId, j.ReversedByEntryId, j.PostedByUserId, j.PostedAtUtc, j.Lines.Sum(l => l.Debit), j.Lines.Sum(l => l.Credit),
        j.Lines.OrderBy(l => l.LineNumber).Select(l => new JournalLineDto(l.AccountId, l.Account?.Code ?? string.Empty, l.Account?.Name ?? string.Empty,
            l.Debit, l.Credit, l.Memo)).ToList());
}
