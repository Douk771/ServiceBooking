using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.5.2, §40.5.5 — the order the three schedulers share: gate over the account state, then routing over
/// the ROUTABLE transports (pure; the account state is built by the reader's own pure core).</summary>
public class MessagingQueueingTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TransportOptionState Open = new(true, true, 490m);
    private static readonly LegacySubscriptionFacts NoSubscription = new(false, null);

    private static NotificationChannel Channel(NotificationTransport transport, ChannelState state = ChannelState.Connected, bool suspended = false) => new()
    {
        Id = Guid.NewGuid(), Transport = transport, State = state, IsSuspendedByAdmin = suspended, CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static AccountMessagingState Account(
        bool whatsAppPaid, bool maxPaid, NotificationChannel? whatsApp, NotificationChannel? max, bool platformEnabled = true)
    {
        TransportState Build(NotificationTransport t, bool paid, NotificationChannel? c) => AccountMessagingReader.BuildTransport(
            t, paid ? new OptionRowFacts(null, Now.AddDays(10), false, Now.AddDays(-5)) : null, null, NoSubscription, Now,
            c is null ? [] : [c], Open, platformEnabled);
        return new AccountMessagingState(Guid.NewGuid(), platformEnabled, Build(NotificationTransport.WhatsApp, whatsAppPaid, whatsApp),
            Build(NotificationTransport.Max, maxPaid, max), new[] { whatsApp, max }.Where(c => c is not null).Select(c => c!).ToList());
    }

    private static QueueingDecision Decide(
        AccountMessagingState account, CompanyNotificationSettings? settings = null, bool optedOut = false,
        MessengerConsentDecision consent = MessengerConsentDecision.Allowed, NotificationType type = NotificationType.BookingConfirmed) =>
        MessagingQueueing.Decide(type, account, settings, optedOut, consent, Now, Now.AddDays(1));

    [Fact]
    public void OnePaidConnectedTransport_RoutesEverythingThere_RegardlessOfModeAndPriority()
    {
        var max = Channel(NotificationTransport.Max);
        var settings = new CompanyNotificationSettings
        {
            DeliveryMode = NotificationDeliveryMode.PriorityChannel, PriorityTransport = NotificationTransport.WhatsApp,
        };

        var decision = Decide(Account(false, true, null, max), settings);

        decision.IsSkipped.Should().BeFalse();
        decision.Targets.Should().ContainSingle().Which.Should().Be(new NotificationRouting.Target(max.Id, NotificationTransport.Max));
    }

    [Fact]
    public void ADisconnectedNumber_StillRoutes_TheDispatcherHoldsTheRow()
    {
        var whatsApp = Channel(NotificationTransport.WhatsApp, ChannelState.Disconnected);

        Decide(Account(true, false, whatsApp, null)).Targets.Should().ContainSingle().Which.ChannelId.Should().Be(whatsApp.Id);
    }

    [Fact]
    public void AdminSuspendedNumber_IsNotRoutable_NoUsableChannel()
    {
        var max = Channel(NotificationTransport.Max, suspended: true);

        var decision = Decide(Account(false, true, null, max));

        decision.SkipReason.Should().Be(NotificationReason.NoUsableChannel);
        decision.Targets.Should().BeEmpty();
        decision.RepresentativeChannelId.Should().Be(max.Id, "the Skipped row points at the account's first number");
    }

    [Fact]
    public void NeverBoundNumber_IsNotRoutable()
    {
        Decide(Account(true, false, Channel(NotificationTransport.WhatsApp, ChannelState.NotConnected), null))
            .SkipReason.Should().Be(NotificationReason.NoUsableChannel);
    }

    [Fact]
    public void NothingPaid_IsNotOnPaidPlan()
    {
        Decide(Account(false, false, Channel(NotificationTransport.Max), null)).SkipReason.Should().Be(NotificationReason.NotOnPaidPlan);
    }

    [Fact]
    public void TwoRoutable_AllChannels_BothInTransportOrder_Priority_OnlyThePriorityOne()
    {
        var whatsApp = Channel(NotificationTransport.WhatsApp);
        var max = Channel(NotificationTransport.Max);
        var account = Account(true, true, whatsApp, max);

        Decide(account, new CompanyNotificationSettings { DeliveryMode = NotificationDeliveryMode.AllChannels })
            .Targets.Select(t => t.Transport).Should().Equal(NotificationTransport.WhatsApp, NotificationTransport.Max);
        Decide(account, new CompanyNotificationSettings { DeliveryMode = NotificationDeliveryMode.PriorityChannel, PriorityTransport = NotificationTransport.Max })
            .Targets.Should().ContainSingle().Which.Transport.Should().Be(NotificationTransport.Max);
    }

    [Fact]
    public void PriorityTransportBroken_TwoRoutable_NoSilentSwitchToTheOther_ButAnUnroutableOneLeavesOnlyTheOther()
    {
        // Priority = WhatsApp, WhatsApp is suspended -> only MAX is routable -> "one transport" rule: all goes to MAX.
        var max = Channel(NotificationTransport.Max);
        var decision = Decide(
            Account(true, true, Channel(NotificationTransport.WhatsApp, suspended: true), max),
            new CompanyNotificationSettings { DeliveryMode = NotificationDeliveryMode.PriorityChannel, PriorityTransport = NotificationTransport.WhatsApp });

        decision.Targets.Should().ContainSingle().Which.ChannelId.Should().Be(max.Id);
    }

    [Theory]
    [InlineData(MessengerConsentDecision.Declined, NotificationReason.ClientDeclinedMessenger)]
    [InlineData(MessengerConsentDecision.NoConsent, NotificationReason.NoProviderDeliveryConsent)]
    public void NoConsent_SkipsWithTheConsentReason_EvenWhenEverythingElseIsFine(MessengerConsentDecision consent, NotificationReason expected)
    {
        Decide(Account(false, true, null, Channel(NotificationTransport.Max)), consent: consent).SkipReason.Should().Be(expected);
    }

    [Fact]
    public void PlatformSwitchOff_SkipsWithPlatformMessagingDisabled_BeforeAnythingElse()
    {
        Decide(Account(false, true, null, Channel(NotificationTransport.Max), platformEnabled: false), optedOut: true)
            .SkipReason.Should().Be(NotificationReason.PlatformMessagingDisabled);
    }

    [Fact]
    public void OptOut_Skips_AndNoNumberAtAll_HasNoRepresentativeChannel()
    {
        var decision = Decide(Account(false, false, null, null), optedOut: true);

        decision.SkipReason.Should().Be(NotificationReason.RecipientOptedOut);
        decision.RepresentativeChannelId.Should().BeNull();
    }
}
