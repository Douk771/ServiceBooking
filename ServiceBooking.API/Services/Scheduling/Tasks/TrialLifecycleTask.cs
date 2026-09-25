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

    /// <summary>Б3 (code review, cycle 18 4th pass, customer decision) — a failure COUNT this high must
    /// surface as an <see cref="ScheduledTaskOutcome.Error"/> regardless of how many OTHER accounts in
    /// the same pass progressed fine. Without this, an account already moved off the trial plan by an
    /// admin (counted as `alreadyHandled`, not `failed`) sitting in the same pass as a mass write failure
    /// (e.g. 500 accounts failing to save) would suppress Error entirely, because the pre-existing
    /// "NO progress at all" gate below only looks at `transitioned == 0 &amp;&amp; alreadyHandled == 0` —
    /// leaving the failure visible only inside the free-text `summary`, exactly the invisibility Н1 was
    /// meant to fix in the first place. 10 is picked as "clearly not one bad row (N4's false-alarm-fatigue
    /// concern), but nowhere near noise" — not derived from any SLO, a customer-approved round number.</summary>
    private const int MassFailureThreshold = 10;

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var phaseFailures = new List<string>();

        var windowsOpened = await RunPhaseAsync("self-heal-window-start", () => SelfHealMissedWindowStartsAsync(now, ct), phaseFailures);
        var windowsClosed = await RunPhaseAsync("close-window", () => CloseExpiredWindowsAsync(now, ct), phaseFailures);
        var warned = await RunPhaseAsync("warn", () => WarnApproachingExpiryAsync(now, ct), phaseFailures);

        var (expired, alreadyHandled, failedAccounts, error) = await RunPhaseAsync(
            "expire", () => ExpireTrialsAsync(now, ct), phaseFailures, (0, 0, 0, (string?)null));

        // N6 (code review) — each phase counter is now a true "rows actually changed" count (see each
        // phase's own comment below for what it no longer over-counts).
        var summary = $"trial-lifecycle: windows-opened={windowsOpened} windows-closed={windowsClosed} " +
                      $"warned={warned} expired={expired} already-handled={alreadyHandled} failed-accounts={failedAccounts}" +
                      (phaseFailures.Count > 0 ? $" failed-phases={string.Join(",", phaseFailures)}" : string.Empty);
        logger.LogInformation("{Summary}", summary);

        var scanned = windowsOpened + windowsClosed + warned + expired + alreadyHandled + failedAccounts;
        var affected = windowsOpened + windowsClosed + warned + expired + alreadyHandled;

        // Н1 (code review, cycle 18 3rd/4th pass) — NOT applied as originally worded. The finding asked
        // for failedAccounts > 0 to always set a non-null Error, matching DataRetentionTask. That would
        // have contradicted CY18L-19 (`FailedIteration_InExpirePhase_...`), which asserts
        // `outcome.Error.Should().BeNull(...)` specifically BECAUSE "a single poisoned account is
        // isolated per-account (N4) — it must never surface as a phase-level Error" — one
        // permanently-broken account (e.g. a row an admin corrupted by hand) must not page an operator
        // every single hour forever. Instead, ExpireTrialsAsync below (see its own doc comment near the
        // end of the method) sets a non-null Error in the narrower cases that are actually
        // indistinguishable from expiry having silently stopped: either NO account in the pass made
        // progress at all, or the failure count crosses <see cref="MassFailureThreshold"/> regardless of
        // how much else progressed. `failed-accounts=N` also still reaches the free-text `summary`
        // unconditionally, exactly as before.
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
    private Task<int> RunPhaseAsync(string phaseName, Func<Task<int>> phase, List<string> phaseFailures) =>
        RunPhaseAsync(phaseName, phase, phaseFailures, 0);

    /// <summary>N7 (code review, cycle 18 3rd pass) — a single generic home for the "isolate a phase
    /// throwing OUTRIGHT from aborting the phases after it" wrapper (mirrors <c>DataRetentionTask</c>'s
    /// per-rule try/catch at the phase granularity), used both by the three <c>int</c>-returning phases
    /// and by <see cref="ExpireTrialsAsync"/>'s tuple return — previously duplicated inline in
    /// <see cref="ExecuteAsync"/> purely because its result type didn't fit the <c>int</c> overload.</summary>
    private async Task<TResult> RunPhaseAsync<TResult>(
        string phaseName, Func<Task<TResult>> phase, List<string> phaseFailures, TResult failureResult)
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
            return failureResult;
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
        // Б2 (code review, cycle 18 3rd pass) — a keyset cursor is required here too, same as phase 3/4:
        // an account whose iteration THREW (caught below, DiscardFailedIterationChanges rolls the
        // in-memory mutation back) still matches this WHERE on the next page, because
        // TrialMailingClosureLoggedAtUtc was never actually committed for it. Relying on "the phase
        // mutates the column it filters on" to progress the pages, as the old doc comment on
        // WarnApproachingExpiryAsync assumed only phase 3 needed a cursor, silently breaks the moment
        // >=BatchSize accounts in a row fail to save (e.g. a read-only DB failover) — the page never
        // shrinks below BatchSize, so `break` never fires and the pass spins until the host's time
        // budget runs out, starving every phase after this one every single hour.
        var cursor = Guid.Empty;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var candidates = await db.BillingAccounts
                .Where(a => a.Id > cursor)
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

            cursor = candidates[^1].Id;
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

        // A keyset cursor is required here for the same reason it now is in phases 2 and 4 (Б2, code
        // review cycle 18 3rd pass): a row that was just warned (or found to need no warning yet) still
        // matches the outer "not expired yet" WHERE on the NEXT iteration too — without `a.Id > cursor`,
        // a table with more than one batch's worth of live trials would re-select the same first page
        // forever. Phase 1 is the only one of the four that can still get away without one, because
        // StartIfDueAsync's own guards make a re-matched row here rare enough that it isn't load-bearing
        // for termination the way it is in phases 2-4 — but it takes one anyway (see its own comment),
        // since a failed iteration there is exactly as capable of leaving the filtered column unmutated.
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

        // Б2 (code review, cycle 18 3rd pass) — same keyset cursor as phases 2/3, for the same reason:
        // an account whose iteration THREW never actually commits TrialExpiredHandledAtUtc, so it still
        // matches this WHERE on the next page. Without `a.Id > cursor`, >=BatchSize consecutively
        // failing accounts (e.g. a read-only DB failover — SELECT still works, UPDATE/INSERT doesn't)
        // makes candidates.Count == BatchSize forever, `break` never fires, and this phase alone spins
        // until the host's time budget runs out — silently starving trial expiry every single pass.
        var cursor = Guid.Empty;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // IX_BillingAccounts_TrialExpiry — "date has passed and no transition has happened yet",
            // deliberately not "date == today" so a missed pass still catches up (US-18-13).
            var candidates = await db.BillingAccounts
                .Where(a => a.Id > cursor)
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
                    if (!sub.TryGetValue(account.Id, out var accountSub) || systemTrial is null)
                    {
                        // No subscription row at all (or no trial plan configured this pass) — nothing to
                        // transition. The account is still marked handled so it stops being re-selected by
                        // this index every pass.
                        account.TrialExpiredHandledAtUtc = now;
                        await db.SaveChangesAsync(ct);
                        alreadyHandled++;
                        continue;
                    }

                    // Н11 (customer-approved middle path, code review cycle 18 3rd pass) — re-read THIS
                    // ONE account's current PlanConfigId straight from the DB, right before mutating,
                    // instead of trusting only the whole-batch snapshot (`sub`) loaded before this loop
                    // started. Without this, an admin/owner decision made AFTER the batch was read but
                    // BEFORE this account's own iteration ran (e.g. AssignSubscription putting the account
                    // on a paid plan) would be silently overwritten back to Free here — the same class of
                    // "admin's decision quietly lost" failure as Б1, and the same price: the owner loses a
                    // plan someone just assigned them, with no journal trace.
                    //
                    // This is NOT a full fix — it narrows the race window from "however long the whole
                    // batch takes to process" down to "between this SELECT and this account's own
                    // SaveChangesAsync a few lines below", but a genuinely concurrent write landing in
                    // THAT gap is still possible. Closing it completely needs the same advisory lock
                    // TrialActivationService/AssignSubscription already take, acquired before BOTH the
                    // re-read and the write — deliberately not done here this pass (see the handoff
                    // report: restructuring per-account transactions/locking here risks the batch
                    // optimization and the CY18L-19/24 coverage right before the final review).
                    //
                    // N-fix (code review, cycle 18 4th pass) — the price of THIS decision, not just the
                    // alternative's: this extra roundtrip runs once per candidate account, every pass,
                    // whether or not a race actually happened — roughly doubling the number of DB
                    // roundtrips this phase makes on a large backlog (one extra SELECT per account on top
                    // of the per-account SaveChangesAsync already required by N4). Accepted because
                    // correctness of an admin's just-made plan decision outweighs the extra load, and the
                    // batch's own paging already bounds how much of the backlog is in flight at once.
                    var currentPlanId = await db.AccountSubscriptions.AsNoTracking()
                        .Where(s => s.BillingAccountId == account.Id)
                        .Select(s => (Guid?)s.PlanConfigId)
                        .FirstOrDefaultAsync(ct);
                    if (currentPlanId != systemTrial.Id)
                    {
                        // The account was already moved OFF the trial plan by an admin/owner action —
                        // either caught by the batch-wide snapshot above, or (this check's own reason to
                        // exist) in the narrow window since. This task must not overwrite a plan decision
                        // someone else already made; there is nothing left to transition.
                        account.TrialExpiredHandledAtUtc = now;
                        await db.SaveChangesAsync(ct);
                        alreadyHandled++;
                        continue;
                    }

                    var oldPlanConfigId = accountSub.PlanConfigId;
                    var oldPaidUntil = accountSub.PaidUntil;
                    var oldIsActive = accountSub.IsActive;

                    // §337.3 — exactly this and nothing more: plan swap, PaidUntil/MailingUntilUtc
                    // cleared, trial option rows dated out. No Remove/RemoveRange/ExecuteDelete anywhere
                    // in this method, no company/employee/booking/photo/template touched, IsActive of
                    // anything but this ONE subscription row left alone.
                    accountSub.PlanConfigId = systemFree.Id;
                    accountSub.PaidUntil = null;
                    accountSub.IsActive = true;
                    accountSub.MailingUntilUtc = null;
                    accountSub.UpdatedAt = now;

                    foreach (var option in trialOptions.Where(o => o.BillingAccountId == account.Id))
                        option.EndsAtUtc = now;

                    account.TrialExpiredHandledAtUtc = now; // Т3: 30-day notice lifetime counts from here

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
                    // N-fix (code review) — SaveChangesAsync per ACCOUNT, not per batch: this is exactly
                    // the journal write the task's own doc comment (top of file) flagged as a known gap
                    // when reload was only applied to account/sub. A batched save let a LATER account's
                    // failure trigger this account's reload-only recovery while this Added TrialExpired
                    // log row, already in the same pending batch, still got persisted — asserting a plan
                    // transition that, for this account, may never actually have committed.
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

            cursor = candidates[^1].Id;
            db.ChangeTracker.Clear();

            if (candidates.Count < BatchSize) break;
        }

        // Н1/Б2 (code review, cycle 18 3rd pass) — deliberately narrower than "any per-account failure
        // sets Error": CY18L-19 already asserts the opposite for a single poisoned account isolated
        // among otherwise-successful ones (N4's whole point — an operator must not get paged every hour
        // over one bad row). What genuinely needs to be visible is a pass that made NO PROGRESS AT ALL
        // despite having work to do — e.g. a read-only DB failover where every account in the batch
        // fails to save (CY18L-24) — because that is functionally indistinguishable from trial expiry
        // having silently stopped altogether, which R5/US-18-11's fail-closed visibility requirement is
        // exactly meant to catch.
        // Б3 (code review, cycle 18 4th pass, customer decision) — Error is set when EITHER: (a) the
        // pass made no progress at all (the original narrow condition — indistinguishable from expiry
        // having silently stopped), OR (b) failed >= MassFailureThreshold, independent of how much else
        // progressed — a mass failure (e.g. hundreds of accounts failing to save) must stay visible even
        // when a handful of unrelated accounts in the same pass happened to be `alreadyHandled` (already
        // moved off the trial plan by an admin) or `transitioned` fine.
        if (error is null && failed > 0 && (transitioned == 0 && alreadyHandled == 0 || failed >= MassFailureThreshold))
            error = $"trial-lifecycle: {failed} account(s) failed to expire this pass " +
                     "— see logs for account IDs (fail-closed visibility, R5/US-18-11).";

        return (transitioned, alreadyHandled, failed, error);
    }
}
