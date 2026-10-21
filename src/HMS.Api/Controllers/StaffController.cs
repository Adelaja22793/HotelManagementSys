using HMS.Api.Authorization;
using HMS.Application.Security;
using HMS.Application.Staff;
using HMS.Contracts.Staff;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/staff")]
public sealed class StaffController(IStaffService staff) : ApiControllerBase
{
    [HttpGet("departments")]
    [HasPermission(Permissions.BranchesView)]
    public Task<IReadOnlyList<DepartmentDto>> Departments(Guid branchId, CancellationToken ct) => staff.ListDepartmentsAsync(branchId, ct);

    [HttpPost("departments")]
    [HasPermission(Permissions.StaffManage)]
    public async Task<ActionResult<DepartmentDto>> CreateDepartment(Guid branchId, SaveDepartmentRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await staff.SaveDepartmentAsync(branchId, null, request, ct));

    [HttpPut("departments/{departmentId:guid}")]
    [HasPermission(Permissions.StaffManage)]
    public Task<DepartmentDto> UpdateDepartment(Guid branchId, Guid departmentId, SaveDepartmentRequest request, CancellationToken ct) =>
        staff.SaveDepartmentAsync(branchId, departmentId, request, ct);

    [HttpGet("employees")]
    [HasPermission(Permissions.StaffView)]
    public Task<IReadOnlyList<EmployeeDto>> Employees(Guid branchId, [FromQuery] Guid? departmentId, [FromQuery] bool activeOnly = true, CancellationToken ct = default) =>
        staff.ListEmployeesAsync(branchId, departmentId, activeOnly, ct);

    [HttpPost("employees")]
    [HasPermission(Permissions.StaffManage)]
    public async Task<ActionResult<EmployeeDto>> CreateEmployee(Guid branchId, SaveEmployeeRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await staff.SaveEmployeeAsync(branchId, null, request, ct));

    [HttpPut("employees/{employeeId:guid}")]
    [HasPermission(Permissions.StaffManage)]
    public Task<EmployeeDto> UpdateEmployee(Guid branchId, Guid employeeId, SaveEmployeeRequest request, CancellationToken ct) =>
        staff.SaveEmployeeAsync(branchId, employeeId, request, ct);

    [HttpGet("shifts")]
    [HasPermission(Permissions.StaffView)]
    public Task<IReadOnlyList<WorkShiftDto>> Shifts(Guid branchId, CancellationToken ct) => staff.ListShiftsAsync(branchId, ct);

    [HttpPost("shifts")]
    [HasPermission(Permissions.StaffManage)]
    public async Task<ActionResult<WorkShiftDto>> CreateShift(Guid branchId, SaveWorkShiftRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await staff.SaveShiftAsync(branchId, null, request, ct));

    [HttpPut("shifts/{shiftId:guid}")]
    [HasPermission(Permissions.StaffManage)]
    public Task<WorkShiftDto> UpdateShift(Guid branchId, Guid shiftId, SaveWorkShiftRequest request, CancellationToken ct) =>
        staff.SaveShiftAsync(branchId, shiftId, request, ct);

    [HttpGet("roster")]
    [HasPermission(Permissions.StaffView)]
    public Task<IReadOnlyList<RosterEntryDto>> Roster(Guid branchId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? employeeId, CancellationToken ct) =>
        staff.ListRosterAsync(branchId, from, to, employeeId, ct);

    [HttpPost("roster")]
    [HasPermission(Permissions.StaffManage)]
    public async Task<ActionResult<RosterEntryDto>> AddRoster(Guid branchId, CreateRosterEntryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await staff.AddRosterEntryAsync(branchId, request, ct));

    [HttpDelete("roster/{entryId:guid}")]
    [HasPermission(Permissions.StaffManage)]
    public async Task<IActionResult> RemoveRoster(Guid branchId, Guid entryId, CancellationToken ct)
    {
        await staff.RemoveRosterEntryAsync(branchId, entryId, ct);
        return NoContent();
    }

    /// <summary>Clock yourself in (any signed-in staff linked to an employee) or, with staff.manage, someone else.</summary>
    [HttpPost("attendance/clock-in")]
    [HasPermission(Permissions.BranchesView)]
    public Task<AttendanceDto> ClockIn(Guid branchId, ClockRequest request, CancellationToken ct) => staff.ClockInAsync(branchId, request, ct);

    [HttpPost("attendance/clock-out")]
    [HasPermission(Permissions.BranchesView)]
    public Task<AttendanceDto> ClockOut(Guid branchId, ClockRequest request, CancellationToken ct) => staff.ClockOutAsync(branchId, request, ct);

    [HttpGet("attendance")]
    [HasPermission(Permissions.StaffView)]
    public Task<IReadOnlyList<AttendanceDto>> Attendance(Guid branchId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? employeeId, CancellationToken ct) =>
        staff.ListAttendanceAsync(branchId, from, to, employeeId, ct);

    [HttpGet("attendance/summary")]
    [HasPermission(Permissions.StaffView)]
    public Task<IReadOnlyList<AttendanceSummaryDto>> Summary(Guid branchId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        staff.AttendanceSummaryAsync(branchId, from, to, ct);

    [HttpGet("leave")]
    [HasPermission(Permissions.StaffView)]
    public Task<IReadOnlyList<LeaveDto>> Leave(Guid branchId, [FromQuery] string? status, CancellationToken ct) => staff.ListLeaveAsync(branchId, status, ct);

    [HttpPost("leave")]
    [HasPermission(Permissions.StaffManage)]
    public async Task<ActionResult<LeaveDto>> RequestLeave(Guid branchId, CreateLeaveRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await staff.RequestLeaveAsync(branchId, request, ct));

    [HttpPost("leave/{leaveId:guid}/decision")]
    [HasPermission(Permissions.StaffManage)]
    public Task<LeaveDto> DecideLeave(Guid branchId, Guid leaveId, StaffDecisionRequest request, CancellationToken ct) =>
        staff.DecideLeaveAsync(branchId, leaveId, request, ct);

    [HttpGet("overtime")]
    [HasPermission(Permissions.StaffView)]
    public Task<IReadOnlyList<OvertimeDto>> Overtime(Guid branchId, [FromQuery] string? status, CancellationToken ct) => staff.ListOvertimeAsync(branchId, status, ct);

    [HttpPost("overtime")]
    [HasPermission(Permissions.StaffManage)]
    public async Task<ActionResult<OvertimeDto>> RequestOvertime(Guid branchId, CreateOvertimeRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await staff.RequestOvertimeAsync(branchId, request, ct));

    [HttpPost("overtime/{overtimeId:guid}/decision")]
    [HasPermission(Permissions.StaffManage)]
    public Task<OvertimeDto> DecideOvertime(Guid branchId, Guid overtimeId, StaffDecisionRequest request, CancellationToken ct) =>
        staff.DecideOvertimeAsync(branchId, overtimeId, request, ct);

    [HttpGet("handovers")]
    [HasPermission(Permissions.BranchesView)]
    public Task<IReadOnlyList<HandoverDto>> Handovers(Guid branchId, [FromQuery] Guid? departmentId, [FromQuery] bool unacknowledgedOnly = false, CancellationToken ct = default) =>
        staff.ListHandoversAsync(branchId, departmentId, unacknowledgedOnly, ct);

    [HttpPost("handovers")]
    [HasPermission(Permissions.BranchesView)]
    public async Task<ActionResult<HandoverDto>> CreateHandover(Guid branchId, CreateHandoverRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await staff.CreateHandoverAsync(branchId, request, ct));

    [HttpPost("handovers/{handoverId:guid}/acknowledge")]
    [HasPermission(Permissions.BranchesView)]
    public Task<HandoverDto> Acknowledge(Guid branchId, Guid handoverId, CancellationToken ct) => staff.AcknowledgeHandoverAsync(branchId, handoverId, ct);
}
