using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 38 — the "Заказы" tariff grid (T38-B04, T38-B05). Written from SPEC_CYCLE38 (§6, Q38-1…Q38-3), API_CONTRACT_CYCLE38.md,
/// contracts/cycle38/openapi.yaml and the review fix (apply never touches IsPublic/Highlights of the existing free tariff), not from the code.
/// Tests that need a catalog without the grid live in their own classes (own database).
/// </summary>
public abstract class Cycle38OrdersTestBase(TestDatabaseFixture fixture) : Cycle28ShowcaseTestBase(fixture)
{
    protected static readonly OpenApiContract C38 = OpenApiContract.Load("cycle38");
    protected static readonly string[] GridNames = ["Лавка", "Магазин", "Сеть магазинов"];

    protected async Task<TariffCatalogReport> ApplyAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<TariffCatalogSeeder>().ApplyAsync();
        scope.ServiceProvider.GetRequiredService<PricingCatalogCache>().Invalidate();
        return report;
    }

    protected void InvalidateCache()
    {
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PricingCatalogCache>().Invalidate();
    }

    protected Task<List<SubscriptionPlanConfig>> OrdersPlansAsync() =>
        DbAsync(db => db.SubscriptionPlanConfigs.AsNoTracking().Where(p => p.Line == CompanyKind.Orders).ToListAsync());

    protected async Task<(HttpResponseMessage Response, JsonElement Body)> GetOrdersPricingAsync(HttpClient? client = null)
    {
        var response = await (client ?? AnonymousClient()).GetAsync("/api/pricing/orders");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response, doc.RootElement.Clone());
    }

    protected static List<string?> Names(JsonElement body) =>
        body.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();

    protected Task SetPublicEnabledAsync(string value) => DbAsync(async db =>
    {
        var row = await db.PlatformSettings.FindAsync("pricing.public-enabled");
        if (row is null) db.PlatformSettings.Add(new PlatformSetting { Key = "pricing.public-enabled", Value = value, UpdatedAt = DateTime.UtcNow });
        else row.Value = value;
        await db.SaveChangesAsync();
    });
}

/// <summary>T38-B04: apply on a clean database.</summary>
[Trait("Area", "billing")]
public class Cycle38OrdersTariffsCleanTests(TestDatabaseFixture fixture) : Cycle38OrdersTestBase(fixture)
{
    [Fact, TestCase("CY38-B04-01")]
    public async Task Apply_OnCleanCatalog_CreatesThreeOrdersTariffs_IsIdempotent_AndLeavesZapisShopAlone()
    {
        // A "Записи" tariff with the same name as an Orders grid tariff must not be taken for it, nor changed.
        var zapisShopId = Guid.NewGuid();
        await DbAsync(async db =>
        {
            db.SubscriptionPlanConfigs.Add(new SubscriptionPlanConfig
            {
                Id = zapisShopId, Name = "Магазин", Line = CompanyKind.Services, PricePerMonth = 111, MaxEmployees = 7, MaxCompanies = 2,
                IsActive = true, IsPublic = true, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });
        var before = await OrdersPlansAsync();
        before.Select(p => p.Name).Should().NotContain(GridNames);

        var first = await ApplyAsync();
        first.LockBusy.Should().BeFalse();
        first.OrdersPlansCreated.Should().Be(3);

        var after = await OrdersPlansAsync();
        var byName = after.Where(p => GridNames.Contains(p.Name)).ToDictionary(p => p.Name);
        byName.Keys.Should().BeEquivalentTo(GridNames);
        byName["Лавка"].PricePerMonth.Should().Be(690);
        byName["Лавка"].MaxCompanies.Should().Be(1);
        byName["Лавка"].MaxEmployees.Should().Be(5);
        byName["Лавка"].MaxProductsPerShop.Should().Be(300);
        byName["Лавка"].MaxOrdersPerMonth.Should().Be(1500);
        byName["Магазин"].PricePerMonth.Should().Be(1490);
        byName["Магазин"].MaxCompanies.Should().Be(3);
        byName["Магазин"].MaxEmployees.Should().Be(15);
        byName["Магазин"].MaxProductsPerShop.Should().Be(1000);
        byName["Магазин"].MaxOrdersPerMonth.Should().Be(5000);
        byName["Сеть магазинов"].PricePerMonth.Should().Be(2990);
        byName["Сеть магазинов"].MaxCompanies.Should().BeNull();
        byName["Сеть магазинов"].MaxEmployees.Should().BeNull();
        byName["Сеть магазинов"].MaxOrdersPerMonth.Should().BeNull();
        byName.Values.Should().OnlyContain(p => p.IsActive && p.IsPublic && p.Line == CompanyKind.Orders && p.AllowOrders);
        byName["Лавка"].Id.Should().Be(OrdersTariffCatalog.LavkaId);
        byName["Магазин"].Id.Should().Be(OrdersTariffCatalog.ShopId);
        byName["Сеть магазинов"].Id.Should().Be(OrdersTariffCatalog.ChainId);

        // The "Записи" tariff with the same name is untouched, and no second "Магазин" appeared in its line.
        var zapis = await DbAsync(db => db.SubscriptionPlanConfigs.AsNoTracking().Where(p => p.Line == CompanyKind.Services && p.Name == "Магазин").ToListAsync());
        zapis.Should().ContainSingle().Which.Should().Match<SubscriptionPlanConfig>(p => p.Id == zapisShopId && p.PricePerMonth == 111 && p.MaxEmployees == 7 && p.MaxCompanies == 2);

        // Rules: one per catalog option for every created tariff; whatsapp is "extra", everything else unavailable.
        await DbAsync(async db =>
        {
            var options = await db.SubscriptionOptions.WhereNotRetired().ToListAsync();
            foreach (var plan in byName.Values)
            {
                var rules = await db.PlanOptionRules.Where(r => r.PlanConfigId == plan.Id).ToListAsync();
                rules.Select(r => r.OptionId).Should().BeEquivalentTo(options.Select(o => o.Id), plan.Name);
                foreach (var option in options)
                {
                    var expected = option.Code == "notifications.whatsapp" ? OptionAvailability.Extra : OptionAvailability.Unavailable;
                    rules.Single(r => r.OptionId == option.Id).Availability.Should().Be(expected, $"{plan.Name}: {option.Code}");
                }
            }
        });

        // The free tariff is moved to the new wording; its limits stay.
        var free = after.Single(p => p.IsSystemFree);
        free.Name.Should().Be("Бесплатный");
        free.SortOrder.Should().Be(10);
        free.MaxCompanies.Should().Be(1);
        free.MaxEmployees.Should().Be(2);
        free.MaxProductsPerShop.Should().Be(50);
        free.MaxOrdersPerMonth.Should().Be(150);

        // Second apply: nothing changes.
        var rulesBefore = await DbAsync(db => db.PlanOptionRules.CountAsync());
        var snapshot = await OrdersPlansAsync();
        var second = await ApplyAsync();
        second.OrdersPlansCreated.Should().Be(0);
        second.OrdersRulesAdded.Should().Be(0);
        second.OrdersFreeFieldsAligned.Should().Be(0);
        (await DbAsync(db => db.PlanOptionRules.CountAsync())).Should().Be(rulesBefore, "a repeated apply must not add rules");
        var again = await OrdersPlansAsync();
        again.Should().HaveCount(snapshot.Count);
        again.Select(p => (p.Id, p.Name, p.PricePerMonth, p.IsPublic, p.SortOrder)).Should()
            .BeEquivalentTo(snapshot.Select(p => (p.Id, p.Name, p.PricePerMonth, p.IsPublic, p.SortOrder)));
    }
}

/// <summary>T38-B04: the free tariff's visibility is the administrator's decision (review fix); a price edited in the admin panel survives apply.</summary>
[Trait("Area", "billing")]
public class Cycle38OrdersTariffsAdminEditsTests(TestDatabaseFixture fixture) : Cycle38OrdersTestBase(fixture)
{
    [Fact, TestCase("CY38-B04-02")]
    public async Task Apply_DoesNotOverwriteEditedPrice_ReportsDivergence_AndKeepsFreeTariffIsPublicAndHighlights()
    {
        // Existing free tariff: unpublished (seeded so) and with highlights typed by an administrator.
        await DbAsync(async db =>
        {
            var free = await db.SubscriptionPlanConfigs.SingleAsync(p => p.IsSystemFree && p.Line == CompanyKind.Orders);
            free.IsPublic = false;
            free.Highlights = "мой пункт";
            await db.SaveChangesAsync();
        });

        await ApplyAsync();
        await DbAsync(async db =>
        {
            var lavka = await db.SubscriptionPlanConfigs.SingleAsync(p => p.Id == OrdersTariffCatalog.LavkaId);
            lavka.PricePerMonth = 777;
            await db.SaveChangesAsync();
        });

        var report = await ApplyAsync();
        report.OrdersPlansCreated.Should().Be(0);
        var lavkaNow = (await OrdersPlansAsync()).Single(p => p.Id == OrdersTariffCatalog.LavkaId);
        lavkaNow.PricePerMonth.Should().Be(777, "apply is create-only: the price edited in the admin panel is not overwritten");
        report.Lines.Should().Contain(l => l.Contains("Лавка") && l.Contains("цена") && l.Contains("777") && l.Contains("690"),
            "the divergence is named in the report");

        var free = (await OrdersPlansAsync()).Single(p => p.IsSystemFree);
        free.IsPublic.Should().BeFalse("apply must not publish a free tariff the administrator hid");
        free.Highlights.Should().Be("мой пункт", "apply must not rewrite highlights of the existing free tariff");
    }
}

/// <summary>T38-B05: 404 before seeding (own database: nobody has applied the grid).</summary>
[Trait("Area", "billing")]
public class Cycle38OrdersPricingNotSeededTests(TestDatabaseFixture fixture) : Cycle38OrdersTestBase(fixture)
{
    [Fact, TestCase("CY38-B05-01")]
    public async Task OrdersPricing_BeforeSeeding_Is404WithEmptyBody()
    {
        InvalidateCache();
        var response = await AnonymousClient().GetAsync("/api/pricing/orders");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeNullOrEmpty("API_CONTRACT_CYCLE38: 404 without a body");
    }
}

/// <summary>T38-B05: the public "Заказы" price list after the grid is applied.</summary>
[Trait("Area", "billing")]
public class Cycle38OrdersPricingTests(TestDatabaseFixture fixture) : Cycle38OrdersTestBase(fixture)
{
    [Fact, TestCase("CY38-B05-02")]
    public async Task OrdersPricing_AfterApply_Is200_MatchesContract_WithHeaders_AndFreeFirstWhenPublished()
    {
        await ApplyAsync();
        // The administrator publishes the free tariff (apply does not). The class shares a database: bring the grid back to its seeded visibility.
        await DbAsync(async db =>
        {
            foreach (var plan in await db.SubscriptionPlanConfigs.Where(p => p.Line == CompanyKind.Orders && GridNames.Contains(p.Name)).ToListAsync())
            { plan.IsPublic = true; plan.IsActive = true; }
            (await db.SubscriptionPlanConfigs.SingleAsync(p => p.IsSystemFree && p.Line == CompanyKind.Orders)).IsPublic = true;
            await db.SaveChangesAsync();
        });
        InvalidateCache();

        var (response, body) = await GetOrdersPricingAsync();
        C38.AssertResponse("get", "/api/pricing/orders", 200, body);
        Names(body).Should().Equal("Бесплатный", "Лавка", "Магазин", "Сеть магазинов");
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(60));
        body.GetProperty("currency").GetString().Should().Be("RUB");

        var plans = body.GetProperty("plans").EnumerateArray().ToList();
        plans.Single(p => p.GetProperty("isFree").GetBoolean()).GetProperty("name").GetString().Should().Be("Бесплатный");
        plans.Count(p => p.GetProperty("isFree").GetBoolean()).Should().Be(1);
        var shop = plans.Single(p => p.GetProperty("name").GetString() == "Магазин");
        shop.GetProperty("pricePerMonth").GetDecimal().Should().Be(1490);
        shop.GetProperty("includedShops").GetInt32().Should().Be(3);
        shop.GetProperty("includedMembers").GetInt32().Should().Be(15);
        shop.GetProperty("includedProductsPerShop").GetInt32().Should().Be(1000);
        shop.GetProperty("includedOrdersPerMonth").GetInt32().Should().Be(5000);
        var chain = plans.Single(p => p.GetProperty("name").GetString() == "Сеть магазинов");
        chain.GetProperty("includedShops").ValueKind.Should().Be(JsonValueKind.Null);
        chain.GetProperty("includedMembers").ValueKind.Should().Be(JsonValueKind.Null);
        chain.GetProperty("includedOrdersPerMonth").ValueKind.Should().Be(JsonValueKind.Null);
        plans.Select(p => p.GetProperty("sortOrder").GetInt32()).Should().BeInAscendingOrder();
        foreach (var p in plans) p.GetProperty("highlights").GetArrayLength().Should().BeLessThanOrEqualTo(5);
    }

    [Fact, TestCase("CY38-B05-03")]
    public async Task OrdersPricing_HidesDemoInactiveAndNonPublic_AndServicesTariffs_AndOrdersAreNotInZapisPricing()
    {
        await ApplyAsync();
        await EnsureShowcasePlanAsync();
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TariffCatalogSeeder>().EnsureOrdersShowcasePlanAsync();
        await DbAsync(async db =>
        {
            // The hidden "Демо" made public by mistake must still not be listed.
            var demo = await db.SubscriptionPlanConfigs.SingleAsync(p => p.Id == ShowcaseCatalog.OrdersShowcasePlanId);
            demo.IsPublic = true; demo.IsActive = true;
            (await db.SubscriptionPlanConfigs.SingleAsync(p => p.Id == OrdersTariffCatalog.ShopId)).IsPublic = false;
            (await db.SubscriptionPlanConfigs.SingleAsync(p => p.Id == OrdersTariffCatalog.ChainId)).IsActive = false;
            db.SubscriptionPlanConfigs.Add(new SubscriptionPlanConfig
            {
                Id = Guid.NewGuid(), Name = "Служебный Services", Line = CompanyKind.Services, PricePerMonth = 5, IsActive = true, IsPublic = true,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });
        InvalidateCache();

        var (_, body) = await GetOrdersPricingAsync();
        var names = Names(body);
        names.Should().Contain("Лавка");
        names.Should().NotContain("Магазин", "IsPublic = false");
        names.Should().NotContain("Сеть магазинов", "IsActive = false");
        names.Should().NotContain(ShowcaseCatalog.OrdersShowcasePlanName);
        names.Should().NotContain(n => n!.Contains("Демо") || n.Contains("Витрина") || n == "Служебный Services");
        body.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())
            .Should().NotContain(ShowcaseCatalog.OrdersShowcasePlanId);

        // The Zapis list has no Orders tariffs.
        await SetPublicEnabledAsync("true");
        InvalidateCache();
        var zapis = await AnonymousClient().GetAsync("/api/pricing");
        zapis.StatusCode.Should().Be(HttpStatusCode.OK);
        var zapisText = await zapis.Content.ReadAsStringAsync();
        zapisText.Should().NotContain("Лавка").And.NotContain("Сеть магазинов").And.NotContain("Бесплатный");
    }

    [Fact, TestCase("CY38-B05-04")]
    public async Task OrdersPricing_DoesNotDependOnZapisPublicationSwitch()
    {
        await ApplyAsync();

        await SetPublicEnabledAsync("false");
        InvalidateCache();
        (await AnonymousClient().GetAsync("/api/pricing")).StatusCode.Should().Be(HttpStatusCode.NotFound, "the Zapis list is switched off");
        var (_, whileOff) = await GetOrdersPricingAsync();
        Names(whileOff).Should().Contain("Лавка");

        await SetPublicEnabledAsync("true");
        InvalidateCache();
        (await AnonymousClient().GetAsync("/api/pricing")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetOrdersPricingAsync()).Body.GetProperty("plans").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact, TestCase("CY38-B05-05")]
    public async Task OrdersPricing_ETag_IfNoneMatch_Is304_AndChangesWhenPriceChanges()
    {
        await ApplyAsync();
        var (first, _) = await GetOrdersPricingAsync();
        var etag = first.Headers.ETag!.ToString();

        var client = AnonymousClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-None-Match", etag);
        var conditional = await client.GetAsync("/api/pricing/orders");
        conditional.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await conditional.Content.ReadAsStringAsync()).Should().BeEmpty();

        var stale = AnonymousClient();
        stale.DefaultRequestHeaders.TryAddWithoutValidation("If-None-Match", "W/\"other\"");
        (await stale.GetAsync("/api/pricing/orders")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY38-B05-06")]
    public async Task OrdersPricing_PriceEditedInAdminPanel_IsVisibleImmediately_AndInvalidatesETag()
    {
        await ApplyAsync();
        var (before, beforeBody) = await GetOrdersPricingAsync(); // fills the 60 s cache
        beforeBody.GetProperty("plans").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Лавка")
            .GetProperty("pricePerMonth").GetDecimal().Should().Be(690);

        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var current = (await OrdersPlansAsync()).Single(p => p.Id == OrdersTariffCatalog.LavkaId);
        var put = await admin.PutJsonAsync($"/api/admin/plans/{current.Id}", new
        {
            name = current.Name, description = current.Description, pricePerMonth = 750m,
            maxEmployees = current.MaxEmployees, maxCompanies = current.MaxCompanies,
            maxProductsPerShop = current.MaxProductsPerShop, maxOrdersPerMonth = current.MaxOrdersPerMonth,
            allowOrders = true, isPublic = true, isActive = true, line = "Orders",
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        var (after, afterBody) = await GetOrdersPricingAsync();
        afterBody.GetProperty("plans").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Лавка")
            .GetProperty("pricePerMonth").GetDecimal().Should().Be(750, "an admin edit must not wait for the 60 s cache");
        after.Headers.ETag!.ToString().Should().NotBe(before.Headers.ETag!.ToString());
    }
}
