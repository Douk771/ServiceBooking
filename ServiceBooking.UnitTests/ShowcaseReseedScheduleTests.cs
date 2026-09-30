using FluentAssertions;
using ServiceBooking.API.Services.Showcase;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE28.md §575.7 — the weekly re-seed slot rule and the options' defaults.</summary>
public class ShowcaseReseedScheduleTests
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"); // UTC+3, no DST
    private static readonly TimeOnly Time = new(4, 30);

    private static DateTime Utc(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void Options_AreOffByDefault_MondayHalfPastFourMoscow()
    {
        var options = new ShowcaseReseedOptions();

        options.Enabled.Should().BeFalse();
        options.DayOfWeek.Should().Be(DayOfWeek.Monday);
        options.TryGetLocalTime(out var time).Should().BeTrue();
        time.Should().Be(new TimeOnly(4, 30));
        options.TimeZoneId.Should().Be("Europe/Moscow");
    }

    [Theory]
    [InlineData("4:30")]
    [InlineData("25:00")]
    [InlineData("noon")]
    [InlineData("")]
    public void Options_LocalTime_MustBeHHmm(string value) =>
        new ShowcaseReseedOptions { LocalTime = value }.TryGetLocalTime(out _).Should().BeFalse();

    [Fact]
    public void LatestSlot_MidWeek_IsThisWeeksMonday()
    {
        // Wednesday 2026-10-07 13:00 Moscow → Monday 2026-10-05 04:30 Moscow = 01:30 UTC.
        ShowcaseReseedSchedule.LatestSlotUtc(Utc(2026, 10, 7, 10, 0), DayOfWeek.Monday, Time, Moscow).Should().Be(Utc(2026, 10, 5, 1, 30));
    }

    [Fact]
    public void LatestSlot_OnTheSlotDayBeforeTheSlotTime_IsLastWeeks()
    {
        // Monday 2026-10-05 04:00 Moscow (01:00 UTC): this week's slot is still ahead.
        ShowcaseReseedSchedule.LatestSlotUtc(Utc(2026, 10, 5, 1, 0), DayOfWeek.Monday, Time, Moscow).Should().Be(Utc(2026, 9, 28, 1, 30));
    }

    [Fact]
    public void LatestSlot_ExactlyAtTheSlot_IsThatSlot() =>
        ShowcaseReseedSchedule.LatestSlotUtc(Utc(2026, 10, 5, 1, 30), DayOfWeek.Monday, Time, Moscow).Should().Be(Utc(2026, 10, 5, 1, 30));

    [Fact]
    public void LatestSlot_UsesTheConfiguredZone_NotUtc()
    {
        // The same instant, Novosibirsk (UTC+7): Monday 04:30 local = Sunday 21:30 UTC.
        var novosibirsk = TimeZoneInfo.FindSystemTimeZoneById("Asia/Novosibirsk");

        ShowcaseReseedSchedule.LatestSlotUtc(Utc(2026, 10, 7, 10, 0), DayOfWeek.Monday, Time, novosibirsk).Should().Be(Utc(2026, 10, 4, 21, 30));
    }

    [Fact]
    public void IsDue_WhenTheLastReseedWasBeforeTheLatestSlot()
    {
        var now = Utc(2026, 10, 7, 10, 0);

        ShowcaseReseedSchedule.IsDue(now, Utc(2026, 9, 28, 1, 40), DayOfWeek.Monday, Time, Moscow).Should().BeTrue();
        ShowcaseReseedSchedule.IsDue(now, Utc(2026, 10, 5, 1, 29), DayOfWeek.Monday, Time, Moscow).Should().BeTrue();
    }

    [Fact]
    public void IsDue_NotAgain_OnceReseededAfterTheSlot()
    {
        var now = Utc(2026, 10, 7, 10, 0);

        ShowcaseReseedSchedule.IsDue(now, Utc(2026, 10, 5, 1, 31), DayOfWeek.Monday, Time, Moscow).Should().BeFalse();
        ShowcaseReseedSchedule.IsDue(now, Utc(2026, 10, 6, 12, 0), DayOfWeek.Monday, Time, Moscow).Should().BeFalse();
        ShowcaseReseedSchedule.IsDue(now, now, DayOfWeek.Monday, Time, Moscow).Should().BeFalse();
    }

    [Fact]
    public void IsDue_MissedSlot_IsMadeUpAtTheNextTick()
    {
        // The machine was down over Monday; it is Thursday now and the last re-seed was two Mondays ago.
        ShowcaseReseedSchedule.IsDue(Utc(2026, 10, 8, 9, 0), Utc(2026, 9, 28, 1, 45), DayOfWeek.Monday, Time, Moscow).Should().BeTrue();
    }

    [Fact]
    public void IsDue_NoRecordOfAReseed_IsDue() =>
        ShowcaseReseedSchedule.IsDue(Utc(2026, 10, 7, 10, 0), null, DayOfWeek.Monday, Time, Moscow).Should().BeTrue();
}
