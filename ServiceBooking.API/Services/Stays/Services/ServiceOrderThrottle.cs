using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.7.1 п. 7, §39.7.8 — per-NUMBER limits of stand-alone orders, evaluated under the lock <c>stay-guest-phone:{phone}</c>. The counters are SEPARATE from
/// the ones of house bookings (<see cref="StayPhoneThrottle"/>) but live under the same lock of the number. The per-IP limit is the <c>stay-service-create</c> policy.
/// </summary>
public class ServiceOrderThrottle(AppDbContext db, IOptions<StaysOptions> options)
{
    public async Task<StayThrottleVerdict> CheckAsync(Guid companyId, string canonicalPhone, DateTime nowUtc, CancellationToken ct = default)
    {
        var limits = options.Value.Services.PhoneLimits;
        var held = await db.StayServiceOrders.AsNoTracking()
            .Where(o => o.GuestPhone == canonicalPhone && o.Status == StayBookingStatus.Held && o.HoldExpiresAtUtc > nowUtc)  // SUBJECT-PHONE-GATE: not-account-scoped — abuse throttle by the number typed at ordering; returns only counts, no subject data
            .Select(o => o.CompanyId).ToListAsync(ct);
        if (held.Count >= limits.MaxHeldPerPhone || held.Count(c => c == companyId) >= limits.MaxHeldPerPhonePerCompany) return StayThrottleVerdict.TooManyHeld;

        var since = nowUtc.AddHours(-24);
        var recent = await db.StayServiceOrders.AsNoTracking().CountAsync(o => o.GuestPhone == canonicalPhone && o.CreatedAtUtc >= since && !o.IsManual, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — abuse throttle by the number typed at ordering; returns only a count, no subject data
        return recent >= limits.MaxCreatedPerPhonePerDay ? StayThrottleVerdict.TooManyPerDay : StayThrottleVerdict.Ok;
    }
}
