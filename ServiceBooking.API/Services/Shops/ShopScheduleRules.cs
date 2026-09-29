using System.Text.Json;

namespace ServiceBooking.API.Services.Shops;

/// <summary>An interval as the API receives it: <c>"HH:mm"</c> strings.</summary>
public sealed record TimeIntervalText(string? Start, string? End);

/// <summary>Outcome of <see cref="ShopScheduleRules"/> validation: the canonical intervals, or the Russian 400 sentence.</summary>
public sealed record IntervalsResult(IReadOnlyList<TimeInterval>? Intervals, string? Error)
{
    public bool Ok => Error is null;
}

public sealed record WeeklyHoursResult(WeeklyHours? Hours, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>
/// ARCHITECTURE_CYCLE24.md §449.1, API_CONTRACT_CYCLE24.md §473.2 — validation, canonical JSON and text of working hours, as pure
/// functions. The order of the checks is the contract's table; "through midnight is only for the last interval" is checked BEFORE
/// the ordering check, otherwise it could never be reached (such an interval always overlaps the next one).
/// </summary>
public static class ShopScheduleRules
{
    public const string TimeFormat = "Укажите время в формате ЧЧ:ММ";
    public const string TimeStep = "Время указывается с шагом 5 минут";
    public const string DayTwice = "День недели указан дважды";
    public const string DayUnknown = "Неизвестный день недели";
    public const string TooManyIntervals = "В дне не больше трёх интервалов";
    public const string ZeroLength = "Интервал не может быть нулевой длины";
    public const string NotInOrder = "Интервалы должны идти по порядку и не пересекаться";
    public const string OnlyLastCrossesMidnight = "Через полночь может переходить только последний интервал дня";
    public const string TailOverlapsNextDay = "Часы после полуночи пересекаются с часами следующего дня";

    public const int MaxIntervalsPerDay = 3;
    public const int StepMinutes = 5;

    /// <summary>Parses <c>"HH:mm"</c> (00:00–23:55, multiple of 5). Returns the sentence of the failure as <paramref name="error"/>.</summary>
    public static bool TryParseTime(string? text, out int minutes, out string? error)
    {
        minutes = 0;
        error = null;
        if (text is null || text.Length != 5 || text[2] != ':' ||
            !char.IsAsciiDigit(text[0]) || !char.IsAsciiDigit(text[1]) || !char.IsAsciiDigit(text[3]) || !char.IsAsciiDigit(text[4]))
        {
            error = TimeFormat;
            return false;
        }
        var h = (text[0] - '0') * 10 + (text[1] - '0');
        var m = (text[3] - '0') * 10 + (text[4] - '0');
        if (h > 23 || m > 59)
        {
            error = TimeFormat;
            return false;
        }
        if (m % StepMinutes != 0)
        {
            error = TimeStep;
            return false;
        }
        minutes = h * 60 + m;
        return true;
    }

    /// <summary>
    /// The intervals of ONE day as the API receives them → canonical minutes. <c>end ≤ start</c> is "through midnight"
    /// (<c>"00:00"</c> as the end means "until midnight"); equal times are a zero-length interval.
    /// </summary>
    public static IntervalsResult ParseDay(IReadOnlyList<TimeIntervalText>? input)
    {
        var list = input ?? [];
        if (list.Count > MaxIntervalsPerDay) return new IntervalsResult(null, TooManyIntervals);

        var result = new List<TimeInterval>(list.Count);
        foreach (var item in list)
        {
            if (!TryParseTime(item?.Start, out var start, out var error)) return new IntervalsResult(null, error);
            if (!TryParseTime(item?.End, out var end, out error)) return new IntervalsResult(null, error);
            if (start == end) return new IntervalsResult(null, ZeroLength);
            if (end < start) end += 1440;
            result.Add(new TimeInterval(start, end));
        }

        for (var i = 0; i < result.Count - 1; i++)
            if (result[i].CrossesMidnight) return new IntervalsResult(null, OnlyLastCrossesMidnight);
        for (var i = 1; i < result.Count; i++)
            if (result[i].StartMinutes <= result[i - 1].EndMinutes) return new IntervalsResult(null, NotInOrder);
        return new IntervalsResult(result, null);
    }

    /// <summary>
    /// The whole week: every day parsed by <see cref="ParseDay"/>, no day twice, no unknown day, and the "tail" after midnight of a
    /// day must not reach the first interval of the next day (the week is cyclic: Sunday's tail is checked against Monday).
    /// </summary>
    public static WeeklyHoursResult ParseWeek(IReadOnlyList<(DayOfWeek Day, IReadOnlyList<TimeIntervalText>? Intervals)>? input)
    {
        var days = new Dictionary<DayOfWeek, IReadOnlyList<TimeInterval>>();
        foreach (var (day, intervals) in input ?? [])
        {
            if (!Enum.IsDefined(day)) return new WeeklyHoursResult(null, DayUnknown);
            if (days.ContainsKey(day)) return new WeeklyHoursResult(null, DayTwice);
            var parsed = ParseDay(intervals);
            if (!parsed.Ok) return new WeeklyHoursResult(null, parsed.Error);
            if (parsed.Intervals!.Count > 0) days[day] = parsed.Intervals;
        }

        var week = new WeeklyHours(days);
        foreach (var (day, intervals) in days)
        {
            var next = week.For(NextDay(day));
            if (TailReaches(intervals, next)) return new WeeklyHoursResult(null, TailOverlapsNextDay);
        }
        return new WeeklyHoursResult(week, null);
    }

    /// <summary>Does the after-midnight tail of <paramref name="day"/> reach the first interval of <paramref name="nextDay"/>?</summary>
    public static bool TailReaches(IReadOnlyList<TimeInterval> day, IReadOnlyList<TimeInterval> nextDay)
    {
        if (day.Count == 0 || nextDay.Count == 0) return false;
        var last = day[^1];
        return last.CrossesMidnight && last.EndMinutes - 1440 >= nextDay[0].StartMinutes;
    }

    public static DayOfWeek NextDay(DayOfWeek day) => (DayOfWeek)(((int)day + 1) % 7);

    // ── canonical JSON (§449.1): { "days": { "Monday": [ { "start": 540, "end": 780 } ] } } ─────────────────────

    public static string Serialize(WeeklyHours hours) => JsonSerializer.Serialize(
        new JsonHours(hours.Days.ToDictionary(d => d.Key.ToString(), d => d.Value.Select(i => new JsonInterval(i.StartMinutes, i.EndMinutes)).ToList())));

    public static string SerializeIntervals(IReadOnlyList<TimeInterval> intervals) =>
        JsonSerializer.Serialize(intervals.Select(i => new JsonInterval(i.StartMinutes, i.EndMinutes)).ToList());

    /// <summary>Null and unreadable JSON are both "hours not set": a corrupted row must not make a shop take orders round the clock.</summary>
    public static WeeklyHours? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<JsonHours>(json);
            if (parsed?.Days is null) return null;
            var days = new Dictionary<DayOfWeek, IReadOnlyList<TimeInterval>>();
            foreach (var (name, intervals) in parsed.Days)
            {
                if (!Enum.TryParse<DayOfWeek>(name, out var day) || !Enum.IsDefined(day) || intervals.Count == 0) continue;
                days[day] = intervals.Select(i => new TimeInterval(i.Start, i.End)).ToList();
            }
            return new WeeklyHours(days);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<TimeInterval> ParseIntervals(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<JsonInterval>>(json)?.Select(i => new TimeInterval(i.Start, i.End)).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ── texts ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>"09:00–14:00, 15:00–21:00", "18:00–03:00 (до утра)", "выходной".</summary>
    public static string DayText(IReadOnlyList<TimeInterval> intervals)
    {
        if (intervals.Count == 0) return "выходной";
        return string.Join(", ", intervals.Select(i =>
            $"{ShopTimeTexts.Hhmm(i.StartMinutes)}–{ShopTimeTexts.Hhmm(i.EndMinutes)}{(i.CrossesMidnight ? " (до утра)" : string.Empty)}"));
    }

    private sealed record JsonInterval(
        [property: System.Text.Json.Serialization.JsonPropertyName("start")] int Start,
        [property: System.Text.Json.Serialization.JsonPropertyName("end")] int End);

    private sealed record JsonHours(
        [property: System.Text.Json.Serialization.JsonPropertyName("days")] Dictionary<string, List<JsonInterval>> Days);
}
