using FluentAssertions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE24.md §448.1, §452.4 — the weekday mask of a product (bit 0 = Monday … bit 6 = Sunday).</summary>
public class WeekdayMaskTests
{
    [Fact]
    public void Bits_AreIsoOrder_MondayFirst()
    {
        WeekdayMask.Bit(DayOfWeek.Monday).Should().Be(1);
        WeekdayMask.Bit(DayOfWeek.Wednesday).Should().Be(4);
        WeekdayMask.Bit(DayOfWeek.Sunday).Should().Be(64);
        WeekdayMask.FromDays(Enum.GetValues<DayOfWeek>()).Should().Be(WeekdayMask.All).And.Be(127);
    }

    [Fact]
    public void Allows_ByDayAndByDate()
    {
        var weekend = WeekdayMask.FromDays([DayOfWeek.Saturday, DayOfWeek.Sunday]);
        WeekdayMask.Allows(weekend, DayOfWeek.Sunday).Should().BeTrue();
        WeekdayMask.Allows(weekend, DayOfWeek.Monday).Should().BeFalse();
        WeekdayMask.Allows(weekend, new DateOnly(2026, 10, 3)).Should().BeTrue();  // Saturday
        WeekdayMask.Allows(weekend, new DateOnly(2026, 10, 5)).Should().BeFalse(); // Monday
        WeekdayMask.Allows(0, new DateOnly(2026, 10, 3)).Should().BeFalse();
    }

    [Fact]
    public void ToDays_IsMondayFirst_AndRoundTrips()
    {
        var days = WeekdayMask.ToDays(WeekdayMask.FromDays([DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Friday]));
        days.Should().Equal(DayOfWeek.Monday, DayOfWeek.Friday, DayOfWeek.Sunday);
    }

    [Fact]
    public void Label_IsShortNames_NullForEveryDay_TextForEmpty()
    {
        WeekdayMask.Label(WeekdayMask.FromDays([DayOfWeek.Wednesday, DayOfWeek.Monday, DayOfWeek.Friday])).Should().Be("пн, ср, пт");
        WeekdayMask.Label(WeekdayMask.All).Should().BeNull();
        WeekdayMask.Label(0).Should().Be("только по меню");
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(127, true)]
    [InlineData(128, false)]
    public void IsValid_IsTheDatabaseCheckRange(int mask, bool valid) => WeekdayMask.IsValid(mask).Should().Be(valid);
}
