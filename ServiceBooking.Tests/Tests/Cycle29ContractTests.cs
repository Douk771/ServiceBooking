using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 29: сверка реальных ответов с машиночитаемыми контрактами (API_CONTRACT_CYCLE29.md §29.26).
/// Только форма ответа; поведение проверяют остальные CY29-*.
/// </summary>
public class Cycle29ContractTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private static readonly OpenApiContract C26 = OpenApiContract.Load("cycle26");
    private static readonly OpenApiContract C29 = OpenApiContract.Load("cycle29");

    private static MultipartFormDataContent Upload(int w)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestImages.SolidJpeg(w, 40));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        return content;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r)
    {
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<(ShopCtx Shop, int City)> SeedAsync()
    {
        var city = await WithDbAsync(db => db.Cities.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Id).Skip(17).Select(c => c.Id).FirstAsync());
        var shop = await CreateShopAsync(openAllDay: false);
        await MoveShopToCityAsync(shop, city);
        await CreateProductAsync(shop);
        var now = ShopNow(shop);
        string F(DateTime t) => t.AddMinutes(-(t.Minute % 5)).ToString("HH:mm");
        await SetHoursAsync(shop, (F(now.AddMinutes(-180)), F(now.AddMinutes(180))));
        var client = AuthedClient(shop.OwnerToken);
        foreach (var w in new[] { 40, 41, 42 })
            (await client.PostAsync($"/api/companies/{shop.Id}/photos", Upload(w))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsync($"/api/companies/{shop.Id}/logo", Upload(50))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/companies/{shop.Id}", new { email = "shop@example.com" })).StatusCode.Should().Be(HttpStatusCode.OK);
        return (shop, city);
    }

    [Fact, TestCase("CY29-30")]
    public async Task Storefront_MatchesCycle26Contract()
    {
        var (shop, _) = await SeedAsync();
        var body = await JsonAsync(await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}"));
        // Цикл 40 дописал в customerNotifications messengerTransports и messengerLabel (contracts/cycle40/openapi.yaml, StorefrontCustomerNotificationsDto);
        // контракт cycle26 заморожен — принимаются только эти два добавления, всё остальное строго.
        var errors = C26.Collect("GET", "/api/storefront/{slug}", 200, body)
            .Where(e => !System.Text.RegularExpressions.Regex.IsMatch(e, @"^\$\.customerNotifications\.(messengerTransports|messengerLabel): property is not described by the schema$"))
            .ToList();
        errors.Should().BeEmpty("the response of GET /api/storefront/{slug} -> 200 must conform to the contract");
    }

    [Fact, TestCase("CY29-31")]
    public async Task PublicPhotos_MatchCycle26Contract()
    {
        var (shop, _) = await SeedAsync();
        var body = await JsonAsync(await AnonymousClient().GetAsync($"/api/companies/{shop.Id}/photos"));
        body.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        C26.AssertResponse("GET", "/api/companies/{id}/photos", 200, body);
    }

    [Fact, TestCase("CY29-32")]
    public async Task ShopManage_MatchesCycle26Contract()
    {
        var (shop, _) = await SeedAsync();
        var body = await JsonAsync(await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}"));
        C26.AssertResponse("GET", "/api/shops/{shopId}", 200, body);
    }

    [Fact, TestCase("CY29-33")]
    public async Task GoodsCatalog_MatchesCycle29Contract_WithAndWithoutLogo()
    {
        var (shop, city) = await SeedAsync();
        var noLogo = await CreateShopAsync(openAllDay: false);
        await MoveShopToCityAsync(noLogo, city);
        await CreateProductAsync(noLogo);
        var now = ShopNow(noLogo);
        string F(DateTime t) => t.AddMinutes(-(t.Minute % 5)).ToString("HH:mm");
        await SetHoursAsync(noLogo, (F(now.AddMinutes(-180)), F(now.AddMinutes(180))));

        var host = Fixture.ClassHost("catalog", cs => new CatalogTestFactory(cs));
        var body = await JsonAsync(await host.CreateClient().GetAsync($"/api/goods/catalog?cityId={city}"));
        C29.AssertResponse("GET", "/api/goods/catalog", 200, body);

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Single(i => i.GetProperty("slug").GetString() == shop.Slug).GetProperty("logoUrl").ValueKind.Should().Be(JsonValueKind.String);
        var bare = items.Single(i => i.GetProperty("slug").GetString() == noLogo.Slug);
        bare.TryGetProperty("logoUrl", out var v).Should().BeTrue("ключ logoUrl обязателен");
        v.ValueKind.Should().Be(JsonValueKind.Null);
    }
}
