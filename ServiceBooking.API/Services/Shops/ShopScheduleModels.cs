namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// One working interval in minutes from the start of the day it BEGINS in. <c>EndMinutes</c> may exceed 1440: an interval through
/// midnight (Fri 18:00–03:00 = 1080–1620) belongs to the day it starts (ARCHITECTURE_CYCLE24.md §449.1).
/// </summary>
public readonly record struct TimeInterval(int StartMinutes, int EndMinutes)
{
    public bool CrossesMidnight => EndMinutes > 1440;
    public int Length => EndMinutes - StartMinutes;
}

/// <summary>The weekly hours: an absent day is a day off. Immutable in practice; built by <see cref="ShopScheduleRules"/>.</summary>
public sealed class WeeklyHours
{
    public IReadOnlyDictionary<DayOfWeek, IReadOnlyList<TimeInterval>> Days { get; }

    public WeeklyHours(IReadOnlyDictionary<DayOfWeek, IReadOnlyList<TimeInterval>> days) => Days = days;

    public static WeeklyHours Empty { get; } = new(new Dictionary<DayOfWeek, IReadOnlyList<TimeInterval>>());

    public IReadOnlyList<TimeInterval> For(DayOfWeek day) =>
        Days.TryGetValue(day, out var intervals) ? intervals : [];
}

/// <summary>An override of one date: closed, or its own intervals.</summary>
public sealed record SpecialDayHours(bool IsClosed, IReadOnlyList<TimeInterval> Intervals)
{
    public static SpecialDayHours Closed { get; } = new(true, []);
}

/// <summary>Everything <see cref="PickupSchedule"/> needs to know about the shop's hours. <c>Weekly == null</c> = "hours not set".</summary>
public sealed record ShopScheduleSnapshot(
    TimeZoneInfo Zone, WeeklyHours? Weekly, IReadOnlyDictionary<DateOnly, SpecialDayHours> SpecialDays)
{
    public bool HoursSet => Weekly is not null;
}

/// <summary>The shop's pickup rules (ARCHITECTURE_CYCLE24.md §448.1, US-24-05).</summary>
public sealed record PickupSettings(
    bool AsapEnabled, bool ScheduledEnabled, int SlotStepMinutes, int PreorderDays, int MinPrepMinutes)
{
    public const int DefaultSlotStep = 15;
    public const int DefaultMinPrep = 15;
}

/// <summary>A working interval of one working day, in UTC.</summary>
public sealed record WorkInterval(DateOnly Day, DateTime StartUtc, DateTime EndUtc);

public sealed record ShopOpenState(bool IsOpen, string Text, DateTime? OpensAtUtc = null, DateTime? ClosesAtUtc = null);

/// <summary>"As soon as possible" for the current moment. <c>Text</c> is null when the option is switched off.</summary>
public sealed record AsapOption(bool Available, DateTime? ReadyAtUtc, DateOnly? PickupDate, string? Text);

public sealed record PickupSlot(DateTime StartUtc, DateTime EndUtc, string Label);

public sealed record PickupDate(DateOnly Date, string Label, bool HasSlots, string? ReasonText);

/// <summary>Kind of a pickup choice made by a customer or by staff (the API's <c>PickupSelectionInput</c>).</summary>
public sealed record PickupSelection(ServiceBooking.Core.Enums.PickupKind Kind, DateOnly? Date, DateTime? SlotStartUtc);

public sealed record PickupValidation(
    bool Ok, DateOnly PickupDate, DateTime StartUtc, DateTime? EndUtc, string? ProblemText)
{
    public static PickupValidation Success(DateOnly date, DateTime start, DateTime? end) => new(true, date, start, end, null);
    public static PickupValidation Problem(string text) => new(false, default, default, null, text);
}
