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
    private static readonly EffectivePlan AllowingPlan = EffectivePlan.Free with { AllowNotificationChannel = true };
    private static readonly EffectivePlan DenyingPlan = EffectivePlan.Free with { AllowNotificationChannel = false };

    private static NotificationChannel PaidChannel() => new() { PaidUntilUtc = Now.AddDays(10) };
    private static CompanyNotificationSettings DefaultSettings() => new();

    private static NotificationGateResult Evaluate(
        EffectivePlan? plan = null, NotificationType type = NotificationType.Reminder,
        bool hasAssignment = true, NotificationChannel? channel = null,
        CompanyNotificationSettings? settings = null, bool optedOut = false,
        DateTime? visitStart = null) =>
        NotificationGate.Evaluate(
            plan ?? AllowingPlan, type, hasAssignment, channel ?? PaidChannel(), settings ?? DefaultSettings(),
            optedOut, Now, visitStart ?? Now.AddDays(1));

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
            DefaultSettings(), recipientOptedOut: false, Now, Now.AddDays(1));
        result.Reason.Should().Be(NotificationReason.NoUsableChannel);
    }

    [Fact]
    public void Evaluate_ChannelNotPaid_Blocked()
    {
        var result = Evaluate(channel: new NotificationChannel { PaidUntilUtc = null });
        result.Reason.Should().Be(NotificationReason.NoUsableChannel);
    }

    [Fact]
    public void Evaluate_ChannelPaymentExpired_Blocked()
    {
        var result = Evaluate(channel: new NotificationChannel { PaidUntilUtc = Now.AddDays(-1) });
        result.Reason.Should().Be(NotificationReason.NoUsableChannel);
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

    [Fact]
    public void Evaluate_NoSettingsRow_UsesDefaults()
    {
        // Missing CompanyNotificationSettings row = defaults (§23.3): all types enabled, MinLeadMinutes 120.
        var result = Evaluate(settings: null, visitStart: Now.AddDays(1));
        result.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }
}
