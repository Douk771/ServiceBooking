using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class BookingFiltersTests
{
    // ── TryParseClientStatus ─────────────────────────────────────────────────

    [Fact]
    public void TryParseClientStatus_Null_ReturnsAll()
    {
        var ok = BookingFilters.TryParseClientStatus(null, out var filter);

        ok.Should().BeTrue();
        filter.Kind.Should().Be(ClientStatusFilterKind.All);
    }

    [Fact]
    public void TryParseClientStatus_Empty_ReturnsAll()
    {
        var ok = BookingFilters.TryParseClientStatus("", out var filter);

        ok.Should().BeTrue();
        filter.Kind.Should().Be(ClientStatusFilterKind.All);
    }

    [Fact]
    public void TryParseClientStatus_Upcoming_ReturnsUpcomingKind()
    {
        var ok = BookingFilters.TryParseClientStatus("upcoming", out var filter);

        ok.Should().BeTrue();
        filter.Kind.Should().Be(ClientStatusFilterKind.Upcoming);
    }

    [Theory]
    [InlineData("UPCOMING")]
    [InlineData("Upcoming")]
    [InlineData("uPcOmInG")]
    public void TryParseClientStatus_UpcomingAnyCase_ReturnsUpcomingKind(string value)
    {
        var ok = BookingFilters.TryParseClientStatus(value, out var filter);

        ok.Should().BeTrue();
        filter.Kind.Should().Be(ClientStatusFilterKind.Upcoming);
    }

    [Fact]
    public void TryParseClientStatus_KnownStatusName_ReturnsByStatus()
    {
        var ok = BookingFilters.TryParseClientStatus("Completed", out var filter);

        ok.Should().BeTrue();
        filter.Kind.Should().Be(ClientStatusFilterKind.ByStatus);
        filter.Status.Should().Be(BookingStatus.Completed);
    }

    [Fact]
    public void TryParseClientStatus_KnownStatusNameLowerCase_ReturnsByStatus()
    {
        // The US-07 defect: lower-case status names used to be silently ignored instead of filtering.
        var ok = BookingFilters.TryParseClientStatus("cancelled", out var filter);

        ok.Should().BeTrue();
        filter.Kind.Should().Be(ClientStatusFilterKind.ByStatus);
        filter.Status.Should().Be(BookingStatus.Cancelled);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("-1")]
    public void TryParseClientStatus_NumericValue_ReturnsFalse(string value)
    {
        // Regression test (contracts/cycle17/openapi.yaml): `status` is documented as an enum of exact
        // names only. Enum.TryParse<T>(string, ...) has a well-known gotcha where it accepts ANY integer
        // literal as a "valid" enum value, even undefined ordinals (e.g. 99, -1) — which previously
        // slipped past this check and produced a 200 with an empty result instead of the documented 400.
        var ok = BookingFilters.TryParseClientStatus(value, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryParseClientStatus_Garbage_ReturnsFalse()
    {
        var ok = BookingFilters.TryParseClientStatus("not-a-real-status", out _);

        ok.Should().BeFalse();
    }

    // ── Upcoming expression ──────────────────────────────────────────────────

    [Fact]
    public void Upcoming_FutureDateConfirmed_IsTrue()
    {
        var today = new DateOnly(2026, 6, 1);
        var now = new TimeOnly(10, 0);
        var booking = new Booking { Status = BookingStatus.Confirmed, Date = today.AddDays(1), StartTime = new TimeOnly(9, 0) };

        BookingFilters.Upcoming(today, now).Compile()(booking).Should().BeTrue();
    }

    [Fact]
    public void Upcoming_TodayLaterThanNow_IsTrue()
    {
        var today = new DateOnly(2026, 6, 1);
        var now = new TimeOnly(10, 0);
        var booking = new Booking { Status = BookingStatus.Pending, Date = today, StartTime = new TimeOnly(11, 0) };

        BookingFilters.Upcoming(today, now).Compile()(booking).Should().BeTrue();
    }

    [Fact]
    public void Upcoming_TodayExactlyNow_IsFalse()
    {
        // Strict '>' — a booking starting exactly now is not "upcoming" anymore.
        var today = new DateOnly(2026, 6, 1);
        var now = new TimeOnly(10, 0);
        var booking = new Booking { Status = BookingStatus.Confirmed, Date = today, StartTime = now };

        BookingFilters.Upcoming(today, now).Compile()(booking).Should().BeFalse();
    }

    [Fact]
    public void Upcoming_PastDate_IsFalse()
    {
        var today = new DateOnly(2026, 6, 1);
        var now = new TimeOnly(10, 0);
        var booking = new Booking { Status = BookingStatus.Confirmed, Date = today.AddDays(-1), StartTime = new TimeOnly(9, 0) };

        BookingFilters.Upcoming(today, now).Compile()(booking).Should().BeFalse();
    }

    [Theory]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.NoShow)]
    public void Upcoming_FutureDateButFinalStatus_IsFalse(BookingStatus status)
    {
        var today = new DateOnly(2026, 6, 1);
        var now = new TimeOnly(10, 0);
        var booking = new Booking { Status = status, Date = today.AddDays(1), StartTime = new TimeOnly(9, 0) };

        BookingFilters.Upcoming(today, now).Compile()(booking).Should().BeFalse();
    }
}
