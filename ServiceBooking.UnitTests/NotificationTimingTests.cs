using FluentAssertions;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §34.1 — the sole place notification time arithmetic happens.</summary>
public class NotificationTimingTests
{
    // ── ComputeVisitStartUtc ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Europe/Kaliningrad", 10)]  // UTC+2
    [InlineData("Europe/Moscow", 9)]        // UTC+3
    [InlineData("Asia/Barnaul", 5)]         // UTC+7
    [InlineData("Asia/Vladivostok", 2)]     // UTC+10
    public void ComputeVisitStartUtc_ConvertsLocalNoonToExpectedUtcHour(string timeZoneId, int expectedUtcHour)
    {
        var date = new DateOnly(2026, 7, 1);
        var time = new TimeOnly(12, 0);

        var utc = NotificationTiming.ComputeVisitStartUtc(date, time, timeZoneId);

        utc.Hour.Should().Be(expectedUtcHour);
        utc.Date.Should().Be(date.ToDateTime(TimeOnly.MinValue).Date);
    }

    [Fact]
    public void ComputeVisitStartUtc_BarnaulAndNovosibirsk_SameOffsetButDistinctZones_AgreeOnWallClock()
    {
        // Both are UTC+7 today, but they are NOT the same IANA identifier (Altai Krai's 2016 move) —
        // this test exists so that "simplifying" Barnaul to Novosibirsk in seed data or code doesn't
        // silently look correct.
        var date = new DateOnly(2026, 3, 15);
        var time = new TimeOnly(9, 30);

        var barnaul = NotificationTiming.ComputeVisitStartUtc(date, time, "Asia/Barnaul");
        var novosibirsk = NotificationTiming.ComputeVisitStartUtc(date, time, "Asia/Novosibirsk");

        barnaul.Should().Be(novosibirsk); // same wall-clock offset today
    }

    [Fact]
    public void ComputeVisitStartUtc_CrossesMidnight_IntoPreviousUtcDay()
    {
        // 01:00 in Vladivostok (UTC+10) on the 2nd is still the 1st in UTC.
        var date = new DateOnly(2026, 6, 2);
        var time = new TimeOnly(1, 0);

        var utc = NotificationTiming.ComputeVisitStartUtc(date, time, "Asia/Vladivostok");

        utc.Should().Be(new DateTime(2026, 6, 1, 15, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ComputeVisitStartUtc_UnknownZone_Throws()
    {
        var act = () => NotificationTiming.ComputeVisitStartUtc(new DateOnly(2026, 1, 1), new TimeOnly(10, 0), "Not/AZone");
        act.Should().Throw<TimeZoneNotFoundException>();
    }

    // ── JitterMinutes ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void JitterMinutes_IsDeterministic_ForSameId()
    {
        var id = Guid.NewGuid();
        NotificationTiming.JitterMinutes(id, 15).Should().Be(NotificationTiming.JitterMinutes(id, 15));
    }

    [Fact]
    public void JitterMinutes_ZeroMax_AlwaysZero()
    {
        NotificationTiming.JitterMinutes(Guid.NewGuid(), 0).Should().Be(0);
    }

    [Fact]
    public void JitterMinutes_StaysWithinRange_AcrossManyIds()
    {
        for (var i = 0; i < 500; i++)
        {
            var jitter = NotificationTiming.JitterMinutes(Guid.NewGuid(), 15);
            jitter.Should().BeInRange(-15, 15);
        }
    }

    [Fact]
    public void JitterMinutes_DifferentIds_ProduceDifferentValues_NotAllZero()
    {
        var jitters = Enumerable.Range(0, 50).Select(_ => NotificationTiming.JitterMinutes(Guid.NewGuid(), 15)).ToList();
        jitters.Distinct().Count().Should().BeGreaterThan(1);
    }

    // ── ComputeReminderDueAtUtc ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeReminderDueAtUtc_NoJitter_IsExactlyLeadTimeBeforeVisit()
    {
        var visitStart = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var due = NotificationTiming.ComputeReminderDueAtUtc(Guid.NewGuid(), visitStart, 1440, 0);

        due.Should().Be(visitStart.AddMinutes(-1440));
    }

    [Fact]
    public void ComputeReminderDueAtUtc_WithJitter_StaysWithinJitterWindow()
    {
        var id = Guid.NewGuid();
        var visitStart = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var baseline = visitStart.AddMinutes(-1440);

        var due = NotificationTiming.ComputeReminderDueAtUtc(id, visitStart, 1440, 15);

        (due - baseline).TotalMinutes.Should().BeInRange(-15, 15);
    }

    // ── IsExpired / IsBelowMinimumLeadTime ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(-1, true)]  // visit was 1 minute ago
    [InlineData(0, true)]   // visit is exactly now
    [InlineData(1, false)]  // visit is 1 minute from now
    public void IsExpired_ComparesAgainstNow(int minutesFromNow, bool expected)
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var visitStart = now.AddMinutes(minutesFromNow);

        NotificationTiming.IsExpired(visitStart, now).Should().Be(expected);
    }

    [Theory]
    [InlineData(60, 120, true)]   // 60 min left, threshold 120 → below
    [InlineData(120, 120, false)] // exactly at threshold → not below
    [InlineData(200, 120, false)] // plenty of time → not below
    public void IsBelowMinimumLeadTime_ComparesRemainingTimeAgainstThreshold(
        int minutesUntilVisit, int minLeadMinutes, bool expected)
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var visitStart = now.AddMinutes(minutesUntilVisit);

        NotificationTiming.IsBelowMinimumLeadTime(visitStart, now, minLeadMinutes).Should().Be(expected);
    }
}
