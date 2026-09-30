using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 31, «Вызов 2»: US-31-02 — пакет из 10 фото не упирается в лимит частоты, правка галереи и логотип живут в своих окнах.
/// Хост с ПРОДОВЫМИ значениями лимитов (10 / 20 / 60): обычный тестовый хост поднимает их до 10000.
/// Написано по SPEC.md и API_CONTRACT_CYCLE31.md §31.25.
/// </summary>
public class Cycle31GalleryRateLimitTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private sealed class ProdLimitsHost(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestHostSettings.Apply(builder, "api", connectionString, factoryType: GetType().Name);
            builder.UseSetting("Uploads:PerUserPerMinute", "10");
            builder.UseSetting("RateLimits:company-photos:PermitLimit", "20");
            builder.UseSetting("RateLimits:company-photos-edit:PermitLimit", "60");
        }
    }

    private static MultipartFormDataContent Upload(int variant)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestImages.SolidJpeg(30 + variant, 40));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", $"p{variant}.jpg");
        return content;
    }

    private static Task<HttpResponseMessage> Post(HttpClient c, Guid id, int variant) =>
        c.PostAsync($"/api/companies/{id}/photos", Upload(variant));

    private static async Task<List<CompanyPhotoDto>> FillAsync(HttpClient c, Guid id, int count, int from = 0)
    {
        var list = new List<CompanyPhotoDto>();
        for (var i = from; i < from + count; i++)
        {
            var r = await Post(c, id, i);
            r.StatusCode.Should().Be(HttpStatusCode.Created, $"фото {i}: " + await r.Content.ReadAsStringAsync());
            list.Add((await r.Content.ReadJsonAsync<CompanyPhotoDto>())!);
        }
        return list;
    }

    [Fact, TestCase("CY31-20")]
    public async Task TenPhotos_ThenCoverAndDelete_NoneRateLimited()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await using var host = new ProdLimitsHost(ConnectionString);
        var c = ClientOn(host, owner.Token);
        var photos = await FillAsync(c, company.Id, 10);

        var order = photos.Select(p => p.Id).Reverse().ToArray();
        var put = await c.PutAsJsonAsync($"/api/companies/{company.Id}/photos/order", new { photoIds = order });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var del = await c.DeleteAsync($"/api/companies/{company.Id}/photos/{photos[3].Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent, await del.Content.ReadAsStringAsync());
    }

    [Fact, TestCase("CY31-21")]
    public async Task TenPhotos_ThenLogoUpload_StillOk_UploadsWindowUntouched()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await using var host = new ProdLimitsHost(ConnectionString);
        var c = ClientOn(host, owner.Token);
        await FillAsync(c, company.Id, 10);
        var r = await c.PostAsync($"/api/companies/{company.Id}/logo", Upload(1));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    [Fact, TestCase("CY31-22")]
    public async Task Gallery_21stUploadInWindow_429_WithContractText()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await using var host = new ProdLimitsHost(ConnectionString);
        var c = ClientOn(host, owner.Token);
        // 10 разных + 10 повторов (200 по дедупликации) = 20 запросов, все проходят лимит
        await FillAsync(c, company.Id, 10);
        for (var i = 0; i < 10; i++) (await Post(c, company.Id, i)).StatusCode.Should().Be(HttpStatusCode.OK, $"повтор {i}");
        var limited = await Post(c, company.Id, 0);
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await limited.Content.ReadAsStringAsync()).Should().Contain("Too many uploads. Try again in a minute.");
    }

    [Fact, TestCase("CY31-23")]
    public async Task Edits_61stInWindow_429_LogoUploadsWindowStillLive_ItsOwn11thIs429()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await using var host = new ProdLimitsHost(ConnectionString);
        var c = ClientOn(host, owner.Token);
        var photos = await FillAsync(c, company.Id, 2);
        var ids = photos.Select(p => p.Id).ToArray();
        for (var i = 0; i < 60; i++)
        {
            var r = await c.PutAsJsonAsync($"/api/companies/{company.Id}/photos/order", new { photoIds = ids });
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"правка {i + 1} из 60");
        }
        var limited = await c.PutAsJsonAsync($"/api/companies/{company.Id}/photos/order", new { photoIds = ids });
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await limited.Content.ReadAsStringAsync()).Should().Contain("Too many uploads");

        // uploads не тронут правками и загрузкой галереи; свой лимит 10 жив
        for (var i = 0; i < 10; i++)
            (await c.PostAsync($"/api/companies/{company.Id}/logo", Upload(i))).StatusCode.Should().Be(HttpStatusCode.OK, $"логотип {i + 1}");
        (await c.PostAsync($"/api/companies/{company.Id}/logo", Upload(11))).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact, TestCase("CY31-24")]
    public async Task SequentialUploads_PositionsFollowSendOrder_CoverUnchangedOnAppend()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var c = AuthedClient(owner.Token);
        var first = await FillAsync(c, company.Id, 1);
        first[0].IsCover.Should().BeTrue();
        var more = await FillAsync(c, company.Id, 3, from: 1);
        more.Select(p => p.Position).Should().Equal(1, 2, 3);
        more.Should().OnlyContain(p => !p.IsCover);
        var gallery = (await AnonymousClient().GetFromJsonAsync<List<CompanyPhotoDto>>($"/api/companies/{company.Id}/photos"))!;
        gallery.OrderBy(p => p.Position).Select(p => p.Id).Should().Equal(first.Concat(more).Select(p => p.Id));
        gallery.Single(p => p.IsCover).Id.Should().Be(first[0].Id);
    }

    [Fact, TestCase("CY31-25")]
    public async Task ShopGallery_TenPhotosThenCover_NoRateLimit()
    {
        var shop = await CreateShopAsync();
        await using var host = new ProdLimitsHost(ConnectionString);
        var c = ClientOn(host, shop.OwnerToken);
        var photos = await FillAsync(c, shop.Id, 10);
        var order = photos.Select(p => p.Id).ToArray();
        (order[0], order[1]) = (order[1], order[0]);
        var put = await c.PutAsJsonAsync($"/api/companies/{shop.Id}/photos/order", new { photoIds = order });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var gallery = (await AnonymousClient().GetFromJsonAsync<List<CompanyPhotoDto>>($"/api/companies/{shop.Id}/photos"))!;
        gallery.Single(p => p.IsCover).Id.Should().Be(order[0]);
    }
}
