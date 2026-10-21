using System.Text.RegularExpressions;
using HMS.Application.Documents;
using Microsoft.Extensions.Options;

namespace HMS.Infrastructure.Documents;

/// <summary>
/// Stores files under a private folder: <c>quarantine/</c> until scanned, then <c>released/</c>. Keys are server-generated
/// (<c>{branch}/{guid}</c>) and validated so a key can never point outside the root.
/// </summary>
internal sealed partial class FileSystemDocumentStorage(IOptions<DocumentOptions> options) : IDocumentStorage
{
    private string Root
    {
        get
        {
            var configured = options.Value.StoragePath;
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HMS", "documents")
                : Path.GetFullPath(configured);
        }
    }

    public async Task WriteQuarantineAsync(string key, Stream content, CancellationToken ct)
    {
        var path = PathFor("quarantine", key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream?> OpenQuarantineAsync(string key, CancellationToken ct) => Task.FromResult(Open(PathFor("quarantine", key)));

    public Task ReleaseAsync(string key, CancellationToken ct)
    {
        var from = PathFor("quarantine", key);
        var to = PathFor("released", key);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (File.Exists(from)) File.Move(from, to, overwrite: true);
        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReleasedAsync(string key, CancellationToken ct) => Task.FromResult(Open(PathFor("released", key)));

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        foreach (var area in new[] { "quarantine", "released" })
        {
            var path = PathFor(area, key);
            if (File.Exists(path)) File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StoredFile> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var (area, released) in new[] { ("quarantine", false), ("released", true) })
        {
            var root = Path.Combine(Root, area);
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var key = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                // Anything that is not a well-formed key is still reported, so stray files are noticed.
                yield return new StoredFile(key, released);
            }
        }
        await Task.CompletedTask;
    }

    private static Stream? Open(string path) =>
        File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;

    private string PathFor(string area, string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("Invalid storage key.", nameof(key));
        var root = Path.Combine(Root, area);
        var full = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException("Invalid storage key.", nameof(key));
        return full;
    }

    [GeneratedRegex("^[0-9a-f]{32}/[0-9a-f]{32}$")]
    private static partial Regex KeyPattern();
}
