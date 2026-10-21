using HMS.Application.Abstractions;
using HMS.Application.Accounting;
using HMS.Application.Common;
using HMS.Contracts.Analytics;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Analytics;

public interface IAnalyticsService
{
    /// <summary>Computes (or recomputes) one day's snapshot inside the caller's unit of work. A final snapshot is never overwritten.</summary>
    Task<BranchDailyMetric> ComputeAsync(BranchScope scope, DateOnly date, bool final, CancellationToken ct);

    Task<RefreshResultDto> RefreshAsync(Guid branchId, RefreshMetricsRequest request, CancellationToken ct = default);
    Task<DashboardDto> DashboardAsync(Guid branchId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<BranchComparisonDto> CompareBranchesAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<ForecastDto> ForecastAsync(Guid branchId, int days, CancellationToken ct = default);
}

internal sealed class AnalyticsService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    ILedgerService ledger) : IAnalyticsService
{
    public const int MaxRangeDays = 366;

    public async Task<BranchDailyMetric> ComputeAsync(BranchScope scope, DateOnly date, bool final, CancellationToken ct)
    {
        var branchId = scope.BranchId;
        var metric = await db.BranchDailyMetrics.SingleOrDefaultAsync(m => m.BranchId == branchId && m.Date == date, ct);
        if (metric is { IsFinal: true }) return metric;
        if (metric is null) db.BranchDailyMetrics.Add(metric = new BranchDailyMetric { CompanyId = scope.CompanyId, BranchId = branchId, Date = date });

        var activeRooms = await db.Rooms.CountAsync(r => r.BranchId == branchId && r.IsActive, ct);
        var blockedRooms = await db.RoomBlocks.Where(b => b.BranchId == branchId && b.IsActive && b.StartDate <= date && b.EndDate > date)
            .Select(b => b.RoomId).Distinct().CountAsync(ct);

        // Live room-night charges for the night (reversed charges and the reversal lines themselves are excluded).
        var roomCharges = await db.FolioCharges.AsNoTracking()
            .Where(c => c.BranchId == branchId && c.Type == ChargeType.Room && c.BusinessDate == date && c.ReversesChargeId == null && c.ReversedByChargeId == null)
            .Select(c => c.Amount).ToListAsync(ct);
        var otherCharges = await db.FolioCharges.AsNoTracking()
            .Where(c => c.BranchId == branchId && c.Type != ChargeType.Room && c.BusinessDate == date)
            .Select(c => new { c.Type, c.Amount }).ToListAsync(ct);

        var payments = await JournalTotalAsync(branchId, "Payment", date, ct);
        var refunds = await JournalTotalAsync(branchId, "Refund", date, ct);

        var arrivalsByStatus = await db.Reservations.AsNoTracking().Where(r => r.BranchId == branchId && r.ArrivalDate == date)
            .GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(params ReservationStatus[] statuses) => arrivalsByStatus.Where(a => statuses.Contains(a.Status)).Sum(a => a.Count);

        metric.RoomsAvailable = Math.Max(0, activeRooms - blockedRooms);
        metric.RoomsSold = roomCharges.Count;
        metric.RoomRevenue = roomCharges.Sum();
        metric.OtherRevenue = otherCharges.Where(c => c.Type != ChargeType.Tax).Sum(c => c.Amount);
        metric.TaxCharged = otherCharges.Where(c => c.Type == ChargeType.Tax).Sum(c => c.Amount);
        metric.Collections = payments;
        metric.Refunds = refunds;
        metric.Arrivals = Count(ReservationStatus.CheckedIn, ReservationStatus.CheckedOut);
        metric.Cancellations = Count(ReservationStatus.Cancelled);
        metric.NoShows = Count(ReservationStatus.NoShow);
        metric.Departures = await db.Reservations.CountAsync(r => r.BranchId == branchId && r.DepartureDate == date && r.Status == ReservationStatus.CheckedOut, ct);
        metric.ComplaintsOpened = await db.GuestComplaints.CountAsync(c => c.BranchId == branchId && c.BusinessDate == date, ct);
        metric.ComplaintsResolved = await db.GuestComplaints.CountAsync(c => c.BranchId == branchId && c.ResolvedBusinessDate == date, ct);
        metric.IsFinal = final;
        metric.ComputedAtUtc = scope.UtcNow;

        await ComputeDepartmentsAsync(scope, date, roomCharges.Sum(), otherCharges.Select(c => (c.Type, c.Amount)).ToList(), ct);
        return metric;
    }

    private async Task ComputeDepartmentsAsync(BranchScope scope, DateOnly date, decimal roomRevenue, List<(ChargeType Type, decimal Amount)> other, CancellationToken ct)
    {
        var foodCost = await AccountNetAsync(scope, AccountKeys.FoodCost, date, ct);
        var roomsCost = await AccountNetAsync(scope, AccountKeys.HousekeepingSupplies, date, ct);
        var rows = new (string Department, decimal Revenue, decimal Cost)[]
        {
            ("Rooms", roomRevenue, roomsCost),
            ("Food and beverage", other.Where(o => o.Type is ChargeType.Restaurant or ChargeType.Bar).Sum(o => o.Amount), foodCost),
            ("Services", other.Where(o => o.Type == ChargeType.Service).Sum(o => o.Amount), 0),
            ("Adjustments", other.Where(o => o.Type is ChargeType.Adjustment or ChargeType.Discount).Sum(o => o.Amount), 0),
        };
        var existing = await db.BranchDailyDepartmentMetrics.Where(m => m.BranchId == scope.BranchId && m.Date == date).ToListAsync(ct);
        foreach (var (department, revenue, cost) in rows)
        {
            var row = existing.SingleOrDefault(e => e.Department == department);
            if (row is null)
                db.BranchDailyDepartmentMetrics.Add(row = new BranchDailyDepartmentMetric { CompanyId = scope.CompanyId, BranchId = scope.BranchId, Date = date, Department = department });
            row.Revenue = revenue;
            row.CostOfSales = cost;
            row.ComputedAtUtc = scope.UtcNow;
        }
    }

    /// <summary>Total of journals of a source type posted on a date (payments and refunds post one balanced journal each).</summary>
    private async Task<decimal> JournalTotalAsync(Guid branchId, string sourceType, DateOnly date, CancellationToken ct) =>
        await db.JournalEntryLines.Where(l => db.JournalEntries.Any(j => j.Id == l.JournalEntryId && j.BranchId == branchId
                && j.SourceType == sourceType && j.EntryDate == date))
            .Select(l => l.Debit).SumClientAsync(ct);

    private async Task<decimal> AccountNetAsync(BranchScope scope, string key, DateOnly date, CancellationToken ct)
    {
        var account = await ledger.ResolveAsync(scope.CompanyId, key, ct);
        var lines = await db.JournalEntryLines.Where(l => l.AccountId == account.Id && db.JournalEntries.Any(j => j.Id == l.JournalEntryId
                && j.BranchId == scope.BranchId && j.EntryDate == date))
            .Select(l => new { l.Debit, l.Credit }).ToListAsync(ct);
        return lines.Sum(l => l.Debit - l.Credit);
    }

    public async Task<RefreshResultDto> RefreshAsync(Guid branchId, RefreshMetricsRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        ValidateRange(request.From, request.To);
        var to = request.To > scope.BusinessDate ? scope.BusinessDate : request.To;
        var run = new AnalyticsRefreshRun
        {
            CompanyId = scope.CompanyId, BranchId = branchId, FromDate = request.From, ToDate = to,
            RequestedByUserId = scope.UserId, StartedAtUtc = scope.UtcNow,
        };
        for (var day = request.From; day <= to; day = day.AddDays(1))
        {
            var metric = await ComputeAsync(scope, day, final: false, ct);
            if (metric.IsFinal) run.DaysSkippedFinal++; else run.DaysComputed++;
        }
        run.CompletedAtUtc = clock.UtcNow;
        db.AnalyticsRefreshRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "Another refresh is running for these dates. Try again.");
        }
        return new RefreshResultDto(run.Id, run.DaysComputed, run.DaysSkippedFinal);
    }

    public async Task<DashboardDto> DashboardAsync(Guid branchId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        ValidateRange(from, to);
        var metrics = await db.BranchDailyMetrics.AsNoTracking().Where(m => m.BranchId == branchId && m.Date >= from && m.Date <= to)
            .OrderBy(m => m.Date).ToListAsync(ct);
        var departments = await db.BranchDailyDepartmentMetrics.AsNoTracking().Where(m => m.BranchId == branchId && m.Date >= from && m.Date <= to)
            .Select(m => new { m.Department, m.Revenue, m.CostOfSales }).ToListAsync(ct);

        var lastDay = to < scope.BusinessDate ? to : scope.BusinessDate.AddDays(-1);
        var missing = Dates(from, lastDay).Where(d => metrics.All(m => m.Date != d)).ToList();
        var totals = await TotalsAsync(branchId, metrics, from, to, ct);
        return new DashboardDto(branchId, from, to, totals, metrics.Select(ToDto).ToList(),
            departments.GroupBy(d => d.Department).Select(g => new DepartmentRevenueDto(g.Key, g.Sum(x => x.Revenue), g.Sum(x => x.CostOfSales))).ToList(),
            missing);
    }

    public async Task<BranchComparisonDto> CompareBranchesAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ValidateRange(from, to);
        var branchIds = await access.GetAccessibleBranchIdsAsync(ct);
        var branches = await db.Branches.AsNoTracking().Where(b => branchIds.Contains(b.Id)).OrderBy(b => b.Code).ToListAsync(ct);
        var metrics = await db.BranchDailyMetrics.AsNoTracking().Where(m => branchIds.Contains(m.BranchId) && m.Date >= from && m.Date <= to).ToListAsync(ct);
        var rows = new List<BranchComparisonRowDto>();
        foreach (var branch in branches)
        {
            var own = metrics.Where(m => m.BranchId == branch.Id).ToList();
            var lastDay = to < branch.GetBusinessDate(clock.UtcNow) ? to : branch.GetBusinessDate(clock.UtcNow).AddDays(-1);
            var missing = Dates(from, lastDay).Count(d => own.All(m => m.Date != d));
            rows.Add(new BranchComparisonRowDto(branch.Id, branch.Code, branch.Name, await TotalsAsync(branch.Id, own, from, to, ct), missing));
        }
        // Company figures are built from summed counts and money, never by averaging branch percentages.
        return new BranchComparisonDto(from, to, rows, Combine(metrics, null));
    }

    public async Task<ForecastDto> ForecastAsync(Guid branchId, int days, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (days is < 1 or > 365) throw new ValidationAppException(nameof(days), "Forecast between 1 and 365 days.");
        var from = scope.BusinessDate;
        var to = from.AddDays(days - 1);
        var activeRooms = await db.Rooms.CountAsync(r => r.BranchId == branchId && r.IsActive, ct);
        var blocks = await db.RoomBlocks.AsNoTracking().Where(b => b.BranchId == branchId && b.IsActive && b.EndDate > from && b.StartDate <= to)
            .Select(b => new { b.RoomId, b.StartDate, b.EndDate }).ToListAsync(ct);
        var nights = await db.ReservationNights.AsNoTracking()
            .Where(n => n.Date >= from && n.Date <= to && db.Reservations.Any(r => r.Id == n.ReservationId && r.BranchId == branchId
                && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.CheckedIn)))
            .Select(n => new { n.Date, n.Rate, n.Discount }).ToListAsync(ct);
        var history = await db.BranchDailyMetrics.AsNoTracking()
            .Where(m => m.BranchId == branchId && m.IsFinal && m.Date >= from.AddDays(-28) && m.Date < from)
            .Select(m => new { m.Date, m.RoomsSold, m.RoomsAvailable }).ToListAsync(ct);

        var result = new List<ForecastDayDto>();
        foreach (var day in Dates(from, to))
        {
            var available = Math.Max(0, activeRooms - blocks.Where(b => b.StartDate <= day && b.EndDate > day).Select(b => b.RoomId).Distinct().Count());
            var booked = nights.Where(n => n.Date == day).ToList();
            var sameWeekday = history.Where(h => h.Date.DayOfWeek == day.DayOfWeek && h.RoomsAvailable > 0).ToList();
            decimal? baseline = sameWeekday.Count == 0 ? null
                : Ratio(sameWeekday.Sum(h => h.RoomsSold), sameWeekday.Sum(h => h.RoomsAvailable));
            result.Add(new ForecastDayDto(day, available, booked.Count, Ratio(booked.Count, available), booked.Sum(n => n.Rate - n.Discount),
                baseline, sameWeekday.Count));
        }
        return new ForecastDto("on-the-books-v1", from, to, result);
    }

    private async Task<MetricTotalsDto> TotalsAsync(Guid branchId, List<BranchDailyMetric> metrics, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var stays = await db.Reservations.AsNoTracking()
            .Where(r => r.BranchId == branchId && r.Status == ReservationStatus.CheckedOut && r.DepartureDate >= from && r.DepartureDate <= to)
            .Select(r => new { r.ArrivalDate, r.DepartureDate }).ToListAsync(ct);
        decimal? alos = stays.Count == 0 ? null
            : Math.Round((decimal)stays.Sum(s => s.DepartureDate.DayNumber - s.ArrivalDate.DayNumber) / stays.Count, 2);
        return Combine(metrics, alos);
    }

    private static MetricTotalsDto Combine(IReadOnlyCollection<BranchDailyMetric> m, decimal? alos)
    {
        var available = m.Sum(x => x.RoomsAvailable);
        var sold = m.Sum(x => x.RoomsSold);
        var roomRevenue = m.Sum(x => x.RoomRevenue);
        var other = m.Sum(x => x.OtherRevenue);
        var billed = roomRevenue + other + m.Sum(x => x.TaxCharged);
        var collections = m.Sum(x => x.Collections);
        var refunds = m.Sum(x => x.Refunds);
        var due = m.Sum(x => x.Arrivals + x.Cancellations + x.NoShows);
        return new MetricTotalsDto(available, sold, Ratio(sold, available), roomRevenue, Money(roomRevenue, sold), Money(roomRevenue, available),
            other, collections, refunds, Ratio(collections - refunds, billed), Ratio(m.Sum(x => x.Cancellations), due),
            Ratio(m.Sum(x => x.NoShows), due), Ratio(m.Sum(x => x.ComplaintsResolved), m.Sum(x => x.ComplaintsOpened)), alos);
    }

    private static DailyMetricDto ToDto(BranchDailyMetric m) => new(m.Date, m.RoomsAvailable, m.RoomsSold, Ratio(m.RoomsSold, m.RoomsAvailable),
        m.RoomRevenue, Money(m.RoomRevenue, m.RoomsSold), Money(m.RoomRevenue, m.RoomsAvailable), m.OtherRevenue, m.TaxCharged, m.Collections, m.Refunds,
        m.Arrivals, m.Departures, m.Cancellations, m.NoShows, m.ComplaintsOpened, m.ComplaintsResolved, m.IsFinal, m.ComputedAtUtc);

    /// <summary>A share between 0 and 1 (4 decimal places); 0 when there is nothing to divide by.</summary>
    internal static decimal Ratio(decimal part, decimal whole) => whole == 0 ? 0 : Math.Round(part / whole, 4);

    private static decimal Money(decimal amount, decimal count) => count == 0 ? 0 : Math.Round(amount / count, 2);

    private static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from) throw new ValidationAppException(nameof(to), "The end date must be on or after the start date.");
        if (to.DayNumber - from.DayNumber >= MaxRangeDays) throw new ValidationAppException(nameof(to), $"Choose at most {MaxRangeDays} days.");
    }

    private static IEnumerable<DateOnly> Dates(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1)) yield return d;
    }
}
