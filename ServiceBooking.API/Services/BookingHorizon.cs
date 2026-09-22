namespace ServiceBooking.API.Services;

/// <summary>
/// The single rule for "how far ahead a client may book" (ARCHITECTURE_CYCLE6.md §45.7, Q5). Pure,
/// no EF types — same convention as <see cref="PhoneNormalizer"/> and <see cref="SlotCalculator"/> —
/// used at exactly three points: <c>CompaniesController.Update</c> (saving the setting),
/// <c>GET /api/bookings/availability</c>, and <c>BookingsController.Create</c>'s client-only path.
/// Staff (manual bookings, reschedule) are never subject to this rule (§45.7 p.2).
/// </summary>
public static class BookingHorizon
{
    public const int Default = 90;
    public const int Min = 1;
    public const int Max = 365;

    /// <summary>0, null or negative → Default; otherwise the value as-is (already validated by the caller).</summary>
    public static int Normalize(int? raw) => raw is null or <= 0 ? Default : raw.Value;

    /// <summary>
    /// False only when a value was actually supplied and falls outside [Min, Max] — the one source of
    /// the 400 in <c>CompaniesController.Update</c>. Null and 0 both normalize to Default and always
    /// succeed (0 is an explicit "reset to default", not an out-of-range value).
    /// </summary>
    public static bool TryNormalize(int? raw, out int days)
    {
        if (raw is null or 0)
        {
            days = Default;
            return true;
        }

        if (raw.Value < Min || raw.Value > Max)
        {
            days = 0;
            return false;
        }

        days = raw.Value;
        return true;
    }

    public static DateOnly LastBookableDate(DateOnly todayUtc, int days) => todayUtc.AddDays(days);

    public static bool IsWithin(DateOnly date, DateOnly todayUtc, int days) =>
        date <= LastBookableDate(todayUtc, days);
}
