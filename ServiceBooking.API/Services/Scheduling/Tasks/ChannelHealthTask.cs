using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// Channel state polling, idle detection/instance deletion, unauthorized-instance timeout cleanup, and
/// orphaned-instance-deletion retries — four jobs, each one batched query, one pass every 15 minutes
/// (ARCHITECTURE_CYCLE4.md §30). The second and last of this cycle's two new <see cref="IScheduledTask"/>
/// implementations; <see cref="ScheduledTaskRunner"/> itself needs no changes (§21 p.2).
/// </summary>
public sealed class ChannelHealthTask(
    AppDbContext db,
    IChannelProvisioningRegistry provisioningRegistry,
    IOptions<NotificationOptions> options,
    PlatformSettings platformSettings,
    INotificationClock clock,
    ILogger<ChannelHealthTask> logger) : IScheduledTask
{
    public string Name => "channel-health";
    public TimeSpan DefaultPeriod => TimeSpan.FromMinutes(15);

    private static readonly ChannelState[] PollableStates =
        [ChannelState.Connecting, ChannelState.Connected, ChannelState.Disconnected];

    // A channel is re-polled at most once per pass period — polling more often than the task itself runs
    // buys nothing.
    private static readonly TimeSpan StateCheckInterval = TimeSpan.FromMinutes(15);

    // US-62 p.3: while a channel stays down, "still disrupted" is logged (Warning → GlitchTip, §31.2) at
    // most this often — not once per 15-minute pass for however long an outage lasts.
    private static readonly TimeSpan DisruptionNotificationCooldown = TimeSpan.FromHours(24);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;

        // §30.4: "every ChannelHealthTask pass starts by finishing off such tails" — a crash between
        // steps 1 and 3 of a PRIOR deletion leaves OrphanedInstanceId set, and this must be retried
        // before anything else in this pass touches the same channel.
        var orphansCleaned = await RetryOrphanDeletionsAsync(ct);

        var (polled, transitioned) = await PollStatesAsync(now, ct);
        var timedOut = await CleanupUnauthorizedTimeoutsAsync(now, ct);
        var (idleWarned, idleDeleted) = await RecomputeIdleAsync(now, ct);

        var scanned = polled;
        var affected = transitioned + timedOut + idleWarned + idleDeleted + orphansCleaned;
        var summary = $"polled {polled} ({transitioned} transitioned), {timedOut} unauthorized instance(s) timed out, " +
                       $"{idleWarned} idle warning(s), {idleDeleted} idle instance(s) deleted, {orphansCleaned} orphan(s) cleaned up";

        logger.LogInformation("channel-health pass: {Summary}", summary);
        return new ScheduledTaskOutcome(scanned, affected, 0, summary);
    }

    /// <summary>§30.1 — network calls (bounded parallelism, reusing Dispatch:MaxParallelChannels so this
    /// task never opens more concurrent connections to the provider than the dispatcher itself does) run
    /// first and touch no database state; every actual DB write happens afterward, single-threaded, in
    /// one pass over the same list plus one <c>SaveChanges</c> — sidestepping the "own scope per worker"
    /// concern entirely (there is nothing to write concurrently, because nothing is written while the
    /// calls are in flight).</summary>
    private async Task<(int Polled, int Transitioned)> PollStatesAsync(DateTime now, CancellationToken ct)
    {
        var staleCutoff = now - StateCheckInterval;
        var channels = await db.NotificationChannels
            .Where(c => PollableStates.Contains(c.State))
            .Where(c => c.LastStateCheckAtUtc == null || c.LastStateCheckAtUtc < staleCutoff)
            .ToListAsync(ct);
        if (channels.Count == 0) return (0, 0);

        var encryptionKey = options.Value.EncryptionKey ?? string.Empty;
        var maxParallel = Math.Max(1, options.Value.Dispatch.MaxParallelChannels);
        var polledStates = new System.Collections.Concurrent.ConcurrentDictionary<Guid, ProviderChannelState?>();
        // I1: kept separate from polledStates — a secret-unavailable channel is NOT a provider answer at
        // all (ChannelStateMapper has no vocabulary for it), so it must never be routed through the
        // mapper's Unknown case. Unknown means "don't regress, stay whatever we were" — right for a
        // transient network hiccup, wrong here: this channel can never be usefully polled again until it
        // is decommissioned and reconnected, so silently doing nothing every pass forever would leave an
        // owner paying for an instance that can never come back on its own.
        var secretUnavailableChannelIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        await Parallel.ForEachAsync(channels, new ParallelOptions { MaxDegreeOfParallelism = maxParallel, CancellationToken = ct },
            async (channel, innerCt) =>
            {
                if (channel.ProviderInstanceId is not { } instanceId || channel.ProviderSecretCiphertext is not { } ciphertext)
                {
                    polledStates[channel.Id] = null;
                    return;
                }

                try
                {
                    var token = SecretProtector.Decrypt(ciphertext, encryptionKey, channel.Id);
                    var state = await provisioningRegistry.For(channel.Transport).GetStateAsync(new ChannelCredentials(instanceId, token), innerCt);
                    polledStates[channel.Id] = state;
                }
                catch (Exception ex) when (ex is ChannelSecretUnavailableException or ArgumentException)
                {
                    // I1 (§24.4 point 6): unreadable secret — handled by the single-threaded loop below,
                    // not folded into a provider-state answer.
                    secretUnavailableChannelIds.Add(channel.Id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "channel-health: GetStateAsync failed for channel {ChannelId}", channel.Id);
                    polledStates[channel.Id] = null; // network failure — skip this channel this pass
                }
            });

        var transitioned = 0;
        var secretUnavailableSet = secretUnavailableChannelIds.ToHashSet();
        foreach (var channel in channels)
        {
            channel.LastStateCheckAtUtc = now;

            if (secretUnavailableSet.Contains(channel.Id))
            {
                // I1: decommission outright, §30.4 style (same procedure as an idle/unauthorized-timeout
                // deletion) — nothing at the provider is ever readable again through this channel's own
                // token, so there is nothing to gain by leaving the instance billed and un-decommissioned.
                await DeleteInstanceAsync(channel, ChannelState.NeedsReconnect, ChannelStateReason.SecretUnavailable, now, ct);
                transitioned++;
                continue;
            }

            if (!polledStates.TryGetValue(channel.Id, out var providerState) || providerState is null) continue;

            var mapping = ChannelStateMapper.Map(channel.State, providerState.Value, channel.ConnectedAtUtc != null);
            if (mapping.State != channel.State)
            {
                // B4: the mapper's own contract is "Reason is non-null whenever State changes" — defended
                // here rather than trusted blindly with `!.Value`, because an unguarded null-forgiving
                // dereference threw mid-loop on a prior bug and failed the WHOLE pass (every channel after
                // the offending one, in memory, unsaved) instead of just skipping the one bad mapping.
                if (mapping.Reason is not { } reason)
                {
                    logger.LogError(
                        "channel-health: ChannelStateMapper returned a state change with no reason, skipping this channel this pass: channelId={ChannelId} from={FromState} to={ToState}",
                        channel.Id, channel.State, mapping.State);
                }
                else
                {
                    await ChannelStateTransition.Apply(db, channel, mapping.State, reason, null, now);
                    transitioned++;
                }
            }

            if (mapping.State == ChannelState.Connected && channel.PhoneNumber is null)
                await BackfillPhoneNumberAsync(channel, encryptionKey, ct);

            // B8 / SPEC US-56 п. 6, US-63 п. 1: a banned number is deleted at the provider IMMEDIATELY,
            // not after the N-day idle grace period every other "no active company" case gets — a banned
            // instance cannot come back regardless of how soon someone reconnects it, so there is no
            // "insurance against an extra QR" reason left to keep paying for it. DeleteInstanceAsync
            // no-ops the state transition (already applied above) and just does the decommission.
            if (mapping.State == ChannelState.Blocked && channel.ProviderInstanceId is not null)
                await DeleteInstanceAsync(channel, ChannelState.Blocked, ChannelStateReason.ProviderReportsBlocked, now, ct);

            ApplyDisruptionNotificationAntiSpam(channel, mapping.State, now);
        }

        await db.SaveChangesAsync(ct);
        return (channels.Count, transitioned);
    }

    /// <summary>B4: a channel that authorized via polling (rather than the QR-scan controller path,
    /// which already records the phone number) would otherwise never get one — <c>getSettings</c> is the
    /// one call that returns it. One extra provider call per pass, only for channels that just became (or
    /// already are) Connected and still have no number on file.</summary>
    private async Task BackfillPhoneNumberAsync(NotificationChannel channel, string encryptionKey, CancellationToken ct)
    {
        if (channel.ProviderInstanceId is not { } instanceId || channel.ProviderSecretCiphertext is not { } ciphertext) return;

        try
        {
            var token = SecretProtector.Decrypt(ciphertext, encryptionKey, channel.Id);
            var phoneNumber = await provisioningRegistry.For(channel.Transport).GetPhoneNumberAsync(new ChannelCredentials(instanceId, token), ct);
            if (!string.IsNullOrEmpty(phoneNumber)) channel.PhoneNumber = phoneNumber;
        }
        catch (Exception ex) when (ex is ChannelSecretUnavailableException or ArgumentException)
        {
            logger.LogDebug(ex, "channel-health: could not decrypt channel secret to backfill phone number: channelId={ChannelId}", channel.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "channel-health: GetPhoneNumberAsync failed for channel {ChannelId}", channel.Id);
        }
    }

    /// <summary>US-62 p.3: a Warning-level "still disrupted" log (→ GlitchTip, §31.2) at most once every
    /// 24 hours for as long as a channel stays down, reset the moment it recovers so the NEXT disruption
    /// notifies promptly rather than inheriting a stale cooldown.</summary>
    private void ApplyDisruptionNotificationAntiSpam(NotificationChannel channel, ChannelState currentState, DateTime now)
    {
        if (currentState is ChannelState.Disconnected or ChannelState.Blocked)
        {
            if (channel.DisruptionNotifiedAtUtc is null || now - channel.DisruptionNotifiedAtUtc > DisruptionNotificationCooldown)
            {
                logger.LogWarning(
                    "{Transport} channel disrupted: channelId={ChannelId} ownerUserId={OwnerUserId} state={State}",
                    channel.Transport, channel.Id, channel.OwnerUserId, currentState);
                channel.DisruptionNotifiedAtUtc = now;
            }
        }
        else
        {
            channel.DisruptionNotifiedAtUtc = null;
        }
    }

    /// <summary>§29.3 — a channel stuck in Connecting past the QR-scan timeout: no owner ever finished
    /// authorizing it, so the (possibly billed) instance is abandoned rather than left running forever.</summary>
    private async Task<int> CleanupUnauthorizedTimeoutsAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now - TimeSpan.FromMinutes(options.Value.UnauthorizedInstanceTimeoutMinutes);
        var stuck = await db.NotificationChannels
            .Where(c => c.State == ChannelState.Connecting)
            .Where(c => c.InstanceCreatedAtUtc != null && c.InstanceCreatedAtUtc < cutoff)
            .ToListAsync(ct);

        foreach (var channel in stuck)
            await DeleteInstanceAsync(channel, ChannelState.NotConnected, ChannelStateReason.UnauthorizedInstanceTimedOut, now, ct);

        return stuck.Count;
    }

    /// <summary>§30.3 — one GROUP BY/LEFT JOIN for every channel's active-assigned-company count, then
    /// <see cref="ChannelIdleCalculator.Recompute"/> per channel (the only place
    /// <see cref="NotificationChannel.IdleSinceUtc"/> is ever written).</summary>
    private async Task<(int Warned, int Deleted)> RecomputeIdleAsync(DateTime now, CancellationToken ct)
    {
        var idleDays = await platformSettings.GetChannelIdleDaysAsync(ct);

        var activeCompanyCounts = await db.NotificationChannels
            .Select(c => new { c.Id, ActiveCompanyCount = c.Assignments.Count(a => a.Company.IsActive) })
            .ToDictionaryAsync(x => x.Id, x => x.ActiveCompanyCount, ct);

        // Replaced is terminal (§23.2) — nothing about its idle state matters anymore.
        var channels = await db.NotificationChannels.Where(c => c.State != ChannelState.Replaced).ToListAsync(ct);

        var warned = 0;
        var deleted = 0;
        foreach (var channel in channels)
        {
            var activeCount = activeCompanyCounts.GetValueOrDefault(channel.Id, 0);
            var recomputedIdleSince = ChannelIdleCalculator.Recompute(
                channel.IdleSinceUtc, activeCount, channel.PaidUntilUtc, channel.IsSuspendedByAdmin, now);

            if (recomputedIdleSince != channel.IdleSinceUtc)
            {
                channel.IdleSinceUtc = recomputedIdleSince;
                if (recomputedIdleSince is null) channel.IdleWarningSentAtUtc = null; // idle ended — antispam resets too
            }

            if (channel.IdleSinceUtc is not { } idleSince) continue;

            var idleDuration = now - idleSince;
            if (idleDuration >= TimeSpan.FromDays(idleDays))
            {
                if (channel.ProviderInstanceId is not null)
                {
                    await DeleteInstanceAsync(channel, ChannelState.NeedsReconnect, ChannelStateReason.IdleInstanceDeleted, now, ct);
                    deleted++;
                }
            }
            else if (idleDuration >= TimeSpan.FromDays(Math.Max(0, idleDays - 1)) && channel.IdleWarningSentAtUtc is null)
            {
                // US-62 p.6: warned BEFORE deletion, "nothing broke yet" — this is the ONLY channel of
                // owner-facing notice for this event (§31.3), via the banner reading IdleWarningSentAtUtc.
                channel.IdleWarningSentAtUtc = now;
                warned++;
            }
        }

        await db.SaveChangesAsync(ct);
        return (warned, deleted);
    }

    /// <summary>Every channel with a non-null <see cref="NotificationChannel.OrphanedInstanceId"/> —
    /// left over from a process crash between §30.4's steps 1 and 3 on a PRIOR pass or a prior synchronous
    /// caller (owner disconnect, account deletion, number ban).</summary>
    private async Task<int> RetryOrphanDeletionsAsync(CancellationToken ct)
    {
        var orphaned = await db.NotificationChannels.Where(c => c.OrphanedInstanceId != null).ToListAsync(ct);
        var cleaned = 0;
        foreach (var channel in orphaned)
        {
            if (await RetryOrphanDeletionForAsync(channel, ct)) cleaned++;
        }
        return cleaned;
    }

    /// <summary>
    /// §30.4, the full necessary-operation procedure: database first (orphan the instance id, blank the
    /// channel's own credentials, transition state), THEN best-effort provider cleanup, retried from the
    /// database on every subsequent pass until it confirms. The channel's own token is blanked in the
    /// SAME transaction that records the orphan id — safe because <see cref="IChannelProvisioning.DeleteInstanceAsync"/>
    /// uses the PLATFORM's partner token and the bare instance id, never the channel's own (a documented
    /// property of the partner API, not a workaround).
    /// </summary>
    private async Task DeleteInstanceAsync(
        NotificationChannel channel, ChannelState targetState, ChannelStateReason reason, DateTime now, CancellationToken ct)
    {
        if (channel.ProviderInstanceId is not { } instanceId)
        {
            // N4: a channel can land here (State == Connecting, past the QR timeout) with NO
            // ProviderInstanceId at all — the connect request created an instance at the provider but the
            // process crashed/was killed before the id was ever written to this row (the window between
            // NotificationChannelsController.Connect's provider call and its next SaveChanges). There is
            // nothing to delete — the id was never recorded, so it can't be — but the channel must not sit
            // stuck in Connecting forever with no way back into the UI (CanConnect is false for
            // Connecting, so Connect() would 409 on every retry). The instance itself is an accepted,
            // unavoidable loss in this specific crash window; what's NOT acceptable is a channel row that
            // can never be un-stuck through the product.
            await ChannelStateTransition.Apply(
                db, channel, targetState, reason, "instance id was never recorded (process crash after provider create)", now);
            await db.SaveChangesAsync(ct);
            return;
        }

        // Captured BEFORE the DB write clears them — §30.4 step 2's best-effort LogoutAsync still needs
        // the channel's OWN (still valid at the provider) credentials, even though step 1 below already
        // blanks the STORED copy. Decrypting can fail on its own (lost key, corrupted ciphertext); that
        // must never block the deletion itself, so it is swallowed independently of the logout call.
        ChannelCredentials? credentials = null;
        if (channel.ProviderSecretCiphertext is { } ciphertext)
        {
            try
            {
                var token = SecretProtector.Decrypt(ciphertext, options.Value.EncryptionKey ?? string.Empty, channel.Id);
                credentials = new ChannelCredentials(instanceId, token);
            }
            catch (Exception ex) when (ex is ChannelSecretUnavailableException or ArgumentException)
            {
                logger.LogDebug(ex, "channel-health: could not decrypt channel secret for a best-effort logout before deletion, skipping logout: channelId={ChannelId}", channel.Id);
            }
        }

        // §30.4 step 1: database first.
        channel.OrphanedInstanceId = instanceId;
        channel.ProviderInstanceId = null;
        channel.ProviderSecretCiphertext = null;
        channel.ProviderSecretKeyId = null;
        await ChannelStateTransition.Apply(db, channel, targetState, reason, null, now);
        await db.SaveChangesAsync(ct);

        // §30.4 step 2, part one: best effort, failure ignored — the DELETE call below does not depend
        // on this having succeeded (it uses the platform's partner token, not the channel's own).
        if (credentials is not null)
        {
            try
            {
                await provisioningRegistry.For(channel.Transport).LogoutAsync(credentials, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "channel-health: best-effort logout before instance deletion failed, continuing: channelId={ChannelId}", channel.Id);
            }
        }

        await RetryOrphanDeletionForAsync(channel, ct);
    }

    /// <summary>Attempts to actually delete <see cref="NotificationChannel.OrphanedInstanceId"/> at the
    /// provider and, on success, clears it (§30.4 step 3) — every failure mode (network, provider still
    /// thinks it exists but errors, etc.) just leaves the field set for the next pass to retry; nothing
    /// here is allowed to throw out of this method.</summary>
    /// <returns>Whether the orphan was cleared this call.</returns>
    private async Task<bool> RetryOrphanDeletionForAsync(NotificationChannel channel, CancellationToken ct)
    {
        if (channel.OrphanedInstanceId is not { } orphanedId) return false;

        try
        {
            var deletion = await provisioningRegistry.For(channel.Transport).DeleteInstanceAsync(orphanedId, ct);
            if (!deletion.Success)
            {
                logger.LogWarning(
                    "{Transport} instance deletion did not confirm success, will retry next pass: channelId={ChannelId} instanceId={InstanceId}",
                    channel.Transport, channel.Id, orphanedId);
                return false;
            }

            channel.OrphanedInstanceId = null;
            await db.SaveChangesAsync(ct);
            // §37: deleting an instance is always a distinctly noticeable event — it is both money and an
            // owner's binding, never folded silently into the pass's aggregate summary alone.
            logger.LogWarning(
                "{Transport} instance deleted: channelId={ChannelId} ownerUserId={OwnerUserId} instanceId={InstanceId}",
                channel.Transport, channel.Id, channel.OwnerUserId, orphanedId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "{Transport} instance deletion failed, will retry next pass: channelId={ChannelId} instanceId={InstanceId}",
                channel.Transport, channel.Id, orphanedId);
            return false;
        }
    }
}
