namespace HMS.Contracts.Staff;

public sealed record DepartmentDto(Guid Id, string Code, string Name, bool IsActive);

public sealed record SaveDepartmentRequest(string Code, string Name, bool IsActive = true);

public sealed record EmployeeDto(Guid Id, string EmployeeNumber, string FirstName, string LastName, Guid DepartmentId, string DepartmentName,
    string? JobTitle, string? UserId, string? Phone, string? Email, DateOnly HireDate, bool IsActive, Guid ConcurrencyStamp);

public sealed record SaveEmployeeRequest(string EmployeeNumber, string FirstName, string LastName, Guid DepartmentId, string? JobTitle,
    string? UserId, string? Phone, string? Email, DateOnly HireDate, bool IsActive = true, Guid? ConcurrencyStamp = null);

/// <summary>Times are branch-local "HH:mm". An end at or before the start means the shift ends the next day.</summary>
public sealed record WorkShiftDto(Guid Id, string Code, string Name, string StartLocal, string EndLocal, int BreakMinutes, bool IsOvernight,
    decimal PaidHours, bool IsActive);

public sealed record SaveWorkShiftRequest(string Code, string Name, string StartLocal, string EndLocal, int BreakMinutes, bool IsActive = true);

public sealed record RosterEntryDto(Guid Id, Guid EmployeeId, string EmployeeName, Guid ShiftId, string ShiftCode, DateOnly WorkDate,
    string? Notes);

public sealed record CreateRosterEntryRequest(Guid EmployeeId, Guid ShiftId, DateOnly WorkDate, string? Notes);

/// <summary>Leave EmployeeId empty to clock yourself in or out.</summary>
public sealed record ClockRequest(Guid? EmployeeId, string? Notes);

public sealed record AttendanceDto(Guid Id, Guid EmployeeId, string EmployeeName, DateOnly WorkDate, DateTime ClockInUtc, DateTime? ClockOutUtc,
    decimal Hours, string Source, string? Notes);

public sealed record AttendanceSummaryDto(Guid EmployeeId, string EmployeeName, DateOnly WorkDate, decimal RosteredHours, decimal WorkedHours,
    decimal ApprovedOvertimeHours);

/// <summary>Type: Annual, Sick, Unpaid, Other. EndDate is the last day of leave.</summary>
public sealed record CreateLeaveRequest(Guid EmployeeId, string Type, DateOnly StartDate, DateOnly EndDate, string? Reason);

public sealed record LeaveDto(Guid Id, Guid EmployeeId, string Type, DateOnly StartDate, DateOnly EndDate, string? Reason, string Status,
    string RequestedByUserId, string? DecidedByUserId, Guid ConcurrencyStamp);

public sealed record CreateOvertimeRequest(Guid EmployeeId, DateOnly WorkDate, decimal Hours, string Reason);

public sealed record OvertimeDto(Guid Id, Guid EmployeeId, DateOnly WorkDate, decimal Hours, string Reason, string Status,
    string RequestedByUserId, string? DecidedByUserId, Guid ConcurrencyStamp);

public sealed record StaffDecisionRequest(bool Approved, Guid ConcurrencyStamp);

public sealed record CreateHandoverRequest(Guid DepartmentId, string Notes, string? OpenIssues);

public sealed record HandoverDto(Guid Id, Guid DepartmentId, DateOnly BusinessDate, string FromUserId, string Notes, string? OpenIssues,
    string? AcknowledgedByUserId, DateTime? AcknowledgedAtUtc, DateTime CreatedAtUtc);
