using FluentAssertions;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE28.md §573.2 — the grid, the free-tariff alignment (Q28-1) and the divergence report.</summary>
public class ZapisTariffCatalogTests
{
    [Fact]
    public void Grid_HasTheApprovedPricesAndLimits()
    {
        ZapisTariffCatalog.Studio.Should().Match<ZapisTariff>(t => t.PricePerMonth == 790 && t.MaxCompanies == 1 && t.MaxEmployees == 5);
        ZapisTariffCatalog.Salon.Should().Match<ZapisTariff>(t => t.PricePerMonth == 1890 && t.MaxCompanies == 3 && t.MaxEmployees == 15);
        ZapisTariffCatalog.Network.Should().Match<ZapisTariff>(t => t.PricePerMonth == 3900 && t.MaxCompanies == null && t.MaxEmployees == null);
        ZapisTariffCatalog.Trial.Should().Match<ZapisTariff>(t => t.PricePerMonth == 0 && t.MaxCompanies == 3 && t.MaxEmployees == 15 && t.IsSystemTrial);
    }

    [Fact]
    public void Grid_PhotoLimits_FollowCustomerDecisionQ283()
    {
        // Q28-3, variant A: Старт 100 МБ (the free row, untouched), Студия 1 ГБ / 6 мес, Салон 3 ГБ / 12 мес, Сеть 10 ГБ / 12 мес.
        (ZapisTariffCatalog.Studio.PhotoQuotaMb, ZapisTariffCatalog.Studio.PhotoRetention).Should().Be((1000, PhotoRetention.SixMonths));
        (ZapisTariffCatalog.Salon.PhotoQuotaMb, ZapisTariffCatalog.Salon.PhotoRetention).Should().Be((3000, PhotoRetention.TwelveMonths));
        (ZapisTariffCatalog.Network.PhotoQuotaMb, ZapisTariffCatalog.Network.PhotoRetention).Should().Be((10000, PhotoRetention.TwelveMonths));
    }

    [Fact]
    public void Grid_IdsAreDistinctLiterals_AndDifferFromTheShowcasePlan()
    {
        var ids = ZapisTariffCatalog.Grid.Select(t => t.Id).Append(ShowcaseCatalog.ShowcasePlanId).ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().NotContain(Guid.Empty);
    }

    [Fact]
    public void Grid_NamesAreUnique_IgnoringCase() =>
        ZapisTariffCatalog.Grid.Select(t => t.Name.ToLowerInvariant()).Should().OnlyHaveUniqueItems();

    [Fact]
    public void Grid_TheTrialIsPublicAndIsTheOnlySystemTrial()
    {
        ZapisTariffCatalog.Grid.Where(t => t.IsSystemTrial).Should().ContainSingle().Which.IsPublic.Should().BeTrue(
            "TrialActivationService answers TrialNotOffered for a trial plan that is not public");
    }

    [Fact]
    public void Grid_EveryPublicTariffIsPricedByWhatTheProductLimits_NoInventedAdvantages()
    {
        var forbidden = new[] { "домен", "поддержк", "без подписи", "сводк", "год", "Powered", "рассылк" };
        foreach (var tariff in ZapisTariffCatalog.Grid)
        {
            var text = string.Join(" | ", tariff.Highlights.Append(tariff.Description));
            foreach (var word in forbidden)
                text.Should().NotContainEquivalentOf(word, $"'{tariff.Name}' must not advertise what the product does not do (§573.4a)");
        }
    }

    [Fact]
    public void Grid_Highlights_FitThePublicCardLimits()
    {
        foreach (var tariff in ZapisTariffCatalog.Grid.Append(ZapisTariffCatalog.Showcase))
        {
            tariff.Highlights.Count.Should().BeLessThanOrEqualTo(5);
            tariff.Highlights.All(h => h.Length <= 120).Should().BeTrue();
        }
    }

    [Fact]
    public void ShowcaseTariff_IsHiddenAndNotForSale()
    {
        var showcase = ZapisTariffCatalog.Showcase;

        showcase.Id.Should().Be(ShowcaseCatalog.ShowcasePlanId);
        showcase.IsPublic.Should().BeFalse();
        showcase.PricePerMonth.Should().Be(0);
        showcase.IsSystemTrial.Should().BeFalse();
    }

    [Fact]
    public void ToEntity_CreatesAnOrdinaryServicesTariff_WithoutMailingCapabilities()
    {
        var plan = TariffCatalogSeeder.ToEntity(ZapisTariffCatalog.Studio);

        plan.Line.Should().Be(CompanyKind.Services);
        plan.AllowMailing.Should().BeFalse();
        plan.AllowNotificationChannel.Should().BeFalse();
        plan.AllowOnlinePayment.Should().BeFalse();
        plan.AllowOnlineBooking.Should().BeTrue();
        plan.AllowAnalytics.Should().BeTrue();
        plan.IsActive.Should().BeTrue();
        plan.IsSystemFree.Should().BeFalse();
        plan.Highlights!.Split('\n').Should().HaveCount(4);
    }

    private static SubscriptionPlanConfig LegacyFree() => new()
    {
        Name = ZapisTariffCatalog.LegacyFreeName, MaxEmployees = 1, MaxCompanies = 1, AllowOnlineBooking = false,
        Description = ZapisTariffCatalog.LegacyFreeDescription, Highlights = ZapisTariffCatalog.LegacyFreeHighlights, IsSystemFree = true,
    };

    [Fact]
    public void AlignFreeTariff_MovesTheUntouchedSeedRowToStart()
    {
        var free = LegacyFree();

        var changes = TariffCatalogSeeder.AlignFreeTariff(free, apply: true);

        changes.Should().HaveCount(5);
        free.Name.Should().Be("Старт");
        free.AllowOnlineBooking.Should().BeTrue();
        free.MaxEmployees.Should().Be(2);
        free.MaxCompanies.Should().Be(1, "the free tariff still allows one company");
        free.Description.Should().Be(ZapisTariffCatalog.StartDescription);
        free.Highlights.Should().Be(ZapisTariffCatalog.StartHighlights);
    }

    [Fact]
    public void AlignFreeTariff_PlanMode_ChangesNothing()
    {
        var free = LegacyFree();

        var changes = TariffCatalogSeeder.AlignFreeTariff(free, apply: false);

        changes.Should().NotBeEmpty();
        free.Should().BeEquivalentTo(LegacyFree(), o => o.Excluding(p => p.CreatedAt));
    }

    [Fact]
    public void AlignFreeTariff_LeavesFieldsAnAdministratorChanged()
    {
        var free = LegacyFree();
        free.Name = "Мой бесплатный";
        free.MaxEmployees = 3;
        free.Highlights = "Своё";

        TariffCatalogSeeder.AlignFreeTariff(free, apply: true);

        free.Name.Should().Be("Мой бесплатный");
        free.MaxEmployees.Should().Be(3);
        free.Highlights.Should().Be("Своё");
        free.AllowOnlineBooking.Should().BeTrue("the field still had the seed value");
    }

    [Fact]
    public void AlignFreeTariff_IsIdempotent()
    {
        var free = LegacyFree();
        TariffCatalogSeeder.AlignFreeTariff(free, apply: true);

        TariffCatalogSeeder.AlignFreeTariff(free, apply: true).Should().BeEmpty();
    }

    [Fact]
    public void DescribeDivergences_NamesEveryDifferingField_AndNothingForAMatch()
    {
        var studio = TariffCatalogSeeder.ToEntity(ZapisTariffCatalog.Studio);
        TariffCatalogSeeder.DescribeDivergences(studio, ZapisTariffCatalog.Studio).Should().BeEmpty();

        studio.PricePerMonth = 990;
        studio.MaxEmployees = null;
        studio.IsPublic = false;

        var differences = TariffCatalogSeeder.DescribeDivergences(studio, ZapisTariffCatalog.Studio);

        differences.Should().HaveCount(3);
        differences.Should().Contain(d => d.Contains("цена в админке 990, в сетке 790") && d.Contains("оставлено как есть"));
        differences.Should().Contain(d => d.Contains("сотрудников в админке без ограничения, в сетке 5"));
    }
}
