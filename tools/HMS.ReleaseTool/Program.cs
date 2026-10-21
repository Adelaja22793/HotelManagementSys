using System.Security.Cryptography;
using HMS.Updater;

// Release-side helper for desktop updates. Run it on the release machine; keep the private key in a protected store
// (a hardware token or the release pipeline's secret store), never in the repository or on workstations.
//
//   keygen --out <folder> --key-id <id>
//   sign   --package <zip> --version <x.y.z> --url <https://host/path.zip> --key <private.pem> --key-id <id>
//          --out <manifest.json> [--min-api <x.y.z>] [--notes <text>]

var options = ParseOptions(args.Skip(1));
switch (args.FirstOrDefault())
{
    case "keygen":
    {
        var folder = Required(options, "out");
        var keyId = Required(options, "key-id");
        Directory.CreateDirectory(folder);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePath = Path.Combine(folder, $"{keyId}.private.pem");
        await File.WriteAllTextAsync(privatePath, key.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(privatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await File.WriteAllTextAsync(Path.Combine(folder, $"{keyId}.public.pem"), key.ExportSubjectPublicKeyInfoPem());
        Console.WriteLine($"Wrote {keyId}.private.pem (keep it secret) and {keyId}.public.pem (add to Updates:TrustedKeys).");
        return 0;
    }
    case "sign":
    {
        var package = Required(options, "package");
        var keyPem = await File.ReadAllTextAsync(Required(options, "key"));
        await using var stream = File.OpenRead(package);
        var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
        var manifest = new UpdateManifest(UpdateManifest.DesktopProduct, Required(options, "version"), Required(options, "url"), sha,
            new FileInfo(package).Length, options.GetValueOrDefault("min-api"), DateTime.UtcNow, options.GetValueOrDefault("notes"));
        using var key = ECDsa.Create();
        key.ImportFromPem(keyPem);
        await File.WriteAllBytesAsync(Required(options, "out"), UpdateManifestSigner.Sign(manifest, key, Required(options, "key-id")));
        Console.WriteLine($"Signed manifest for {manifest.Version} ({manifest.PackageSize:N0} bytes, SHA-256 {sha}).");
        return 0;
    }
    default:
        Console.Error.WriteLine("Usage: HMS.ReleaseTool keygen|sign [options]. See the comment at the top of Program.cs.");
        return 1;
}

static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    string? name = null;
    foreach (var arg in args)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal)) name = arg[2..];
        else if (name is not null) { result[name] = arg; name = null; }
    }
    return result;
}

static string Required(Dictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) ? value : throw new ArgumentException($"--{name} is required.");
