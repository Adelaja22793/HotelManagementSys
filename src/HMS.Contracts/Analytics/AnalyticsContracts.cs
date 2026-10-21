namespace HMS.Contracts.Analytics;

/// <summary>
/// Occupancy = rooms sold / rooms available. ADR = room revenue / rooms sold. RevPAR = room revenue / rooms available.
/// Room revenue is the nightly room charge after discounts and before tax, net of reversals.
/// </summary>
public sealed record DailyMetricDto(DateOnly Date, int RoomsAvailable, int RoomsSold, decimal Occupancy, decimal RoomRevenue, decimal Adr,
    decimal RevPar, decimal OtherRevenue, decimal TaxCharged, decimal Collections, decimal Refunds, int Arrivals, int Departures, int Cancellations, int NoShows,
    int ComplaintsOpened, int ComplaintsResolved, bool IsFinal, DateTime ComputedAtUtc);

public sealed record DepartmentRevenueDto(string Department, decimal Revenue, decimal CostOfSales);

/// <summary>
/// Totals are weighted (sums first, then ratios), never averages of daily percentages. MissingDates lists days with no
/// snapshot; they are excluded rather than counted as zero.
/// </summary>
public sealed record MetricTotalsDto(int RoomsAvailable, int RoomsSold, decimal Occupancy, decimal RoomRevenue, decimal Adr, decimal RevPar,
    decimal OtherRevenue, decimal Collections, decimal Refunds, decimal CollectionRate, decimal CancellationRate, decimal NoShowRate,
    decimal ComplaintResolutionRate, decimal? AverageLengthOfStay);

public sealed record DashboardDto(Guid BranchId, DateOnly From, DateOnly To, MetricTotalsDto Totals, IReadOnlyList<DailyMetricDto> Days,
    IReadOnlyList<DepartmentRevenueDto> Departments, IReadOnlyList<DateOnly> MissingDates);

public sealed record BranchComparisonRowDto(Guid BranchId, string BranchCode, string BranchName, MetricTotalsDto Totals, int MissingDays);

public sealed record BranchComparisonDto(DateOnly From, DateOnly To, IReadOnlyList<BranchComparisonRowDto> Branches, MetricTotalsDto Company);

public sealed record RefreshMetricsRequest(DateOnly From, DateOnly To);

public sealed record RefreshResultDto(Guid RunId, int DaysComputed, int DaysSkippedFinal);

/// <summary>
/// Model "on-the-books-v1": OnTheBooks is confirmed and in-house room nights already sold for the date. Baseline is the
/// average actual occupancy of the same weekday over the previous four weeks of final snapshots (null when none exist).
/// </summary>
public sealed record ForecastDayDto(DateOnly Date, int RoomsAvailable, int OnTheBooks, decimal OnTheBooksOccupancy, decimal OnTheBooksRevenue,
    decimal? BaselineOccupancy, int BaselineSampleDays);

public sealed record ForecastDto(string Model, DateOnly From, DateOnly To, IReadOnlyList<ForecastDayDto> Days);
