using HMS.Application.Abstractions;
using HMS.Contracts.Notifications;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HMS.Application.Notifications;

/// <summary>Result of handing a message to a provider. Permanent failures (bad address, no provider) are not retried.</summary>
public sealed record SendResult(bool Success, string? ProviderMessageId = null, string? Error = null, bool Permanent = false)
{
    public static SendResult Ok(string? providerMessageId = null) => new(true, providerMessageId);
    public static SendResult Fail(string error, bool permanent = false) => new(false, null, error, permanent);
}

public sealed record OutgoingMessage(Guid Id, NotificationChannel Channel, string Recipient, string? Subject, string Body, string IdempotencyKey);

/// <summary>A delivery channel (SMTP email, SMS gateway, in-app). Implementations must not log message bodies.</summary>
public interface INotificationSender
{
    NotificationChannel Channel { get; }
    string Provider { get; }
    Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct);
}

public interface INotificationDispatcher
{
    /// <summary>Claims due messages with a lease, sends them and records each attempt. Safe to run on many workers at once.</summary>
    Task<DispatchResultDto> DispatchDueAsync(int maxMessages, CancellationToken ct);
}

internal sealed class NotificationDispatcher(
    IApplicationDbContext db,
    IClock clock,
    IEnumerable<INotificationSender> senders,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<DispatchResultDto> DispatchDueAsync(int maxMessages, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var candidates = await db.NotificationOutbox.AsNoTracking()
            .Where(o => (o.Status == OutboxStatus.Pending && o.NextAttemptAtUtc <= now)
                || (o.Status == OutboxStatus.Processing && o.LeaseExpiresAtUtc < now))
            .OrderBy(o => o.NextAttemptAtUtc)
            .Select(o => o.Id)
            .Take(maxMessages)
            .ToListAsync(ct);

        int claimed = 0, sent = 0, failed = 0, retrying = 0;
        foreach (var id in candidates)
        {
            var lease = Guid.NewGuid();
            var expires = now.Add(Lease);
            // Atomic claim: only one worker's UPDATE matches the row.
            var won = await db.NotificationOutbox
                .Where(o => o.Id == id && ((o.Status == OutboxStatus.Pending && o.NextAttemptAtUtc <= now)
                    || (o.Status == OutboxStatus.Processing && o.LeaseExpiresAtUtc < now)))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, OutboxStatus.Processing)
                    .SetProperty(o => o.LeaseId, lease)
                    .SetProperty(o => o.LeaseExpiresAtUtc, expires)
                    .SetProperty(o => o.Attempts, o => o.Attempts + 1), ct);
            if (won == 0) continue;
            claimed++;

            db.ChangeTracker.Clear();
            var message = await db.NotificationOutbox.SingleAsync(o => o.Id == id, ct);
            var sender = senders.FirstOrDefault(s => s.Channel == message.Channel);
            SendResult result;
            try
            {
                result = sender is null
                    ? SendResult.Fail($"No {message.Channel} provider is configured.", permanent: true)
                    : await sender.SendAsync(new OutgoingMessage(message.Id, message.Channel, message.Recipient, message.Subject, message.Body,
                        message.IdempotencyKey), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Never log the body or recipient; the outbox id is enough to investigate.
                logger.LogWarning(ex, "Notification {OutboxId} delivery attempt {Attempt} failed", message.Id, message.Attempts);
                result = SendResult.Fail(ex.GetType().Name + ": " + ex.Message);
            }

            var at = clock.UtcNow;
            db.NotificationDeliveries.Add(new NotificationDelivery
            {
                OutboxId = message.Id,
                Attempt = message.Attempts,
                Provider = sender?.Provider ?? "none",
                ProviderMessageId = result.ProviderMessageId,
                Status = result.Success ? (message.Channel == NotificationChannel.InApp ? DeliveryStatus.Delivered : DeliveryStatus.Submitted) : DeliveryStatus.Failed,
                SubmittedAtUtc = at,
                DeliveredAtUtc = result.Success && message.Channel == NotificationChannel.InApp ? at : null,
                Error = result.Error is { Length: > 500 } e ? e[..500] : result.Error,
            });
            if (result.Success)
            {
                message.MarkSent(at);
                sent++;
            }
            else
            {
                message.MarkFailed(result.Error ?? "Unknown error", result.Permanent, at);
                if (message.Status == OutboxStatus.Failed) failed++; else retrying++;
            }

            try
            {
                await db.SaveChangesAsync(ct); // LeaseId is a concurrency token: a lost lease does not overwrite the new owner.
            }
            catch (DbUpdateConcurrencyException)
            {
                logger.LogWarning("Notification {OutboxId} lease was lost before the result was saved", message.Id);
            }
        }
        return new DispatchResultDto(claimed, sent, failed, retrying);
    }
}

/// <summary>In-app messages are "delivered" by being visible in the inbox; there is no external provider.</summary>
internal sealed class InAppSender : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.InApp;
    public string Provider => "in-app";
    public Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct) => Task.FromResult(SendResult.Ok());
}
