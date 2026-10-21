using System.Security.Cryptography;
using System.Text.Json;

namespace HMS.Updater;

public sealed class UpdaterOptions
{
    /// <summary>Public keys (PEM, ECDSA P-256) by key id. Shipped with the app; more than one allows key rotation.</summary>
    public Dictionary<string, string> TrustedKeys { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Hosts packages may be downloaded from, for example updates.hotel.local.</summary>
    public List<string> TrustedPackageHosts { get; set; } = [];
    public long MaxPackageBytes { get; set; } = 500L * 1024 * 1024;
    public long MaxManifestBytes { get; set; } = 64 * 1024;
}

/// <summary>
/// Checks a signed manifest and a downloaded package. Every check fails closed: anything unexpected is a rejection.
/// </summary>
public sealed class UpdateVerifier(UpdaterOptions options)
{
    /// <summary>Returns the manifest when it is signed by a trusted key, is for this product and is newer than <paramref name="installed"/>.</summary>
    public UpdateManifest VerifyManifest(ReadOnlySpan<byte> envelopeJson, Version installed, string product = UpdateManifest.DesktopProduct)
    {
        if (envelopeJson.Length == 0 || envelopeJson.Length > options.MaxManifestBytes) throw new UpdateRejectedException("Manifest size is not acceptable.");

        SignedManifestEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<SignedManifestEnvelope>(envelopeJson); }
        catch (JsonException) { throw new UpdateRejectedException("Manifest is not valid JSON."); }
        if (envelope is null || string.IsNullOrEmpty(envelope.KeyId)) throw new UpdateRejectedException("Manifest envelope is incomplete.");
        if (!options.TrustedKeys.TryGetValue(envelope.KeyId, out var pem)) throw new UpdateRejectedException($"Manifest key '{envelope.KeyId}' is not trusted.");

        byte[] manifestBytes, signature;
        try
        {
            manifestBytes = Convert.FromBase64String(envelope.ManifestBase64 ?? "");
            signature = Convert.FromBase64String(envelope.SignatureBase64 ?? "");
        }
        catch (FormatException) { throw new UpdateRejectedException("Manifest encoding is not valid."); }

        using (var key = ECDsa.Create())
        {
            key.ImportFromPem(pem);
            if (key.KeySize != 256) throw new UpdateRejectedException("Trusted key must be ECDSA P-256.");
            if (!key.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256)) throw new UpdateRejectedException("Manifest signature is not valid.");
        }

        // Only now, with the signature proven, is the content read.
        UpdateManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes); }
        catch (JsonException) { throw new UpdateRejectedException("Signed manifest is not valid JSON."); }
        if (manifest is null) throw new UpdateRejectedException("Signed manifest is empty.");

        if (!string.Equals(manifest.Product, product, StringComparison.Ordinal)) throw new UpdateRejectedException("Manifest is for another product.");
        if (!System.Version.TryParse(manifest.Version, out var offered)) throw new UpdateRejectedException("Manifest version is not valid.");
        if (offered <= installed) throw new UpdateRejectedException($"Version {offered} is not newer than installed {installed}.");
        if (manifest.PackageSize <= 0 || manifest.PackageSize > options.MaxPackageBytes) throw new UpdateRejectedException("Package size is not acceptable.");
        if (manifest.PackageSha256 is not { Length: 64 } || !manifest.PackageSha256.All(Uri.IsHexDigit)) throw new UpdateRejectedException("Package hash is not valid.");
        if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new UpdateRejectedException("Package must be downloaded over HTTPS.");
        if (!options.TrustedPackageHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase)) throw new UpdateRejectedException($"Package host '{url.Host}' is not trusted.");
        return manifest;
    }

    /// <summary>Checks the downloaded package's size and SHA-256 against the verified manifest.</summary>
    public static async Task VerifyPackageAsync(Stream package, UpdateManifest manifest, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await package.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > manifest.PackageSize) throw new UpdateRejectedException("Package is larger than the manifest says.");
            sha.AppendData(buffer, 0, read);
        }
        if (total != manifest.PackageSize) throw new UpdateRejectedException("Package size does not match the manifest.");
        var actual = Convert.ToHexStringLower(sha.GetHashAndReset());
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(actual),
                System.Text.Encoding.ASCII.GetBytes(manifest.PackageSha256.ToLowerInvariant())))
            throw new UpdateRejectedException("Package hash does not match the manifest.");
    }
}

/// <summary>Release-side signing, used by the release tool. The private key never ships with the app.</summary>
public static class UpdateManifestSigner
{
    public static byte[] Sign(UpdateManifest manifest, ECDsa privateKey, string keyId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var signature = privateKey.SignData(bytes, HashAlgorithmName.SHA256);
        return JsonSerializer.SerializeToUtf8Bytes(new SignedManifestEnvelope(keyId, Convert.ToBase64String(bytes), Convert.ToBase64String(signature)),
            new JsonSerializerOptions { WriteIndented = true });
    }
}
