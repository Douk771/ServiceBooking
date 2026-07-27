using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

public record EffectivePlan(
    bool AllowOnlineBooking,
    bool AllowMailing,
    bool AllowAnalytics,
    bool AllowPublicListing,
    bool AllowOnlinePayment,
    int? MaxEmployees,
    int? MaxCompanies)
{
    // No usable subscription → a restrictive baseline: no paid features, and an account may have just
    // one company with one employee (the owner alone). This is what blocks free-company spam and forces
    // an upgrade before a second branch or the first extra staff member can be added. Public listing is
    // the one flag that stays true on Free — otherwise every existing unsubscribed company would
    // silently vanish from the public directory.
    public static readonly EffectivePlan Free = new(
        AllowOnlineBooking: false, AllowMailing: false, AllowAnalytics: false,
        AllowPublicListing: true, AllowOnlinePayment: false, MaxEmployees: 1, MaxCompanies: 1);

    public static EffectivePlan FromConfig(SubscriptionPlanConfig c) => new(
        c.AllowOnlineBooking, c.AllowMailing, c.AllowAnalytics,
        c.AllowPublicListing, c.AllowOnlinePayment, c.MaxEmployees, c.MaxCompanies);
}

/// <summary>
/// Resolves the effective plan for an account. A subscription is bound to the account owner (the
/// company creator, <see cref="Company.OwnerUserId"/>), so every company they own shares one plan.
/// </summary>
public class SubscriptionResolver(AppDbContext db)
{
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
            var usable = sub is not null && sub.IsActive && (!sub.PaidUntil.HasValue || sub.PaidUntil >= now);
            result[id] = usable && sub!.PlanConfig is not null
                ? EffectivePlan.FromConfig(sub.PlanConfig)
                : EffectivePlan.Free;
        }
        return result;
    }
}
