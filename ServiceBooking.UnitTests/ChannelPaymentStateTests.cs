using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §23.2 — payment state is computed, never stored. Cycle 22 (§379, Р2):
/// computed from the channel's funding (ChannelFundingReader), not the dropped PaidUntilUtc column.</summary>
public class ChannelPaymentStateTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ChannelFundingInfo Funding(ChannelFundingState state, DateTime? paidUntil) => new(state, "", paidUntil);

    [Fact]
    public void Of_NoFundingInfo_IsNotPaid()
    {
        ChannelPaymentState.Of(new NotificationChannel(), funding: null).Should().Be(ChannelPaymentStatus.NotPaid);
    }

    [Fact]
    public void Of_Funded_IsPaid()
    {
        ChannelPaymentState.Of(new NotificationChannel(), Funding(ChannelFundingState.Funded, Now.AddDays(10)))
            .Should().Be(ChannelPaymentStatus.Paid);
    }

    [Fact]
    public void Of_Unfunded_IsNotPaid_EvenWithAccountPaidUntilInFuture()
    {
        // The account pays for fewer numbers than it has — this one is outside the paid slots.
        ChannelPaymentState.Of(new NotificationChannel(), Funding(ChannelFundingState.Unfunded, Now.AddDays(10)))
            .Should().Be(ChannelPaymentStatus.NotPaid);
    }

    [Fact]
    public void Of_NotPaid_WithPastPaidUntil_IsNotPaid()
    {
        ChannelPaymentState.Of(new NotificationChannel(), Funding(ChannelFundingState.NotPaid, Now.AddDays(-1)))
            .Should().Be(ChannelPaymentStatus.NotPaid);
    }

    [Fact]
    public void Of_SuspendedByAdmin_OverridesFunding()
    {
        var channel = new NotificationChannel { IsSuspendedByAdmin = true };
        ChannelPaymentState.Of(channel, Funding(ChannelFundingState.Funded, Now.AddDays(10)))
            .Should().Be(ChannelPaymentStatus.Suspended);
    }
}
