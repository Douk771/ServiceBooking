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
/// The fifth scheduled task (ARCHITECTURE_CYCLE9.md §105.8, US-116/US-124). One pass: select a batch by
/// <c>IX_StaffPushNotifications_Dispatch</c>, expire what's overdue, then send the rest — bounded
/// PARALLEL across DEVICES (unlike <see cref="NotificationDispatchTask"/>'s per-CHANNEL sequential
/// antiban pacing: push services impose no comparable per-account rate limit at this volume, §105.8).
/// </summary>
public sealed class StaffPushDispatchTask(
    AppDbContext db,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IOptions<WebPushOptions> options,
    IOptions<NotificationOptions> notificationOptions,
    INotificationClock clock,
    ILogger<StaffPushDispatchTask> logger) : IScheduledTask
{
    public string Name => "staff-push-dispatch";
    public TimeSpan DefaultPeriod => TimeSpan.FromMinutes(1);

    private static readonly int[] BackoffMinutes = [1, 5, 15, 60];

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var opts = options.Value.Dispatch;

        var taskOptions = ScheduledTaskOptions.For(configuration, this);
        var runnerHeadroom = taskOptions.MaxRunTime - TimeSpan.FromSeconds(10);
        var configuredBudget = TimeSpan.FromSeconds(opts.BudgetSeconds);
        var budget = runnerHeadroom < configuredBudget ? runnerHeadroom : configuredBudget;
        if (budget < TimeSpan.Zero) budget = TimeSpan.Zero;

        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadlineCts.CancelAfter(budget);
        var linkedCt = deadlineCts.Token;

        var scanned = 0;
        var expired = 0;
        var sent = 0;
        var failed = 0;
        var skipped = 0;
        var partial = false;
        var results = new ConcurrentBag<(int Sent, int Failed, int Skipped)>();

        try
        {
            var now = clock.UtcNow;

            var candidates = await db.StaffPushNotifications
                .Where(n => n.Status == NotificationStatus.Pending)
                .Where(n => n.NextAttemptAtUtc == null || n.NextAttemptAtUtc <= now)
                .OrderBy(n => n.ExpiresAtUtc).ThenBy(n => n.CreatedAt)
                .Take(opts.BatchSize)
                .ToListAsync(linkedCt);

            scanned = candidates.Count;
            if (scanned == 0)
                return await FinishAsync(scanned, sent, failed, expired, skipped, "queue empty");

            var visitStartByRowId = await ResolveVisitStartTimesAsync(candidates, linkedCt);

            var readyToSend = new List<StaffPushNotification>();
            foreach (var row in candidates)
            {
                if (row.ExpiresAtUtc >= now)
                {
                    readyToSend.Add(row);
                    continue;
                }

                // §105.8: Expired, either because the visit itself already started, or because the 1-hour
                // queue TTL was reached first (Q17) — the two read differently in the journal even though
                // both mean "never sent to the push service at all".
                var visitStartUtc = visitStartByRowId.GetValueOrDefault(row.Id);
                row.Status = NotificationStatus.Expired;
                row.Reason = visitStartUtc is { } start && start <= now
                    ? NotificationReason.VisitAlreadyStarted
                    : NotificationReason.PushTtlExhausted;
                expired++;
            }

            await db.SaveChangesAsync(linkedCt);

            if (readyToSend.Count > 0)
            {
                await Parallel.ForEachAsync(readyToSend, new ParallelOptions
                {
                    MaxDegreeOfParallelism = opts.MaxParallelEndpoints,
                    CancellationToken = CancellationToken.None,
                }, async (row, _) =>
                {
                    if (linkedCt.IsCancellationRequested) return; // budget exhausted — remainder stays Pending
                    var result = await ProcessRowAsync(row.Id, opts, ct);
                    results.Add(result);
                });
            }

            sent = results.Sum(r => r.Sent);
            failed = results.Sum(r => r.Failed);
            skipped = results.Sum(r => r.Skipped);

            if (linkedCt.IsCancellationRequested && !ct.IsCancellationRequested) partial = true;
        }
        catch (OperationCanceledException) when (linkedCt.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            sent = results.Sum(r => r.Sent);
            failed = results.Sum(r => r.Failed);
            skipped = results.Sum(r => r.Skipped);
            partial = true;
        }

        var summary = $"scanned {scanned}, sent {sent}, failed {failed}, expired {expired}, skipped {skipped}" +
                       (partial ? " (partial, time budget reached)" : "");
        return await FinishAsync(scanned, sent, failed, expired, skipped, summary);
    }

    private async Task<ScheduledTaskOutcome> FinishAsync(int scanned, int sent, int failed, int expired, int skipped, string summary)
    {
        var queueDepth = 0;
        try
        {
            queueDepth = await db.StaffPushNotifications.CountAsync(n => n.Status == NotificationStatus.Pending, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "staff-push-dispatch: failed to compute queue depth for the summary log");
        }

        logger.LogInformation("staff-push-dispatch pass: {Summary}. queueDepth={QueueDepth}", summary, queueDepth);
        return new ScheduledTaskOutcome(scanned, sent + failed + expired + skipped, 0, summary);
    }

    /// <summary>
    /// One row's device send, resolved in its OWN <see cref="IServiceScopeFactory"/> scope — same reason
    /// <c>NotificationDispatchTask.ProcessChannelGroupAsync</c> does: <see cref="AppDbContext"/> is not
    /// thread-safe and <see cref="Parallel.ForEachAsync{TSource}(System.Collections.Generic.IEnumerable{TSource},ParallelOptions,Func{TSource,CancellationToken,ValueTask})"/>
    /// runs many rows concurrently here.
    /// </summary>
    private async Task<(int Sent, int Failed, int Skipped)> ProcessRowAsync(
        Guid rowId, WebPushOptions.DispatchOptions opts, CancellationToken runnerCt)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IWebPushSender>();
        var scopedClock = scope.ServiceProvider.GetRequiredService<INotificationClock>();
        var encryptionKey = notificationOptions.Value.EncryptionKey ?? string.Empty;

        var row = await scopedDb.StaffPushNotifications.FirstOrDefaultAsync(n => n.Id == rowId, runnerCt);
        if (row is null || row.Status != NotificationStatus.Pending) return (0, 0, 0);

        // §105.6/R4: rights and the company setting are re-checked HERE, at send time — never trusted
        // from queue time. A row already sitting in the queue is held the instant either flips.
        var isStaff = await CompanyMembership.IsStaffAsync(scopedDb, row.CompanyId, row.UserId);
        if (!isStaff)
            return await SkipAsync(scopedDb, row, NotificationReason.MasterNoLongerInCompany, runnerCt);

        var settings = await scopedDb.CompanyNotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == row.CompanyId, runnerCt);
        var staffPushEnabled = settings?.StaffPushEnabled ?? new CompanyNotificationSettings().StaffPushEnabled;
        if (!staffPushEnabled)
            return await SkipAsync(scopedDb, row, NotificationReason.StaffPushDisabledByCompany, runnerCt);

        var subscription = row.SubscriptionId is { } subscriptionId
            ? await scopedDb.PushSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, runnerCt)
            : null;
        if (subscription is null)
            return await SkipAsync(scopedDb, row, NotificationReason.PushSubscriptionGone, runnerCt);

        // R4/§105.5: the subscription row's Id is stable across re-registration on a shared device, but
        // PushSubscriptionWriter.UpsertAsync reassigns UserId when a different account re-registers the
        // same endpoint. Never trust the FK alone — a stale row queued for user A must not fire against a
        // subscription that now belongs to user B (client's name would leak to a different master).
        if (subscription.UserId != row.UserId)
            return await SkipAsync(scopedDb, row, NotificationReason.PushSubscriptionReassigned, runnerCt);

        // In-flight marker BEFORE the network call, same convention as NotificationDispatchTask (§26.3).
        row.AttemptCount++;
        row.LastAttemptAtUtc = scopedClock.UtcNow;
        await scopedDb.SaveChangesAsync(runnerCt);

        string p256dh, auth;
        try
        {
            var aad = $"push-subscription:{subscription.Id}";
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
        // §105.8: TTL = min(3600, seconds to visit start) at queue time; here approximated as the
        // remaining time until this row's OWN ExpiresAtUtc (= min(CreatedAt+1h, visit start), §105.4) —
        // always <= the ideal value and never past it, so the push service is never told to hold a
        // message longer than this row itself would still be considered live for.
        var ttlSeconds = Math.Clamp((int)(row.ExpiresAtUtc - scopedClock.UtcNow).TotalSeconds, 1, 3600);
        var topic = $"b-{row.BookingId?.ToString() ?? row.Id.ToString()}";

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
                // §105.8: the subscription is dead — removed in THIS pass, no retry.
                scopedDb.PushSubscriptions.Remove(subscription);
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

                var backoffIndex = Math.Min(row.AttemptCount, BackoffMinutes.Length) - 1;
                row.NextAttemptAtUtc = scopedClock.UtcNow.AddMinutes(BackoffMinutes[backoffIndex]);
                await scopedDb.SaveChangesAsync(runnerCt);
                return (0, 0, 0); // stays Pending — not sent, not (yet) failed, not skipped

            default:
                throw new InvalidOperationException($"Unhandled {nameof(WebPushSendOutcome)} subtype: {outcome.GetType().Name}");
        }
    }

    private static async Task<(int Sent, int Failed, int Skipped)> SkipAsync(
        AppDbContext scopedDb, StaffPushNotification row, NotificationReason reason, CancellationToken ct)
    {
        row.Status = NotificationStatus.Skipped;
        row.Reason = reason;
        await scopedDb.SaveChangesAsync(ct);
        return (0, 0, 1);
    }

    /// <summary>Batch resolution of each candidate row's visit start time, purely so an expiring row can
    /// be journaled as <c>VisitAlreadyStarted</c> vs. <c>PushTtlExhausted</c> (§105.8) — one join for the
    /// whole batch, not one query per row.</summary>
    private async Task<Dictionary<Guid, DateTime>> ResolveVisitStartTimesAsync(
        List<StaffPushNotification> candidates, CancellationToken ct)
    {
        var result = new Dictionary<Guid, DateTime>();
        var bookingIds = candidates.Where(n => n.BookingId.HasValue).Select(n => n.BookingId!.Value).Distinct().ToList();
        if (bookingIds.Count == 0) return result;

        var bookings = await db.Bookings.Where(b => bookingIds.Contains(b.Id))
            .Select(b => new { b.Id, b.CompanyId, b.Date, b.StartTime }).ToListAsync(ct);
        if (bookings.Count == 0) return result;

        var companyIds = bookings.Select(b => b.CompanyId).Distinct().ToList();
        var timeZonesByCompany = await db.Companies.Where(c => companyIds.Contains(c.Id))
            .Select(c => new { c.Id, c.TimeZoneId }).ToDictionaryAsync(c => c.Id, c => c.TimeZoneId, ct);

        var bookingById = bookings.ToDictionary(b => b.Id);
        foreach (var row in candidates)
        {
            if (row.BookingId is not { } bookingId || !bookingById.TryGetValue(bookingId, out var booking)) continue;
            if (!timeZonesByCompany.TryGetValue(booking.CompanyId, out var timeZoneId)) continue;
            result[row.Id] = NotificationTiming.ComputeVisitStartUtc(booking.Date, booking.StartTime, timeZoneId);
        }

        return result;
    }

    private static string? Truncate(string? text, int maxLength = 300) =>
        string.IsNullOrEmpty(text) || text.Length <= maxLength ? text : text[..maxLength];
}
