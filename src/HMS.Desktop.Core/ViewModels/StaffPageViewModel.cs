using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Staff;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>Employees, the weekly roster, clocking in and out, leave and overtime approvals, and shift handovers.</summary>
public sealed partial class StaffPageViewModel(ApiClient api, SessionState session, IDialogService dialogs) : PageViewModel
{
    public static readonly string[] LeaveTypes = ["Annual", "Sick", "Unpaid", "Other"];

    public override string Title => "Staff and rosters";

    public ObservableCollection<DepartmentDto> Departments { get; } = [];
    public ObservableCollection<EmployeeDto> Employees { get; } = [];
    public ObservableCollection<WorkShiftDto> Shifts { get; } = [];
    public ObservableCollection<RosterEntryDto> Roster { get; } = [];
    public ObservableCollection<AttendanceSummaryDto> Attendance { get; } = [];
    public ObservableCollection<LeaveDto> Leave { get; } = [];
    public ObservableCollection<OvertimeDto> Overtime { get; } = [];
    public ObservableCollection<HandoverDto> Handovers { get; } = [];

    /// <summary>Monday of the week shown on the roster and attendance tabs.</summary>
    [ObservableProperty] private DateTime _weekStart = StartOfWeek(DateTime.Today);

    [ObservableProperty] private string _newNumber = "";
    [ObservableProperty] private string _newFirstName = "";
    [ObservableProperty] private string _newLastName = "";
    [ObservableProperty] private DepartmentDto? _newDepartment;
    [ObservableProperty] private string? _newJobTitle;
    [ObservableProperty] private DateTime _newHireDate = DateTime.Today;

    [ObservableProperty] private EmployeeDto? _employee;
    [ObservableProperty] private WorkShiftDto? _shift;
    [ObservableProperty] private DateTime _workDate = DateTime.Today;
    [ObservableProperty] private string? _rosterNotes;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveRosterCommand))]
    private RosterEntryDto? _selectedRoster;

    [ObservableProperty] private string _leaveType = "Annual";
    [ObservableProperty] private DateTime _leaveStart = DateTime.Today.AddDays(7);
    [ObservableProperty] private DateTime _leaveEnd = DateTime.Today.AddDays(8);
    [ObservableProperty] private string? _leaveReason;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveLeaveCommand), nameof(RejectLeaveCommand))]
    private LeaveDto? _selectedLeave;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveOvertimeCommand), nameof(RejectOvertimeCommand))]
    private OvertimeDto? _selectedOvertime;

    [ObservableProperty] private DepartmentDto? _handoverDepartment;
    [ObservableProperty] private string _handoverNotes = "";
    [ObservableProperty] private string? _openIssues;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcknowledgeCommand))]
    private HandoverDto? _selectedHandover;

    public bool CanManage => session.HasPermission(PermissionNames.StaffManage);

    private static DateTime StartOfWeek(DateTime day) => day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    private DateOnly From => DateOnly.FromDateTime(WeekStart);
    private DateOnly To => From.AddDays(6);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        FrontDeskPageViewModel.Replace(Departments, await api.GetAsync<List<DepartmentDto>>(api.BranchPath("staff/departments")));
        FrontDeskPageViewModel.Replace(Employees, (await api.GetAsync<List<EmployeeDto>>(api.BranchPath("staff/employees"))).OrderBy(e => e.LastName).ThenBy(e => e.FirstName));
        FrontDeskPageViewModel.Replace(Shifts, (await api.GetAsync<List<WorkShiftDto>>(api.BranchPath("staff/shifts"))).Where(s => s.IsActive));
        FrontDeskPageViewModel.Replace(Roster, (await api.GetAsync<List<RosterEntryDto>>(Query.Build(api.BranchPath("staff/roster"), ("from", From), ("to", To))))
            .OrderBy(r => r.WorkDate).ThenBy(r => r.EmployeeName));
        FrontDeskPageViewModel.Replace(Attendance, await api.GetAsync<List<AttendanceSummaryDto>>(Query.Build(api.BranchPath("staff/attendance/summary"), ("from", From), ("to", To))));
        FrontDeskPageViewModel.Replace(Leave, await api.GetAsync<List<LeaveDto>>(api.BranchPath("staff/leave")));
        FrontDeskPageViewModel.Replace(Overtime, await api.GetAsync<List<OvertimeDto>>(api.BranchPath("staff/overtime")));
        FrontDeskPageViewModel.Replace(Handovers, await api.GetAsync<List<HandoverDto>>(api.BranchPath("staff/handovers")));
    });

    [RelayCommand]
    private async Task AddEmployeeAsync()
    {
        if (string.IsNullOrWhiteSpace(NewNumber) || string.IsNullOrWhiteSpace(NewFirstName) || string.IsNullOrWhiteSpace(NewLastName) || NewDepartment is null)
        {
            ErrorMessage = "Enter the employee number, first and last name, and department.";
            return;
        }
        EmployeeDto? created = null;
        if (await RunAsync(async () => created = await api.PostAsync<EmployeeDto>(api.BranchPath("staff/employees"),
                new SaveEmployeeRequest(NewNumber.Trim(), NewFirstName.Trim(), NewLastName.Trim(), NewDepartment.Id,
                    string.IsNullOrWhiteSpace(NewJobTitle) ? null : NewJobTitle.Trim(), null, null, null, DateOnly.FromDateTime(NewHireDate)))))
        {
            NewNumber = NewFirstName = NewLastName = "";
            NewJobTitle = null;
            await LoadAsync();
            Employee = Employees.FirstOrDefault(e => e.Id == created!.Id);
            StatusMessage = $"{created!.FirstName} {created.LastName} added.";
        }
    }

    [RelayCommand]
    private Task PreviousWeekAsync() { WeekStart = WeekStart.AddDays(-7); return LoadAsync(); }

    [RelayCommand]
    private Task NextWeekAsync() { WeekStart = WeekStart.AddDays(7); return LoadAsync(); }

    public string EmployeeName(Guid id) => Employees.FirstOrDefault(e => e.Id == id) is { } e ? $"{e.FirstName} {e.LastName}" : "";

    [RelayCommand]
    private async Task AddRosterAsync()
    {
        if (Employee is null || Shift is null) { ErrorMessage = "Pick the employee and the shift."; return; }
        if (await RunAsync(() => api.PostAsync<RosterEntryDto>(api.BranchPath("staff/roster"),
                new CreateRosterEntryRequest(Employee.Id, Shift.Id, DateOnly.FromDateTime(WorkDate), RosterNotes))))
        {
            RosterNotes = null;
            WeekStart = StartOfWeek(WorkDate);
            await LoadAsync();
            StatusMessage = $"{Employee.FirstName} rostered on {Shift.Code} for {WorkDate:ddd dd MMM}.";
        }
    }

    private bool RosterSelected() => CanManage && SelectedRoster is not null;

    [RelayCommand(CanExecute = nameof(RosterSelected))]
    private async Task RemoveRosterAsync()
    {
        var entry = SelectedRoster!;
        if (await RunAsync(() => api.DeleteAsync(api.BranchPath($"staff/roster/{entry.Id}"))))
        {
            await LoadAsync();
            StatusMessage = $"{entry.EmployeeName} removed from {entry.WorkDate:ddd dd MMM}.";
        }
    }

    [RelayCommand]
    private Task ClockInAsync() => ClockAsync("clock-in", "clocked in");

    [RelayCommand]
    private Task ClockOutAsync() => ClockAsync("clock-out", "clocked out");

    /// <summary>Clocks the picked employee, or yourself when nobody is picked.</summary>
    private async Task ClockAsync(string action, string done)
    {
        AttendanceDto? record = null;
        if (await RunAsync(async () => record = await api.PostAsync<AttendanceDto>(api.BranchPath($"staff/attendance/{action}"), new ClockRequest(Employee?.Id, null))))
        {
            await LoadAsync();
            StatusMessage = $"{record!.EmployeeName} {done} at {record.ClockOutUtc?.ToLocalTime() ?? record.ClockInUtc.ToLocalTime():HH:mm}.";
        }
    }

    [RelayCommand]
    private async Task RequestLeaveAsync()
    {
        if (Employee is null) { ErrorMessage = "Pick the employee."; return; }
        if (LeaveEnd < LeaveStart) { ErrorMessage = "The last day of leave is before the first."; return; }
        if (await RunAsync(() => api.PostAsync<LeaveDto>(api.BranchPath("staff/leave"),
                new CreateLeaveRequest(Employee.Id, LeaveType, DateOnly.FromDateTime(LeaveStart), DateOnly.FromDateTime(LeaveEnd), LeaveReason))))
        {
            LeaveReason = null;
            await LoadAsync();
            StatusMessage = "Leave requested; it needs approval by someone else.";
        }
    }

    private bool LeavePending() => CanManage && SelectedLeave is { Status: "Pending" };
    private bool OvertimePending() => CanManage && SelectedOvertime is { Status: "Pending" };

    [RelayCommand(CanExecute = nameof(LeavePending))]
    private Task ApproveLeaveAsync() => DecideAsync($"staff/leave/{SelectedLeave!.Id}/decision", true, SelectedLeave.ConcurrencyStamp);

    [RelayCommand(CanExecute = nameof(LeavePending))]
    private Task RejectLeaveAsync() => DecideAsync($"staff/leave/{SelectedLeave!.Id}/decision", false, SelectedLeave.ConcurrencyStamp);

    [RelayCommand(CanExecute = nameof(OvertimePending))]
    private Task ApproveOvertimeAsync() => DecideAsync($"staff/overtime/{SelectedOvertime!.Id}/decision", true, SelectedOvertime.ConcurrencyStamp);

    [RelayCommand(CanExecute = nameof(OvertimePending))]
    private Task RejectOvertimeAsync() => DecideAsync($"staff/overtime/{SelectedOvertime!.Id}/decision", false, SelectedOvertime.ConcurrencyStamp);

    private async Task DecideAsync(string path, bool approved, Guid stamp)
    {
        if (!approved && !dialogs.Confirm("Reject this request?")) return;
        if (await RunAsync(() => api.PostAsync<object>(api.BranchPath(path), new StaffDecisionRequest(approved, stamp))))
        {
            await LoadAsync();
            StatusMessage = approved ? "Approved." : "Rejected.";
        }
    }

    [RelayCommand]
    private async Task WriteHandoverAsync()
    {
        if (HandoverDepartment is null || string.IsNullOrWhiteSpace(HandoverNotes)) { ErrorMessage = "Pick the department and write the handover notes."; return; }
        if (await RunAsync(() => api.PostAsync<HandoverDto>(api.BranchPath("staff/handovers"),
                new CreateHandoverRequest(HandoverDepartment.Id, HandoverNotes.Trim(), string.IsNullOrWhiteSpace(OpenIssues) ? null : OpenIssues.Trim()))))
        {
            HandoverNotes = "";
            OpenIssues = null;
            await LoadAsync();
            StatusMessage = "Handover saved for the next shift.";
        }
    }

    private bool HandoverUnread() => SelectedHandover is { AcknowledgedByUserId: null };

    [RelayCommand(CanExecute = nameof(HandoverUnread))]
    private async Task AcknowledgeAsync()
    {
        var handover = SelectedHandover!;
        if (await RunAsync(() => api.PostAsync<HandoverDto>(api.BranchPath($"staff/handovers/{handover.Id}/acknowledge"), new { })))
        {
            await LoadAsync();
            StatusMessage = "Handover acknowledged.";
        }
    }
}
