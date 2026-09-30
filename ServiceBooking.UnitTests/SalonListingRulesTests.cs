using FluentAssertions;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Shops;
using Xunit;

namespace ServiceBooking.UnitTests;

public class SalonListingRulesTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void Visible_OnlyWhenActive_AllowedByPlan_AndOwnerOptedIn(bool active, bool plan, bool owner, bool visible)
    {
        var r = SalonListingRules.Evaluate(new SalonListingInput(active, plan, owner));
        r.Visible.Should().Be(visible);
        r.Visible.Should().Be(r.Checklist.All(c => c.Done));
    }

    [Fact]
    public void Checklist_ListsBlockedAndPlanItemsOnlyWhenTheyApply_InOrder()
    {
        var all = SalonListingRules.Evaluate(new SalonListingInput(false, false, false));
        all.Checklist.Select(c => c.Code).Should().Equal(
            CatalogListingCheckCode.SalonBlocked, CatalogListingCheckCode.NotAllowedByPlan, CatalogListingCheckCode.HiddenByOwner);

        var ok = SalonListingRules.Evaluate(new SalonListingInput(true, true, true));
        ok.Checklist.Should().ContainSingle().Which.Code.Should().Be(CatalogListingCheckCode.HiddenByOwner);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HiddenByOwnerText_IsTheSame_WhateverTheSwitch(bool owner)
    {
        var item = SalonListingRules.Evaluate(new SalonListingInput(true, true, owner)).Checklist.Single();
        item.Text.Should().Be("Показ включен в настройках");
        item.Done.Should().Be(owner);
    }
}
