using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

public class TrialWindowTests
{
    private static readonly DateTime TrialStart = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TrialEnd = TrialStart.AddDays(14);

    [Fact]
    public void WindowEnd_NoChannelAuthorized_ReturnsNull()
    {
        TrialWindow.WindowEnd(null, TrialStart, TrialEnd, 7).Should().BeNull();
    }

    [Fact]
    public void WindowEnd_ChannelAuthorizedAfterStart_UsualCase()
    {
        var authorizedAt = TrialStart.AddDays(1);
        var end = TrialWindow.WindowEnd(authorizedAt, TrialStart, TrialEnd, 7);
        end.Should().Be(authorizedAt.AddDays(7));
    }

    [Fact]
    public void WindowEnd_ChannelAuthorizedNearTrialEnd_CapsAtTrialEnd()
    {
        var authorizedAt = TrialEnd.AddDays(-2);
        var end = TrialWindow.WindowEnd(authorizedAt, TrialStart, TrialEnd, 7);
        end.Should().Be(TrialEnd);
    }

    [Fact]
    public void WindowEnd_ChannelAuthorizedBeforeTrialStarted_UsesMaxNotHistory()
    {
        // Account lived on a paid plan, dropped to Free, then got a trial — the channel was authorized
        // long before the trial existed. The window must not be burned by that history (§336.2).
        var authorizedAt = TrialStart.AddDays(-100);
        var end = TrialWindow.WindowEnd(authorizedAt, TrialStart, TrialEnd, 7);
        end.Should().Be(TrialStart.AddDays(7));
    }

    [Fact]
    public void ApplicableThreshold_NoneCrossedYet_ReturnsNull()
    {
        TrialWindow.ApplicableThreshold([7, 3, 1], alreadyWarnedAtThresholdDays: null, daysLeft: 10).Should().BeNull();
    }

    [Fact]
    public void ApplicableThreshold_FirstThresholdCrossed_ReturnsIt()
    {
        TrialWindow.ApplicableThreshold([7, 3, 1], alreadyWarnedAtThresholdDays: null, daysLeft: 7).Should().Be(7);
    }

    [Fact]
    public void ApplicableThreshold_AlreadyWarnedAtThatThreshold_DoesNotRepeat()
    {
        TrialWindow.ApplicableThreshold([7, 3, 1], alreadyWarnedAtThresholdDays: 7, daysLeft: 2).Should().Be(3);
    }

    [Fact]
    public void ApplicableThreshold_MissedSeveralPasses_ReturnsOnlyClosestNotYetCrossed()
    {
        // A background pass that skipped several days must not fire a burst of stale warnings —
        // only the nearest not-yet-crossed threshold (US-18-10).
        TrialWindow.ApplicableThreshold([7, 3, 1], alreadyWarnedAtThresholdDays: null, daysLeft: 0).Should().Be(1);
    }

    [Fact]
    public void ApplicableThreshold_AllThresholdsAlreadyWarned_ReturnsNull()
    {
        TrialWindow.ApplicableThreshold([7, 3, 1], alreadyWarnedAtThresholdDays: 1, daysLeft: 0).Should().BeNull();
    }

    [Fact]
    public void ParseThresholds_ValidSnapshot_ParsesInOrder()
    {
        TrialWindow.ParseThresholds("7,3,1").Should().BeEquivalentTo(new[] { 7, 3, 1 }, o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("7,-1,3")]
    public void ParseThresholds_CorruptOrMissing_FallsBackToDefault(string? snapshot)
    {
        TrialWindow.ParseThresholds(snapshot).Should().BeEquivalentTo(TrialWindow.DefaultWarningThresholds);
    }
}
