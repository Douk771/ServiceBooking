using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §23.2 — payment state is computed, never stored.</summary>
public class ChannelPaymentStateTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Of_NoPaidUntil_IsNotPaid()
    {
        var channel = new NotificationChannel { PaidUntilUtc = null };
        ChannelPaymentState.Of(channel, Now).Should().Be(ChannelPaymentStatus.NotPaid);
    }

    [Fact]
    public void Of_PaidUntilInFuture_IsPaid()
    {
        var channel = new NotificationChannel { PaidUntilUtc = Now.AddDays(10) };
        ChannelPaymentState.Of(channel, Now).Should().Be(ChannelPaymentStatus.Paid);
    }

    [Fact]
    public void Of_PaidUntilExactlyNow_IsPaid()
    {
        var channel = new NotificationChannel { PaidUntilUtc = Now };
        ChannelPaymentState.Of(channel, Now).Should().Be(ChannelPaymentStatus.Paid);
    }

    [Fact]
    public void Of_PaidUntilInPast_IsSuspended()
    {
        var channel = new NotificationChannel { PaidUntilUtc = Now.AddDays(-1) };
        ChannelPaymentState.Of(channel, Now).Should().Be(ChannelPaymentStatus.Suspended);
    }

    [Fact]
    public void Of_SuspendedByAdmin_OverridesLivePeriod()
    {
        var channel = new NotificationChannel { PaidUntilUtc = Now.AddDays(10), IsSuspendedByAdmin = true };
        ChannelPaymentState.Of(channel, Now).Should().Be(ChannelPaymentStatus.Suspended);
    }
}
