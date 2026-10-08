using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.PhoneVerification.Max;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.API.Services.StaffMax;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §499.4 — sends the queued <see cref="StaffMaxMessage"/> rows to MAX (task <c>staff-max-dispatch</c>, lane "realtime", every
/// 5 s). The shape of <see cref="StaffPushDispatchTask"/>: a batch by the partial index, overdue rows expire, the rest go in bounded parallel, each in its
/// own scope. Before EVERY send the row is re-checked — the platform switch, the shop's switch, and that a still-active binding of this chat belongs to a
/// person who is staff of the shop NOW (so a removed employee, "Отключить" or a stopped bot silence the messages that were already queued). A failure of
/// MAX only changes the row; it never touches the order.
/// </summary>
public sealed class StaffMaxDispatchTask(
    AppDbContext db,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IOptions<StaffMaxOptions> options,
    IOptions<NotificationOptions> notificationOptions,
    StaffMaxAvailability availability,
    INotificationClock clock,
    ShowcaseOutboundGuard showcaseGuard,
    ILogger<StaffMaxDispatchTask> logger) : IScheduledTask
{
    public string Name => "staff-max-dispatch";
    public TimeSpan DefaultPeriod => TimeSpan.FromSeconds(5);

    /// <summary>Retry after a transient failure: 1 / 5 / 15 minutes (the attempt count picks the step).</summary>
    private static readonly int[] BackoffMinutes = [1, 5, 15];

    /// <summary>A row the platform rate-limited waits this long; the attempt does not count.</summary>
    private static readonly TimeSpan RateLimitedDelay = TimeSpan.FromSeconds(10);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var taskOptions = ScheduledTaskOptions.For(configuration, this);
        var budget = taskOptions.MaxRunTime - TimeSpan.FromSeconds(5);
        if (budget < TimeSpan.FromSeconds(1)) budget = TimeSpan.FromSeconds(1);

        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadlineCts.CancelAfter(budget);
        var linkedCt = deadlineCts.Token;

        var scanned = 0;
        var expired = 0;
        var partial = false;
        var results = new ConcurrentBag<Result>();

        // The dispatcher's own ceiling (§499.3): the phone confirmation keeps most of MAX's global budget.
        var perSecond = Math.Max(1, opts.MaxMessagesPerSecond);
        using var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = perSecond,
            TokensPerPeriod = perSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,
            QueueLimit = Math.Max(1, opts.BatchSize),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });

        try
        {
            var now = clock.UtcNow;
            var inFlightCutoff = now - TimeSpan.FromMinutes(opts.InFlightGraceMinutes);

            // A row with an explicit retry time is governed by it; one without (never tried, or tried and left in flight) is held back by the
            // in-flight grace, so an overlapping or crashed pass cannot double-send. The in-flight marker clears NextAttemptAtUtc for that reason.
            var candidates = await db.StaffMaxMessages
                .Where(m => m.Status == NotificationStatus.Pending)
                .Where(m => m.NextAttemptAtUtc != null
                    ? m.NextAttemptAtUtc <= now
                    : m.LastAttemptAtUtc == null || m.LastAttemptAtUtc < inFlightCutoff)
                .OrderBy(m => m.ExpiresAtUtc).ThenBy(m => m.CreatedAt)
                .Take(Math.Max(1, opts.BatchSize))
                .ToListAsync(linkedCt);

            scanned = candidates.Count;
            if (scanned == 0) return await FinishAsync(scanned, results, expired, "queue empty");

            // ARCHITECTURE_CYCLE35.md §35.6.2 — safety net of the demo, BEFORE the platform-switch check of ProcessRowAsync, so that the journal says ShowcaseSuppressed and
            // not StaffMaxPlatformDisabled: a MAX row of a showcase shop (or any row on the demo stand) is never sent.
            var showcaseCompanyIds = await showcaseGuard.SuppressedCompanyIdsAsync(
                candidates.Select(m => m.CompanyId).Distinct().ToList(), linkedCt);

            var ready = new List<StaffMaxMessage>();
            foreach (var row in candidates)
            {
                if (showcaseCompanyIds.Contains(row.CompanyId))
                {
                    row.Status = NotificationStatus.Skipped;
                    row.Reason = NotificationReason.ShowcaseSuppressed;
                    results.Add(Result.Skipped);
                    continue;
                }

                if (row.ExpiresAtUtc >= now) { ready.Add(row); continue; }
                row.Status = NotificationStatus.Expired;
                row.Reason = NotificationReason.PushTtlExhausted;
                expired++;
            }
            await db.SaveChangesAsync(linkedCt);

            if (ready.Count > 0)
                await Parallel.ForEachAsync(ready, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, opts.MaxParallel),
                    CancellationToken = CancellationToken.None,
                }, async (row, _) =>
                {
                    if (linkedCt.IsCancellationRequested) return; // budget exhausted — the rest stays Pending
                    results.Add(await ProcessRowAsync(row.Id, limiter, opts, ct));
                });

            if (linkedCt.IsCancellationRequested && !ct.IsCancellationRequested) partial = true;
        }
        catch (OperationCanceledException) when (linkedCt.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            partial = true;
        }

        var summary = $"scanned {scanned}, sent {results.Count(r => r == Result.Sent)}, failed {results.Count(r => r == Result.Failed)}, " +
                      $"expired {expired}, skipped {results.Count(r => r == Result.Skipped)}" + (partial ? " (partial, time budget reached)" : "");
        return await FinishAsync(scanned, results, expired, summary);
    }

    private enum Result { Sent, Failed, Skipped, Pending }

    private async Task<ScheduledTaskOutcome> FinishAsync(int scanned, IReadOnlyCollection<Result> results, int expired, string summary)
    {
        var queueDepth = 0;
        try { queueDepth = await db.StaffMaxMessages.CountAsync(m => m.Status == NotificationStatus.Pending, CancellationToken.None); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "staff-max-dispatch: failed to compute the queue depth for the summary log");
        }
        logger.LogInformation("staff-max-dispatch pass: {Summary}. queueDepth={QueueDepth}", summary, queueDepth);
        return new ScheduledTaskOutcome(scanned, results.Count(r => r != Result.Pending) + expired, 0, summary);
    }

    private async Task<Result> ProcessRowAsync(Guid rowId, TokenBucketRateLimiter limiter, StaffMaxOptions opts, CancellationToken runnerCt)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messenger = scope.ServiceProvider.GetRequiredService<IMaxBotMessenger>();
        var scopedClock = scope.ServiceProvider.GetRequiredService<INotificationClock>();

        var row = await scopedDb.StaffMaxMessages.FirstOrDefaultAsync(m => m.Id == rowId, runnerCt);
        if (row is null || row.Status != NotificationStatus.Pending) return Result.Pending;

        // 1. The platform switch — checked again at send time, so switching it off holds what is already queued.
        if (!availability.Enabled) return await SkipAsync(scopedDb, row, NotificationReason.StaffMaxPlatformDisabled, runnerCt);

        // 1a. A deactivated shop sends nothing to its staff.
        var shopActive = await scopedDb.Companies.AsNoTracking().Where(c => c.Id == row.CompanyId).Select(c => (bool?)c.IsActive).FirstOrDefaultAsync(runnerCt);
        if (shopActive != true) return await SkipAsync(scopedDb, row, NotificationReason.StaffMaxShopInactive, runnerCt);

        // 2. The shop's own switch, for order messages (the owner's limit warning is about the account — like push, §459.6).
        Order? order = null;
        if (row.Type != NotificationType.OwnerOrderLimitWarning)
        {
            var shopEnabled = row.StayBookingId != null
                ? await scopedDb.StaysSettings.AsNoTracking().Where(s => s.CompanyId == row.CompanyId).Select(s => (bool?)s.StaffMaxEnabled).FirstOrDefaultAsync(runnerCt) ?? true
                : await scopedDb.ShopSettings.AsNoTracking().Where(s => s.CompanyId == row.CompanyId)
                    .Select(s => (bool?)s.StaffMaxEnabled).FirstOrDefaultAsync(runnerCt) ?? true;
            if (!shopEnabled) return await SkipAsync(scopedDb, row, NotificationReason.StaffMaxDisabledByShop, runnerCt);
            if (row.OrderId is { } orderId)
                order = await scopedDb.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, runnerCt);
        }

        // 3. A still-active binding of this chat that belongs to a CURRENT recipient.
        var links = await scopedDb.StaffMaxLinks
            .Where(l => l.ChatKey == row.ChatKey && l.Status == StaffMaxLinkStatus.Active && l.ChatIdCiphertext != null)
            .ToListAsync(runnerCt);
        StaffMaxLink? link = null;
        string? ownerUserId = row.Type == NotificationType.OwnerOrderLimitWarning ? await OwnerOfAsync(scopedDb, row.CompanyId, runnerCt) : null;
        foreach (var candidate in links)
        {
            var recipient = row.Type == NotificationType.OwnerOrderLimitWarning
                ? ownerUserId == candidate.UserId
                : row.StayBookingId != null
                    ? await Stays.StayStaffRecipients.IsRecipientAsync(scopedDb, row.CompanyId, candidate.UserId, runnerCt)
                    : candidate.UserId != order?.CustomerUserId && await CompanyMembership.IsStaffAsync(scopedDb, row.CompanyId, candidate.UserId);
            if (!recipient) continue;
            link = candidate;
            break;
        }
        if (link is null) return await SkipAsync(scopedDb, row, NotificationReason.StaffMaxNoRecipient, runnerCt);

        // In-flight marker BEFORE the network call (the push rule); it also clears the previous retry time.
        row.AttemptCount++;
        row.LastAttemptAtUtc = scopedClock.UtcNow;
        row.NextAttemptAtUtc = null;
        await scopedDb.SaveChangesAsync(runnerCt);

        string chatId;
        try
        {
            chatId = SecretProtector.Decrypt(link.ChatIdCiphertext!, notificationOptions.Value.EncryptionKey ?? string.Empty, $"staff-max-link:{link.Id}");
        }
        catch (Exception ex) when (ex is ChannelSecretUnavailableException or ArgumentException)
        {
            row.Status = NotificationStatus.Failed;
            row.Reason = NotificationReason.PushAuthRejected;
            row.ReasonDetail = Truncate(ex.Message);
            await scopedDb.SaveChangesAsync(runnerCt);
            return Result.Failed;
        }

        // The dispatcher's own ceiling: wait for a token; without one the row simply tries again shortly.
        using var lease = await limiter.AcquireAsync(1, runnerCt);
        MaxSendOutcome outcome = lease.IsAcquired ? await messenger.SendAsync(chatId, row.Text, runnerCt) : new MaxSendOutcome.RateLimited();

        switch (outcome)
        {
            case MaxSendOutcome.Sent:
                row.Status = NotificationStatus.Sent;
                row.SentAtUtc = scopedClock.UtcNow;
                row.Reason = NotificationReason.Delivered;
                link.LastSuccessAtUtc = scopedClock.UtcNow;
                link.ConsecutiveFailures = 0;
                await scopedDb.SaveChangesAsync(runnerCt);
                return Result.Sent;

            case MaxSendOutcome.ChatUnavailable unavailable:
                // The bot was stopped or blocked: every binding of this chat is switched off and forgets the chat id (§498.4).
                row.Status = NotificationStatus.Skipped;
                row.Reason = NotificationReason.StaffMaxChatUnavailable;
                row.ReasonDetail = $"HTTP {unavailable.StatusCode}";
                await scopedDb.SaveChangesAsync(runnerCt);
                await StaffMaxStartHandler.MarkChatStoppedAsync(scopedDb, row.ChatKey, scopedClock.UtcNow, runnerCt);
                return Result.Skipped;

            case MaxSendOutcome.RateLimited:
                // Stays pending, the attempt does not count.
                row.AttemptCount = Math.Max(0, row.AttemptCount - 1);
                row.Reason = NotificationReason.StaffMaxRateLimited;
                row.NextAttemptAtUtc = scopedClock.UtcNow.Add(RateLimitedDelay);
                await scopedDb.SaveChangesAsync(runnerCt);
                return Result.Pending;

            case MaxSendOutcome.Transient transient:
                link.ConsecutiveFailures++;
                if (row.AttemptCount >= opts.MaxAttempts)
                {
                    row.Status = NotificationStatus.Failed;
                    row.Reason = NotificationReason.RetriesExhausted;
                    row.ReasonDetail = Truncate(transient.Detail);
                    await scopedDb.SaveChangesAsync(runnerCt);
                    return Result.Failed;
                }
                row.ReasonDetail = Truncate(transient.Detail);
                row.NextAttemptAtUtc = scopedClock.UtcNow.AddMinutes(BackoffMinutes[Math.Clamp(row.AttemptCount, 1, BackoffMinutes.Length) - 1]);
                await scopedDb.SaveChangesAsync(runnerCt);
                return Result.Pending;

            case MaxSendOutcome.Rejected rejected:
                // 400/401: the bot's configuration or the message shape — no retry, and an operator must see it.
                logger.LogError("staff-max-dispatch: MAX rejected a message (HTTP {StatusCode}) — check the bot's configuration", rejected.StatusCode);
                row.Status = NotificationStatus.Failed;
                row.Reason = NotificationReason.PushAuthRejected;
                row.ReasonDetail = Truncate(rejected.Detail);
                await scopedDb.SaveChangesAsync(runnerCt);
                return Result.Failed;

            default:
                throw new InvalidOperationException($"Unhandled {nameof(MaxSendOutcome)} subtype: {outcome.GetType().Name}");
        }
    }

    /// <summary>The owner of the billing account of the shop (the addressee of the limit warning).</summary>
    private static async Task<string?> OwnerOfAsync(AppDbContext scopedDb, Guid shopId, CancellationToken ct) =>
        await scopedDb.Companies.AsNoTracking().Where(c => c.Id == shopId)
            .Join(scopedDb.BillingAccounts, c => c.BillingAccountId, a => (Guid?)a.Id, (c, a) => a.OwnerUserId)
            .FirstOrDefaultAsync(ct);

    private static async Task<Result> SkipAsync(AppDbContext scopedDb, StaffMaxMessage row, NotificationReason reason, CancellationToken ct)
    {
        row.Status = NotificationStatus.Skipped;
        row.Reason = reason;
        await scopedDb.SaveChangesAsync(ct);
        return Result.Skipped;
    }

    private static string? Truncate(string? text, int maxLength = 300) =>
        string.IsNullOrEmpty(text) || text.Length <= maxLength ? text : text[..maxLength];
}
