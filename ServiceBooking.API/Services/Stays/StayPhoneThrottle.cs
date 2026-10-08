using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

public enum StayThrottleVerdict { Ok, TooManyHeld, TooManyPerDay }

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.7.1 step 6 — per-NUMBER limits of guest bookings, evaluated under the lock <c>stay-guest-phone:{phone}</c>
/// (the per-IP limit is the <c>stay-create</c> policy). Texts of the 429 answers: <see cref="HeldText"/>, <see cref="PerDayText"/>.
/// </summary>
public class StayPhoneThrottle(AppDbContext db, IOptions<StaysOptions> options)
{
    public const string HeldText = "Слишком много неоплаченных броней. Оплатите или отмените текущую бронь";
    public const string PerDayText = "Слишком много броней с этого номера. Попробуйте позже";

    public async Task<StayThrottleVerdict> CheckAsync(Guid companyId, string canonicalPhone, DateTime nowUtc, CancellationToken ct = default)
    {
        var limits = options.Value.PhoneLimits;
        var held = await db.StayBookings.AsNoTracking()
            .Where(b => b.GuestPhone == canonicalPhone && b.Status == StayBookingStatus.Held && b.HoldExpiresAtUtc > nowUtc)  // SUBJECT-PHONE-GATE: not-account-scoped — abuse throttle by the number typed at booking; returns only counts, no subject data
            .Select(b => b.CompanyId).ToListAsync(ct);
        if (held.Count >= limits.MaxHeldPerPhone || held.Count(c => c == companyId) >= limits.MaxHeldPerPhonePerCompany) return StayThrottleVerdict.TooManyHeld;

        var since = nowUtc.AddHours(-24);
        var recent = await db.StayBookings.AsNoTracking().CountAsync(b => b.GuestPhone == canonicalPhone && b.CreatedAtUtc >= since && !b.IsManual, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — abuse throttle by the number typed at booking; returns only a count, no subject data
        return recent >= limits.MaxCreatedPerPhonePerDay ? StayThrottleVerdict.TooManyPerDay : StayThrottleVerdict.Ok;
    }
}
