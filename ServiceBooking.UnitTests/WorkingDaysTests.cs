using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE5.md §44.4 — US-74's response-deadline arithmetic. No production
/// calendar, only Sat/Sun excluded.</summary>
public class WorkingDaysTests
{
    // 2026-09-21 is a Monday.
    private static readonly DateTime Monday = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Add_ZeroDays_ReturnsTheSameInstant()
    {
        WorkingDays.Add(Monday, 0).Should().Be(Monday);
    }

    [Fact]
    public void Add_WithinTheSameWeek_SkipsNoWeekend()
    {
        // Monday + 4 working days = Friday.
        WorkingDays.Add(Monday, 4).DayOfWeek.Should().Be(DayOfWeek.Friday);
        WorkingDays.Add(Monday, 4).Date.Should().Be(new DateTime(2026, 9, 25));
    }

    [Fact]
    public void Add_CrossingAWeekend_SkipsSaturdayAndSunday()
    {
        // Monday + 5 working days = the FOLLOWING Monday (Sat/Sun don't count).
        var result = WorkingDays.Add(Monday, 5);
        result.DayOfWeek.Should().Be(DayOfWeek.Monday);
        result.Date.Should().Be(new DateTime(2026, 9, 28));
    }

    [Fact]
    public void Add_TenWorkingDays_MatchesTheDefaultResponseWindow()
    {
        // SubjectRequests:ResponseWorkingDays default is 10 (Q-L12) — two full working weeks from a Monday.
        var result = WorkingDays.Add(Monday, 10);
        result.Date.Should().Be(new DateTime(2026, 10, 5));
        result.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void Add_StartingOnASaturday_TheStartDayItselfDoesNotCount()
    {
        var saturday = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
        var result = WorkingDays.Add(saturday, 1);
        // Saturday -> Sunday (not counted) -> Monday (day 1).
        result.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void Add_PreservesTimeOfDay()
    {
        var result = WorkingDays.Add(Monday, 3);
        result.TimeOfDay.Should().Be(Monday.TimeOfDay);
    }

    [Fact]
    public void Add_NegativeDays_Throws()
    {
        var act = () => WorkingDays.Add(Monday, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
