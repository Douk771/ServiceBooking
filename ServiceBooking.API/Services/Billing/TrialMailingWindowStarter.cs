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

        // No trial ever granted, or the window already started once — never restart it (Д5).
        if (account.TrialStartedAtUtc is null) return;
        if (account.TrialChannelFirstAuthorizedAtUtc is not null) return;
        if (account.TrialEndsAtUtc is null || account.TrialMailingWindowDays is null) return;

        account.TrialChannelFirstAuthorizedAtUtc = nowUtc;
        account.TrialMailingWindowEndsAtUtc = TrialWindow.WindowEnd(
            nowUtc, account.TrialStartedAtUtc.Value, account.TrialEndsAtUtc.Value, account.TrialMailingWindowDays.Value);

        var sub = await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == billingAccountId, ct);
        if (sub is not null)
            sub.MailingUntilUtc = account.TrialMailingWindowEndsAtUtc;

        // §336.3 — one journal row when the window opens, same actor convention as trial-lifecycle's
        // own writes even though this particular row is written synchronously from the request/webhook
        // path rather than from the background task.
        db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
        {
            Id = Guid.NewGuid(),
            OwnerUserId = account.OwnerUserId,
            ChangedByUserId = TrialActors.System,
            ChangedAt = nowUtc,
            BillingAccountId = billingAccountId,
            ChangeKind = SubscriptionChangeKind.TrialMailingWindow,
            Comment = account.TrialMailingWindowEndsAtUtc is { } end
                ? $"Окно бесплатных рассылок открыто, до {end:dd.MM.yyyy}"
                : "Окно бесплатных рассылок открыто",
        });
    }
}
