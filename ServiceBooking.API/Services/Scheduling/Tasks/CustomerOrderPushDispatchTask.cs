using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §456.2 — sends the web-push messages queued for customers without an account (<see cref="CustomerOrderPushNotification"/>): the
/// shape of <see cref="StaffPushDispatchTask"/> (batch by the partial dispatch index, expire what is overdue, bounded parallel sends per device, the same
/// <see cref="WebPushResponseClassifier"/> outcomes, an in-flight marker before the network call) without users and memberships. What is re-checked at SEND time:
/// the shop still allows web-push to customers, and the subscription still exists. A dead subscription (404/410) is deleted in the same pass. The TTL of a row is
/// its own <c>ExpiresAtUtc</c> (queued + 2 h): a status that arrives too late is worse than one that never arrives.
/// </summary>
public sealed class CustomerOrderPushDispatchTask(
    AppDbContext db,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IOptions<WebPushOptions> options,
    IOptions<NotificationOptions> notificationOptions,
    INotificationClock clock,
    ILogger<CustomerOrderPushDispatchTask> logger) : IScheduledTask
{
    public string Name => "customer-order-push-dispatch";
    public TimeSpan DefaultPeriod => TimeSpan.FromSeconds(10);

    private static readonly int[] BackoffMinutes = [1, 5, 15, 60];

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var opts = options.Value.Dispatch;
        var taskOptions = ScheduledTaskOptions.For(configuration, this);
        var budget = TimeSpan.FromSeconds(Math.Min(opts.BudgetSeconds, Math.Max(0, (taskOptions.MaxRunTime - TimeSpan.FromSeconds(10)).TotalSeconds)));

        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadlineCts.CancelAfter(budget);
        var linkedCt = deadlineCts.Token;

        int scanned = 0, expired = 0;
        var partial = false;
        var results = new ConcurrentBag<(int Sent, int Failed, int Skipped)>();

        try
        {
            var now = clock.UtcNow;
            var inFlightCutoff = now - TimeSpan.FromMinutes(opts.InFlightGraceMinutes);
            var candidates = await db.CustomerOrderPushNotifications
                .Where(n => n.Status == NotificationStatus.Pending)
                .Where(n => n.NextAttemptAtUtc == null || n.NextAttemptAtUtc <= now)
                .Where(n => n.LastAttemptAtUtc == null || n.LastAttemptAtUtc < inFlightCutoff)
                .OrderBy(n => n.ExpiresAtUtc).ThenBy(n => n.CreatedAt)
                .Take(opts.BatchSize)
                .ToListAsync(linkedCt);
            scanned = candidates.Count;
            if (scanned == 0) return await FinishAsync(0, 0, 0, 0, 0, "queue empty");

            var readyToSend = new List<CustomerOrderPushNotification>();
            foreach (var row in candidates)
            {
                if (row.ExpiresAtUtc >= now) { readyToSend.Add(row); continue; }
                row.Status = NotificationStatus.Expired;
                row.Reason = NotificationReason.PushTtlExhausted;
                expired++;
            }
            await db.SaveChangesAsync(linkedCt);

            if (readyToSend.Count > 0)
                await Parallel.ForEachAsync(readyToSend, new ParallelOptions { MaxDegreeOfParallelism = opts.MaxParallelEndpoints, CancellationToken = CancellationToken.None },
                    async (row, _) =>
                    {
                        if (linkedCt.IsCancellationRequested) return; // budget exhausted — the remainder stays Pending
                        results.Add(await ProcessRowAsync(row.Id, opts, ct));
                    });
            if (linkedCt.IsCancellationRequested && !ct.IsCancellationRequested) partial = true;
        }
        catch (OperationCanceledException) when (linkedCt.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            partial = true;
        }

        var sent = results.Sum(r => r.Sent);
        var failed = results.Sum(r => r.Failed);
        var skipped = results.Sum(r => r.Skipped);
        var summary = $"scanned {scanned}, sent {sent}, failed {failed}, expired {expired}, skipped {skipped}" + (partial ? " (partial, time budget reached)" : "");
        return await FinishAsync(scanned, sent, failed, expired, skipped, summary);
    }

    private async Task<ScheduledTaskOutcome> FinishAsync(int scanned, int sent, int failed, int expired, int skipped, string summary)
    {
        var queueDepth = 0;
        try
        {
            queueDepth = await db.CustomerOrderPushNotifications.CountAsync(n => n.Status == NotificationStatus.Pending, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "customer-order-push-dispatch: failed to compute queue depth for the summary log");
        }
        logger.LogInformation("customer-order-push-dispatch pass: {Summary}. queueDepth={QueueDepth}", summary, queueDepth);
        return new ScheduledTaskOutcome(scanned, sent + failed + expired + skipped, 0, summary);
    }

    private async Task<(int Sent, int Failed, int Skipped)> ProcessRowAsync(Guid rowId, WebPushOptions.DispatchOptions opts, CancellationToken runnerCt)
    {
        // AppDbContext is not thread-safe and sends run in parallel: every row gets its own scope.
        using var scope = scopeFactory.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IWebPushSender>();
        var scopedClock = scope.ServiceProvider.GetRequiredService<INotificationClock>();
        var encryptionKey = notificationOptions.Value.EncryptionKey ?? string.Empty;

        var row = await scopedDb.CustomerOrderPushNotifications.FirstOrDefaultAsync(n => n.Id == rowId, runnerCt);
        if (row is null || row.Status != NotificationStatus.Pending) return (0, 0, 0);

        // Re-checked at send time, never trusted from queue time: the shop may have switched web-push to customers off since.
        var enabled = await scopedDb.ShopSettings.AsNoTracking().Where(s => s.CompanyId == row.CompanyId)
            .Select(s => (bool?)s.CustomerWebPushEnabled).FirstOrDefaultAsync(runnerCt) ?? new ShopSettings().CustomerWebPushEnabled;
        if (!enabled) return await SkipAsync(scopedDb, row, NotificationReason.CustomerPushDisabledByShop, runnerCt);

        var subscription = row.SubscriptionId is { } subscriptionId
            ? await scopedDb.OrderPushSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, runnerCt)
            : null;
        if (subscription is null) return await SkipAsync(scopedDb, row, NotificationReason.PushSubscriptionGone, runnerCt);

        row.AttemptCount++;
        row.LastAttemptAtUtc = scopedClock.UtcNow; // in-flight marker BEFORE the network call
        await scopedDb.SaveChangesAsync(runnerCt);

        string p256dh, auth;
        try
        {
            var aad = $"order-push-subscription:{subscription.Id}";
            p256dh = SecretProtector.Decrypt(subscription.P256dhCiphertext, encryptionKey, aad);
            auth = SecretProtector.Decrypt(subscription.AuthCiphertext, encryptionKey, aad);
        }
        catch (Exception ex) when (ex is ChannelSecretUnavailableException or ArgumentException)
        {
            row.Status = NotificationStatus.Failed;
            row.Reason = NotificationReason.PushAuthRejected;
            row.ReasonDetail = Truncate(ex.Message);
            await scopedDb.SaveChangesAsync(runnerCt);
            return (0, 1, 0);
        }

        var target = new WebPushSubscriptionTarget(subscription.Id, subscription.Endpoint, p256dh, auth);
        var ttlSeconds = Math.Clamp((int)(row.ExpiresAtUtc - scopedClock.UtcNow).TotalSeconds, 1, 7200);
        var topic = row.OrderId is { } orderId ? $"co-{orderId:N}"[..32] : $"co-{row.Id:N}"[..32];
        var outcome = await sender.SendAsync(target, row.Payload, ttlSeconds, topic, runnerCt);

        switch (outcome)
        {
            case WebPushSendOutcome.Sent:
                row.Status = NotificationStatus.Sent;
                row.SentAtUtc = scopedClock.UtcNow;
                row.Reason = NotificationReason.Delivered;
                subscription.LastSuccessAtUtc = scopedClock.UtcNow;
                subscription.ConsecutiveFailures = 0;
                await scopedDb.SaveChangesAsync(runnerCt);
                return (1, 0, 0);

            case WebPushSendOutcome.Gone:
                scopedDb.OrderPushSubscriptions.Remove(subscription); // the browser dropped it: gone in THIS pass, no retry
                row.Status = NotificationStatus.Skipped;
                row.Reason = NotificationReason.PushSubscriptionGone;
                await scopedDb.SaveChangesAsync(runnerCt);
                return (0, 0, 1);

            case WebPushSendOutcome.AuthRejected authRejected:
                row.Status = NotificationStatus.Failed;
                row.Reason = NotificationReason.PushAuthRejected;
                row.ReasonDetail = Truncate(authRejected.Detail);
                subscription.ConsecutiveFailures++;
                await scopedDb.SaveChangesAsync(runnerCt);
                return (0, 1, 0);

            case WebPushSendOutcome.PayloadTooLarge tooLarge:
                row.Status = NotificationStatus.Failed;
                row.Reason = NotificationReason.PushPayloadTooLarge;
                row.ReasonDetail = Truncate(tooLarge.Detail);
                await scopedDb.SaveChangesAsync(runnerCt);
                return (0, 1, 0);

            case WebPushSendOutcome.Transient transient:
                subscription.ConsecutiveFailures++;
                if (row.AttemptCount >= opts.MaxAttempts)
                {
                    row.Status = NotificationStatus.Failed;
                    row.Reason = NotificationReason.RetriesExhausted;
                    row.ReasonDetail = Truncate(transient.Detail);
                    await scopedDb.SaveChangesAsync(runnerCt);
                    return (0, 1, 0);
                }
                row.NextAttemptAtUtc = scopedClock.UtcNow.AddMinutes(BackoffMinutes[Math.Min(row.AttemptCount, BackoffMinutes.Length) - 1]);
                await scopedDb.SaveChangesAsync(runnerCt);
                return (0, 0, 0); // stays Pending

            default:
                throw new InvalidOperationException($"Unhandled {nameof(WebPushSendOutcome)} subtype: {outcome.GetType().Name}");
        }
    }

    private static async Task<(int Sent, int Failed, int Skipped)> SkipAsync(
        AppDbContext scopedDb, CustomerOrderPushNotification row, NotificationReason reason, CancellationToken ct)
    {
        row.Status = NotificationStatus.Skipped;
        row.Reason = reason;
        await scopedDb.SaveChangesAsync(ct);
        return (0, 0, 1);
    }

    private static string? Truncate(string? text, int maxLength = 300) =>
        string.IsNullOrEmpty(text) || text.Length <= maxLength ? text : text[..maxLength];
}
