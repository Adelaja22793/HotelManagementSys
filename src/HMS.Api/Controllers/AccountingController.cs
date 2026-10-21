using HMS.Api.Authorization;
using HMS.Application.Accounting;
using HMS.Application.Security;
using HMS.Contracts.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/accounting")]
public sealed class AccountingController(IAccountingService accounting) : ApiControllerBase
{
    [HttpGet("accounts")]
    [HasPermission(Permissions.AccountingView)]
    public Task<IReadOnlyList<AccountDto>> Accounts(Guid branchId, CancellationToken ct) => accounting.ListAccountsAsync(branchId, ct);

    [HttpPost("accounts")]
    [HasPermission(Permissions.AccountingManage)]
    public async Task<ActionResult<AccountDto>> CreateAccount(Guid branchId, SaveAccountRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await accounting.CreateAccountAsync(branchId, request, ct));

    [HttpPut("accounts/{accountId:guid}")]
    [HasPermission(Permissions.AccountingManage)]
    public Task<AccountDto> UpdateAccount(Guid branchId, Guid accountId, SaveAccountRequest request, CancellationToken ct) =>
        accounting.UpdateAccountAsync(branchId, accountId, request, ct);

    [HttpGet("accounts/{accountId:guid}/ledger")]
    [HasPermission(Permissions.AccountingView)]
    public Task<IReadOnlyList<LedgerLineDto>> Ledger(Guid branchId, Guid accountId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        accounting.AccountLedgerAsync(branchId, accountId, from, to, ct);

    [HttpGet("mappings")]
    [HasPermission(Permissions.AccountingView)]
    public Task<IReadOnlyList<AccountMappingDto>> Mappings(Guid branchId, CancellationToken ct) => accounting.ListMappingsAsync(branchId, ct);

    [HttpPut("mappings")]
    [HasPermission(Permissions.AccountingManage)]
    public Task<AccountMappingDto> SetMapping(Guid branchId, SetAccountMappingRequest request, CancellationToken ct) => accounting.SetMappingAsync(branchId, request, ct);

    [HttpGet("periods")]
    [HasPermission(Permissions.AccountingView)]
    public Task<IReadOnlyList<AccountingPeriodDto>> Periods(Guid branchId, CancellationToken ct) => accounting.ListPeriodsAsync(branchId, ct);

    [HttpPost("periods/{periodId:guid}/close")]
    [HasPermission(Permissions.AccountingManage)]
    public Task<AccountingPeriodDto> ClosePeriod(Guid branchId, Guid periodId, PeriodActionRequest request, CancellationToken ct) =>
        accounting.ClosePeriodAsync(branchId, periodId, request, ct);

    [HttpPost("periods/{periodId:guid}/reopen")]
    [HasPermission(Permissions.AccountingManage)]
    public Task<AccountingPeriodDto> ReopenPeriod(Guid branchId, Guid periodId, PeriodActionRequest request, CancellationToken ct) =>
        accounting.ReopenPeriodAsync(branchId, periodId, request, ct);

    [HttpGet("journals")]
    [HasPermission(Permissions.AccountingView)]
    public Task<PagedResultLite<JournalEntryDto>> Journals(Guid branchId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? sourceType,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        accounting.ListJournalsAsync(branchId, from, to, sourceType, page, pageSize, ct);

    [HttpGet("journals/{journalId:guid}")]
    [HasPermission(Permissions.AccountingView)]
    public Task<JournalEntryDto> Journal(Guid branchId, Guid journalId, CancellationToken ct) => accounting.GetJournalAsync(branchId, journalId, ct);

    [HttpPost("journals")]
    [HasPermission(Permissions.AccountingPost)]
    public async Task<ActionResult<JournalEntryDto>> CreateJournal(Guid branchId, CreateJournalRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await accounting.CreateManualJournalAsync(branchId, request, ct));

    [HttpPost("journals/{journalId:guid}/reverse")]
    [HasPermission(Permissions.AccountingPost)]
    public Task<JournalEntryDto> Reverse(Guid branchId, Guid journalId, ReverseJournalRequest request, CancellationToken ct) =>
        accounting.ReverseJournalAsync(branchId, journalId, request, ct);

    [HttpGet("trial-balance")]
    [HasPermission(Permissions.AccountingView)]
    public Task<TrialBalanceDto> TrialBalance(Guid branchId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] bool consolidated = false, CancellationToken ct = default) =>
        accounting.TrialBalanceAsync(branchId, from, to, consolidated, ct);
}
