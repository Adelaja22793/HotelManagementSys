using System.IO.Compression;
using System.Text.Json;

namespace HMS.Updater;

/// <summary>
/// Side-by-side installs: <c>versions/{version}/</c> per release and a <c>current.json</c> pointer naming the active
/// and previous release. A new version is extracted and checked in full before the pointer moves, so a failed update
/// never leaves a half-installed app, and rollback is a pointer change. The install root should be writable only by
/// the updater's account or administrators.
/// </summary>
public sealed class InstallLayout(string root, string entryPoint = "HMS.Desktop.exe")
{
    public const int MaxEntries = 20_000;
    public const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;

    private sealed record Pointer(string Current, string? Previous);

    public string Root { get; } = Path.GetFullPath(root);
    private string VersionsFolder => Path.Combine(Root, "versions");
    private string PointerFile => Path.Combine(Root, "current.json");

    public string VersionFolder(string version)
    {
        if (!System.Version.TryParse(version, out var parsed) || parsed.ToString() != version)
            throw new UpdateRejectedException("Version folder name is not a plain version number.");
        return Path.Combine(VersionsFolder, version);
    }

    public (string? Current, string? Previous) Read()
    {
        if (!File.Exists(PointerFile)) return (null, null);
        try
        {
            var pointer = JsonSerializer.Deserialize<Pointer>(File.ReadAllBytes(PointerFile));
            return (pointer?.Current, pointer?.Previous);
        }
        catch (JsonException) { return (null, null); }
    }

    /// <summary>Extracts a verified package into its version folder. The pointer does not move.</summary>
    public async Task StageAsync(Stream zip, string version, CancellationToken ct)
    {
        var final = VersionFolder(version);
        var partial = final + ".partial";
        if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true);
        if (Directory.Exists(final))
        {
            if (Read().Current == version) throw new UpdateRejectedException("That version is already active.");
            Directory.Delete(final, recursive: true); // left by an earlier attempt that never activated
        }
        Directory.CreateDirectory(partial);
        try
        {
            await ExtractSafelyAsync(zip, partial, ct);
            if (!File.Exists(Path.Combine(partial, entryPoint))) throw new UpdateRejectedException($"Package has no {entryPoint}.");
            Directory.Move(partial, final);
        }
        catch
        {
            if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true);
            throw;
        }
    }

    /// <summary>Makes a staged version current and keeps the old one as the rollback target.</summary>
    public void Activate(string version)
    {
        if (!File.Exists(Path.Combine(VersionFolder(version), entryPoint))) throw new UpdateRejectedException("That version is not staged.");
        var (current, _) = Read();
        Write(new Pointer(version, current == version ? null : current));
    }

    /// <summary>Switches back to the previous version. Returns false when there is nothing to go back to.</summary>
    public bool Rollback()
    {
        var (current, previous) = Read();
        if (previous is null || !File.Exists(Path.Combine(VersionFolder(previous), entryPoint))) return false;
        Write(new Pointer(previous, current));
        return true;
    }

    /// <summary>The program to start: the current version, or the previous one if the current is damaged.</summary>
    public string? LaunchTarget()
    {
        var (current, previous) = Read();
        foreach (var version in new[] { current, previous })
        {
            if (version is null) continue;
            var path = Path.Combine(VersionFolder(version), entryPoint);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>Deletes versions other than the current and previous ones.</summary>
    public void Prune()
    {
        if (!Directory.Exists(VersionsFolder)) return;
        var (current, previous) = Read();
        foreach (var folder in Directory.EnumerateDirectories(VersionsFolder))
        {
            var name = Path.GetFileName(folder);
            if (name == current || name == previous) continue;
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { /* in use; try again next time */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Write(Pointer pointer)
    {
        Directory.CreateDirectory(Root);
        var temp = PointerFile + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(pointer));
        File.Move(temp, PointerFile, overwrite: true); // atomic replace on the same volume
    }

    private static async Task ExtractSafelyAsync(Stream zip, string target, CancellationToken ct)
    {
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxEntries) throw new UpdateRejectedException("Package has too many files.");
        long written = 0;
        var buffer = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.Length == 0 || name.StartsWith('/') || name.Contains(':') || name.Split('/').Contains(".."))
                throw new UpdateRejectedException($"Package entry '{entry.FullName}' has an unsafe path.");
            // Unix mode bits live in the high 16 bits; 0xA000 is a symbolic link.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new UpdateRejectedException($"Package entry '{entry.FullName}' is a link.");

            var destination = Path.GetFullPath(Path.Combine(target, name));
            if (!destination.StartsWith(root, StringComparison.Ordinal)) throw new UpdateRejectedException($"Package entry '{entry.FullName}' escapes the folder.");
            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                written += read;
                // Counts real bytes, not the sizes the archive claims, so a zip bomb stops early.
                if (written > MaxExtractedBytes) throw new UpdateRejectedException("Package expands beyond the size limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
    }
}
