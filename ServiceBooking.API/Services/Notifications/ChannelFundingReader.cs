using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>A channel's funding as the owner sees it: the §47.1 rank, its user-facing text, and the
/// paid-until date shown for it (the account's WhatsApp option, else its subscription period).</summary>
public sealed record ChannelFundingInfo(ChannelFundingState State, string Text, DateTime? PaidUntil);

/// <summary>
/// ARCHITECTURE_CYCLE7.md §47.1/§47.3 — whether a notification channel is actually paid for, read from
/// what the account bought (its effective plan's paid numbers ranked over its live channels, and the
/// notifications.whatsapp option's period), never from the channel row itself (its legacy PaidUntilUtc column is dropped in cycle 22).
///
/// Cycle 22 (ARCHITECTURE_CYCLE22.md §375 F14, §379): extracted from NotificationChannelsController's
/// private LoadFundingAsync/IsChannelFundedAsync with the same semantics, and BATCHED — one query per
/// kind of row for all the billing accounts involved, instead of three queries per account. Package P4
/// made it the single source for every former reader of NotificationChannel.PaidUntilUtc (admin channel
/// list/summary/suspend log, company notification settings/summary, ChannelHealthTask idle, replace).
/// </summary>
public sealed class ChannelFundingReader(AppDbContext db, SubscriptionResolver subscriptionResolver)
{
    /// <summary>Funding for every channel of every billing account the given channels belong to (a
    /// channel without a billing account gets no entry — callers treat that as NotPaid).</summary>
    /// <remarks><c>nowUtc</c> is "now" for the notifications.whatsapp option's EndsAtUtc filter (which option
    /// row supplies the paid-until); defaults to <see cref="DateTime.UtcNow"/>. A scheduled task passes its
    /// own <see cref="INotificationClock"/> instant here (ChannelHealthTask, debt C22-5). The funding
    /// STATE is not affected by it: it ranks channels by the effective plan's paid numbers, and plan
    /// resolution (<see cref="SubscriptionResolver.GetEffectivePlansForAccountsAsync"/>, which takes no
    /// "now") still reads the wall clock — the part of C22-5 that remains open.</remarks>
    public async Task<Dictionary<Guid, ChannelFundingInfo>> LoadAsync(
        IReadOnlyList<NotificationChannel> channels, CancellationToken ct = default, DateTime? nowUtc = null)
    {
        var result = new Dictionary<Guid, ChannelFundingInfo>();
        var accountIds = channels.Where(c => c.BillingAccountId.HasValue).Select(c => c.BillingAccountId!.Value).Distinct().ToList();
        if (accountIds.Count == 0) return result;

        var plans = await subscriptionResolver.GetEffectivePlansForAccountsAsync(accountIds);
        var siblingsByAccount = (await db.NotificationChannels.AsNoTracking()
                .Where(c => c.BillingAccountId != null && accountIds.Contains(c.BillingAccountId.Value))
                .ToListAsync(ct))
            .ToLookup(c => c.BillingAccountId!.Value);

        // N6, §47.3: the paid-until shown is the account's notifications.whatsapp option's, the channel
        // row has no paid period of its own (cycle 22 dropped its PaidUntilUtc column). An option row with no own PaidUntilUtc
        // rides the subscription's own paid period instead (same convention SubscriptionResolver uses),
        // so falls back to the subscription's PaidUntil. One option row per account is looked at — the
        // first one the database returns, as the per-account FirstOrDefault did. "Now" for the EndsAtUtc
        // filter is the caller's (a task's INotificationClock), else the wall clock.
        var now = nowUtc ?? DateTime.UtcNow;
        var whatsappOptionByAccount = (await db.AccountSubscriptionOptions
                .Include(o => o.Option)
                .Where(o => accountIds.Contains(o.BillingAccountId) && o.Option.Code == SubscriptionResolver.WhatsAppOptionCode)
                .Where(o => o.EndsAtUtc == null || o.EndsAtUtc > now)
                .ToListAsync(ct))
            .GroupBy(o => o.BillingAccountId)
            .ToDictionary(g => g.Key, g => g.First());
        var subscriptionPaidUntilByAccount = (await db.AccountSubscriptions
                .Where(s => s.BillingAccountId != null && accountIds.Contains(s.BillingAccountId.Value))
                .Select(s => new { BillingAccountId = s.BillingAccountId!.Value, s.PaidUntil })
                .ToListAsync(ct))
            .GroupBy(s => s.BillingAccountId)
            .ToDictionary(g => g.Key, g => g.First().PaidUntil);

        foreach (var accountId in accountIds)
        {
            var plan = plans.TryGetValue(accountId, out var p) ? p : EffectivePlan.Free;
            var siblings = siblingsByAccount[accountId].ToList();
            var live = siblings.Where(c => c.State != ChannelState.Replaced).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList();
            var ranking = ChannelFunding.Rank(siblings, plan.PaidNotificationNumbers);
            var workingChannel = live.FirstOrDefault(c => ranking.TryGetValue(c.Id, out var s) && s == ChannelFundingState.Funded);
            var workingMasked = workingChannel?.PhoneNumber is null ? null : PhoneDisplayMask.Mask(workingChannel.PhoneNumber);

            var whatsappOption = whatsappOptionByAccount.GetValueOrDefault(accountId);
            var subPaidUntil = whatsappOption?.PaidUntilUtc is null
                ? subscriptionPaidUntilByAccount.GetValueOrDefault(accountId)
                : null;
            var paidUntil = whatsappOption?.PaidUntilUtc ?? subPaidUntil;

            foreach (var c in siblings)
            {
                var state = ranking.TryGetValue(c.Id, out var s2) ? s2 : ChannelFundingState.NotPaid;
                var text = BillingTexts.FundingText(state, plan.PaidNotificationNumbers, live.Count, workingMasked);
                result[c.Id] = new ChannelFundingInfo(state, text, paidUntil);
            }
        }
        return result;
    }

    /// <summary>§47.1/§47.2: is this one channel within its account's paid numbers under
    /// <paramref name="plan"/>? No channel, or one without a billing account, never is.</summary>
    public async Task<bool> IsFundedAsync(NotificationChannel? channel, EffectivePlan plan, CancellationToken ct = default)
    {
        if (channel?.BillingAccountId is not { } accountId) return false;
        var siblings = await db.NotificationChannels.AsNoTracking().Where(c => c.BillingAccountId == accountId).ToListAsync(ct);
        var ranking = ChannelFunding.Rank(siblings, plan.PaidNotificationNumbers);
        return ranking.TryGetValue(channel.Id, out var state) && state == ChannelFundingState.Funded;
    }
}
