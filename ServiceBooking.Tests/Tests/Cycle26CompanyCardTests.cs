using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 26, «Вызов 2»: галерея фото магазина, витрина (email + photos), профиль магазина (ссылки карт, город и часовой пояс).
/// Написано по SPEC.md и API_CONTRACT_CYCLE26.md §558–§563, §566, §568, а не по реализации.
/// </summary>
public class Cycle26CompanyCardTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private const string YandexUrl = "https://yandex.ru/maps/org/syrovarnya/11766054863/?ll=83.795110%2C53.330510&z=17";
    private const string TwoGisUrl = "https://2gis.ru/barnaul/firm/563478234628539/83.795014%2C53.330486?m=83.795954%2C53.330025%2F17.89";

    private static MultipartFormDataContent Upload(byte[]? bytes = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? TestImages.SolidJpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        return content;
    }

    private async Task<CompanyPhotoDto> UploadOkAsync(string token, Guid id, int variant = 0)
    {
        var r = await AuthedClient(token).PostAsync($"/api/companies/{id}/photos", Upload(TestImages.SolidJpeg(40 + variant, 40)));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<CompanyPhotoDto>())!;
    }

    private async Task<List<CompanyPhotoDto>> GalleryAsync(Guid id) =>
        (await AnonymousClient().GetFromJsonAsync<List<CompanyPhotoDto>>($"/api/companies/{id}/photos"))!;

    private async Task<ShopManageDto> ManageAsync(ShopCtx shop)
    {
        var r = await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadJsonAsync<ShopManageDto>())!;
    }

    private Task<HttpResponseMessage> PutCompanyAsync(ShopCtx shop, object body) =>
        AuthedClient(shop.OwnerToken).PutAsJsonAsync($"/api/companies/{shop.Id}", body);

    private static int Offset(string tz) => (int)TimeZoneInfo.FindSystemTimeZoneById(tz).GetUtcOffset(DateTime.UtcNow).TotalMinutes;

    /// <summary>(city, tz) активных городов справочника.</summary>
    private async Task<List<City>> CitiesAsync() =>
        await WithDbAsync(db => db.Cities.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Id).ToListAsync());

    private async Task SetShopCityAsync(ShopCtx shop, City city) =>
        await WithDbAsync(async db =>
        {
            var c = await db.Companies.SingleAsync(x => x.Id == shop.Id);
            c.CityId = city.Id; c.TimeZoneId = city.TimeZoneId; c.TimeZoneIsManual = false;
            await db.SaveChangesAsync();
            return 0;
        });

    private async Task PlaceOrderForAsync(ShopCtx shop)
    {
        var p = await CreateProductAsync(shop);
        await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
    }

    // ── US-26-01 / T-26-01: галерея магазина ─────────────────────────────────────

    [Fact, TestCase("CY26-01")]
    public async Task ShopOwner_UploadsPhoto_ItIsCover_VisibleInPublicGallery()
    {
        var shop = await CreateShopAsync();
        var photo = await UploadOkAsync(shop.OwnerToken, shop.Id);
        photo.IsCover.Should().BeTrue();
        photo.Position.Should().Be(0);
        (await GalleryAsync(shop.Id)).Should().ContainSingle(p => p.Id == photo.Id);

        var second = await UploadOkAsync(shop.OwnerToken, shop.Id, 1);
        second.IsCover.Should().BeFalse("новое фото само обложкой не становится");
    }

    [Fact, TestCase("CY26-02")]
    public async Task ShopOwner_SameFileTwice_NoDuplicate()
    {
        var shop = await CreateShopAsync();
        var bytes = TestImages.SolidJpeg(50, 50);
        var a = await AuthedClient(shop.OwnerToken).PostAsync($"/api/companies/{shop.Id}/photos", Upload(bytes));
        a.StatusCode.Should().Be(HttpStatusCode.Created);
        var b = await AuthedClient(shop.OwnerToken).PostAsync($"/api/companies/{shop.Id}/photos", Upload(bytes));
        b.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GalleryAsync(shop.Id)).Should().HaveCount(1);
    }

    [Fact, TestCase("CY26-03")]
    public async Task ShopGallery_11thPhoto_400_WithShopText_NotSalonText()
    {
        var shop = await CreateShopAsync();
        for (var i = 0; i < 10; i++) await UploadOkAsync(shop.OwnerToken, shop.Id, i);
        var r = await AuthedClient(shop.OwnerToken).PostAsync($"/api/companies/{shop.Id}/photos", Upload(TestImages.SolidJpeg(90, 40)));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await r.Content.ReadAsStringAsync();
        text.Should().Contain("магазина").And.NotContain("салона");
        (await GalleryAsync(shop.Id)).Should().HaveCount(10);
    }

    [Fact, TestCase("CY26-04")]
    public async Task SalonGallery_11thPhoto_TextUnchanged_Salon()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        for (var i = 0; i < 10; i++) await UploadOkAsync(owner.Token, company.Id, i);
        var r = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", Upload(TestImages.SolidJpeg(90, 40)));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("В галерее салона может быть не больше 10 фотографий");
    }

    [Fact, TestCase("CY26-05")]
    public async Task ShopGallery_Reorder_ChangesCover_IncompleteAndForeignIds_400WithShopText()
    {
        var shop = await CreateShopAsync();
        var a = await UploadOkAsync(shop.OwnerToken, shop.Id, 0);
        var b = await UploadOkAsync(shop.OwnerToken, shop.Id, 1);
        var c = await UploadOkAsync(shop.OwnerToken, shop.Id, 2);
        var client = AuthedClient(shop.OwnerToken);

        var incomplete = await client.PutAsJsonAsync($"/api/companies/{shop.Id}/photos/order", new { photoIds = new[] { a.Id, b.Id } });
        incomplete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await incomplete.Content.ReadAsStringAsync()).Should().Contain("магазина").And.NotContain("салона");

        var foreign = await client.PutAsJsonAsync($"/api/companies/{shop.Id}/photos/order", new { photoIds = new[] { a.Id, b.Id, Guid.NewGuid() } });
        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var dup = await client.PutAsJsonAsync($"/api/companies/{shop.Id}/photos/order", new { photoIds = new[] { a.Id, a.Id, b.Id } });
        dup.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await client.PutAsJsonAsync($"/api/companies/{shop.Id}/photos/order", new { photoIds = new[] { c.Id, a.Id, b.Id } });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var gallery = await GalleryAsync(shop.Id);
        gallery.Select(p => p.Id).Should().Equal(c.Id, a.Id, b.Id);
        gallery[0].IsCover.Should().BeTrue();
    }

    [Fact, TestCase("CY26-06")]
    public async Task ShopGallery_Delete_RecomputesPositions()
    {
        var shop = await CreateShopAsync();
        var a = await UploadOkAsync(shop.OwnerToken, shop.Id, 0);
        var b = await UploadOkAsync(shop.OwnerToken, shop.Id, 1);
        var c = await UploadOkAsync(shop.OwnerToken, shop.Id, 2);
        (await AuthedClient(shop.OwnerToken).DeleteAsync($"/api/companies/{shop.Id}/photos/{a.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var gallery = await GalleryAsync(shop.Id);
        gallery.Select(p => (p.Id, p.Position)).Should().Equal((b.Id, 0), (c.Id, 1));
        gallery[0].IsCover.Should().BeTrue();
        // повторное удаление — 404, а не 500
        (await AuthedClient(shop.OwnerToken).DeleteAsync($"/api/companies/{shop.Id}/photos/{a.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY26-07")]
    public async Task ShopGallery_Rights_StaffAndForeignOwnerAndAnonymous_Refused()
    {
        var shop = await CreateShopAsync();
        var photo = await UploadOkAsync(shop.OwnerToken, shop.Id);
        var staff = await AddShopStaffAsync(shop);
        var stranger = await CreateShopAsync();

        foreach (var token in new[] { staff.Token, stranger.OwnerToken })
        {
            var c = AuthedClient(token);
            (await c.PostAsync($"/api/companies/{shop.Id}/photos", Upload(TestImages.SolidJpeg(70, 70)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await c.DeleteAsync($"/api/companies/{shop.Id}/photos/{photo.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await c.PutAsJsonAsync($"/api/companies/{shop.Id}/photos/order", new { photoIds = new[] { photo.Id } })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var anon = AnonymousClient();
        (await anon.PostAsync($"/api/companies/{shop.Id}/photos", Upload())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.DeleteAsync($"/api/companies/{shop.Id}/photos/{photo.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GalleryAsync(shop.Id)).Should().ContainSingle();
    }

    [Fact, TestCase("CY26-08")]
    public async Task ShopGallery_Upload_BadInputs_Refused_NothingSaved()
    {
        var shop = await CreateShopAsync();
        var client = AuthedClient(shop.OwnerToken);
        // не картинка
        var junk = new MultipartFormDataContent();
        var f = new ByteArrayContent([1, 2, 3, 4]); f.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        junk.Add(f, "file", "x.jpg");
        (await client.PostAsync($"/api/companies/{shop.Id}/photos", junk)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // без файла
        (await client.PostAsync($"/api/companies/{shop.Id}/photos", new MultipartFormDataContent())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GalleryAsync(shop.Id)).Should().BeEmpty();
    }

    [Fact, TestCase("CY26-09")]
    public async Task ShopGallery_SuperAdmin_DeleteWithReason_204_AndNoticePublished_ManageRoutesWork()
    {
        var shop = await CreateShopAsync();
        var admin = await LoginAsSuperAdminAsync();
        var photo = await AuthedClient(admin.Token).PostAsync($"/api/companies/{shop.Id}/photos", Upload());
        photo.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await photo.Content.ReadJsonAsync<CompanyPhotoDto>())!;

        var bad = await AuthedClient(admin.Token).DeleteAsync($"/api/companies/{shop.Id}/photos/{dto.Id}?reason=Nope");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var del = await AuthedClient(admin.Token).DeleteAsync($"/api/companies/{shop.Id}/photos/{dto.Id}?reason=DepictedPersonRequest");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GalleryAsync(shop.Id)).Should().BeEmpty();
        var notices = await WithDbAsync(db => db.PlatformNotices.AsNoTracking().Where(n => n.Kind == PlatformNoticeKind.PhotoRemoved).ToListAsync());
        notices.Should().Contain(n => n.Body.Contains(shop.Shop.Name) || n.Title.Contains(shop.Shop.Name), "владельцу магазина уходит уведомление о снятии фото");
    }

    [Fact, TestCase("CY26-10")]
    public async Task ShopOwner_DeleteWithReasonParam_NoNotice()
    {
        var shop = await CreateShopAsync();
        var photo = await UploadOkAsync(shop.OwnerToken, shop.Id);
        var before = await WithDbAsync(db => db.PlatformNotices.CountAsync(n => n.Kind == PlatformNoticeKind.PhotoRemoved));
        (await AuthedClient(shop.OwnerToken).DeleteAsync($"/api/companies/{shop.Id}/photos/{photo.Id}?reason=DepictedPersonRequest"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.PlatformNotices.CountAsync(n => n.Kind == PlatformNoticeKind.PhotoRemoved))).Should().Be(before);
    }

    [Fact, TestCase("CY26-11")]
    public async Task SalonOnlyRoutes_ForShop_Still409()
    {
        var shop = await CreateShopAsync();
        var r = await AuthedClient(shop.OwnerToken).GetAsync($"/api/companies/{shop.Id}/photo-usage");
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var svc = await AuthedClient(shop.OwnerToken).GetAsync($"/api/companies/{shop.Id}/services");
        svc.StatusCode.Should().NotBe(HttpStatusCode.OK, "услуги салона для магазина недоступны");
    }

    // ── US-26-02: витрина ────────────────────────────────────────────────────────

    [Fact, TestCase("CY26-20")]
    public async Task Storefront_ReturnsPhotosInOrder_AndEmail_AndAddressWithoutCity()
    {
        var shop = await CreateShopAsync();
        var a = await UploadOkAsync(shop.OwnerToken, shop.Id, 0);
        var b = await UploadOkAsync(shop.OwnerToken, shop.Id, 1);
        (await AuthedClient(shop.OwnerToken).PutAsJsonAsync($"/api/companies/{shop.Id}/photos/order", new { photoIds = new[] { b.Id, a.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutCompanyAsync(shop, new { email = "shop@example.com" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuthedClient(shop.OwnerToken).PutAsJsonAsync($"/api/companies/{shop.Id}/address", new { address = "Ленина, 5" }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var sf = await GetStorefrontAsync(shop.Slug);
        sf.Photos.Select(p => p.Id).Should().Equal(b.Id, a.Id);
        sf.Photos[0].IsCover.Should().BeTrue();
        sf.Email.Should().Be("shop@example.com");
        sf.Address.Should().Be("Ленина, 5", "сервер город к адресу не добавляет (§561)");

        // галерея правится — витрина видит сразу (нет серверного кеша)
        (await AuthedClient(shop.OwnerToken).DeleteAsync($"/api/companies/{shop.Id}/photos/{b.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetStorefrontAsync(shop.Slug)).Photos.Should().ContainSingle(p => p.Id == a.Id);
    }

    [Fact, TestCase("CY26-21")]
    public async Task Storefront_NoPhotos_EmptyArray_BlankEmail_Null()
    {
        var shop = await CreateShopAsync();
        (await PutCompanyAsync(shop, new { email = "   " })).StatusCode.Should().Be(HttpStatusCode.OK);
        var r = await AnonymousClient().GetStringAsync($"/api/storefront/{shop.Slug}");
        using var doc = JsonDocument.Parse(r);
        doc.RootElement.GetProperty("photos").GetArrayLength().Should().Be(0);
        var email = doc.RootElement.GetProperty("email");
        email.ValueKind.Should().Be(JsonValueKind.Null, "пробельный email — null");
    }

    [Fact, TestCase("CY26-22")]
    public async Task Storefront_InactiveShop_ShowsNoPhotos()
    {
        var shop = await CreateShopAsync();
        await UploadOkAsync(shop.OwnerToken, shop.Id);
        await WithDbAsync(async db =>
        {
            var c = await db.Companies.SingleAsync(x => x.Id == shop.Id);
            c.IsActive = false; await db.SaveChangesAsync(); return 0;
        });
        var r = await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}");
        if (r.StatusCode == HttpStatusCode.OK)
        {
            var sf = (await r.Content.ReadJsonAsync<StorefrontDto>())!;
            sf.IsAvailable.Should().BeFalse();
            sf.Photos.Should().BeEmpty();
        }
        else r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── US-26-03: ссылки на карты ────────────────────────────────────────────────

    [Fact, TestCase("CY26-30")]
    public async Task ShopProfile_MapLinks_SaveClearAndUntouched()
    {
        var shop = await CreateShopAsync();
        (await PutCompanyAsync(shop, new { yandexMapsUrl = YandexUrl, twoGisUrl = TwoGisUrl })).StatusCode.Should().Be(HttpStatusCode.OK);
        var sf = await GetStorefrontAsync(shop.Slug);
        sf.YandexMapsUrl.Should().Be(YandexUrl);
        sf.TwoGisUrl.Should().Be(TwoGisUrl);

        // нетронутое не меняется
        (await PutCompanyAsync(shop, new { description = "Новое описание" })).StatusCode.Should().Be(HttpStatusCode.OK);
        sf = await GetStorefrontAsync(shop.Slug);
        sf.YandexMapsUrl.Should().Be(YandexUrl);
        sf.TwoGisUrl.Should().Be(TwoGisUrl);

        // "" очищает только своё поле
        (await PutCompanyAsync(shop, new { yandexMapsUrl = "" })).StatusCode.Should().Be(HttpStatusCode.OK);
        sf = await GetStorefrontAsync(shop.Slug);
        sf.YandexMapsUrl.Should().BeNull();
        sf.TwoGisUrl.Should().Be(TwoGisUrl);
    }

    [Theory, TestCase("CY26-31")]
    [InlineData("http://yandex.ru/maps/org/1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://yandex.evil.com/maps/org/1")]
    [InlineData("https://2gis.ru/barnaul/firm/1")] // ссылка 2ГИС в поле Яндекса
    public async Task ShopProfile_BadYandexLink_400_NothingSaved(string bad)
    {
        var shop = await CreateShopAsync();
        var r = await PutCompanyAsync(shop, new { name = "Не должно сохраниться", yandexMapsUrl = bad });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace();
        (await GetStorefrontAsync(shop.Slug)).Name.Should().Be(shop.Shop.Name);
    }

    [Fact, TestCase("CY26-32")]
    public async Task ShopProfile_BadTwoGisLink_And_TooLong_400()
    {
        var shop = await CreateShopAsync();
        var bad = await PutCompanyAsync(shop, new { twoGisUrl = "https://yandex.ru/maps/org/1" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("2ГИС");
        var tooLong = await PutCompanyAsync(shop, new { yandexMapsUrl = "https://yandex.ru/maps/org/1?" + new string('a', 500) });
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).Should().Contain("500");
    }

    [Fact, TestCase("CY26-33")]
    public async Task ShopProfile_Staff_CannotEditProfile()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var r = await AuthedClient(staff.Token).PutAsJsonAsync($"/api/companies/{shop.Id}", new { yandexMapsUrl = YandexUrl });
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── US-26-07: город и часовой пояс магазина ──────────────────────────────────

    [Fact, TestCase("CY26-40")]
    public async Task ShopWithoutOrders_CityChange_ToOtherZone_200_ZoneFollowsCity()
    {
        var cities = await CitiesAsync();
        var a = cities.First();
        var other = cities.First(c => Offset(c.TimeZoneId) != Offset(a.TimeZoneId));
        var shop = await CreateShopAsync();
        await SetShopCityAsync(shop, a);

        var before = await ManageAsync(shop);
        before.TimeZoneChangeAllowed.Should().BeTrue();
        before.TimeZoneChangeLockedText.Should().BeNull();
        before.UtcOffsetMinutes.Should().Be(Offset(a.TimeZoneId));
        before.CityRegion.Should().Be(a.Region);

        var r = await PutCompanyAsync(shop, new { cityId = other.Id });
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var after = await ManageAsync(shop);
        after.CityId.Should().Be(other.Id);
        after.TimeZoneId.Should().Be(other.TimeZoneId);
        after.UtcOffsetMinutes.Should().Be(Offset(other.TimeZoneId));
        (await GetStorefrontAsync(shop.Slug)).CityName.Should().Be(other.Name);
    }

    [Fact, TestCase("CY26-41")]
    public async Task ShopWithOrder_CityChange_ToOtherOffset_409_NothingSaved_LockedTextInManageDto()
    {
        var cities = await CitiesAsync();
        var a = cities.First();
        var other = cities.First(c => Offset(c.TimeZoneId) != Offset(a.TimeZoneId));
        var shop = await CreateShopAsync();
        await SetShopCityAsync(shop, a);
        await PlaceOrderForAsync(shop);

        var manage = await ManageAsync(shop);
        manage.TimeZoneChangeAllowed.Should().BeFalse();
        manage.TimeZoneChangeLockedText.Should().NotBeNullOrWhiteSpace();

        var r = await PutCompanyAsync(shop, new { cityId = other.Id, yandexMapsUrl = YandexUrl, name = "Переименован" });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var text = await r.Content.ReadAsStringAsync();
        text.Should().Contain("У магазина уже есть заказы").And.Contain("UTC");
        text.Should().Be(manage.TimeZoneChangeLockedText);

        var after = await ManageAsync(shop);
        after.CityId.Should().Be(a.Id);
        after.TimeZoneId.Should().Be(a.TimeZoneId);
        after.YandexMapsUrl.Should().BeNull("при 409 не сохраняется ничего, включая ссылки карт");
        after.Name.Should().Be(shop.Shop.Name);
    }

    [Fact, TestCase("CY26-42")]
    public async Task ShopWithOrder_CityChange_SameOffset_200()
    {
        var cities = await CitiesAsync();
        var group = cities.GroupBy(c => Offset(c.TimeZoneId)).First(g => g.Count() > 1).ToList();
        var a = group[0];
        var same = group[1];
        var shop = await CreateShopAsync();
        await SetShopCityAsync(shop, a);
        await PlaceOrderForAsync(shop);

        var r = await PutCompanyAsync(shop, new { cityId = same.Id });
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        (await ManageAsync(shop)).CityId.Should().Be(same.Id);
    }

    [Fact, TestCase("CY26-43")]
    public async Task ShopProfile_ManualTimeZone_400_EqualToCityOrNull_Allowed_UnknownCity400()
    {
        var cities = await CitiesAsync();
        var a = cities.First();
        var other = cities.First(c => Offset(c.TimeZoneId) != Offset(a.TimeZoneId));
        var shop = await CreateShopAsync();
        await SetShopCityAsync(shop, a);

        var manual = await PutCompanyAsync(shop, new { timeZoneId = other.TimeZoneId });
        manual.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await manual.Content.ReadAsStringAsync()).Should().Be("Часовой пояс магазина задаётся городом");

        (await PutCompanyAsync(shop, new { timeZoneId = a.TimeZoneId })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutCompanyAsync(shop, new { timeZoneId = (string?)null })).StatusCode.Should().Be(HttpStatusCode.OK);

        // пояс «другого» города вместе со сменой города на него — допустим (пояс после смены = поясу города)
        (await PutCompanyAsync(shop, new { cityId = other.Id, timeZoneId = other.TimeZoneId })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PutCompanyAsync(shop, new { cityId = 99999999 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PutCompanyAsync(shop, new { timeZoneId = "Not/AZone" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ManageAsync(shop)).CityId.Should().Be(other.Id);
    }

    [Fact, TestCase("CY26-44")]
    public async Task Salon_TimeZoneOverride_StillManual_RegressionOfCycle4()
    {
        var cities = await CitiesAsync();
        var a = cities.First();
        var other = cities.First(c => Offset(c.TimeZoneId) != Offset(a.TimeZoneId));
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var r = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}", new { cityId = a.Id, timeZoneId = other.TimeZoneId });
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var dto = (await r.Content.ReadJsonAsync<CompanyDto>())!;
        dto.TimeZoneId.Should().Be(other.TimeZoneId);
        dto.TimeZoneIsManual.Should().BeTrue();
    }

    [Fact, TestCase("CY26-45")]
    public async Task ShopProfile_ConcurrentSaves_OfDifferentFields_NoLostUpdate()
    {
        var shop = await CreateShopAsync();
        var t1 = PutCompanyAsync(shop, new { yandexMapsUrl = YandexUrl });
        var t2 = PutCompanyAsync(shop, new { twoGisUrl = TwoGisUrl });
        var t3 = PutCompanyAsync(shop, new { description = "Описание" });
        var rs = await Task.WhenAll(t1, t2, t3);
        rs.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var sf = await GetStorefrontAsync(shop.Slug);
        sf.YandexMapsUrl.Should().Be(YandexUrl);
        sf.TwoGisUrl.Should().Be(TwoGisUrl);
        sf.Description.Should().Be("Описание");
    }
}
