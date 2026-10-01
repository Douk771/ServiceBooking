using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 29, «Вызов 2»: US-29-04 (битая форма — 400), US-29-02 (logoUrl в каталоге goods), T-29-07 (галерея магазина на бесплатном тарифе «Заказы»).
/// Написано по SPEC.md и API_CONTRACT_CYCLE29.md, а не по реализации.
/// </summary>
public class Cycle29QaTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private const string MalformedText = "Не удалось прочитать данные формы. Проверьте имена полей запроса.";

    private static MultipartFormDataContent Upload(int w = 40, string fieldName = "file", string? extraField = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestImages.SolidJpeg(w, 40));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, fieldName, "photo.jpg");
        if (extraField is not null) content.Add(new StringContent("1"), extraField);
        return content;
    }

    private int PublicFileCount()
    {
        var root = Factory.Services.GetRequiredService<FileStorage>().PublicRootFullPath;
        return Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length : 0;
    }

    private static async Task AssertMalformedAsync(HttpResponseMessage r)
    {
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        r.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        var text = await r.Content.ReadAsStringAsync();
        text.Should().Be(MalformedText);
        text.Should().NotContain("Exception").And.NotContain("JQuery");
    }

    private async Task<int> GalleryCountAsync(Guid id) =>
        (await AnonymousClient().GetFromJsonAsync<List<JsonElement>>($"/api/companies/{id}/photos"))!.Count;

    [Fact, TestCase("CY29-01")]
    public async Task Gallery_MalformedFieldName_400_NothingSaved()
    {
        var shop = await CreateShopAsync();
        var before = PublicFileCount();
        var r = await AuthedClient(shop.OwnerToken).PostAsync($"/api/companies/{shop.Id}/photos", Upload(extraField: "file["));
        await AssertMalformedAsync(r);
        (await GalleryCountAsync(shop.Id)).Should().Be(0);
        PublicFileCount().Should().Be(before);
    }

    [Fact, TestCase("CY29-02")]
    public async Task Logo_MalformedFieldName_400_LogoUnchanged()
    {
        var shop = await CreateShopAsync();
        var client = AuthedClient(shop.OwnerToken);
        (await client.PostAsync($"/api/companies/{shop.Id}/logo", Upload(50))).StatusCode.Should().Be(HttpStatusCode.OK);
        var logoBefore = await WithDbAsync(db => db.Companies.AsNoTracking().Where(c => c.Id == shop.Id).Select(c => c.LogoUrl).SingleAsync());
        logoBefore.Should().NotBeNullOrEmpty();
        var files = PublicFileCount();

        await AssertMalformedAsync(await client.PostAsync($"/api/companies/{shop.Id}/logo", Upload(60, extraField: "file[")));

        (await WithDbAsync(db => db.Companies.AsNoTracking().Where(c => c.Id == shop.Id).Select(c => c.LogoUrl).SingleAsync())).Should().Be(logoBefore);
        PublicFileCount().Should().Be(files);
    }

    [Fact, TestCase("CY29-03")]
    public async Task RouteWithoutFiles_MalformedForm_400_NothingChanged()
    {
        var shop = await CreateShopAsync();
        var client = AuthedClient(shop.OwnerToken);
        (await client.PostAsync($"/api/companies/{shop.Id}/photos", Upload(45))).StatusCode.Should().Be(HttpStatusCode.Created);
        var photo = (await AnonymousClient().GetFromJsonAsync<List<JsonElement>>($"/api/companies/{shop.Id}/photos"))![0].GetProperty("id").GetGuid();

        var form = new MultipartFormDataContent { { new StringContent("1"), "reason[" } };
        var r = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/companies/{shop.Id}/photos/{photo}") { Content = form });
        await AssertMalformedAsync(r);
        (await GalleryCountAsync(shop.Id)).Should().Be(1, "действие не должно было выполниться");

        // JSON-маршрут, которому прислали битую форму: 400 (по контракту §29.23.4) или 415 (не JSON), но не 500 и ничего не меняется
        var put = await client.PutAsync($"/api/companies/{shop.Id}", new MultipartFormDataContent { { new StringContent("Hacked"), "name[" } });
        ((int)put.StatusCode).Should().BeOneOf(400, 415);
        (await WithDbAsync(db => db.Companies.AsNoTracking().Where(c => c.Id == shop.Id).Select(c => c.Name).SingleAsync())).Should().Be(shop.Shop.Name);
    }

    [Fact, TestCase("CY29-04")]
    public async Task Anonymous_MalformedForm_401()
    {
        var shop = await CreateShopAsync();
        var r = await AnonymousClient().PostAsync($"/api/companies/{shop.Id}/photos", Upload(extraField: "file["));
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("CY29-05")]
    public async Task ForeignOrUnknownCompany_MalformedForm_SameBody_NotOracle()
    {
        var mine = await CreateShopAsync();
        var other = await CreateShopAsync();
        var client = AuthedClient(mine.OwnerToken);
        var a = await client.PostAsync($"/api/companies/{other.Id}/photos", Upload(extraField: "file["));
        var b = await client.PostAsync($"/api/companies/{Guid.NewGuid()}/photos", Upload(extraField: "file["));
        await AssertMalformedAsync(a);
        await AssertMalformedAsync(b);
        (await GalleryCountAsync(other.Id)).Should().Be(0);
    }

    [Fact, TestCase("CY29-07")]
    public async Task ValidBracketSyntax_StillWorks_201()
    {
        var shop = await CreateShopAsync();
        var r = await AuthedClient(shop.OwnerToken).PostAsync($"/api/companies/{shop.Id}/photos", Upload(extraField: "meta[x]"));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        (await GalleryCountAsync(shop.Id)).Should().Be(1);
    }

    [Fact, TestCase("CY29-08")]
    public async Task OtherUploadRoutes_MalformedForm_400()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var client = AuthedClient(owner.Token);
        await AssertMalformedAsync(await client.PostAsync("/api/profile/avatar", Upload(extraField: "file[")));
        var svc = await CreateServiceAsync(owner.Token, company.Id);
        var r = await client.PostAsync($"/api/companies/{company.Id}/services/{svc.Id}/photo", Upload(extraField: "file["));
        if (r.StatusCode != HttpStatusCode.NotFound) await AssertMalformedAsync(r);
    }

    [Fact, TestCase("CY29-10")]
    public async Task Catalog_LogoUrl_AppearsAfterUpload_AndMatchesStorefront()
    {
        var city = await WithDbAsync(db => db.Cities.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Id).Skip(20).Select(c => c.Id).FirstAsync());
        var shop = await CreateShopAsync(openAllDay: false);
        await MoveShopToCityAsync(shop, city);
        await CreateProductAsync(shop);
        var now = ShopNow(shop);
        string F(DateTime t) => t.AddMinutes(-(t.Minute % 5)).ToString("HH:mm");
        await SetHoursAsync(shop, (F(now.AddMinutes(-180)), F(now.AddMinutes(180))));

        var host = Fixture.ClassHost("catalog", cs => new CatalogTestFactory(cs));
        var bare = await CatalogItemAsync(host, city, shop.Slug);
        bare.TryGetProperty("logoUrl", out var v).Should().BeTrue();
        v.ValueKind.Should().Be(JsonValueKind.Null);

        (await AuthedClient(shop.OwnerToken).PostAsync($"/api/companies/{shop.Id}/logo", Upload(50))).StatusCode.Should().Be(HttpStatusCode.OK);
        var item = await CatalogItemAsync(host, city, shop.Slug);
        var logo = item.GetProperty("logoUrl").GetString();
        logo.Should().NotBeNullOrEmpty();
        var sf = JsonDocument.Parse(await AnonymousClient().GetStringAsync($"/api/storefront/{shop.Slug}")).RootElement;
        sf.GetProperty("logoUrl").GetString().Should().Be(logo);
        var manage = JsonDocument.Parse(await AuthedClient(shop.OwnerToken).GetStringAsync($"/api/shops/{shop.Id}")).RootElement;
        manage.GetProperty("logoUrl").GetString().Should().Be(logo);
    }

    private static async Task<JsonElement> CatalogItemAsync(CatalogTestFactory host, int city, string slug)
    {
        var body = JsonDocument.Parse(await host.CreateClient().GetStringAsync($"/api/goods/catalog?cityId={city}")).RootElement;
        return body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("slug").GetString() == slug).Clone();
    }

    [Fact, TestCase("CY29-20")]
    public async Task ShopGallery_FreeOrdersPlan_10Accepted_11thRefusedByGalleryLimit()
    {
        var shop = await CreateShopAsync(paidPlan: false);
        var client = AuthedClient(shop.OwnerToken);
        for (var i = 0; i < 10; i++)
        {
            var r = await client.PostAsync($"/api/companies/{shop.Id}/photos", Upload(40 + i));
            r.StatusCode.Should().Be(HttpStatusCode.Created, $"фото #{i + 1}: {await r.Content.ReadAsStringAsync()}");
        }
        var eleventh = await client.PostAsync($"/api/companies/{shop.Id}/photos", Upload(90));
        eleventh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await eleventh.Content.ReadAsStringAsync()).Should().Contain("В галерее магазина может быть не больше 10 фотографий");
        (await GalleryCountAsync(shop.Id)).Should().Be(10);
    }
}
