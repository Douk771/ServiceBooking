using FluentAssertions;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §449.2 — the parts of PickupSchedule the reference vectors do not spell out: the server-side re-check of a chosen pickup
/// (customer and staff), DST-free UTC conversion, summary lines, order pickup texts. 2026-09-30 is a Wednesday; Europe/Moscow = UTC+3.
/// </summary>
public class PickupScheduleTests
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
    private static readonly DateTime Noon = new(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc); // 12:00 local
    private static readonly DateOnly Today = new(2026, 9, 30);

    private static PickupSchedule Schedule(
        bool asap = true, bool scheduled = true, int step = 15, int preorder = 1, int prep = 15,
        IReadOnlyDictionary<DayOfWeek, IReadOnlyList<TimeInterval>>? days = null)
    {
        var hours = new WeeklyHours(days ?? new Dictionary<DayOfWeek, IReadOnlyList<TimeInterval>>
        {
            [DayOfWeek.Monday] = [new(540, 1260)], [DayOfWeek.Tuesday] = [new(540, 1260)], [DayOfWeek.Wednesday] = [new(540, 1260)],
            [DayOfWeek.Thursday] = [new(540, 1260)], [DayOfWeek.Friday] = [new(540, 1260)],
        });
        return new PickupSchedule(new ShopScheduleSnapshot(Moscow, hours, new Dictionary<DateOnly, SpecialDayHours>()),
            new PickupSettings(asap, scheduled, step, preorder, prep));
    }

    private static PickupSelection Slot(DateOnly date, DateTime start) => new(PickupKind.Slot, date, start);

    // ── customer validation ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_Asap_Open_IsOk_WithEstimateAndWorkingDay()
    {
        var v = Schedule().Validate(new PickupSelection(PickupKind.Asap, null, null), Noon, forStaff: false);
        v.Ok.Should().BeTrue();
        (v.PickupDate, v.StartUtc, v.EndUtc).Should().Be((Today, Noon.AddMinutes(15), (DateTime?)null));
    }

    [Fact]
    public void Validate_Asap_SwitchedOff_ClosedAndTooLate_HaveTheirOwnSentences()
    {
        Schedule(asap: false).Validate(new PickupSelection(PickupKind.Asap, null, null), Noon, false).ProblemText
            .Should().Be("„Как можно скорее“ в этом магазине недоступно — выберите время");
        Schedule().Validate(new PickupSelection(PickupKind.Asap, null, null), new DateTime(2026, 9, 30, 18, 30, 0, DateTimeKind.Utc), false).ProblemText
            .Should().Be("Сейчас не успеем приготовить заказ — выберите время");
        Schedule().Validate(new PickupSelection(PickupKind.Asap, null, null), new DateTime(2026, 9, 30, 17, 50, 0, DateTimeKind.Utc), false).ProblemText
            .Should().Be("Сейчас не успеем приготовить заказ — выберите время");
    }

    [Fact]
    public void Validate_Slot_ExactStartOfAGridSlot_IsOk_AndCarriesTheEnd()
    {
        var start = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc); // 13:00 local
        var v = Schedule().Validate(Slot(Today, start), Noon, false);
        v.Ok.Should().BeTrue();
        (v.PickupDate, v.StartUtc, v.EndUtc).Should().Be((Today, start, (DateTime?)start.AddMinutes(15)));
    }

    [Theory]
    [InlineData(10, 1)]  // 13:01 — not on the grid
    [InlineData(8, 30)]  // 11:30 — already in the past
    public void Validate_Slot_OffGridOrPast_IsTheSlotGoneSentence(int hourUtc, int minute)
    {
        var start = new DateTime(2026, 9, 30, hourUtc, minute, 0, DateTimeKind.Utc);
        Schedule().Validate(Slot(Today, start), Noon, false).ProblemText.Should().Be("Это время уже недоступно — выберите другое");
    }

    [Fact]
    public void Validate_Slot_InsideThePreparationTime_IsGone()
    {
        // 12:10 is a grid slot but starts before now + 15 minutes (12:15).
        var v = Schedule().Validate(Slot(Today, new DateTime(2026, 9, 30, 9, 15, 0, DateTimeKind.Utc)), Noon, false);
        v.Ok.Should().BeTrue("12:15 is exactly now + preparation");
        Schedule().Validate(Slot(Today, new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc)), Noon, false).Ok.Should().BeFalse();
    }

    [Fact]
    public void Validate_Slot_SwitchedOff_OutsideHorizonOrDayOff_HaveTheirSentences()
    {
        var start = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);
        Schedule(scheduled: false).Validate(Slot(Today, start), Noon, false).ProblemText.Should().Be("Заказ ко времени в этом магазине недоступен");
        // Horizon: today + 1 — the day after tomorrow is out.
        Schedule().Validate(Slot(Today.AddDays(2), start.AddDays(2)), Noon, false).ProblemText.Should().Be("На эту дату заказать нельзя — выберите другую");
        // Saturday is a day off.
        Schedule(preorder: 3).Validate(Slot(new DateOnly(2026, 10, 3), new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc)), Noon, false).ProblemText
            .Should().Be("На эту дату заказать нельзя — выберите другую");
    }

    [Fact]
    public void Validate_SlotWithoutDateOrTime_IsRefusedNotThrown()
    {
        Schedule().Validate(new PickupSelection(PickupKind.Slot, null, null), Noon, false).Ok.Should().BeFalse();
        Schedule().Validate(new PickupSelection(PickupKind.Slot, Today, null), Noon, false).Ok.Should().BeFalse();
    }

    // ── staff validation ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_Staff_IgnoresSwitchesAndPreparation_ButNeedsAnOpenShopForAsap()
    {
        var soon = Noon; // 12:00–12:15 starts inside the preparation time of "now"
        Schedule(scheduled: false).Validate(Slot(Today, soon), Noon, forStaff: true).Ok.Should().BeTrue();
        Schedule(asap: false).Validate(new PickupSelection(PickupKind.Asap, null, null), Noon, forStaff: true).Ok.Should().BeTrue();

        var closedNow = new DateTime(2026, 9, 30, 18, 30, 0, DateTimeKind.Utc);
        Schedule().Validate(new PickupSelection(PickupKind.Asap, null, null), closedNow, forStaff: true).ProblemText
            .Should().Be("Это время недоступно — выберите другое");
    }

    [Fact]
    public void Validate_Staff_SlotThatHasEnded_IsRefused_OneRunningIsFine()
    {
        var ended = new DateTime(2026, 9, 30, 8, 30, 0, DateTimeKind.Utc);   // 11:30–11:45
        var running = new DateTime(2026, 9, 30, 8, 45, 0, DateTimeKind.Utc); // 11:45–12:00 → ends exactly now: refused
        var current = new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc);  // 12:00–12:15
        Schedule().Validate(Slot(Today, ended), Noon, true).Ok.Should().BeFalse();
        Schedule().Validate(Slot(Today, running), Noon, true).Ok.Should().BeFalse();
        Schedule().Validate(Slot(Today, current), Noon, true).Ok.Should().BeTrue();
    }

    // ── grid ────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(15, 48)]
    [InlineData(30, 24)]
    [InlineData(60, 12)]
    public void Slots_StepDecidesTheGrid(int step, int count) =>
        Schedule(step: step).Slots(new DateOnly(2026, 10, 1), Noon, forStaff: false).Should().HaveCount(count);

    [Fact]
    public void Slots_LabelsAreLocalWithLeadingZero()
    {
        var first = Schedule().Slots(new DateOnly(2026, 10, 1), Noon, false)[0];
        first.Label.Should().Be("09:00–09:15");
    }

    [Fact]
    public void SlotsForDate_IsEmptyOutsideTheHorizon_WhileRawSlotsKnowNothingOfIt()
    {
        var far = Today.AddDays(5);
        Schedule().SlotsForDate(far, Noon, false).Should().BeEmpty();
        Schedule().Slots(far, Noon, false).Should().NotBeEmpty();
    }

    [Fact]
    public void SlotsReason_ExplainsEachEmptyCase()
    {
        Schedule(scheduled: false).SlotsReason(Today, Noon, false).Should().Be("Заказ ко времени в этом магазине недоступен");
        Schedule(preorder: 6).SlotsReason(new DateOnly(2026, 10, 3), Noon, false).Should().Be("В этот день магазин не работает");
        Schedule().SlotsReason(Today.AddDays(2), Noon, false).Should().Be("На эту дату заказать нельзя — выберите другую");
        Schedule().SlotsReason(Today.AddDays(1), Noon, false).Should().BeNull();
    }

    // ── time zones ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToUtc_IsTheLocalTimeMinusTheOffset_AndSkipsANonexistentLocalTime()
    {
        PickupSchedule.ToUtc(Moscow, Today, 540).Should().Be(new DateTime(2026, 9, 30, 6, 0, 0, DateTimeKind.Utc));
        PickupSchedule.ToUtc(Moscow, Today, 1500).Should().Be(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc)); // 25:00 = 01:00 next day

        // A zone with a spring-forward gap: 02:30 on 2026-03-29 does not exist in Europe/Berlin — moved to the first valid instant (+1 h).
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        PickupSchedule.ToUtc(berlin, new DateOnly(2026, 3, 29), 150).Should().Be(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void DayBounds_AndCurrentWorkingDay_UseTheShopsZone_NotUtc()
    {
        // 22:00 UTC on Sep 30 is already Oct 1 01:00 in Moscow — the shop's calendar date is Oct 1.
        var s = Schedule();
        s.LocalDate(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc)).Should().Be(new DateOnly(2026, 10, 1));
        s.CurrentWorkingDay(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc)).Should().Be(new DateOnly(2026, 10, 1));
    }

    // ── presentation ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SummaryLines_GlueEqualConsecutiveDays()
    {
        var week = new WeeklyHours(new Dictionary<DayOfWeek, IReadOnlyList<TimeInterval>>
        {
            [DayOfWeek.Monday] = [new(540, 1260)], [DayOfWeek.Tuesday] = [new(540, 1260)], [DayOfWeek.Wednesday] = [new(540, 1260)],
            [DayOfWeek.Thursday] = [new(540, 1260)], [DayOfWeek.Friday] = [new(540, 1260)], [DayOfWeek.Saturday] = [new(600, 900)],
        });
        PickupSchedule.SummaryLines(week).Should().Equal(("пн–пт", "09:00–21:00"), ("сб", "10:00–15:00"), ("вс", "выходной"));
        PickupSchedule.SummaryLines(null).Should().BeEmpty();
    }

    [Fact]
    public void SummaryLines_TwoDaysAreListedNotDashed()
    {
        var week = new WeeklyHours(new Dictionary<DayOfWeek, IReadOnlyList<TimeInterval>>
        {
            [DayOfWeek.Monday] = [new(540, 1260)], [DayOfWeek.Tuesday] = [new(540, 1260)],
        });
        PickupSchedule.SummaryLines(week).First().Should().Be(("пн, вт", "09:00–21:00"));
    }

    [Fact]
    public void PickupText_AsapTodayTomorrowLater()
    {
        var start = new DateTime(2026, 9, 30, 9, 30, 0, DateTimeKind.Utc); // 12:30 local
        PickupSchedule.PickupText(PickupKind.Asap, Today, start, Moscow, Noon).Should().Be("Как можно скорее (≈ 12:30)");
        PickupSchedule.PickupText(PickupKind.Slot, Today, start, Moscow, Noon).Should().Be("К 12:30");
        PickupSchedule.PickupText(PickupKind.Slot, Today.AddDays(1), start.AddDays(1), Moscow, Noon).Should().Be("Завтра, к 12:30");
        PickupSchedule.PickupText(PickupKind.Slot, new DateOnly(2026, 10, 2), start.AddDays(2), Moscow, Noon).Should().Be("пт 2 окт, к 12:30");
        // CY24-35: with the working day passed explicitly (after-midnight tail → yesterday), "today"/"tomorrow" count from it.
        PickupSchedule.PickupText(PickupKind.Slot, Today, start, Moscow, Noon, Today.AddDays(-1)).Should().Be("Завтра, к 12:30");
        PickupSchedule.PickupText(PickupKind.Slot, Today.AddDays(-1), start, Moscow, Noon, Today.AddDays(-1)).Should().Be("К 12:30");
    }
}
