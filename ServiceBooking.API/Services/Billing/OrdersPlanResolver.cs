using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §459.2 — what the "Заказы" line of an account allows. <c>Usable</c> is true for every resolved plan, the free
/// tier included; an empty limit means "no limit".
/// </summary>
public sealed record OrdersPlan(
    Guid? PlanId, string PlanName, bool IsFreeTier, bool Usable, bool AllowOrders, int? MaxShops, int? MaxSeats,
    int? MaxProductsPerShop, int? MaxOrdersPerMonth,
    // ARCHITECTURE_CYCLE25.md §505.1 (Q-25-7): whether the plan lets the account's shops appear in the goods catalog (the plan's own AllowPublicListing).
    bool AllowPublicListing = true)
{
    /// <summary>The numbers of the seeded free tariff (§448.3) — used when the system free tariff of the line is missing from the database.</summary>
    public static OrdersPlan FallbackFree { get; } = new(
        OrdersFreePlan.SeedId, OrdersFreePlan.Name, IsFreeTier: true, Usable: true, AllowOrders: true, MaxShops: 1, MaxSeats: 2,
        MaxProductsPerShop: 50, MaxOrdersPerMonth: 150);
}

/// <summary>
/// The "Заказы" line's own subscription lives in <see cref="OrdersSubscription"/> (§459.1); no row / an unusable one → the system free
/// tariff of the line. <see cref="Resolve"/> is pure; the two async methods only load its inputs, in batches.
/// </summary>
public class OrdersPlanResolver(AppDbContext db)
{
    public static OrdersPlan Resolve(OrdersSubscription? sub, SubscriptionPlanConfig? systemFreeOrders, DateTime nowUtc)
    {
        var plan = sub?.PlanConfig;
        if (sub is { IsActive: true } && (sub.PaidUntil is null || sub.PaidUntil >= nowUtc) &&
            plan is { IsActive: true, Line: CompanyKind.Orders })
            return From(plan, isFree: false);

        return systemFreeOrders is { Line: CompanyKind.Orders } free ? From(free, isFree: true) : OrdersPlan.FallbackFree;
    }

    private static OrdersPlan From(SubscriptionPlanConfig plan, bool isFree) => new(
        plan.Id, plan.Name, isFree, Usable: true, plan.AllowOrders, plan.MaxCompanies, plan.MaxEmployees, plan.MaxProductsPerShop,
        plan.MaxOrdersPerMonth, plan.AllowPublicListing);

    /// <summary>The plans of many accounts with two queries (subscriptions with their tariffs + the system free tariff).</summary>
    public async Task<Dictionary<Guid, OrdersPlan>> GetForAccountsAsync(IEnumerable<Guid> accountIds, CancellationToken ct = default)
    {
        var ids = accountIds.Distinct().ToList();
        var subs = await db.OrdersSubscriptions.AsNoTracking().Include(s => s.PlanConfig)
            .Where(s => ids.Contains(s.BillingAccountId)).ToDictionaryAsync(s => s.BillingAccountId, ct);
        var free = await LoadSystemFreeAsync(ct);
        var now = DateTime.UtcNow;
        return ids.ToDictionary(id => id, id => Resolve(subs.GetValueOrDefault(id), free, now));
    }

    public async Task<OrdersPlan> GetForAccountAsync(Guid accountId, CancellationToken ct = default) =>
        (await GetForAccountsAsync([accountId], ct))[accountId];

    /// <summary>The plan of the account a shop belongs to. A shop without an account (should not happen) gets the free tier.</summary>
    public async Task<OrdersPlan> GetForCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        var accountId = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BillingAccountId).FirstOrDefaultAsync(ct);
        return accountId is { } id ? await GetForAccountAsync(id, ct) : Resolve(null, await LoadSystemFreeAsync(ct), DateTime.UtcNow);
    }

    private Task<SubscriptionPlanConfig?> LoadSystemFreeAsync(CancellationToken ct) =>
        db.SubscriptionPlanConfigs.AsNoTracking().FirstOrDefaultAsync(p => p.IsSystemFree && p.Line == CompanyKind.Orders, ct);
}
