namespace HMS.Contracts.BusinessDay;

/// <summary>Severity: Warning, Blocking. Status: Open, Resolved, Accepted.</summary>
public sealed record BusinessDayExceptionDto(Guid Id, string Code, string Reference, string Severity, string Message, string Status,
    string? AcceptedByUserId, string? AcceptanceReason, DateTime? AcceptedAtUtc);

/// <summary>Status: Blocked (open blocking exceptions remain) or Closed.</summary>
public sealed record BusinessDayCloseDto(Guid Id, DateOnly BusinessDate, string Status, int RunCount, string StartedByUserId,
    DateTime StartedAtUtc, DateTime? LastRunAtUtc, string? ClosedByUserId, DateTime? ClosedAtUtc,
    IReadOnlyList<BusinessDayExceptionDto> Exceptions, Guid ConcurrencyStamp);

/// <summary>NextDateToClose is the day the night audit must close next (days close in order). PendingClose is its latest run, if any.</summary>
public sealed record BusinessDayStatusDto(DateOnly CurrentBusinessDate, DateOnly ClockBusinessDate, DateOnly? LastClosedBusinessDate,
    DateOnly NextDateToClose, bool CanCloseNow, BusinessDayCloseDto? PendingClose);

/// <summary>BusinessDate must be the next day to close, so a stale screen cannot close the wrong day.</summary>
public sealed record CloseBusinessDayRequest(DateOnly BusinessDate);

public sealed record AcceptExceptionRequest(string Reason);
