using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Orders.Notifications;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §459.6 — the 80 % / 100 % warnings of the monthly order limit. Runs inside the creation transaction right after the
/// counter was incremented: when the count crosses <c>ceil(0.8 × limit)</c> (and again at the limit) the flag on the counter row is set — exactly
/// once, by an UPDATE guarded with <c>IS NULL</c> — and a push to the account owner is queued on the devices they subscribed from goods.
/// </summary>
public class OrderLimitWarner(
    AppDbContext db, OrderMonthlyCounter counter, OrderStaffPushQueue pushQueue, OrderStaffMaxQueue maxQueue,
    ServiceBooking.API.Services.StaffMax.StaffMaxAvailability maxAvailability, ServiceBooking.API.Services.PublicSites.PublicSiteLinks links)
{
    public async Task AfterIncrementAsync(
        Company shop, Guid billingAccountId, DateOnly month, MonthlyUsage usage, OrdersPlan plan, DateTime nowUtc, CancellationToken ct)
    {
        if (plan.MaxOrdersPerMonth is not { } limit) return;

        var reached = usage.Count >= limit;
        var warnReached = reached && usage.Warned100AtUtc is null;
        var warn80 = !reached && usage.Count >= OrderLimitRules.Warning80Threshold(limit) && usage.Warned80AtUtc is null;
        if (!warnReached && !warn80) return;

        // Both thresholds can coincide (limit 1): the stronger message is the only one, and it settles the weaker flag too.
        if (warnReached && usage.Warned80AtUtc is null) await counter.MarkWarnedAsync(billingAccountId, month, reached: false, nowUtc, ct);
        if (!await counter.MarkWarnedAsync(billingAccountId, month, reached, nowUtc, ct)) return;

        var ownerId = await db.BillingAccounts.AsNoTracking().Where(a => a.Id == billingAccountId).Select(a => a.OwnerUserId).FirstOrDefaultAsync(ct);
        if (ownerId is null) return;

        var payload = OrderNotificationTexts.OwnerOrderLimitWarning(reached, usage.Count, limit, month);
        var keySeed = $"{NotificationType.OwnerOrderLimitWarning}:{billingAccountId}:{month:yyyy-MM}:{(reached ? "reached" : "80")}";
        await pushQueue.QueueForOwnerAsync(shop.Id, ownerId, keySeed, payload, ct);

        // ARCHITECTURE_CYCLE25.md §499.2 (P1, US-25-04): the same warning to the owner's MAX chat, with the same key seed. The shop's own MAX switch does
        // not hold it back (like push, §459.6) — only the platform switch does.
        if (maxAvailability.Enabled)
            await maxQueue.QueueForOwnerAsync(
                shop.Id, ownerId, keySeed,
                ServiceBooking.API.Services.StaffMax.StaffMaxTexts.OwnerOrderLimitWarning(reached, usage.Count, limit, month, links.OrdersSubscriptionUrl()), ct);
    }
}
