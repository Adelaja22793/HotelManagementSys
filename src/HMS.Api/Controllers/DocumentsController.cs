using HMS.Api.Authorization;
using HMS.Application.Documents;
using HMS.Application.Security;
using HMS.Contracts.Documents;
using Microsoft.AspNetCore.Mvc;

namespace HMS.Api.Controllers;

[Route(V1 + "/branches/{branchId:guid}/documents")]
public sealed class DocumentsController(IDocumentService documents) : ApiControllerBase
{
    private const long MaxRequestBytes = 11 * 1024 * 1024;

    [HttpGet]
    [HasPermission(Permissions.DocumentsView)]
    public Task<IReadOnlyList<DocumentDto>> List(Guid branchId, [FromQuery] string? entityType, [FromQuery] Guid? entityId, CancellationToken ct) =>
        documents.ListAsync(branchId, entityType, entityId, ct);

    [HttpGet("{documentId:guid}")]
    [HasPermission(Permissions.DocumentsView)]
    public Task<DocumentDto> Get(Guid branchId, Guid documentId, CancellationToken ct) => documents.GetAsync(branchId, documentId, ct);

    /// <summary>
    /// Multipart upload. The file is quarantined until the malware scan passes (and approval, for contracts, supplier
    /// invoices and policies). Fields: file, title, category, classification, entityType, entityId, retentionUntil.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.DocumentsView)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<DocumentDto>> Upload(Guid branchId, IFormFile file, [FromForm] string title, [FromForm] string category,
        [FromForm] string? classification, [FromForm] string? entityType, [FromForm] Guid? entityId, [FromForm] DateOnly? retentionUntil, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await documents.UploadAsync(branchId, title, category, classification, entityType, entityId, retentionUntil,
            new DocumentUpload(stream, file.FileName, file.ContentType, file.Length), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{documentId:guid}/versions")]
    [HasPermission(Permissions.DocumentsView)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<DocumentDto>> AddVersion(Guid branchId, Guid documentId, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await documents.AddVersionAsync(branchId, documentId, new DocumentUpload(stream, file.FileName, file.ContentType, file.Length), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Downloads the current released version, or a specific version. Every download is written to the audit trail.</summary>
    [HttpGet("{documentId:guid}/content")]
    [HasPermission(Permissions.DocumentsView)]
    [Produces("application/octet-stream")]
    public async Task<IActionResult> Download(Guid branchId, Guid documentId, [FromQuery] Guid? versionId, CancellationToken ct)
    {
        var download = await documents.DownloadAsync(branchId, documentId, versionId, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpPost("{documentId:guid}/versions/{versionId:guid}/decision")]
    [HasPermission(Permissions.DocumentsApprove)]
    public Task<DocumentDto> Decide(Guid branchId, Guid documentId, Guid versionId, DocumentApprovalRequest request, CancellationToken ct) =>
        documents.DecideAsync(branchId, documentId, versionId, request, ct);

    [HttpPost("{documentId:guid}/versions/{versionId:guid}/rescan")]
    [HasPermission(Permissions.DocumentsManage)]
    public Task<DocumentDto> Rescan(Guid branchId, Guid documentId, Guid versionId, CancellationToken ct) =>
        documents.RescanAsync(branchId, documentId, versionId, ct);

    [HttpPost("{documentId:guid}/versions/{versionId:guid}/signatures")]
    [HasPermission(Permissions.DocumentsView)]
    public Task<SignatureDto> Sign(Guid branchId, Guid documentId, Guid versionId, SignDocumentRequest request, CancellationToken ct) =>
        documents.SignAsync(branchId, documentId, versionId, request, ct);

    [HttpGet("{documentId:guid}/signatures")]
    [HasPermission(Permissions.DocumentsView)]
    public Task<IReadOnlyList<SignatureDto>> Signatures(Guid branchId, Guid documentId, CancellationToken ct) => documents.SignaturesAsync(branchId, documentId, ct);

    [HttpPut("{documentId:guid}/retention")]
    [HasPermission(Permissions.DocumentsManage)]
    public Task<DocumentDto> Retention(Guid branchId, Guid documentId, RetentionRequest request, CancellationToken ct) =>
        documents.SetRetentionAsync(branchId, documentId, request, ct);

    [HttpDelete("{documentId:guid}")]
    [HasPermission(Permissions.DocumentsManage)]
    public async Task<IActionResult> Delete(Guid branchId, Guid documentId, CancellationToken ct)
    {
        await documents.DeleteAsync(branchId, documentId, ct);
        return NoContent();
    }
}
