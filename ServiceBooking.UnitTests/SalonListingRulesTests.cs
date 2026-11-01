using FluentAssertions;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Shops;
using Xunit;

namespace ServiceBooking.UnitTests;

/// <summary>CY49-B1 — truth table 2^5 for the salon catalog rule.</summary>
public class SalonListingRulesTests
{
    public static IEnumerable<object[]> AllCombinations()
    {
        for (var mask = 0; mask < 32; mask++)
            yield return [(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, (mask & 16) != 0];
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Visible_IffAllFiveConditionsHold(bool active, bool service, bool provider, bool plan, bool owner)
    {
        var r = SalonListingRules.Evaluate(new SalonListingInput(active, service, provider, plan, owner));
        r.Visible.Should().Be(active && service && provider && plan && owner);
        r.Visible.Should().Be(r.Checklist.All(c => c.Done));

        var codes = r.Checklist.Select(c => c.Code).ToList();
        var expected = new List<CatalogListingCheckCode>();
        if (!active) expected.Add(CatalogListingCheckCode.SalonBlocked);
        expected.Add(CatalogListingCheckCode.NoActiveServices);
        expected.Add(CatalogListingCheckCode.NoServiceProviders);
        if (!plan) expected.Add(CatalogListingCheckCode.NotAllowedByPlan);
        expected.Add(CatalogListingCheckCode.HiddenByOwner);
        codes.Should().Equal(expected);

        r.Checklist.Single(c => c.Code == CatalogListingCheckCode.NoActiveServices).Done.Should().Be(service);
        r.Checklist.Single(c => c.Code == CatalogListingCheckCode.NoServiceProviders).Done.Should().Be(provider);
        r.Checklist.Single(c => c.Code == CatalogListingCheckCode.HiddenByOwner).Done.Should().Be(owner);
    }

    [Fact]
    public void Texts_AreTheConstants()
    {
        var items = SalonListingRules.Evaluate(new SalonListingInput(false, false, false, false, true)).Checklist;
        items.Single(c => c.Code == CatalogListingCheckCode.SalonBlocked).Text.Should().Be("Салон заблокирован администратором");
        items.Single(c => c.Code == CatalogListingCheckCode.NoActiveServices).Text.Should().Be("Есть услуги");
        items.Single(c => c.Code == CatalogListingCheckCode.NoServiceProviders).Text.Should().Be("Есть сотрудники, оказывающие услуги");
        items.Single(c => c.Code == CatalogListingCheckCode.NotAllowedByPlan).Text.Should().Be(CatalogListingRules.NotAllowedByPlanText);
        items.Single(c => c.Code == CatalogListingCheckCode.HiddenByOwner).Text.Should().Be(CatalogListingRules.HiddenByOwnerText);
        SalonListingRules.ConditionsText.Should().Be("Салон появится в каталоге ezbook.ru, только когда выполнены все условия ниже.");
    }

    [Fact]
    public void NewCodes_AreAppendedAtTheEndOfTheEnum()
    {
        ((int)CatalogListingCheckCode.SalonBlocked).Should().Be(5);
        ((int)CatalogListingCheckCode.NoActiveServices).Should().Be(6);
        ((int)CatalogListingCheckCode.NoServiceProviders).Should().Be(7);
    }

    [Theory]
    [InlineData(true, "Салон виден в каталоге ezbook.ru")]
    [InlineData(false, "Салона сейчас нет в каталоге")]
    public void StatusText_MatchesVisibility(bool visible, string text) =>
        SalonListingRules.StatusText(visible).Should().Be(text);
}
