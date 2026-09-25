using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Signals;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// Cycle 18, B7 (ARCHITECTURE_CYCLE18.md §337.1) — the EIGHTH <see cref="IScheduledTask"/>,
/// <c>trial-lifecycle</c>. Runs hourly, not daily: unlike <c>SubjectRequestDueSoonTask</c>'s
/// day-granularity idempotency (a missed pass there means a permanently skipped signal, an accepted
/// trade-off named in that task's own doc comment), a trial's warnings and expiry transition must
/// eventually happen even after several missed passes (US-18-13) — so idempotency here is built on
/// STATE stored on the account, never on "did today's pass already run".
///
/// Four phases per pass, in this order (§337.1):
/// 1. Self-heal a missed mailing-window-start hook (§336.1 п.2) — an account with a trial and a
///    channel already connected, but whose window was never opened.
/// 2. Close a mailing window whose end date has passed (journal-only — nothing to disable, §336.3).
/// 3. Warnings, thresholds read from the ACCOUNT'S OWN SNAPSHOT (Д19), never the live platform setting.
/// 4. Expiry — the materialized transition to the system Free plan (§337.3: no irreversible
///    consequence of any kind).
///
/// A failure on one account never aborts the pass for the rest, nor for any later phase (same TD-04
/// convention as <c>DataRetentionTask</c>, applied here at BOTH the phase granularity and, within each
/// phase's batch loop, at the per-account granularity — code review, cycle 18 late delta: a version of
/// this task with no try/catch anywhere let one poisoned account in phase 4's first batch, every single
/// hour, throw out of <c>ExecuteAsync</c> before phases already queued never even ran and stop every
/// account after it in the batch from ever being processed). Phase 4's fail-closed case (no system Free
/// plan configured) is reported through <see cref="ScheduledTaskOutcome.Error"/> AND a GlitchTip signal,
/// exactly like R5/US-18-11 requires — this task must never invent "some other free-ish plan" to keep
/// going.
/// </summary>
public sealed class TrialLifecycleTask(
    AppDbContext db,
    INotificationClock clock,
    IGlitchTipSignalService signals,
    ILogger<TrialLifecycleTask> logger) : IScheduledTask
{
    public string Name => "trial-lifecycle";
    public TimeSpan DefaultPeriod => TimeSpan.FromHours(1);

    private const int BatchSize = 100;

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var phaseFailures = new List<string>();

        var windowsOpened = await RunPhaseAsync("self-heal-window-start", () => SelfHealMissedWindowStartsAsync(now, ct), phaseFailures);
        var windowsClosed = await RunPhaseAsync("close-window", () => CloseExpiredWindowsAsync(now, ct), phaseFailures);
        var warned = await RunPhaseAsync("warn", () => WarnApproachingExpiryAsync(now, ct), phaseFailures);

        var expired = 0;
        var alreadyHandled = 0;
        var failedAccounts = 0;
        string? error = null;
        try
        {
            (expired, alreadyHandled, failedAccounts, error) = await ExpireTrialsAsync(now, ct);
        }
        catch (OperationCanceledException)
        {
            throw; // time budget / host shutdown — not a phase failure, must still unwind.
        }
        catch (Exception ex)
        {
            phaseFailures.Add("expire");
            logger.LogError(ex, "trial-lifecycle: phase expire failed outright");
        }

        // N6 (code review) — each phase counter is now a true "rows actually changed" count (see each
        // phase's own comment below for what it no longer over-counts).
        var summary = $"trial-lifecycle: windows-opened={windowsOpened} windows-closed={windowsClosed} " +
                      $"warned={warned} expired={expired} already-handled={alreadyHandled} failed-accounts={failedAccounts}" +
                      (phaseFailures.Count > 0 ? $" failed-phases={string.Join(",", phaseFailures)}" : string.Empty);
        logger.LogInformation("{Summary}", summary);

        var scanned = windowsOpened + windowsClosed + warned + expired + alreadyHandled + failedAccounts;
        var affected = windowsOpened + windowsClosed + warned + expired + alreadyHandled;
        var combinedError = phaseFailures.Count > 0
            ? $"{phaseFailures.Count} of 4 trial-lifecycle phase(s) failed outright: {string.Join(", ", phaseFailures)}" +
              (error is not null ? $" | {error}" : string.Empty)
            : error;
        return new ScheduledTaskOutcome(scanned, affected, 0, summary) { Error = combinedError };
    }

    /// <summary>N-fix (code review) — discards every entity this ONE failed iteration touched (Added or
    /// Modified), without disturbing already-saved entities from earlier iterations of the same batch or
    /// not-yet-visited entities queued for later ones. Safe precisely because each phase below now calls
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken)"/> once per
    /// ACCOUNT rather than once per batch: by the time an iteration's try block starts, the tracker holds
    /// only Unchanged entries (everything from prior successful iterations was already committed), so any
    /// non-Unchanged entry found here can only have been produced by the iteration that just failed.
    /// Replaces the narrower "reload just account/sub" fix, which left a same-iteration Added
    /// <see cref="SubscriptionChangeLog"/> row (this project's append-only, evidentiary journal — Д18/Т1)
    /// still queued for the next successful save, asserting a transition that never actually happened.</summary>
    private static void DiscardFailedIterationChanges(AppDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
            entry.State = EntityState.Detached;
    }

    /// <summary>N4 (code review) — isolates a phase throwing OUTRIGHT (as opposed to one account inside
    /// it failing, which each phase already isolates internally below) from aborting the phases after
    /// it. Mirrors <c>DataRetentionTask</c>'s per-rule try/catch at the phase granularity.</summary>
    private async Task<int> RunPhaseAsync(string phaseName, Func<Task<int>> phase, List<string> phaseFailures)
    {
        try
        {
            return await phase();
        }
        catch (OperationCanceledException)
        {
            throw; // time budget / host shutdown — must still unwind, not a phase failure.
        }
        catch (Exception ex)
        {
            phaseFailures.Add(phaseName);
            logger.LogError(ex, "trial-lifecycle: phase {Phase} failed outright", phaseName);
            return 0;
        }
    }

    /// <summary>Phase 1, §336.1 п.2 — "самолечение". Picks up accounts where the hook in
    /// <see cref="Notifications.ChannelStateTransition"/> should have fired but, for whatever reason,
    /// never ran (a channel connected through a code path added later that forgot to route through
    /// <c>Apply</c>, a restored backup, etc.). The window is dated from the REAL earliest authorization,
    /// never from the moment this pass happens to run.</summary>
    private async Task<int> SelfHealMissedWindowStartsAsync(DateTime now, CancellationToken ct)
    {
        var opened = 0;
        // Keyset cursor: StartIfDueAsync's own guards (TrialEndsAtUtc/TrialMailingWindowDays missing, or
        // the MinAsync race noted below) can leave a row still matching this WHERE after being visited —
        // without `a.Id > cursor` a batch of >=BatchSize such rows would be re-selected forever.
        var cursor = Guid.Empty;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var candidates = await db.BillingAccounts
                .Where(a => a.Id > cursor)
                .Where(a => a.TrialStartedAtUtc != null && a.TrialChannelFirstAuthorizedAtUtc == null)
                .Where(a => db.NotificationChannels.Any(c => c.BillingAccountId == a.Id && c.ConnectedAtUtc != null))
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (candidates.Count == 0) break;

            foreach (var account in candidates)
            {
                try
                {
                    var earliest = await db.NotificationChannels
                        .Where(c => c.BillingAccountId == account.Id && c.ConnectedAtUtc != null)
                        .MinAsync(c => c.ConnectedAtUtc, ct);
                    if (earliest is null) continue; // race: the channel's ConnectedAtUtc was cleared since the query above

                    // Reuse the exact same arithmetic/journal write the request-time hook uses
                    // (TrialMailingWindowStarter.StartIfDueAsync) — dated at the REAL earliest
                    // authorization, never at "now this pass happens to run".
                    await TrialMailingWindowStarter.StartIfDueAsync(db, account, earliest.Value, ct);
                    // N-fix (code review) — SaveChangesAsync per ACCOUNT, not per batch: this phase can
                    // add a SubscriptionChangeLog row (StartIfDueAsync) as a side effect. Append-only
                    // journal (Д18/Т1 evidentiary role) — a batched save would let a later account's
                    // failure roll back the account/sub reload below while this Added log row, already
                    // in the batch's SaveChanges call, still got persisted, leaving the journal asserting
                    // a transition that never actually completed.
                    await db.SaveChangesAsync(ct);
                    // N6 (code review) — only counted if the write actually happened: StartIfDueAsync's
                    // own guards (TrialEndsAtUtc/TrialMailingWindowDays missing) can leave it a no-op even
                    // though the outer query matched.
                    if (account.TrialChannelFirstAuthorizedAtUtc is not null) opened++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // N4 (code review) — one poisoned account never stops the rest of the batch (or the
                    // rest of the pass). SaveChangesAsync now runs per account (see above), so a failed
                    // iteration never reached a commit: nothing — not even the SubscriptionChangeLog row
                    // StartIfDueAsync may have queued — was persisted for it. DiscardFailedIterationChanges
                    // just forgets that in-memory tracking so it never rides along on a later account's
                    // save in this same batch.
                    logger.LogError(ex, "trial-lifecycle: self-heal-window-start failed for account {AccountId}", account.Id);
                    DiscardFailedIterationChanges(db);
                }
            }

            cursor = candidates[^1].Id;
            db.ChangeTracker.Clear();

            if (candidates.Count < BatchSize) break;
        }
        return opened;
    }

    /// <summary>Phase 2, §336.3 — journal-only. Nothing to disable: access is already gated by
    /// <c>MailingUntilUtc</c> in <c>SubscriptionResolver</c>. Idempotent on
    /// <c>TrialMailingClosureLoggedAtUtc</c>, not on the date, so a missed pass still writes the row on
    /// the first pass that DOES run.</summary>
    private async Task<int> CloseExpiredWindowsAsync(DateTime now, CancellationToken ct)
    {
        var closed = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var candidates = await db.BillingAccounts
                .Where(a => a.TrialMailingWindowEndsAtUtc != null && a.TrialMailingWindowEndsAtUtc <= now)
                .Where(a => a.TrialMailingClosureLoggedAtUtc == null)
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (candidates.Count == 0) break;

            foreach (var account in candidates)
            {
                try
                {
                    account.TrialMailingClosureLoggedAtUtc = now;
                    db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
                    {
                        Id = Guid.NewGuid(),
                        OwnerUserId = account.OwnerUserId,
                        ChangedByUserId = TrialActors.System,
                        ChangedAt = now,
                        BillingAccountId = account.Id,
                        ChangeKind = SubscriptionChangeKind.TrialMailingWindow,
                        Comment = "Окно бесплатных рассылок закрыто",
                    });
                    // N-fix (code review) — SaveChangesAsync per ACCOUNT, not per batch: this phase adds a
                    // SubscriptionChangeLog row every iteration. A batched save let a LATER account's
                    // failure trigger the account-only reload (below) while this Added log row, already
                    // in the same pending batch, still got persisted — the append-only journal (Д18/Т1)
                    // would then assert a closure that, for this account, may never have committed.
                    await db.SaveChangesAsync(ct);
                    closed++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // N4 (code review) — isolate a single account's failure from the rest of the batch.
                    // SaveChangesAsync now runs per account (see above), so a failed iteration never
                    // reached a commit; this just forgets the in-memory tracking so it can't ride along on
                    // a later account's save.
                    logger.LogError(ex, "trial-lifecycle: close-window failed for account {AccountId}", account.Id);
                    DiscardFailedIterationChanges(db);
                }
            }

            db.ChangeTracker.Clear();

            if (candidates.Count < BatchSize) break;
        }
        return closed;
    }

    /// <summary>Phase 3, §337.1 п.3 — thresholds come from the account's OWN
    /// <see cref="BillingAccount.TrialWarningThresholdsDays"/> snapshot (Д19), never the live platform
    /// setting. Only the nearest not-yet-crossed threshold is ever applied per pass
    /// (<see cref="TrialWindow.ApplicableThreshold"/>) — a multi-day-missed pass does not dump a burst of
    /// stale warnings, it just catches up to the single closest one still owed (US-18-10).</summary>
    private async Task<int> WarnApproachingExpiryAsync(DateTime now, CancellationToken ct)
    {
        var warned = 0;
        var systemTrialId = await db.SubscriptionPlanConfigs
            .Where(p => p.IsSystemTrial).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (systemTrialId is null) return 0; // no trial plan configured at all — nothing is "on trial"

        // A keyset cursor is required here, unlike the other three phases: those each mutate the very
        // column their own WHERE clause filters on, so a processed row naturally falls out of the next
        // page. Here, a row that was just warned (or found to need no warning yet) still matches the
        // outer "not expired yet" WHERE on the NEXT iteration too — without `a.Id > cursor`, a table
        // with more than one batch's worth of live trials would re-select the same first page forever.
        var cursor = Guid.Empty;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Only accounts CURRENTLY usable on the trial plan — an account moved onto a paid plan
            // early (before TrialEndsAtUtc) keeps its Trial* history columns forever (§337.3) but must
            // not keep collecting "trial ending soon" warnings for a trial that, in effect, already
            // ended by upgrade.
            var candidates = await db.BillingAccounts
                .Where(a => a.Id > cursor)
                .Where(a => a.TrialStartedAtUtc != null && a.TrialEndsAtUtc != null && a.TrialEndsAtUtc > now)
                .Where(a => a.TrialExpiredHandledAtUtc == null)
                .Where(a => db.AccountSubscriptions.Any(s =>
                    s.BillingAccountId == a.Id && s.IsActive && s.PlanConfigId == systemTrialId))
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (candidates.Count == 0) break;

            foreach (var account in candidates)
            {
                try
                {
                    var thresholds = TrialWindow.ParseThresholds(account.TrialWarningThresholdsDays);
                    var daysLeft = (int)Math.Ceiling((account.TrialEndsAtUtc!.Value - now).TotalDays);
                    var applicable = TrialWindow.ApplicableThreshold(thresholds, account.TrialWarnedAtThresholdDays, daysLeft);
                    if (applicable is not null)
                    {
                        account.TrialWarnedAtThresholdDays = applicable;
                        // N-fix (code review) — per-account save, matching the other three phases, so a
                        // failed account's DiscardFailedIterationChanges below can never coincide with an
                        // already-pending, not-yet-committed mutation from this same iteration.
                        await db.SaveChangesAsync(ct);
                        warned++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // N4 (code review) — isolate a single account's failure from the rest of the batch.
                    logger.LogError(ex, "trial-lifecycle: warn failed for account {AccountId}", account.Id);
                    DiscardFailedIterationChanges(db);
                }
            }

            cursor = candidates[^1].Id;
            db.ChangeTracker.Clear();

            if (candidates.Count < BatchSize) break;
        }
        return warned;
    }

    /// <summary>Phase 4, §337.1 п.4 / §337.3 — the materialized transition to the system Free plan.
    /// Fail-closed (R5/US-18-11): an account is never moved onto "some other free-ish plan" just because
    /// no plan is flagged <c>IsSystemFree</c> — it is left completely untouched, counted as a failure,
    /// and reported both in <see cref="ScheduledTaskOutcome.Error"/> and via
    /// <see cref="IGlitchTipSignalService"/> so an operator notices instead of trials silently piling up
    /// unresolved.</summary>
    private async Task<(int Transitioned, int AlreadyHandled, int Failed, string? Error)> ExpireTrialsAsync(DateTime now, CancellationToken ct)
    {
        // N6 (code review) — "transitioned" (actually moved to Free) and "already handled" (nothing left
        // to transition, only marked so this index stops re-selecting it) used to be folded into one
        // `expired` counter; kept apart so the task's own summary — the only thing a superadmin sees per
        // §337.1 — doesn't overstate how many trials genuinely ended this pass.
        var transitioned = 0;
        var alreadyHandled = 0;
        var failed = 0;
        string? error = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // IX_BillingAccounts_TrialExpiry — "date has passed and no transition has happened yet",
            // deliberately not "date == today" so a missed pass still catches up (US-18-13).
            var candidates = await db.BillingAccounts
                .Where(a => a.TrialEndsAtUtc != null && a.TrialEndsAtUtc < now && a.TrialExpiredHandledAtUtc == null)
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (candidates.Count == 0) break;

            // One lookup per pass, not per account — the flag can only change between passes, an hour
            // apart, never mid-batch.
            var systemTrial = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.IsSystemTrial, ct);
            var systemFree = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.IsSystemFree, ct);
            if (systemFree is null)
            {
                failed += candidates.Count;
                error = "trial-lifecycle: no plan is flagged IsSystemFree — trial expiry cannot be " +
                        $"materialized for {candidates.Count} account(s) this pass (fail-closed, R5/US-18-11).";
                logger.LogError("{Error}", error);
                await signals.SendAsync(error, ct);
                break; // every remaining candidate would hit the exact same missing-plan failure
            }

            var accountIds = candidates.Select(a => a.Id).ToList();
            var sub = await db.AccountSubscriptions
                .Where(s => s.BillingAccountId != null && accountIds.Contains(s.BillingAccountId!.Value))
                .ToDictionaryAsync(s => s.BillingAccountId!.Value, ct);
            // B1 (code review, cycle 18 late delta) — GrantedByTrial narrows this to rows the trial
            // ITSELF materialized (§333.3). Without it, a paid notifications.whatsapp row an admin
            // assigned before the account ever went on trial (same OptionId, EndsAtUtc == null) would be
            // dated out here too and never revived automatically — an irreversible side effect of the
            // trial→Free transition, which §337.3 forbids outright.
            var trialOptions = await db.AccountSubscriptionOptions
                .Where(o => accountIds.Contains(o.BillingAccountId) && o.EndsAtUtc == null && o.GrantedByTrial)
                .ToListAsync(ct);

            foreach (var account in candidates)
            {
                try
                {
                if (!sub.TryGetValue(account.Id, out var accountSub) ||
                    systemTrial is null || accountSub.PlanConfigId != systemTrial.Id)
                {
                    // No subscription row at all, or the account was already moved OFF the trial plan
                    // by an admin/owner action before the trial's own end date materialized (e.g. a paid
                    // plan assigned early) — this task must not overwrite a plan decision someone else
                    // already made. Either way there is nothing left to transition; the account is still
                    // marked handled so it stops being re-selected by this index every pass forever.
                    account.TrialExpiredHandledAtUtc = now;
                    await db.SaveChangesAsync(ct);
                    alreadyHandled++;
                    continue;
                }

                var oldPlanConfigId = accountSub.PlanConfigId;
                var oldPaidUntil = accountSub.PaidUntil;
                var oldIsActive = accountSub.IsActive;

                // §337.3 — exactly this and nothing more: plan swap, PaidUntil/MailingUntilUtc cleared,
                // trial option rows dated out. No Remove/RemoveRange/ExecuteDelete anywhere in this
                // method, no company/employee/booking/photo/template touched, IsActive of anything but
                // this ONE subscription row left alone.
                accountSub.PlanConfigId = systemFree.Id;
                accountSub.PaidUntil = null;
                accountSub.IsActive = true;
                accountSub.MailingUntilUtc = null;
                accountSub.UpdatedAt = now;

                foreach (var option in trialOptions.Where(o => o.BillingAccountId == account.Id))
                    option.EndsAtUtc = now;

                account.TrialExpiredHandledAtUtc = now; // Т3: the 30-day notice lifetime counts from here

                db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = account.OwnerUserId,
                    ChangedByUserId = TrialActors.System,
                    ChangedAt = now,
                    OldPlanConfigId = oldPlanConfigId,
                    NewPlanConfigId = systemFree.Id,
                    OldPaidUntil = oldPaidUntil,
                    NewPaidUntil = null,
                    OldIsActive = oldIsActive,
                    NewIsActive = true,
                    BillingAccountId = account.Id,
                    ChangeKind = SubscriptionChangeKind.TrialExpired,
                    Comment = $"Пробный период закончился {account.TrialEndsAtUtc:dd.MM.yyyy}, подписка переведена на бесплатный тариф",
                });
                // N-fix (code review) — SaveChangesAsync per ACCOUNT, not per batch: this is exactly the
                // journal write the task's own doc comment (top of file) flagged as a known gap when
                // reload was only applied to account/sub. A batched save let a LATER account's failure
                // trigger this account's reload-only recovery while this Added TrialExpired log row,
                // already in the same pending batch, still got persisted — asserting a plan transition
                // that, for this account, may never actually have committed.
                await db.SaveChangesAsync(ct);
                transitioned++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // N4 (code review) — one poisoned account never stops the rest of the batch (or the
                    // pass). SaveChangesAsync now runs per account (see above), so a failed iteration never
                    // reached a commit — not the plan swap, not the option EndsAtUtc dating, not the
                    // TrialExpired journal row. DiscardFailedIterationChanges forgets all of it at once.
                    failed++;
                    logger.LogError(ex, "trial-lifecycle: expire failed for account {AccountId}", account.Id);
                    DiscardFailedIterationChanges(db);
                }
            }

            db.ChangeTracker.Clear();

            if (candidates.Count < BatchSize) break;
        }

        return (transitioned, alreadyHandled, failed, error);
    }
}
