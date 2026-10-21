using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Accounting;

/// <summary>One side of a posting, addressed by account mapping key (see <see cref="AccountKeys"/>).</summary>
public sealed record LedgerLine(string AccountKey, decimal Debit, decimal Credit, string? Memo = null)
{
    public static LedgerLine Dr(string key, decimal amount, string? memo = null) => amount >= 0 ? new(key, amount, 0, memo) : new(key, 0, -amount, memo);
    public static LedgerLine Cr(string key, decimal amount, string? memo = null) => amount >= 0 ? new(key, 0, amount, memo) : new(key, -amount, 0, memo);
}

/// <summary>
/// The only way operational modules write to the general ledger. Every call runs inside the caller's
/// transaction, so the journal commits or rolls back together with the business record, and the
/// (source type, source id) key makes a retried operation post at most once.
/// </summary>
public interface ILedgerService
{
    Task<JournalEntry?> PostAsync(BranchScope scope, string sourceType, string sourceId, string description,
        IReadOnlyList<LedgerLine> lines, CancellationToken ct, DateOnly? entryDate = null);

    Task PostFolioChargesAsync(BranchScope scope, IReadOnlyList<FolioCharge> charges, string sourceType, string sourceId, string description, CancellationToken ct);

    Task PostPaymentAsync(BranchScope scope, Folio folio, Payment payment, CancellationToken ct);

    Task PostRefundAsync(BranchScope scope, Folio folio, Refund refund, CancellationToken ct);

    Task<Account> ResolveAsync(Guid companyId, string key, CancellationToken ct);

    Task<AccountingPeriod> OpenPeriodForAsync(Guid companyId, DateOnly date, CancellationToken ct);

    Task EnsureChartAsync(Guid companyId, CancellationToken ct);
}

internal sealed class LedgerService(IApplicationDbContext db, INumberSequence numbers) : ILedgerService
{
    private readonly Dictionary<(Guid, string), Account> _accounts = [];

    public async Task<JournalEntry?> PostAsync(BranchScope scope, string sourceType, string sourceId, string description,
        IReadOnlyList<LedgerLine> lines, CancellationToken ct, DateOnly? entryDate = null)
    {
        db.RequireTransaction();
        var existing = db.JournalEntries.Local.FirstOrDefault(j => j.BranchId == scope.BranchId && j.SourceType == sourceType && j.SourceId == sourceId)
            ?? await db.JournalEntries.SingleOrDefaultAsync(j => j.BranchId == scope.BranchId && j.SourceType == sourceType && j.SourceId == sourceId, ct);
        if (existing is not null) return existing;

        // Net each account to a single debit or credit line; drop zero lines.
        var netted = lines.GroupBy(l => l.AccountKey)
            .Select(g => (Key: g.Key, Net: g.Sum(l => l.Debit) - g.Sum(l => l.Credit), Memo: g.Select(l => l.Memo).FirstOrDefault(m => m is not null)))
            .Where(x => x.Net != 0)
            .ToList();
        if (netted.Count == 0) return null;

        var date = entryDate ?? scope.BusinessDate;
        var period = await OpenPeriodForAsync(scope.CompanyId, date, ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.Journal, ct);
        var entry = new JournalEntry(scope.CompanyId, scope.BranchId, number, period.Id, date, Trim(description), sourceType, sourceId, scope.UserId, scope.UtcNow);
        foreach (var (key, net, memo) in netted)
        {
            var account = await ResolveAsync(scope.CompanyId, key, ct);
            entry.AddLine(account.Id, net > 0 ? net : 0, net < 0 ? -net : 0, memo);
        }
        entry.EnsureBalanced();
        db.JournalEntries.Add(entry);
        return entry;
    }

    public Task PostFolioChargesAsync(BranchScope scope, IReadOnlyList<FolioCharge> charges, string sourceType, string sourceId, string description, CancellationToken ct)
    {
        if (charges.Count == 0) return Task.CompletedTask;
        var lines = new List<LedgerLine>();
        foreach (var c in charges)
        {
            lines.Add(LedgerLine.Dr(AccountKeys.GuestReceivable, c.Amount));
            lines.Add(LedgerLine.Cr(RevenueKey(c.Type), c.Amount));
        }
        return PostAsync(scope, sourceType, sourceId, description, lines, ct);
    }

    public Task PostPaymentAsync(BranchScope scope, Folio folio, Payment payment, CancellationToken ct) =>
        PostAsync(scope, "Payment", payment.Id.ToString(), $"Payment {payment.Tender} on folio {folio.FolioNumber}",
        [
            LedgerLine.Dr(TenderKey(payment.Tender), payment.Amount, payment.ExternalReference),
            LedgerLine.Cr(AccountKeys.GuestReceivable, payment.Amount),
        ], ct);

    public Task PostRefundAsync(BranchScope scope, Folio folio, Refund refund, CancellationToken ct) =>
        PostAsync(scope, "Refund", refund.Id.ToString(), $"Refund {refund.RefundNumber} on folio {folio.FolioNumber}",
        [
            LedgerLine.Dr(AccountKeys.GuestReceivable, refund.Amount),
            LedgerLine.Cr(TenderKey(refund.Tender), refund.Amount, refund.ExternalReference),
        ], ct);

    public static string TenderKey(TenderType tender) => tender switch
    {
        TenderType.Cash => AccountKeys.Cash,
        TenderType.PosCard => AccountKeys.PosSettlement,
        _ => AccountKeys.Bank,
    };

    public static string RevenueKey(ChargeType type) => type switch
    {
        ChargeType.Room => AccountKeys.RoomRevenue,
        ChargeType.Tax => AccountKeys.TaxPayable,
        ChargeType.Restaurant => AccountKeys.RestaurantRevenue,
        ChargeType.Bar => AccountKeys.BarRevenue,
        _ => AccountKeys.OtherRevenue,
    };

    public async Task<Account> ResolveAsync(Guid companyId, string key, CancellationToken ct)
    {
        if (_accounts.TryGetValue((companyId, key), out var cached)) return cached;
        var mapping = await db.AccountMappings.Include(m => m.Account).SingleOrDefaultAsync(m => m.CompanyId == companyId && m.Key == key, ct);
        if (mapping is null)
        {
            await EnsureChartAsync(companyId, ct);
            mapping = db.AccountMappings.Local.FirstOrDefault(m => m.CompanyId == companyId && m.Key == key)
                ?? throw new ConflictException("ACCOUNT_MAPPING_MISSING", $"No ledger account is mapped for '{key}'. Set it in accounting settings.");
            mapping.Account ??= db.Accounts.Local.First(a => a.Id == mapping.AccountId);
        }
        if (!mapping.Account!.IsActive)
            throw new ConflictException("ACCOUNT_INACTIVE", $"The account mapped for '{key}' ({mapping.Account.Code}) is inactive.");
        return _accounts[(companyId, key)] = mapping.Account;
    }

    public async Task<AccountingPeriod> OpenPeriodForAsync(Guid companyId, DateOnly date, CancellationToken ct)
    {
        var period = db.AccountingPeriods.Local.FirstOrDefault(p => p.CompanyId == companyId && p.Year == date.Year && p.Month == date.Month)
            ?? await db.AccountingPeriods.SingleOrDefaultAsync(p => p.CompanyId == companyId && p.Year == date.Year && p.Month == date.Month, ct);
        if (period is null)
        {
            // Periods open on first use; closing one is an explicit accounting action.
            var start = new DateOnly(date.Year, date.Month, 1);
            period = new AccountingPeriod { CompanyId = companyId, Year = date.Year, Month = date.Month, StartDate = start, EndDate = start.AddMonths(1).AddDays(-1) };
            db.AccountingPeriods.Add(period);
        }
        if (period.Status != PeriodStatus.Open)
            throw new ConflictException("PERIOD_CLOSED", $"Accounting period {date:yyyy-MM} is closed. Postings must go into an open period.");
        return period;
    }

    public async Task EnsureChartAsync(Guid companyId, CancellationToken ct)
    {
        var accounts = await db.Accounts.Where(a => a.CompanyId == companyId).ToListAsync(ct);
        accounts.AddRange(db.Accounts.Local.Where(a => a.CompanyId == companyId && !accounts.Contains(a)));
        var mapped = (await db.AccountMappings.Where(m => m.CompanyId == companyId).Select(m => m.Key).ToListAsync(ct))
            .Concat(db.AccountMappings.Local.Where(m => m.CompanyId == companyId).Select(m => m.Key)).ToHashSet();

        foreach (var d in DefaultChart.Accounts)
        {
            var account = accounts.FirstOrDefault(a => a.Code == d.Code);
            if (account is null)
            {
                account = new Account { CompanyId = companyId, Code = d.Code, Name = d.Name, Type = d.Type, IsControl = d.IsControl };
                db.Accounts.Add(account);
                accounts.Add(account);
            }
            if (d.Key is not null && !mapped.Contains(d.Key))
            {
                db.AccountMappings.Add(new AccountMapping { CompanyId = companyId, Key = d.Key, AccountId = account.Id, Account = account });
                mapped.Add(d.Key);
            }
        }
    }

    private static string Trim(string text) => text.Length > 300 ? text[..300] : text;
}
