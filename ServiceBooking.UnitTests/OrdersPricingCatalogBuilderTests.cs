using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE37.md §37.9.3 — T37-B03: filter, order, products ceiling and highlights of the public "Заказы" grid.</summary>
public class OrdersPricingCatalogBuilderTests
{
    private const int Ceiling = 1000;

    private static SubscriptionPlanConfig Plan(string name, decimal price = 100, int sort = 10, CompanyKind line = CompanyKind.Orders,
        bool active = true, bool isPublic = true, Guid? id = null, int? products = 50, string? highlights = null) => new()
    {
        Id = id ?? Guid.NewGuid(), Name = name, PricePerMonth = price, SortOrder = sort, Line = line, IsActive = active, IsPublic = isPublic,
        MaxProductsPerShop = products, Highlights = highlights,
    };

    private static List<string> Names(IEnumerable<SubscriptionPlanConfig> plans) =>
        OrdersPricingCatalogBuilder.Build("v", plans, Ceiling, null).Plans.Select(p => p.Name).ToList();

    [Fact]
    public void Build_KeepsOnlyActivePublicPlansOfTheOrdersLine()
    {
        var names = Names([
            Plan("Лавка"),
            Plan("Скрытый", isPublic: false),
            Plan("Выключенный", active: false),
            Plan("Запись", line: CompanyKind.Services),
        ]);

        names.Should().Equal("Лавка");
    }

    [Fact]
    public void Build_NeverShowsTheServiceDemoPlan_EvenIfItIsPublic()
    {
        var names = Names([Plan("Демо", id: ShowcaseCatalog.OrdersShowcasePlanId), Plan("Лавка")]);

        names.Should().Equal("Лавка");
    }

    [Fact]
    public void Build_NoPublicPlans_GivesAnEmptyList()
    {
        OrdersPricingCatalogBuilder.Build("v", [Plan("Скрытый", isPublic: false)], Ceiling, null).Plans.Should().BeEmpty();
    }

    [Fact]
    public void Build_SortsBySortOrderThenPrice()
    {
        var names = Names([Plan("C", price: 5, sort: 20), Plan("B", price: 9, sort: 10), Plan("A", price: 3, sort: 10)]);

        names.Should().Equal("A", "B", "C");
    }

    [Theory]
    [InlineData(null, 1000)]
    [InlineData(1500, 1000)]
    [InlineData(300, 300)]
    [InlineData(1000, 1000)]
    public void Build_ProductsPerShop_IsCappedByTheCeilingAndNeverNull(int? limit, int expected)
    {
        var dto = OrdersPricingCatalogBuilder.Build("v", [Plan("P", products: limit)], Ceiling, null);

        dto.Plans.Single().IncludedProductsPerShop.Should().Be(expected);
    }

    [Fact]
    public void Build_MapsLimitsAndFlags_AndKeepsNullAsUnlimited()
    {
        var plan = Plan("Сеть магазинов");
        plan.MaxCompanies = null;
        plan.MaxEmployees = null;
        plan.MaxOrdersPerMonth = null;
        plan.IsSystemFree = false;

        var dto = OrdersPricingCatalogBuilder.Build("v", [plan], Ceiling, null).Plans.Single();

        (dto.IncludedShops, dto.IncludedMembers, dto.IncludedOrdersPerMonth, dto.IsFree).Should().Be(((int?)null, (int?)null, (int?)null, false));
    }

    [Fact]
    public void Build_Highlights_AreTrimmedAndLimitedToFive()
    {
        var plan = Plan("P", highlights: " один \n\n два\nтри\nчетыре\nпять\nшесть\nсемь");

        OrdersPricingCatalogBuilder.Build("v", [plan], Ceiling, null).Plans.Single().Highlights
            .Should().Equal("один", "два", "три", "четыре", "пять");
    }

    [Fact]
    public void Build_Envelope_HasVersionRubNoticeAndLegalNotice()
    {
        var withText = OrdersPricingCatalogBuilder.Build("abc", [Plan("P")], Ceiling, "Юридический текст");
        var blank = OrdersPricingCatalogBuilder.Build("abc", [Plan("P")], Ceiling, "  ");

        (withText.Version, withText.Currency, withText.Notice, withText.LegalNotice)
            .Should().Be(("abc", "RUB", OrdersPricingCatalogBuilder.DefaultNotice, "Юридический текст"));
        blank.LegalNotice.Should().BeNull();
    }
}
