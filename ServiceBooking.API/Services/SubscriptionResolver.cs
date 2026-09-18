using Microsoft.EntityFrameworkCore;
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
    int? MaxEmployees,
    int? MaxCompanies,
    // Client-note photo storage cap in MB; null = unlimited (US-24, ARCHITECTURE.md §7.1).
    int? PhotoQuotaMb,
    PhotoRetention PhotoRetention,
    // Cycle 4 (ARCHITECTURE_CYCLE4.md §33): whether this plan may buy the WhatsApp channel option.
    // False on Free (Q1). Deliberately NOT given a default value: every existing positional construction
    // site (Free below, FromConfig, and every unit test that builds an EffectivePlan positionally) must
    // say explicitly whether the plan it's describing allows the option, rather than silently inheriting
    // false from a default and hiding a forgotten decision.
    bool AllowNotificationChannel)
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
        AllowPublicListing: true, AllowOnlinePayment: false, MaxEmployees: 1, MaxCompanies: 1,
        PhotoQuotaMb: 100, PhotoRetention: PhotoRetention.SixMonths, AllowNotificationChannel: false);

    public static EffectivePlan FromConfig(SubscriptionPlanConfig c) => new(
        c.AllowOnlineBooking, c.AllowMailing, c.AllowAnalytics,
        c.AllowPublicListing, c.AllowOnlinePayment, c.MaxEmployees, c.MaxCompanies,
        c.PhotoQuotaMb, c.PhotoRetention, c.AllowNotificationChannel);
}

/// <summary>
/// Resolves the effective plan for an account. A subscription is bound to the account owner (the
/// company creator, <see cref="Company.OwnerUserId"/>), so every company they own shares one plan.
/// </summary>
public class SubscriptionResolver(AppDbContext db)
{
    /// <summary>Pure plan-resolution rule: no DB access, "now" is passed in so it can be unit-tested.</summary>
    public static EffectivePlan Resolve(AccountSubscription? sub, DateTime nowUtc)
    {
        var usable = sub is not null && sub.IsActive && (!sub.PaidUntil.HasValue || sub.PaidUntil >= nowUtc);
        // A plan an admin has deactivated (SubscriptionPlanConfig.IsActive == false, e.g. discontinued)
        // must fall back to Free even for an owner who is still actively subscribed to it — otherwise a
        // deleted plan keeps granting its features forever to whoever was on it when it was retired.
        return usable && sub!.PlanConfig is { IsActive: true }
            ? EffectivePlan.FromConfig(sub.PlanConfig)
            : EffectivePlan.Free;
    }

    public async Task<EffectivePlan> GetEffectivePlanForOwnerAsync(string ownerUserId)
    {
        var plans = await GetEffectivePlansForOwnersAsync([ownerUserId]);
        return plans[ownerUserId];
    }

    /// <summary>Resolves the effective plan for a company by looking up its owner's account subscription.</summary>
    public async Task<EffectivePlan> GetEffectivePlanAsync(Guid companyId)
    {
        var ownerId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.OwnerUserId).FirstOrDefaultAsync();
        if (ownerId is null) return EffectivePlan.Free;
        return await GetEffectivePlanForOwnerAsync(ownerId);
    }

    /// <summary>
    /// Resolves effective plans for several companies in one round trip (avoids N+1 in list endpoints
    /// like CompaniesController.GetAll/GetMy/GetMemberOf). Each company resolves through its owner.
    /// </summary>
    public async Task<Dictionary<Guid, EffectivePlan>> GetEffectivePlansAsync(IEnumerable<Guid> companyIds)
    {
        var ids = companyIds.Distinct().ToList();
        var owners = await db.Companies
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.OwnerUserId })
            .ToListAsync();

        var ownerPlans = await GetEffectivePlansForOwnersAsync(owners.Select(o => o.OwnerUserId));
        return ids.ToDictionary(
            id => id,
            id =>
            {
                var ownerId = owners.FirstOrDefault(o => o.Id == id)?.OwnerUserId;
                return ownerId is not null ? ownerPlans[ownerId] : EffectivePlan.Free;
            });
    }

    private async Task<Dictionary<string, EffectivePlan>> GetEffectivePlansForOwnersAsync(IEnumerable<string> ownerUserIds)
    {
        var ids = ownerUserIds.Distinct().ToList();
        var now = DateTime.UtcNow;

        var subs = await db.AccountSubscriptions
            .Include(s => s.PlanConfig)
            .Where(s => ids.Contains(s.OwnerUserId))
            .ToListAsync();

        var result = new Dictionary<string, EffectivePlan>();
        foreach (var id in ids)
        {
            var sub = subs.FirstOrDefault(s => s.OwnerUserId == id);
            result[id] = Resolve(sub, now);
        }
        return result;
    }
}
