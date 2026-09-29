using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// CY20-U-02 (ARCHITECTURE_CYCLE20.md §403.2, §417, US-20-02) — pure, no EF/HTTP.
/// </summary>
public class ManualPlanAssignmentPolicyTests
{
    private static readonly Guid CurrentPlan = Guid.NewGuid();
    private static readonly Guid OtherHiddenPlan = Guid.NewGuid();

    [Fact]
    public void RequiresReason_HiddenPlanDifferentFromCurrent_True() =>
        ManualPlanAssignmentPolicy.RequiresReason(CurrentPlan, OtherHiddenPlan, targetIsPublic: false).Should().BeTrue();

    [Fact]
    public void RequiresReason_HiddenPlanSameAsCurrent_False() =>
        // П3: extending/renewing the SAME hidden plan the account already has is not "assigning a
        // hidden tariff" — it's a renewal, no reason required.
        ManualPlanAssignmentPolicy.RequiresReason(CurrentPlan, CurrentPlan, targetIsPublic: false).Should().BeFalse();

    [Fact]
    public void RequiresReason_PublicPlan_False() =>
        ManualPlanAssignmentPolicy.RequiresReason(CurrentPlan, OtherHiddenPlan, targetIsPublic: true).Should().BeFalse();

    [Fact]
    public void RequiresReason_NoTargetPlan_False() =>
        ManualPlanAssignmentPolicy.RequiresReason(CurrentPlan, targetPlanId: null, targetIsPublic: false).Should().BeFalse();

    [Fact]
    public void Validate_RequiredButNoReasonCode_ReturnsError() =>
        ManualPlanAssignmentPolicy.Validate(reasonCode: null, reasonDetails: null, required: true)
            .Should().NotBeNull();

    [Fact]
    public void Validate_NotRequiredAndNoReasonCode_Ok() =>
        ManualPlanAssignmentPolicy.Validate(reasonCode: null, reasonDetails: null, required: false)
            .Should().BeNull();

    [Fact]
    public void Validate_PublicPlan_NoReasonNeeded_Ok() =>
        // Same fact as above, phrased against the API contract's own example (public tariff, no reason).
        ManualPlanAssignmentPolicy.Validate(reasonCode: null, reasonDetails: null, required: false)
            .Should().BeNull();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_TrialReissue_AlwaysRejected(bool required) =>
        // Р6/§433.1: TrialReissue is never accepted through this endpoint, required or not — it only
        // exists so history can show the same title the regrant path wrote.
        ManualPlanAssignmentPolicy.Validate(SubscriptionChangeReason.TrialReissue, "любой текст", required)
            .Should().NotBeNull();

    [Fact]
    public void Validate_OperatorErrorCorrectionWithoutDetails_ReturnsError() =>
        ManualPlanAssignmentPolicy.Validate(SubscriptionChangeReason.OperatorErrorCorrection, "", required: true)
            .Should().NotBeNull();

    [Fact]
    public void Validate_OperatorErrorCorrectionWithDetails_Ok() =>
        ManualPlanAssignmentPolicy.Validate(SubscriptionChangeReason.OperatorErrorCorrection, "оплата от 12.09 не отмечена", required: true)
            .Should().BeNull();

    [Fact]
    public void Validate_DetailsTooLong_ReturnsError() =>
        ManualPlanAssignmentPolicy.Validate(
                SubscriptionChangeReason.OperatorErrorCorrection, new string('a', 1001), required: true)
            .Should().NotBeNull();

    [Fact]
    public void Validate_DetailsAtMaxLength_Ok() =>
        ManualPlanAssignmentPolicy.Validate(
                SubscriptionChangeReason.OperatorErrorCorrection, new string('a', 1000), required: true)
            .Should().BeNull();

    // Code-review finding (cycle 20): Program.cs's JsonStringEnumConverter accepts a raw out-of-range
    // integer for any enum by default, so `"reasonCode": 99` used to bind straight through model binding
    // as a technically-non-null `SubscriptionChangeReason` and reach here undetected — §433.1 requires 400
    // for an unknown reasonCode, not a row silently written with a meaningless numeric code.
    [Fact]
    public void Validate_UndefinedReasonCode_ReturnsError() =>
        ManualPlanAssignmentPolicy.Validate((SubscriptionChangeReason)99, "любой текст", required: true)
            .Should().NotBeNull();

    [Fact]
    public void Validate_UndefinedReasonCode_ReturnsErrorEvenWhenNotRequired() =>
        // §433.1's own ordering: a volunteered reason is validated regardless of `required`.
        ManualPlanAssignmentPolicy.Validate((SubscriptionChangeReason)99, "любой текст", required: false)
            .Should().NotBeNull();
}
