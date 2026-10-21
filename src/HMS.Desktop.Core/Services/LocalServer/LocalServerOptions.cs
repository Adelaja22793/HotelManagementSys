namespace HMS.Desktop.Services.LocalServer;

/// <summary>
/// Settings for the built-in server. In "Local" mode the desktop app starts the HMS API itself on this PC,
/// so staff only ever open one program.
/// </summary>
public sealed class LocalServerOptions
{
    public const string SectionName = "LocalServer";

    /// <summary>Database for this PC. LocalDB needs no setup and starts on demand.</summary>
    public string ConnectionString { get; set; } =
        @"Server=(localdb)\MSSQLLocalDB;Database=HMS_Local;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>The first start creates the database, which can take a while on a slow PC.</summary>
    public int StartTimeoutSeconds { get; set; } = 180;

    /// <summary>Where settings, logs and documents are kept. Empty means %LocalAppData%\HMS.</summary>
    public string DataDirectory { get; set; } = string.Empty;

    /// <summary>The folder holding the API program. Empty means the "server" folder next to the desktop app.</summary>
    public string ServerDirectory { get; set; } = string.Empty;

    internal string ResolvedDataDirectory => string.IsNullOrWhiteSpace(DataDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HMS")
        : Path.GetFullPath(DataDirectory);

    internal string ResolvedServerDirectory => string.IsNullOrWhiteSpace(ServerDirectory)
        ? Path.Combine(AppContext.BaseDirectory, "server")
        : Path.GetFullPath(ServerDirectory);
}

/// <summary>What the first-run screen collects: the hotel and its first administrator.</summary>
public sealed record LocalSetup(string HotelName, string BranchName, string AdminFullName, string AdminEmail, string AdminPassword);

/// <summary>Protects the server's signing key at rest. On Windows this is DPAPI, tied to the signed-in user.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public sealed class LocalServerException(string message, IReadOnlyList<string> output) : Exception(message)
{
    /// <summary>The last lines the server printed, for the error screen and support.</summary>
    public IReadOnlyList<string> Output { get; } = output;

    /// <summary>True when the server could not reach SQL Server LocalDB, usually because it is not installed.</summary>
    public bool IsDatabaseUnavailable => Output.Any(line =>
        line.Contains("LocalDB", StringComparison.OrdinalIgnoreCase)
        || line.Contains("network-related or instance-specific", StringComparison.OrdinalIgnoreCase)
        || line.Contains("error: 52", StringComparison.OrdinalIgnoreCase)
        || line.Contains("error: 50", StringComparison.OrdinalIgnoreCase));
}
