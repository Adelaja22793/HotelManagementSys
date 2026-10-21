using System.Diagnostics;

namespace HMS.Api.Services;

/// <summary>
/// When the desktop app runs this API as its built-in server it passes its own process id in
/// <c>Hosting:ParentProcessId</c>. The API then shuts down as soon as the desktop app exits, even after a crash,
/// so no orphaned server is left running.
/// </summary>
public sealed class ParentProcessWatcher(IConfiguration configuration, IHostApplicationLifetime lifetime, ILogger<ParentProcessWatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!int.TryParse(configuration["Hosting:ParentProcessId"], out var parentId)) return;

        Process parent;
        try
        {
            parent = Process.GetProcessById(parentId);
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Parent process {ParentId} is not running. Stopping.", parentId);
            lifetime.StopApplication();
            return;
        }

        using (parent)
        {
            try
            {
                await parent.WaitForExitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        logger.LogInformation("Parent process {ParentId} exited. Stopping.", parentId);
        lifetime.StopApplication();
    }
}
