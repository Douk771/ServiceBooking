using FluentAssertions;
using ServiceBooking.API.Services.Orders.Reports;
using Xunit;

namespace ServiceBooking.UnitTests;

public class ReportPeriodDateRangeTests
{
    private static readonly DateOnly WorkingDay = new(2026, 9, 30);

    [Theory]
    [InlineData("0001-01-01", "0001-01-05")]
    [InlineData("2026-01-01", "9999-12-31")]
    public void Custom_DatesOutOfSaneRange_AreRefused(string from, string to)
    {
        ReportPeriod.TryResolve(ReportPeriodPreset.Custom, DateOnly.Parse(from), DateOnly.Parse(to), WorkingDay, out _, out var error).Should().BeFalse();
        error.Should().Be(ReportPeriod.DateOutOfRange);
    }

    [Fact]
    public void Custom_AtTheLowerBound_PreviousDoesNotOverflow()
    {
        ReportPeriod.TryResolve(ReportPeriodPreset.Custom, ReportPeriod.MinDate, ReportPeriod.MinDate.AddDays(365), WorkingDay, out var period, out _).Should().BeTrue();
        period.Previous().From.Should().BeBefore(ReportPeriod.MinDate);
    }
}
