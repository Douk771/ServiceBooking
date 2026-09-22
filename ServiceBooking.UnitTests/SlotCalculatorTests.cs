using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

public class SlotCalculatorTests
{
    private static readonly TimeOnly NineAm = new(9, 0);
    private static readonly TimeOnly SixPm = new(18, 0);

    // ── Calculate ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_WithinWorkingHours_ReturnsGridOfStepMinutes()
    {
        var slots = SlotCalculator.Calculate(60, NineAm, new TimeOnly(11, 0), [], [], ScheduleFallback.None);

        slots.Select(s => s.Start).Should().Equal(new TimeOnly(9, 0), new TimeOnly(9, 30), new TimeOnly(10, 0));
        slots.Should().OnlyContain(s => s.End - s.Start == TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void Calculate_SlotOverlappingBooking_IsExcluded()
    {
        var bookings = new List<TimeRange> { new(new TimeOnly(10, 0), new TimeOnly(11, 0)) };

        var slots = SlotCalculator.Calculate(60, NineAm, SixPm, [], bookings, ScheduleFallback.None);

        slots.Should().NotContain(s => s.Start == new TimeOnly(10, 0));
        slots.Should().NotContain(s => s.Start == new TimeOnly(9, 30)); // 9:30-10:30 overlaps 10:00-11:00
        slots.Should().Contain(s => s.Start == new TimeOnly(9, 0));
        slots.Should().Contain(s => s.Start == new TimeOnly(11, 0));
    }

    [Fact]
    public void Calculate_SlotOverlappingBreak_IsExcluded()
    {
        var breaks = new List<TimeRange> { new(new TimeOnly(13, 0), new TimeOnly(14, 0)) };

        var slots = SlotCalculator.Calculate(60, NineAm, SixPm, breaks, [], ScheduleFallback.None);

        slots.Should().NotContain(s => s.Start == new TimeOnly(13, 0));
        slots.Should().NotContain(s => s.Start == new TimeOnly(12, 30));
        slots.Should().Contain(s => s.Start == new TimeOnly(14, 0));
    }

    [Fact]
    public void Calculate_NoScheduleAndNotAllowed_ReturnsEmpty()
    {
        var slots = SlotCalculator.Calculate(60, null, null, [], [], ScheduleFallback.None);

        slots.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_NoScheduleAndAllowed_ReturnsFullDayGridFromMidnight()
    {
        var slots = SlotCalculator.Calculate(60, null, null, [], [], ScheduleFallback.WholeDay);

        slots.Should().Contain(s => s.Start == new TimeOnly(0, 0));
        // Last possible 60-minute slot is 22:30-23:30: a 23:00 start would end exactly at 24:00, which
        // TimeOnly can't represent, so the loop stops one step before rolling over (SlotService.cs:37).
        slots.Should().Contain(s => s.Start == new TimeOnly(22, 30));
        slots.Should().NotContain(s => s.Start > new TimeOnly(22, 30));
    }

    [Fact]
    public void Calculate_NoScheduleAndDefaultWindow_ReturnsConfiguredWindowNotWholeDay()
    {
        // ARCHITECTURE_CYCLE6.md §46.2: staff without extendedHours get a configured default window
        // (09:00-21:00 in this test), not the full day.
        var slots = SlotCalculator.Calculate(60, null, null, [], [], ScheduleFallback.DefaultWindow, NineAm, new TimeOnly(21, 0));

        slots.Should().Contain(s => s.Start == NineAm);
        slots.Should().NotContain(s => s.Start < NineAm);
        slots.Should().NotContain(s => s.Start >= new TimeOnly(21, 0));
    }

    [Fact]
    public void Calculate_ScheduleRowPresent_DefaultWindowIsIgnored()
    {
        // An actual WorkingHours row always wins over the fallback window, regardless of fallback kind.
        var slots = SlotCalculator.Calculate(60, NineAm, SixPm, [], [], ScheduleFallback.DefaultWindow, new TimeOnly(0, 0), new TimeOnly(23, 0));

        slots.Should().NotContain(s => s.Start < NineAm);
        slots.Should().NotContain(s => s.Start >= SixPm);
    }

    [Fact]
    public void Calculate_ScheduleRowPresent_WholeDayIgnoresScheduleWindow()
    {
        // SPEC.md §0.1 Q7 (поправка 2026-09-22): a master working 10:00-14:00 on a normal weekday
        // must still be reachable at 19:00 via the "show other hours" toggle — the server itself
        // accepts a staff booking at any free time regardless of schedule (isStaffManualBooking), so
        // the grid must offer it too. Before the fix, WholeDay only took effect when there was no
        // WorkingHours row at all, making the toggle a no-op on any date the master actually works.
        var slots = SlotCalculator.Calculate(60, new TimeOnly(10, 0), new TimeOnly(14, 0), [], [], ScheduleFallback.WholeDay);

        slots.Should().Contain(s => s.Start == new TimeOnly(19, 0));
        slots.Should().Contain(s => s.Start == new TimeOnly(0, 0));
    }

    [Fact]
    public void Calculate_ScheduleRowPresent_WholeDayAlsoIgnoresBreaks()
    {
        // Sopутствующее решение (SPEC.md §0.1 Q7): if WholeDay hid break time, staff would see less
        // than the server actually allows them to book — same bug in miniature.
        var breaks = new List<TimeRange> { new(new TimeOnly(12, 0), new TimeOnly(13, 0)) };

        var slots = SlotCalculator.Calculate(60, new TimeOnly(10, 0), new TimeOnly(14, 0), breaks, [], ScheduleFallback.WholeDay);

        slots.Should().Contain(s => s.Start == new TimeOnly(12, 0));
    }

    [Fact]
    public void Calculate_ServiceLongerThanWindow_ReturnsEmpty()
    {
        var slots = SlotCalculator.Calculate(600, NineAm, new TimeOnly(10, 0), [], [], ScheduleFallback.None);

        slots.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_ServiceDurationNotMultipleOfStep_StillGridsByStepMinutes()
    {
        // Known limitation (CURRENT_STATE §9 P2-9): the grid always advances by 30 minutes regardless
        // of service duration, so a 45-minute service produces overlapping-looking slot windows.
        var slots = SlotCalculator.Calculate(45, NineAm, new TimeOnly(10, 30), [], [], ScheduleFallback.None);

        slots.Select(s => s.Start).Should().Equal(new TimeOnly(9, 0), new TimeOnly(9, 30));
        slots[0].End.Should().Be(new TimeOnly(9, 45));
        slots[1].End.Should().Be(new TimeOnly(10, 15));
    }

    [Fact]
    public void Calculate_DoesNotRollPastMidnight()
    {
        var slots = SlotCalculator.Calculate(90, new TimeOnly(22, 30), null, [], [], ScheduleFallback.WholeDay);

        slots.Should().NotContain(s => s.Start >= new TimeOnly(23, 0));
    }

    // ── IsSlotAllowed ────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsSlotAllowed_ExactGridMatch_ReturnsTrue()
    {
        var ok = SlotCalculator.IsSlotAllowed(new TimeOnly(10, 0), 60, NineAm, SixPm, [], [], ScheduleFallback.None);

        ok.Should().BeTrue();
    }

    [Fact]
    public void IsSlotAllowed_NotOnGrid_ReturnsFalse()
    {
        var ok = SlotCalculator.IsSlotAllowed(new TimeOnly(10, 7), 60, NineAm, SixPm, [], [], ScheduleFallback.None);

        ok.Should().BeFalse();
    }

    [Fact]
    public void IsSlotAllowed_InsideBreak_ReturnsFalse()
    {
        var breaks = new List<TimeRange> { new(new TimeOnly(13, 0), new TimeOnly(14, 0)) };

        var ok = SlotCalculator.IsSlotAllowed(new TimeOnly(13, 0), 30, NineAm, SixPm, breaks, [], ScheduleFallback.None);

        ok.Should().BeFalse();
    }

    [Fact]
    public void IsSlotAllowed_OutsideWorkingWindow_ReturnsFalse()
    {
        var ok = SlotCalculator.IsSlotAllowed(new TimeOnly(20, 0), 60, NineAm, SixPm, [], [], ScheduleFallback.None);

        ok.Should().BeFalse();
    }

    [Fact]
    public void IsSlotAllowed_OverlapsExistingBooking_ReturnsFalse()
    {
        var bookings = new List<TimeRange> { new(new TimeOnly(10, 0), new TimeOnly(11, 0)) };

        var ok = SlotCalculator.IsSlotAllowed(new TimeOnly(10, 0), 60, NineAm, SixPm, [], bookings, ScheduleFallback.None);

        ok.Should().BeFalse();
    }

    [Fact]
    public void IsSlotAllowed_NoScheduleAndNotAllowed_ReturnsFalse()
    {
        var ok = SlotCalculator.IsSlotAllowed(new TimeOnly(10, 0), 60, null, null, [], [], ScheduleFallback.None);

        ok.Should().BeFalse();
    }

    [Fact]
    public void IsSlotAllowed_NoScheduleButAllowed_EarlyMorningSlot_ReturnsTrue()
    {
        var ok = SlotCalculator.IsSlotAllowed(new TimeOnly(3, 0), 60, null, null, [], [], ScheduleFallback.WholeDay);

        ok.Should().BeTrue();
    }
}
