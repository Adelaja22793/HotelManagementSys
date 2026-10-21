using System.Diagnostics;
using System.IO;
using System.Net.Http;
using HMS.Updater;
using Microsoft.Extensions.Configuration;

namespace HMS.Desktop.Services;

/// <summary>
/// Looks for a signed update, downloads it into the install folder, verifies it and stages it beside the running
/// version. Switching happens only when the user agrees, through the launcher, so a bad download never replaces a
/// working install. Does nothing unless Updates:ManifestUrl is set and the app runs from a launcher install.
/// </summary>
public sealed class UpdateService(HttpClient http, IConfiguration configuration)
{
    private readonly UpdaterOptions _options = configuration.GetSection("Updates").Get<UpdaterOptions>() ?? new UpdaterOptions();

    /// <summary>The install root when this copy runs from <c>{root}/versions/{version}/</c>; otherwise null.</summary>
    public static string? InstallRoot()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return here.Parent is { Name: "versions", Parent: { } root } ? root.FullName : null;
    }

    public async Task<UpdateManifest?> CheckAndStageAsync(CancellationToken ct)
    {
        var manifestUrl = configuration["Updates:ManifestUrl"];
        var root = InstallRoot();
        if (string.IsNullOrWhiteSpace(manifestUrl) || root is null) return null;
        if (!manifestUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new UpdateRejectedException("Updates:ManifestUrl must use HTTPS.");

        var installed = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0);
        using var response = await http.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > _options.MaxManifestBytes) throw new UpdateRejectedException("Manifest is too large.");
        var envelope = await response.Content.ReadAsByteArrayAsync(ct);

        UpdateManifest manifest;
        try
        {
            manifest = new UpdateVerifier(_options).VerifyManifest(envelope, installed);
        }
        catch (UpdateRejectedException ex) when (ex.Message.Contains("not newer", StringComparison.Ordinal))
        {
            return null; // up to date
        }

        var layout = new InstallLayout(root);
        var downloads = Path.Combine(root, "downloads");
        Directory.CreateDirectory(downloads);
        var file = Path.Combine(downloads, $"{manifest.Version}.zip");
        try
        {
            await using (var output = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                using var package = await http.GetAsync(manifest.PackageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                package.EnsureSuccessStatusCode();
                await using var input = await package.Content.ReadAsStreamAsync(ct);
                await CopyLimitedAsync(input, output, manifest.PackageSize, ct);
                output.Position = 0;
                await UpdateVerifier.VerifyPackageAsync(output, manifest, ct);
                output.Position = 0;
                await layout.StageAsync(output, manifest.Version, ct);
            }
            return manifest;
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Points the launcher at the staged version and starts it. The caller then closes this copy.</summary>
    public static void ActivateAndRestart(UpdateManifest manifest)
    {
        var root = InstallRoot() ?? throw new InvalidOperationException("Not running from an install folder.");
        var layout = new InstallLayout(root);
        layout.Activate(manifest.Version);
        layout.Prune();
        Process.Start(new ProcessStartInfo(Path.Combine(root, "HMS.exe")) { UseShellExecute = false, WorkingDirectory = root });
    }

    private static async Task CopyLimitedAsync(Stream input, Stream output, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > limit) throw new UpdateRejectedException("Download is larger than the manifest says.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}
