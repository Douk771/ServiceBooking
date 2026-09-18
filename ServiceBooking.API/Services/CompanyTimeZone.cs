namespace ServiceBooking.API.Services;

/// <summary>
/// Pure rules for what <c>Company.TimeZoneId</c>/<c>TimeZoneIsManual</c> become after a create or update
/// (ARCHITECTURE_CYCLE4.md §34, US-30 p.3, API_CONTRACT_CYCLE4.md §31.3). No DB, no
/// <see cref="TimeZoneInfo"/> lookups here — callers resolve the city's own zone and pass it in, so this
/// stays testable without a real time zone database.
/// </summary>
public static class CompanyTimeZoneResolver
{
    public readonly record struct Result(string TimeZoneId, bool IsManual);

    /// <summary>POST /api/companies: no previous state to preserve. A requested zone equal to the
    /// city's own zone is not "manual" — only an actual override is.</summary>
    public static Result ForNewCompany(string cityTimeZoneId, string? requestedTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(requestedTimeZoneId) ||
            string.Equals(requestedTimeZoneId, cityTimeZoneId, StringComparison.Ordinal))
            return new Result(cityTimeZoneId, false);

        return new Result(requestedTimeZoneId, true);
    }

    /// <summary>
    /// PUT /api/companies/{id}. Implements US-30 p.3's three rules:
    /// <list type="bullet">
    /// <item>only <c>cityId</c> changed, zone untouched by this request → zone follows the new city,
    /// UNLESS the current zone is already a manual override (a manual override must survive a later
    /// city save that doesn't touch the zone field).</item>
    /// <item><c>timeZoneId</c> given and different from the (possibly just-changed) city's zone →
    /// becomes a manual override.</item>
    /// <item><c>timeZoneId</c> given as an explicit JSON <c>null</c> → reverts to the city's zone and
    /// clears the manual flag.</item>
    /// </list>
    /// </summary>
    /// <param name="effectiveCityTimeZoneId">The zone of the company's city AFTER this request (the new
    /// city's zone if <paramref name="cityChanged"/>, otherwise the same city's zone as before).</param>
    /// <param name="cityChanged">Whether this request changed <c>CityId</c>.</param>
    /// <param name="timeZoneIdFieldProvided">Whether the request body contained a <c>timeZoneId</c> key
    /// at all — distinguishes "field omitted" from "field explicitly set to null" (US-30 p.3's third
    /// rule), which a plain <c>string? == null</c> check on its own cannot.</param>
    /// <param name="requestedTimeZoneId">The value of that field when present (may itself be null for
    /// an explicit-null request).</param>
    /// <param name="currentTimeZoneId">The company's zone before this request.</param>
    /// <param name="currentIsManual">Whether the company's zone before this request was a manual override.</param>
    public static Result ForUpdate(
        string effectiveCityTimeZoneId,
        bool cityChanged,
        bool timeZoneIdFieldProvided,
        string? requestedTimeZoneId,
        string currentTimeZoneId,
        bool currentIsManual)
    {
        if (timeZoneIdFieldProvided)
        {
            if (requestedTimeZoneId is null)
                return new Result(effectiveCityTimeZoneId, false); // explicit null → revert to city zone

            return string.Equals(requestedTimeZoneId, effectiveCityTimeZoneId, StringComparison.Ordinal)
                ? new Result(effectiveCityTimeZoneId, false)
                : new Result(requestedTimeZoneId, true);
        }

        // timeZoneId not part of this request: only the city may move the zone, and only when the
        // current zone isn't already a deliberate override.
        if (cityChanged && !currentIsManual)
            return new Result(effectiveCityTimeZoneId, false);

        return new Result(currentTimeZoneId, currentIsManual);
    }
}

/// <summary>Resolves a UTC offset for display (API_CONTRACT_CYCLE4.md §31.1/§31.4's <c>utcOffsetMinutes</c>).
/// Not pure (reads the OS time zone database) but deterministic for a given instant, which is what
/// makes it usable in a test without depending on "now".</summary>
public static class TimeZoneOffset
{
    /// <param name="timeZoneId">IANA time zone identifier.</param>
    /// <param name="atUtc">Instant to resolve the offset at (time zones' offsets can change over the
    /// year in zones that observe DST, though none of Russia's current zones do).</param>
    /// <param name="minutes">The resolved offset, or 0 when resolution fails.</param>
    public static bool TryGetUtcOffsetMinutes(string timeZoneId, DateTime atUtc, out int minutes)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            minutes = (int)tz.GetUtcOffset(atUtc).TotalMinutes;
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            minutes = 0;
            return false;
        }
    }
}
