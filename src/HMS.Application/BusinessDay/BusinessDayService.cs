using System.Globalization;
using HMS.Application.Abstractions;
using HMS.Application.Accounting;
using HMS.Application.Analytics;
using HMS.Application.Common;
using HMS.Application.Notifications;
using HMS.Application.Security;
using HMS.Contracts.BusinessDay;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.BusinessDay;

public interface IBusinessDayService
{
    Task<BusinessDayStatusDto> StatusAsync(Guid branchId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessDayCloseDto>> ListAsync(Guid branchId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    /// <summary>
    /// Runs the night audit for the current business date. With open blocking exceptions the day stays open and the
    /// result lists them; otherwise the day closes, the daily snapshot is taken and the branch moves to the next date.
    /// Re-running is safe: checks re-evaluate the same record and a closed day is returned unchanged.
    /// </summary>
    Task<BusinessDayCloseDto> CloseAsync(Guid branchId, CloseBusinessDayRequest request, CancellationToken ct = default);

    Task<BusinessDayCloseDto> AcceptExceptionAsync(Guid branchId, Guid exceptionId, AcceptExceptionRequest request, CancellationToken ct = default);
}

internal sealed class BusinessDayService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    ILedgerService ledger,
    IAnalyticsService analytics,
    INotificationQueue notifications) : IBusinessDayService
{
    public async Task<BusinessDayStatusDto> StatusAsync(Guid branchId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var next = scope.Branch.GetNextDateToClose(scope.UtcNow);
        var clockDate = scope.Branch.GetClockDate(scope.UtcNow);
        var close = await db.BusinessDayCloses.AsNoTracking().Include(c => c.Exceptions)
            .SingleOrDefaultAsync(c => c.BranchId == branchId && c.BusinessDate == next, ct);
        return new BusinessDayStatusDto(scope.BusinessDate, clockDate, scope.Branch.LastClosedBusinessDate, next, next <= clockDate,
            close is null ? null : ToDto(close));
    }

    public async Task<IReadOnlyList<BusinessDayCloseDto>> ListAsync(Guid branchId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.BusinessDayCloses.AsNoTracking().Include(c => c.Exceptions).Where(c => c.BranchId == branchId);
        if (from is { } f) q = q.Where(c => c.BusinessDate >= f);
        if (to is { } t) q = q.Where(c => c.BusinessDate <= t);
        return (await q.OrderByDescending(c => c.BusinessDate).Take(100).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<BusinessDayCloseDto> CloseAsync(Guid branchId, CloseBusinessDayRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var date = request.BusinessDate;
        // A retry after a successful close finds the day already closed: return the original result.
        var done = await db.BusinessDayCloses.AsNoTracking().Include(c => c.Exceptions)
            .SingleOrDefaultAsync(c => c.BranchId == branchId && c.BusinessDate == date && c.Status == BusinessDayStatus.Closed, ct);
        if (done is not null) return ToDto(done);
        var next = scope.Branch.GetNextDateToClose(scope.UtcNow);
        if (date != next)
            throw new ConflictException("BUSINESS_DATE_MISMATCH", $"The next day to close is {next:yyyy-MM-dd}. Days close in order.");
        if (date > scope.Branch.GetClockDate(scope.UtcNow))
            throw new ConflictException("BUSINESS_DAY_NOT_STARTED", $"{date:yyyy-MM-dd} has not started yet.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var close = await db.BusinessDayCloses.Include(c => c.Exceptions).SingleOrDefaultAsync(c => c.BranchId == branchId && c.BusinessDate == date, ct);
        if (close is null)
            db.BusinessDayCloses.Add(close = new BusinessDayClose(scope.CompanyId, branchId, date, scope.UserId, scope.UtcNow));
        else
            db.Touch(close); // two managers pressing "close" at once: the second gets a clean 409

        close.RecordRun(scope.UtcNow);
        close.ApplyFindings(await CheckAsync(scope, date, ct));

        if (!close.HasOpenBlockers)
        {
            var metric = await analytics.ComputeAsync(scope, date, final: true, ct);
            scope.Branch.CloseBusinessDay(date, scope.UtcNow);
            db.Touch(scope.Branch);
            close.Close(scope.UserId, scope.UtcNow);
            await notifications.EnqueueInAppAsync(scope, NotificationEvents.DailyReportReady, "BusinessDayClose", close.Id.ToString(),
                "perm:" + Permissions.ReportsView, new Dictionary<string, string>
                {
                    ["BusinessDate"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["Occupancy"] = (AnalyticsService.Ratio(metric.RoomsSold, metric.RoomsAvailable) * 100).ToString("0.#", CultureInfo.InvariantCulture),
                    ["Adr"] = NotificationQueue.Money(metric.RoomsSold == 0 ? 0 : Math.Round(metric.RoomRevenue / metric.RoomsSold, 2)),
                    ["RevPar"] = NotificationQueue.Money(metric.RoomsAvailable == 0 ? 0 : Math.Round(metric.RoomRevenue / metric.RoomsAvailable, 2)),
                    ["RoomRevenue"] = NotificationQueue.Money(metric.RoomRevenue),
                    ["Collections"] = NotificationQueue.Money(metric.Collections - metric.Refunds),
                    ["Currency"] = scope.Branch.Currency,
                }, ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict,
                ex is DbUpdateConcurrencyException ? "The day was being closed by someone else. Reload and try again." : "The night audit is already running. Try again.");
        }
        await tx.CommitAsync(ct);
        return ToDto(await db.BusinessDayCloses.AsNoTracking().Include(c => c.Exceptions).SingleAsync(c => c.Id == close.Id, ct));
    }

    public async Task<BusinessDayCloseDto> AcceptExceptionAsync(Guid branchId, Guid exceptionId, AcceptExceptionRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var close = await db.BusinessDayCloses.Include(c => c.Exceptions)
            .SingleOrDefaultAsync(c => c.BranchId == branchId && c.Exceptions.Any(e => e.Id == exceptionId), ct)
            ?? throw new NotFoundException("Business day exception", exceptionId);
        if (close.IsClosed) throw new ConflictException("BUSINESS_DAY_CLOSED", $"{close.BusinessDate:yyyy-MM-dd} is already closed.");
        close.Exceptions.Single(e => e.Id == exceptionId).Accept(scope.UserId, request.Reason, scope.UtcNow);
        db.Touch(close);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "The night audit changed while you were working. Reload and try again.");
        }
        return ToDto(close);
    }

    /// <summary>The night audit checks. Blocking findings stop the close until fixed or accepted; warnings are recorded only.</summary>
    private async Task<List<BusinessDayFinding>> CheckAsync(BranchScope scope, DateOnly date, CancellationToken ct)
    {
        var branchId = scope.BranchId;
        var findings = new List<BusinessDayFinding>();

        var arrivals = await db.Reservations.AsNoTracking()
            .Where(r => r.BranchId == branchId && r.Status == ReservationStatus.Confirmed && r.ArrivalDate <= date)
            .Select(r => new { r.Id, r.ReservationNumber, r.ArrivalDate }).Take(200).ToListAsync(ct);
        findings.AddRange(arrivals.Select(r => new BusinessDayFinding("ARRIVAL_PENDING", ExceptionSeverity.Blocking,
            $"{r.ReservationNumber} was due on {r.ArrivalDate:yyyy-MM-dd}. Check the guest in, mark a no-show, cancel or move the booking.", r.Id.ToString())));

        var departures = await db.Stays.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.Status == StayStatus.Active && s.Reservation!.DepartureDate <= date)
            .Select(s => new { s.Id, s.Reservation!.ReservationNumber, s.Room!.RoomNumber }).Take(200).ToListAsync(ct);
        findings.AddRange(departures.Select(s => new BusinessDayFinding("DEPARTURE_PENDING", ExceptionSeverity.Blocking,
            $"Room {s.RoomNumber} ({s.ReservationNumber}) was due to leave. Check the guest out or extend the stay.", s.Id.ToString())));

        var orders = await db.RestaurantOrders.AsNoTracking()
            .Where(o => o.BranchId == branchId && o.Status == RestaurantOrderStatus.Open && o.BusinessDate <= date)
            .Select(o => new { o.Id, o.OrderNumber }).Take(200).ToListAsync(ct);
        findings.AddRange(orders.Select(o => new BusinessDayFinding("OPEN_RESTAURANT_ORDER", ExceptionSeverity.Blocking,
            $"Order {o.OrderNumber} is still open. Settle, charge to a room or void it.", o.Id.ToString())));

        var shifts = await db.CashierShifts.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.Status != CashierShiftStatus.Closed && s.BusinessDate <= date)
            .Select(s => new { s.Id, s.Status }).ToListAsync(ct);
        findings.AddRange(shifts.Select(s => new BusinessDayFinding("CASHIER_SHIFT_OPEN", ExceptionSeverity.Warning,
            s.Status == CashierShiftStatus.Closing ? "A cashier shift is waiting for variance approval." : "A cashier shift is still open.", s.Id.ToString())));

        var pendingPayments = await db.Payments.CountAsync(p => p.BranchId == branchId && p.Status == PaymentStatus.Pending, ct);
        if (pendingPayments > 0)
            findings.Add(new BusinessDayFinding("PAYMENTS_UNVERIFIED", ExceptionSeverity.Warning,
                $"{pendingPayments} bank transfer or online payment(s) are waiting for verification."));

        var approvedRefunds = await db.Refunds.CountAsync(r => r.BranchId == branchId && r.Status == RefundStatus.Approved, ct);
        if (approvedRefunds > 0)
            findings.Add(new BusinessDayFinding("REFUNDS_NOT_PAID", ExceptionSeverity.Warning, $"{approvedRefunds} approved refund(s) have not been paid out."));

        // Reconciliation: the guest ledger must agree with the folios to the kobo.
        var charges = await db.FolioCharges.Where(c => c.BranchId == branchId).Select(c => c.Amount).SumClientAsync(ct);
        var paid = await db.Payments.Where(p => p.BranchId == branchId && p.Status == PaymentStatus.Confirmed).Select(p => p.Amount).SumClientAsync(ct);
        var refunded = await db.Refunds.Where(r => r.BranchId == branchId && r.Status == RefundStatus.Executed).Select(r => r.Amount).SumClientAsync(ct);
        var folioBalance = charges - paid + refunded;
        var arLedger = await LedgerBalanceAsync(scope, AccountKeys.GuestReceivable, ct);
        if (folioBalance != arLedger)
            findings.Add(new BusinessDayFinding("AR_LEDGER_MISMATCH", ExceptionSeverity.Blocking,
                $"Guest folios total {scope.Branch.Currency} {folioBalance:N2} but the guest receivable account shows {arLedger:N2}. Investigate before closing."));

        // Stock is valued at weighted-average cost, so allow for rounding before flagging a difference.
        var stock = await db.StockBalances.AsNoTracking().Where(b => b.BranchId == branchId).Select(b => new { b.Quantity, b.AverageCost }).ToListAsync(ct);
        var stockValue = stock.Sum(b => Math.Round(b.Quantity * b.AverageCost, 2));
        var inventoryLedger = await LedgerBalanceAsync(scope, AccountKeys.Inventory, ct);
        if (Math.Abs(stockValue - inventoryLedger) > Math.Max(1m, stock.Count * 0.05m))
            findings.Add(new BusinessDayFinding("INVENTORY_LEDGER_MISMATCH", ExceptionSeverity.Warning,
                $"Stock on hand is valued at {stockValue:N2} but the inventory account shows {inventoryLedger:N2}."));
        return findings;
    }

    private async Task<decimal> LedgerBalanceAsync(BranchScope scope, string key, CancellationToken ct)
    {
        var account = await ledger.ResolveAsync(scope.CompanyId, key, ct);
        var lines = await db.JournalEntryLines.Where(l => l.AccountId == account.Id && db.JournalEntries.Any(j => j.Id == l.JournalEntryId && j.BranchId == scope.BranchId))
            .Select(l => new { l.Debit, l.Credit }).ToListAsync(ct);
        return lines.Sum(l => l.Debit - l.Credit);
    }

    private static BusinessDayCloseDto ToDto(BusinessDayClose c) => new(c.Id, c.BusinessDate, c.Status.ToString(), c.RunCount, c.StartedByUserId,
        c.StartedAtUtc, c.LastRunAtUtc, c.ClosedByUserId, c.ClosedAtUtc,
        c.Exceptions.OrderByDescending(e => e.Severity).ThenBy(e => e.Code).Select(e => new BusinessDayExceptionDto(e.Id, e.Code, e.Reference,
            e.Severity.ToString(), e.Message, e.Status.ToString(), e.AcceptedByUserId, e.AcceptanceReason, e.AcceptedAtUtc)).ToList(),
        c.ConcurrencyStamp);
}
