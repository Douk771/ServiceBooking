namespace ServiceBooking.API.Services.Stays;

public sealed record ServiceSlotRules(int MinHours, int MaxHours, int StepMinutes, int BufferMinutes, int MinLeadMinutes);

/// <summary>An active session of the service (or its preparation): the half-open interval [StartUtc, OccupiedUntilUtc) — REAL moments, midnight and 06:00 do not exist for it.</summary>
public sealed record OccupiedSpec(DateTime StartUtc, DateTime OccupiedUntilUtc);

/// <summary>A session inside a stay: from the check-in moment to the check-out moment (snapshot times of the booking).</summary>
public sealed record StayRangeSpec(DateTime FromUtc, DateTime ToUtc);

public sealed record ServiceSlotInput(
    DateOnly BusinessDate, string TimeZoneId, DateTime NowUtc, int HorizonDays, ServiceSlotRules Service,
    IReadOnlyList<WindowSpec> Windows, IReadOnlyList<PriceRuleSpec> PriceRules, IReadOnlyList<OccupiedSpec> Occupied,
    StayRangeSpec? StayRange = null, int BusinessDayStartMinute = BusinessClock.DefaultBusinessDayStartMinute);

public sealed record ServiceStart(int StartMinute, DateTime StartUtc, int MaxHours);

public sealed record ServiceStarts(StartsReason? Reason, IReadOnlyList<ServiceStart> Starts);

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.4 — the available starts of a business date. ONE function both for what the guest is shown (<see cref="Calculate"/>) and for what the
/// server accepts (<see cref="IsStartAllowed"/> is built ON TOP of it): «shown ⇒ accepted». Pure, no EF. The salon <c>SlotCalculator</c> is deliberately not reused
/// (§39.4.1): it lives in <c>TimeOnly</c> of one day, without price, buffer or a duration to choose. Vectors: service-vectors.json → starts.
/// </summary>
public static class ServiceSlotCalculator
{
    public static ServiceStarts Calculate(ServiceSlotInput input)
    {
        var today = BusinessClock.TodayBusinessDate(input.TimeZoneId, input.NowUtc, input.BusinessDayStartMinute);
        if (input.BusinessDate < today) return new ServiceStarts(StartsReason.DateInPast, []);
        if (input.BusinessDate > today.AddDays(input.HorizonDays - 1)) return new ServiceStarts(StartsReason.BeyondHorizon, []);
        if (input.Windows.Count == 0) return new ServiceStarts(StartsReason.Closed, []);

        var starts = new List<ServiceStart>();
        foreach (var window in input.Windows.OrderBy(w => w.StartMinute))
            for (var s = window.StartMinute; s < window.EndMinute; s += input.Service.StepMinutes)
            {
                var max = MaxHoursOf(input, window, s);
                if (max >= input.Service.MinHours) starts.Add(new ServiceStart(s, BusinessClock.ToUtc(input.TimeZoneId, input.BusinessDate, s), max));
            }
        return starts.Count == 0 ? new ServiceStarts(StartsReason.NoStarts, []) : new ServiceStarts(null, starts);
    }

    /// <summary>The same list, but one question: is this start with this duration acceptable? Defined through <see cref="Calculate"/> so that the two can never disagree.</summary>
    public static bool IsStartAllowed(ServiceSlotInput input, int startMinute, int hours) =>
        hours >= input.Service.MinHours && Calculate(input).Starts.Any(s => s.StartMinute == startMinute && s.MaxHours >= hours);

    /// <summary>
    /// The first reason (the order of the contract) a choice is refused, or null when it is allowed. Only chooses the TEXT of a 409; whether the choice is allowed
    /// is always <see cref="IsStartAllowed"/>.
    /// </summary>
    public static ServiceRefusalCode? Diagnose(ServiceSlotInput input, int startMinute, int hours)
    {
        if (IsStartAllowed(input, startMinute, hours)) return null;
        var list = Calculate(input);
        if (list.Reason == StartsReason.DateInPast) return ServiceRefusalCode.DateInPast;
        if (list.Reason == StartsReason.BeyondHorizon) return ServiceRefusalCode.BeyondHorizon;
        if (hours < input.Service.MinHours || hours > input.Service.MaxHours) return ServiceRefusalCode.HoursOutOfRange;

        var startUtc = BusinessClock.ToUtc(input.TimeZoneId, input.BusinessDate, startMinute);
        var endUtc = startUtc.AddHours(hours);
        if (input.StayRange is { } stay && (startUtc < stay.FromUtc || endUtc > stay.ToUtc)) return ServiceRefusalCode.OutsideStay;
        if (startUtc < input.NowUtc.AddMinutes(input.Service.MinLeadMinutes)) return ServiceRefusalCode.TooEarly;

        var window = input.Windows.FirstOrDefault(w => startMinute >= w.StartMinute && startMinute < w.EndMinute && (startMinute - w.StartMinute) % input.Service.StepMinutes == 0);
        if (window is null || startMinute + 60 * hours > window.EndMinute) return ServiceRefusalCode.StartUnavailable;
        if (!ServicePricing.AllPriced(ServicePricing.HourPrices(input.PriceRules, input.BusinessDate, startMinute, hours))) return ServiceRefusalCode.NoPriceForHours;
        return ServiceRefusalCode.SlotTaken;
    }

    /// <summary>The longest duration (≤ MaxHours) from this start; every condition is monotonic in the hours, so the first failure ends the search. 0 when even one hour fails.</summary>
    private static int MaxHoursOf(ServiceSlotInput input, WindowSpec window, int startMinute)
    {
        var startUtc = BusinessClock.ToUtc(input.TimeZoneId, input.BusinessDate, startMinute);
        if (startUtc < input.NowUtc.AddMinutes(input.Service.MinLeadMinutes)) return 0;
        var prices = ServicePricing.HourPrices(input.PriceRules, input.BusinessDate, startMinute, input.Service.MaxHours);
        var max = 0;
        for (var h = 1; h <= input.Service.MaxHours; h++)
        {
            if (startMinute + 60 * h > window.EndMinute) break;
            if (prices[h - 1] is null) break;
            var endUtc = startUtc.AddHours(h);
            var occupiedUntil = endUtc.AddMinutes(input.Service.BufferMinutes);
            if (input.Occupied.Any(o => startUtc < o.OccupiedUntilUtc && o.StartUtc < occupiedUntil)) break;
            if (input.StayRange is { } stay && (startUtc < stay.FromUtc || endUtc > stay.ToUtc)) break;
            max = h;
        }
        return max;
    }

    /// <summary>The semantics of EX_StayServiceSessions_NoOverlap on two sessions (vectors: overlap): [start, end + buffer) intersect.</summary>
    public static bool Conflicts(DateTime aStartUtc, DateTime aEndUtc, int aBufferMinutes, DateTime bStartUtc, DateTime bEndUtc, int bBufferMinutes) =>
        aStartUtc < bEndUtc.AddMinutes(bBufferMinutes) && bStartUtc < aEndUtc.AddMinutes(aBufferMinutes);
}
