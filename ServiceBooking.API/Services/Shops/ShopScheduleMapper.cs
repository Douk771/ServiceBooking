using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Shops;

/// <summary>ARCHITECTURE_CYCLE24.md §473 — the pure mapping of the schedule model to its DTOs. Every text is composed here or in the rules classes.</summary>
public static class ShopScheduleMapper
{
    public static TimeIntervalDto ToDto(TimeInterval i) =>
        new(ShopTimeTexts.Hhmm(i.StartMinutes), ShopTimeTexts.Hhmm(i.EndMinutes), i.CrossesMidnight);

    public static WorkingHoursDto ToDto(WeeklyHours? hours)
    {
        var days = WeekdayMask.IsoDays.Select(day =>
        {
            var intervals = hours?.For(day) ?? [];
            return new WorkingDayDto(day, ShopTimeTexts.DayName(day), intervals.Select(ToDto).ToList(),
                hours is null ? "не заданы" : ShopScheduleRules.DayText(intervals));
        }).ToList();
        return new WorkingHoursDto(hours is not null, days);
    }

    public static SpecialDayDto ToDto(ShopSpecialDay day)
    {
        var intervals = day.IsClosed ? [] : ShopScheduleRules.ParseIntervals(day.IntervalsJson);
        return new SpecialDayDto(day.Date, ShopTimeTexts.DateShort(day.Date), day.IsClosed, intervals.Select(ToDto).ToList(),
            day.IsClosed ? "выходной" : ShopScheduleRules.DayText(intervals));
    }

    public static PickupSettingsDto ToDto(PickupSettings s) =>
        new(s.AsapEnabled, s.ScheduledEnabled, s.SlotStepMinutes, s.PreorderDays, s.MinPrepMinutes);

    public static ShopOpenStateDto ToDto(ShopOpenState s) => new(s.IsOpen, s.Text, s.OpensAtUtc, s.ClosesAtUtc);

    public static ShopAcceptanceDto ToDto(ShopAcceptanceState s) =>
        new(s.Mode, s.PausedUntilUtc, s.StatusText, s.ChangedAtUtc, s.ChangedByName, s.ChangedText);

    /// <summary>null when the option is switched off by the shop.</summary>
    public static AsapOptionDto? ToDto(AsapOption a, bool enabled) => enabled ? new AsapOptionDto(a.Available, a.ReadyAtUtc, a.Text) : null;

    public static OrderLimitDto ToDto(OrderLimitInfo l) => new(l.Used, l.Limit, l.MonthLabel, l.WarningLevel, l.Text);

    public static PickupOptionsDto ToPickupOptions(ShopGateResult gate, PickupSettings settings) => new(
        settings.AsapEnabled, settings.ScheduledEnabled, ToDto(gate.Asap, settings.AsapEnabled),
        gate.Dates.Select(d => new PickupDateDto(d.Date, d.Label, d.HasSlots, d.ReasonText)).ToList());

    /// <summary>The setup checklist of the cabinet (§472): in cycle 24 one item — the working hours.</summary>
    public static List<SetupChecklistItemDto> SetupChecklist(bool workingHoursSet) =>
        [new SetupChecklistItemDto("WorkingHours", "Задайте часы работы", workingHoursSet)];
}
