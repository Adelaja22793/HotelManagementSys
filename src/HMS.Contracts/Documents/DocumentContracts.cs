namespace HMS.Contracts.Documents;

/// <summary>
/// Category: GuestIdentification, Contract, SupplierInvoice, Incident, Employee, Maintenance, Policy, Other.
/// Classification: Internal, Confidential, Restricted. Status: Pending, Active, Rejected, Deleted.
/// </summary>
public sealed record DocumentDto(Guid Id, string Title, string Category, string Classification, string? EntityType, Guid? EntityId,
    string Status, bool RequiresApproval, Guid? CurrentVersionId, int LatestVersionNumber, DateOnly? RetentionUntil, bool LegalHold,
    DateTime CreatedAtUtc, IReadOnlyList<DocumentVersionDto> Versions, Guid ConcurrencyStamp);

/// <summary>ScanStatus: Pending, Clean, Infected, Failed. Approval: null (not required), Pending, Approved, Rejected.</summary>
public sealed record DocumentVersionDto(Guid Id, int VersionNumber, string OriginalFileName, string ContentType, long SizeBytes, string Sha256,
    string ScanStatus, string? ScanDetail, string? Approval, string? ApprovedByUserId, string UploadedByUserId, DateTime UploadedAtUtc, bool IsDownloadable);

public sealed record DocumentApprovalRequest(bool Approved, string? Note);

public sealed record RetentionRequest(DateOnly? RetentionUntil, bool LegalHold, Guid ConcurrencyStamp);

public sealed record SignDocumentRequest(string Purpose);

public sealed record SignatureDto(Guid Id, Guid VersionId, int VersionNumber, string SignerUserId, string SignerName, string Purpose,
    string ContentSha256, DateTime SignedAtUtc, bool IsValid);

public sealed record ScanRunResultDto(int Scanned, int Clean, int Infected, int Failed);
