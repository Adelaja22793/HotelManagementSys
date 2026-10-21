using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>A security incident. Its history is a list of append-only actions; nothing is rewritten.</summary>
public class SecurityIncident : BranchEntity, IConcurrencyTracked
{
    private SecurityIncident() { }

    public SecurityIncident(Guid companyId, Guid branchId, string number, IncidentType type, IncidentSeverity severity, string title,
        string description, string? location, Guid? roomId, Guid? guestId, DateTime occurredAtUtc, string userId, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        IncidentNumber = number;
        Type = type;
        Severity = severity;
        Title = Guard.Required(title, "TITLE_REQUIRED", "A title is required", 200);
        Description = Guard.Required(description, "DESCRIPTION_REQUIRED", "Describe what happened", 4000);
        Location = location;
        RoomId = roomId;
        GuestId = guestId;
        OccurredAtUtc = occurredAtUtc;
        ReportedByUserId = userId;
        ReportedAtUtc = utcNow;
        Actions.Add(new IncidentAction(Id, "Reported", null, userId, utcNow));
    }

    public string IncidentNumber { get; private set; } = string.Empty;
    public IncidentType Type { get; private set; }
    public IncidentSeverity Severity { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public Guid? RoomId { get; private set; }
    public Guid? GuestId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string ReportedByUserId { get; private set; } = string.Empty;
    public DateTime ReportedAtUtc { get; private set; }
    public CaseStatus Status { get; private set; } = CaseStatus.Open;
    public string? OwnerUserId { get; private set; }
    public string? Resolution { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public List<IncidentAction> Actions { get; private set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Assign(string ownerUserId, string userId, DateTime utcNow)
    {
        EnsureNotClosed();
        OwnerUserId = Guard.Required(ownerUserId, "USER_REQUIRED", "Choose who owns the follow-up", 450);
        if (Status == CaseStatus.Open) Status = CaseStatus.InProgress;
        Actions.Add(new IncidentAction(Id, "Assigned", $"Owner {OwnerUserId}", userId, utcNow));
    }

    public void AddNote(string note, string userId, DateTime utcNow)
    {
        EnsureNotClosed();
        Actions.Add(new IncidentAction(Id, "Note", Guard.Required(note, "NOTE_REQUIRED", "A note is required", 2000), userId, utcNow));
    }

    public void Resolve(string resolution, string userId, DateTime utcNow)
    {
        EnsureNotClosed();
        Resolution = Guard.Required(resolution, "RESOLUTION_REQUIRED", "Describe the outcome", 2000);
        Status = CaseStatus.Resolved;
        Actions.Add(new IncidentAction(Id, "Resolved", Resolution, userId, utcNow));
    }

    public void Close(string userId, DateTime utcNow)
    {
        if (Status != CaseStatus.Resolved) throw new DomainConflictException("CASE_STATE", "Resolve the incident before closing it.");
        Status = CaseStatus.Closed;
        ClosedAtUtc = utcNow;
        Actions.Add(new IncidentAction(Id, "Closed", null, userId, utcNow));
    }

    private void EnsureNotClosed()
    {
        if (Status == CaseStatus.Closed) throw new DomainConflictException("CASE_CLOSED", "The incident is closed.");
    }
}

public class IncidentAction
{
    private IncidentAction() { }

    internal IncidentAction(Guid incidentId, string action, string? note, string userId, DateTime utcNow)
    {
        IncidentId = incidentId;
        Action = action;
        Note = note;
        UserId = userId;
        AtUtc = utcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid IncidentId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateTime AtUtc { get; private set; }
}

/// <summary>Found property. Every hand-over is a custody event, so the chain of custody is never lost.</summary>
public class LostFoundItem : BranchEntity, IConcurrencyTracked
{
    private LostFoundItem() { }

    public LostFoundItem(Guid companyId, Guid branchId, string number, string description, string? category, string foundLocation,
        Guid? roomId, Guid? guestId, string storageLocation, bool isValuable, string userId, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ItemNumber = number;
        Description = Guard.Required(description, "DESCRIPTION_REQUIRED", "Describe the item", 500);
        Category = category;
        FoundLocation = Guard.Required(foundLocation, "LOCATION_REQUIRED", "Where was it found?", 200);
        RoomId = roomId;
        GuestId = guestId;
        StorageLocation = Guard.Required(storageLocation, "STORAGE_REQUIRED", "Where is it kept?", 200);
        IsValuable = isValuable;
        FoundByUserId = userId;
        FoundAtUtc = utcNow;
        Events.Add(new LostFoundCustodyEvent(Id, CustodyEventType.Found, $"Stored at {StorageLocation}", null, userId, utcNow));
    }

    public string ItemNumber { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Category { get; private set; }
    public string FoundLocation { get; private set; } = string.Empty;
    public Guid? RoomId { get; private set; }
    public Guid? GuestId { get; private set; }
    public string StorageLocation { get; private set; } = string.Empty;
    public bool IsValuable { get; private set; }
    public string FoundByUserId { get; private set; } = string.Empty;
    public DateTime FoundAtUtc { get; private set; }
    public LostFoundStatus Status { get; private set; } = LostFoundStatus.InCustody;
    public List<LostFoundCustodyEvent> Events { get; private set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Move(string storageLocation, string userId, DateTime utcNow)
    {
        EnsureInCustody();
        StorageLocation = Guard.Required(storageLocation, "STORAGE_REQUIRED", "Where is it kept?", 200);
        Events.Add(new LostFoundCustodyEvent(Id, CustodyEventType.Moved, $"Moved to {StorageLocation}", null, userId, utcNow));
    }

    /// <summary>Hand back to the owner. The receiver's name and how their identity was checked are recorded.</summary>
    public void Return(string receivedBy, string identityCheck, string userId, DateTime utcNow)
    {
        EnsureInCustody();
        var receiver = Guard.Required(receivedBy, "RECEIVER_REQUIRED", "Who collected the item?", 200);
        var check = Guard.Required(identityCheck, "IDENTITY_CHECK_REQUIRED", "Record how the owner's identity was checked", 300);
        Status = LostFoundStatus.Returned;
        Events.Add(new LostFoundCustodyEvent(Id, CustodyEventType.Returned, check, receiver, userId, utcNow));
    }

    public void Discard(bool donated, string reason, int heldDays, int minimumHoldDays, string userId, DateTime utcNow)
    {
        EnsureInCustody();
        if (heldDays < minimumHoldDays)
            throw new DomainConflictException("HOLD_PERIOD", $"Items must be held for {minimumHoldDays} days before disposal.");
        var why = Guard.Required(reason, "REASON_REQUIRED", "Give a reason", 300);
        Status = donated ? LostFoundStatus.Donated : LostFoundStatus.Disposed;
        Events.Add(new LostFoundCustodyEvent(Id, donated ? CustodyEventType.Donated : CustodyEventType.Disposed, why, null, userId, utcNow));
    }

    public void AddNote(string note, string userId, DateTime utcNow) =>
        Events.Add(new LostFoundCustodyEvent(Id, CustodyEventType.Note, Guard.Required(note, "NOTE_REQUIRED", "A note is required", 500), null, userId, utcNow));

    private void EnsureInCustody()
    {
        if (Status != LostFoundStatus.InCustody) throw new DomainConflictException("ITEM_RELEASED", "The item is no longer in custody.");
    }
}

public class LostFoundCustodyEvent
{
    private LostFoundCustodyEvent() { }

    internal LostFoundCustodyEvent(Guid itemId, CustodyEventType type, string? note, string? counterparty, string userId, DateTime utcNow)
    {
        ItemId = itemId;
        Type = type;
        Note = note;
        Counterparty = counterparty;
        UserId = userId;
        AtUtc = utcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ItemId { get; private set; }
    public CustodyEventType Type { get; private set; }
    public string? Note { get; private set; }
    public string? Counterparty { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateTime AtUtc { get; private set; }
}

/// <summary>A visitor to the property. ID numbers are not stored; only that an ID was checked and its type.</summary>
public class VisitorLog : BranchEntity
{
    public string VisitorName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? IdTypeChecked { get; set; }
    public Guid? HostGuestId { get; set; }
    public Guid? RoomId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public DateTime ArrivedAtUtc { get; set; }
    public DateTime? LeftAtUtc { get; set; }
    public string RecordedByUserId { get; set; } = string.Empty;
}

public class GuestComplaint : BranchEntity, IConcurrencyTracked
{
    private GuestComplaint() { }

    public GuestComplaint(Guid companyId, Guid branchId, string number, Guid? guestId, Guid? stayId, string category, string description,
        IncidentSeverity severity, string userId, DateTime utcNow, DateOnly businessDate)
    {
        CompanyId = companyId;
        BranchId = branchId;
        ComplaintNumber = number;
        GuestId = guestId;
        StayId = stayId;
        Category = Guard.Required(category, "CATEGORY_REQUIRED", "A category is required", 100);
        Description = Guard.Required(description, "DESCRIPTION_REQUIRED", "Describe the complaint", 4000);
        Severity = severity;
        ReportedByUserId = userId;
        ReportedAtUtc = utcNow;
        BusinessDate = businessDate;
    }

    public string ComplaintNumber { get; private set; } = string.Empty;
    public Guid? GuestId { get; private set; }
    public Guid? StayId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public IncidentSeverity Severity { get; private set; }
    public CaseStatus Status { get; private set; } = CaseStatus.Open;
    public string ReportedByUserId { get; private set; } = string.Empty;
    public DateTime ReportedAtUtc { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string? OwnerUserId { get; private set; }
    public string? Resolution { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public DateOnly? ResolvedBusinessDate { get; private set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Assign(string ownerUserId)
    {
        if (Status is CaseStatus.Resolved or CaseStatus.Closed) throw new DomainConflictException("CASE_STATE", "The complaint is already resolved.");
        OwnerUserId = Guard.Required(ownerUserId, "USER_REQUIRED", "Choose who owns the complaint", 450);
        Status = CaseStatus.InProgress;
    }

    public void Resolve(string resolution, DateTime utcNow, DateOnly businessDate)
    {
        if (Status is CaseStatus.Resolved or CaseStatus.Closed) throw new DomainConflictException("CASE_STATE", "The complaint is already resolved.");
        Resolution = Guard.Required(resolution, "RESOLUTION_REQUIRED", "Describe how it was resolved", 2000);
        Status = CaseStatus.Resolved;
        ResolvedAtUtc = utcNow;
        ResolvedBusinessDate = businessDate;
    }

    public void Close()
    {
        if (Status != CaseStatus.Resolved) throw new DomainConflictException("CASE_STATE", "Resolve the complaint before closing it.");
        Status = CaseStatus.Closed;
    }
}

/// <summary>
/// A log of an emergency and the actions taken. Recording is never a prerequisite for responding: nothing in the
/// software blocks or waits on these records.
/// </summary>
public class EmergencyEvent : BranchEntity
{
    private EmergencyEvent() { }

    public EmergencyEvent(Guid companyId, Guid branchId, EmergencyType type, string description, string userId, DateTime utcNow)
    {
        CompanyId = companyId;
        BranchId = branchId;
        Type = type;
        Description = Guard.Required(description, "DESCRIPTION_REQUIRED", "Describe the emergency", 2000);
        StartedAtUtc = utcNow;
        StartedByUserId = userId;
        Actions.Add(new EmergencyAction(Id, "Declared", userId, utcNow));
    }

    public EmergencyType Type { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public EmergencyStatus Status { get; private set; } = EmergencyStatus.Active;
    public DateTime StartedAtUtc { get; private set; }
    public string StartedByUserId { get; private set; } = string.Empty;
    public DateTime? EndedAtUtc { get; private set; }
    public string? Summary { get; private set; }
    public List<EmergencyAction> Actions { get; private set; } = [];

    // Actions can be logged after the event ends: notes are often written up afterwards.
    public void Log(string action, string userId, DateTime utcNow) =>
        Actions.Add(new EmergencyAction(Id, Guard.Required(action, "ACTION_REQUIRED", "Describe the action", 1000), userId, utcNow));

    public void End(string summary, string userId, DateTime utcNow)
    {
        if (Status == EmergencyStatus.Ended) throw new DomainConflictException("EMERGENCY_ENDED", "The emergency has already ended.");
        Summary = Guard.Required(summary, "SUMMARY_REQUIRED", "Summarise the outcome", 2000);
        Status = EmergencyStatus.Ended;
        EndedAtUtc = utcNow;
        Actions.Add(new EmergencyAction(Id, "Ended", userId, utcNow));
    }
}

public class EmergencyAction
{
    private EmergencyAction() { }

    internal EmergencyAction(Guid emergencyId, string action, string userId, DateTime utcNow)
    {
        EmergencyId = emergencyId;
        Action = action;
        UserId = userId;
        AtUtc = utcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EmergencyId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public DateTime AtUtc { get; private set; }
}
