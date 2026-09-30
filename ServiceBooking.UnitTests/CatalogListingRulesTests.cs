using FluentAssertions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §505.1 — the five conditions of visibility in the goods catalog.</summary>
public class CatalogListingRulesTests
{
    private static readonly CatalogListingInput Ok = new(true, true, true, true, true);

    [Fact]
    public void AllConditionsMet_IsVisible_WithoutBlockedOrPlanItems()
    {
        var r = CatalogListingRules.Evaluate(Ok);
        r.Visible.Should().BeTrue();
        r.Checklist.Select(c => c.Code).Should().Equal(
            CatalogListingCheckCode.NoWorkingHours, CatalogListingCheckCode.NoPublishedProducts, CatalogListingCheckCode.HiddenByOwner);
        r.Checklist.Should().OnlyContain(c => c.Done);
    }

    [Theory]
    [InlineData(false, true, true, true, true, CatalogListingCheckCode.ShopBlocked, "Магазин заблокирован администратором")]
    [InlineData(true, false, true, true, true, CatalogListingCheckCode.NoWorkingHours, "Задайте часы работы")]
    [InlineData(true, true, false, true, true, CatalogListingCheckCode.NoPublishedProducts, "Опубликуйте хотя бы один товар")]
    [InlineData(true, true, true, false, true, CatalogListingCheckCode.NotAllowedByPlan, "Показ в каталоге не входит в ваш тариф")]
    [InlineData(true, true, true, true, false, CatalogListingCheckCode.HiddenByOwner, "Показ выключен в настройках")]
    public void EachConditionAlone_HidesTheShop_AndIsInTheChecklist(
        bool active, bool hours, bool product, bool plan, bool owner, CatalogListingCheckCode code, string text)
    {
        var r = CatalogListingRules.Evaluate(new CatalogListingInput(active, hours, product, plan, owner));
        r.Visible.Should().BeFalse();
        var item = r.Checklist.Single(c => c.Code == code);
        item.Done.Should().BeFalse();
        item.Text.Should().Be(text);
        r.Checklist.Where(c => c.Code != code).Should().OnlyContain(c => c.Done);
    }

    [Fact]
    public void VisibleIffEveryItemDone()
    {
        foreach (var bits in Enumerable.Range(0, 32))
        {
            var r = CatalogListingRules.Evaluate(new CatalogListingInput(
                (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0));
            r.Visible.Should().Be(r.Checklist.All(c => c.Done), $"bits {bits}");
            r.Visible.Should().Be(bits == 31, $"bits {bits}");
        }
    }

    [Fact]
    public void StatusTexts() =>
        (CatalogListingRules.StatusText(true), CatalogListingRules.StatusText(false))
            .Should().Be(("Магазин виден в каталоге goods.ezbook.ru", "Магазина сейчас нет в каталоге"));
}
