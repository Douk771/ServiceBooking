using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Showcase;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE28.md §574.2, API_CONTRACT_CYCLE28.md §595 — the pure mixing rules and their exact texts.</summary>
public class ShowcaseMixingGuardTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void CheckMember_SameMark_Allowed(bool company, bool user) =>
        ShowcaseMixingGuard.CheckMember(company, user).Should().BeNull();

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CheckMember_DifferentMarks_Refused_WithTheContractText(bool company, bool user) =>
        ShowcaseMixingGuard.CheckMember(company, user).Should().Be("Витринную компанию и настоящие учётные записи смешивать нельзя.");

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, true, null)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void CheckTransfer_ConsistentMarks_Allowed(bool company, bool account, bool? responsible) =>
        ShowcaseMixingGuard.CheckTransfer(company, account, responsible).Should().BeNull();

    [Theory]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public void CheckTransfer_AnyDisagreement_Refused_WithTheContractText(bool company, bool account, bool? responsible) =>
        ShowcaseMixingGuard.CheckTransfer(company, account, responsible)
            .Should().Be("Витринную компанию нельзя перенести в настоящий аккаунт, а настоящую — в витринный.");

    [Fact]
    public void CheckServicePlan_TheHiddenDemoTariffOfOrders_IsRestricted_ToShowcaseAccountsToo()
    {
        ShowcaseCatalog.IsServicePlan(ShowcaseCatalog.ShowcasePlanId).Should().BeTrue();
        ShowcaseCatalog.IsServicePlan(ShowcaseCatalog.OrdersShowcasePlanId).Should().BeTrue();
        ShowcaseCatalog.IsServicePlan(Guid.NewGuid()).Should().BeFalse();
        ShowcaseCatalog.OrdersShowcasePlanId.Should().Be(Guid.Parse("5a1e0c35-0000-4000-8000-000000000901"), "a literal that never changes");
        ShowcaseMixingGuard.CheckServicePlan(ShowcaseCatalog.OrdersShowcasePlanId, accountIsShowcase: true).Should().BeNull();
        ShowcaseMixingGuard.CheckServicePlan(ShowcaseCatalog.OrdersShowcasePlanId, accountIsShowcase: false)
            .Should().Be(ShowcaseMixingGuard.ServicePlanText);
    }

    [Fact]
    public void CheckServicePlan_OnlyTheShowcasePlan_IsRestricted_ToShowcaseAccounts()
    {
        ShowcaseMixingGuard.CheckServicePlan(ShowcaseCatalog.ShowcasePlanId, accountIsShowcase: true).Should().BeNull();
        ShowcaseMixingGuard.CheckServicePlan(ShowcaseCatalog.ShowcasePlanId, accountIsShowcase: false)
            .Should().Be("Служебный тариф витрины нельзя назначить настоящему аккаунту.");
        ShowcaseMixingGuard.CheckServicePlan(Guid.NewGuid(), accountIsShowcase: false).Should().BeNull();
        ShowcaseMixingGuard.CheckServicePlan(Guid.NewGuid(), accountIsShowcase: true).Should().BeNull();
    }

    [Theory]
    [InlineData("primer-salon", true)]
    [InlineData("PRIMER-salon", true)]
    [InlineData("  primer-x", true)]
    [InlineData("primer-", true)]
    [InlineData("primer", false)]
    [InlineData("my-primer-salon", false)]
    [InlineData("primerka", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsReservedSlug_MatchesThePrefixOnly(string? slug, bool expected) =>
        ShowcaseMixingGuard.IsReservedSlug(slug).Should().Be(expected);

    [Fact]
    public void ReservedSlugText_IsTheContractText() =>
        ShowcaseMixingGuard.ReservedSlugText.Should().Be("Адрес, начинающийся с «primer-», зарезервирован. Выберите другой.");

    [Fact]
    public void LoginOutcome_ShowcaseAccount_AnswersLikeAWrongPassword()
    {
        var showcase = LoginOutcomeMapper.ToErrorResponse(LoginOutcome.ShowcaseAccount);
        var wrongPassword = LoginOutcomeMapper.ToErrorResponse(LoginOutcome.WrongPassword);

        showcase.Should().BeOfType(wrongPassword.GetType());
        ((Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult)showcase).Value.Should().Be("Invalid credentials");
    }
}
