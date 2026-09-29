using FluentAssertions;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404.3, API_CONTRACT_CYCLE20.md §434.5 (US-20-03) — pure, no EF/HTTP.
/// </summary>
public class PlatformNoticeRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);

    // ── IsSuperAdminCreatable ────────────────────────────────────────────────────

    [Fact]
    public void IsSuperAdminCreatable_PhotoRemoved_False() =>
        PlatformNoticeRules.IsSuperAdminCreatable(PlatformNoticeKind.PhotoRemoved).Should().BeFalse();

    [Theory]
    [InlineData(PlatformNoticeKind.PriceChange)]
    [InlineData(PlatformNoticeKind.TermsChange)]
    [InlineData(PlatformNoticeKind.Suspension)]
    [InlineData(PlatformNoticeKind.NewProcessor)]
    [InlineData(PlatformNoticeKind.Other)]
    public void IsSuperAdminCreatable_EveryOtherKind_True(PlatformNoticeKind kind) =>
        PlatformNoticeRules.IsSuperAdminCreatable(kind).Should().BeTrue();

    // ── IsOwnerFacingAudience ────────────────────────────────────────────────────

    [Theory]
    [InlineData(NoticeAudienceType.AllOwners, true)]
    [InlineData(NoticeAudienceType.OwnersOnPlans, true)]
    [InlineData(NoticeAudienceType.BillingAccount, true)]
    [InlineData(NoticeAudienceType.AllClients, false)]
    public void IsOwnerFacingAudience_MatchesExpectation(NoticeAudienceType type, bool expected) =>
        PlatformNoticeRules.IsOwnerFacingAudience(type).Should().Be(expected);

    // ── ValidateAudienceForKind ──────────────────────────────────────────────────

    [Fact]
    public void ValidateAudienceForKind_PriceChangeToAllClients_Rejected() =>
        PlatformNoticeRules.ValidateAudienceForKind(PlatformNoticeKind.PriceChange, NoticeAudienceType.AllClients, null)
            .Should().NotBeNull();

    [Fact]
    public void ValidateAudienceForKind_PriceChangeToAllOwners_Ok() =>
        PlatformNoticeRules.ValidateAudienceForKind(PlatformNoticeKind.PriceChange, NoticeAudienceType.AllOwners, null)
            .Should().BeNull();

    [Fact]
    public void ValidateAudienceForKind_TermsOwnerToAllClients_Rejected() =>
        PlatformNoticeRules.ValidateAudienceForKind(PlatformNoticeKind.TermsChange, NoticeAudienceType.AllClients, LegalDocumentType.TermsOwner)
            .Should().NotBeNull();

    [Fact]
    public void ValidateAudienceForKind_TermsOwnerToAllOwners_Ok() =>
        PlatformNoticeRules.ValidateAudienceForKind(PlatformNoticeKind.TermsChange, NoticeAudienceType.AllOwners, LegalDocumentType.TermsOwner)
            .Should().BeNull();

    [Fact]
    public void ValidateAudienceForKind_TermsClientToAllOwners_Rejected() =>
        PlatformNoticeRules.ValidateAudienceForKind(PlatformNoticeKind.TermsChange, NoticeAudienceType.AllOwners, LegalDocumentType.TermsClient)
            .Should().NotBeNull();

    [Fact]
    public void ValidateAudienceForKind_TermsClientToAllClients_Ok() =>
        PlatformNoticeRules.ValidateAudienceForKind(PlatformNoticeKind.TermsChange, NoticeAudienceType.AllClients, LegalDocumentType.TermsClient)
            .Should().BeNull();

    [Theory]
    [InlineData(NoticeAudienceType.AllOwners)]
    [InlineData(NoticeAudienceType.OwnersOnPlans)]
    [InlineData(NoticeAudienceType.BillingAccount)]
    [InlineData(NoticeAudienceType.AllClients)]
    public void ValidateAudienceForKind_PrivacyToAnyAudience_Ok(NoticeAudienceType type) =>
        PlatformNoticeRules.ValidateAudienceForKind(PlatformNoticeKind.TermsChange, type, LegalDocumentType.Privacy)
            .Should().BeNull();

    [Fact]
    public void ValidateAudienceForKind_SuspensionToAllOwners_Rejected() =>
        PlatformNoticeRules.ValidateAudienceForKind(PlatformNoticeKind.Suspension, NoticeAudienceType.AllOwners, null)
            .Should().NotBeNull();

    [Fact]
    public void ValidateAudienceForKind_SuspensionToBillingAccount_Ok() =>
        PlatformNoticeRules.ValidateAudienceForKind(PlatformNoticeKind.Suspension, NoticeAudienceType.BillingAccount, null)
            .Should().BeNull();

    [Fact]
    public void ValidateAudienceForKind_PhotoRemoved_AlwaysRejected() =>
        PlatformNoticeRules.ValidateAudienceForKind(PlatformNoticeKind.PhotoRemoved, NoticeAudienceType.BillingAccount, null)
            .Should().NotBeNull();

    // ── ValidateParameterShape ───────────────────────────────────────────────────

    [Fact]
    public void ValidateParameterShape_PriceChangeWithTitle_Rejected() =>
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.PriceChange, hasTitle: true, hasBody: false, hasPriceChange: true, hasTermsChange: false, hasAttachment: false)
            .Should().NotBeNull();

    [Fact]
    public void ValidateParameterShape_PriceChangeWithoutParams_Rejected() =>
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.PriceChange, hasTitle: false, hasBody: false, hasPriceChange: false, hasTermsChange: false, hasAttachment: false)
            .Should().NotBeNull();

    [Fact]
    public void ValidateParameterShape_PriceChangeWellFormed_Ok() =>
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.PriceChange, hasTitle: false, hasBody: false, hasPriceChange: true, hasTermsChange: false, hasAttachment: false)
            .Should().BeNull();

    [Fact]
    public void ValidateParameterShape_TermsChangeParamsOnPriceChangeKind_Rejected() =>
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.PriceChange, hasTitle: false, hasBody: false, hasPriceChange: true, hasTermsChange: true, hasAttachment: false)
            .Should().NotBeNull();

    [Fact]
    public void ValidateParameterShape_TermsChangeWithoutAttachment_Rejected() =>
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.TermsChange, hasTitle: false, hasBody: false, hasPriceChange: false, hasTermsChange: true, hasAttachment: false)
            .Should().NotBeNull();

    [Fact]
    public void ValidateParameterShape_TermsChangeWellFormed_Ok() =>
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.TermsChange, hasTitle: false, hasBody: false, hasPriceChange: false, hasTermsChange: true, hasAttachment: true)
            .Should().BeNull();

    [Fact]
    public void ValidateParameterShape_SuspensionWithAttachment_Rejected() =>
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.Suspension, hasTitle: true, hasBody: true, hasPriceChange: false, hasTermsChange: false, hasAttachment: true)
            .Should().NotBeNull();

    [Fact]
    public void ValidateParameterShape_SuspensionWithoutTitleOrBody_Rejected() =>
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.Suspension, hasTitle: false, hasBody: false, hasPriceChange: false, hasTermsChange: false, hasAttachment: false)
            .Should().NotBeNull();

    [Fact]
    public void ValidateParameterShape_SuspensionWellFormed_Ok() =>
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.Suspension, hasTitle: true, hasBody: true, hasPriceChange: false, hasTermsChange: false, hasAttachment: false)
            .Should().BeNull();

    [Fact]
    public void ValidateParameterShape_OtherWithAttachment_Ok() =>
        // §434.5's own row: attachment is "необязательно" for Other, only forbidden for Suspension.
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.Other, hasTitle: true, hasBody: true, hasPriceChange: false, hasTermsChange: false, hasAttachment: true)
            .Should().BeNull();

    [Fact]
    public void ValidateParameterShape_PhotoRemoved_AlwaysRejected() =>
        PlatformNoticeRules.ValidateParameterShape(PlatformNoticeKind.PhotoRemoved, hasTitle: true, hasBody: true, hasPriceChange: false, hasTermsChange: false, hasAttachment: false)
            .Should().NotBeNull();

    // ── ValidateEffectiveFrom ────────────────────────────────────────────────────

    [Fact]
    public void ValidateEffectiveFrom_PriceChange_Null_Rejected() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.PriceChange, NoticeAudienceType.AllOwners, null, Today)
            .Should().NotBeNull();

    [Fact]
    public void ValidateEffectiveFrom_PriceChange_29Days_Rejected() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.PriceChange, NoticeAudienceType.AllOwners, Today.AddDays(29), Today)
            .Should().NotBeNull();

    [Fact]
    public void ValidateEffectiveFrom_PriceChange_Exactly30Days_Ok() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.PriceChange, NoticeAudienceType.AllOwners, Today.AddDays(30), Today)
            .Should().BeNull();

    [Fact]
    public void ValidateEffectiveFrom_TermsChange_OwnerFacing_14Days_Rejected() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.TermsChange, NoticeAudienceType.AllOwners, Today.AddDays(14), Today)
            .Should().NotBeNull();

    [Fact]
    public void ValidateEffectiveFrom_TermsChange_OwnerFacing_Exactly15Days_Ok() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.TermsChange, NoticeAudienceType.AllOwners, Today.AddDays(15), Today)
            .Should().BeNull();

    [Fact]
    public void ValidateEffectiveFrom_TermsChange_AllClients_9Days_Rejected() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.TermsChange, NoticeAudienceType.AllClients, Today.AddDays(9), Today)
            .Should().NotBeNull();

    [Fact]
    public void ValidateEffectiveFrom_TermsChange_AllClients_Exactly10Days_Ok() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.TermsChange, NoticeAudienceType.AllClients, Today.AddDays(10), Today)
            .Should().BeNull();

    [Fact]
    public void ValidateEffectiveFrom_NewProcessor_OwnerFacing_Null_Rejected() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.NewProcessor, NoticeAudienceType.AllOwners, null, Today)
            .Should().NotBeNull();

    [Fact]
    public void ValidateEffectiveFrom_NewProcessor_OwnerFacing_14Days_Rejected() =>
        // D3 п. 11.9.5 / legal-counsel addendum (LEGAL_REVIEW_CYCLE20.md §11 ⚠️) — the 15-day floor.
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.NewProcessor, NoticeAudienceType.BillingAccount, Today.AddDays(14), Today)
            .Should().NotBeNull();

    [Fact]
    public void ValidateEffectiveFrom_NewProcessor_OwnerFacing_Exactly15Days_Ok() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.NewProcessor, NoticeAudienceType.OwnersOnPlans, Today.AddDays(15), Today)
            .Should().BeNull();

    [Fact]
    public void ValidateEffectiveFrom_NewProcessor_AllClients_Null_Rejected() =>
        // Date is still mandatory for AllClients — only the minimum lead time is not checked.
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.NewProcessor, NoticeAudienceType.AllClients, null, Today)
            .Should().NotBeNull();

    [Fact]
    public void ValidateEffectiveFrom_NewProcessor_AllClients_Tomorrow_Ok() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.NewProcessor, NoticeAudienceType.AllClients, Today.AddDays(1), Today)
            .Should().BeNull();

    [Fact]
    public void ValidateEffectiveFrom_Suspension_Null_Ok() =>
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.Suspension, NoticeAudienceType.BillingAccount, null, Today)
            .Should().BeNull();

    [Fact]
    public void ValidateEffectiveFrom_Other_PastDate_Ok() =>
        // Other's own row: "необязательна, срок не проверяется" — even a past date is not this
        // function's business to reject.
        PlatformNoticeRules.ValidateEffectiveFrom(PlatformNoticeKind.Other, NoticeAudienceType.AllClients, Today.AddDays(-5), Today)
            .Should().BeNull();

    // ── ValidateAudienceShape ────────────────────────────────────────────────────

    [Fact]
    public void ValidateAudienceShape_OwnersOnPlansWithoutPlanIds_Rejected() =>
        PlatformNoticeRules.ValidateAudienceShape(NoticeAudienceType.OwnersOnPlans, null, null).Should().NotBeNull();

    [Fact]
    public void ValidateAudienceShape_OwnersOnPlansWithEmptyPlanIds_Rejected() =>
        PlatformNoticeRules.ValidateAudienceShape(NoticeAudienceType.OwnersOnPlans, [], null).Should().NotBeNull();

    [Fact]
    public void ValidateAudienceShape_OwnersOnPlansWithPlanIds_Ok() =>
        PlatformNoticeRules.ValidateAudienceShape(NoticeAudienceType.OwnersOnPlans, [Guid.NewGuid()], null).Should().BeNull();

    [Fact]
    public void ValidateAudienceShape_AllOwnersWithPlanIds_Rejected() =>
        PlatformNoticeRules.ValidateAudienceShape(NoticeAudienceType.AllOwners, [Guid.NewGuid()], null).Should().NotBeNull();

    [Fact]
    public void ValidateAudienceShape_BillingAccountWithoutId_Rejected() =>
        PlatformNoticeRules.ValidateAudienceShape(NoticeAudienceType.BillingAccount, null, null).Should().NotBeNull();

    [Fact]
    public void ValidateAudienceShape_BillingAccountWithId_Ok() =>
        PlatformNoticeRules.ValidateAudienceShape(NoticeAudienceType.BillingAccount, null, Guid.NewGuid()).Should().BeNull();

    [Fact]
    public void ValidateAudienceShape_AllOwnersWithBillingAccountId_Rejected() =>
        PlatformNoticeRules.ValidateAudienceShape(NoticeAudienceType.AllOwners, null, Guid.NewGuid()).Should().NotBeNull();

    // ── ValidateLinkUrl ──────────────────────────────────────────────────────────

    [Fact]
    public void ValidateLinkUrl_Null_Ok() => PlatformNoticeRules.ValidateLinkUrl(null).Should().BeNull();

    [Fact]
    public void ValidateLinkUrl_RelativePath_Ok() => PlatformNoticeRules.ValidateLinkUrl("/legal/terms-owner").Should().BeNull();

    [Fact]
    public void ValidateLinkUrl_AbsoluteUrl_Rejected() => PlatformNoticeRules.ValidateLinkUrl("https://evil.example/x").Should().NotBeNull();

    [Fact]
    public void ValidateLinkUrl_ProtocolRelative_Rejected() => PlatformNoticeRules.ValidateLinkUrl("//evil.example/x").Should().NotBeNull();

    [Fact]
    public void ValidateLinkUrl_NoLeadingSlash_Rejected() => PlatformNoticeRules.ValidateLinkUrl("legal/terms-owner").Should().NotBeNull();

    [Fact]
    public void ValidateLinkUrl_TooLong_Rejected() => PlatformNoticeRules.ValidateLinkUrl("/" + new string('a', 500)).Should().NotBeNull();

    // ── ComputeVisibleUntilUtc ───────────────────────────────────────────────────

    [Fact]
    public void ComputeVisibleUntilUtc_NoEffectiveFrom_PublishPlus365Wins()
    {
        var publishedAt = new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc);
        PlatformNoticeRules.ComputeVisibleUntilUtc(publishedAt, null).Should().Be(publishedAt.AddDays(365));
    }

    [Fact]
    public void ComputeVisibleUntilUtc_FarFutureEffectiveFrom_EffectivePlus30Wins()
    {
        var publishedAt = new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc);
        var effectiveFrom = new DateOnly(2028, 1, 1); // far enough that +30d beats published+365d
        var expected = DateTime.SpecifyKind(effectiveFrom.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc).AddDays(30);
        PlatformNoticeRules.ComputeVisibleUntilUtc(publishedAt, effectiveFrom).Should().Be(expected);
    }

    [Fact]
    public void ComputeVisibleUntilUtc_NearEffectiveFrom_PublishPlus365StillWins()
    {
        var publishedAt = new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc);
        var effectiveFrom = new DateOnly(2026, 10, 29); // +30d from this is far short of published+365d
        PlatformNoticeRules.ComputeVisibleUntilUtc(publishedAt, effectiveFrom).Should().Be(publishedAt.AddDays(365));
    }
}
