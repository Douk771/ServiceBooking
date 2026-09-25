using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Signals;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// Cycle 18, B7 (ARCHITECTURE_CYCLE18.md §337.1) — the sixth <see cref="IScheduledTask"/>,
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
/// A failure on one account never aborts the pass for the rest (same TD-04 convention as
/// <c>DataRetentionTask</c>); phase 4's fail-closed case (no system Free plan configured) is reported
/// through <see cref="ScheduledTaskOutcome.Error"/> AND a GlitchTip signal, exactly like R5/US-18-11
/// requires — this task must never invent "some other free-ish plan" to keep going.
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

        var windowsOpened = await SelfHealMissedWindowStartsAsync(now, ct);
        var windowsClosed = await CloseExpiredWindowsAsync(now, ct);
        var warned = await WarnApproachingExpiryAsync(now, ct);
        var (expired, failed, error) = await ExpireTrialsAsync(now, ct);

        var summary = $"trial-lifecycle: windows-opened={windowsOpened} windows-closed={windowsClosed} " +
                      $"warned={warned} expired={expired} failed={failed}";
        logger.LogInformation("{Summary}", summary);

        var scanned = windowsOpened + windowsClosed + warned + expired + failed;
        var affected = windowsOpened + windowsClosed + warned + expired;
        return new ScheduledTaskOutcome(scanned, affected, 0, summary) { Error = error };
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
                var earliest = await db.NotificationChannels
                    .Where(c => c.BillingAccountId == account.Id && c.ConnectedAtUtc != null)
                    .MinAsync(c => c.ConnectedAtUtc, ct);
                if (earliest is null) continue; // race: the channel's ConnectedAtUtc was cleared since the query above

                // Reuse the exact same arithmetic/journal write the request-time hook uses
                // (TrialMailingWindowStarter.StartIfDueAsync) — dated at the REAL earliest
                // authorization, never at "now this pass happens to run".
                await TrialMailingWindowStarter.StartIfDueAsync(db, account, earliest.Value, ct);
                opened++;
            }

            cursor = candidates[^1].Id;
            await db.SaveChangesAsync(ct);
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
                closed++;
            }

            await db.SaveChangesAsync(ct);
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
                var thresholds = TrialWindow.ParseThresholds(account.TrialWarningThresholdsDays);
                var daysLeft = (int)Math.Ceiling((account.TrialEndsAtUtc!.Value - now).TotalDays);
                var applicable = TrialWindow.ApplicableThreshold(thresholds, account.TrialWarnedAtThresholdDays, daysLeft);
                if (applicable is not null)
                {
                    account.TrialWarnedAtThresholdDays = applicable;
                    warned++;
                }
            }

            cursor = candidates[^1].Id;
            await db.SaveChangesAsync(ct);
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
    private async Task<(int Expired, int Failed, string? Error)> ExpireTrialsAsync(DateTime now, CancellationToken ct)
    {
        var expired = 0;
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
            var trialOptions = await db.AccountSubscriptionOptions
                .Where(o => accountIds.Contains(o.BillingAccountId) && o.EndsAtUtc == null)
                .ToListAsync(ct);

            foreach (var account in candidates)
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
                    expired++;
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
                expired++;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            if (candidates.Count < BatchSize) break;
        }

        return (expired, failed, error);
    }
}
