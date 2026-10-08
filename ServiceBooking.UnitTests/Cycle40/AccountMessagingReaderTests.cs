using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.3.3, §40.5.1 — the pure core of <see cref="AccountMessagingReader"/> (no database).</summary>
public class AccountMessagingReaderTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly LegacySubscriptionFacts NoSubscription = new(false, null);
    private static readonly TransportOptionState Open = new(true, true, 490m);

    private static NotificationChannel Channel(
        NotificationTransport transport, int minutes, ChannelState state = ChannelState.Connected, bool suspended = false) => new()
    {
        Id = Guid.NewGuid(), Transport = transport, State = state, IsSuspendedByAdmin = suspended,
        CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes),
    };

    private static OptionRowFacts Paid(DateTime until) => new(null, until, false, Now.AddDays(-5));

    private static TransportState Build(
        NotificationTransport transport, OptionRowFacts? row, IEnumerable<NotificationChannel> channels,
        bool platformEnabled = true, TransportOptionState? option = null, DateTime? trialEnd = null) =>
        AccountMessagingReader.BuildTransport(transport, row, trialEnd, NoSubscription, Now, channels, option ?? Open, platformEnabled);

    [Fact]
    public void PaidTransport_FirstLiveNumberIsFunded_AndWorksWhenConnected()
    {
        var first = Channel(NotificationTransport.Max, 0);
        var state = Build(NotificationTransport.Max, Paid(Now.AddDays(10)), [first]);

        state.Paid.Should().BeTrue();
        state.Primary.Should().BeSameAs(first);
        state.Routable.Should().BeTrue();
        state.Working.Should().BeTrue();
    }

    [Fact]
    public void SecondNumberOfTheSameTransport_IsADuplicate_NotThePrimary()
    {
        var first = Channel(NotificationTransport.Max, 0);
        var second = Channel(NotificationTransport.Max, 5);
        var state = Build(NotificationTransport.Max, Paid(Now.AddDays(10)), [second, first]);

        state.Primary.Should().BeSameAs(first);
        state.Duplicates.Should().ContainSingle().Which.Should().BeSameAs(second);
    }

    [Fact]
    public void ChannelsOfTheOtherTransportAndReplacedOnes_AreIgnored()
    {
        var whatsapp = Channel(NotificationTransport.WhatsApp, 0);
        var replaced = Channel(NotificationTransport.Max, 1, ChannelState.Replaced);
        var live = Channel(NotificationTransport.Max, 2);
        var state = Build(NotificationTransport.Max, Paid(Now.AddDays(10)), [whatsapp, replaced, live]);

        state.Primary.Should().BeSameAs(live);
        state.Duplicates.Should().BeEmpty();
    }

    [Fact]
    public void NotPaidTransport_IsNotRoutable_EvenWithAConnectedNumber()
    {
        var state = Build(NotificationTransport.Max, null, [Channel(NotificationTransport.Max, 0)]);

        state.Paid.Should().BeFalse();
        state.Routable.Should().BeFalse();
        state.Working.Should().BeFalse();
    }

    [Fact]
    public void ExpiredOption_IsNotPaid() =>
        Build(NotificationTransport.Max, Paid(Now.AddDays(-1)), [Channel(NotificationTransport.Max, 0)]).Routable.Should().BeFalse();

    [Fact]
    public void SuspendedByAdmin_IsNotRoutable()
    {
        var state = Build(NotificationTransport.Max, Paid(Now.AddDays(10)), [Channel(NotificationTransport.Max, 0, suspended: true)]);
        state.Paid.Should().BeTrue();
        state.Routable.Should().BeFalse();
    }

    [Theory]
    [InlineData(ChannelState.Connected, true)]
    [InlineData(ChannelState.Disconnected, true)]
    [InlineData(ChannelState.NeedsReconnect, true)]
    [InlineData(ChannelState.Blocked, true)]
    [InlineData(ChannelState.NotConnected, false)]
    [InlineData(ChannelState.Connecting, false)]
    [InlineData(ChannelState.DisabledByOwner, false)]
    public void Routable_RequiresTheNumberToHaveBeenBoundAtLeastOnce(ChannelState channelState, bool routable) =>
        Build(NotificationTransport.Max, Paid(Now.AddDays(10)), [Channel(NotificationTransport.Max, 0, channelState)]).Routable.Should().Be(routable);

    [Fact]
    public void Working_NeedsConnected_AndThePlatformSwitchOn()
    {
        var disconnected = Build(NotificationTransport.Max, Paid(Now.AddDays(10)), [Channel(NotificationTransport.Max, 0, ChannelState.Disconnected)]);
        disconnected.Routable.Should().BeTrue();
        disconnected.Working.Should().BeFalse();

        var switchedOff = Build(NotificationTransport.Max, Paid(Now.AddDays(10)), [Channel(NotificationTransport.Max, 0)], platformEnabled: false);
        switchedOff.Routable.Should().BeTrue();
        switchedOff.Working.Should().BeFalse();
    }

    [Fact]
    public void ClosedOption_DoesNotSwitchOffWhatWasBought()
    {
        // Р40-Ю1: closing WhatsApp forbids selling it; the paid number keeps routing and working.
        var closed = new TransportOptionState(false, false, null);
        var state = Build(NotificationTransport.WhatsApp, Paid(Now.AddDays(10)), [Channel(NotificationTransport.WhatsApp, 0)], option: closed);

        state.Paid.Should().BeTrue();
        state.Routable.Should().BeTrue();
        state.Working.Should().BeTrue();
        state.Option.Open.Should().BeFalse();
    }

    [Fact]
    public void TrialRowWithoutADate_IsPaidUntilTheTrialEnds()
    {
        var trialRow = new OptionRowFacts(null, null, true, Now.AddDays(-1));
        var running = Build(NotificationTransport.Max, trialRow, [Channel(NotificationTransport.Max, 0)], trialEnd: Now.AddDays(5));
        running.Payment.IsTrial.Should().BeTrue();
        running.Paid.Should().BeTrue();

        Build(NotificationTransport.Max, trialRow, [], trialEnd: Now.AddDays(-1)).Paid.Should().BeFalse();
    }

    [Fact]
    public void State_FundingOf_RanksPerTransport_AndCountsAnAccountWithTwoPaidTransports()
    {
        var wa = Channel(NotificationTransport.WhatsApp, 0);
        var mx = Channel(NotificationTransport.Max, 0);
        var mxExtra = Channel(NotificationTransport.Max, 9);
        var channels = new[] { wa, mx, mxExtra };
        var state = new AccountMessagingState(
            Guid.NewGuid(), true,
            Build(NotificationTransport.WhatsApp, null, channels),
            Build(NotificationTransport.Max, Paid(Now.AddDays(10)), channels),
            channels);

        state.FundingOf(wa).Should().Be(ChannelFundingState.NotPaid);
        state.FundingOf(mx).Should().Be(ChannelFundingState.Funded);
        state.FundingOf(mxExtra).Should().Be(ChannelFundingState.Unfunded);
        state.AnyPaid.Should().BeTrue();
        state.PaidTransportCount.Should().Be(1);
        state.FundedChannel(NotificationTransport.Max).Should().BeSameAs(mx);
        state.FundedChannel(NotificationTransport.WhatsApp).Should().BeNull();
        state.AnyRoutable.Should().BeTrue();
    }

    [Fact]
    public void ChannelOfNoTransportSlot_CountsAsNotPaid()
    {
        var replaced = Channel(NotificationTransport.Max, 0, ChannelState.Replaced);
        var state = new AccountMessagingState(
            Guid.NewGuid(), true,
            Build(NotificationTransport.WhatsApp, null, [replaced]),
            Build(NotificationTransport.Max, Paid(Now.AddDays(10)), [replaced]),
            [replaced]);

        state.FundingOf(replaced).Should().Be(ChannelFundingState.NotPaid);
    }

    [Theory]
    [InlineData("notifications.whatsapp", NotificationTransport.WhatsApp)]
    [InlineData("notifications.max", NotificationTransport.Max)]
    public void OptionCode_AndTransport_AreInverse(string code, NotificationTransport transport)
    {
        AccountMessagingReader.OptionCode(transport).Should().Be(code);
        AccountMessagingReader.TransportOf(code).Should().Be(transport);
    }

    [Fact]
    public void TransportOf_UnknownCode_IsNull() => AccountMessagingReader.TransportOf("analytics").Should().BeNull();
}
