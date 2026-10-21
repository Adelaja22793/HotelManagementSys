using System.Text.Json.Serialization;

namespace HMS.Updater;

/// <summary>
/// What a release offers. It only describes a package; it never carries commands, scripts or install paths,
/// so a manifest cannot make a workstation run anything except the verified package's own program.
/// </summary>
public sealed record UpdateManifest(
    [property: JsonPropertyName("product")] string Product,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("packageUrl")] string PackageUrl,
    [property: JsonPropertyName("packageSha256")] string PackageSha256,
    [property: JsonPropertyName("packageSize")] long PackageSize,
    [property: JsonPropertyName("minimumApiVersion")] string? MinimumApiVersion,
    [property: JsonPropertyName("publishedAtUtc")] DateTime PublishedAtUtc,
    [property: JsonPropertyName("releaseNotes")] string? ReleaseNotes)
{
    public const string DesktopProduct = "HMS.Desktop";
}

/// <summary>The file the update server publishes: the manifest's exact bytes and a detached signature over them.</summary>
public sealed record SignedManifestEnvelope(
    [property: JsonPropertyName("keyId")] string KeyId,
    [property: JsonPropertyName("manifest")] string ManifestBase64,
    [property: JsonPropertyName("signature")] string SignatureBase64);

public sealed class UpdateRejectedException(string reason) : Exception(reason);
