using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 49: тарифы «Записи» на /billing (US-49-06, ARCHITECTURE_CYCLE49 §49.6.1, CY49-B6/B7). Пишется по SPEC/архитектуре, не по реализации.
/// База общая на класс-фикстуру, поэтому проверяются только свои тарифы (по уникальному префиксу имени), а не весь список.
/// </summary>
public class Cycle49BillingPlansTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private static readonly OpenApiContract C49 = OpenApiContract.Load("cycle49");

    private async Task<Guid> InsertPlanAsync(
        string name, CompanyKind line, decimal price, int sortOrder = 0, bool isActive = true, bool isPublic = true,
        int? maxCompanies = 3, int? maxEmployees = 10, string? highlights = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var plan = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(), Name = name, PricePerMonth = price, Line = line, SortOrder = sortOrder,
            IsActive = isActive, IsPublic = isPublic, MaxCompanies = maxCompanies, MaxEmployees = maxEmployees,
            Description = "описание " + name, Highlights = highlights,
        };
        db.SubscriptionPlanConfigs.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private async Task<Guid> InsertOptionAsync(decimal price)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var option = new SubscriptionOption
        {
            Id = Guid.NewGuid(), Code = Unique("cy49-opt-"), Name = Unique("Опция CY49 "), Kind = OptionKind.Toggle,
            PricePerMonth = price, IsActive = true, IsPublic = true,
        };
        db.SubscriptionOptions.Add(option);
        await db.SaveChangesAsync();
        return option.Id;
    }

    private async Task<JsonElement> GetSubAsync(HttpClient c, string? line = null)
    {
        var r = await c.GetAsync("/api/billing/subscription" + (line is null ? "" : "?line=" + line));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<JsonElement> Plans(JsonElement sub) => sub.GetProperty("availablePlans").EnumerateArray().ToList();

    // ── CY49-B6 ────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY49-B6")]
    public async Task ServicesAvailablePlans_FilteredOrdered_WithLimitsText_NoOtherLineFields()
    {
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Showcase.Tariffs.TariffCatalogSeeder>().ApplyAsync();
        var p = Unique("B6 ");
        var cheapLate = await InsertPlanAsync(p + "cheap-late", CompanyKind.Services, 100m, sortOrder: 5000, maxCompanies: 1, maxEmployees: null);
        var priceTie = await InsertPlanAsync(p + "tie-expensive", CompanyKind.Services, 900m, sortOrder: 4000, maxCompanies: 2, maxEmployees: 21);
        var priceTieCheap = await InsertPlanAsync(p + "tie-cheap", CompanyKind.Services, 300m, sortOrder: 4000, maxCompanies: 5, maxEmployees: 11,
            highlights: "первый\nвторой");
        var first = await InsertPlanAsync(p + "first", CompanyKind.Services, 700m, sortOrder: 3000, maxCompanies: null, maxEmployees: 1);
        var inactive = await InsertPlanAsync(p + "inactive", CompanyKind.Services, 50m, isActive: false);
        var hidden = await InsertPlanAsync(p + "hidden", CompanyKind.Services, 60m, isPublic: false);
        var orders = await InsertPlanAsync(p + "orders", CompanyKind.Orders, 70m);
        var stays = await InsertPlanAsync(p + "stays", CompanyKind.Stays, 80m);
        var baths = await InsertPlanAsync(p + "baths", CompanyKind.Baths, 90m);

        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedClient(owner.Token);
        var sub = await GetSubAsync(client);
        C49.AssertResponse("get", "/api/billing/subscription", 200, sub);
        sub.GetProperty("line").GetString().Should().Be("Services");

        var all = Plans(sub);
        var mine = all.Where(x => x.GetProperty("name").GetString()!.StartsWith(p)).ToList();
        mine.Select(x => x.GetProperty("planId").GetGuid()).Should().Equal(
            [first, priceTieCheap, priceTie, cheapLate], "порядок SortOrder, затем цена; неактивный, непубличный и чужих линий нет");

        // Системные тарифы «пробный» и «бесплатный» в список не попадают.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var system = await db.SubscriptionPlanConfigs.AsNoTracking().Where(x => x.IsSystemTrial || x.IsSystemFree).Select(x => x.Id).ToListAsync();
            system.Should().NotBeEmpty("сидер заводит пробный и бесплатный тарифы");
            all.Select(x => x.GetProperty("planId").GetGuid()).Should().NotContain(system);
            var wrong = new[] { inactive, hidden, orders, stays, baths };
            all.Select(x => x.GetProperty("planId").GetGuid()).Should().NotContain(wrong);
            // Всё, что показано, действительно принадлежит «Записи», активно и публично.
            var ids = all.Select(x => x.GetProperty("planId").GetGuid()).ToList();
            var rows = await db.SubscriptionPlanConfigs.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync();
            rows.Should().OnlyContain(x => x.Line == CompanyKind.Services && x.IsActive && x.IsPublic && !x.IsSystemTrial && !x.IsSystemFree);
            // Общий порядок списка — SortOrder, затем цена.
            var bySort = rows.ToDictionary(x => x.Id);
            var sequence = ids.Select(id => (bySort[id].SortOrder, bySort[id].PricePerMonth)).ToList();
            sequence.Should().BeInAscendingOrder();
        }

        // limitsText и поля плана.
        var byId = mine.ToDictionary(x => x.GetProperty("planId").GetGuid());
        byId[cheapLate].GetProperty("limitsText").GetString().Should().Be("до 1 точки · сотрудники без ограничения");
        byId[priceTie].GetProperty("limitsText").GetString().Should().Be("до 2 точек · до 21 сотрудника");
        byId[priceTieCheap].GetProperty("limitsText").GetString().Should().Be("до 5 точек · до 11 сотрудников");
        byId[first].GetProperty("limitsText").GetString().Should().Be("точки без ограничения · до 1 сотрудника");
        byId[priceTieCheap].GetProperty("pricePerMonth").GetDecimal().Should().Be(300m);
        byId[priceTieCheap].GetProperty("highlights").EnumerateArray().Select(h => h.GetString()).Should().Equal("первый", "второй");
        foreach (var plan in mine)
        {
            plan.TryGetProperty("maxHouses", out _).Should().BeFalse("maxHouses только у линии «Дома»");
            plan.TryGetProperty("maxResources", out _).Should().BeFalse("maxResources только у линии «Бани»");
        }
    }

    [Fact, TestCase("CY49-B6")]
    public async Task OtherLines_AvailablePlans_StayTheirOwn_AndDoNotContainServicesPlans()
    {
        var p = Unique("B6o ");
        var services = await InsertPlanAsync(p + "services", CompanyKind.Services, 111m);
        var orders = await InsertPlanAsync(p + "orders", CompanyKind.Orders, 222m);
        var stays = await InsertPlanAsync(p + "stays", CompanyKind.Stays, 333m);
        var baths = await InsertPlanAsync(p + "baths", CompanyKind.Baths, 444m);

        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedClient(owner.Token);
        var expected = new Dictionary<string, Guid> { ["Orders"] = orders, ["Stays"] = stays, ["Baths"] = baths };
        foreach (var (line, own) in expected)
        {
            var sub = await GetSubAsync(client, line);
            sub.GetProperty("line").GetString().Should().Be(line);
            var ids = Plans(sub).Select(x => x.GetProperty("planId").GetGuid()).ToList();
            ids.Should().Contain(own, $"в списке линии {line} есть её собственный тариф");
            ids.Should().NotContain(services, $"тариф «Записи» не должен попасть в список линии {line}");
            ids.Should().NotContain(expected.Where(e => e.Key != line).Select(e => e.Value));
        }
    }

    // ── CY49-B7 ────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY49-B7")]
    public async Task ServicesPlanRequest_EstimateUsesRequestedPlanPricePlusOptions()
    {
        var p = Unique("B7 ");
        var requested = await InsertPlanAsync(p + "requested", CompanyKind.Services, 1234m);
        var other = await InsertPlanAsync(p + "other", CompanyKind.Services, 5678m);
        var option = await InsertOptionAsync(150m);

        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedClient(owner.Token);
        var before = await GetSubAsync(client);
        before.GetProperty("pendingRequest").ValueKind.Should().Be(JsonValueKind.Null);
        var currentPrice = before.GetProperty("plan").GetProperty("pricePerMonth").GetDecimal();
        currentPrice.Should().NotBe(1234m, "цена текущего тарифа отличается от запрошенного — иначе регрессия к ней не заметна");

        var post = await client.PostAsJsonAsync("/api/billing/subscription/request",
            new { planId = requested, options = new[] { new { optionId = option, quantity = 1 } }, comment = "CY49-B7" });
        post.StatusCode.Should().Be(HttpStatusCode.OK, await post.Content.ReadAsStringAsync());
        var postBody = await post.Content.ReadFromJsonAsync<JsonElement>();
        C49.AssertResponse("post", "/api/billing/subscription/request", 200, postBody);
        postBody.GetProperty("desiredPlanId").GetGuid().Should().Be(requested);
        postBody.GetProperty("line").GetString().Should().Be("Services");

        var after = await GetSubAsync(client);
        C49.AssertResponse("get", "/api/billing/subscription", 200, after);
        var pending = after.GetProperty("pendingRequest");
        pending.GetProperty("desiredPlanId").GetGuid().Should().Be(requested);
        pending.GetProperty("line").GetString().Should().Be("Services");
        pending.GetProperty("estimatedMonthlyPrice").GetDecimal().Should().Be(1234m + 150m, "цена ЗАПРОШЕННОГО тарифа + опции заявки, а не текущего");

        // Повторная заявка перезаписывает прежнюю: оценка следует за новым запрошенным тарифом.
        var again = await client.PostAsJsonAsync("/api/billing/subscription/request", new { planId = other, options = Array.Empty<object>() });
        again.StatusCode.Should().Be(HttpStatusCode.OK, await again.Content.ReadAsStringAsync());
        var pending2 = (await GetSubAsync(client)).GetProperty("pendingRequest");
        pending2.GetProperty("desiredPlanId").GetGuid().Should().Be(other);
        pending2.GetProperty("estimatedMonthlyPrice").GetDecimal().Should().Be(5678m);
    }

    [Fact, TestCase("CY49-B7")]
    public async Task OptionsOnlyServicesRequest_EstimateUsesCurrentPlanPrice()
    {
        var option = await InsertOptionAsync(150m);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedClient(owner.Token);
        var currentPrice = (await GetSubAsync(client)).GetProperty("plan").GetProperty("pricePerMonth").GetDecimal();

        var post = await client.PostAsJsonAsync("/api/billing/subscription/request",
            new { options = new[] { new { optionId = option, quantity = 1 } } });
        post.StatusCode.Should().Be(HttpStatusCode.OK, await post.Content.ReadAsStringAsync());
        var pending = (await GetSubAsync(client)).GetProperty("pendingRequest");
        pending.GetProperty("desiredPlanId").ValueKind.Should().Be(JsonValueKind.Null);
        pending.GetProperty("estimatedMonthlyPrice").GetDecimal().Should().Be(currentPrice + 150m);
    }

    [Fact, TestCase("CY49-B7")]
    public async Task ServicesRequest_WithOrdersPlanAndNoLine_Is400DifferentLine()
    {
        var ordersPlan = await InsertPlanAsync(Unique("B7 orders "), CompanyKind.Orders, 300m);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedClient(owner.Token);

        var r = await client.PostAsJsonAsync("/api/billing/subscription/request", new { planId = ordersPlan, options = Array.Empty<object>() });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("Этот тариф из другой линейки");
        (await GetSubAsync(client)).GetProperty("pendingRequest").ValueKind.Should().Be(JsonValueKind.Null, "отклонённая заявка ничего не сохраняет");
    }

    [Fact, TestCase("CY49-B7")]
    public async Task ServicesRequest_WhileOrdersRequestOpen_Is409PlainText()
    {
        var ordersPlan = await InsertPlanAsync(Unique("B7 orders-open "), CompanyKind.Orders, 300m);
        var servicesPlan = await InsertPlanAsync(Unique("B7 services-blocked "), CompanyKind.Services, 400m);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedClient(owner.Token);

        var open = await client.PostAsJsonAsync("/api/billing/subscription/request", new { planId = ordersPlan, line = "Orders", options = Array.Empty<object>() });
        open.StatusCode.Should().Be(HttpStatusCode.OK, await open.Content.ReadAsStringAsync());

        var r = await client.PostAsJsonAsync("/api/billing/subscription/request", new { planId = servicesPlan, options = Array.Empty<object>() });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        r.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        (await r.Content.ReadAsStringAsync()).Should().Contain("У вас уже есть заявка на смену тарифа");

        // Открытая заявка «Заказов» на экране «Записи» не показывается как заявка «Записи».
        var pending = (await GetSubAsync(client)).GetProperty("pendingRequest");
        if (pending.ValueKind != JsonValueKind.Null)
            pending.GetProperty("line").GetString().Should().NotBe("Services");
    }
}
