using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace HMS.Desktop.Services.LocalServer;

/// <summary>
/// Runs the HMS API as a child process on this PC. It listens on 127.0.0.1 only, so nothing on the network can
/// reach it, and it stops when the desktop app exits (it watches the app's process id).
/// </summary>
public sealed class LocalServer(LocalServerOptions options, ISecretProtector protector) : IAsyncDisposable
{
    private const int OutputLines = 200;
    private readonly Queue<string> _output = new();
    private readonly Lock _outputLock = new();
    private Process? _process;
    private StreamWriter? _log;

    private string StatePath => Path.Combine(options.ResolvedDataDirectory, "local-server.json");

    /// <summary>True until the first-run setup has created the hotel and its administrator.</summary>
    public bool NeedsSetup => !LoadState().SetupComplete;

    public Uri? BaseAddress { get; private set; }

    public IReadOnlyList<string> RecentOutput
    {
        get { lock (_outputLock) return [.. _output]; }
    }

    /// <summary>Starts the server and waits until it reports ready. Pass <paramref name="setup"/> on the first run.</summary>
    public async Task StartAsync(LocalSetup? setup, CancellationToken ct = default)
    {
        if (_process is not null) throw new InvalidOperationException("The server is already running.");
        Directory.CreateDirectory(options.ResolvedDataDirectory);

        var state = LoadState();
        if (state.ProtectedSigningKey is null)
        {
            state = state with { ProtectedSigningKey = Convert.ToBase64String(protector.Protect(RandomNumberGenerator.GetBytes(48))) };
            SaveState(state);
        }
        var signingKey = Convert.ToBase64String(protector.Unprotect(Convert.FromBase64String(state.ProtectedSigningKey)));

        var port = FreePort();
        var start = CreateStartInfo();
        foreach (var (key, value) in BuildEnvironment(options, signingKey, port, Environment.ProcessId, setup))
            start.Environment[key] = value;

        OpenLog();
        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => Capture(e.Data);
        _process.ErrorDataReceived += (_, e) => Capture(e.Data);
        try
        {
            _process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _process.Dispose();
            _process = null;
            throw new LocalServerException("The HMS server program could not be started: " + ex.Message, RecentOutput);
        }
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var baseAddress = new Uri($"http://127.0.0.1:{port}/");
        await WaitUntilReadyAsync(_process, baseAddress, ct);
        BaseAddress = baseAddress;

        if (setup is not null) SaveState(state with { SetupComplete = true });
    }

    private async Task WaitUntilReadyAsync(Process process, Uri baseAddress, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(options.StartTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(process, _process))
                throw new LocalServerException("HMS was closed while the server was starting.", RecentOutput);
            if (process.HasExited)
            {
                // Let the output readers drain so the error screen shows why it stopped.
                await process.WaitForExitAsync(ct);
                var code = process.ExitCode;
                await StopAsync();
                throw new LocalServerException($"The HMS server stopped while starting (exit code {code}).", RecentOutput);
            }
            try
            {
                using var response = await http.GetAsync("health/ready", ct);
                if (response.IsSuccessStatusCode) return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // Not listening yet.
            }
            await Task.Delay(500, ct);
        }
        await StopAsync();
        throw new LocalServerException("The HMS server did not start in time.", RecentOutput);
    }

    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        BaseAddress = null;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(wait.Token);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
            {
                // Already gone, or it will stop by itself when it notices this app has exited.
            }
            process.Dispose();
        }
        lock (_outputLock)
        {
            _log?.Dispose();
            _log = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    internal static Dictionary<string, string> BuildEnvironment(LocalServerOptions options, string signingKey, int port, int parentProcessId, LocalSetup? setup)
    {
        var env = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
            ["ASPNETCORE_HTTPS_PORTS"] = "",
            ["ASPNETCORE_HTTP_PORTS"] = "",
            ["ConnectionStrings__HotelManagementDb"] = options.ConnectionString,
            ["Database__Provider"] = "SqlServer",
            // There is no database administrator on a single PC, so the app keeps its own schema up to date.
            ["Database__ApplyMigrationsOnStartup"] = "true",
            ["Jwt__SigningKey"] = signingKey,
            ["Documents__StoragePath"] = Path.Combine(options.ResolvedDataDirectory, "documents"),
            ["Hosting__ParentProcessId"] = parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Bootstrap__Enabled"] = setup is null ? "false" : "true",
        };
        if (setup is not null)
        {
            env["Bootstrap__CompanyName"] = setup.HotelName;
            env["Bootstrap__BranchName"] = setup.BranchName;
            env["Bootstrap__AdminFullName"] = setup.AdminFullName;
            env["Bootstrap__AdminEmail"] = setup.AdminEmail;
            env["Bootstrap__AdminPassword"] = setup.AdminPassword;
        }
        return env;
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var folder = options.ResolvedServerDirectory;
        var exe = Path.Combine(folder, OperatingSystem.IsWindows() ? "HMS.Api.exe" : "HMS.Api");
        var dll = Path.Combine(folder, "HMS.Api.dll");
        ProcessStartInfo start;
        if (File.Exists(exe)) start = new ProcessStartInfo(exe);
        else if (File.Exists(dll)) start = new ProcessStartInfo("dotnet") { ArgumentList = { dll } };
        else throw new LocalServerException($"The HMS server program is missing from {folder}. Reinstall HMS.", []);

        start.WorkingDirectory = folder;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        return start;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void OpenLog()
    {
        var folder = Path.Combine(options.ResolvedDataDirectory, "logs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "server.log");
        var append = !File.Exists(path) || new FileInfo(path).Length < 5 * 1024 * 1024;
        lock (_outputLock)
        {
            _output.Clear();
            _log = new StreamWriter(path, append) { AutoFlush = true };
            _log.WriteLine($"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} starting");
        }
    }

    private void Capture(string? line)
    {
        if (line is null) return;
        lock (_outputLock)
        {
            _output.Enqueue(line);
            while (_output.Count > OutputLines) _output.Dequeue();
            _log?.WriteLine(line);
        }
    }

    private LocalServerState LoadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<LocalServerState>(File.ReadAllText(StatePath)) ?? new LocalServerState(null, false)
                : new LocalServerState(null, false);
        }
        catch (JsonException)
        {
            return new LocalServerState(null, false);
        }
    }

    private void SaveState(LocalServerState state)
    {
        Directory.CreateDirectory(options.ResolvedDataDirectory);
        var temp = StatePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state));
        File.Move(temp, StatePath, overwrite: true);
    }

    private sealed record LocalServerState(string? ProtectedSigningKey, bool SetupComplete);
}
