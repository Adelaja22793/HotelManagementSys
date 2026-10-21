using System.Globalization;
using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Application.Security;
using HMS.Contracts.Staff;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Staff;

public interface IStaffService
{
    Task<IReadOnlyList<DepartmentDto>> ListDepartmentsAsync(Guid branchId, CancellationToken ct = default);
    Task<DepartmentDto> SaveDepartmentAsync(Guid branchId, Guid? departmentId, SaveDepartmentRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeDto>> ListEmployeesAsync(Guid branchId, Guid? departmentId, bool activeOnly, CancellationToken ct = default);
    Task<EmployeeDto> SaveEmployeeAsync(Guid branchId, Guid? employeeId, SaveEmployeeRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<WorkShiftDto>> ListShiftsAsync(Guid branchId, CancellationToken ct = default);
    Task<WorkShiftDto> SaveShiftAsync(Guid branchId, Guid? shiftId, SaveWorkShiftRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<RosterEntryDto>> ListRosterAsync(Guid branchId, DateOnly from, DateOnly to, Guid? employeeId, CancellationToken ct = default);
    Task<RosterEntryDto> AddRosterEntryAsync(Guid branchId, CreateRosterEntryRequest request, CancellationToken ct = default);
    Task RemoveRosterEntryAsync(Guid branchId, Guid entryId, CancellationToken ct = default);
    Task<AttendanceDto> ClockInAsync(Guid branchId, ClockRequest request, CancellationToken ct = default);
    Task<AttendanceDto> ClockOutAsync(Guid branchId, ClockRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceDto>> ListAttendanceAsync(Guid branchId, DateOnly from, DateOnly to, Guid? employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceSummaryDto>> AttendanceSummaryAsync(Guid branchId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveDto>> ListLeaveAsync(Guid branchId, string? status, CancellationToken ct = default);
    Task<LeaveDto> RequestLeaveAsync(Guid branchId, CreateLeaveRequest request, CancellationToken ct = default);
    Task<LeaveDto> DecideLeaveAsync(Guid branchId, Guid leaveId, StaffDecisionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<OvertimeDto>> ListOvertimeAsync(Guid branchId, string? status, CancellationToken ct = default);
    Task<OvertimeDto> RequestOvertimeAsync(Guid branchId, CreateOvertimeRequest request, CancellationToken ct = default);
    Task<OvertimeDto> DecideOvertimeAsync(Guid branchId, Guid overtimeId, StaffDecisionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<HandoverDto>> ListHandoversAsync(Guid branchId, Guid? departmentId, bool unacknowledgedOnly, CancellationToken ct = default);
    Task<HandoverDto> CreateHandoverAsync(Guid branchId, CreateHandoverRequest request, CancellationToken ct = default);
    Task<HandoverDto> AcknowledgeHandoverAsync(Guid branchId, Guid handoverId, CancellationToken ct = default);
}

internal sealed class StaffService(IApplicationDbContext db, IBranchAccessService access, ICurrentUser user, IClock clock) : IStaffService
{
    /// <summary>Minimum hours between the end of one rostered shift and the start of the next.</summary>
    public const int MinimumRestHours = 8;

    public async Task<IReadOnlyList<DepartmentDto>> ListDepartmentsAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return await db.Departments.AsNoTracking().Where(d => d.BranchId == branchId).OrderBy(d => d.Name)
            .Select(d => new DepartmentDto(d.Id, d.Code, d.Name, d.IsActive)).ToListAsync(ct);
    }

    public async Task<DepartmentDto> SaveDepartmentAsync(Guid branchId, Guid? departmentId, SaveDepartmentRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Code), request.Code, 20).Require(nameof(request.Name), request.Name, 100).ThrowIfInvalid();
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Departments.AnyAsync(d => d.BranchId == branchId && d.Code == code && d.Id != (departmentId ?? Guid.Empty), ct))
            throw new ConflictException("DEPARTMENT_CODE_EXISTS", $"Department code {code} is already used.");
        var dept = departmentId is { } id ? await db.Departments.GetOwnedAsync(branchId, id, "Department", ct) : null;
        if (dept is null) db.Departments.Add(dept = new Department { CompanyId = scope.CompanyId, BranchId = branchId });
        dept.Code = code;
        dept.Name = request.Name.Trim();
        dept.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return new DepartmentDto(dept.Id, dept.Code, dept.Name, dept.IsActive);
    }

    public async Task<IReadOnlyList<EmployeeDto>> ListEmployeesAsync(Guid branchId, Guid? departmentId, bool activeOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.Employees.AsNoTracking().Include(e => e.Department).Where(e => e.BranchId == branchId);
        if (departmentId is { } d) q = q.Where(e => e.DepartmentId == d);
        if (activeOnly) q = q.Where(e => e.IsActive);
        return (await q.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).Take(1000).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<EmployeeDto> SaveEmployeeAsync(Guid branchId, Guid? employeeId, SaveEmployeeRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.EmployeeNumber), request.EmployeeNumber, 30).Require(nameof(request.FirstName), request.FirstName, 100)
            .Require(nameof(request.LastName), request.LastName, 100).ThrowIfInvalid();
        await db.Departments.GetOwnedAsync(branchId, request.DepartmentId, "Department", ct);
        var number = request.EmployeeNumber.Trim().ToUpperInvariant();
        if (await db.Employees.AnyAsync(e => e.BranchId == branchId && e.EmployeeNumber == number && e.Id != (employeeId ?? Guid.Empty), ct))
            throw new ConflictException("EMPLOYEE_NUMBER_EXISTS", $"Employee number {number} is already used.");
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId.Trim();
        if (userId is not null && await db.Employees.AnyAsync(e => e.UserId == userId && e.Id != (employeeId ?? Guid.Empty), ct))
            throw new ConflictException("USER_LINKED", "That sign-in account is already linked to another employee.");

        var employee = employeeId is { } id ? await db.Employees.GetOwnedAsync(branchId, id, "Employee", ct) : null;
        if (employee is not null && request.ConcurrencyStamp is { } stamp) BranchOperations.EnsureStamp(employee, stamp, "employee");
        if (employee is null) db.Employees.Add(employee = new Employee { CompanyId = scope.CompanyId, BranchId = branchId });
        employee.EmployeeNumber = number;
        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.DepartmentId = request.DepartmentId;
        employee.JobTitle = Clean(request.JobTitle);
        employee.UserId = userId;
        employee.Phone = Clean(request.Phone);
        employee.Email = Clean(request.Email);
        employee.HireDate = request.HireDate;
        employee.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(await db.Employees.AsNoTracking().Include(e => e.Department).SingleAsync(e => e.Id == employee.Id, ct));
    }

    public async Task<IReadOnlyList<WorkShiftDto>> ListShiftsAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return (await db.WorkShifts.AsNoTracking().Where(s => s.BranchId == branchId).OrderBy(s => s.StartLocal).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<WorkShiftDto> SaveShiftAsync(Guid branchId, Guid? shiftId, SaveWorkShiftRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var v = new Validation().Require(nameof(request.Code), request.Code, 20).Require(nameof(request.Name), request.Name, 100)
            .When(request.BreakMinutes is < 0 or > 240, nameof(request.BreakMinutes), "Break must be 0–240 minutes.");
        var okStart = TimeOnly.TryParseExact(request.StartLocal ?? string.Empty, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start);
        var okEnd = TimeOnly.TryParseExact(request.EndLocal ?? string.Empty, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end);
        v.When(!okStart, nameof(request.StartLocal), "Use HH:mm.").When(!okEnd, nameof(request.EndLocal), "Use HH:mm.").ThrowIfInvalid();
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.WorkShifts.AnyAsync(s => s.BranchId == branchId && s.Code == code && s.Id != (shiftId ?? Guid.Empty), ct))
            throw new ConflictException("SHIFT_CODE_EXISTS", $"Shift code {code} is already used.");
        var shift = shiftId is { } id ? await db.WorkShifts.GetOwnedAsync(branchId, id, "Shift", ct) : null;
        if (shift is null) db.WorkShifts.Add(shift = new WorkShift { CompanyId = scope.CompanyId, BranchId = branchId });
        shift.Code = code;
        shift.Name = request.Name.Trim();
        shift.StartLocal = start;
        shift.EndLocal = end;
        shift.BreakMinutes = request.BreakMinutes;
        shift.IsActive = request.IsActive;
        if (shift.PaidHours <= 0) throw new ValidationAppException(nameof(request.BreakMinutes), "The break is longer than the shift.");
        await db.SaveChangesAsync(ct);
        return ToDto(shift);
    }

    public async Task<IReadOnlyList<RosterEntryDto>> ListRosterAsync(Guid branchId, DateOnly from, DateOnly to, Guid? employeeId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        ValidateRange(from, to, 62);
        var q = db.RosterEntries.AsNoTracking().Include(r => r.Employee).Include(r => r.Shift)
            .Where(r => r.BranchId == branchId && r.WorkDate >= from && r.WorkDate <= to);
        if (employeeId is { } e) q = q.Where(r => r.EmployeeId == e);
        return (await q.OrderBy(r => r.WorkDate).ThenBy(r => r.Shift!.StartLocal).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<RosterEntryDto> AddRosterEntryAsync(Guid branchId, CreateRosterEntryRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var employee = await db.Employees.GetOwnedAsync(branchId, request.EmployeeId, "Employee", ct);
        if (!employee.IsActive) throw new ConflictException("EMPLOYEE_INACTIVE", $"{employee.FullName} is not active.");
        var shift = await db.WorkShifts.GetOwnedAsync(branchId, request.ShiftId, "Shift", ct);
        if (!shift.IsActive) throw new ConflictException("SHIFT_INACTIVE", $"Shift {shift.Code} is inactive.");

        if (await db.LeaveRecords.AnyAsync(l => l.EmployeeId == employee.Id && l.Status == ApprovalStatus.Approved
                && l.StartDate <= request.WorkDate && l.EndDate >= request.WorkDate, ct))
            throw new ConflictException("EMPLOYEE_ON_LEAVE", $"{employee.FullName} is on approved leave on {request.WorkDate:yyyy-MM-dd}.");

        // Compare with shifts on neighbouring days too: overnight shifts cross midnight.
        var (start, end) = shift.LocalWindow(request.WorkDate);
        var nearby = await db.RosterEntries.AsNoTracking().Include(r => r.Shift)
            .Where(r => r.EmployeeId == employee.Id && r.WorkDate >= request.WorkDate.AddDays(-1) && r.WorkDate <= request.WorkDate.AddDays(1))
            .ToListAsync(ct);
        foreach (var other in nearby)
        {
            var (oStart, oEnd) = other.Shift!.LocalWindow(other.WorkDate);
            if (start < oEnd && oStart < end)
                throw new ConflictException("ROSTER_OVERLAP", $"{employee.FullName} is already rostered on {other.Shift.Code} ({other.WorkDate:yyyy-MM-dd}) at that time.");
            var gap = start >= oEnd ? start - oEnd : oStart - end;
            if (gap < TimeSpan.FromHours(MinimumRestHours))
                throw new ConflictException("REST_PERIOD", $"{employee.FullName} needs at least {MinimumRestHours} hours' rest between shifts.");
        }

        var entry = new RosterEntry
        {
            CompanyId = scope.CompanyId, BranchId = branchId, EmployeeId = employee.Id, ShiftId = shift.Id, WorkDate = request.WorkDate, Notes = Clean(request.Notes),
        };
        db.RosterEntries.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflictException("ROSTER_OVERLAP", $"{employee.FullName} is already rostered on that shift.");
        }
        return ToDto(await db.RosterEntries.AsNoTracking().Include(r => r.Employee).Include(r => r.Shift).SingleAsync(r => r.Id == entry.Id, ct));
    }

    public async Task RemoveRosterEntryAsync(Guid branchId, Guid entryId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var entry = await db.RosterEntries.GetOwnedAsync(branchId, entryId, "Roster entry", ct);
        db.RosterEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AttendanceDto> ClockInAsync(Guid branchId, ClockRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var (employee, source) = await ResolveEmployeeAsync(scope, request.EmployeeId, ct);
        if (!employee.IsActive) throw new ConflictException("EMPLOYEE_INACTIVE", $"{employee.FullName} is not active.");
        if (await db.AttendanceRecords.AnyAsync(a => a.EmployeeId == employee.Id && a.ClockOutUtc == null, ct))
            throw new ConflictException("ALREADY_CLOCKED_IN", $"{employee.FullName} is already clocked in.");
        var record = new AttendanceRecord
        {
            CompanyId = scope.CompanyId, BranchId = branchId, EmployeeId = employee.Id, WorkDate = DateOnly.FromDateTime(scope.Branch.ToLocal(scope.UtcNow)),
            ClockInUtc = scope.UtcNow, Source = source, Notes = Clean(request.Notes),
        };
        db.AttendanceRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflictException("ALREADY_CLOCKED_IN", $"{employee.FullName} is already clocked in.");
        }
        return ToDto(record, employee);
    }

    public async Task<AttendanceDto> ClockOutAsync(Guid branchId, ClockRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var (employee, _) = await ResolveEmployeeAsync(scope, request.EmployeeId, ct);
        var record = await db.AttendanceRecords.SingleOrDefaultAsync(a => a.EmployeeId == employee.Id && a.ClockOutUtc == null, ct)
            ?? throw new ConflictException("NOT_CLOCKED_IN", $"{employee.FullName} is not clocked in.");
        record.ClockOutUtc = scope.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.Notes)) record.Notes = request.Notes.Trim();
        await db.SaveChangesAsync(ct);
        return ToDto(record, employee);
    }

    public async Task<IReadOnlyList<AttendanceDto>> ListAttendanceAsync(Guid branchId, DateOnly from, DateOnly to, Guid? employeeId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        ValidateRange(from, to, 62);
        var q = db.AttendanceRecords.AsNoTracking().Include(a => a.Employee).Where(a => a.BranchId == branchId && a.WorkDate >= from && a.WorkDate <= to);
        if (employeeId is { } e) q = q.Where(a => a.EmployeeId == e);
        return (await q.OrderBy(a => a.ClockInUtc).Take(2000).ToListAsync(ct)).Select(a => ToDto(a, a.Employee!)).ToList();
    }

    public async Task<IReadOnlyList<AttendanceSummaryDto>> AttendanceSummaryAsync(Guid branchId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        ValidateRange(from, to, 62);
        var roster = await db.RosterEntries.AsNoTracking().Include(r => r.Shift).Include(r => r.Employee)
            .Where(r => r.BranchId == branchId && r.WorkDate >= from && r.WorkDate <= to).ToListAsync(ct);
        var attendance = await db.AttendanceRecords.AsNoTracking().Include(a => a.Employee)
            .Where(a => a.BranchId == branchId && a.WorkDate >= from && a.WorkDate <= to).ToListAsync(ct);
        var overtime = await db.OvertimeRequests.AsNoTracking()
            .Where(o => o.BranchId == branchId && o.Status == ApprovalStatus.Approved && o.WorkDate >= from && o.WorkDate <= to).ToListAsync(ct);

        var keys = roster.Select(r => (r.EmployeeId, r.WorkDate, Name: r.Employee!.FullName))
            .Concat(attendance.Select(a => (a.EmployeeId, a.WorkDate, Name: a.Employee!.FullName)))
            .DistinctBy(k => (k.EmployeeId, k.WorkDate));
        return keys.Select(k => new AttendanceSummaryDto(k.EmployeeId, k.Name, k.WorkDate,
                roster.Where(r => r.EmployeeId == k.EmployeeId && r.WorkDate == k.WorkDate).Sum(r => r.Shift!.PaidHours),
                attendance.Where(a => a.EmployeeId == k.EmployeeId && a.WorkDate == k.WorkDate).Sum(a => a.Hours),
                overtime.Where(o => o.EmployeeId == k.EmployeeId && o.WorkDate == k.WorkDate).Sum(o => o.Hours)))
            .OrderBy(s => s.WorkDate).ThenBy(s => s.EmployeeName).ToList();
    }

    public async Task<IReadOnlyList<LeaveDto>> ListLeaveAsync(Guid branchId, string? status, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.LeaveRecords.AsNoTracking().Where(l => l.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = EnumParser.Parse<ApprovalStatus>(status, nameof(status));
            q = q.Where(l => l.Status == parsed);
        }
        return (await q.OrderByDescending(l => l.StartDate).Take(500).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<LeaveDto> RequestLeaveAsync(Guid branchId, CreateLeaveRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var type = EnumParser.Parse<LeaveType>(request.Type, nameof(request.Type));
        if (request.EndDate < request.StartDate) throw new ValidationAppException(nameof(request.EndDate), "Leave must end on or after it starts.");
        if (request.EndDate.DayNumber - request.StartDate.DayNumber > 365) throw new ValidationAppException(nameof(request.EndDate), "Leave can be at most a year.");
        var employee = await db.Employees.GetOwnedAsync(branchId, request.EmployeeId, "Employee", ct);
        if (await db.LeaveRecords.AnyAsync(l => l.EmployeeId == employee.Id && l.Status != ApprovalStatus.Rejected
                && l.StartDate <= request.EndDate && l.EndDate >= request.StartDate, ct))
            throw new ConflictException("LEAVE_OVERLAP", "The employee already has leave in that period.");
        var leave = new LeaveRecord
        {
            CompanyId = scope.CompanyId, BranchId = branchId, EmployeeId = employee.Id, Type = type, StartDate = request.StartDate, EndDate = request.EndDate,
            Reason = Clean(request.Reason), RequestedByUserId = scope.UserId,
        };
        db.LeaveRecords.Add(leave);
        await db.SaveChangesAsync(ct);
        return ToDto(leave);
    }

    public async Task<LeaveDto> DecideLeaveAsync(Guid branchId, Guid leaveId, StaffDecisionRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var leave = await db.LeaveRecords.GetOwnedAsync(branchId, leaveId, "Leave", ct);
        BranchOperations.EnsureStamp(leave, request.ConcurrencyStamp, "leave");
        if (leave.Status != ApprovalStatus.Pending) throw new ConflictException("ALREADY_DECIDED", "The leave has already been decided.");
        if (leave.RequestedByUserId == scope.UserId) throw new ConflictException("SELF_APPROVAL", "Someone else must approve this leave.");
        if (request.Approved && await db.RosterEntries.AnyAsync(r => r.EmployeeId == leave.EmployeeId && r.WorkDate >= leave.StartDate && r.WorkDate <= leave.EndDate, ct))
            throw new ConflictException("ROSTERED_DURING_LEAVE", "Remove the employee's rostered shifts in that period before approving the leave.");
        leave.Status = request.Approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        leave.DecidedByUserId = scope.UserId;
        await db.SaveChangesAsync(ct);
        return ToDto(leave);
    }

    public async Task<IReadOnlyList<OvertimeDto>> ListOvertimeAsync(Guid branchId, string? status, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.OvertimeRequests.AsNoTracking().Where(o => o.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = EnumParser.Parse<ApprovalStatus>(status, nameof(status));
            q = q.Where(o => o.Status == parsed);
        }
        return (await q.OrderByDescending(o => o.WorkDate).Take(500).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<OvertimeDto> RequestOvertimeAsync(Guid branchId, CreateOvertimeRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Reason), request.Reason, 300)
            .When(request.Hours is <= 0 or > 16, nameof(request.Hours), "Overtime must be between 0 and 16 hours.").ThrowIfInvalid();
        var employee = await db.Employees.GetOwnedAsync(branchId, request.EmployeeId, "Employee", ct);
        var overtime = new OvertimeRequest
        {
            CompanyId = scope.CompanyId, BranchId = branchId, EmployeeId = employee.Id, WorkDate = request.WorkDate, Hours = request.Hours,
            Reason = request.Reason.Trim(), RequestedByUserId = scope.UserId,
        };
        db.OvertimeRequests.Add(overtime);
        await db.SaveChangesAsync(ct);
        return ToDto(overtime);
    }

    public async Task<OvertimeDto> DecideOvertimeAsync(Guid branchId, Guid overtimeId, StaffDecisionRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var overtime = await db.OvertimeRequests.GetOwnedAsync(branchId, overtimeId, "Overtime", ct);
        BranchOperations.EnsureStamp(overtime, request.ConcurrencyStamp, "overtime");
        if (overtime.Status != ApprovalStatus.Pending) throw new ConflictException("ALREADY_DECIDED", "The overtime has already been decided.");
        if (overtime.RequestedByUserId == scope.UserId) throw new ConflictException("SELF_APPROVAL", "Someone else must approve this overtime.");
        overtime.Status = request.Approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        overtime.DecidedByUserId = scope.UserId;
        await db.SaveChangesAsync(ct);
        return ToDto(overtime);
    }

    public async Task<IReadOnlyList<HandoverDto>> ListHandoversAsync(Guid branchId, Guid? departmentId, bool unacknowledgedOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.ShiftHandovers.AsNoTracking().Where(h => h.BranchId == branchId);
        if (departmentId is { } d) q = q.Where(h => h.DepartmentId == d);
        if (unacknowledgedOnly) q = q.Where(h => h.AcknowledgedAtUtc == null);
        return (await q.OrderByDescending(h => h.CreatedAtUtc).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<HandoverDto> CreateHandoverAsync(Guid branchId, CreateHandoverRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Notes), request.Notes, 4000).ThrowIfInvalid();
        await db.Departments.GetOwnedAsync(branchId, request.DepartmentId, "Department", ct);
        var handover = new ShiftHandover
        {
            CompanyId = scope.CompanyId, BranchId = branchId, DepartmentId = request.DepartmentId, BusinessDate = scope.BusinessDate,
            FromUserId = scope.UserId, Notes = request.Notes.Trim(), OpenIssues = Clean(request.OpenIssues),
        };
        db.ShiftHandovers.Add(handover);
        await db.SaveChangesAsync(ct);
        return ToDto(handover);
    }

    public async Task<HandoverDto> AcknowledgeHandoverAsync(Guid branchId, Guid handoverId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var handover = await db.ShiftHandovers.GetOwnedAsync(branchId, handoverId, "Handover", ct);
        if (handover.AcknowledgedAtUtc is not null) throw new ConflictException("ALREADY_ACKNOWLEDGED", "The handover was already acknowledged.");
        if (handover.FromUserId == scope.UserId) throw new ConflictException("SELF_ACKNOWLEDGE", "The incoming person must acknowledge the handover.");
        handover.AcknowledgedByUserId = scope.UserId;
        handover.AcknowledgedAtUtc = scope.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(handover);
    }

    private async Task<(Employee Employee, AttendanceSource Source)> ResolveEmployeeAsync(BranchScope scope, Guid? employeeId, CancellationToken ct)
    {
        var self = await db.Employees.SingleOrDefaultAsync(e => e.BranchId == scope.BranchId && e.UserId == scope.UserId, ct);
        if (employeeId is null || employeeId == self?.Id)
            return (self ?? throw new ConflictException("NO_EMPLOYEE_RECORD", "Your account is not linked to an employee in this branch."), AttendanceSource.Desktop);
        if (!RolePermissions.HasPermission(user.Roles, Permissions.StaffManage))
            throw new ForbiddenException(message: "Only staff managers can clock other employees in or out.");
        return (await db.Employees.GetOwnedAsync(scope.BranchId, employeeId.Value, "Employee", ct), AttendanceSource.Manager);
    }

    private static void ValidateRange(DateOnly from, DateOnly to, int maxDays)
    {
        if (to < from) throw new ValidationAppException("to", "The end date must be on or after the start date.");
        if (to.DayNumber - from.DayNumber > maxDays) throw new ValidationAppException("to", $"Choose at most {maxDays} days.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static EmployeeDto ToDto(Employee e) => new(e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DepartmentId, e.Department?.Name ?? string.Empty,
        e.JobTitle, e.UserId, e.Phone, e.Email, e.HireDate, e.IsActive, e.ConcurrencyStamp);

    private static WorkShiftDto ToDto(WorkShift s) => new(s.Id, s.Code, s.Name, s.StartLocal.ToString("HH:mm", CultureInfo.InvariantCulture),
        s.EndLocal.ToString("HH:mm", CultureInfo.InvariantCulture), s.BreakMinutes, s.IsOvernight, Math.Round(s.PaidHours, 2), s.IsActive);

    private static RosterEntryDto ToDto(RosterEntry r) => new(r.Id, r.EmployeeId, r.Employee?.FullName ?? string.Empty, r.ShiftId, r.Shift?.Code ?? string.Empty,
        r.WorkDate, r.Notes);

    private static AttendanceDto ToDto(AttendanceRecord a, Employee e) => new(a.Id, a.EmployeeId, e.FullName, a.WorkDate, a.ClockInUtc, a.ClockOutUtc, a.Hours,
        a.Source.ToString(), a.Notes);

    private static LeaveDto ToDto(LeaveRecord l) => new(l.Id, l.EmployeeId, l.Type.ToString(), l.StartDate, l.EndDate, l.Reason, l.Status.ToString(),
        l.RequestedByUserId, l.DecidedByUserId, l.ConcurrencyStamp);

    private static OvertimeDto ToDto(OvertimeRequest o) => new(o.Id, o.EmployeeId, o.WorkDate, o.Hours, o.Reason, o.Status.ToString(), o.RequestedByUserId,
        o.DecidedByUserId, o.ConcurrencyStamp);

    private static HandoverDto ToDto(ShiftHandover h) => new(h.Id, h.DepartmentId, h.BusinessDate, h.FromUserId, h.Notes, h.OpenIssues, h.AcknowledgedByUserId,
        h.AcknowledgedAtUtc, h.CreatedAtUtc);
}
