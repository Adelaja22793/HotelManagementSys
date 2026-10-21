using HMS.Application.Documents;
using HMS.Application.Notifications;
using HMS.Contracts.Documents;
using HMS.Contracts.Notifications;

namespace HMS.Api.Services;

public sealed class BackgroundJobsOptions
{
    public const string SectionName = "BackgroundJobs";
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
    public int NotificationBatchSize { get; set; } = 50;
    public int ScanBatchSize { get; set; } = 10;
}

public sealed record JobRunResult(DispatchResultDto Notifications, ScanRunResultDto Scans);

/// <summary>Runs one round of the periodic jobs. Each job gets its own scope so a failure in one never blocks the other.</summary>
public sealed class BackgroundJobRunner(IServiceScopeFactory scopes, Microsoft.Extensions.Options.IOptions<BackgroundJobsOptions> options)
{
    public async Task<JobRunResult> RunOnceAsync(CancellationToken ct)
    {
        var settings = options.Value;
        DispatchResultDto sent;
        await using (var scope = scopes.CreateAsyncScope())
            sent = await scope.ServiceProvider.GetRequiredService<INotificationDispatcher>().DispatchDueAsync(settings.NotificationBatchSize, ct);
        ScanRunResultDto scanned;
        await using (var scope = scopes.CreateAsyncScope())
            scanned = await scope.ServiceProvider.GetRequiredService<IDocumentScanProcessor>().ProcessPendingAsync(settings.ScanBatchSize, ct);
        return new JobRunResult(sent, scanned);
    }
}

/// <summary>
/// Sends queued notifications and scans uploaded documents. Safe to run on several API instances: both jobs claim
/// work with database leases.
/// </summary>
public sealed class BackgroundJobsWorker(
    BackgroundJobRunner runner,
    Microsoft.Extensions.Options.IOptions<BackgroundJobsOptions> options,
    ILogger<BackgroundJobsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Background jobs are disabled.");
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, settings.IntervalSeconds)));
        do
        {
            try
            {
                var result = await runner.RunOnceAsync(stoppingToken);
                if (result.Notifications.Claimed > 0 || result.Scans.Scanned > 0)
                    logger.LogInformation("Background jobs: {Sent} sent, {Failed} failed, {Retrying} retrying; {Scanned} files scanned",
                        result.Notifications.Sent, result.Notifications.Failed, result.Notifications.Retrying, result.Scans.Scanned);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background job round failed; it will run again on the next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
