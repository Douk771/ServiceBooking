using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §23.2 — the one rule for "can this be queued/sent".</summary>
public class NotificationGateTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    // Cycle 5 (§47.2): the gate's first check is now PaidNotificationNumbers == 0, not
    // AllowNotificationChannel — AllowingPlan/DenyingPlan set both together so existing scenarios below
    // keep meaning "this account currently has the option funded" / "does not".
    private static readonly EffectivePlan AllowingPlan =
        EffectivePlan.Free with { AllowNotificationChannel = true, PaidNotificationNumbers = 1 };
    private static readonly EffectivePlan DenyingPlan =
        EffectivePlan.Free with { AllowNotificationChannel = false, PaidNotificationNumbers = 0 };

    private static NotificationChannel PaidChannel() => new() { PaidUntilUtc = Now.AddDays(10) };
    private static CompanyNotificationSettings DefaultSettings() => new();

    private static NotificationGateResult Evaluate(
        EffectivePlan? plan = null, NotificationType type = NotificationType.Reminder,
        bool hasAssignment = true, NotificationChannel? channel = null,
        CompanyNotificationSettings? settings = null, bool optedOut = false,
        DateTime? visitStart = null, bool channelIsFunded = true,
        ProviderDeliveryConsentMode providerDeliveryConsentMode = ProviderDeliveryConsentMode.AccountsOnly,
        bool? recipientHasProviderDeliveryConsent = null) =>
        NotificationGate.Evaluate(
            plan ?? AllowingPlan, type, hasAssignment, channel ?? PaidChannel(), settings ?? DefaultSettings(),
            optedOut, Now, visitStart ?? Now.AddDays(1), channelIsFunded,
            providerDeliveryConsentMode, recipientHasProviderDeliveryConsent);

    [Fact]
    public void Evaluate_EverythingFine_Allowed()
    {
        Evaluate().Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    [Fact]
    public void Evaluate_RecipientOptedOut_Blocked_EvenIfEverythingElseFine()
    {
        var result = Evaluate(optedOut: true);
        result.Outcome.Should().Be(NotificationGateOutcome.Blocked);
        result.Reason.Should().Be(NotificationReason.RecipientOptedOut);
    }

    [Fact]
    public void Evaluate_PlanDoesNotAllowChannel_Blocked()
    {
        var result = Evaluate(plan: DenyingPlan);
        result.Reason.Should().Be(NotificationReason.NotOnPaidPlan);
    }

    [Fact]
    public void Evaluate_NoAssignment_Blocked()
    {
        var result = Evaluate(hasAssignment: false);
        result.Reason.Should().Be(NotificationReason.NoUsableChannel);
    }

    [Fact]
    public void Evaluate_ChannelNull_Blocked()
    {
        var result = NotificationGate.Evaluate(
            AllowingPlan, NotificationType.Reminder, companyHasAssignment: true, channel: null,
            DefaultSettings(), recipientOptedOut: false, Now, Now.AddDays(1), channelIsFunded: true);
        result.Reason.Should().Be(NotificationReason.NoUsableChannel);
    }

    [Fact]
    public void Evaluate_ChannelNotFunded_Blocked()
    {
        // §47.2: an Unfunded channel blocks with NotOnPaidPlan (append-only NotificationReason honestly
        // covers both "account never paid" and "paid for fewer numbers than configured", §47.2).
        var result = Evaluate(channel: new NotificationChannel { PaidUntilUtc = null }, channelIsFunded: false);
        result.Reason.Should().Be(NotificationReason.NotOnPaidPlan);
    }

    [Fact]
    public void Evaluate_ChannelFundingExpired_Blocked()
    {
        var result = Evaluate(channel: new NotificationChannel { PaidUntilUtc = Now.AddDays(-1) }, channelIsFunded: false);
        result.Reason.Should().Be(NotificationReason.NotOnPaidPlan);
    }

    [Fact]
    public void Evaluate_TypeDisabledInSettings_Blocked()
    {
        var settings = new CompanyNotificationSettings { EnabledTypeMask = ~(1 << (int)NotificationType.Reminder) };
        var result = Evaluate(type: NotificationType.Reminder, settings: settings);
        result.Reason.Should().Be(NotificationReason.TypeDisabledByCompany);
    }

    [Fact]
    public void Evaluate_TypeEnabledInSettings_Allowed()
    {
        var settings = new CompanyNotificationSettings { EnabledTypeMask = 1 << (int)NotificationType.Reminder, MinLeadMinutes = 0 };
        var result = Evaluate(type: NotificationType.Reminder, settings: settings, visitStart: Now.AddDays(1));
        result.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    [Fact]
    public void Evaluate_BelowMinimumLeadTime_Blocked()
    {
        var settings = new CompanyNotificationSettings { MinLeadMinutes = 120 };
        var result = Evaluate(settings: settings, visitStart: Now.AddMinutes(30));
        result.Reason.Should().Be(NotificationReason.BelowMinimumLeadTime);
    }

    [Fact]
    public void Evaluate_ChannelNotConnected_StillAllowed()
    {
        // §30.2: connectivity is a "hold for later" concern for the dispatcher, NOT a gate rejection —
        // a queued row for a Disconnected channel must stay Pending, not become Skipped.
        var channel = new NotificationChannel { PaidUntilUtc = Now.AddDays(10), State = ChannelState.Disconnected };
        var result = Evaluate(channel: channel);
        result.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    // B3 / SPEC US-31 п. 2: the minimum-lead-time threshold is a reminder-only safety valve; a salon
    // that confirms, cancels or reschedules a visit 90 minutes out must still notify the client even
    // when MinLeadMinutes is much larger than that.
    [Theory]
    [InlineData(NotificationType.BookingConfirmed)]
    [InlineData(NotificationType.BookingCancelled)]
    [InlineData(NotificationType.BookingRescheduled)]
    [InlineData(NotificationType.StaffBookingCreated)]
    [InlineData(NotificationType.StaffBookingCancelled)]
    public void Evaluate_BelowMinimumLeadTime_NonReminderType_StillAllowed(NotificationType type)
    {
        var settings = new CompanyNotificationSettings { MinLeadMinutes = 120 };
        var result = Evaluate(type: type, settings: settings, visitStart: Now.AddMinutes(30));
        result.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    [Fact]
    public void Evaluate_BelowMinimumLeadTime_ReminderType_Blocked()
    {
        var settings = new CompanyNotificationSettings { MinLeadMinutes = 120 };
        var result = Evaluate(type: NotificationType.Reminder, settings: settings, visitStart: Now.AddMinutes(30));
        result.Outcome.Should().Be(NotificationGateOutcome.Blocked);
        result.Reason.Should().Be(NotificationReason.BelowMinimumLeadTime);
    }

    [Fact]
    public void Evaluate_NoSettingsRow_UsesDefaults()
    {
        // Missing CompanyNotificationSettings row = defaults (§23.3): all types enabled, MinLeadMinutes 120.
        var result = Evaluate(settings: null, visitStart: Now.AddDays(1));
        result.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    // ── T-24: ProviderDeliveryConsentMode (ARCHITECTURE_CYCLE5.md §52.3) ────────────────────────────

    [Fact]
    public void Evaluate_AccountsOnly_GuestRecipient_NeverBlocked()
    {
        // recipientHasProviderDeliveryConsent: null = "no account" — nobody asked a guest for this
        // consent, so AccountsOnly (the shipped default) treats them as covered by the contractual basis.
        var result = Evaluate(providerDeliveryConsentMode: ProviderDeliveryConsentMode.AccountsOnly,
            recipientHasProviderDeliveryConsent: null);
        result.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    [Fact]
    public void Evaluate_AccountsOnly_AccountHolderWithoutGrant_Blocked()
    {
        var result = Evaluate(providerDeliveryConsentMode: ProviderDeliveryConsentMode.AccountsOnly,
            recipientHasProviderDeliveryConsent: false);
        result.Outcome.Should().Be(NotificationGateOutcome.Blocked);
        result.Reason.Should().Be(NotificationReason.NoProviderDeliveryConsent);
    }

    [Fact]
    public void Evaluate_AccountsOnly_AccountHolderWithGrant_Allowed()
    {
        var result = Evaluate(providerDeliveryConsentMode: ProviderDeliveryConsentMode.AccountsOnly,
            recipientHasProviderDeliveryConsent: true);
        result.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    [Fact]
    public void Evaluate_Strict_GuestRecipient_Blocked()
    {
        // §52.4's "ужесточение": Strict has no AccountsOnly carve-out for guests — they structurally
        // cannot satisfy "has an explicit, current grant".
        var result = Evaluate(providerDeliveryConsentMode: ProviderDeliveryConsentMode.Strict,
            recipientHasProviderDeliveryConsent: null);
        result.Outcome.Should().Be(NotificationGateOutcome.Blocked);
        result.Reason.Should().Be(NotificationReason.NoProviderDeliveryConsent);
    }

    [Fact]
    public void Evaluate_Strict_AccountHolderWithGrant_Allowed()
    {
        var result = Evaluate(providerDeliveryConsentMode: ProviderDeliveryConsentMode.Strict,
            recipientHasProviderDeliveryConsent: true);
        result.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    [Fact]
    public void Evaluate_Strict_AccountHolderWithoutGrant_Blocked()
    {
        var result = Evaluate(providerDeliveryConsentMode: ProviderDeliveryConsentMode.Strict,
            recipientHasProviderDeliveryConsent: false);
        result.Outcome.Should().Be(NotificationGateOutcome.Blocked);
        result.Reason.Should().Be(NotificationReason.NoProviderDeliveryConsent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_Off_NeverBlocksRegardlessOfConsentState(bool? hasConsent)
    {
        // §52.4 step 1: Off relies on named disclosure alone — the purpose is never checked at queue time
        // under this value, for a guest OR an account holder who explicitly withheld/revoked it.
        var result = Evaluate(providerDeliveryConsentMode: ProviderDeliveryConsentMode.Off,
            recipientHasProviderDeliveryConsent: hasConsent);
        result.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    [Fact]
    public void Evaluate_DefaultMode_IsAccountsOnly()
    {
        // The Evaluate helper's own default mirrors NotificationGate.Evaluate's own default parameter
        // value — pins that the shipped default really is AccountsOnly (ARCHITECTURE_CYCLE5.md §52.3.1),
        // not merely this test file's assumption about it.
        var blockedResult = NotificationGate.Evaluate(
            AllowingPlan, NotificationType.Reminder, companyHasAssignment: true, PaidChannel(), DefaultSettings(),
            recipientOptedOut: false, Now, Now.AddDays(1), channelIsFunded: true, recipientHasProviderDeliveryConsent: false);
        blockedResult.Reason.Should().Be(NotificationReason.NoProviderDeliveryConsent);

        var allowedResult = NotificationGate.Evaluate(
            AllowingPlan, NotificationType.Reminder, companyHasAssignment: true, PaidChannel(), DefaultSettings(),
            recipientOptedOut: false, Now, Now.AddDays(1), channelIsFunded: true, recipientHasProviderDeliveryConsent: null);
        allowedResult.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    [Fact]
    public void Evaluate_RecipientOptedOut_TakesPrecedenceOverMissingProviderDeliveryConsent()
    {
        // Ordering matters for which single reason ends up on the row: opt-out (an unconditional refusal
        // channel, US-33) is checked first, same as it was checked before every other rule in this gate.
        var result = Evaluate(optedOut: true, providerDeliveryConsentMode: ProviderDeliveryConsentMode.Strict,
            recipientHasProviderDeliveryConsent: false);
        result.Reason.Should().Be(NotificationReason.RecipientOptedOut);
    }
}
