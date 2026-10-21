using System.Net.Http.Json;
using System.Text;
using HMS.Api.IntegrationTests;
using HMS.Application.Security;
using HMS.Contracts.Staff;
using HMS.Desktop.Services;
using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Tests;

/// <summary>Back-office screens (night audit, accounting, staff, documents, security, users, audit) against the in-memory API.</summary>
public sealed class AdminScreenTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>, IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("hms-desktop-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private Task<(ApiClient Api, SessionState Session)> SignInAsync(HotelSetup hotel, string? email = null) => TestDesk.SignInAsync(factory, hotel, email);

    private static void Ok(PageViewModel page) => TestDesk.Ok(page);

    [Fact]
    public async Task Night_audit_reports_a_blocking_arrival_then_closes_after_it_is_accepted()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        await hotel.BookAsync(nights: 1, lastName: "Late");
        var (api, session) = await SignInAsync(hotel);
        var audit = new NightAuditPageViewModel(api, session, new YesDialogs());
        await audit.LoadAsync();
        Ok(audit);
        Assert.Equal(hotel.Today, audit.Status!.NextDateToClose);
        Assert.True(audit.CloseDayCommand.CanExecute(null));

        await audit.CloseDayCommand.ExecuteAsync(null);
        Ok(audit);
        Assert.Contains("not closed", audit.StatusMessage);
        audit.SelectedException = audit.Exceptions.Single(e => e.Code == "ARRIVAL_PENDING");
        await audit.AcceptExceptionCommand.ExecuteAsync(null);
        Assert.NotNull(audit.ErrorMessage); // a reason is required
        audit.AcceptReason = "Guest arriving after midnight";
        await audit.AcceptExceptionCommand.ExecuteAsync(null);
        Ok(audit);

        await audit.CloseDayCommand.ExecuteAsync(null);
        Ok(audit);
        Assert.EndsWith("is closed.", audit.StatusMessage);
        Assert.Equal(hotel.Today.AddDays(1), audit.Status!.CurrentBusinessDate);
        Assert.Contains(audit.History, c => c.BusinessDate == hotel.Today && c.Status == "Closed");
        Assert.False(audit.CloseDayCommand.CanExecute(null)); // tomorrow cannot close yet
    }

    [Fact]
    public async Task Manual_journal_is_posted_shown_in_the_ledger_and_reversed()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var (api, session) = await SignInAsync(hotel);
        var page = new AccountingPageViewModel(api, session, new YesDialogs())
        {
            From = hotel.Today.AddDays(-1).ToDateTime(TimeOnly.MinValue),
            To = hotel.Today.ToDateTime(TimeOnly.MinValue),
        };
        await page.LoadAsync();
        Ok(page);
        var cash = page.Accounts.Single(a => a.Code == "1000");
        var equity = page.Accounts.Single(a => a.Code == "3000");

        page.EntryDate = hotel.Today.ToDateTime(TimeOnly.MinValue);
        page.EntryDescription = "Owner float";
        page.DraftLines[0].Account = cash;
        page.DraftLines[0].Debit = 10_000;
        page.DraftLines[1].Account = equity;
        page.DraftLines[1].Credit = 9_000;
        await page.PostJournalCommand.ExecuteAsync(null);
        Assert.Contains("must be equal", page.ErrorMessage); // caught before it reaches the server
        page.DraftLines[1].Credit = 10_000;
        await page.PostJournalCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Equal(page.TotalDebit, page.TotalCredit);
        Assert.Contains(page.Periods, p => p.Status == "Open");
        Assert.Equal(10_000m, page.TrialBalance.Single(l => l.Code == "1000").Debit);

        page.LedgerAccount = cash; // loads the ledger on its own
        for (var i = 0; i < 100 && page.Ledger.Count == 0; i++) await Task.Delay(20);
        Assert.Equal(10_000m, Assert.Single(page.Ledger).RunningBalance);

        page.SelectedJournal = page.Journals.Single(j => j.Description == "Owner float");
        page.ReverseReason = "Posted in error";
        await page.ReverseCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Contains("reversed by", page.StatusMessage);
        Assert.Equal(0m, page.Ledger.Last().RunningBalance);
    }

    [Fact]
    public async Task Staff_are_added_rostered_given_leave_and_hand_over()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var hrEmail = await hotel.CreateStaffAsync(Roles.HumanResources);
        var department = await (await hotel.Admin.PostAsJsonAsync(hotel.P("staff/departments"), new SaveDepartmentRequest("FO", "Front office"))).Content.ReadFromJsonAsync<DepartmentDto>();
        var shift = await (await hotel.Admin.PostAsJsonAsync(hotel.P("staff/shifts"), new SaveWorkShiftRequest("AM", "Morning", "07:00", "15:00", 30))).Content.ReadFromJsonAsync<WorkShiftDto>();
        Assert.NotNull(department);
        Assert.NotNull(shift);

        var (api, session) = await SignInAsync(hotel, hrEmail);
        var page = new StaffPageViewModel(api, session, new YesDialogs());
        await page.LoadAsync();
        Ok(page);

        page.NewNumber = "E100";
        page.NewFirstName = "Bola";
        page.NewLastName = "Ade";
        page.NewDepartment = page.Departments.Single();
        page.NewJobTitle = "Receptionist";
        await page.AddEmployeeCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Equal("E100", page.Employee?.EmployeeNumber);

        page.Shift = page.Shifts.Single();
        page.WorkDate = hotel.Today.AddDays(7).ToDateTime(TimeOnly.MinValue);
        await page.AddRosterCommand.ExecuteAsync(null);
        Ok(page);
        page.SelectedRoster = Assert.Single(page.Roster);
        await page.RemoveRosterCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Empty(page.Roster);

        // HR can request leave but not approve their own request; the manager approves it on their screen.
        page.LeaveStart = hotel.Today.AddDays(10).ToDateTime(TimeOnly.MinValue);
        page.LeaveEnd = hotel.Today.AddDays(12).ToDateTime(TimeOnly.MinValue);
        await page.RequestLeaveCommand.ExecuteAsync(null);
        Ok(page);
        page.SelectedLeave = page.Leave.Single();
        await page.ApproveLeaveCommand.ExecuteAsync(null);
        Assert.NotNull(page.ErrorMessage);

        var (adminApi, adminSession) = await SignInAsync(hotel);
        var manager = new StaffPageViewModel(adminApi, adminSession, new YesDialogs());
        await manager.LoadAsync();
        manager.SelectedLeave = manager.Leave.Single();
        await manager.ApproveLeaveCommand.ExecuteAsync(null);
        Ok(manager);
        Assert.Equal("Approved", manager.Leave.Single().Status);

        // Manager clocks the employee in and out.
        manager.Employee = manager.Employees.Single(e => e.EmployeeNumber == "E100");
        await manager.ClockInCommand.ExecuteAsync(null);
        Ok(manager);
        await manager.ClockOutCommand.ExecuteAsync(null);
        Ok(manager);
        Assert.Contains("clocked out", manager.StatusMessage);

        // Handover written by HR, acknowledged by the manager.
        page.HandoverDepartment = page.Departments.Single();
        page.HandoverNotes = "Room 101 guest asked for a late check-out.";
        await page.WriteHandoverCommand.ExecuteAsync(null);
        Ok(page);
        await manager.LoadAsync();
        manager.SelectedHandover = manager.Handovers.Single();
        await manager.AcknowledgeCommand.ExecuteAsync(null);
        Ok(manager);
        Assert.NotNull(manager.Handovers.Single().AcknowledgedByUserId);
    }

    [Fact]
    public async Task Document_is_uploaded_scanned_downloaded_signed_and_held()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var source = Path.Combine(_folder, "fire-drill.pdf");
        var content = Encoding.ASCII.GetBytes("%PDF-1.4\nFire drill record " + Guid.NewGuid() + "\n%%EOF");
        await File.WriteAllBytesAsync(source, content);
        var dialogs = new YesDialogs { FileToOpen = source, FileToSave = Path.Combine(_folder, "copy.pdf") };
        var (api, session) = await SignInAsync(hotel);
        var page = new DocumentsPageViewModel(api, session, dialogs);
        await page.LoadAsync();
        Ok(page);

        page.NewTitle = "Fire drill October";
        page.NewCategory = "Other";
        await page.UploadCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Equal("fire-drill.pdf", page.SelectedVersion?.OriginalFileName);
        await page.DownloadCommand.ExecuteAsync(null);
        Assert.Contains("not available yet", page.ErrorMessage); // still in quarantine

        var (admin, _) = await factory.LoginAsAdminAsync();
        Assert.True((await admin.PostAsync("/api/v1/system/jobs/run", null)).IsSuccessStatusCode);
        await page.LoadAsync();
        Assert.Equal("Clean", page.SelectedVersion?.ScanStatus);
        await page.DownloadCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Equal(content, await File.ReadAllBytesAsync(dialogs.FileToSave));

        page.SignPurpose = "Reviewed by duty manager";
        await page.SignCommand.ExecuteAsync(null);
        Ok(page);
        Assert.True(Assert.Single(page.Signatures).IsValid);

        page.LegalHold = true;
        await page.SaveRetentionCommand.ExecuteAsync(null);
        Ok(page);
        await page.DeleteCommand.ExecuteAsync(null);
        Assert.NotNull(page.ErrorMessage); // legal hold blocks deletion
        Assert.Single(page.Documents);
    }

    [Fact]
    public async Task Security_desk_logs_incidents_items_visitors_and_emergencies()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var (api, session) = await SignInAsync(hotel);
        var page = new SecurityPageViewModel(api, session, new YesDialogs());
        await page.LoadAsync();
        Ok(page);

        page.IncidentType = "Damage";
        page.IncidentTitle = "Broken window";
        page.IncidentDescription = "Window in the lobby cracked during the storm.";
        await page.ReportIncidentCommand.ExecuteAsync(null);
        Ok(page);
        page.CaseNote = "Glazier booked";
        await page.AddNoteCommand.ExecuteAsync(null);
        Ok(page);
        page.CaseNote = "Window replaced";
        await page.ResolveCommand.ExecuteAsync(null);
        Ok(page);
        await page.CloseIncidentCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Empty(page.Incidents); // only open incidents are listed

        page.ItemDescription = "Black wallet";
        page.FoundLocation = "Restaurant";
        page.IsValuable = true;
        await page.RecordItemCommand.ExecuteAsync(null);
        Ok(page);
        page.SelectedItem = page.LostItems.Single();
        page.ReceivedBy = "Mr Okafor";
        page.IdentityCheck = "Passport checked";
        await page.ReturnItemCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Empty(page.LostItems);

        page.VisitorName = "Ada Nwosu";
        page.Purpose = "Meeting a guest";
        await page.SignInVisitorCommand.ExecuteAsync(null);
        Ok(page);
        page.SelectedVisitor = page.Visitors.Single();
        await page.SignOutVisitorCommand.ExecuteAsync(null);
        Ok(page);
        Assert.Empty(page.Visitors);

        page.EmergencyType = "PowerOutage";
        page.EmergencyDescription = "Mains power lost in block B";
        await page.DeclareEmergencyCommand.ExecuteAsync(null);
        Ok(page);
        page.EmergencyAction = "Generator started";
        await page.LogActionCommand.ExecuteAsync(null);
        Ok(page);
        page.EmergencySummary = "Power restored after 40 minutes";
        await page.EndEmergencyCommand.ExecuteAsync(null);
        Ok(page);
        Assert.NotNull(page.Emergencies.Single().EndedAtUtc);
    }

    [Fact]
    public async Task Users_are_created_and_deactivated_and_the_audit_log_shows_it()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var dialogs = new YesDialogs { FileToSave = Path.Combine(_folder, "audit.csv") };
        var (api, session) = await SignInAsync(hotel);
        var users = new UsersPageViewModel(api, session, dialogs);
        await users.LoadAsync();
        Ok(users);
        Assert.Contains(users.Roles, r => r.Value == Roles.Receptionist);

        var email = $"desk-{Guid.NewGuid():N}@example.com";
        users.Email = email;
        users.FullName = "Desk Clerk";
        users.TemporaryPassword = "short";
        users.Roles.Single(r => r.Value == Roles.Receptionist).IsSelected = true;
        await users.CreateCommand.ExecuteAsync(null);
        Assert.Contains("12 characters", users.ErrorMessage);
        users.TemporaryPassword = "Temporary-Pass-123!";
        await users.CreateCommand.ExecuteAsync(null);
        Ok(users);
        Assert.Equal(email, users.Selected?.Email);
        Assert.True(users.Selected!.MustChangePassword);

        users.IsActive = false;
        await users.SaveAccessCommand.ExecuteAsync(null);
        Ok(users);
        Assert.False(users.Users.Single(u => u.Email == email).IsActive);

        var audit = new AuditLogPageViewModel(api, dialogs) { From = DateTime.Today.AddDays(-1) };
        await audit.SearchCommand.ExecuteAsync(null);
        Ok(audit);
        Assert.NotEmpty(audit.Entries);
        await audit.ExportCommand.ExecuteAsync(null);
        Ok(audit);
        Assert.True(new FileInfo(dialogs.FileToSave!).Length > 0);
    }
}
