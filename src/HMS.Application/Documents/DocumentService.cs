using System.Security.Cryptography;
using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Application.Security;
using HMS.Contracts.Documents;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HMS.Application.Documents;

public interface IDocumentService
{
    Task<IReadOnlyList<DocumentDto>> ListAsync(Guid branchId, string? entityType, Guid? entityId, CancellationToken ct = default);
    Task<DocumentDto> GetAsync(Guid branchId, Guid documentId, CancellationToken ct = default);
    Task<DocumentDto> UploadAsync(Guid branchId, string title, string category, string? classification, string? entityType, Guid? entityId,
        DateOnly? retentionUntil, DocumentUpload upload, CancellationToken ct = default);
    Task<DocumentDto> AddVersionAsync(Guid branchId, Guid documentId, DocumentUpload upload, CancellationToken ct = default);
    Task<DocumentDownload> DownloadAsync(Guid branchId, Guid documentId, Guid? versionId, CancellationToken ct = default);
    Task<DocumentDto> DecideAsync(Guid branchId, Guid documentId, Guid versionId, DocumentApprovalRequest request, CancellationToken ct = default);
    Task<DocumentDto> RescanAsync(Guid branchId, Guid documentId, Guid versionId, CancellationToken ct = default);
    Task<SignatureDto> SignAsync(Guid branchId, Guid documentId, Guid versionId, SignDocumentRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SignatureDto>> SignaturesAsync(Guid branchId, Guid documentId, CancellationToken ct = default);
    Task<DocumentDto> SetRetentionAsync(Guid branchId, Guid documentId, RetentionRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid branchId, Guid documentId, CancellationToken ct = default);
}

public interface IDocumentScanProcessor
{
    /// <summary>Scans quarantined versions (claimed with a lease) and releases clean ones.</summary>
    Task<ScanRunResultDto> ProcessPendingAsync(int maxFiles, CancellationToken ct);
}

internal sealed class DocumentService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    IDocumentStorage storage,
    IDocumentSigner signer,
    IOptions<DocumentOptions> options) : IDocumentService
{
    // Records documents may be attached to. Each is checked to exist in the same branch.
    public static readonly IReadOnlySet<string> EntityTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Guest", "Reservation", "Stay", "Supplier", "SupplierInvoice", "Employee", "WorkOrder", "Asset", "SecurityIncident", "LostFoundItem", "GuestComplaint",
    };

    private static readonly HashSet<DocumentCategory> ApprovalCategories = [DocumentCategory.Contract, DocumentCategory.SupplierInvoice, DocumentCategory.Policy];

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(Guid branchId, string? entityType, Guid? entityId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.Documents.AsNoTracking().Include(d => d.Versions).Where(d => d.BranchId == branchId && d.Status != DocumentStatus.Deleted);
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(d => d.EntityType == entityType);
        if (entityId is { } id) q = q.Where(d => d.EntityId == id);
        var docs = await q.OrderByDescending(d => d.CreatedAtUtc).Take(300).ToListAsync(ct);
        // Documents the caller may not read are left out entirely.
        return docs.Where(d => CanRead(d.Classification)).Select(ToDto).ToList();
    }

    public async Task<DocumentDto> GetAsync(Guid branchId, Guid documentId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return ToDto(await LoadReadableAsync(branchId, documentId, tracked: false, ct));
    }

    public async Task<DocumentDto> UploadAsync(Guid branchId, string title, string category, string? classification, string? entityType, Guid? entityId,
        DateOnly? retentionUntil, DocumentUpload upload, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var cat = EnumParser.Parse<DocumentCategory>(category, nameof(category));
        var cls = string.IsNullOrWhiteSpace(classification) ? DocumentClassification.Internal
            : EnumParser.Parse<DocumentClassification>(classification, nameof(classification));
        // Identity documents are always at least confidential.
        if (cat == DocumentCategory.GuestIdentification && cls == DocumentClassification.Internal) cls = DocumentClassification.Confidential;
        EnsureCanUpload(cat, cls);
        new Validation().Require(nameof(title), title, 200)
            .When(entityType is not null && !EntityTypes.Contains(entityType), nameof(entityType), $"Documents can be attached to: {string.Join(", ", EntityTypes)}.")
            .When((entityType is null) != (entityId is null), nameof(entityId), "Give both the record type and its id, or neither.")
            .When(retentionUntil is { } r && r < scope.BusinessDate, nameof(retentionUntil), "The retention date cannot be in the past.")
            .ThrowIfInvalid();
        if (entityType is not null) await EnsureTargetAsync(branchId, entityType, entityId!.Value, ct);

        var (bytes, contentType, sha) = await ReadAndValidateAsync(upload, ct);
        var document = new DocumentRecord(scope.CompanyId, branchId, title.Trim(), cat, cls, entityType, entityId, ApprovalCategories.Contains(cat), retentionUntil);
        var key = NewKey(branchId);
        document.AddVersion(FileRules.SafeName(upload.FileName), key, contentType, bytes.Length, sha, scope.UserId, scope.UtcNow);
        db.Documents.Add(document);

        // File first, metadata second: an orphaned quarantined file is harmless, a record without its file is not.
        await storage.WriteQuarantineAsync(key, new MemoryStream(bytes, writable: false), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }
        return ToDto(document);
    }

    public async Task<DocumentDto> AddVersionAsync(Guid branchId, Guid documentId, DocumentUpload upload, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var document = await LoadReadableAsync(branchId, documentId, tracked: true, ct);
        EnsureCanUpload(document.Category, document.Classification);
        var (bytes, contentType, sha) = await ReadAndValidateAsync(upload, ct);
        var key = NewKey(branchId);
        document.AddVersion(FileRules.SafeName(upload.FileName), key, contentType, bytes.Length, sha, scope.UserId, scope.UtcNow);
        db.Touch(document);
        await storage.WriteQuarantineAsync(key, new MemoryStream(bytes, writable: false), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await storage.DeleteAsync(key, CancellationToken.None);
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "Another version was uploaded at the same time. Reload and try again.");
        }
        return ToDto(document);
    }

    public async Task<DocumentDownload> DownloadAsync(Guid branchId, Guid documentId, Guid? versionId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var document = await LoadReadableAsync(branchId, documentId, tracked: false, ct);
        document.EnsureNotDeleted();
        var version = versionId is { } vid
            ? document.Versions.SingleOrDefault(v => v.Id == vid) ?? throw new NotFoundException("Document version", vid)
            : document.Versions.SingleOrDefault(v => v.Id == document.CurrentVersionId)
              ?? throw new ConflictException("DOCUMENT_NOT_RELEASED", "No version of this document has passed scanning and approval yet.");

        if (version.ScanStatus != ScanStatus.Clean)
            throw new ConflictException("DOCUMENT_QUARANTINED", "The file has not passed the malware scan and cannot be downloaded.");
        // Approvers may open a clean file waiting for their decision; nobody else can until it is approved.
        if (version.Approval is ApprovalStatus.Pending or ApprovalStatus.Rejected && !Has(Permissions.DocumentsApprove))
            throw new ConflictException("DOCUMENT_NOT_APPROVED", "The file is waiting for approval.");

        var stream = await storage.OpenReleasedAsync(version.StorageKey, ct)
            ?? throw new ConflictException("DOCUMENT_FILE_MISSING", "The stored file is missing. Restore it from the document backup.");
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAtUtc = scope.UtcNow, CompanyId = scope.CompanyId, BranchId = branchId, UserId = scope.UserId, Action = "Downloaded",
            EntityType = nameof(DocumentVersion), EntityId = version.Id.ToString(), TraceId = user.TraceId,
            Details = $"Document {document.Id} version {version.VersionNumber}",
        });
        await db.SaveChangesAsync(ct);
        return new DocumentDownload(stream, version.OriginalFileName, version.ContentType);
    }

    public async Task<DocumentDto> DecideAsync(Guid branchId, Guid documentId, Guid versionId, DocumentApprovalRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var document = await LoadReadableAsync(branchId, documentId, tracked: true, ct);
        document.EnsureNotDeleted();
        var version = document.Versions.SingleOrDefault(v => v.Id == versionId) ?? throw new NotFoundException("Document version", versionId);
        version.Decide(request.Approved, scope.UserId, string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), scope.UtcNow);
        document.Refresh(version);
        db.Touch(document);
        await SaveOrConflictAsync(ct);
        return ToDto(document);
    }

    public async Task<DocumentDto> RescanAsync(Guid branchId, Guid documentId, Guid versionId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var document = await LoadReadableAsync(branchId, documentId, tracked: true, ct);
        var version = document.Versions.SingleOrDefault(v => v.Id == versionId) ?? throw new NotFoundException("Document version", versionId);
        if (version.ScanStatus != ScanStatus.Failed) throw new ConflictException("SCAN_STATE", "Only versions whose scan failed can be rescanned.");
        await db.DocumentVersions.Where(v => v.Id == versionId && v.ScanStatus == ScanStatus.Failed)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ScanStatus, ScanStatus.Pending).SetProperty(v => v.ScanLeaseId, (Guid?)null), ct);
        return await GetAsync(branchId, documentId, ct);
    }

    public async Task<SignatureDto> SignAsync(Guid branchId, Guid documentId, Guid versionId, SignDocumentRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Purpose), request.Purpose, 300).ThrowIfInvalid();
        var document = await LoadReadableAsync(branchId, documentId, tracked: false, ct);
        document.EnsureNotDeleted();
        var version = document.Versions.SingleOrDefault(v => v.Id == versionId) ?? throw new NotFoundException("Document version", versionId);
        if (!version.IsReleasable) throw new ConflictException("DOCUMENT_NOT_RELEASED", "Only a scanned and approved version can be signed.");

        var signature = new DocumentSignature
        {
            DocumentId = document.Id, VersionId = version.Id, SignerUserId = scope.UserId, SignerName = user.DisplayName ?? scope.UserId,
            Purpose = request.Purpose.Trim(), ContentSha256 = version.Sha256,
            SignedAtUtc = DateTime.SpecifyKind(new DateTime(scope.UtcNow.Ticks - scope.UtcNow.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc),
        };
        signature.SignatureHash = signer.Sign(Payload(signature));
        db.DocumentSignatures.Add(signature);
        await db.SaveChangesAsync(ct);
        return ToDto(signature, version.VersionNumber, true);
    }

    public async Task<IReadOnlyList<SignatureDto>> SignaturesAsync(Guid branchId, Guid documentId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var document = await LoadReadableAsync(branchId, documentId, tracked: false, ct);
        var signatures = await db.DocumentSignatures.AsNoTracking().Where(s => s.DocumentId == documentId).OrderBy(s => s.SignedAtUtc).ToListAsync(ct);
        var fileHashes = new Dictionary<Guid, string?>();
        var result = new List<SignatureDto>();
        foreach (var s in signatures)
        {
            var version = document.Versions.Single(v => v.Id == s.VersionId);
            if (!fileHashes.TryGetValue(version.Id, out var actual))
                fileHashes[version.Id] = actual = await HashStoredAsync(version.StorageKey, ct);
            // Valid only if the record is untouched, matches the version, and the stored file still has the signed content.
            var valid = signer.Verify(Payload(s), s.SignatureHash) && s.ContentSha256 == version.Sha256 && actual == version.Sha256;
            result.Add(ToDto(s, version.VersionNumber, valid));
        }
        return result;
    }

    public async Task<DocumentDto> SetRetentionAsync(Guid branchId, Guid documentId, RetentionRequest request, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var document = await LoadReadableAsync(branchId, documentId, tracked: true, ct);
        BranchOperations.EnsureStamp(document, request.ConcurrencyStamp, "document");
        document.SetRetention(request.RetentionUntil, request.LegalHold);
        await SaveOrConflictAsync(ct);
        return ToDto(document);
    }

    public async Task DeleteAsync(Guid branchId, Guid documentId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var document = await LoadReadableAsync(branchId, documentId, tracked: true, ct);
        document.Delete(scope.UserId, scope.BusinessDate, scope.UtcNow);
        await SaveOrConflictAsync(ct);
        // Metadata stays for the audit trail; the files themselves are removed.
        foreach (var version in document.Versions) await storage.DeleteAsync(version.StorageKey, CancellationToken.None);
    }

    // ---------- helpers ----------

    private async Task<DocumentRecord> LoadReadableAsync(Guid branchId, Guid documentId, bool tracked, CancellationToken ct)
    {
        var q = db.Documents.Include(d => d.Versions).Where(d => d.Id == documentId && d.BranchId == branchId);
        var document = await (tracked ? q : q.AsNoTracking()).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Document", documentId);
        // Hide documents the caller may not read, as if they did not exist.
        if (!CanRead(document.Classification)) throw new NotFoundException("Document", documentId);
        return document;
    }

    private bool CanRead(DocumentClassification classification) => classification switch
    {
        DocumentClassification.Internal => Has(Permissions.DocumentsView),
        DocumentClassification.Confidential => Has(Permissions.DocumentsManage) || Has(Permissions.DocumentsApprove),
        _ => Has(Permissions.DocumentsApprove),
    };

    private void EnsureCanUpload(DocumentCategory category, DocumentClassification classification)
    {
        var allowed = category == DocumentCategory.GuestIdentification
            ? Has(Permissions.GuestsManage) || Has(Permissions.DocumentsManage)
            : Has(Permissions.DocumentsManage);
        if (classification == DocumentClassification.Restricted) allowed = Has(Permissions.DocumentsApprove);
        if (!allowed) throw new ForbiddenException(message: "You cannot upload this kind of document.");
    }

    private bool Has(string permission) => RolePermissions.HasPermission(user.Roles, permission);

    private async Task<(byte[] Bytes, string ContentType, string Sha256)> ReadAndValidateAsync(DocumentUpload upload, CancellationToken ct)
    {
        var max = options.Value.MaxBytes;
        if (upload.Length <= 0) throw new ValidationAppException("file", "The file is empty.");
        if (upload.Length > max) throw new ValidationAppException("file", $"Files are limited to {max / (1024 * 1024)} MB.");
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, ct);
        if (buffer.Length > max) throw new ValidationAppException("file", $"Files are limited to {max / (1024 * 1024)} MB.");
        var bytes = buffer.ToArray();
        var (contentType, error) = FileRules.Check(upload.FileName, upload.DeclaredContentType, bytes.AsSpan(0, Math.Min(16, bytes.Length)));
        if (error is not null) throw new ValidationAppException("file", error);
        return (bytes, contentType!, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private async Task EnsureTargetAsync(Guid branchId, string entityType, Guid id, CancellationToken ct)
    {
        var exists = entityType switch
        {
            "Guest" => await db.Guests.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            "Reservation" => await db.Reservations.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            "Stay" => await db.Stays.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            "Supplier" => await db.Suppliers.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            "SupplierInvoice" => await db.SupplierInvoices.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            "Employee" => await db.Employees.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            "WorkOrder" => await db.WorkOrders.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            "Asset" => await db.Assets.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            "SecurityIncident" => await db.SecurityIncidents.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            "LostFoundItem" => await db.LostFoundItems.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            "GuestComplaint" => await db.GuestComplaints.AnyAsync(x => x.Id == id && x.BranchId == branchId, ct),
            _ => false,
        };
        if (!exists) throw new NotFoundException(entityType, id);
    }

    private async Task<string?> HashStoredAsync(string key, CancellationToken ct)
    {
        await using var stream = await storage.OpenReleasedAsync(key, ct);
        return stream is null ? null : Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    private static string NewKey(Guid branchId) => $"{branchId:N}/{Guid.NewGuid():N}";

    internal static string Payload(DocumentSignature s) =>
        string.Join('|', s.Id.ToString("N"), s.DocumentId.ToString("N"), s.VersionId.ToString("N"), s.SignerUserId, s.Purpose, s.ContentSha256,
            // Fixed format: the value read back from the database has no DateTimeKind, so "O" would differ.
            s.SignedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));

    private async Task SaveOrConflictAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "The document was changed by someone else. Reload and try again.");
        }
    }

    private static DocumentDto ToDto(DocumentRecord d) => new(d.Id, d.Title, d.Category.ToString(), d.Classification.ToString(), d.EntityType,
        d.EntityId, d.Status.ToString(), d.RequiresApproval, d.CurrentVersionId, d.LatestVersionNumber, d.RetentionUntil, d.LegalHold, d.CreatedAtUtc,
        d.Versions.OrderBy(v => v.VersionNumber).Select(v => new DocumentVersionDto(v.Id, v.VersionNumber, v.OriginalFileName, v.ContentType,
            v.SizeBytes, v.Sha256, v.ScanStatus.ToString(), v.ScanDetail, v.Approval?.ToString(), v.ApprovedByUserId, v.UploadedByUserId,
            v.UploadedAtUtc, v.IsReleasable && d.Status != DocumentStatus.Deleted)).ToList(),
        d.ConcurrencyStamp);

    private static SignatureDto ToDto(DocumentSignature s, int versionNumber, bool valid) => new(s.Id, s.VersionId, versionNumber, s.SignerUserId,
        s.SignerName, s.Purpose, s.ContentSha256, s.SignedAtUtc, valid);
}

internal sealed class DocumentScanProcessor(
    IApplicationDbContext db,
    IClock clock,
    IDocumentStorage storage,
    IMalwareScanner scanner,
    IOptions<DocumentOptions> options,
    ILogger<DocumentScanProcessor> logger) : IDocumentScanProcessor
{
    public async Task<ScanRunResultDto> ProcessPendingAsync(int maxFiles, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var ids = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.ScanStatus == ScanStatus.Pending && (v.ScanLeaseExpiresAtUtc == null || v.ScanLeaseExpiresAtUtc < now))
            .OrderBy(v => v.UploadedAtUtc).Select(v => v.Id).Take(maxFiles).ToListAsync(ct);
        int scanned = 0, clean = 0, infected = 0, failed = 0;
        foreach (var id in ids)
        {
            var lease = Guid.NewGuid();
            var expires = now.AddMinutes(5);
            var won = await db.DocumentVersions
                .Where(v => v.Id == id && v.ScanStatus == ScanStatus.Pending && (v.ScanLeaseExpiresAtUtc == null || v.ScanLeaseExpiresAtUtc < now))
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.ScanLeaseId, lease).SetProperty(v => v.ScanLeaseExpiresAtUtc, expires), ct);
            if (won == 0) continue;
            scanned++;

            db.ChangeTracker.Clear();
            var version = await db.DocumentVersions.SingleAsync(v => v.Id == id, ct);
            var document = await db.Documents.Include(d => d.Versions).SingleAsync(d => d.Id == version.DocumentId, ct);
            version = document.Versions.Single(v => v.Id == id);

            ScanVerdict verdict;
            await using (var stream = await storage.OpenQuarantineAsync(version.StorageKey, ct))
            {
                if (stream is null) verdict = new ScanVerdict(ScanOutcome.Error, "The quarantined file is missing.");
                else
                {
                    try
                    {
                        verdict = await scanner.ScanAsync(stream, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "Malware scan of document version {VersionId} failed", id);
                        verdict = new ScanVerdict(ScanOutcome.Error, "The scanner could not be reached.");
                    }
                }
            }
            if (verdict.Outcome == ScanOutcome.Error && options.Value.AllowUnscanned)
                verdict = new ScanVerdict(ScanOutcome.Clean, "Not scanned (development setting AllowUnscanned).");

            var at = clock.UtcNow;
            switch (verdict.Outcome)
            {
                case ScanOutcome.Clean:
                    await storage.ReleaseAsync(version.StorageKey, ct);
                    version.RecordScan(ScanStatus.Clean, verdict.Detail, at);
                    clean++;
                    break;
                case ScanOutcome.Infected:
                    await storage.DeleteAsync(version.StorageKey, ct); // never keep malware around
                    version.RecordScan(ScanStatus.Infected, verdict.Detail, at);
                    infected++;
                    break;
                default:
                    version.RecordScan(ScanStatus.Failed, verdict.Detail, at);
                    failed++;
                    break;
            }
            document.Refresh(version);
            await db.SaveChangesAsync(ct);
        }
        return new ScanRunResultDto(scanned, clean, infected, failed);
    }
}
