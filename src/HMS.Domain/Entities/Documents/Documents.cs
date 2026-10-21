using HMS.Domain.Common;
using HMS.Domain.Enums;

namespace HMS.Domain.Entities;

/// <summary>
/// A private document attached to a hotel record. Files live outside the web root under generated keys; a version is
/// only downloadable once its malware scan is clean and any required approval is given.
/// </summary>
public class DocumentRecord : BranchEntity, IConcurrencyTracked
{
    private DocumentRecord() { }

    public DocumentRecord(Guid companyId, Guid branchId, string title, DocumentCategory category, DocumentClassification classification,
        string? entityType, Guid? entityId, bool requiresApproval, DateOnly? retentionUntil)
    {
        CompanyId = companyId;
        BranchId = branchId;
        Title = Guard.Required(title, "TITLE_REQUIRED", "A title is required", 200);
        Category = category;
        Classification = classification;
        EntityType = entityType;
        EntityId = entityId;
        RequiresApproval = requiresApproval;
        RetentionUntil = retentionUntil;
    }

    public string Title { get; private set; } = string.Empty;
    public DocumentCategory Category { get; private set; }
    public DocumentClassification Classification { get; private set; }
    public string? EntityType { get; private set; }
    public Guid? EntityId { get; private set; }
    public bool RequiresApproval { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Pending;
    /// <summary>The newest version that passed scanning and approval; what downloads return by default.</summary>
    public Guid? CurrentVersionId { get; private set; }
    public int LatestVersionNumber { get; private set; }
    public DateOnly? RetentionUntil { get; private set; }
    public bool LegalHold { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public string? DeletedByUserId { get; private set; }
    public List<DocumentVersion> Versions { get; private set; } = [];
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public DocumentVersion AddVersion(string originalFileName, string storageKey, string contentType, long sizeBytes, string sha256,
        string userId, DateTime utcNow)
    {
        EnsureNotDeleted();
        LatestVersionNumber++;
        var version = new DocumentVersion(Id, LatestVersionNumber, originalFileName, storageKey, contentType, sizeBytes, sha256,
            RequiresApproval ? ApprovalStatus.Pending : null, userId, utcNow);
        Versions.Add(version);
        return version;
    }

    /// <summary>Called when a version finishes scanning or approval; releases it if both passed.</summary>
    public void Refresh(DocumentVersion version)
    {
        if (version.IsReleasable && (CurrentVersionId is null || version.VersionNumber > CurrentVersionNumberOf(CurrentVersionId.Value)))
        {
            CurrentVersionId = version.Id;
            if (Status == DocumentStatus.Pending) Status = DocumentStatus.Active;
        }
        else if (CurrentVersionId is null && Versions.All(v => v.IsFinallyBlocked))
        {
            Status = DocumentStatus.Rejected;
        }
    }

    private int CurrentVersionNumberOf(Guid id) => Versions.SingleOrDefault(v => v.Id == id)?.VersionNumber ?? 0;

    public void SetRetention(DateOnly? retentionUntil, bool legalHold)
    {
        EnsureNotDeleted();
        RetentionUntil = retentionUntil;
        LegalHold = legalHold;
    }

    public void Delete(string userId, DateOnly today, DateTime utcNow)
    {
        EnsureNotDeleted();
        if (LegalHold) throw new DomainConflictException("LEGAL_HOLD", "The document is under legal hold and cannot be deleted.");
        if (RetentionUntil is { } until && today <= until)
            throw new DomainConflictException("RETENTION_ACTIVE", $"The document must be kept until {until:yyyy-MM-dd}.");
        Status = DocumentStatus.Deleted;
        DeletedAtUtc = utcNow;
        DeletedByUserId = userId;
    }

    public void EnsureNotDeleted()
    {
        if (Status == DocumentStatus.Deleted) throw new DomainConflictException("DOCUMENT_DELETED", "The document has been deleted.");
    }
}

public class DocumentVersion : Entity
{
    private DocumentVersion() { }

    internal DocumentVersion(Guid documentId, int versionNumber, string originalFileName, string storageKey, string contentType, long sizeBytes,
        string sha256, ApprovalStatus? approval, string userId, DateTime utcNow)
    {
        DocumentId = documentId;
        VersionNumber = versionNumber;
        OriginalFileName = originalFileName;
        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        Approval = approval;
        UploadedByUserId = userId;
        UploadedAtUtc = utcNow;
    }

    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public ScanStatus ScanStatus { get; private set; } = ScanStatus.Pending;
    public string? ScanDetail { get; private set; }
    public DateTime? ScannedAtUtc { get; private set; }
    public Guid? ScanLeaseId { get; private set; }
    public DateTime? ScanLeaseExpiresAtUtc { get; private set; }
    /// <summary>Null when the document does not need approval.</summary>
    public ApprovalStatus? Approval { get; private set; }
    public string? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? ApprovalNote { get; private set; }
    public string UploadedByUserId { get; private set; } = string.Empty;
    public DateTime UploadedAtUtc { get; private set; }

    public bool IsReleasable => ScanStatus == ScanStatus.Clean && Approval is null or ApprovalStatus.Approved;
    public bool IsFinallyBlocked => ScanStatus == ScanStatus.Infected || Approval == ApprovalStatus.Rejected;

    public void RecordScan(ScanStatus result, string? detail, DateTime utcNow)
    {
        if (ScanStatus is ScanStatus.Clean or ScanStatus.Infected)
            throw new DomainConflictException("SCAN_DONE", "This version has already been scanned.");
        ScanStatus = result;
        ScanDetail = detail is { Length: > 300 } ? detail[..300] : detail;
        ScannedAtUtc = utcNow;
        ScanLeaseId = null;
        ScanLeaseExpiresAtUtc = null;
    }

    public void Decide(bool approved, string userId, string? note, DateTime utcNow)
    {
        if (Approval != ApprovalStatus.Pending) throw new DomainConflictException("APPROVAL_STATE", "This version is not waiting for approval.");
        if (ScanStatus != ScanStatus.Clean) throw new DomainConflictException("SCAN_PENDING", "Only a clean, scanned file can be approved.");
        if (userId == UploadedByUserId) throw new DomainConflictException("SELF_APPROVAL", "Someone other than the uploader must approve the document.");
        Approval = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        ApprovedByUserId = userId;
        ApprovedAtUtc = utcNow;
        ApprovalNote = note;
    }
}

/// <summary>
/// A tamper-evident electronic signature: who signed which exact file content (SHA-256), when and why.
/// SignatureHash is an HMAC over those values with a server-held key, so a changed row no longer verifies.
/// </summary>
public class DocumentSignature
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Guid VersionId { get; set; }
    public string SignerUserId { get; set; } = string.Empty;
    public string SignerName { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string ContentSha256 { get; set; } = string.Empty;
    public DateTime SignedAtUtc { get; set; }
    public string SignatureHash { get; set; } = string.Empty;
}
