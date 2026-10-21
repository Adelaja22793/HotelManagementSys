using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Accounting;
using HMS.Contracts.Common;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>One editable line of a manual journal.</summary>
public sealed partial class JournalLineDraft : ObservableObject
{
    [ObservableProperty] private AccountDto? _account;
    [ObservableProperty] private decimal _debit;
    [ObservableProperty] private decimal _credit;
    [ObservableProperty] private string? _memo;
}

/// <summary>
/// Trial balance, account ledgers, journals (with manual entries and reversals) and accounting periods.
/// Postings from the hotel's operations arrive automatically; manual journals must balance and avoid control accounts.
/// </summary>
public sealed partial class AccountingPageViewModel(ApiClient api, SessionState session, IDialogService dialogs) : PageViewModel
{
    public override string Title => "Accounting";

    public ObservableCollection<TrialBalanceLineDto> TrialBalance { get; } = [];
    public ObservableCollection<AccountDto> Accounts { get; } = [];
    public ObservableCollection<LedgerLineDto> Ledger { get; } = [];
    public ObservableCollection<JournalEntryDto> Journals { get; } = [];
    public ObservableCollection<AccountingPeriodDto> Periods { get; } = [];
    public ObservableCollection<JournalLineDraft> DraftLines { get; } = [new(), new()];

    [ObservableProperty] private DateTime _from = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _to = DateTime.Today;
    [ObservableProperty] private bool _consolidated;
    [ObservableProperty] private decimal _totalDebit;
    [ObservableProperty] private decimal _totalCredit;

    [ObservableProperty] private AccountDto? _ledgerAccount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReverseCommand))]
    private JournalEntryDto? _selectedJournal;

    [ObservableProperty] private string? _reverseReason;
    [ObservableProperty] private DateTime _entryDate = DateTime.Today;
    [ObservableProperty] private string _entryDescription = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClosePeriodCommand), nameof(ReopenPeriodCommand))]
    private AccountingPeriodDto? _selectedPeriod;

    public bool CanPost => session.HasPermission(PermissionNames.AccountingPost);
    public bool CanManage => session.HasPermission(PermissionNames.AccountingManage);

    private DateOnly FromDate => DateOnly.FromDateTime(From);
    private DateOnly ToDate => DateOnly.FromDateTime(To);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        if (To < From) { ErrorMessage = "The end date is before the start date."; return; }
        var tb = await api.GetAsync<TrialBalanceDto>(Query.Build(api.BranchPath("accounting/trial-balance"),
            ("from", FromDate), ("to", ToDate), ("consolidated", Consolidated)));
        FrontDeskPageViewModel.Replace(TrialBalance, tb.Lines);
        TotalDebit = tb.TotalDebit;
        TotalCredit = tb.TotalCredit;
        FrontDeskPageViewModel.Replace(Accounts, (await api.GetAsync<List<AccountDto>>(api.BranchPath("accounting/accounts"))).Where(a => a.IsActive).OrderBy(a => a.Code, StringComparer.Ordinal));
        var journals = await api.GetAsync<PagedResult<JournalEntryDto>>(Query.Build(api.BranchPath("accounting/journals"),
            ("from", FromDate), ("to", ToDate), ("pageSize", 200)));
        FrontDeskPageViewModel.Replace(Journals, journals.Items);
        FrontDeskPageViewModel.Replace(Periods, (await api.GetAsync<List<AccountingPeriodDto>>(api.BranchPath("accounting/periods"))).OrderByDescending(p => p.StartDate));
        if (LedgerAccount is not null) await LoadLedgerAsync();
    });

    [RelayCommand]
    private Task ShowAsync() => LoadAsync();

    partial void OnLedgerAccountChanged(AccountDto? value)
    {
        if (value is not null) _ = RunAsync(LoadLedgerAsync);
    }

    private async Task LoadLedgerAsync()
    {
        if (LedgerAccount is null) return;
        FrontDeskPageViewModel.Replace(Ledger, await api.GetAsync<List<LedgerLineDto>>(Query.Build(
            api.BranchPath($"accounting/accounts/{LedgerAccount.Id}/ledger"), ("from", FromDate), ("to", ToDate))));
    }

    [RelayCommand]
    private void AddLine() => DraftLines.Add(new JournalLineDraft());

    [RelayCommand]
    private void RemoveLine(JournalLineDraft? line)
    {
        if (line is not null && DraftLines.Count > 2) DraftLines.Remove(line);
    }

    [RelayCommand]
    private async Task PostJournalAsync()
    {
        var lines = DraftLines.Where(l => l.Account is not null && (l.Debit != 0 || l.Credit != 0)).ToList();
        if (string.IsNullOrWhiteSpace(EntryDescription)) { ErrorMessage = "Describe the journal."; return; }
        if (lines.Count < 2) { ErrorMessage = "A journal needs at least two lines with an account and an amount."; return; }
        var debit = lines.Sum(l => l.Debit);
        var credit = lines.Sum(l => l.Credit);
        if (debit != credit) { ErrorMessage = $"Debits ({debit:N2}) and credits ({credit:N2}) must be equal."; return; }
        if (!dialogs.Confirm($"Post a journal of {debit:N2} dated {EntryDate:dd MMM yyyy}?")) return;
        JournalEntryDto? posted = null;
        if (await RunAsync(async () => posted = await api.PostAsync<JournalEntryDto>(api.BranchPath("accounting/journals"),
                new CreateJournalRequest(DateOnly.FromDateTime(EntryDate), EntryDescription.Trim(),
                    lines.Select(l => new JournalLineRequest(l.Account!.Id, l.Debit, l.Credit, string.IsNullOrWhiteSpace(l.Memo) ? null : l.Memo.Trim())).ToList()))))
        {
            EntryDescription = "";
            DraftLines.Clear();
            DraftLines.Add(new JournalLineDraft());
            DraftLines.Add(new JournalLineDraft());
            await LoadAsync();
            StatusMessage = $"Journal {posted!.EntryNumber} posted.";
        }
    }

    private bool CanReverse() => CanPost && SelectedJournal is { ReversedByEntryId: null, ReversesEntryId: null };

    [RelayCommand(CanExecute = nameof(CanReverse))]
    private async Task ReverseAsync()
    {
        if (string.IsNullOrWhiteSpace(ReverseReason)) { ErrorMessage = "Give a reason for the reversal."; return; }
        var journal = SelectedJournal!;
        if (!dialogs.Confirm($"Reverse journal {journal.EntryNumber}?")) return;
        JournalEntryDto? reversal = null;
        if (await RunAsync(async () => reversal = await api.PostAsync<JournalEntryDto>(api.BranchPath($"accounting/journals/{journal.Id}/reverse"),
                new ReverseJournalRequest(ReverseReason.Trim()))))
        {
            ReverseReason = null;
            await LoadAsync();
            StatusMessage = $"{journal.EntryNumber} reversed by {reversal!.EntryNumber}.";
        }
    }

    private bool PeriodOpen() => CanManage && SelectedPeriod is { Status: "Open" };
    private bool PeriodClosed() => CanManage && SelectedPeriod is { Status: "Closed" };

    [RelayCommand(CanExecute = nameof(PeriodOpen))]
    private Task ClosePeriodAsync() => PeriodActionAsync("close", "No more postings can be dated in {0}. Close it?", "{0} closed.");

    [RelayCommand(CanExecute = nameof(PeriodClosed))]
    private Task ReopenPeriodAsync() => PeriodActionAsync("reopen", "Reopen {0} for postings?", "{0} reopened.");

    private async Task PeriodActionAsync(string action, string question, string done)
    {
        var period = SelectedPeriod!;
        var name = new DateTime(period.Year, period.Month, 1).ToString("MMMM yyyy");
        if (!dialogs.Confirm(string.Format(question, name))) return;
        if (await RunAsync(() => api.PostAsync<AccountingPeriodDto>(api.BranchPath($"accounting/periods/{period.Id}/{action}"),
                new PeriodActionRequest(period.ConcurrencyStamp))))
        {
            await LoadAsync();
            StatusMessage = string.Format(done, name);
        }
    }
}
