using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

public record EffectivePlan(
    bool AllowOnlineBooking,
    bool AllowMailing,
    bool AllowAnalytics,
    bool AllowPublicListing,
    bool AllowOnlinePayment,
    // Cycle 7 (ARCHITECTURE_CYCLE7.md §44.1) — renamed on purpose (was MaxEmployees/MaxCompanies,
    // enforced per company): the limit is now SUMMED across every company the billing account owns.
    // The rename is a deliberately breaking change — a positional/named-argument mismatch here is a
    // compile error, not a silent behavior change (§45.3 p.2).
    int? AccountMaxEmployees,
    int? AccountMaxCompanies,
    // Client-note photo storage cap in MB; null = unlimited (US-24, ARCHITECTURE.md §7.1).
    int? PhotoQuotaMb,
    PhotoRetention PhotoRetention)
{
    // No usable subscription → a restrictive baseline: no paid features, and an account may have just
    // one company. Cycle 28 (customer decision Q28-1, ARCHITECTURE_CYCLE28.md §583.4): the free tier "Старт" now
    // includes online booking and two employees (was: no online booking, one employee) — "online booking is the product,
    // without it the free pages do not work as advertising" (tariff draft §5, cycle 28). It changes behaviour for EVERY
    // existing free company. This still blocks free-company spam (one company) and forces an upgrade before a second
    // branch or the third staff member can be added. Public listing is
    // the one flag that stays true on Free — otherwise every existing unsubscribed company would
    // silently vanish from the public directory. Photo limits get the same treatment as everything else
    // here: a low but non-zero baseline (SPEC US-24 p.2, "low-risk assumption") rather than 0/null,
    // which would either block every upload or grant unlimited storage to unpaid accounts.
    public static readonly EffectivePlan Free = new(
        AllowOnlineBooking: true, AllowMailing: false, AllowAnalytics: false,
        AllowPublicListing: true, AllowOnlinePayment: false, AccountMaxEmployees: 2, AccountMaxCompanies: 1,
        PhotoQuotaMb: 100, PhotoRetention: PhotoRetention.SixMonths);

    public static EffectivePlan FromConfig(SubscriptionPlanConfig c) => new(
        c.AllowOnlineBooking, c.AllowMailing, c.AllowAnalytics,
        c.AllowPublicListing, c.AllowOnlinePayment, c.MaxEmployees, c.MaxCompanies,
        c.PhotoQuotaMb, c.PhotoRetention);
}

/// <summary>
/// Resolves the effective plan for a billing account (ARCHITECTURE_CYCLE7.md §43.2, §44, §45). A
/// subscription is bound to the account, not to a person or a single company — every company the
/// account owns shares one plan. Money reads go through <see cref="BillingAccount"/>/
/// <see cref="Company.BillingAccountId"/> from here on; <c>Company.OwnerUserId</c> stays a
/// rights/visibility question (§45.2) answered elsewhere.
/// </summary>
public class SubscriptionResolver(AppDbContext db)
{
    /// <summary>ARCHITECTURE_CYCLE7.md §43.3/§47.1 — the catalog code of the WhatsApp channel option. Since cycle 40 payment is decided
    /// per transport by <c>AccountMessagingReader</c> / <c>ChannelOptionFunding</c> (this resolver reads no option row); the code stays
    /// as the shared literal (<c>ChannelOptionCodes.WhatsApp</c> equals it).</summary>
    public const string WhatsAppOptionCode = "notifications.whatsapp";

    /// <summary>
    /// ARCHITECTURE_CYCLE19.md §384.2/§400 — pure plan-resolution rule: no DB access, "now" is passed
    /// in so it can be unit-tested. <paramref name="grandfatheredEmployeeBonus"/> is
    /// <see cref="BillingAccount.GrandfatheredEmployeeBonus"/> — added to the seat limit regardless of
    /// subscription state (it survives a downgrade to Free by design, §54.4/§44.3 p.8) but never
    /// surfaced as money. Purchased "extra-*" options are never read here (cycle 19 removed the
    /// 6-argument overload that summed them in) — the whole limit is
    /// <see cref="AccountLimitFormula.Compute"/> over the base plan's fields and the bonus.
    /// </summary>
    public static EffectivePlan Resolve(
        AccountSubscription? sub, int grandfatheredEmployeeBonus, DateTime nowUtc)
    {
        var usable = SubscriptionUsability.IsUsable(sub, nowUtc);
        // A plan an admin has deactivated (SubscriptionPlanConfig.IsActive == false, e.g. discontinued)
        // must fall back to Free even for an account still actively subscribed to it — otherwise a
        // deleted plan keeps granting its features forever to whoever was on it when it was retired.
        var basePlan = usable && sub!.PlanConfig is { IsActive: true }
            ? EffectivePlan.FromConfig(sub.PlanConfig)
            : EffectivePlan.Free;

        var (employees, companies) = AccountLimitFormula.Compute(
            basePlan.AccountMaxEmployees, basePlan.AccountMaxCompanies, grandfatheredEmployeeBonus);
        var plan = basePlan with { AccountMaxEmployees = employees, AccountMaxCompanies = companies };

        // Cycle 18 (ARCHITECTURE_CYCLE18.md §333.2). Mailing capabilities of a subscription may have
        // their OWN, shorter deadline — exactly like a purchased option (N12: "an option can't outlive
        // the subscription"). This rule is general for any plan; the trial is its first consumer, but
        // the resolver knows nothing about trials (R8 SPEC) — grep -n "IsSystemTrial" against this file
        // must stay empty.
        var mailingUsable = usable && (sub!.MailingUntilUtc is null || sub.MailingUntilUtc >= nowUtc);
        if (!mailingUsable)
            plan = plan with { AllowMailing = false };

        return plan;
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

        // §375 F18: a dictionary lookup per company, not a linear FirstOrDefault scan (Id is the key).
        var accountByCompany = companies.ToDictionary(c => c.Id, c => c.BillingAccountId);
        return ids.ToDictionary(
            id => id,
            id =>
            {
                var accountId = accountByCompany.GetValueOrDefault(id);
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

        var result = new Dictionary<Guid, EffectivePlan>();
        foreach (var id in ids)
        {
            var sub = subs.FirstOrDefault(s => s.BillingAccountId == id);
            var bonus = bonuses.FirstOrDefault(b => b.Id == id)?.GrandfatheredEmployeeBonus ?? 0;
            result[id] = Resolve(sub, bonus, now);
        }
        return result;
    }
}
