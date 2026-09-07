using FluentAssertions;
using ServiceBooking.API.Services.Scheduling;

namespace ServiceBooking.UnitTests;

public class ScheduledTaskScheduleTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    // ── IsDue ────────────────────────────────────────────────────────────────

    [Fact]
    public void IsDue_NeverRunBefore_IsDue()
    {
        ScheduledTaskSchedule.IsDue(null, OneDay, Now).Should().BeTrue();
    }

    [Fact]
    public void IsDue_LastRunWithinThePeriod_IsNotDue()
    {
        var lastStarted = Now.AddHours(-1);
        ScheduledTaskSchedule.IsDue(lastStarted, OneDay, Now).Should().BeFalse();
    }

    [Fact]
    public void IsDue_LastRunExactlyOnePeriodAgo_IsDue()
    {
        var lastStarted = Now - OneDay;
        ScheduledTaskSchedule.IsDue(lastStarted, OneDay, Now).Should().BeTrue();
    }

    [Fact]
    public void IsDue_LastRunLongerThanOnePeriodAgo_IsDue()
    {
        var lastStarted = Now.AddDays(-3);
        ScheduledTaskSchedule.IsDue(lastStarted, OneDay, Now).Should().BeTrue();
    }

    [Fact]
    public void IsDue_FailedPreviousRun_StillWaitsForTheNormalPeriod_NoImmediateRetry()
    {
        // SPEC US-21 p.3: a failed run is "scheduled for its next run on the usual schedule" — not an
        // immediate retry. IsDue only ever looks at LastStartedAtUtc, so a run that started (and then
        // failed) 1 hour ago is treated identically to a run that started and SUCCEEDED 1 hour ago.
        var lastStartedOfFailedRun = Now.AddHours(-1);
        ScheduledTaskSchedule.IsDue(lastStartedOfFailedRun, OneDay, Now).Should().BeFalse();
    }

    [Fact]
    public void IsDue_DifferentPeriod_ScalesTheDueWindow()
    {
        var lastStarted = Now.AddMinutes(-30);
        ScheduledTaskSchedule.IsDue(lastStarted, TimeSpan.FromMinutes(60), Now).Should().BeFalse();
        ScheduledTaskSchedule.IsDue(lastStarted, TimeSpan.FromMinutes(15), Now).Should().BeTrue();
    }

    // ── IsOverdue ────────────────────────────────────────────────────────────

    [Fact]
    public void IsOverdue_NeverFinished_IsOverdue()
    {
        ScheduledTaskSchedule.IsOverdue(null, OneDay, Now).Should().BeTrue();
    }

    [Fact]
    public void IsOverdue_FinishedWithinOnePeriod_IsNotOverdue()
    {
        ScheduledTaskSchedule.IsOverdue(Now.AddHours(-1), OneDay, Now).Should().BeFalse();
    }

    [Fact]
    public void IsOverdue_FinishedJustOverTwoPeriodsAgo_IsOverdue()
    {
        var lastFinished = Now - (OneDay * 2) - TimeSpan.FromMinutes(1);
        ScheduledTaskSchedule.IsOverdue(lastFinished, OneDay, Now).Should().BeTrue();
    }

    [Fact]
    public void IsOverdue_FinishedJustUnderTwoPeriodsAgo_IsNotOverdue()
    {
        var lastFinished = Now - (OneDay * 2) + TimeSpan.FromMinutes(1);
        ScheduledTaskSchedule.IsOverdue(lastFinished, OneDay, Now).Should().BeFalse();
    }
}
