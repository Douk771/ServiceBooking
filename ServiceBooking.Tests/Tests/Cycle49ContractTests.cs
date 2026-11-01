using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Контрактная проверка цикла 49: реальные ответы против contracts/cycle49/openapi.json (только форма, не бизнес-логика).
/// </summary>
public class Cycle49ContractTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private static readonly OpenApiContract C49 = OpenApiContract.Load("cycle49");

    private async Task<JsonElement> GetJsonAsync(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact, TestCase("CY49-CONTRACT")]
    public async Task SalonCatalogListing_BothWithoutAndWithService_ConformsToCycle49()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var url = $"/api/companies/{company.Id}/catalog-listing";

        var empty = await GetJsonAsync(AuthedClient(owner.Token), url);
        C49.AssertResponse("get", "/api/companies/{id}/catalog-listing", 200, empty);
        empty.TryGetProperty("conditionsText", out _).Should().BeTrue("conditionsText есть у салона");

        await CreateServiceAsync(owner.Token, company.Id);
        var full = await GetJsonAsync(AuthedClient(owner.Token), url);
        C49.AssertResponse("get", "/api/companies/{id}/catalog-listing", 200, full);

        var put = await AuthedClient(owner.Token).PutJsonAsync(url, new { showInCatalog = false });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        C49.AssertResponse("put", "/api/companies/{id}/catalog-listing", 200, await put.Content.ReadFromJsonAsync<JsonElement>());
    }

    [Fact, TestCase("CY49-CONTRACT")]
    public async Task ShopCatalogListing_ConformsAndHasNoConditionsText()
    {
        var shop = await CreateShopAsync();
        var body = await GetJsonAsync(AuthedClient(shop.OwnerToken), $"/api/shops/{shop.Shop.Id}/catalog-listing");
        C49.AssertResponse("get", "/api/shops/{shopId}/catalog-listing", 200, body);
        body.TryGetProperty("conditionsText", out var ct).Should().BeFalse("у магазина conditionsText не выводится, а пришло: " + ct);
    }

    [Fact, TestCase("CY49-CONTRACT")]
    public async Task CompanyLists_Conform()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await CreateServiceAsync(owner.Token, company.Id);

        C49.AssertResponse("get", "/api/companies", 200, await GetJsonAsync(AnonymousClient(), "/api/companies"));
        C49.AssertResponse("get", "/api/companies/public", 200, await GetJsonAsync(AnonymousClient(), "/api/companies/public?pageSize=100"));
        C49.AssertResponse("get", "/api/companies/my", 200, await GetJsonAsync(AuthedClient(owner.Token), "/api/companies/my"));
    }

    [Fact, TestCase("CY49-CONTRACT")]
    public async Task OwnerSubscription_AndPlanRequest_ConformToCycle49()
    {
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TariffCatalogSeeder>().ApplyAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedClient(owner.Token);
        var sub = await GetJsonAsync(client, "/api/billing/subscription");
        C49.AssertResponse("get", "/api/billing/subscription", 200, sub);

        var plans = sub.GetProperty("availablePlans");
        plans.ValueKind.Should().Be(JsonValueKind.Array, "для линии «Записи» availablePlans — список");
        plans.GetArrayLength().Should().BeGreaterThan(0);

        var planId = plans[0].GetProperty("planId").GetGuid();
        var post = await client.PostAsJsonAsync("/api/billing/subscription/request", new { planId, options = Array.Empty<object>(), comment = "контракт" });
        post.StatusCode.Should().Be(HttpStatusCode.OK, await post.Content.ReadAsStringAsync());
        C49.AssertResponse("post", "/api/billing/subscription/request", 200, await post.Content.ReadFromJsonAsync<JsonElement>());

        C49.AssertResponse("get", "/api/billing/subscription", 200, await GetJsonAsync(client, "/api/billing/subscription"));
    }
}
