using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

public record EffectivePlan(
    bool AllowOnlineBooking,
    bool AllowMailing,
    bool AllowAnalytics,
    bool AllowPublicListing,
    bool AllowOnlinePayment,
    // Cycle 5 (ARCHITECTURE_CYCLE5.md §44.1) — renamed on purpose (was MaxEmployees/MaxCompanies,
    // enforced per company): the limit is now SUMMED across every company the billing account owns.
    // The rename is a deliberately breaking change — a positional/named-argument mismatch here is a
    // compile error, not a silent behavior change (§45.3 p.2).
    int? AccountMaxEmployees,
    int? AccountMaxCompanies,
    // Client-note photo storage cap in MB; null = unlimited (US-24, ARCHITECTURE.md §7.1).
    int? PhotoQuotaMb,
    PhotoRetention PhotoRetention,
    // Cycle 4 (ARCHITECTURE_CYCLE4.md §33): whether this plan may buy the WhatsApp channel option.
    // False on Free (Q1). Deliberately NOT given a default value: every existing positional construction
    // site (Free below, FromConfig, and every unit test that builds an EffectivePlan positionally) must
    // say explicitly whether the plan it's describing allows the option, rather than silently inheriting
    // false from a default and hiding a forgotten decision.
    bool AllowNotificationChannel,
    // Cycle 5 (ARCHITECTURE_CYCLE5.md §47.1) — "N": how many notification numbers the account's
    // AccountSubscriptionOptions row for "notifications.whatsapp" currently pays for. 0 means "the
    // option isn't paid at all" (NotificationGate blocks with NotOnPaidPlan before ChannelFunding.Rank
    // is even consulted). NOT the same axis as AllowNotificationChannel above (which is "may this PLAN
    // buy the option at all") — an account can be on a plan that allows the option yet have 0 paid
    // right now (never bought it, or let it lapse).
    int PaidNotificationNumbers = 0)
{
    // No usable subscription → a restrictive baseline: no paid features, and an account may have just
    // one company with one employee (the owner alone). This is what blocks free-company spam and forces
    // an upgrade before a second branch or the first extra staff member can be added. Public listing is
    // the one flag that stays true on Free — otherwise every existing unsubscribed company would
    // silently vanish from the public directory. Photo limits get the same treatment as everything else
    // here: a low but non-zero baseline (SPEC US-24 p.2, "low-risk assumption") rather than 0/null,
    // which would either block every upload or grant unlimited storage to unpaid accounts.
    public static readonly EffectivePlan Free = new(
        AllowOnlineBooking: false, AllowMailing: false, AllowAnalytics: false,
        AllowPublicListing: true, AllowOnlinePayment: false, AccountMaxEmployees: 1, AccountMaxCompanies: 1,
        PhotoQuotaMb: 100, PhotoRetention: PhotoRetention.SixMonths, AllowNotificationChannel: false);

    public static EffectivePlan FromConfig(SubscriptionPlanConfig c) => new(
        c.AllowOnlineBooking, c.AllowMailing, c.AllowAnalytics,
        c.AllowPublicListing, c.AllowOnlinePayment, c.MaxEmployees, c.MaxCompanies,
        c.PhotoQuotaMb, c.PhotoRetention, c.AllowNotificationChannel);
}

/// <summary>
/// Resolves the effective plan for a billing account (ARCHITECTURE_CYCLE5.md §43.2, §44, §45). A
/// subscription is bound to the account, not to a person or a single company — every company the
/// account owns shares one plan. Money reads go through <see cref="BillingAccount"/>/
/// <see cref="Company.BillingAccountId"/> from here on; <c>Company.OwnerUserId</c> stays a
/// rights/visibility question (§45.2) answered elsewhere.
/// </summary>
public class SubscriptionResolver(AppDbContext db)
{
    /// <summary>ARCHITECTURE_CYCLE5.md §43.3/§47.1 — the catalog code for the notification-channel
    /// quantity option; the single source of truth for "how many numbers are paid for" reads this
    /// exact code from AccountSubscriptionOptions.</summary>
    public const string WhatsAppOptionCode = "notifications.whatsapp";

    /// <summary>
    /// Pure plan-resolution rule: no DB access, "now" is passed in so it can be unit-tested.
    /// <paramref name="grandfatheredEmployeeBonus"/> is <see cref="BillingAccount.GrandfatheredEmployeeBonus"/>
    /// — added to the seat limit regardless of subscription state (it survives a downgrade to Free by
    /// design, §54.4/§44.3 p.8) but never surfaced as money.
    /// </summary>
    public static EffectivePlan Resolve(
        AccountSubscription? sub, int grandfatheredEmployeeBonus, int paidNotificationNumbers, DateTime nowUtc) =>
        Resolve(sub, grandfatheredEmployeeBonus, extraEmployees: 0, extraCompanies: 0, paidNotificationNumbers, nowUtc);

    /// <summary>
    /// ARCHITECTURE_CYCLE5.md §44.3 п.6: employees = plan.MaxEmployees + Σ quantity of options whose
    /// CapabilityKey is <see cref="CapabilityKeys.Employees"/> + grandfathered bonus; companies =
    /// plan.MaxCompanies + Σ quantity of options whose CapabilityKey is
    /// <see cref="CapabilityKeys.Companies"/>. <paramref name="extraEmployees"/>
    /// and <paramref name="extraCompanies"/> must already be pre-filtered by the caller to options that
    /// are currently paid (own PaidUntilUtc, or riding the subscription's own paid period) — this pure
    /// method only applies the arithmetic, it does not re-derive "is this option still paid".
    /// </summary>
    public static EffectivePlan Resolve(
        AccountSubscription? sub, int grandfatheredEmployeeBonus, int extraEmployees, int extraCompanies,
        int paidNotificationNumbers, DateTime nowUtc)
    {
        var usable = sub is not null && sub.IsActive && (!sub.PaidUntil.HasValue || sub.PaidUntil >= nowUtc);
        // A plan an admin has deactivated (SubscriptionPlanConfig.IsActive == false, e.g. discontinued)
        // must fall back to Free even for an account still actively subscribed to it — otherwise a
        // deleted plan keeps granting its features forever to whoever was on it when it was retired.
        var basePlan = usable && sub!.PlanConfig is { IsActive: true }
            ? EffectivePlan.FromConfig(sub.PlanConfig)
            : EffectivePlan.Free;

        var employeeBonus = grandfatheredEmployeeBonus + Math.Max(extraEmployees, 0);
        var plan = employeeBonus <= 0
            ? basePlan
            : basePlan with
            {
                AccountMaxEmployees = basePlan.AccountMaxEmployees is { } max ? max + employeeBonus : null,
            };

        plan = extraCompanies <= 0
            ? plan
            : plan with
            {
                AccountMaxCompanies = plan.AccountMaxCompanies is { } maxC ? maxC + extraCompanies : null,
            };

        return paidNotificationNumbers <= 0 ? plan : plan with { PaidNotificationNumbers = paidNotificationNumbers };
    }

    /// <summary>Resolves the effective plan for a company by looking up its billing account.</summary>
    public async Task<EffectivePlan> GetEffectivePlanAsync(Guid companyId)
    {
        var accountId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BillingAccountId).FirstOrDefaultAsync();
        if (accountId is null) return EffectivePlan.Free;
        return await GetEffectivePlanForAccountAsync(accountId.Value);
    }

    /// <summary>
    /// Resolves effective plans for several companies in one round trip (avoids N+1 in list endpoints
    /// like CompaniesController.GetAll/GetMy/GetMemberOf). Sequence and grouping: companies → their
    /// billing accounts → each account's plan (§44.4) — this signature is the one thing this cycle does
    /// NOT change, everything else is free to change underneath it.
    /// </summary>
    public async Task<Dictionary<Guid, EffectivePlan>> GetEffectivePlansAsync(IEnumerable<Guid> companyIds)
    {
        var ids = companyIds.Distinct().ToList();
        var companies = await db.Companies
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.BillingAccountId })
            .ToListAsync();

        var accountIds = companies.Where(c => c.BillingAccountId.HasValue).Select(c => c.BillingAccountId!.Value).Distinct();
        var accountPlans = await GetEffectivePlansForAccountsAsync(accountIds);

        return ids.ToDictionary(
            id => id,
            id =>
            {
                var accountId = companies.FirstOrDefault(c => c.Id == id)?.BillingAccountId;
                return accountId.HasValue && accountPlans.TryGetValue(accountId.Value, out var plan) ? plan : EffectivePlan.Free;
            });
    }

    /// <summary>Resolves the effective plan for a single billing account.</summary>
    public async Task<EffectivePlan> GetEffectivePlanForAccountAsync(Guid accountId)
    {
        var plans = await GetEffectivePlansForAccountsAsync([accountId]);
        return plans[accountId];
    }

    /// <summary>
    /// Batch account → plan resolution (§44.4: two grouped queries total, no N+1). Public — also used by
    /// the cycle-4 admin channel summary (T4-B11), which resolves plans for a page of channels' billing
    /// accounts.
    /// </summary>
    public async Task<Dictionary<Guid, EffectivePlan>> GetEffectivePlansForAccountsAsync(IEnumerable<Guid> accountIds)
    {
        var ids = accountIds.Distinct().ToList();
        var now = DateTime.UtcNow;

        var subs = await db.AccountSubscriptions
            .Include(s => s.PlanConfig)
            .Where(s => s.BillingAccountId != null && ids.Contains(s.BillingAccountId!.Value))
            .ToListAsync();

        var bonuses = await db.BillingAccounts
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.GrandfatheredEmployeeBonus })
            .ToListAsync();

        // Cycle 5 (§44.1-§44.3): every option row still in force (own PaidUntilUtc still in the future,
        // or none — meaning "rides the subscription's own paid period" — and not EndsAtUtc'd), grouped
        // by CapabilityKey. This is the one place an option's quantity turns into a plan effect —
        // adding a new "extra-*" catalog row (US-76) needs no code change here.
        var activeOptions = await db.AccountSubscriptionOptions
            .Include(o => o.Option)
            .Where(o => ids.Contains(o.BillingAccountId))
            .Where(o => o.EndsAtUtc == null || o.EndsAtUtc > now)
            .ToListAsync();

        var result = new Dictionary<Guid, EffectivePlan>();
        foreach (var id in ids)
        {
            var sub = subs.FirstOrDefault(s => s.BillingAccountId == id);
            var bonus = bonuses.FirstOrDefault(b => b.Id == id)?.GrandfatheredEmployeeBonus ?? 0;
            var subUsable = sub is not null && sub.IsActive && (!sub.PaidUntil.HasValue || sub.PaidUntil >= now);

            // "Currently paid" quantity for one option row: its own PaidUntilUtc if set, otherwise it
            // lives and dies with the subscription's own paid period (N12: an option cannot outlive an
            // expired subscription unless it carries its own paid-through date).
            int PaidQuantity(AccountSubscriptionOption o) => o.PaidUntilUtc.HasValue
                ? (o.PaidUntilUtc.Value >= now ? o.Quantity : 0)
                : (subUsable ? o.Quantity : 0);

            var accountOptions = activeOptions.Where(o => o.BillingAccountId == id).ToList();
            var extraEmployees = accountOptions.Where(o => o.Option.CapabilityKey == CapabilityKeys.Employees).Sum(PaidQuantity);
            var extraCompanies = accountOptions.Where(o => o.Option.CapabilityKey == CapabilityKeys.Companies).Sum(PaidQuantity);
            var whatsapp = accountOptions.FirstOrDefault(o => o.Option.Code == WhatsAppOptionCode);
            var paidNumbers = whatsapp is null ? 0 : PaidQuantity(whatsapp);

            result[id] = Resolve(sub, bonus, extraEmployees, extraCompanies, paidNumbers, now);
        }
        return result;
    }
}
