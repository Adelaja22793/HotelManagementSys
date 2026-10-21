using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>
/// The night audit for one branch business date. One row per branch and date; running the close again
/// re-checks the same row, so a restart never duplicates work.
/// </summary>
public class BusinessDayClose : BranchEntity, IConcurrencyTracked
{
    private BusinessDayClose() { }

    public BusinessDayClose(Guid companyId, Guid branchId, DateOnly businessDate, string userId, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        BusinessDate = businessDate;
        StartedByUserId = userId;
        StartedAtUtc = utcNow;
    }

    public DateOnly BusinessDate { get; private set; }
    public BusinessDayStatus Status { get; private set; } = BusinessDayStatus.Blocked;
    public string StartedByUserId { get; private set; } = string.Empty;
    public DateTime StartedAtUtc { get; private set; }
    public int RunCount { get; private set; }
    public DateTime? LastRunAtUtc { get; private set; }
    public string? ClosedByUserId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public List<BusinessDayException> Exceptions { get; private set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsClosed => Status == BusinessDayStatus.Closed;

    public void RecordRun(DateTime utcNow)
    {
        EnsureNotClosed();
        RunCount++;
        LastRunAtUtc = utcNow;
    }

    /// <summary>
    /// Merges this run's findings: new problems are added, problems no longer found are resolved,
    /// and accepted exceptions stay accepted while they still apply.
    /// </summary>
    public IReadOnlyList<BusinessDayException> ApplyFindings(IEnumerable<BusinessDayFinding> findings)
    {
        EnsureNotClosed();
        var added = new List<BusinessDayException>();
        var current = findings.GroupBy(f => (f.Code, f.Reference)).Select(g => g.First()).ToList();
        foreach (var finding in current)
        {
            var existing = Exceptions.SingleOrDefault(e => e.Code == finding.Code && e.Reference == finding.Reference);
            if (existing is null)
            {
                var exception = new BusinessDayException(Id, finding);
                Exceptions.Add(exception);
                added.Add(exception);
            }
            else
            {
                existing.Refresh(finding);
            }
        }
        foreach (var gone in Exceptions.Where(e => e.Status == BusinessDayExceptionStatus.Open
                     && !current.Any(f => f.Code == e.Code && f.Reference == e.Reference)))
            gone.Resolve();
        return added;
    }

    public bool HasOpenBlockers => Exceptions.Any(e => e.Severity == ExceptionSeverity.Blocking && e.Status == BusinessDayExceptionStatus.Open);

    public void Close(string userId, DateTime utcNow)
    {
        EnsureNotClosed();
        if (HasOpenBlockers)
            throw new DomainConflictException("BUSINESS_DAY_BLOCKED", "Resolve or accept every blocking exception before closing the day.");
        Status = BusinessDayStatus.Closed;
        ClosedByUserId = userId;
        ClosedAtUtc = utcNow;
    }

    private void EnsureNotClosed()
    {
        if (IsClosed) throw new DomainConflictException("BUSINESS_DAY_CLOSED", $"{BusinessDate:yyyy-MM-dd} is already closed.");
    }
}

/// <summary>A problem found by a close check. Reference identifies the record (e.g. a reservation id) or is empty for totals.</summary>
public sealed record BusinessDayFinding(string Code, ExceptionSeverity Severity, string Message, string Reference = "");

public class BusinessDayException : Entity
{
    private BusinessDayException() { }

    internal BusinessDayException(Guid closeId, BusinessDayFinding finding)
    {
        CloseId = closeId;
        Code = finding.Code;
        Reference = finding.Reference;
        Severity = finding.Severity;
        Message = finding.Message;
    }

    public Guid CloseId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public ExceptionSeverity Severity { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public BusinessDayExceptionStatus Status { get; private set; } = BusinessDayExceptionStatus.Open;
    public string? AcceptedByUserId { get; private set; }
    public string? AcceptanceReason { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }

    internal void Refresh(BusinessDayFinding finding)
    {
        Message = finding.Message;
        Severity = finding.Severity;
        if (Status == BusinessDayExceptionStatus.Resolved) Status = BusinessDayExceptionStatus.Open; // came back
    }

    internal void Resolve() => Status = BusinessDayExceptionStatus.Resolved;

    public void Accept(string userId, string reason, DateTime utcNow)
    {
        if (Status != BusinessDayExceptionStatus.Open)
            throw new DomainConflictException("EXCEPTION_STATE", "Only open exceptions can be accepted.");
        AcceptanceReason = Guard.Required(reason, "REASON_REQUIRED", "Explain why the day can close with this exception", 500);
        AcceptedByUserId = userId;
        AcceptedAtUtc = utcNow;
        Status = BusinessDayExceptionStatus.Accepted;
    }
}

/// <summary>
/// Daily hotel figures for one branch, taken from posted stays, folio charges and the ledger.
/// Snapshots taken by the night audit are final; refreshes only recompute days that are not final.
/// </summary>
public class BranchDailyMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly Date { get; set; }
    public int RoomsAvailable { get; set; }
    public int RoomsSold { get; set; }
    public decimal RoomRevenue { get; set; }
    public decimal OtherRevenue { get; set; }
    public decimal TaxCharged { get; set; }
    public decimal Collections { get; set; }
    public decimal Refunds { get; set; }
    public int Arrivals { get; set; }
    public int Departures { get; set; }
    public int Cancellations { get; set; }
    public int NoShows { get; set; }
    public int ComplaintsOpened { get; set; }
    public int ComplaintsResolved { get; set; }
    public bool IsFinal { get; set; }
    public DateTime ComputedAtUtc { get; set; }
}

/// <summary>Revenue and cost of sales by department for one branch and day.</summary>
public class BranchDailyDepartmentMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly Date { get; set; }
    public string Department { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal CostOfSales { get; set; }
    public DateTime ComputedAtUtc { get; set; }
}

/// <summary>Record of each analytics recalculation, for support and auditing.</summary>
public class AnalyticsRefreshRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public int DaysComputed { get; set; }
    public int DaysSkippedFinal { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}
