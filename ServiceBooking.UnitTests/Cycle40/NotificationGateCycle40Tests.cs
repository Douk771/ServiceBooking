using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.5.3 — the order of the cycle-40 gate's refusals.</summary>
public class NotificationGateCycle40Tests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly MessagingAvailability Ok = new(true, true, true);

    private static MessagingGateResult Run(
        MessagingAvailability? availability = null, bool optedOut = false, MessengerConsentDecision consent = MessengerConsentDecision.Allowed,
        NotificationType type = NotificationType.BookingConfirmed, CompanyNotificationSettings? settings = null, DateTime? visit = null) =>
        NotificationGate.Evaluate(type, availability ?? Ok, settings, optedOut, consent, Now, visit ?? Now.AddDays(1));

    [Fact] public void Everything_Fine_Allowed() => Run().IsAllowed.Should().BeTrue();

    [Fact]
    public void PlatformSwitch_BeatsEverything() =>
        Run(new(false, false, false), optedOut: true, consent: MessengerConsentDecision.Declined).Reason
            .Should().Be(MessagingBlockReason.PlatformMessagingDisabled);

    [Fact]
    public void OptOut_BeatsDeclined() =>
        Run(optedOut: true, consent: MessengerConsentDecision.Declined).Reason.Should().Be(MessagingBlockReason.RecipientOptedOut);

    [Fact]
    public void Declined_BeatsNoConsent_AndPayment() =>
        Run(new(true, false, false), consent: MessengerConsentDecision.Declined).Reason.Should().Be(MessagingBlockReason.ClientDeclinedMessenger);

    [Fact]
    public void NoConsent_BeatsPayment() =>
        Run(new(true, false, false), consent: MessengerConsentDecision.NoConsent).Reason.Should().Be(MessagingBlockReason.NoProviderDeliveryConsent);

    [Fact]
    public void NothingPaid_BeatsNothingRoutable() =>
        Run(new(true, false, false)).Reason.Should().Be(MessagingBlockReason.NotOnPaidPlan);

    [Fact]
    public void PaidButNotRoutable_NoUsableChannel() =>
        Run(new(true, true, false)).Reason.Should().Be(MessagingBlockReason.NoUsableChannel);

    [Fact]
    public void TypeMask_DisablesBookingType() =>
        Run(settings: new CompanyNotificationSettings { EnabledTypeMask = 0 }).Reason.Should().Be(MessagingBlockReason.TypeDisabledByCompany);

    [Fact]
    public void Reminder_BelowLeadTime_Blocked_ButConfirmationIsNot()
    {
        var soon = Now.AddMinutes(5);
        var settings = new CompanyNotificationSettings { MinLeadMinutes = 60 };
        Run(type: NotificationType.Reminder, settings: settings, visit: soon).Reason.Should().Be(MessagingBlockReason.BelowMinimumLeadTime);
        Run(type: NotificationType.BookingConfirmed, settings: settings, visit: soon).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void OrderTypes_IgnoreSalonMask() =>
        Run(type: NotificationType.OrderAccepted, settings: new CompanyNotificationSettings { EnabledTypeMask = 0 }).IsAllowed.Should().BeTrue();

    [Fact]
    public void MessagingBlockReason_NamesMatchNotificationReasonNames()
    {
        // Until BE-40-M adds the three new NotificationReason members, only the old ones can be checked by name.
        var existing = Enum.GetNames<NotificationReason>().ToHashSet();
        var cycle40Only = new[] { "ClientDeclinedMessenger", "PlatformMessagingDisabled", "ChannelAccountMismatch" };
        foreach (var name in Enum.GetNames<MessagingBlockReason>())
            (existing.Contains(name) || cycle40Only.Contains(name)).Should().BeTrue(name);
    }
}
