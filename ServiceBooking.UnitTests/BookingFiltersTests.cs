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
    [InlineData("3", BookingStatus.Completed)]
    [InlineData("0", BookingStatus.Pending)]
    public void TryParseClientStatus_DefinedNumericValue_StillWorks(string value, BookingStatus expected)
    {
        // Backward compatibility, stated by TryParseClientStatus itself: numeric enum values were
        // always accepted and callers may rely on it. Cycle 17 broke this for a while by refusing all
        // numeric input; the rejection it was after is the NEXT test's job, and Enum.IsDefined already
        // did it.
        var ok = BookingFilters.TryParseClientStatus(value, out var filter);

        ok.Should().BeTrue();
        filter.Kind.Should().Be(ClientStatusFilterKind.ByStatus);
        filter.Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData(" 99 ")]
    public void TryParseClientStatus_UndefinedOrdinal_ReturnsFalse(string value)
    {
        // Enum.TryParse<T>(string, ...) accepts ANY integer literal, including ordinals that match no
        // member, which would give a 200 with an empty result instead of the documented 400. The
        // `Enum.IsDefined` check is what closes this — including the padded form, since TryParse trims
        // and a character-by-character digit guard would not.
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
