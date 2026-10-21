using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HMS.Contracts.Documents;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels;

/// <summary>
/// Document centre: upload files (they stay in quarantine until the malware scan passes and, for contracts, supplier
/// invoices and policies, until someone approves them), add versions, download, sign, and manage retention.
/// </summary>
public sealed partial class DocumentsPageViewModel(ApiClient api, SessionState session, IDialogService dialogs) : PageViewModel
{
    public static readonly string[] Categories = ["GuestIdentification", "Contract", "SupplierInvoice", "Incident", "Employee", "Maintenance", "Policy", "Other"];
    public static readonly string[] Classifications = ["Internal", "Confidential", "Restricted"];

    public override string Title => "Document centre";

    public ObservableCollection<DocumentDto> Documents { get; } = [];
    public ObservableCollection<SignatureDto> Signatures { get; } = [];

    [ObservableProperty] private string _newTitle = "";
    [ObservableProperty] private string _newCategory = "Other";
    [ObservableProperty] private string _newClassification = "Internal";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddVersionCommand), nameof(DownloadCommand), nameof(SaveRetentionCommand), nameof(DeleteCommand))]
    private DocumentDto? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(RejectCommand), nameof(SignCommand), nameof(RescanCommand))]
    private DocumentVersionDto? _selectedVersion;

    [ObservableProperty] private string? _decisionNote;
    [ObservableProperty] private string? _signPurpose;
    [ObservableProperty] private DateTime? _retentionUntil;
    [ObservableProperty] private bool _legalHold;

    public bool CanApprove => session.HasPermission(PermissionNames.DocumentsApprove);
    public bool CanManage => session.HasPermission(PermissionNames.DocumentsManage);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        FrontDeskPageViewModel.Replace(Documents, (await api.GetAsync<List<DocumentDto>>(api.BranchPath("documents"))).OrderByDescending(d => d.CreatedAtUtc));
        if (Selected is not null) Selected = Documents.FirstOrDefault(d => d.Id == Selected.Id);
    });

    async partial void OnSelectedChanged(DocumentDto? value)
    {
        SelectedVersion = value?.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        RetentionUntil = value?.RetentionUntil?.ToDateTime(TimeOnly.MinValue);
        LegalHold = value?.LegalHold ?? false;
        Signatures.Clear();
        if (value is null) return;
        await RunAsync(async () => FrontDeskPageViewModel.Replace(Signatures,
            await api.GetAsync<List<SignatureDto>>(api.BranchPath($"documents/{value.Id}/signatures"))));
    }

    private static Func<HttpContent> FileForm(string path, IEnumerable<(string Name, string? Value)> fields) => () =>
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(File.ReadAllBytes(path));
        file.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeFor(path));
        form.Add(file, "file", Path.GetFileName(path));
        foreach (var (name, value) in fields)
            if (!string.IsNullOrWhiteSpace(value)) form.Add(new StringContent(value), name);
        return form;
    };

    internal static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        _ => "application/octet-stream",
    };

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTitle)) { ErrorMessage = "Give the document a title."; return; }
        var path = dialogs.PickFileToOpen();
        if (path is null) return;
        DocumentDto? created = null;
        if (await RunAsync(async () => created = await api.UploadAsync<DocumentDto>(api.BranchPath("documents"),
                FileForm(path, [("title", NewTitle.Trim()), ("category", NewCategory), ("classification", NewClassification)]))))
        {
            NewTitle = "";
            await LoadAsync();
            Selected = Documents.FirstOrDefault(d => d.Id == created!.Id);
            StatusMessage = created!.RequiresApproval
                ? "Uploaded. It can be opened once the scan passes and it is approved."
                : "Uploaded. It can be opened once the scan passes.";
        }
    }

    private bool HasDocument() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task AddVersionAsync()
    {
        var document = Selected!;
        var path = dialogs.PickFileToOpen();
        if (path is null) return;
        if (await RunAsync(() => api.UploadAsync<DocumentDto>(api.BranchPath($"documents/{document.Id}/versions"), FileForm(path, []))))
        {
            await LoadAsync();
            StatusMessage = $"New version of {document.Title} uploaded.";
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task DownloadAsync()
    {
        var document = Selected!;
        var version = SelectedVersion;
        if (version is { IsDownloadable: false }) { ErrorMessage = $"Version {version.VersionNumber} is not available yet (scan: {version.ScanStatus}, approval: {version.Approval ?? "not needed"})."; return; }
        var path = dialogs.PickFileToSave(version?.OriginalFileName ?? document.Title);
        if (path is null) return;
        await RunAsync(async () =>
        {
            var (content, _) = await api.DownloadAsync(Query.Build(api.BranchPath($"documents/{document.Id}/content"), ("versionId", version?.Id)));
            await File.WriteAllBytesAsync(path, content);
        }, $"Saved to {path}.");
    }

    private bool VersionAwaitingApproval() => CanApprove && SelectedVersion is { Approval: "Pending" };

    [RelayCommand(CanExecute = nameof(VersionAwaitingApproval))]
    private Task ApproveAsync() => DecideAsync(true);

    [RelayCommand(CanExecute = nameof(VersionAwaitingApproval))]
    private Task RejectAsync()
    {
        if (string.IsNullOrWhiteSpace(DecisionNote)) { ErrorMessage = "Say why the document is rejected."; return Task.CompletedTask; }
        return DecideAsync(false);
    }

    private async Task DecideAsync(bool approved)
    {
        var document = Selected!;
        var version = SelectedVersion!;
        if (await RunAsync(() => api.PostAsync<DocumentDto>(api.BranchPath($"documents/{document.Id}/versions/{version.Id}/decision"),
                new DocumentApprovalRequest(approved, string.IsNullOrWhiteSpace(DecisionNote) ? null : DecisionNote.Trim()))))
        {
            DecisionNote = null;
            await LoadAsync();
            StatusMessage = approved ? "Approved." : "Rejected.";
        }
    }

    private bool VersionSelected() => SelectedVersion is not null;

    [RelayCommand(CanExecute = nameof(VersionSelected))]
    private async Task SignAsync()
    {
        if (string.IsNullOrWhiteSpace(SignPurpose)) { ErrorMessage = "Say what you are signing for, for example \"Approved for payment\"."; return; }
        var document = Selected!;
        var version = SelectedVersion!;
        if (await RunAsync(() => api.PostAsync<SignatureDto>(api.BranchPath($"documents/{document.Id}/versions/{version.Id}/signatures"),
                new SignDocumentRequest(SignPurpose.Trim()))))
        {
            SignPurpose = null;
            FrontDeskPageViewModel.Replace(Signatures, await api.GetAsync<List<SignatureDto>>(api.BranchPath($"documents/{document.Id}/signatures")));
            StatusMessage = $"Signed version {version.VersionNumber}.";
        }
    }

    private bool CanRescan() => CanManage && SelectedVersion is { ScanStatus: "Pending" or "Failed" };

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync()
    {
        var document = Selected!;
        var version = SelectedVersion!;
        if (await RunAsync(() => api.PostAsync<DocumentDto>(api.BranchPath($"documents/{document.Id}/versions/{version.Id}/rescan"), new { })))
        {
            await LoadAsync();
            StatusMessage = "Scan finished.";
        }
    }

    private bool CanManageSelected() => CanManage && Selected is not null;

    [RelayCommand(CanExecute = nameof(CanManageSelected))]
    private async Task SaveRetentionAsync()
    {
        var document = Selected!;
        if (await RunAsync(() => api.PutAsync<DocumentDto>(api.BranchPath($"documents/{document.Id}/retention"),
                new RetentionRequest(RetentionUntil is { } d ? DateOnly.FromDateTime(d) : null, LegalHold, document.ConcurrencyStamp))))
        {
            await LoadAsync();
            StatusMessage = "Retention saved.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageSelected))]
    private async Task DeleteAsync()
    {
        var document = Selected!;
        if (!dialogs.Confirm($"Delete {document.Title}? Documents under legal hold or retention cannot be deleted.")) return;
        if (await RunAsync(() => api.DeleteAsync(api.BranchPath($"documents/{document.Id}"))))
        {
            Selected = null;
            await LoadAsync();
            StatusMessage = "Document deleted.";
        }
    }
}
