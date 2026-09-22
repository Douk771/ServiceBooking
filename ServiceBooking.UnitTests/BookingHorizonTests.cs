using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

public class BookingHorizonTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Normalize_NullZeroOrNegative_ReturnsDefault(int? raw) =>
        BookingHorizon.Normalize(raw).Should().Be(90);

    [Fact]
    public void Normalize_PositiveValue_ReturnsAsIs() =>
        BookingHorizon.Normalize(30).Should().Be(30);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void TryNormalize_NullOrZero_SucceedsWithDefault(int? raw)
    {
        BookingHorizon.TryNormalize(raw, out var days).Should().BeTrue();
        days.Should().Be(90);
    }

    [Fact]
    public void TryNormalize_ValidValue_Succeeds()
    {
        BookingHorizon.TryNormalize(30, out var days).Should().BeTrue();
        days.Should().Be(30);
    }

    [Theory]
    [InlineData(366)]
    [InlineData(-5)]
    public void TryNormalize_OutOfRange_Fails(int raw) =>
        BookingHorizon.TryNormalize(raw, out _).Should().BeFalse();

    [Fact]
    public void TryNormalize_UpperBound365_Succeeds() =>
        BookingHorizon.TryNormalize(365, out var days).Should().BeTrue();

    [Fact]
    public void IsWithin_OnTheBoundaryDate_ReturnsTrue()
    {
        var today = new DateOnly(2026, 1, 1);
        BookingHorizon.IsWithin(today.AddDays(90), today, 90).Should().BeTrue();
    }

    [Fact]
    public void IsWithin_OneDayPastBoundary_ReturnsFalse()
    {
        var today = new DateOnly(2026, 1, 1);
        BookingHorizon.IsWithin(today.AddDays(91), today, 90).Should().BeFalse();
    }
}
