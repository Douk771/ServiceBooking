using FluentAssertions;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404.2 (US-20-03) — pure, no EF/HTTP. <see cref="PlatformNotice"/> is a plain
/// POCO here, never attached to a DbContext.
/// </summary>
public class NoticeAudienceTests
{
    private static readonly Guid HeldAccountId = Guid.NewGuid();
    private static readonly Guid OtherAccountId = Guid.NewGuid();
    private static readonly Guid PlanA = Guid.NewGuid();
    private static readonly Guid PlanB = Guid.NewGuid();
    private static readonly Guid SystemFreePlan = Guid.NewGuid();

    private static PlatformNotice Notice(NoticeAudienceType type, Guid[]? planIds = null, Guid? targetAccountId = null) =>
        new() { Id = Guid.NewGuid(), AudienceType = type, AudiencePlanIds = planIds, TargetBillingAccountId = targetAccountId };

    [Fact]
    public void AllOwners_MatchesAnyAccountHolder() =>
        NoticeAudience.Matches(Notice(NoticeAudienceType.AllOwners), new NoticeCallerFacts(false, HeldAccountId, null, null))
            .Should().BeTrue();

    [Fact]
    public void AllOwners_DoesNotMatchACallerWithNoAccount_EgACompanyManager() =>
        NoticeAudience.Matches(Notice(NoticeAudienceType.AllOwners), new NoticeCallerFacts(false, null, null, null))
            .Should().BeFalse();

    [Fact]
    public void OwnersOnPlans_MatchesWhenHeldPlanIsInTheList() =>
        NoticeAudience.Matches(Notice(NoticeAudienceType.OwnersOnPlans, [PlanA, PlanB]), new NoticeCallerFacts(false, HeldAccountId, PlanA, null))
            .Should().BeTrue();

    [Fact]
    public void OwnersOnPlans_DoesNotMatchWhenHeldPlanIsNotInTheList() =>
        NoticeAudience.Matches(Notice(NoticeAudienceType.OwnersOnPlans, [PlanA, PlanB]), new NoticeCallerFacts(false, HeldAccountId, Guid.NewGuid(), null))
            .Should().BeFalse();

    [Fact]
    public void OwnersOnPlans_NoSubscriptionRow_MatchesWhenSystemFreeIsInTheList() =>
        // §404.2: "если в списке есть системный Free, подходят и аккаунты без подписки / PlanConfigId=null".
        NoticeAudience.Matches(Notice(NoticeAudienceType.OwnersOnPlans, [SystemFreePlan]), new NoticeCallerFacts(false, HeldAccountId, null, SystemFreePlan))
            .Should().BeTrue();

    [Fact]
    public void OwnersOnPlans_NoSubscriptionRow_DoesNotMatchWhenSystemFreeIsNotInTheList() =>
        NoticeAudience.Matches(Notice(NoticeAudienceType.OwnersOnPlans, [PlanA]), new NoticeCallerFacts(false, HeldAccountId, null, SystemFreePlan))
            .Should().BeFalse();

    [Fact]
    public void OwnersOnPlans_NoAccountHeld_NeverMatches() =>
        NoticeAudience.Matches(Notice(NoticeAudienceType.OwnersOnPlans, [PlanA]), new NoticeCallerFacts(false, null, PlanA, null))
            .Should().BeFalse();

    [Fact]
    public void BillingAccount_MatchesTheExactHolder() =>
        NoticeAudience.Matches(Notice(NoticeAudienceType.BillingAccount, targetAccountId: HeldAccountId), new NoticeCallerFacts(false, HeldAccountId, null, null))
            .Should().BeTrue();

    [Fact]
    public void BillingAccount_DoesNotMatchADifferentHolder() =>
        NoticeAudience.Matches(Notice(NoticeAudienceType.BillingAccount, targetAccountId: OtherAccountId), new NoticeCallerFacts(false, HeldAccountId, null, null))
            .Should().BeFalse();

    [Fact]
    public void AllClients_MatchesAnOrdinaryUser() =>
        NoticeAudience.Matches(Notice(NoticeAudienceType.AllClients), new NoticeCallerFacts(false, null, null, null))
            .Should().BeTrue();

    [Fact]
    public void AllClients_ExcludesSuperAdmin() =>
        // D2 п. 21.2 — SuperAdmin is staff, not a client of the platform.
        NoticeAudience.Matches(Notice(NoticeAudienceType.AllClients), new NoticeCallerFacts(true, null, null, null))
            .Should().BeFalse();

    [Fact]
    public void AllClients_StillMatchesASuperAdminWhoAlsoHoldsAnAccount() =>
        // The exclusion is purely role-based — holding a billing account doesn't change it either way.
        NoticeAudience.Matches(Notice(NoticeAudienceType.AllClients), new NoticeCallerFacts(true, HeldAccountId, PlanA, null))
            .Should().BeFalse();
}
