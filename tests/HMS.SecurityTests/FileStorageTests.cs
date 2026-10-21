using HMS.Application.Documents;
using HMS.Infrastructure.Documents;
using Microsoft.Extensions.Options;

namespace HMS.SecurityTests;

/// <summary>Storage keys cannot escape the private document folder, and quarantined files are not served as released.</summary>
public sealed class FileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hms-sec-" + Guid.NewGuid().ToString("N"));
    private readonly FileSystemDocumentStorage _storage;

    public FileStorageTests() => _storage = new FileSystemDocumentStorage(Options.Create(new DocumentOptions { StoragePath = _root }));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("0123456789abcdef0123456789abcdef/../../x")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF/0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef/extra")]
    [InlineData("")]
    public async Task Keys_that_could_point_outside_the_store_are_rejected(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.WriteQuarantineAsync(key, new MemoryStream([1]), default));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.OpenReleasedAsync(key, default));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.DeleteAsync(key, default));
    }

    [Fact]
    public async Task A_quarantined_file_is_not_readable_as_released_until_released()
    {
        var key = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}";
        await _storage.WriteQuarantineAsync(key, new MemoryStream("%PDF-1.7"u8.ToArray()), default);
        Assert.Null(await _storage.OpenReleasedAsync(key, default));
        await _storage.ReleaseAsync(key, default);
        await using var released = await _storage.OpenReleasedAsync(key, default);
        Assert.NotNull(released);
        Assert.Null(await _storage.OpenQuarantineAsync(key, default));
    }

    [Fact]
    public async Task An_existing_key_is_never_overwritten()
    {
        var key = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}";
        await _storage.WriteQuarantineAsync(key, new MemoryStream([1]), default);
        await Assert.ThrowsAsync<IOException>(() => _storage.WriteQuarantineAsync(key, new MemoryStream([2]), default));
    }

    [Theory]
    [InlineData("invoice.pdf.exe")]
    [InlineData("photo.svg")]
    [InlineData("page.html")]
    [InlineData("macro.docm")]
    [InlineData("noextension")]
    public void Risky_file_types_are_refused(string name) => Assert.NotNull(FileRules.Check(name, null, "%PDF-1.7"u8).Error);

    [Fact]
    public void An_executable_renamed_to_pdf_is_refused() =>
        Assert.NotNull(FileRules.Check("report.pdf", "application/pdf", "MZ\u0090\0"u8).Error);

    [Theory]
    [InlineData("../../secret.pdf", "secret.pdf")]
    [InlineData("C:\\Users\\me\\report.pdf", "report.pdf")]
    public void Display_names_lose_their_folders(string input, string expected) => Assert.Equal(expected, FileRules.SafeName(input));
}
