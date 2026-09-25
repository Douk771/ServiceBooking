using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 18 (ARCHITECTURE_CYCLE18.md §336, risk A1) — the ONE place the trial mailing window is
/// started, called from <see cref="Notifications.ChannelStateTransition"/> on every path a channel can
/// become <c>Connected</c> (webhook callback, QR poll, health-task polling) so the hook never has to be
/// duplicated inline at each call site.
///
/// Д5: start = max(first channel authorization, trial start). Д19: the window's LENGTH is the snapshot
/// taken at grant time (<c>BillingAccount.TrialMailingWindowDays</c>), never the live platform setting.
/// The stamp is written exactly once — a channel later disconnecting, being replaced, or a different
/// channel authorizing afterward must never move <see cref="BillingAccount.TrialChannelFirstAuthorizedAtUtc"/>
/// once it is set (enforced below by the null-check, which is equivalent to "MIN(ConnectedAtUtc) across
/// every channel including Replaced" as long as this is the only place that ever writes the field).
/// </summary>
public static class TrialMailingWindowStarter
{
    public static async Task OnChannelBecameConnectedAsync(
        AppDbContext db, NotificationChannel channel, DateTime nowUtc, CancellationToken ct = default)
    {
        if (channel.BillingAccountId is not { } billingAccountId) return;

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.Id == billingAccountId, ct);
        if (account is null) return;

        await StartIfDueAsync(db, account, nowUtc, ct);
    }

    /// <summary>Shared core, also called by <c>TrialLifecycleTask</c>'s self-heal phase (§336.1 п.2) with
    /// the account's REAL earliest channel authorization instant rather than "now" — so a missed hook is
    /// backdated correctly instead of burning window days against the pass's own run time. Kept as one
    /// method precisely so the two callers can never drift into two different arithmetic/journal
    /// implementations of the same write.</summary>
    public static async Task StartIfDueAsync(AppDbContext db, BillingAccount account, DateTime firstAuthorizedUtc, CancellationToken ct = default)
    {
        // No trial ever granted, or the window already started once — never restart it (Д5).
        if (account.TrialStartedAtUtc is null) return;
        if (account.TrialChannelFirstAuthorizedAtUtc is not null) return;
        if (account.TrialEndsAtUtc is null || account.TrialMailingWindowDays is null) return;

        // Б... (code review, cycle 18 4th pass) — clamp the STORED/reported authorization instant to the
        // CURRENT trial's own start, the same clamp TrialWindow.WindowEnd already applies internally when
        // computing the window's end date. Without this, a regrant with a channel that was already
        // connected from a PRIOR trial would stamp both the owner-facing mailingWindow.startedAt and this
        // journal row's ChangedAt with the old, pre-regrant date — the window's own END date was already
        // correct (WindowEnd clamps `start` before adding windowDays), but the owner would be told
        // mailings "have been running since" a date up to a whole prior trial ago, and the append-only
        // journal (Д18/Т1, ordered by ChangedAt) would sort this "window opened" row BEFORE the prior
        // trial's own "window closed" row — backwards, in evidence meant to be read in order.
        var clampedAuthorizedUtc = firstAuthorizedUtc > account.TrialStartedAtUtc.Value
            ? firstAuthorizedUtc : account.TrialStartedAtUtc.Value;

        account.TrialChannelFirstAuthorizedAtUtc = clampedAuthorizedUtc;
        account.TrialMailingWindowEndsAtUtc = TrialWindow.WindowEnd(
            firstAuthorizedUtc, account.TrialStartedAtUtc.Value, account.TrialEndsAtUtc.Value, account.TrialMailingWindowDays.Value);

        var sub = await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == account.Id, ct);
        if (sub is not null)
            sub.MailingUntilUtc = account.TrialMailingWindowEndsAtUtc;

        // §336.3 — the option row itself (§333.3's materialization) carries its own PaidUntilUtc, the
        // mechanism IsOptionCurrentlyPaid already reads (§333.2); this keeps it in sync with the window
        // now that it actually has an end date instead of the null it was created with.
        var trialOptions = await db.AccountSubscriptionOptions
            .Where(o => o.BillingAccountId == account.Id && o.GrantedByTrial && o.EndsAtUtc == null)
            .ToListAsync(ct);
        foreach (var option in trialOptions)
            option.PaidUntilUtc = account.TrialMailingWindowEndsAtUtc;

        // §336.3 — one journal row when the window opens, same actor convention as trial-lifecycle's
        // own writes even though this particular row can also be written synchronously from the
        // request/webhook path rather than from the background task.
        db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
        {
            Id = Guid.NewGuid(),
            OwnerUserId = account.OwnerUserId,
            ChangedByUserId = TrialActors.System,
            ChangedAt = clampedAuthorizedUtc,
            BillingAccountId = account.Id,
            ChangeKind = SubscriptionChangeKind.TrialMailingWindow,
            Comment = account.TrialMailingWindowEndsAtUtc is { } end
                ? $"Окно бесплатных рассылок открыто, до {end:dd.MM.yyyy}"
                : "Окно бесплатных рассылок открыто",
        });
    }
}
