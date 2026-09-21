using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class PricingCatalogBuilderTests
{
    private static SubscriptionPlanConfig Plan(
        string name = "Базовый", decimal price = 990, bool isActive = true, bool isPublic = true,
        int sortOrder = 0, bool isSystemFree = false, string? highlights = null,
        int? maxCompanies = 1, int? maxEmployees = 5) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        PricePerMonth = price,
        IsActive = isActive,
        IsPublic = isPublic,
        SortOrder = sortOrder,
        IsSystemFree = isSystemFree,
        Highlights = highlights,
        MaxCompanies = maxCompanies,
        MaxEmployees = maxEmployees,
    };

    private static SubscriptionOption Option(
        string name = "Доп. компания", OptionKind kind = OptionKind.Quantity, decimal? price = 490,
        bool isActive = true, bool isPublic = true, string? unitName = "компания",
        string? unitPriceText = null, int sortOrder = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Kind = kind,
        PricePerMonth = price,
        IsActive = isActive,
        IsPublic = isPublic,
        UnitName = unitName,
        UnitPriceText = unitPriceText,
        SortOrder = sortOrder,
    };

    [Fact]
    public void Build_FiltersOutInactiveAndNonPublicPlans()
    {
        var plans = new[]
        {
            Plan(name: "Публичный", isActive: true, isPublic: true),
            Plan(name: "Скрытый", isActive: true, isPublic: false),
            Plan(name: "Снят с продажи", isActive: false, isPublic: true),
        };

        var result = PricingCatalogBuilder.Build("v1", plans, [], legalNotice: null);

        result.Plans.Should().ContainSingle().Which.Name.Should().Be("Публичный");
    }

    [Fact]
    public void Build_FiltersOutOptionsWithoutPriceOrNotPublic()
    {
        var options = new[]
        {
            Option(name: "Продаётся", price: 490, isPublic: true),
            Option(name: "Без цены", price: null, isPublic: true),
            Option(name: "Непубличная", price: 300, isPublic: false),
            Option(name: "Снята с продажи", price: 300, isPublic: true, isActive: false),
        };

        var result = PricingCatalogBuilder.Build("v1", [], options, legalNotice: null);

        result.Options.Should().ContainSingle().Which.Name.Should().Be("Продаётся");
    }

    [Fact]
    public void Build_SortsPlansAndOptionsBySortOrderThenName()
    {
        var plans = new[]
        {
            Plan(name: "Б", sortOrder: 1),
            Plan(name: "А", sortOrder: 0),
        };

        var result = PricingCatalogBuilder.Build("v1", plans, [], legalNotice: null);

        result.Plans.Select(p => p.Name).Should().Equal("А", "Б");
    }

    [Fact]
    public void Build_QuantityOptionWithoutAuthoredText_GetsGenericUnitPriceText()
    {
        var option = Option(name: "Доп. сотрудник", unitName: "сотрудник", price: 290, unitPriceText: null);

        var result = PricingCatalogBuilder.Build("v1", [], [option], legalNotice: null);

        result.Options.Single().UnitPriceText.Should().Be("сотрудник — 290 ₽/мес");
    }

    [Fact]
    public void Build_QuantityOptionWithAuthoredText_KeepsAuthoredText()
    {
        var option = Option(unitPriceText: "дополнительная компания — 490 ₽/мес");

        var result = PricingCatalogBuilder.Build("v1", [], [option], legalNotice: null);

        result.Options.Single().UnitPriceText.Should().Be("дополнительная компания — 490 ₽/мес");
    }

    [Fact]
    public void Build_ToggleOptionNeverGetsUnitPriceText()
    {
        var option = Option(kind: OptionKind.Toggle, unitName: null, unitPriceText: null);

        var result = PricingCatalogBuilder.Build("v1", [], [option], legalNotice: null);

        result.Options.Single().UnitPriceText.Should().BeNull();
    }

    [Fact]
    public void Build_MapsIsSystemFreeToIsFree()
    {
        var plan = Plan(isSystemFree: true, price: 0);

        var result = PricingCatalogBuilder.Build("v1", [plan], [], legalNotice: null);

        result.Plans.Single().IsFree.Should().BeTrue();
    }

    [Fact]
    public void Build_SplitsHighlightsOnNewlineAndCapsAtFive()
    {
        var plan = Plan(highlights: "Первая\nВторая\nТретья\nЧетвёртая\nПятая\nШестая");

        var result = PricingCatalogBuilder.Build("v1", [plan], [], legalNotice: null);

        result.Plans.Single().Highlights.Should().HaveCount(5)
            .And.Equal("Первая", "Вторая", "Третья", "Четвёртая", "Пятая");
    }

    [Fact]
    public void Build_BlankLegalNoticeBecomesNull()
    {
        var result = PricingCatalogBuilder.Build("v1", [], [], legalNotice: "   ");

        result.LegalNotice.Should().BeNull();
    }

    [Fact]
    public void Build_NonBlankLegalNoticeIsPassedThrough()
    {
        var result = PricingCatalogBuilder.Build("v1", [], [], legalNotice: "Цены указаны с учётом НДС.");

        result.LegalNotice.Should().Be("Цены указаны с учётом НДС.");
    }

    [Fact]
    public void Build_UsesRubCurrencyAndGivenVersion()
    {
        var result = PricingCatalogBuilder.Build("abc123", [], [], legalNotice: null);

        result.Currency.Should().Be("RUB");
        result.Version.Should().Be("abc123");
    }
}
