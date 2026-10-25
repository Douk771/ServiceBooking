using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests.Cycle48;

/// <summary>CY48-B4 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.9): catalog constants equal contracts/cycle48/storefront-pages.json, and the seeder aligns only untouched texts.</summary>
public class TariffTextsAlignmentTests
{
    private static JsonElement Tariff(JsonDocument doc, string key) => doc.RootElement.GetProperty("tariffs").GetProperty(key);

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public void ZapisCatalog_EqualsTheJson()
    {
        using var doc = ContractFiles.Load("cycle48", "storefront-pages.json");
        var network = Tariff(doc, "zapisNetwork");
        var trial = Tariff(doc, "zapisTrial");

        ZapisTariffCatalog.Network.Description.Should().Be(network.GetProperty("description").GetString());
        ZapisTariffCatalog.Network.Highlights.Should().Equal(Strings(network.GetProperty("highlights")));
        ZapisTariffCatalog.Trial.Highlights.Should().Equal(Strings(trial.GetProperty("highlights")));
        ZapisTariffCatalog.LegacyNetworkDescription.Should().Be(network.GetProperty("legacyDescription").GetString());
        ZapisTariffCatalog.LegacyNetworkHighlights.Should().Be(string.Join('\n', Strings(network.GetProperty("legacyHighlights"))));
        ZapisTariffCatalog.LegacyTrialHighlights.Should().Be(string.Join('\n', Strings(trial.GetProperty("legacyHighlights"))));
    }

    [Fact]
    public void OrdersCatalog_EqualsTheJson()
    {
        using var doc = ContractFiles.Load("cycle48", "storefront-pages.json");
        var chain = Tariff(doc, "ordersChain");

        OrdersTariffCatalog.Chain.Description.Should().Be(chain.GetProperty("description").GetString());
        OrdersTariffCatalog.Chain.Highlights.Should().Equal(Strings(chain.GetProperty("highlights")));
        OrdersTariffCatalog.LegacyChainDescription.Should().Be(chain.GetProperty("legacyDescription").GetString());
        OrdersTariffCatalog.LegacyChainHighlights.Should().Be(string.Join('\n', Strings(chain.GetProperty("legacyHighlights"))));
    }

    [Fact]
    public void PricesAndLimits_AreNotChangedByTheCycle()
    {
        ZapisTariffCatalog.Network.PricePerMonth.Should().Be(3900m);
        ZapisTariffCatalog.Network.MaxCompanies.Should().BeNull();
        ZapisTariffCatalog.Network.MaxEmployees.Should().BeNull();
        ZapisTariffCatalog.Network.PhotoQuotaMb.Should().Be(10000);
        OrdersTariffCatalog.Chain.PricePerMonth.Should().Be(2990m);
        OrdersTariffCatalog.Chain.MaxProductsPerShop.Should().Be(1000);
        OrdersTariffCatalog.Chain.MaxOrdersPerMonth.Should().BeNull();
    }

    private static SubscriptionPlanConfig Plan(string? description, string? highlights) => new() { Name = "Сеть", Description = description, Highlights = highlights };

    private static List<string> AlignNetwork(SubscriptionPlanConfig plan, bool apply) => TariffCatalogSeeder.AlignPaidTariffTexts(
        plan, ZapisTariffCatalog.LegacyNetworkDescription, ZapisTariffCatalog.Network.Description,
        ZapisTariffCatalog.LegacyNetworkHighlights, string.Join('\n', ZapisTariffCatalog.Network.Highlights), apply);

    [Fact]
    public void Align_LegacyFields_AreReplaced()
    {
        var plan = Plan(ZapisTariffCatalog.LegacyNetworkDescription, ZapisTariffCatalog.LegacyNetworkHighlights);

        var changes = AlignNetwork(plan, apply: true);

        changes.Should().Equal("описание обновлено", "преимущества обновлены");
        plan.Description.Should().Be(ZapisTariffCatalog.Network.Description);
        plan.Highlights.Should().Be(string.Join('\n', ZapisTariffCatalog.Network.Highlights));
    }

    [Fact]
    public void Align_EditedFields_AreLeftAlone()
    {
        var plan = Plan("Своё описание", ZapisTariffCatalog.LegacyNetworkHighlights + "\nещё строка");

        var changes = AlignNetwork(plan, apply: true);

        changes.Should().BeEmpty();
        plan.Description.Should().Be("Своё описание");
        plan.Highlights.Should().Be(ZapisTariffCatalog.LegacyNetworkHighlights + "\nещё строка");
    }

    [Fact]
    public void Align_EachFieldIndependently()
    {
        var plan = Plan("Своё описание", ZapisTariffCatalog.LegacyNetworkHighlights);

        AlignNetwork(plan, apply: true).Should().Equal("преимущества обновлены");
        plan.Description.Should().Be("Своё описание");
    }

    [Fact]
    public void Align_WithoutApply_ReportsButDoesNotMutate()
    {
        var plan = Plan(ZapisTariffCatalog.LegacyNetworkDescription, ZapisTariffCatalog.LegacyNetworkHighlights);

        AlignNetwork(plan, apply: false).Should().Equal("описание обновлено", "преимущества обновлены");

        plan.Description.Should().Be(ZapisTariffCatalog.LegacyNetworkDescription);
        plan.Highlights.Should().Be(ZapisTariffCatalog.LegacyNetworkHighlights);
    }

    [Fact]
    public void Align_IsIdempotent()
    {
        var plan = Plan(ZapisTariffCatalog.LegacyNetworkDescription, ZapisTariffCatalog.LegacyNetworkHighlights);
        AlignNetwork(plan, apply: true);

        AlignNetwork(plan, apply: true).Should().BeEmpty();
    }

    [Fact]
    public void Align_CaseDifferenceCountsAsEdited()
    {
        var plan = Plan(ZapisTariffCatalog.LegacyNetworkDescription.ToUpperInvariant(), null);

        AlignNetwork(plan, apply: true).Should().BeEmpty();
    }

    [Fact]
    public void Align_TrialHighlightsOnly_LeavesDescriptionAlone()
    {
        var plan = Plan("14 дней возможностей тарифа «Салон» бесплатно.", ZapisTariffCatalog.LegacyTrialHighlights);

        var changes = TariffCatalogSeeder.AlignPaidTariffTexts(plan, null, null, ZapisTariffCatalog.LegacyTrialHighlights,
            string.Join('\n', ZapisTariffCatalog.Trial.Highlights), apply: true);

        changes.Should().Equal("преимущества обновлены");
        plan.Highlights!.Split('\n').Should().HaveCount(4).And.Contain("Отправка сообщений клиентам — не дольше 7 дней из 14");
        plan.Description.Should().Be("14 дней возможностей тарифа «Салон» бесплатно.");
    }

    [Fact]
    public void Align_OrdersChain_Works()
    {
        var plan = Plan(OrdersTariffCatalog.LegacyChainDescription, OrdersTariffCatalog.LegacyChainHighlights);

        TariffCatalogSeeder.AlignPaidTariffTexts(plan, OrdersTariffCatalog.LegacyChainDescription, OrdersTariffCatalog.Chain.Description,
            OrdersTariffCatalog.LegacyChainHighlights, string.Join('\n', OrdersTariffCatalog.Chain.Highlights), apply: true)
            .Should().Equal("описание обновлено", "преимущества обновлены");
        plan.Description.Should().Be(OrdersTariffCatalog.Chain.Description);
    }

    [Fact]
    public void ToEntity_OnACleanDatabase_TakesTheNewTexts()
    {
        TariffCatalogSeeder.ToEntity(ZapisTariffCatalog.Network).Description.Should().Be(ZapisTariffCatalog.Network.Description);
        TariffCatalogSeeder.ToEntity(ZapisTariffCatalog.Trial).Highlights.Should().Contain("не дольше 7 дней из 14");
        TariffCatalogSeeder.ToEntity(OrdersTariffCatalog.Chain).Highlights.Should().Contain("Цена не зависит от числа магазинов");
    }

    [Fact]
    public void Warnings_ForbiddenWordingLeftInEditedFields_IsReported()
    {
        var warnings = TariffCatalogSeeder.ForbiddenTextWarnings("Сеть", "Стоимость от 3 900 ₽ в месяц", "Всё, что в «Салоне»\nИтоговая цена — после заявки");

        warnings.Should().HaveCount(2);
        warnings[0].Should().StartWith("внимание: «Сеть»: в описании осталось «от 3 900 ₽»").And.Contain("contracts/cycle48/storefront-pages.json");
        warnings[1].Should().StartWith("внимание: «Сеть»: в преимуществах осталось «после заявки»");
    }

    [Fact]
    public void Warnings_CleanTexts_GiveNothing() =>
        TariffCatalogSeeder.ForbiddenTextWarnings("Сеть", ZapisTariffCatalog.Network.Description, string.Join('\n', ZapisTariffCatalog.Network.Highlights))
            .Should().BeEmpty();
}
