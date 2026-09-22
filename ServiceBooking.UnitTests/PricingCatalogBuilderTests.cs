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
    public void Build_SortsPlansBySortOrder()
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
    public void Build_TiedSortOrder_PlansFallBackToPrice()
    {
        // API_CONTRACT_CYCLE5.md §39: "по sortOrder, затем по цене" — not by name.
        var plans = new[]
        {
            Plan(name: "Я", price: 500, sortOrder: 0),
            Plan(name: "А", price: 1500, sortOrder: 0),
        };

        var result = PricingCatalogBuilder.Build("v1", plans, [], legalNotice: null);

        result.Plans.Select(p => p.Name).Should().Equal("Я", "А");
    }

    [Fact]
    public void Build_TiedSortOrder_OptionsFallBackToPrice()
    {
        var options = new[]
        {
            Option(name: "Я", price: 100, sortOrder: 0),
            Option(name: "А", price: 900, sortOrder: 0),
        };

        var result = PricingCatalogBuilder.Build("v1", [], options, legalNotice: null);

        result.Options.Select(o => o.Name).Should().Equal("Я", "А");
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
    public void Build_SplitsHighlightsOnNewlineAndCapsAtPublicMaxHighlights()
    {
        // openapi-cycle5.yaml PublicPlanDto.highlights: maxItems 5 — deliberately LOWER than
        // AdminPlanDto/AdminPlanInput's write-time cap of 10 (PricingCatalogBuilder.MaxHighlights).
        // cycle-07 QA finding #4: the display side must cap at PublicMaxHighlights, not MaxHighlights,
        // or an admin can save up to 10 bullets that the storefront silently trims to 5 with no warning.
        var lines = Enumerable.Range(1, PricingCatalogBuilder.MaxHighlights + 2).Select(i => $"Пункт {i}").ToList();
        var plan = Plan(highlights: string.Join('\n', lines));

        var result = PricingCatalogBuilder.Build("v1", [plan], [], legalNotice: null);

        result.Plans.Single().Highlights.Should().HaveCount(PricingCatalogBuilder.PublicMaxHighlights)
            .And.Equal(lines.Take(PricingCatalogBuilder.PublicMaxHighlights));
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

    // ── ValidateHighlights (cycle-07 QA findings #3 and #4) ─────────────────────────────────────────

    [Fact]
    public void ValidateHighlights_NullOrEmpty_IsValid()
    {
        PricingCatalogBuilder.ValidateHighlights(null).Should().BeNull();
        PricingCatalogBuilder.ValidateHighlights([]).Should().BeNull();
    }

    [Fact]
    public void ValidateHighlights_AtMaxCount_IsValid()
    {
        var highlights = Enumerable.Range(1, PricingCatalogBuilder.MaxHighlights).Select(i => $"Пункт {i}").ToList();

        PricingCatalogBuilder.ValidateHighlights(highlights).Should().BeNull();
    }

    // openapi-cycle5.yaml AdminPlanInput.highlights: maxItems 10 — writing 11 must be rejected, not
    // silently truncated (the previous behaviour let an admin save more than the contract allows and
    // never find out).
    [Fact]
    public void ValidateHighlights_OverMaxCount_ReturnsError()
    {
        var highlights = Enumerable.Range(1, PricingCatalogBuilder.MaxHighlights + 1).Select(i => $"Пункт {i}").ToList();

        PricingCatalogBuilder.ValidateHighlights(highlights).Should().NotBeNull();
    }

    [Fact]
    public void ValidateHighlights_ItemAtMaxLength_IsValid()
    {
        var highlights = new List<string> { new string('x', PricingCatalogBuilder.MaxHighlightLength) };

        PricingCatalogBuilder.ValidateHighlights(highlights).Should().BeNull();
    }

    // openapi-cycle5.yaml AdminPlanDto/AdminPlanInput.highlights item: maxLength 120 — writing 121 must
    // be rejected (cycle-07 QA finding #3: this used to not be checked at write time at all).
    [Fact]
    public void ValidateHighlights_ItemOverMaxLength_ReturnsError()
    {
        var highlights = new List<string> { new string('x', PricingCatalogBuilder.MaxHighlightLength + 1) };

        PricingCatalogBuilder.ValidateHighlights(highlights).Should().NotBeNull();
    }
}
