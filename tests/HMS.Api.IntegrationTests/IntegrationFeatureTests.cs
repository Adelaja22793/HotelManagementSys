using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HMS.Api.Services;
using HMS.Application.Security;
using HMS.Contracts.Analytics;
using HMS.Contracts.BusinessDay;
using HMS.Contracts.Common;
using HMS.Contracts.Documents;
using HMS.Contracts.FrontDesk;
using HMS.Contracts.Guests;
using HMS.Contracts.Notifications;
using HMS.Contracts.Operations;
using HMS.Contracts.Reservations;
using HMS.Contracts.SecurityOps;
using static HMS.Api.IntegrationTests.HotelSetup;

namespace HMS.Api.IntegrationTests;

/// <summary>Phase 4: night audit, analytics, notifications, documents, security log and audit export.</summary>
public class IntegrationFeatureTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    // ---------- helpers ----------

    private async Task<JobRunResult> RunJobsAsync()
    {
        var (admin, _) = await factory.LoginAsAdminAsync();
        return await ApiAssert.OkAsync<JobRunResult>(await admin.PostAsync("/api/v1/system/jobs/run", null));
    }

    private static Task<HttpResponseMessage> CloseDayAsync(HotelSetup hotel, HttpClient client, DateOnly date) =>
        client.PostAsJsonAsync(hotel.P("business-day/close"), new CloseBusinessDayRequest(date));

    private static Task<HttpResponseMessage> UploadAsync(HotelSetup hotel, HttpClient client, string fileName, byte[] content, string category,
        string contentType = "application/pdf", string? classification = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent("Test " + category), "title");
        form.Add(new StringContent(category), "category");
        if (classification is not null) form.Add(new StringContent(classification), "classification");
        return client.PostAsync(hotel.P("documents"), form);
    }

    private static byte[] Pdf(string body) => Encoding.ASCII.GetBytes("%PDF-1.4\n" + body + "\n%%EOF");

    private static async Task<List<OutboxMessageDto>> OutboxForAsync(HotelSetup hotel, string sourceId) =>
        (await hotel.GetAsync<List<OutboxMessageDto>>("notifications/outbox")).Where(m => m.SourceId == sourceId).ToList();

    // ---------- night audit ----------

    [Fact]
    public async Task Night_audit_blocks_on_pending_arrival_then_closes_once_and_advances_the_date()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var today = hotel.Today;
        var status = await hotel.GetAsync<BusinessDayStatusDto>("business-day");
        Assert.Null(status.LastClosedBusinessDate);
        Assert.Equal(today, status.NextDateToClose);
        Assert.True(status.CanCloseNow);

        var booking = await hotel.BookAsync(nights: 2, lastName: "Audit");

        await ApiAssert.ErrorAsync(await CloseDayAsync(hotel, hotel.Admin, today.AddDays(1)), HttpStatusCode.Conflict, "BUSINESS_DATE_MISMATCH");
        var reception = await hotel.StaffAsync(Receptionist);
        await ApiAssert.ErrorAsync(await CloseDayAsync(hotel, reception, today), HttpStatusCode.Forbidden);

        var blocked = await ApiAssert.OkAsync<BusinessDayCloseDto>(await CloseDayAsync(hotel, hotel.Admin, today));
        Assert.Equal("Blocked", blocked.Status);
        var arrival = blocked.Exceptions.Single(e => e.Code == "ARRIVAL_PENDING");
        Assert.Equal(booking.Id.ToString(), arrival.Reference);
        Assert.Equal("Blocking", arrival.Severity);
        Assert.Equal(today, (await hotel.GetAsync<BusinessDayStatusDto>("business-day")).CurrentBusinessDate);

        await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P($"business-day/exceptions/{arrival.Id}/accept"),
            new AcceptExceptionRequest(" ")), HttpStatusCode.BadRequest, "REASON_REQUIRED");
        var accepted = await ApiAssert.OkAsync<BusinessDayCloseDto>(await hotel.Admin.PostAsJsonAsync(
            hotel.P($"business-day/exceptions/{arrival.Id}/accept"), new AcceptExceptionRequest("Guest arriving after midnight, confirmed by phone")));
        Assert.Equal("Accepted", accepted.Exceptions.Single(e => e.Id == arrival.Id).Status);

        var closed = await ApiAssert.OkAsync<BusinessDayCloseDto>(await CloseDayAsync(hotel, hotel.Admin, today));
        Assert.Equal("Closed", closed.Status);
        Assert.Equal(2, closed.RunCount);
        Assert.Equal(blocked.Id, closed.Id);

        // A retry of the same request returns the original close instead of failing or closing again.
        var retry = await ApiAssert.OkAsync<BusinessDayCloseDto>(await CloseDayAsync(hotel, hotel.Admin, today));
        Assert.Equal(closed.Id, retry.Id);
        Assert.Equal(2, retry.RunCount);

        status = await hotel.GetAsync<BusinessDayStatusDto>("business-day");
        Assert.Equal(today, status.LastClosedBusinessDate);
        Assert.Equal(today.AddDays(1), status.CurrentBusinessDate);
        Assert.Equal(today.AddDays(1), status.NextDateToClose);
        Assert.False(status.CanCloseNow);
        await hotel.RefreshTodayAsync();
        Assert.Equal(today.AddDays(1), hotel.Today);

        // The next day cannot be closed before it starts.
        await ApiAssert.ErrorAsync(await CloseDayAsync(hotel, hotel.Admin, today.AddDays(1)), HttpStatusCode.Conflict, "BUSINESS_DAY_NOT_STARTED");

        // The close took a final snapshot and told report readers.
        var dashboard = await hotel.GetAsync<DashboardDto>($"analytics/dashboard?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        var day = Assert.Single(dashboard.Days);
        Assert.True(day.IsFinal);
        Assert.Equal(3, day.RoomsAvailable);
        Assert.Equal(0, day.RoomsSold);

        var inbox = await hotel.GetAsync<List<InboxItemDto>>("notifications/inbox?unreadOnly=true");
        var report = inbox.Single(i => i.EventType == "DailyReportReady");
        Assert.Contains(today.ToString("yyyy-MM-dd"), report.Subject);
        Assert.Contains("Occupancy 0%", report.Body);
        var read = await hotel.Admin.PostAsync(hotel.P($"notifications/inbox/{report.Id}/read"), null);
        Assert.True(read.IsSuccessStatusCode);
        inbox = await hotel.GetAsync<List<InboxItemDto>>("notifications/inbox?unreadOnly=true");
        Assert.DoesNotContain(inbox, i => i.Id == report.Id);
    }

    // ---------- analytics ----------

    [Fact]
    public async Task Dashboard_uses_weighted_totals_and_reports_missing_days()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 4);
        var booking = await hotel.BookAsync(nights: 2, lastName: "Metrics");
        await ApiAssert.OkAsync<StayDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("front-desk/check-in"),
            new CheckInRequest(booking.Id, hotel.Rooms[0].Id, booking.ConcurrencyStamp)));

        var from = hotel.Today.AddDays(-2);
        var refresh = await ApiAssert.OkAsync<RefreshResultDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("analytics/refresh"),
            new RefreshMetricsRequest(hotel.Today, hotel.Today)));
        Assert.Equal(1, refresh.DaysComputed);

        var dashboard = await hotel.GetAsync<DashboardDto>($"analytics/dashboard?from={from:yyyy-MM-dd}&to={hotel.Today:yyyy-MM-dd}");
        Assert.Equal([from, from.AddDays(1)], dashboard.MissingDates);
        var day = Assert.Single(dashboard.Days);
        Assert.False(day.IsFinal);
        Assert.Equal(4, day.RoomsAvailable);
        Assert.Equal(1, day.RoomsSold);
        Assert.Equal(50_000m, day.RoomRevenue);
        Assert.Equal(1, day.Arrivals);
        Assert.Equal(0.25m, dashboard.Totals.Occupancy);
        Assert.Equal(50_000m, dashboard.Totals.Adr);
        Assert.Equal(12_500m, dashboard.Totals.RevPar);
        Assert.Equal(50_000m, dashboard.Departments.Single(d => d.Department == "Rooms").Revenue);

        var forecast = await hotel.GetAsync<ForecastDto>("analytics/forecast?days=3");
        Assert.Equal("on-the-books-v1", forecast.Model);
        Assert.Equal(1, forecast.Days.Single(d => d.Date == hotel.Today.AddDays(1)).OnTheBooks);

        var (company, _) = await factory.LoginAsAdminAsync();
        var comparison = await ApiAssert.OkAsync<BranchComparisonDto>(await company.GetAsync(
            $"/api/v1/analytics/branches?from={hotel.Today:yyyy-MM-dd}&to={hotel.Today:yyyy-MM-dd}"));
        Assert.Contains(comparison.Branches, b => b.BranchId == hotel.Branch.Id && b.Totals.RoomsSold == 1);

        // Front desk staff do not see management reports.
        var reception = await hotel.StaffAsync(Receptionist);
        await ApiAssert.ErrorAsync(await reception.GetAsync(hotel.P($"analytics/dashboard?from={from:yyyy-MM-dd}&to={hotel.Today:yyyy-MM-dd}")),
            HttpStatusCode.Forbidden);
    }

    // ---------- notifications ----------

    [Fact]
    public async Task Reservation_email_is_queued_once_and_sent_by_the_worker()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var request = Booking(hotel.Standard.Id, hotel.Today.AddDays(5), 2, lastName: "Mailtest");
        var key = Guid.NewGuid().ToString();
        var booking = await ApiAssert.OkAsync<ReservationDto>(await hotel.PostBookingAsync(hotel.Admin, request, key), HttpStatusCode.Created);
        await hotel.PostBookingAsync(hotel.Admin, request, key); // client retry with the same key

        var queued = Assert.Single(await OutboxForAsync(hotel, booking.Id.ToString()));
        Assert.Equal("ReservationConfirmed", queued.EventType);
        Assert.Equal("Email", queued.Channel);
        Assert.Equal("mailtest@guest.test", queued.Recipient);
        Assert.Equal("Pending", queued.Status);

        var run = await RunJobsAsync();
        var sent = Assert.Single(await OutboxForAsync(hotel, booking.Id.ToString()));
        Assert.True(sent.Status == "Sent", $"{sent.Status}: {sent.LastError}");
        Assert.True(run.Notifications.Sent >= 1);
        Assert.Equal(1, sent.Attempts);
        Assert.Equal("Submitted", Assert.Single(sent.Deliveries).Status);
        var eml = Directory.GetFiles(factory.MailPickupPath, "*.eml").Select(File.ReadAllText).Single(t => t.Contains("mailtest@guest.test"));
        Assert.Contains(booking.ReservationNumber, eml);

        // Running again sends nothing twice.
        await RunJobsAsync();
        Assert.Equal(1, Assert.Single(await OutboxForAsync(hotel, booking.Id.ToString())).Attempts);
    }

    [Fact]
    public async Task Message_without_a_provider_fails_visibly_and_can_be_retried_or_cancelled()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var request = Booking(hotel.Standard.Id, hotel.Today.AddDays(3), 1) with
        {
            NewGuest = new SaveGuestRequest("Bayo", "Phoneonly", null, "+2348030000000", "NG", null, null, null, null),
        };
        var booking = await ApiAssert.OkAsync<ReservationDto>(await hotel.PostBookingAsync(hotel.Admin, request), HttpStatusCode.Created);
        var sms = Assert.Single(await OutboxForAsync(hotel, booking.Id.ToString()));
        Assert.Equal("Sms", sms.Channel);

        await RunJobsAsync();
        var failed = Assert.Single(await OutboxForAsync(hotel, booking.Id.ToString()));
        Assert.Equal("Failed", failed.Status);
        Assert.Contains("No Sms provider", failed.LastError);

        var reception = await hotel.StaffAsync(Receptionist);
        await ApiAssert.ErrorAsync(await reception.PostAsync(hotel.P($"notifications/outbox/{failed.Id}/retry"), null), HttpStatusCode.Forbidden);

        var retried = await ApiAssert.OkAsync<OutboxMessageDto>(await hotel.Admin.PostAsync(hotel.P($"notifications/outbox/{failed.Id}/retry"), null));
        Assert.Equal("Pending", retried.Status);
        var cancelled = await ApiAssert.OkAsync<OutboxMessageDto>(await hotel.Admin.PostAsync(hotel.P($"notifications/outbox/{failed.Id}/cancel"), null));
        Assert.Equal("Cancelled", cancelled.Status);
        await RunJobsAsync();
        Assert.Equal("Cancelled", Assert.Single(await OutboxForAsync(hotel, booking.Id.ToString())).Status);
    }

    [Fact]
    public async Task Branch_template_overrides_the_default_text()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        await ApiAssert.OkAsync<TemplateDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("notifications/templates"),
            new SaveTemplateRequest("ReservationConfirmed", "Email", "en", "Welcome {{GuestName}}", "Booking {{ReservationNumber}} is held for you.", true)),
            HttpStatusCode.Created);

        var booking = await ApiAssert.OkAsync<ReservationDto>(await hotel.PostBookingAsync(hotel.Admin,
            Booking(hotel.Standard.Id, hotel.Today.AddDays(2), 1, lastName: "Templated")), HttpStatusCode.Created);
        var message = Assert.Single(await OutboxForAsync(hotel, booking.Id.ToString()));
        Assert.Equal("Welcome Ada Templated", message.Subject);
    }

    // ---------- documents ----------

    [Fact]
    public async Task Contract_stays_quarantined_until_scanned_and_approved_by_someone_else()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var (manager, managerId) = await hotel.StaffWithIdAsync(Roles.HotelManager);
        var reception = await hotel.StaffAsync(Receptionist);
        var content = Pdf("Supplier contract " + Guid.NewGuid());

        await ApiAssert.ErrorAsync(await UploadAsync(hotel, reception, "contract.pdf", content, "Contract"), HttpStatusCode.Forbidden);
        await ApiAssert.ErrorAsync(await UploadAsync(hotel, manager, "contract.pdf", Encoding.ASCII.GetBytes("MZ not a pdf"), "Contract"),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await ApiAssert.ErrorAsync(await UploadAsync(hotel, manager, "run.exe", content, "Contract", "application/octet-stream"),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        var document = await ApiAssert.OkAsync<DocumentDto>(await UploadAsync(hotel, manager, "..\\..\\contract.pdf", content, "Contract"), HttpStatusCode.Created);
        var version = Assert.Single(document.Versions);
        Assert.Equal("Pending", document.Status);
        Assert.Equal("contract.pdf", version.OriginalFileName);
        Assert.Equal("Pending", version.ScanStatus);
        Assert.Equal("Pending", version.Approval);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)), version.Sha256);

        await ApiAssert.ErrorAsync(await manager.GetAsync(hotel.P($"documents/{document.Id}/content")), HttpStatusCode.Conflict, "DOCUMENT_NOT_RELEASED");
        await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P($"documents/{document.Id}/versions/{version.Id}/decision"),
            new DocumentApprovalRequest(true, null)), HttpStatusCode.Conflict, "SCAN_PENDING");

        Assert.True((await RunJobsAsync()).Scans.Clean >= 1);
        document = await ApiAssert.OkAsync<DocumentDto>(await manager.GetAsync(hotel.P($"documents/{document.Id}")));
        Assert.Equal("Clean", document.Versions.Single().ScanStatus);
        Assert.Equal("Pending", document.Status); // still needs approval

        await ApiAssert.ErrorAsync(await manager.PostAsJsonAsync(hotel.P($"documents/{document.Id}/versions/{version.Id}/decision"),
            new DocumentApprovalRequest(true, null)), HttpStatusCode.Conflict, "SELF_APPROVAL");
        document = await ApiAssert.OkAsync<DocumentDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"documents/{document.Id}/versions/{version.Id}/decision"),
            new DocumentApprovalRequest(true, "Checked against the purchase order")));
        Assert.Equal("Active", document.Status);
        Assert.Equal(version.Id, document.CurrentVersionId);

        var download = await manager.GetAsync(hotel.P($"documents/{document.Id}/content"));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());

        var signature = await ApiAssert.OkAsync<SignatureDto>(await manager.PostAsJsonAsync(
            hotel.P($"documents/{document.Id}/versions/{version.Id}/signatures"), new SignDocumentRequest("Accepted on behalf of the hotel")));
        Assert.True(signature.IsValid);
        Assert.Equal(version.Sha256, signature.ContentSha256);
        Assert.True(Assert.Single(await ApiAssert.OkAsync<List<SignatureDto>>(await manager.GetAsync(hotel.P($"documents/{document.Id}/signatures")))).IsValid);

        // Legal hold and retention stop deletion.
        document = await ApiAssert.OkAsync<DocumentDto>(await manager.PutAsJsonAsync(hotel.P($"documents/{document.Id}/retention"),
            new RetentionRequest(null, true, document.ConcurrencyStamp)));
        await ApiAssert.ErrorAsync(await manager.DeleteAsync(hotel.P($"documents/{document.Id}")), HttpStatusCode.Conflict, "LEGAL_HOLD");
        document = await ApiAssert.OkAsync<DocumentDto>(await manager.PutAsJsonAsync(hotel.P($"documents/{document.Id}/retention"),
            new RetentionRequest(hotel.Today.AddYears(6), false, document.ConcurrencyStamp)));
        await ApiAssert.ErrorAsync(await manager.DeleteAsync(hotel.P($"documents/{document.Id}")), HttpStatusCode.Conflict, "RETENTION_ACTIVE");

        // Every download is in the audit trail with the user who made it.
        var audit = await ApiAssert.OkAsync<PagedResult<AuditLogDto>>(await hotel.Admin.GetAsync(
            $"/api/v1/branches/{hotel.Branch.Id}/audit-logs?action=Downloaded&entityId={version.Id}"));
        Assert.Contains(audit.Items, a => a.UserId == managerId);
    }

    [Fact]
    public async Task Infected_file_is_rejected_and_never_downloadable()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var document = await ApiAssert.OkAsync<DocumentDto>(await UploadAsync(hotel, hotel.Admin, "report.pdf",
            Pdf("X5O!P%@AP EICAR-TEST payload"), "Incident"), HttpStatusCode.Created);
        Assert.False(document.RequiresApproval);

        await RunJobsAsync();
        document = await hotel.GetAsync<DocumentDto>($"documents/{document.Id}");
        Assert.Equal("Rejected", document.Status);
        Assert.Equal("Infected", document.Versions.Single().ScanStatus);
        await ApiAssert.ErrorAsync(await hotel.Admin.GetAsync(hotel.P($"documents/{document.Id}/content?versionId={document.Versions.Single().Id}")),
            HttpStatusCode.Conflict, "DOCUMENT_QUARANTINED");
    }

    [Fact]
    public async Task Scanner_outage_keeps_files_quarantined_until_rescanned()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        factory.Scanner.Unavailable = true;
        DocumentDto document;
        try
        {
            document = await ApiAssert.OkAsync<DocumentDto>(await UploadAsync(hotel, hotel.Admin, "notes.pdf", Pdf("maintenance notes"), "Maintenance"),
                HttpStatusCode.Created);
            await RunJobsAsync();
            document = await hotel.GetAsync<DocumentDto>($"documents/{document.Id}");
            Assert.Equal("Failed", document.Versions.Single().ScanStatus);
            await ApiAssert.ErrorAsync(await hotel.Admin.GetAsync(hotel.P($"documents/{document.Id}/content")), HttpStatusCode.Conflict, "DOCUMENT_NOT_RELEASED");
        }
        finally
        {
            factory.Scanner.Unavailable = false;
        }

        var versionId = document.Versions.Single().Id;
        await ApiAssert.OkAsync<DocumentDto>(await hotel.Admin.PostAsync(hotel.P($"documents/{document.Id}/versions/{versionId}/rescan"), null));
        await RunJobsAsync();
        document = await hotel.GetAsync<DocumentDto>($"documents/{document.Id}");
        Assert.Equal("Active", document.Status);
    }

    [Fact]
    public async Task Restricted_documents_are_hidden_from_ordinary_readers()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var document = await ApiAssert.OkAsync<DocumentDto>(await UploadAsync(hotel, hotel.Admin, "policy.pdf", Pdf("salary policy"), "Employee",
            classification: "Restricted"), HttpStatusCode.Created);
        var reception = await hotel.StaffAsync(Receptionist);
        await ApiAssert.ErrorAsync(await reception.GetAsync(hotel.P($"documents/{document.Id}")), HttpStatusCode.NotFound);
        var list = await ApiAssert.OkAsync<List<DocumentDto>>(await reception.GetAsync(hotel.P("documents")));
        Assert.DoesNotContain(list, d => d.Id == document.Id);
    }

    // ---------- security log and complaints ----------

    [Fact]
    public async Task Incident_follows_its_workflow_with_an_append_only_history()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var reception = await hotel.StaffAsync(Receptionist);
        var (_, officerId) = await hotel.StaffWithIdAsync(Roles.HotelManager);

        var incident = await ApiAssert.OkAsync<IncidentDto>(await reception.PostAsJsonAsync(hotel.P("security/incidents"),
            new CreateIncidentRequest("Theft", "High", "Phone missing", "Guest reports a phone missing from the lobby.", "Lobby", null, null, null)),
            HttpStatusCode.Created);
        Assert.StartsWith("INC-", incident.IncidentNumber);
        Assert.Equal("Open", incident.Status);

        // Only security managers move the case on.
        await ApiAssert.ErrorAsync(await reception.PostAsJsonAsync(hotel.P($"security/incidents/{incident.Id}/actions"),
            new CaseUpdateRequest("resolve", null, "found", incident.ConcurrencyStamp)), HttpStatusCode.Forbidden);

        incident = await ApiAssert.OkAsync<IncidentDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"security/incidents/{incident.Id}/actions"),
            new CaseUpdateRequest("assign", officerId, null, incident.ConcurrencyStamp)));
        await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P($"security/incidents/{incident.Id}/actions"),
            new CaseUpdateRequest("close", null, null, incident.ConcurrencyStamp)), HttpStatusCode.Conflict, "CASE_STATE");
        var stale = incident.ConcurrencyStamp;
        incident = await ApiAssert.OkAsync<IncidentDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"security/incidents/{incident.Id}/actions"),
            new CaseUpdateRequest("resolve", null, "Phone was handed in at the bar.", incident.ConcurrencyStamp)));
        await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P($"security/incidents/{incident.Id}/actions"),
            new CaseUpdateRequest("note", null, "late note", stale)), HttpStatusCode.Conflict, ErrorCodes.ConcurrencyConflict);
        incident = await ApiAssert.OkAsync<IncidentDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"security/incidents/{incident.Id}/actions"),
            new CaseUpdateRequest("close", null, null, incident.ConcurrencyStamp)));

        Assert.Equal("Closed", incident.Status);
        Assert.Equal(officerId, incident.OwnerUserId);
        Assert.True(incident.Actions.Count >= 4);
    }

    [Fact]
    public async Task Lost_property_keeps_chain_of_custody_and_hold_period()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var reception = await hotel.StaffAsync(Receptionist);
        var watch = await ApiAssert.OkAsync<LostItemDto>(await reception.PostAsJsonAsync(hotel.P("security/lost-found"),
            new CreateLostItemRequest("Silver watch", "Jewellery", "Room 101", hotel.Rooms[0].Id, null, "Safe A", true)), HttpStatusCode.Created);
        Assert.StartsWith("LF-", watch.ItemNumber);

        watch = await ApiAssert.OkAsync<LostItemDto>(await reception.PostAsJsonAsync(hotel.P($"security/lost-found/{watch.Id}/custody"),
            new CustodyRequest("move", "Safe B", null, null, null, watch.ConcurrencyStamp)));
        Assert.Equal("Safe B", watch.StorageLocation);

        // Returning or disposing of property needs a security manager, and disposal waits for the hold period.
        await ApiAssert.ErrorAsync(await reception.PostAsJsonAsync(hotel.P($"security/lost-found/{watch.Id}/custody"),
            new CustodyRequest("return", null, "Ada Okafor", "Passport A123", null, watch.ConcurrencyStamp)), HttpStatusCode.Forbidden);
        await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P($"security/lost-found/{watch.Id}/custody"),
            new CustodyRequest("donate", null, null, null, "Unclaimed", watch.ConcurrencyStamp)), HttpStatusCode.Conflict, "HOLD_PERIOD");

        watch = await ApiAssert.OkAsync<LostItemDto>(await hotel.Admin.PostAsJsonAsync(hotel.P($"security/lost-found/{watch.Id}/custody"),
            new CustodyRequest("return", null, "Ada Okafor", "Passport A123", null, watch.ConcurrencyStamp)));
        Assert.Equal("Returned", watch.Status);
        Assert.Equal(["Found", "Moved", "Returned"], watch.Events.Select(e => e.Type).ToArray());
        await ApiAssert.ErrorAsync(await hotel.Admin.PostAsJsonAsync(hotel.P($"security/lost-found/{watch.Id}/custody"),
            new CustodyRequest("move", "Safe C", null, null, null, watch.ConcurrencyStamp)), HttpStatusCode.Conflict, "ITEM_RELEASED");
    }

    [Fact]
    public async Task Complaints_and_visitors_are_recorded_and_feed_the_daily_figures()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        var reception = await hotel.StaffAsync(Receptionist);
        var complaint = await ApiAssert.OkAsync<ComplaintDto>(await reception.PostAsJsonAsync(hotel.P("complaints"),
            new CreateComplaintRequest(null, null, "Noise", "Loud music from the next room.", "Medium")), HttpStatusCode.Created);
        Assert.StartsWith("CMP-", complaint.ComplaintNumber);
        complaint = await ApiAssert.OkAsync<ComplaintDto>(await reception.PostAsJsonAsync(hotel.P($"complaints/{complaint.Id}/actions"),
            new CaseUpdateRequest("resolve", null, "Spoke to the neighbouring guest; music stopped.", complaint.ConcurrencyStamp)));
        Assert.Equal("Resolved", complaint.Status);

        var visitor = await ApiAssert.OkAsync<VisitorDto>(await reception.PostAsJsonAsync(hotel.P("security/visitors"),
            new CreateVisitorRequest("Tunde Bello", "+2348000000001", "Driver's licence", null, hotel.Rooms[0].Id, "Delivery")), HttpStatusCode.Created);
        visitor = await ApiAssert.OkAsync<VisitorDto>(await reception.PostAsync(hotel.P($"security/visitors/{visitor.Id}/sign-out"), null));
        Assert.NotNull(visitor.LeftAtUtc);
        await ApiAssert.ErrorAsync(await reception.PostAsync(hotel.P($"security/visitors/{visitor.Id}/sign-out"), null), HttpStatusCode.Conflict, "VISITOR_LEFT");

        await hotel.Admin.PostAsJsonAsync(hotel.P("analytics/refresh"), new RefreshMetricsRequest(hotel.Today, hotel.Today));
        var dashboard = await hotel.GetAsync<DashboardDto>($"analytics/dashboard?from={hotel.Today:yyyy-MM-dd}&to={hotel.Today:yyyy-MM-dd}");
        Assert.Equal(1, dashboard.Days.Single().ComplaintsOpened);
        Assert.Equal(1, dashboard.Days.Single().ComplaintsResolved);
        Assert.Equal(1m, dashboard.Totals.ComplaintResolutionRate);
    }

    // ---------- audit export and isolation ----------

    [Fact]
    public async Task Audit_export_is_csv_and_itself_audited()
    {
        var hotel = await HotelSetup.CreateAsync(factory);
        await ApiAssert.OkAsync<IncidentDto>(await hotel.Admin.PostAsJsonAsync(hotel.P("security/incidents"),
            new CreateIncidentRequest("Other", "Low", "Lift alarm", "Alarm tested", null, null, null, null)), HttpStatusCode.Created);

        var export = await hotel.Admin.GetAsync($"/api/v1/branches/{hotel.Branch.Id}/audit-logs/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.StartsWith("OccurredAtUtc,", csv.TrimStart('﻿'));
        Assert.Contains("SecurityIncident", csv);

        var audit = await ApiAssert.OkAsync<PagedResult<AuditLogDto>>(await hotel.Admin.GetAsync(
            $"/api/v1/branches/{hotel.Branch.Id}/audit-logs?action=Exported"));
        Assert.NotEmpty(audit.Items);

        var reception = await hotel.StaffAsync(Receptionist);
        await ApiAssert.ErrorAsync(await reception.GetAsync($"/api/v1/branches/{hotel.Branch.Id}/audit-logs/export"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_of_one_branch_cannot_reach_another_branch()
    {
        var first = await HotelSetup.CreateAsync(factory);
        var second = await HotelSetup.CreateAsync(factory);
        var incident = await ApiAssert.OkAsync<IncidentDto>(await first.Admin.PostAsJsonAsync(first.P("security/incidents"),
            new CreateIncidentRequest("Damage", "Low", "Broken lamp", "Lamp broken in room 101.", null, null, null, null)), HttpStatusCode.Created);
        var document = await ApiAssert.OkAsync<DocumentDto>(await UploadAsync(first, first.Admin, "lamp.pdf", Pdf("lamp"), "Incident"), HttpStatusCode.Created);

        var otherManager = await second.StaffAsync(Roles.HotelManager);
        await ApiAssert.ErrorAsync(await otherManager.GetAsync(first.P($"security/incidents/{incident.Id}")), HttpStatusCode.Forbidden);
        await ApiAssert.ErrorAsync(await otherManager.GetAsync(first.P($"documents/{document.Id}")), HttpStatusCode.Forbidden);
        await ApiAssert.ErrorAsync(await otherManager.PostAsJsonAsync(first.P("business-day/close"), new CloseBusinessDayRequest(first.Today)),
            HttpStatusCode.Forbidden);
        await ApiAssert.ErrorAsync(await otherManager.GetAsync(first.P("notifications/outbox")), HttpStatusCode.Forbidden);

        // Asking for the other branch's record through your own branch looks like it does not exist.
        await ApiAssert.ErrorAsync(await otherManager.GetAsync(second.P($"security/incidents/{incident.Id}")), HttpStatusCode.NotFound);
        await ApiAssert.ErrorAsync(await otherManager.GetAsync(second.P($"documents/{document.Id}")), HttpStatusCode.NotFound);
    }
}
