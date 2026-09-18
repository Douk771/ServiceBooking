using FluentAssertions;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §30.3 — the one place IdleSinceUtc is computed.</summary>
public class ChannelIdleCalculatorTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Recompute_ActiveCompanyAndLivePeriod_ReturnsNull()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: Now.AddDays(-2), activeCompanyCount: 1, paidUntilUtc: Now.AddDays(10),
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().BeNull();
    }

    [Fact]
    public void Recompute_NoActiveCompany_StartsIdleNow_WhenNotAlreadyIdle()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: null, activeCompanyCount: 0, paidUntilUtc: Now.AddDays(10),
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().Be(Now);
    }

    [Fact]
    public void Recompute_AlreadyIdle_DoesNotRestartTheClock()
    {
        var idleSince = Now.AddDays(-5);
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: idleSince, activeCompanyCount: 0, paidUntilUtc: null,
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().Be(idleSince);
    }

    [Fact]
    public void Recompute_PeriodExpired_IsIdle_EvenWithActiveCompany()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: null, activeCompanyCount: 1, paidUntilUtc: Now.AddDays(-1),
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().Be(Now);
    }

    [Fact]
    public void Recompute_SuspendedByAdmin_IsIdle_EvenWithLivePeriodAndActiveCompany()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: null, activeCompanyCount: 1, paidUntilUtc: Now.AddDays(10),
            isSuspendedByAdmin: true, nowUtc: Now);

        result.Should().Be(Now);
    }

    [Fact]
    public void Recompute_NeverPaid_IsIdle()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: null, activeCompanyCount: 1, paidUntilUtc: null,
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().Be(Now);
    }

    [Fact]
    public void Recompute_RecoversFromIdle_WhenBothCausesResolveTogether()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: Now.AddDays(-4), activeCompanyCount: 1, paidUntilUtc: Now.AddDays(1),
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().BeNull();
    }

    [Fact]
    public void Recompute_PaidExactlyNow_CountsAsLive()
    {
        var result = ChannelIdleCalculator.Recompute(
            existingIdleSinceUtc: Now.AddDays(-1), activeCompanyCount: 1, paidUntilUtc: Now,
            isSuspendedByAdmin: false, nowUtc: Now);

        result.Should().BeNull();
    }
}
