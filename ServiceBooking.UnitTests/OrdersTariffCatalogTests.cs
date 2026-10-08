using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE37.md §37.10 — T37-B01: the paid "Заказы" grid, T37-B02: the free-tariff alignment.</summary>
public class OrdersTariffCatalogTests
{
    [Fact]
    public void Grid_HasTheApprovedPricesAndLimits()
    {
        OrdersTariffCatalog.Lavka.Should().Match<OrdersTariff>(t =>
            t.PricePerMonth == 690 && t.MaxCompanies == 1 && t.MaxEmployees == 5 && t.MaxProductsPerShop == 300 && t.MaxOrdersPerMonth == 1500);
        OrdersTariffCatalog.Shop.Should().Match<OrdersTariff>(t =>
            t.PricePerMonth == 1490 && t.MaxCompanies == 3 && t.MaxEmployees == 15 && t.MaxProductsPerShop == 1000 && t.MaxOrdersPerMonth == 5000);
        OrdersTariffCatalog.Chain.Should().Match<OrdersTariff>(t =>
            t.PricePerMonth == 2990 && t.MaxCompanies == null && t.MaxEmployees == null && t.MaxProductsPerShop == 1000 && t.MaxOrdersPerMonth == null);
        OrdersTariffCatalog.Grid.Select(t => t.Name).Should().Equal("Лавка", "Магазин", "Сеть магазинов");
        OrdersTariffCatalog.Grid.Select(t => t.SortOrder).Should().Equal(20, 30, 40);
    }

    [Fact]
    public void Grid_IdsAreUnique_AndDifferFromOtherLinesAndServicePlans()
    {
        var ids = OrdersTariffCatalog.Grid.Select(t => t.Id)
            .Concat(ZapisTariffCatalog.Grid.Select(t => t.Id))
            .Append(ZapisTariffCatalog.Showcase.Id)
            .Append(ShowcaseCatalog.OrdersShowcasePlanId)
            .Append(OrdersFreePlan.SeedId)
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().NotContain(Guid.Empty);
    }

    [Fact]
    public void Grid_Highlights_FitThePublicContract()
    {
        foreach (var tariff in OrdersTariffCatalog.Grid)
        {
            tariff.Highlights.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(PricingCatalogBuilder.PublicMaxHighlights);
            tariff.Highlights.Should().OnlyContain(h => h.Length <= PricingCatalogBuilder.MaxHighlightLength);
        }
        OrdersTariffCatalog.FreeHighlights.Split('\n').Should().HaveCountLessThanOrEqualTo(PricingCatalogBuilder.PublicMaxHighlights);
    }

    [Fact]
    public void ToEntity_BuildsAnActivePublicOrdersRow()
    {
        var plan = TariffCatalogSeeder.ToEntity(OrdersTariffCatalog.Shop);

        plan.Should().Match<SubscriptionPlanConfig>(p =>
            p.Line == Core.Enums.CompanyKind.Orders && p.AllowOrders && p.AllowPublicListing && p.AllowNotificationChannel
            && !p.AllowOnlineBooking && !p.AllowOnlinePayment && p.IsActive && p.IsPublic && !p.IsSystemFree && !p.IsSystemTrial
            && p.MaxProductsPerShop == 1000 && p.MaxOrdersPerMonth == 5000 && p.PricePerMonth == 1490);
        plan.Highlights!.Split('\n').Should().Equal(OrdersTariffCatalog.Shop.Highlights);
    }

    private static SubscriptionPlanConfig SeedFree() => new()
    {
        Id = OrdersFreePlan.SeedId, Name = OrdersTariffCatalog.LegacyFreeName, Line = Core.Enums.CompanyKind.Orders, IsSystemFree = true,
        Description = OrdersTariffCatalog.LegacyFreeDescription, Highlights = null, IsPublic = false, SortOrder = -1,
        MaxCompanies = 1, MaxEmployees = 2, MaxProductsPerShop = 50, MaxOrdersPerMonth = 150,
    };

    [Fact]
    public void AlignOrdersFreeTariff_Apply_MovesEverySeedFieldAndKeepsLimits()
    {
        var free = SeedFree();

        var changes = TariffCatalogSeeder.AlignOrdersFreeTariff(free, apply: true);

        changes.Should().HaveCount(5);
        (free.Name, free.Description, free.Highlights, free.IsPublic, free.SortOrder).Should().Be(
            (OrdersTariffCatalog.FreeName, OrdersTariffCatalog.FreeDescription, OrdersTariffCatalog.FreeHighlights, true, OrdersTariffCatalog.FreeSortOrder));
        (free.MaxCompanies, free.MaxEmployees, free.MaxProductsPerShop, free.MaxOrdersPerMonth).Should().Be((1, 2, 50, 150));
        TariffCatalogSeeder.AlignOrdersFreeTariff(free, apply: true).Should().BeEmpty("a second run changes nothing");
    }

    [Fact]
    public void AlignOrdersFreeTariff_Plan_DescribesButDoesNotMutate()
    {
        var free = SeedFree();

        TariffCatalogSeeder.AlignOrdersFreeTariff(free, apply: false).Should().HaveCount(5);

        free.Name.Should().Be(OrdersTariffCatalog.LegacyFreeName);
        free.Highlights.Should().BeNull();
        free.IsPublic.Should().BeFalse();
        free.SortOrder.Should().Be(-1);
    }

    [Fact]
    public void AlignOrdersFreeTariff_KeepsEveryFieldAnAdministratorChanged()
    {
        var free = SeedFree();
        free.Name = "Старт для магазина";
        free.Description = "Своё описание";
        free.Highlights = "Своё преимущество";
        free.IsPublic = true;
        free.SortOrder = 5;

        TariffCatalogSeeder.AlignOrdersFreeTariff(free, apply: true).Should().BeEmpty();

        (free.Name, free.Description, free.Highlights, free.SortOrder).Should().Be(("Старт для магазина", "Своё описание", "Своё преимущество", 5));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("highlights")]
    [InlineData("public")]
    [InlineData("sort")]
    public void AlignOrdersFreeTariff_ChangesOnlyTheFieldStillAtTheSeed(string untouched)
    {
        var free = SeedFree();
        // Every field except one has been edited by an administrator; only that one still carries the seed value.
        free.Name = untouched == "name" ? free.Name : "Моё имя";
        free.Description = untouched == "description" ? free.Description : "Моё описание";
        free.Highlights = untouched == "highlights" ? null : "Мои преимущества";
        free.IsPublic = untouched != "public";
        free.SortOrder = untouched == "sort" ? -1 : 7;

        TariffCatalogSeeder.AlignOrdersFreeTariff(free, apply: true).Should().HaveCount(1);
    }

    [Fact]
    public void DescribeOrdersDivergences_NamesWhatDiffers()
    {
        var plan = TariffCatalogSeeder.ToEntity(OrdersTariffCatalog.Lavka);
        TariffCatalogSeeder.DescribeOrdersDivergences(plan, OrdersTariffCatalog.Lavka).Should().BeEmpty();

        plan.PricePerMonth = 790;
        plan.MaxOrdersPerMonth = null;
        plan.IsPublic = false;

        var differences = TariffCatalogSeeder.DescribeOrdersDivergences(plan, OrdersTariffCatalog.Lavka);

        differences.Should().HaveCount(3);
        differences.Should().Contain(d => d.Contains("цена в админке 790, в сетке 690"));
    }
}
