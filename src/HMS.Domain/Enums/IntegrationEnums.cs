namespace HMS.Domain.Enums;

// ---------- business day ----------

public enum BusinessDayStatus
{
    /// <summary>Checks ran and at least one blocking exception is still open.</summary>
    Blocked = 0,
    Closed = 1,
}

public enum ExceptionSeverity
{
    Warning = 0,
    Blocking = 1,
}

public enum BusinessDayExceptionStatus
{
    Open = 0,
    /// <summary>The problem was fixed; the check passed on a later run.</summary>
    Resolved = 1,
    /// <summary>A manager accepted the exception with a reason so the day can close.</summary>
    Accepted = 2,
}

// ---------- notifications ----------

public enum NotificationChannel
{
    Email = 0,
    Sms = 1,
    InApp = 2,
}

public enum OutboxStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Failed = 3,
    Cancelled = 4,
}

public enum DeliveryStatus
{
    Submitted = 0,
    Delivered = 1,
    Failed = 2,
}

// ---------- documents ----------

public enum DocumentCategory
{
    GuestIdentification = 0,
    Contract = 1,
    SupplierInvoice = 2,
    Incident = 3,
    Employee = 4,
    Maintenance = 5,
    Policy = 6,
    Other = 7,
}

public enum DocumentClassification
{
    Internal = 0,
    Confidential = 1,
    Restricted = 2,
}

public enum DocumentStatus
{
    /// <summary>Waiting for the malware scan (or approval) of its first version.</summary>
    Pending = 0,
    Active = 1,
    Rejected = 2,
    Deleted = 3,
}

public enum ScanStatus
{
    Pending = 0,
    Clean = 1,
    Infected = 2,
    /// <summary>The scan could not run (no scanner, scanner error). The file stays quarantined.</summary>
    Failed = 3,
}

// ---------- security, lost and found, complaints ----------

public enum IncidentType
{
    Theft = 0,
    Damage = 1,
    Injury = 2,
    Disturbance = 3,
    Fire = 4,
    Medical = 5,
    Trespass = 6,
    Other = 7,
}

public enum IncidentSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

public enum CaseStatus
{
    Open = 0,
    InProgress = 1,
    Resolved = 2,
    Closed = 3,
}

public enum LostFoundStatus
{
    InCustody = 0,
    Returned = 1,
    Disposed = 2,
    Donated = 3,
}

public enum CustodyEventType
{
    Found = 0,
    Moved = 1,
    Returned = 2,
    Disposed = 3,
    Donated = 4,
    Note = 5,
}

public enum EmergencyType
{
    Fire = 0,
    Medical = 1,
    Security = 2,
    Evacuation = 3,
    PowerOutage = 4,
    Flood = 5,
    Other = 6,
}

public enum EmergencyStatus
{
    Active = 0,
    Ended = 1,
}
