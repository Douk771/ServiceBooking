using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// The dispatcher (ARCHITECTURE_CYCLE4.md §26 — the main architectural question of the whole cycle).
/// One pass: select a batch by the dispatch partial index, classify it against gate/timing rules with a
/// single <c>SaveChanges</c>, then send — grouped by channel, bounded parallelism across channels,
/// strictly sequential WITHIN a channel with a 5-15s pause between sends. Registered once in
/// <c>Program.cs</c>, exactly like <c>PhotoRetentionCleanupTask</c>; <see cref="ScheduledTaskRunner"/>
/// itself needs no changes for this (§21 p.2).
/// </summary>
public sealed class NotificationDispatchTask(
    AppDbContext db,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IOptions<NotificationOptions> options,
    SubscriptionResolver subscriptionResolver,
    INotificationClock clock,
    ILogger<NotificationDispatchTask> logger) : IScheduledTask
{
    public string Name => "notification-dispatch";
    public TimeSpan DefaultPeriod => TimeSpan.FromMinutes(1);

    // Row is retried at AttemptCount-1's index once (AttemptCount is incremented BEFORE this is read),
    // so a first failure (AttemptCount becomes 1) waits 1 minute, a second 5, and so on — US-28 p.9.
    private static readonly int[] BackoffMinutes = [1, 5, 15, 60, 180];

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var opts = options.Value.Dispatch;

        // §26.1: budget = min(MaxRunTime - 10s, Dispatch:BudgetSeconds) — the task ends itself before
        // ScheduledTaskRunner's own MaxRunMinutes cutoff would, so a normal early stop always looks like
        // "reached its own configured budget", never like the runner's own hard cancellation firing mid
        // SaveChanges.
        var taskOptions = ScheduledTaskOptions.For(configuration, this);
        var runnerHeadroom = taskOptions.MaxRunTime - TimeSpan.FromSeconds(10);
        var configuredBudget = TimeSpan.FromSeconds(opts.BudgetSeconds);
        var budget = runnerHeadroom < configuredBudget ? runnerHeadroom : configuredBudget;
        if (budget < TimeSpan.Zero) budget = TimeSpan.Zero;

        // Linked to the runner's own token: the budget expiring and the runner's own shutdown/MaxRunMinutes
        // both flow through the SAME token, so every await downstream (including the pause between sends)
        // honors both with one check (§26.1, §26.2).
        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadlineCts.CancelAfter(budget);
        var linkedCt = deadlineCts.Token;

        var scanned = 0;
        var expired = 0;
        var skipped = 0;
        var sent = 0;
        var failed = 0;
        var partial = false;
        var perGroupResults = new System.Collections.Concurrent.ConcurrentBag<(int Sent, int Failed)>();

        try
        {
            var now = clock.UtcNow;

            // ── Phase 1: selection ──────────────────────────────────────────────────────────────
            // Matches IX_OutboundNotifications_Dispatch exactly: Status = Pending (the filter), ordered
            // VisitStartUtc, CreatedAt (closest visit first, early exit via Take), the remaining
            // predicates applied against the index's own columns/INCLUDEs.
            var inFlightCutoff = now - TimeSpan.FromMinutes(opts.InFlightGraceMinutes);
            var candidates = await db.OutboundNotifications
                .Where(n => n.Status == NotificationStatus.Pending)
                .Where(n => n.DueAtUtc <= now)
                .Where(n => n.NextAttemptAtUtc == null || n.NextAttemptAtUtc <= now)
                .Where(n => n.LastAttemptAtUtc == null || n.LastAttemptAtUtc < inFlightCutoff)
                .OrderBy(n => n.VisitStartUtc).ThenBy(n => n.CreatedAt)
                .Take(opts.BatchSize)
                .ToListAsync(linkedCt);

            scanned = candidates.Count;
            if (candidates.Count == 0)
                return await FinishAsync(scanned, sent, failed, expired, skipped, "queue empty");

            // Invariant this batch relies on (§23.4): a Pending row's ChannelId is null ONLY for rows
            // queued with no usable channel at all, and those are marked Skipped at queue time, never
            // Pending — so every candidate here has a channel to resolve.
            await RefreshVisitStartTimesAsync(candidates, linkedCt);

            var channelIds = candidates.Where(n => n.ChannelId.HasValue).Select(n => n.ChannelId!.Value).Distinct().ToList();
            var channels = await db.NotificationChannels.Where(c => channelIds.Contains(c.Id)).ToListAsync(linkedCt);
            var channelById = channels.ToDictionary(c => c.Id);

            // Plan resolved by the CHANNEL's own billing account (§45.1) — the account whose
            // AccountSubscription pays for the option — not the assigned company's own account (a
            // channel may, in principle, be assigned to a company under a different membership than the
            // one that bought the channel). NotificationChannel doesn't carry BillingAccountId yet
            // (TODO ARCHITECTURE_CYCLE5.md §47 — a later slice of this cycle); until then, the account is
            // looked up from the channel's OwnerUserId the same way BillingAccountProvisioner does.
            var ownerIds = channels.Select(c => c.OwnerUserId).Distinct().ToList();
            var accountIdByOwner = await db.BillingAccounts
                .Where(a => ownerIds.Contains(a.OwnerUserId))
                .Select(a => new { a.OwnerUserId, a.Id })
                .ToDictionaryAsync(a => a.OwnerUserId, a => a.Id, linkedCt);
            var accountIds = accountIdByOwner.Values.Distinct();
            var plansByAccount = await subscriptionResolver.GetEffectivePlansForAccountsAsync(accountIds);

            var companyIds = candidates.Select(n => n.CompanyId).Distinct().ToList();
            var settingsByCompany = await db.CompanyNotificationSettings
                .Where(s => companyIds.Contains(s.CompanyId))
                .ToDictionaryAsync(s => s.CompanyId, linkedCt);

            var phones = candidates.Select(n => n.RecipientPhone).Distinct().ToList();
            var optedOutPhones = (await db.NotificationOptOuts
                    .Where(o => phones.Contains(o.Phone))
                    .Select(o => o.Phone)
                    .ToListAsync(linkedCt))
                .ToHashSet(StringComparer.Ordinal);

            // ── Phase 2: triage (one SaveChanges for the whole batch) ──────────────────────────────
            var readyToSend = new List<OutboundNotification>();
            foreach (var row in candidates)
            {
                if (NotificationTiming.IsExpired(row.VisitStartUtc, now))
                {
                    row.Status = NotificationStatus.Expired;
                    row.Reason = NotificationReason.VisitAlreadyStarted;
                    expired++;
                    continue;
                }

                var channel = row.ChannelId.HasValue && channelById.TryGetValue(row.ChannelId.Value, out var found) ? found : null;
                var plan = channel is not null && accountIdByOwner.TryGetValue(channel.OwnerUserId, out var accountId)
                    && plansByAccount.TryGetValue(accountId, out var resolvedPlan)
                    ? resolvedPlan
                    : EffectivePlan.Free;
                settingsByCompany.TryGetValue(row.CompanyId, out var settings);
                var optedOut = optedOutPhones.Contains(row.RecipientPhone);
                // TODO(ARCHITECTURE_CYCLE5.md §47): today's channel-level payment state, until the
                // account-level funding rule (paid N vs configured M) replaces it in a later slice.
                var channelIsFunded = channel is not null && ChannelPaymentState.Of(channel, now) == ChannelPaymentStatus.Paid;

                var gate = NotificationGate.Evaluate(
                    plan, row.Type, companyHasAssignment: channel is not null, channel, settings, optedOut, now, row.VisitStartUtc,
                    channelIsFunded);

                if (gate.Outcome == NotificationGateOutcome.Blocked)
                {
                    row.Status = NotificationStatus.Skipped;
                    row.Reason = gate.Reason;
                    skipped++;
                    continue;
                }

                // §30.2: an unconnected channel HOLDS the row rather than discarding it — Gate
                // deliberately does not check connectivity, that is this task's own job.
                if (channel is null || channel.State != ChannelState.Connected)
                    continue;

                readyToSend.Add(row);
            }

            await db.SaveChangesAsync(linkedCt);

            // ── Phase 3: send, grouped by channel, bounded cross-channel parallelism ───────────────
            var groups = readyToSend
                .GroupBy(r => r.ChannelId!.Value)
                .Select(g => (ChannelId: g.Key, RowIds: g.Select(r => r.Id).ToList()))
                .ToList();

            if (groups.Count > 0)
            {
                // B5/I7: the group action itself must not observe linkedCt as ITS cancellation token — if
                // it did, `Parallel.ForEachAsync` would throw the group out of the results collection the
                // instant the budget expired mid-send, and a message the provider had already accepted
                // would never get recorded (§26.1: budget must not cancel a send already in flight, only
                // gate whether a NEW row starts). `ct` (this pass's own runner/shutdown token, NOT linked
                // to the budget deadline) is threaded through for the parts that must never be budget-
                // cancelled; `linkedCt` is threaded through separately, only for the loop-top "start a new
                // row?" check and the inter-row pause.
                await Parallel.ForEachAsync(groups, new ParallelOptions
                {
                    MaxDegreeOfParallelism = opts.MaxParallelChannels,
                    CancellationToken = CancellationToken.None,
                }, async (group, _) =>
                {
                    var result = await ProcessChannelGroupAsync(group.ChannelId, group.RowIds, opts, budgetCt: linkedCt, runnerCt: ct);
                    perGroupResults.Add(result);
                });
            }

            // I7: summed unconditionally, AFTER the loop. A group interrupted mid-pause by the budget now
            // ALSO reaches this line with a normal return (ProcessChannelGroupAsync catches its own
            // budget cancellation around the pause and breaks its loop instead of throwing) — so every
            // group's contribution, including the one the budget cut off, is in perGroupResults by the
            // time this runs. Before that fix, an uncaught OperationCanceledException from the pause
            // would unwind out of ProcessChannelGroupAsync WITHOUT reaching `perGroupResults.Add(result)`
            // above, silently dropping exactly the group that had the most reason to be counted (the one
            // that was actively sending when the budget ran out) — every OTHER group still summed
            // correctly, so this was never a "sent 0" bug, just a quietly short one.
            sent = perGroupResults.Sum(r => r.Sent);
            failed = perGroupResults.Sum(r => r.Failed);

            if (linkedCt.IsCancellationRequested && !ct.IsCancellationRequested) partial = true;
        }
        catch (OperationCanceledException) when (linkedCt.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Hit our OWN time budget (not the runner's own shutdown/MaxRunMinutes, and not a host
            // shutdown) — §26.2: this is a normal outcome for a queue with more due work than fits in one
            // pass. Everything already committed (the phase-2 SaveChanges, and every already-finished
            // channel group's short transactions) stands; the remainder stays Pending for the next pass.
            // Summarized here too (rather than left at zero) for the same reason as the try block above —
            // selection-phase (phases 1-2) cancellation still leaves per-group results, if any ran.
            sent = perGroupResults.Sum(r => r.Sent);
            failed = perGroupResults.Sum(r => r.Failed);
            partial = true;
        }

        var summary = $"scanned {scanned}, sent {sent}, failed {failed}, expired {expired}, skipped {skipped}" +
                       (partial ? " (partial, time budget reached)" : "");
        return await FinishAsync(scanned, sent, failed, expired, skipped, summary);
    }

    /// <summary>
    /// Logs the pass (§37: sent/deferred/failed/expired/skipped, plus queue depth and the oldest
    /// remaining Pending row's age) and returns the outcome the runner persists. The two diagnostic
    /// queries use <see cref="CancellationToken.None"/> deliberately — a pass that already spent its
    /// whole budget sending messages must still get to report honest numbers, not throw while merely
    /// measuring what's left.
    /// </summary>
    private async Task<ScheduledTaskOutcome> FinishAsync(int scanned, int sent, int failed, int expired, int skipped, string summary)
    {
        var queueDepth = 0;
        DateTime? oldestPendingCreatedAt = null;
        try
        {
            queueDepth = await db.OutboundNotifications.CountAsync(n => n.Status == NotificationStatus.Pending, CancellationToken.None);
            oldestPendingCreatedAt = await db.OutboundNotifications
                .Where(n => n.Status == NotificationStatus.Pending)
                .OrderBy(n => n.CreatedAt)
                .Select(n => (DateTime?)n.CreatedAt)
                .FirstOrDefaultAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "notification-dispatch: failed to compute queue depth/oldest-pending for the summary log");
        }

        var oldestPendingAgeMinutes = oldestPendingCreatedAt is { } createdAt
            ? (int)(clock.UtcNow - createdAt).TotalMinutes
            : (int?)null;

        logger.LogInformation(
            "notification-dispatch pass: {Summary}. queueDepth={QueueDepth} oldestPendingAgeMinutes={OldestAgeMinutes}",
            summary, queueDepth, oldestPendingAgeMinutes);

        return new ScheduledTaskOutcome(scanned, sent + failed + expired + skipped, 0, summary);
    }

    /// <summary>
    /// §23.4's safety net: a batch-update of VisitStartUtc from each row's actual current
    /// booking/company state, one join by primary key for the whole batch — not per row — so a
    /// reschedule that raced with this pass is still caught even if the reschedule endpoint's own
    /// VisitStartUtc update (§25.3) somehow didn't land on this exact row in time.
    /// </summary>
    private async Task RefreshVisitStartTimesAsync(List<OutboundNotification> candidates, CancellationToken ct)
    {
        var bookingIds = candidates.Where(n => n.BookingId.HasValue).Select(n => n.BookingId!.Value).Distinct().ToList();
        if (bookingIds.Count == 0) return;

        var bookings = await db.Bookings
            .Where(b => bookingIds.Contains(b.Id))
            .Select(b => new { b.Id, b.CompanyId, b.Date, b.StartTime })
            .ToListAsync(ct);
        if (bookings.Count == 0) return;

        var companyIds = bookings.Select(b => b.CompanyId).Distinct().ToList();
        var companyTimeZones = await db.Companies
            .Where(c => companyIds.Contains(c.Id))
            .Select(c => new { c.Id, c.TimeZoneId })
            .ToDictionaryAsync(c => c.Id, c => c.TimeZoneId, ct);

        var bookingById = bookings.ToDictionary(b => b.Id);
        foreach (var row in candidates)
        {
            if (row.BookingId is not { } bookingId || !bookingById.TryGetValue(bookingId, out var booking)) continue;
            if (!companyTimeZones.TryGetValue(booking.CompanyId, out var timeZoneId)) continue;

            row.VisitStartUtc = NotificationTiming.ComputeVisitStartUtc(booking.Date, booking.StartTime, timeZoneId);
        }
    }

    /// <summary>
    /// One channel's slice of the batch, sent strictly sequentially with a pause in between — the whole
    /// reason this runs in its OWN <see cref="IServiceScopeFactory"/> scope (own <see cref="AppDbContext"/>,
    /// own copies of every scoped service) is that <see cref="AppDbContext"/> is not thread-safe and
    /// <see cref="Parallel.ForEachAsync{TSource}(IAsyncEnumerable{TSource},ParallelOptions,Func{TSource,CancellationToken,ValueTask})"/>
    /// runs multiple channel groups concurrently (§26.2) — sharing <c>db</c> (the task's own constructor-
    /// injected context, used only for phases 1-2 on the caller's single thread) across groups would be
    /// exactly the most likely implementation bug this architecture calls out by name.
    /// </summary>
    private async Task<(int Sent, int Failed)> ProcessChannelGroupAsync(
        Guid channelId, IReadOnlyList<Guid> rowIds, NotificationOptions.DispatchOptions opts,
        CancellationToken budgetCt, CancellationToken runnerCt)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transport = scope.ServiceProvider.GetRequiredService<INotificationTransport>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IChannelProvisioning>();
        var pause = scope.ServiceProvider.GetRequiredService<IPauseGenerator>();
        var delay = scope.ServiceProvider.GetRequiredService<IDispatchDelay>();
        var scopedClock = scope.ServiceProvider.GetRequiredService<INotificationClock>();
        var encryptionKey = scope.ServiceProvider.GetRequiredService<IOptions<NotificationOptions>>().Value.EncryptionKey;

        // B5: the channel lookup itself, and every subsequent read/write below, is done with `runnerCt`
        // (host shutdown only) — `budgetCt` (this pass's own time budget, §26.1) is consulted in exactly
        // two places: the loop-top "start a new row?" check, and the inter-row pause. Once a row has been
        // decided on, nothing about sending it or recording its outcome is allowed to be budget-cancelled.
        var channel = await scopedDb.NotificationChannels.FirstOrDefaultAsync(c => c.Id == channelId, runnerCt);
        if (channel is null) return (0, 0); // deleted/reassigned between phase 1 and now — nothing to do here

        var sentCount = 0;
        var failedCount = 0;

        for (var i = 0; i < rowIds.Count; i++)
        {
            if (budgetCt.IsCancellationRequested) break; // budget exhausted mid-channel — remainder stays Pending

            var row = await scopedDb.OutboundNotifications.FirstOrDefaultAsync(n => n.Id == rowIds[i], runnerCt);
            // Defensive only: another pass/process could in principle have already taken this row (§26.3
            // tolerates at most one duplicate on an aborted process, never more, and this check costs
            // nothing on the ordinary path).
            if (row is null || row.Status != NotificationStatus.Pending) continue;

            // Transaction #1 — mark the attempt BEFORE the network call (§26.3's in-flight marker).
            row.AttemptCount++;
            row.LastAttemptAtUtc = scopedClock.UtcNow;
            await scopedDb.SaveChangesAsync(runnerCt);

            ChannelCredentials credentials;
            try
            {
                var token = SecretProtector.Decrypt(channel.ProviderSecretCiphertext ?? string.Empty, encryptionKey ?? string.Empty, channel.Id);
                credentials = new ChannelCredentials(channel.ProviderInstanceId ?? string.Empty, token);
            }
            catch (Exception ex) when (ex is ChannelSecretUnavailableException or ArgumentException)
            {
                // I1: an unreadable secret (rotated/lost key, corrupted ciphertext) gets ITS OWN reason —
                // ChannelStateReason.SecretUnavailable — rather than being folded into
                // ProviderReportsUnauthorized. The two need to read differently to an owner: "your number
                // was logged out at WhatsApp" is actionable by the owner; "our encryption key changed
                // under you" is a platform incident. And unlike ProviderReportsUnauthorized, leaving
                // ProviderInstanceId set here would leave Connect permanently 409ing (it refuses to start
                // while an instance id is on file) with no poll ever able to clear it (nothing can prove
                // the instance's own state without the very token that's unreadable) — so the instance is
                // decommissioned outright, §30.4 style: database first (orphan id, blank the channel's own
                // credentials, transition), then a best-effort partner-token delete (no channel token
                // needed for that call), with ChannelHealthTask's orphan sweep as the retry path if this
                // attempt itself fails. The row stays Pending; this channel's group ends immediately.
                var instanceId = channel.ProviderInstanceId;
                channel.OrphanedInstanceId = instanceId;
                channel.ProviderInstanceId = null;
                channel.ProviderSecretCiphertext = null;
                channel.ProviderSecretKeyId = null;
                ChannelStateTransition.Apply(scopedDb, channel, ChannelState.NeedsReconnect,
                    ChannelStateReason.SecretUnavailable, "channel secret unavailable", scopedClock.UtcNow);
                await scopedDb.SaveChangesAsync(runnerCt);

                if (instanceId is not null)
                {
                    try
                    {
                        var deletion = await provisioning.DeleteInstanceAsync(instanceId, runnerCt);
                        if (deletion.Success)
                        {
                            channel.OrphanedInstanceId = null;
                            await scopedDb.SaveChangesAsync(runnerCt);
                        }
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogWarning(deleteEx,
                            "notification-dispatch: best-effort instance deletion after secret-unavailable failed, left orphaned for retry: channelId={ChannelId}",
                            channel.Id);
                    }
                }

                return (sentCount, failedCount);
            }

            var outcome = await transport.SendAsync(credentials, row.RecipientPhone, row.Body, runnerCt);

            switch (outcome)
            {
                case SendOutcome.Sent sentOutcome:
                    row.Status = NotificationStatus.Sent;
                    row.SentAtUtc = scopedClock.UtcNow;
                    row.ProviderMessageId = sentOutcome.ProviderMessageId;
                    row.Reason = NotificationReason.Delivered;
                    channel.ConsecutiveSendFailures = 0;
                    sentCount++;
                    break;

                case SendOutcome.TransientFailure transientFailure:
                    if (row.AttemptCount >= opts.MaxAttempts)
                    {
                        row.Status = NotificationStatus.Failed;
                        row.Reason = NotificationReason.RetriesExhausted;
                        row.ReasonDetail = Truncate(transientFailure.Detail);
                        failedCount++;
                    }
                    else
                    {
                        var backoffIndex = Math.Min(row.AttemptCount, BackoffMinutes.Length) - 1;
                        row.NextAttemptAtUtc = scopedClock.UtcNow.AddMinutes(BackoffMinutes[backoffIndex]);
                    }

                    channel.ConsecutiveSendFailures++;
                    if (channel.ConsecutiveSendFailures >= options.Value.ConsecutiveFailureThreshold)
                        ChannelStateTransition.Apply(scopedDb, channel, ChannelState.Disconnected,
                            ChannelStateReason.ConsecutiveSendFailuresExceeded, transientFailure.Detail, scopedClock.UtcNow);
                    break;

                case SendOutcome.PermanentlyRejected rejected:
                    // Terminal, no retry — and the CHANNEL is not touched: "recipient has no WhatsApp" is
                    // a property of the recipient, not a sign the channel itself is broken (§26.4).
                    row.Status = NotificationStatus.Failed;
                    row.Reason = rejected.Reason;
                    row.ReasonDetail = Truncate(rejected.Detail);
                    failedCount++;
                    break;

                case SendOutcome.ChannelInvalid invalid:
                    // Row stays Pending (US-28 p.9) — the problem is the channel, so the message may
                    // still go out once it's reconnected. This channel's group stops immediately: every
                    // remaining row would fail identically for the same reason (§26.4).
                    ChannelStateTransition.Apply(scopedDb, channel, ChannelState.Disconnected,
                        ChannelStateReason.ProviderReportsUnauthorized, invalid.Detail, scopedClock.UtcNow);
                    await scopedDb.SaveChangesAsync(runnerCt);
                    return (sentCount, failedCount);

                default:
                    throw new InvalidOperationException($"Unhandled {nameof(SendOutcome)} subtype: {outcome.GetType().Name}");
            }

            // Transaction #2 — the outcome, including the channel's own counters/state. B5: never
            // budget-cancelled — a send the provider already accepted must always get recorded.
            await scopedDb.SaveChangesAsync(runnerCt);

            // B5: the ONE place besides the loop-top check where the budget is actually consulted
            // (§26.1) — between rows, during the antiban pause, never mid-send. Caught locally, not left
            // to propagate: an unhandled OperationCanceledException here would unwind straight out of
            // this method WITHOUT returning (sentCount, failedCount) — the caller's
            // `perGroupResults.Add(result)` (only reached on a normal return) never runs, so THIS group's
            // already-committed sends/failures would silently vanish from the pass's own summary even
            // though every row was correctly recorded in the database. Caught, not avoided: the budget
            // must still end the loop exactly here, same as the loop-top check does.
            var isLastInGroup = i == rowIds.Count - 1;
            if (!isLastInGroup)
            {
                try
                {
                    await delay.DelayAsync(pause.Next(opts.PauseMinMs, opts.PauseMaxMs), budgetCt);
                }
                catch (OperationCanceledException) when (budgetCt.IsCancellationRequested)
                {
                    break; // budget exhausted mid-pause — remainder stays Pending, same as the loop-top check
                }
            }
        }

        return (sentCount, failedCount);
    }

    private static string? Truncate(string? text, int maxLength = 300) =>
        string.IsNullOrEmpty(text) || text.Length <= maxLength ? text : text[..maxLength];
}
