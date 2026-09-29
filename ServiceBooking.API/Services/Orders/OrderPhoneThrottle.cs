using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §395.2 step 6 — the per-phone limits of order creation, by the database (the IP/user limit is the
/// "order-create" rate-limit policy): active orders of this number in this shop, and orders of this number on the whole
/// platform in the last 24 hours. Both counts are of orders that exist — cancelled ones still count towards the daily limit.
/// </summary>
public class OrderPhoneThrottle(AppDbContext db, IOptions<OrdersOptions> options)
{
    public async Task<bool> IsLimitedAsync(Guid companyId, string canonicalPhone, DateTime nowUtc, CancellationToken ct = default)
    {
        var limits = options.Value.PhoneLimits;

        var active = await db.Orders.AsNoTracking().CountAsync(o =>
            o.CompanyId == companyId && o.CustomerPhone == canonicalPhone &&  // SUBJECT-PHONE-GATE: not-account-scoped — abuse throttle by the number typed at checkout; returns only a count, no subject data
            (o.Status == OrderStatus.New || o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Ready), ct);
        if (active >= limits.MaxActivePerShop) return true;

        var since = nowUtc.AddHours(-24);
        var recent = await db.Orders.AsNoTracking().CountAsync(o =>
            o.CustomerPhone == canonicalPhone && o.CreatedAtUtc >= since, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — abuse throttle by the number typed at checkout; returns only a count, no subject data
        return recent >= limits.MaxPerDay;
    }
}
