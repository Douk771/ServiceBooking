using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

public class SubscriptionDiagnosticsTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SubscriptionPlanConfig ActivePlan(bool allowOnlineBooking = true) => new()
    {
        Id = Guid.NewGuid(), Name = "Стандарт", IsActive = true, AllowOnlineBooking = allowOnlineBooking,
    };

    [Fact]
    public void Describe_NoSubscription_ReturnsNoSubscription()
    {
        var (status, text) = SubscriptionDiagnostics.Describe(null, Now);
        status.Should().Be(SubscriptionStatus.NoSubscription);
        text.Should().Be("Тариф не назначен");
    }

    [Fact]
    public void Describe_Deactivated_ReturnsDeactivatedRegardlessOfDate()
    {
        var sub = new AccountSubscription { IsActive = false, PlanConfig = ActivePlan(), PaidUntil = Now.AddYears(1) };
        SubscriptionDiagnostics.Describe(sub, Now).Status.Should().Be(SubscriptionStatus.Deactivated);
    }

    [Fact]
    public void Describe_PlanRetired_ReturnsPlanRetired()
    {
        var sub = new AccountSubscription { IsActive = true, PlanConfig = new SubscriptionPlanConfig { IsActive = false, Name = "Старый" } };
        SubscriptionDiagnostics.Describe(sub, Now).Status.Should().Be(SubscriptionStatus.PlanRetired);
    }

    [Fact]
    public void Describe_Expired_ReturnsExpired()
    {
        var sub = new AccountSubscription { IsActive = true, PlanConfig = ActivePlan(), PaidUntil = Now.AddDays(-1) };
        SubscriptionDiagnostics.Describe(sub, Now).Status.Should().Be(SubscriptionStatus.Expired);
    }

    [Fact]
    public void Describe_Active_ReturnsActive()
    {
        var sub = new AccountSubscription { IsActive = true, PlanConfig = ActivePlan(), PaidUntil = Now.AddDays(1) };
        SubscriptionDiagnostics.Describe(sub, Now).Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void BlockingReasonFor_NoSubscription_ReturnsNoSubscription() =>
        SubscriptionDiagnostics.BlockingReasonFor(null, EffectivePlan.Free, true, Now)
            .Should().Be(PlanNotAppliedReason.NoSubscription);

    [Fact]
    public void BlockingReasonFor_PlanDisallowsOnlineBooking_DistinctFromOwnerDisabled()
    {
        var sub = new AccountSubscription { IsActive = true, PlanConfig = ActivePlan(allowOnlineBooking: false), PaidUntil = Now.AddDays(1) };
        var effective = SubscriptionResolver.Resolve(sub, Now);
        SubscriptionDiagnostics.BlockingReasonFor(sub, effective, allowSelfBooking: true, Now)
            .Should().Be(PlanNotAppliedReason.PlanDisallowsOnlineBooking);
    }

    [Fact]
    public void BlockingReasonFor_OwnerDisabledSelfBooking_ReturnsSelfBookingDisabledByOwner()
    {
        var sub = new AccountSubscription { IsActive = true, PlanConfig = ActivePlan(), PaidUntil = Now.AddDays(1) };
        var effective = SubscriptionResolver.Resolve(sub, Now);
        SubscriptionDiagnostics.BlockingReasonFor(sub, effective, allowSelfBooking: false, Now)
            .Should().Be(PlanNotAppliedReason.SelfBookingDisabledByOwner);
    }

    [Fact]
    public void BlockingReasonFor_EverythingOk_ReturnsNone()
    {
        var sub = new AccountSubscription { IsActive = true, PlanConfig = ActivePlan(), PaidUntil = Now.AddDays(1) };
        var effective = SubscriptionResolver.Resolve(sub, Now);
        SubscriptionDiagnostics.BlockingReasonFor(sub, effective, allowSelfBooking: true, Now)
            .Should().Be(PlanNotAppliedReason.None);
    }
}
