using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

public class Department : BranchEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>An employee of the branch. <see cref="UserId"/> links to a sign-in account when they use the system.</summary>
public class Employee : BranchEntity, IConcurrencyTracked
{
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? UserId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateOnly HireDate { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>
/// A shift pattern in branch-local time. If <see cref="EndLocal"/> is not after <see cref="StartLocal"/>
/// the shift runs overnight into the next day.
/// </summary>
public class WorkShift : BranchEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartLocal { get; set; }
    public TimeOnly EndLocal { get; set; }
    public int BreakMinutes { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsOvernight => EndLocal <= StartLocal;

    public (DateTime Start, DateTime End) LocalWindow(DateOnly workDate)
    {
        var start = workDate.ToDateTime(StartLocal);
        var end = (IsOvernight ? workDate.AddDays(1) : workDate).ToDateTime(EndLocal);
        return (start, end);
    }

    public decimal PaidHours => (decimal)((IsOvernight ? TimeSpan.FromDays(1) : TimeSpan.Zero) + (EndLocal - StartLocal)).TotalMinutes / 60m - BreakMinutes / 60m;
}

/// <summary>Planned shift for one employee on one work date (the date the shift starts).</summary>
public class RosterEntry : BranchEntity
{
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public Guid ShiftId { get; set; }
    public WorkShift? Shift { get; set; }
    public DateOnly WorkDate { get; set; }
    public string? Notes { get; set; }
}

/// <summary>One clock-in/clock-out session. Several per day are allowed; only one may be open per employee.</summary>
public class AttendanceRecord : BranchEntity, IConcurrencyTracked
{
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    /// <summary>Branch-local business date of the clock-in.</summary>
    public DateOnly WorkDate { get; set; }
    public DateTime ClockInUtc { get; set; }
    public DateTime? ClockOutUtc { get; set; }
    public AttendanceSource Source { get; set; }
    public string? Notes { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public bool IsOpen => ClockOutUtc is null;
    public decimal Hours => ClockOutUtc is { } end ? Math.Round((decimal)(end - ClockInUtc).TotalHours, 2) : 0;
}

public class LeaveRecord : BranchEntity, IConcurrencyTracked
{
    public Guid EmployeeId { get; set; }
    public LeaveType Type { get; set; }
    public DateOnly StartDate { get; set; }
    /// <summary>Inclusive last day of leave.</summary>
    public DateOnly EndDate { get; set; }
    public string? Reason { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string RequestedByUserId { get; set; } = string.Empty;
    public string? DecidedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Overtime claim. Approval is recorded here only; payroll export is a separate, explicit step.</summary>
public class OvertimeRequest : BranchEntity, IConcurrencyTracked
{
    public Guid EmployeeId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string RequestedByUserId { get; set; } = string.Empty;
    public string? DecidedByUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>Notes passed from one shift to the next, acknowledged by the incoming person.</summary>
public class ShiftHandover : BranchEntity
{
    public Guid DepartmentId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string FromUserId { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? OpenIssues { get; set; }
    public string? AcknowledgedByUserId { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
}
