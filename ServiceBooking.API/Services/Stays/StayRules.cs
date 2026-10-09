using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>The codes of a refusal to book / quote (API_CONTRACT_CYCLE37.md §37.25.2). Declaration order = order of checking.</summary>
public enum StayRefusalCode
{
    InvalidDates,
    CheckInInPast,
    SameDayNotAllowed,
    BeyondHorizon,
    MaxNightsExceeded,
    DatesUnavailable,
    MinNightsNotMet,
    TooManyGuests,
    DogsNotAllowed,
    CotNotAvailable,
    NoPriceForNights,
    NotAcceptingBookings,
    PriceChanged,
    ServiceSlotUnavailable,
    ServiceSelectionInvalid
}

public readonly record struct StayRulesSettings(int MinNights, int MaxNights, int HorizonDays, bool AllowGapFill, bool AllowSameDayCheckIn);

/// <summary>An occupied period [Start, End): End is the check-out date. <see cref="HoldExpiresAtUtc"/> is set for a held booking.</summary>
public readonly record struct OccupiedPeriod(DateOnly Start, DateOnly End, DateTime? HoldExpiresAtUtc = null);

/// <summary>ARCHITECTURE_CYCLE37.md §37.6.2 — availability and stay-length rules. Pure.</summary>
public static class StayRules
{
    /// <summary>Is the period still occupying nights at <paramref name="nowUtc"/>? An expired, not-yet-processed hold does not (§37.5.2).</summary>
    public static bool IsActive(OccupiedPeriod p, DateTime nowUtc) => p.HoldExpiresAtUtc is null || p.HoldExpiresAtUtc.Value > nowUtc;

    public static bool Overlaps(OccupiedPeriod p, DateOnly checkIn, DateOnly checkOut) => checkIn < p.End && p.Start < checkOut;

    /// <summary>The first failed rule, or null when the stay is allowed. A manual (staff) booking skips the horizon, maximum and minimum.</summary>
    public static StayRefusalCode? CheckStay(
        DateOnly checkIn, DateOnly checkOut, DateOnly today, DateTime nowUtc,
        StayRulesSettings settings, IReadOnlyList<OccupiedPeriod> occupancies, bool manual = false)
    {
        if (checkOut <= checkIn) return StayRefusalCode.InvalidDates;
        if (checkIn < today) return StayRefusalCode.CheckInInPast;
        if (!manual && checkIn == today && !settings.AllowSameDayCheckIn) return StayRefusalCode.SameDayNotAllowed;

        var nights = checkOut.DayNumber - checkIn.DayNumber;
        if (!manual)
        {
            var lastNight = checkOut.AddDays(-1);
            if (lastNight > LastBookableNight(today, settings.HorizonDays)) return StayRefusalCode.BeyondHorizon;
            if (nights > settings.MaxNights) return StayRefusalCode.MaxNightsExceeded;
        }

        var active = occupancies.Where(p => IsActive(p, nowUtc)).ToList();
        if (active.Any(p => Overlaps(p, checkIn, checkOut))) return StayRefusalCode.DatesUnavailable;

        if (!manual && nights < settings.MinNights)
        {
            // §4.3: a short stay is allowed only when it exactly fills a gap between two bookings/blocks.
            var fillsGap = settings.AllowGapFill && active.Any(p => p.End == checkIn) && active.Any(p => p.Start == checkOut);
            if (!fillsGap) return StayRefusalCode.MinNightsNotMet;
        }
        return null;
    }

    /// <summary>The last night a guest can book: today + horizon − 1.</summary>
    public static DateOnly LastBookableNight(DateOnly today, int horizonDays) => today.AddDays(horizonDays - 1);
}

public readonly record struct GuestCheck(StayRefusalCode? Refusal, int ExtraBeds);

/// <summary>ARCHITECTURE_CYCLE37.md §37.6.2 — guest-count rules: capacity, extra beds, dogs, cot.</summary>
public static class GuestRules
{
    public static GuestCheck Check(
        int adults, int children, int dogs, bool needCot,
        int capacity, bool extraBedsEnabled, int extraBedsMax, bool dogsForbidden, bool hasCot)
    {
        var guests = adults + children;
        var extra = Math.Max(0, guests - capacity);
        var allowedExtra = extraBedsEnabled ? extraBedsMax : 0;
        if (extra > allowedExtra) return new GuestCheck(StayRefusalCode.TooManyGuests, extra);
        if (dogs > 0 && dogsForbidden) return new GuestCheck(StayRefusalCode.DogsNotAllowed, extra);
        if (needCot && !hasCot) return new GuestCheck(StayRefusalCode.CotNotAvailable, extra);
        return new GuestCheck(null, extra);
    }
}
