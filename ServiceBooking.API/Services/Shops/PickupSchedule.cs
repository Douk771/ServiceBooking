using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §449.2 — everything about a shop's time as ONE pure class: working intervals in UTC (special days over the
/// week), the current working day (an overnight interval belongs to the day it STARTS), "open / break / closed", "as soon as
/// possible", the slot grid, the dates offered, the end-of-day pause and the server-side re-check of a chosen pickup time. Nothing
/// here reads the clock or the database: <c>nowUtc</c> is a parameter, which is what makes the reference vectors
/// (<c>contracts/cycle24/pickup-schedule-vectors.json</c>) testable. The frontend never repeats any of it.
/// </summary>
public sealed class PickupSchedule(ShopScheduleSnapshot schedule, PickupSettings settings)
{
    public const string AsapDisabledText = "„Как можно скорее“ в этом магазине недоступно — выберите время";
    public const string AsapNoTimeText = "Сейчас не успеем приготовить заказ — выберите время";
    public const string ScheduledDisabledText = "Заказ ко времени в этом магазине недоступен";
    public const string SlotGoneText = "Это время уже недоступно — выберите другое";
    public const string StaffTimeUnavailableText = "Это время недоступно — выберите другое";
    public const string DateNotAvailableText = "На эту дату заказать нельзя — выберите другую";
    public const string NotEnoughTimeAsapText = "Сегодня уже не успеем приготовить заказ";
    public const string NotEnoughTimeDateText = "Сегодня уже не успеем приготовить — выберите другой день";
    public const string NoFreeTimeOnDateText = "На эту дату нет свободного времени";
    public const string DayOffText = "В этот день магазин не работает";

    /// <summary>How far ahead "closed, we open …" looks for the next opening.</summary>
    public const int OpeningLookAheadDays = 14;

    public ShopScheduleSnapshot Schedule => schedule;
    public PickupSettings Settings => settings;

    // ── time plumbing ───────────────────────────────────────────────────────────────────────────────────────

    public DateOnly LocalDate(DateTime nowUtc) => DateOnly.FromDateTime(ToLocal(nowUtc));

    private DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), schedule.Zone);

    private int LocalMinutes(DateTime utc)
    {
        var local = ToLocal(utc);
        return local.Hour * 60 + local.Minute;
    }

    /// <summary>The UTC moment of "date, minutes after its local midnight" (minutes may exceed 1440). A local time that does not exist (DST gap) moves to the first valid instant.</summary>
    public static DateTime ToUtc(TimeZoneInfo zone, DateOnly date, int minutes)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private DateTime ToUtc(DateOnly date, int minutes) => ToUtc(schedule.Zone, date, minutes);

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    // ── working intervals ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The intervals of a working day in UTC (a special day replaces the weekly ones; closed / not set → none).</summary>
    public IReadOnlyList<WorkInterval> IntervalsFor(DateOnly day)
    {
        if (!schedule.HoursSet) return [];
        IReadOnlyList<TimeInterval> intervals = schedule.SpecialDays.TryGetValue(day, out var special)
            ? (special.IsClosed ? [] : special.Intervals)
            : schedule.Weekly!.For(day.DayOfWeek);
        return intervals.Select(i => new WorkInterval(day, ToUtc(day, i.StartMinutes), ToUtc(day, i.EndMinutes))).ToList();
    }

    private WorkInterval? CurrentInterval(DateTime nowUtc)
    {
        var now = AsUtc(nowUtc);
        var today = LocalDate(now);
        return IntervalsFor(today.AddDays(-1)).Concat(IntervalsFor(today)).FirstOrDefault(i => i.StartUtc <= now && now < i.EndUtc);
    }

    /// <summary>
    /// The working day the current moment belongs to: yesterday while yesterday's overnight interval is still running (Sat 01:00 with
    /// "Fri 18:00–03:00" → Friday), otherwise the shop's calendar date.
    /// </summary>
    public DateOnly CurrentWorkingDay(DateTime nowUtc)
    {
        var now = AsUtc(nowUtc);
        var yesterday = LocalDate(now).AddDays(-1);
        return IntervalsFor(yesterday).Any(i => i.StartUtc <= now && now < i.EndUtc) ? yesterday : LocalDate(now);
    }

    // ── open / closed ───────────────────────────────────────────────────────────────────────────────────────

    public ShopOpenState OpenState(DateTime nowUtc)
    {
        var now = AsUtc(nowUtc);
        if (!schedule.HoursSet) return new ShopOpenState(false, "Часы работы не заданы");

        var current = CurrentInterval(now);
        if (current is not null)
            return new ShopOpenState(true, $"Открыто до {ShopTimeTexts.Clock(LocalMinutes(current.EndUtc))}", ClosesAtUtc: current.EndUtc);

        var today = LocalDate(now);
        var todays = IntervalsFor(today);
        var nextToday = todays.FirstOrDefault(i => i.StartUtc > now);
        if (nextToday is not null && todays.Any(i => i.EndUtc <= now))
            return new ShopOpenState(false, $"Перерыв до {ShopTimeTexts.Clock(LocalMinutes(nextToday.StartUtc))}", OpensAtUtc: nextToday.StartUtc);

        for (var ahead = 0; ahead <= OpeningLookAheadDays; ahead++)
        {
            var day = today.AddDays(ahead);
            var next = IntervalsFor(day).FirstOrDefault(i => i.StartUtc > now);
            if (next is null) continue;
            var clock = ShopTimeTexts.Clock(LocalMinutes(next.StartUtc));
            var text = ahead switch
            {
                0 => $"Закрыто, откроемся в {clock}",
                1 => $"Закрыто, откроемся завтра в {clock}",
                <= 6 => $"Закрыто, откроемся в {ShopTimeTexts.DayName(day.DayOfWeek)} в {clock}",
                _ => $"Закрыто, откроемся {ShopTimeTexts.DayMonth(day)} в {clock}"
            };
            return new ShopOpenState(false, text, OpensAtUtc: next.StartUtc);
        }
        return new ShopOpenState(false, "Закрыто");
    }

    // ── as soon as possible ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Available ⇔ switched on ∧ open now ∧ now + preparation ≤ the end of the running interval.</summary>
    public AsapOption Asap(DateTime nowUtc)
    {
        var now = AsUtc(nowUtc);
        if (!settings.AsapEnabled) return new AsapOption(false, null, null, null);

        var current = CurrentInterval(now);
        if (current is null) return new AsapOption(false, null, null, OpenState(now).Text);

        var ready = now.AddMinutes(settings.MinPrepMinutes);
        if (ready > current.EndUtc) return new AsapOption(false, null, null, NotEnoughTimeAsapText);
        return new AsapOption(true, ready, current.Day, $"≈ к {ShopTimeTexts.Clock(LocalMinutes(ready))}");
    }

    // ── slots and dates ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The slot grid of a working day: from the start of every interval, in steps of <c>SlotStepMinutes</c>, a slot must lie entirely
    /// inside its interval. A customer gets slots that start at least "now + preparation" ahead and only when ordering to a time is
    /// on; staff get every slot that has not ended yet, always.
    /// </summary>
    public IReadOnlyList<PickupSlot> Slots(DateOnly day, DateTime nowUtc, bool forStaff)
    {
        var now = AsUtc(nowUtc);
        if (!forStaff && !settings.ScheduledEnabled) return [];

        var earliestStart = now.AddMinutes(settings.MinPrepMinutes);
        var step = TimeSpan.FromMinutes(settings.SlotStepMinutes);
        var slots = new List<PickupSlot>();
        foreach (var interval in IntervalsFor(day))
            for (var start = interval.StartUtc; start + step <= interval.EndUtc; start += step)
            {
                var end = start + step;
                if (forStaff ? end <= now : start < earliestStart) continue;
                slots.Add(new PickupSlot(start, end, $"{ShopTimeTexts.Hhmm(LocalMinutes(start))}–{ShopTimeTexts.Hhmm(LocalMinutes(end))}"));
            }
        return slots;
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE25.md §503 — the whole slot grid of a working day (step <c>SlotStepMinutes</c>, every slot entirely inside an interval),
    /// with NO cut-off for "already gone", no preparation time and no dependence on <c>ScheduledEnabled</c>: the pick list needs the grid of past
    /// and future dates too.
    /// </summary>
    public IReadOnlyList<PickupSlot> DaySlots(DateOnly day)
    {
        var step = TimeSpan.FromMinutes(settings.SlotStepMinutes);
        var slots = new List<PickupSlot>();
        foreach (var interval in IntervalsFor(day))
            for (var start = interval.StartUtc; start + step <= interval.EndUtc; start += step)
            {
                var end = start + step;
                slots.Add(new PickupSlot(start, end, $"{ShopTimeTexts.Hhmm(LocalMinutes(start))}–{ShopTimeTexts.Hhmm(LocalMinutes(end))}"));
            }
        return slots;
    }

    /// <summary>The slots a date really offers: none outside the horizon or on a day off (the raw <see cref="Slots"/> grid knows nothing of the horizon).</summary>
    public IReadOnlyList<PickupSlot> SlotsForDate(DateOnly day, DateTime nowUtc, bool forStaff) =>
        SelectableDays(nowUtc).Contains(day) ? Slots(day, nowUtc, forStaff) : [];

    /// <summary>The working days a pickup can be chosen for: from the current working day to today + <c>PreorderDays</c>, days off and closed special days left out.</summary>
    public IReadOnlyList<DateOnly> SelectableDays(DateTime nowUtc)
    {
        var first = CurrentWorkingDay(nowUtc);
        var last = LocalDate(nowUtc).AddDays(Math.Max(0, settings.PreorderDays));
        var days = new List<DateOnly>();
        for (var day = first; day <= last; day = day.AddDays(1))
            if (IntervalsFor(day).Count > 0) days.Add(day);
        return days;
    }

    /// <summary>The dates offered to a customer. Without "order to a time" the list is empty: choosing a slot is not offered at all.</summary>
    public IReadOnlyList<PickupDate> Dates(DateTime nowUtc)
    {
        if (!settings.ScheduledEnabled) return [];
        var today = CurrentWorkingDay(nowUtc);
        return SelectableDays(nowUtc).Select(day =>
        {
            var hasSlots = Slots(day, nowUtc, forStaff: false).Count > 0;
            var reason = hasSlots ? null : day == today ? NotEnoughTimeDateText : NoFreeTimeOnDateText;
            return new PickupDate(day, ShopTimeTexts.DateLabel(day, today), hasSlots, reason);
        }).ToList();
    }

    /// <summary>True when the shop can be ordered from through a slot at least once within the horizon.</summary>
    public bool ScheduledAvailable(DateTime nowUtc) => Dates(nowUtc).Any(d => d.HasSlots);

    /// <summary>Why a date has no slots (text for <c>PickupSlotsDto.reasonText</c>), or null when it has.</summary>
    public string? SlotsReason(DateOnly day, DateTime nowUtc, bool forStaff)
    {
        if (!forStaff && !settings.ScheduledEnabled) return ScheduledDisabledText;
        if (IntervalsFor(day).Count == 0) return DayOffText;
        if (!SelectableDays(nowUtc).Contains(day)) return DateNotAvailableText;
        return Slots(day, nowUtc, forStaff).Count > 0
            ? null
            : day == CurrentWorkingDay(nowUtc) ? NotEnoughTimeDateText : NoFreeTimeOnDateText;
    }

    // ── pause until the end of the day ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The end of the last interval of the current working day, if it is still ahead (also inside a break and inside an overnight
    /// tail); otherwise the nearest local midnight.
    /// </summary>
    public DateTime PauseEndOfDay(DateTime nowUtc)
    {
        var now = AsUtc(nowUtc);
        var intervals = IntervalsFor(CurrentWorkingDay(now));
        if (intervals.Count > 0)
        {
            var lastEnd = intervals.Max(i => i.EndUtc);
            if (lastEnd > now) return lastEnd;
        }
        return ToUtc(LocalDate(now).AddDays(1), 0);
    }

    // ── re-check of a chosen pickup ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-checks a chosen pickup against the schedule AT THE MOMENT of the request (§451.2). A customer's choice is bound by the shop's
    /// switches, the preparation time and the horizon; staff's (US-24-09) only by "the shop is open now" / "the slot has not ended yet".
    /// The slot start must match a slot of the grid EXACTLY.
    /// </summary>
    public PickupValidation Validate(PickupSelection selection, DateTime nowUtc, bool forStaff)
    {
        var now = AsUtc(nowUtc);
        if (selection.Kind == PickupKind.Asap)
        {
            if (forStaff)
            {
                var current = CurrentInterval(now);
                return current is null
                    ? PickupValidation.Problem(StaffTimeUnavailableText)
                    : PickupValidation.Success(current.Day, now.AddMinutes(settings.MinPrepMinutes), null);
            }
            if (!settings.AsapEnabled) return PickupValidation.Problem(AsapDisabledText);
            var asap = Asap(now);
            return asap.Available
                ? PickupValidation.Success(asap.PickupDate!.Value, asap.ReadyAtUtc!.Value, null)
                : PickupValidation.Problem(AsapNoTimeText);
        }

        if (!forStaff && !settings.ScheduledEnabled) return PickupValidation.Problem(ScheduledDisabledText);
        if (selection.Date is not { } date || selection.SlotStartUtc is not { } start)
            return PickupValidation.Problem(forStaff ? StaffTimeUnavailableText : SlotGoneText);
        if (!SelectableDays(now).Contains(date)) return PickupValidation.Problem(forStaff ? StaffTimeUnavailableText : DateNotAvailableText);

        var wanted = AsUtc(start);
        var slot = Slots(date, now, forStaff).FirstOrDefault(s => s.StartUtc == wanted);
        return slot is null
            ? PickupValidation.Problem(forStaff ? StaffTimeUnavailableText : SlotGoneText)
            : PickupValidation.Success(date, slot.StartUtc, slot.EndUtc);
    }

    // ── presentation ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The hours as summary lines with equal consecutive days glued: "пн–пт: 09:00–21:00", "сб–вс: выходной".</summary>
    public static List<(string DayLabel, string Text)> SummaryLines(WeeklyHours? weekly)
    {
        var lines = new List<(string, string)>();
        if (weekly is null) return lines;
        var days = WeekdayMask.IsoDays;
        var i = 0;
        while (i < days.Count)
        {
            var text = ShopScheduleRules.DayText(weekly.For(days[i]));
            var j = i;
            while (j + 1 < days.Count && ShopScheduleRules.DayText(weekly.For(days[j + 1])) == text) j++;
            var label = j == i ? ShopTimeTexts.DayName(days[i])
                : j == i + 1 ? $"{ShopTimeTexts.DayName(days[i])}, {ShopTimeTexts.DayName(days[j])}"
                : $"{ShopTimeTexts.DayName(days[i])}–{ShopTimeTexts.DayName(days[j])}";
            lines.Add((label, text));
            i = j + 1;
        }
        return lines;
    }

    /// <summary>
    /// The text of a pickup on an order or in the journal: "Как можно скорее (≈ 13:20)", "К 12:30", "Завтра, к 12:30", "пт 2 окт, к 12:30".
    /// <paramref name="today"/> is the current WORKING day (CY24-35: in the after-midnight tail of an overnight interval it is yesterday's
    /// date, as on the storefront); required so that no caller silently falls back to the calendar date.
    /// </summary>
    public static string PickupText(
        PickupKind kind, DateOnly pickupDate, DateTime startUtc, TimeZoneInfo zone, DateTime nowUtc, DateOnly today)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(startUtc), zone);
        var clock = ShopTimeTexts.Clock(local.Hour * 60 + local.Minute);
        if (kind == PickupKind.Asap) return $"Как можно скорее (≈ {clock})";
        var day = today;
        if (pickupDate == day) return $"К {clock}";
        if (pickupDate == day.AddDays(1)) return $"Завтра, к {clock}";
        return $"{ShopTimeTexts.DateShort(pickupDate)}, к {clock}";
    }
}
