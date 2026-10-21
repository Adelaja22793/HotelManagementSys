using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HMS.Updater;

namespace HMS.SecurityTests;

/// <summary>Desktop updates: only signed, newer, hash-matching packages from trusted hosts install, and safely.</summary>
public sealed class UpdaterTests : IDisposable
{
    private const string KeyId = "release-2026";
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hms-upd-" + Guid.NewGuid().ToString("N"));
    private readonly UpdateVerifier _verifier;
    private static readonly Version Installed = new(1, 0, 0);

    public UpdaterTests()
    {
        _verifier = new UpdateVerifier(new UpdaterOptions
        {
            TrustedKeys = { [KeyId] = _key.ExportSubjectPublicKeyInfoPem() },
            TrustedPackageHosts = ["updates.hotel.test"],
        });
    }

    public void Dispose()
    {
        _key.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static UpdateManifest Manifest(string version = "1.1.0", byte[]? package = null, string url = "https://updates.hotel.test/hms/1.1.0.zip",
        string product = UpdateManifest.DesktopProduct)
    {
        package ??= [1, 2, 3];
        return new UpdateManifest(product, version, url, Convert.ToHexStringLower(SHA256.HashData(package)), package.Length, null, DateTime.UtcNow, null);
    }

    private byte[] Signed(UpdateManifest manifest, ECDsa? key = null, string keyId = KeyId) => UpdateManifestSigner.Sign(manifest, key ?? _key, keyId);

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }
        return buffer.ToArray();
    }

    [Fact]
    public void A_signed_newer_manifest_is_accepted() => Assert.Equal("1.1.0", _verifier.VerifyManifest(Signed(Manifest()), Installed).Version);

    [Fact]
    public void A_manifest_signed_by_an_unknown_key_is_rejected()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<UpdateRejectedException>(() => _verifier.VerifyManifest(Signed(Manifest(), other), Installed));
        Assert.Throws<UpdateRejectedException>(() => _verifier.VerifyManifest(Signed(Manifest(), other, "attacker"), Installed));
    }

    [Fact]
    public void Editing_the_signed_manifest_breaks_it()
    {
        var envelope = JsonSerializer.Deserialize<SignedManifestEnvelope>(Signed(Manifest()))!;
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(envelope.ManifestBase64))
            .Replace("updates.hotel.test", "evil.example");
        var tampered = envelope with { ManifestBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) };
        Assert.Throws<UpdateRejectedException>(() => _verifier.VerifyManifest(JsonSerializer.SerializeToUtf8Bytes(tampered), Installed));
    }

    [Theory]
    [InlineData("1.0.0")] // same version
    [InlineData("0.9.9")] // downgrade
    public void Old_or_same_versions_are_rejected(string version) =>
        Assert.Throws<UpdateRejectedException>(() => _verifier.VerifyManifest(Signed(Manifest(version)), Installed));

    [Theory]
    [InlineData("http://updates.hotel.test/hms.zip")]
    [InlineData("https://evil.example/hms.zip")]
    [InlineData("file:///C:/temp/hms.zip")]
    public void Packages_only_come_from_trusted_https_hosts(string url) =>
        Assert.Throws<UpdateRejectedException>(() => _verifier.VerifyManifest(Signed(Manifest(url: url)), Installed));

    [Fact]
    public void A_manifest_for_another_product_is_rejected() =>
        Assert.Throws<UpdateRejectedException>(() => _verifier.VerifyManifest(Signed(Manifest(product: "Other")), Installed));

    [Fact]
    public void Garbage_is_rejected_without_crashing()
    {
        Assert.Throws<UpdateRejectedException>(() => _verifier.VerifyManifest("not json"u8, Installed));
        Assert.Throws<UpdateRejectedException>(() => _verifier.VerifyManifest("{}"u8, Installed));
        Assert.Throws<UpdateRejectedException>(() => _verifier.VerifyManifest(new byte[100_000], Installed));
    }

    [Fact]
    public async Task A_package_must_match_the_signed_hash_and_size()
    {
        var package = Zip(("HMS.Desktop.exe", "app"));
        var manifest = Manifest(package: package);
        await UpdateVerifier.VerifyPackageAsync(new MemoryStream(package), manifest, default);

        var altered = (byte[])package.Clone();
        altered[^1] ^= 0xFF;
        await Assert.ThrowsAsync<UpdateRejectedException>(() => UpdateVerifier.VerifyPackageAsync(new MemoryStream(altered), manifest, default));
        await Assert.ThrowsAsync<UpdateRejectedException>(() => UpdateVerifier.VerifyPackageAsync(new MemoryStream([.. package, 0]), manifest, default));
    }

    [Fact]
    public async Task Staging_activation_and_rollback_switch_whole_versions()
    {
        var layout = new InstallLayout(_root);
        await layout.StageAsync(new MemoryStream(Zip(("HMS.Desktop.exe", "v1"), ("lib/a.dll", "a"))), "1.0.0", default);
        layout.Activate("1.0.0");
        await layout.StageAsync(new MemoryStream(Zip(("HMS.Desktop.exe", "v2"))), "1.1.0", default);
        Assert.Equal(("1.0.0", (string?)null), layout.Read()); // staging alone changes nothing

        layout.Activate("1.1.0");
        Assert.Equal("v2", await File.ReadAllTextAsync(layout.LaunchTarget()!));
        Assert.True(layout.Rollback());
        Assert.Equal("v1", await File.ReadAllTextAsync(layout.LaunchTarget()!));

        // A damaged current version falls back to the previous one.
        layout.Activate("1.1.0");
        File.Delete(Path.Combine(layout.VersionFolder("1.1.0"), "HMS.Desktop.exe"));
        Assert.Equal("v1", await File.ReadAllTextAsync(layout.LaunchTarget()!));
    }

    [Theory]
    [InlineData("../evil.exe")]
    [InlineData("lib/../../evil.exe")]
    [InlineData("/etc/evil")]
    [InlineData("C:/Windows/evil.exe")]
    [InlineData("..\\evil.exe")]
    public async Task Archive_entries_cannot_escape_the_version_folder(string entry)
    {
        var layout = new InstallLayout(_root);
        await Assert.ThrowsAsync<UpdateRejectedException>(() =>
            layout.StageAsync(new MemoryStream(Zip(("HMS.Desktop.exe", "app"), (entry, "x"))), "1.1.0", default));
        Assert.False(Directory.Exists(Path.Combine(_root, "versions", "1.1.0")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.exe")));
        Assert.False(File.Exists(Path.Combine(_root, "versions", "evil.exe")));
    }

    [Fact]
    public async Task Links_inside_the_archive_are_rejected()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(archive.CreateEntry("HMS.Desktop.exe").Open())) w.Write("app");
            var link = archive.CreateEntry("config");
            link.ExternalAttributes = unchecked((int)(0xA1FFu << 16)); // symbolic link, mode 777
            using (var w = new StreamWriter(link.Open())) w.Write("/etc/passwd");
        }
        await Assert.ThrowsAsync<UpdateRejectedException>(() => new InstallLayout(_root).StageAsync(new MemoryStream(buffer.ToArray()), "1.1.0", default));
    }

    [Fact]
    public async Task A_package_without_the_app_is_not_staged()
    {
        var layout = new InstallLayout(_root);
        await Assert.ThrowsAsync<UpdateRejectedException>(() => layout.StageAsync(new MemoryStream(Zip(("readme.txt", "hi"))), "1.1.0", default));
        Assert.Throws<UpdateRejectedException>(() => layout.Activate("1.1.0"));
    }

    [Theory]
    [InlineData("1.1")]
    [InlineData("..\\1.0.0")]
    [InlineData("1.0.0/../../x")]
    public void Version_folders_are_plain_version_numbers(string version)
    {
        if (version == "1.1") Assert.EndsWith("1.1", new InstallLayout(_root).VersionFolder(version));
        else Assert.Throws<UpdateRejectedException>(() => new InstallLayout(_root).VersionFolder(version));
    }
}
