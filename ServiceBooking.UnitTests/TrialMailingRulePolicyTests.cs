using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE28.md §573.3 — when a trial plan without an Included mailing rule is a misconfiguration and when it is intended.</summary>
public class TrialMailingRulePolicyTests
{
    [Fact]
    public void TrialPlanDesignedWithMailings_MissingRule_IsAMisconfiguration() =>
        TrialMailingRulePolicy.IsMisconfiguration(new SubscriptionPlanConfig { AllowNotificationChannel = true }).Should().BeTrue();

    [Fact]
    public void TrialPlanWithoutMailings_MissingRule_IsIntended_NoSignal() =>
        TrialMailingRulePolicy.IsMisconfiguration(new SubscriptionPlanConfig { AllowNotificationChannel = false }).Should().BeFalse();
}
