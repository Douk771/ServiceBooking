using FluentAssertions;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §30.3 — the one place IdleSinceUtc is computed. Cycle 22 (§379, Р2):
/// "paid period live" is the channel's funding (a bool from ChannelFundingReader), not a paid-until date;
/// the "paid exactly now counts as live" boundary now lives in SubscriptionResolver.IsOptionCurrentlyPaid
/// (SubscriptionResolverOptionGatingTests).</summary>
public class ChannelIdleCalculatorTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Recompute_ActiveCompanyAndFunded_ReturnsNull()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: Now.AddDays(-2), activeCompanyCount: 1, isFunded: true,
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().BeNull();
    }

    [Fact]
    public void Recompute_NoActiveCompany_StartsIdleNow_WhenNotAlreadyIdle()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: null, activeCompanyCount: 0, isFunded: true,
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().Be(Now);
    }

    [Fact]
    public void Recompute_AlreadyIdle_DoesNotRestartTheClock()
    {
        var idleSince = Now.AddDays(-5);
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: idleSince, activeCompanyCount: 0, isFunded: false,
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().Be(idleSince);
    }

    [Fact]
    public void Recompute_NotFunded_IsIdle_EvenWithActiveCompany()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: null, activeCompanyCount: 1, isFunded: false,
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().Be(Now);
    }

    [Fact]
    public void Recompute_SuspendedByAdmin_IsIdle_EvenWhenFundedWithActiveCompany()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: null, activeCompanyCount: 1, isFunded: true,
            isSuspendedByAdmin: true, nowUtc: Now);

        result.Should().Be(Now);
    }

    [Fact]
    public void Recompute_NotFundedAndNoActiveCompany_IsIdle()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: null, activeCompanyCount: 0, isFunded: false,
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().Be(Now);
    }

    [Fact]
    public void Recompute_RecoversFromIdle_WhenBothCausesResolveTogether()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: Now.AddDays(-4), activeCompanyCount: 1, isFunded: true,
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().BeNull();
    }
}
