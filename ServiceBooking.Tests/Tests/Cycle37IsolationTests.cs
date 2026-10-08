using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 37, «Вызов 2»: вид компании «Дома» и разделение вертикалей (US-37-01, US-37-08, US-37-33, ARCHITECTURE_CYCLE37.md §37.3).
/// Написано по SPEC_CYCLE37_STAYS_HOUSES.md и API_CONTRACT_CYCLE37.md §37.21, а не по реализации.
/// </summary>
public class Cycle37IsolationTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    private const string StaysRefusal = "Это компания «Дома»: записи, услуги и расписание для неё недоступны.";
    private const string GalleryRefusal = "У компании «Дома» фото добавляются к домам.";
    private const string ShopRefusal = "Это магазин: записи, услуги и расписание для него недоступны.";

    [Fact, TestCase("CY37-10")]
    public async Task SalonRoutes_RefuseStaysCompany_With409Text_AfterRightsCheck()
    {
        var company = await CreateStaysCompanyAsync();
        var c = AuthedClient(company.OwnerToken);

        var routes = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Post, "/api/services", new { companyId = company.Id, name = "Стрижка", durationMinutes = 30, price = 100 }),
            (HttpMethod.Get, $"/api/companies/{company.Id}/masters", null),
            (HttpMethod.Get, $"/api/companies/{company.Id}/stats?from=2026-01-01&to=2026-01-31", null),
            (HttpMethod.Get, $"/api/companies/{company.Id}/photo-usage", null),
            (HttpMethod.Get, $"/api/companies/{company.Id}/clients/u_{Guid.NewGuid()}/health-consent-form", null),
            (HttpMethod.Post, $"/api/companies/{company.Id}/clients/u_{Guid.NewGuid()}/health-written-consent",
                new { textVersion = "v1", formId = (string?)null, confirmed = false }),
        };
        foreach (var (method, url, body) in routes)
        {
            var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = JsonContent.Create(body);
            var r = await c.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, $"{method} {url}");
            // API_CONTRACT_CYCLE37.md §37.21.1: photo-usage (a gallery route) answers with the gallery text, not the general one
            (await r.Content.ReadAsStringAsync()).Should().Contain(url.EndsWith("/photo-usage") ? GalleryRefusal : StaysRefusal, $"{method} {url}");
        }

        // Галерея компании: у «Домов» фото добавляются к домам.
        var photo = await c.PostAsync($"/api/companies/{company.Id}/photos", FileContent(TestImages.SolidJpeg(40, 40), "image/jpeg", "p.jpg"));
        photo.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await photo.Content.ReadAsStringAsync()).Should().Contain("У компании «Дома» фото добавляются к домам.");
        var gallery = await AnonymousClient().GetAsync($"/api/companies/{company.Id}/photos");
        gallery.StatusCode.Should().Be(HttpStatusCode.OK);
        (await gallery.Content.ReadAsStringAsync()).Trim().Should().Be("[]");

        // «после прав»: посторонний получает 403, а не 409 (прецедент CY23-04)
        var stranger = await RegisterAsync();
        (await AuthedClient(stranger.Token).GetAsync($"/api/companies/{company.Id}/stats?from=2026-01-01&to=2026-01-31"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "проверка типа идёт после проверки прав");

        // магазин отвечает прежним текстом (байт-в-байт)
        var shop = await CreateShopAsync();
        var shopRefusal = await AuthedClient(shop.OwnerToken).GetAsync($"/api/companies/{shop.Id}/masters");
        shopRefusal.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await shopRefusal.Content.ReadAsStringAsync()).Should().Be(ShopRefusal);
    }

    [Fact, TestCase("CY37-11")]
    public async Task ShopRoutes_SeeStaysCompanyAs404_AndStaysRoutes_SeeSalonAndShopAs404()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company);
        var staysOwner = AuthedClient(company.OwnerToken);

        (await staysOwner.GetAsync($"/api/shops/{company.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await staysOwner.GetAsync($"/api/shops/{company.Id}/products")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await staysOwner.GetAsync($"/api/shops/{company.Id}/order-board")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync($"/api/storefront/{company.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (salonOwner, salon) = await CreateOwnerWithCompanyAsync();
        var shop = await CreateShopAsync();
        foreach (var (token, id, what) in new[] { (salonOwner.Token, salon.Id, "салон"), (shop.OwnerToken, shop.Id, "магазин") })
        {
            var c = AuthedClient(token);
            (await c.GetAsync($"/api/stays/companies/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, what);
            (await c.GetAsync($"/api/stays/companies/{id}/houses")).StatusCode.Should().Be(HttpStatusCode.NotFound, what);
            (await c.GetAsync($"/api/stays/companies/{id}/board")).StatusCode.Should().Be(HttpStatusCode.NotFound, what);
            (await c.GetAsync($"/api/stays/companies/{id}/schedule")).StatusCode.Should().Be(HttpStatusCode.NotFound, what);
            (await c.GetAsync($"/api/stays/companies/{id}/bookings")).StatusCode.Should().Be(HttpStatusCode.NotFound, what);
            (await c.PutJsonAsync($"/api/stays/companies/{id}/settings", company.Company.Settings!)).StatusCode.Should().Be(HttpStatusCode.NotFound, what);
            (await c.PostJsonAsync($"/api/stays/companies/{id}/houses", new HouseCreateInput("Дом", 2))).StatusCode.Should().Be(HttpStatusCode.NotFound, what);
        }
        // публичные страницы «Домов» по slug салона/магазина — 404
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{salon.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{shop.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // дом чужой компании под своим companyId — 404 (компания не видит чужих домов)
        var other = await CreateStaysCompanyAsync();
        (await AuthedClient(other.OwnerToken).GetAsync($"/api/stays/companies/{other.Id}/houses/{house.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuthedClient(other.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "чужая компания — неотличимо от несуществующей");
    }

    [Fact, TestCase("CY37-12")]
    public async Task StaysCompany_IsOnlyOnDom_NotInSalonOrShopListings_AndHasDomPublicUrl()
    {
        var company = await CreateStaysCompanyAsync(slug: Unique("dom-list-"));
        var house = await CreateHouseAsync(company);

        // общий публичный список салонов: «Дома» там нет
        var salonList = await AnonymousClient().GetAsync("/api/companies/public");
        var listText = salonList.IsSuccessStatusCode ? await salonList.Content.ReadAsStringAsync() : "";
        listText.Should().NotContain(company.Slug);
        var plain = await AnonymousClient().GetAsync("/api/companies");
        (await plain.Content.ReadAsStringAsync()).Should().NotContain(company.Slug);

        // каталог goods: «Домов» там нет (тест не зависит от города — компания не должна нигде попадаться)
        var cityId = await WithDbAsync(db => Task.FromResult(db.Companies.Where(x => x.Id == company.Id).Select(x => x.CityId).First()));
        var goods = await AnonymousClient().GetAsync($"/api/goods/catalog?cityId={cityId}");
        (await goods.Content.ReadAsStringAsync()).Should().NotContain(company.Slug);

        // карточка компании по slug: kind Stays, ссылка на dom
        var card = await J(await AnonymousClient().GetAsync($"/api/companies/{company.Slug}"));
        card.GetProperty("kind").GetString().Should().Be("Stays");
        card.GetProperty("publicUrl").GetString().Should().Be($"https://dom.ezbook.ru/{company.Slug}");

        // каталог «Домов» показывает дом, ссылки внутри dom
        InvalidateCatalog();
        var catalog = await J(await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50"));
        catalog.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("houseId").GetGuid()).Should().Contain(house.Id);

        // свой список «Домов»; салонный список по умолчанию «Домов» не содержит
        var mine = await J(await AuthedClient(company.OwnerToken).GetAsync("/api/companies/my?kind=Stays"));
        mine.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().Contain(company.Id);
        var defaultMine = await J(await AuthedClient(company.OwnerToken).GetAsync("/api/companies/my"));
        defaultMine.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().NotContain(company.Id,
            "без параметра kind — салоны, как раньше");

        // сводка видов
        var summary = await J(await AuthedClient(company.OwnerToken).GetAsync("/api/companies/kinds-summary"));
        summary.GetProperty("stays").GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        summary.GetProperty("stays").GetProperty("siteUrl").GetString().Should().StartWith("https://dom.ezbook.ru");
    }

    [Fact, TestCase("CY37-13")]
    public async Task OneAccount_OwnsSalonShopAndStays_EachKindSeesOnlyItself_AndSalonShopStayAsBefore()
    {
        var (owner, salon) = await CreateOwnerWithCompanyAsync();
        var (shopToken, shop) = await CreateShopForAsync(owner.Token);
        var stays = await CreateStaysCompanyForAsync(shopToken, Unique("dom-all-"));

        var token = (await LoginAsync(owner.Phone, "Password123!")).Token;
        var c = AuthedClient(token);

        var salons = (await J(await c.GetAsync("/api/companies/my"))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        salons.Should().Contain(salon.Id).And.NotContain(shop.Id).And.NotContain(stays.Company.Id);
        var shops = (await J(await c.GetAsync("/api/companies/my?kind=Orders"))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        shops.Should().Contain(shop.Id).And.NotContain(salon.Id).And.NotContain(stays.Company.Id);
        var houses = (await J(await c.GetAsync("/api/stays/companies/my"))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        houses.Should().ContainSingle().Which.Should().Be(stays.Company.Id);

        // «Дома» не потребили салонный лимит: аккаунт по-прежнему может работать с салоном (создать услугу)
        (await c.PostAsJsonAsync("/api/services", new { companyId = salon.Id, name = "Стрижка", durationMinutes = 30, price = 100 }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        // и магазин работает
        (await c.GetAsync($"/api/shops/{shop.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY37-14")]
    public async Task StaysCompany_CityIsSheregeshOnly_AndTimeZoneFollowsCity()
    {
        var company = await CreateStaysCompanyAsync();
        company.Company.CityName.Should().Be("Шерегеш");
        company.Company.TimeZoneId.Should().Be("Asia/Novokuznetsk");

        var otherCity = await WithDbAsync(db => Task.FromResult(db.Cities.Where(x => x.IsActive && x.Name != "Шерегеш").Select(x => x.Id).First()));
        var r = await AuthedClient(company.OwnerToken).PutAsJsonAsync($"/api/companies/{company.Id}", new { cityId = otherCity });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("Город компании «Дома» — Шерегеш");

        var tz = await AuthedClient(company.OwnerToken).PutAsJsonAsync($"/api/companies/{company.Id}", new { timeZoneId = "Europe/Moscow" });
        tz.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tz.Content.ReadAsStringAsync()).Should().Contain("Часовой пояс компании задаётся городом");
    }

    [Fact, TestCase("CY37-15")]
    public async Task Members_PositionRules()
    {
        var company = await CreateStaysCompanyAsync();
        var c = AuthedClient(company.OwnerToken);

        async Task<HttpResponseMessage> Add(object body) => await c.PostJsonAsync($"/api/Companies/{company.Id}/members", body);
        var user1 = await RegisterAsync();
        var noPosition = await Add(new { phone = user1.Phone, firstName = "А", lastName = "Б", role = "Master", position = (string?)null });
        noPosition.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noPosition.Content.ReadAsStringAsync()).Should().Contain("Укажите должность: управляющий или горничная.");

        var wrongRole = await Add(new { phone = user1.Phone, firstName = "А", lastName = "Б", role = "CompanyOwner", position = "Manager" });
        wrongRole.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrongRole.Content.ReadAsStringAsync()).Should().Contain("В компанию «Дома» можно добавить только сотрудника.");

        var ok = await Add(new { phone = user1.Phone, firstName = "А", lastName = "Б", role = "Master", position = "Housekeeper" });
        ok.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var member = await J(ok);
        member.GetProperty("position").GetString().Should().Be("Housekeeper");

        // смена должности
        var memberId = member.GetProperty("id").GetGuid();
        var change = await c.PutJsonAsync($"/api/Companies/{company.Id}/members/{memberId}/position", new { position = "Manager" });
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var list = await J(await c.GetAsync($"/api/Companies/{company.Id}/members"));
        list.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == memberId).GetProperty("position").GetString().Should().Be("Manager");

        // владелец: должность не меняется
        var ownerMember = list.EnumerateArray().First(m => m.GetProperty("position").ValueKind == JsonValueKind.Null);
        var ownerChange = await c.PutJsonAsync($"/api/Companies/{company.Id}/members/{ownerMember.GetProperty("id").GetGuid()}/position", new { position = "Housekeeper" });
        ownerChange.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ownerChange.Content.ReadAsStringAsync()).Should().Contain("Должность владельца не меняется.");

        // салон: должность задавать нельзя
        var (salonOwner, salon) = await CreateOwnerWithCompanyAsync();
        var user2 = await RegisterAsync();
        var salonPos = await AuthedClient(salonOwner.Token).PostJsonAsync($"/api/Companies/{salon.Id}/members",
            new { phone = user2.Phone, firstName = "В", lastName = "Г", role = "Master", position = "Manager" });
        salonPos.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await salonPos.Content.ReadAsStringAsync()).Should().Contain("Должность задаётся только сотрудникам компании «Дома».");
        // смена должности в салоне — 409 строкой общего текста
        var salonMember = await AddMasterAsync(salonOwner.Token, salon.Id);
        var salonMembers = await J(await AuthedClient(salonOwner.Token).GetAsync($"/api/Companies/{salon.Id}/members"));
        var salonMemberId = salonMembers.EnumerateArray().First(m => m.GetProperty("userId").GetString() == salonMember.UserId).GetProperty("id").GetGuid();
        var salonChange = await AuthedClient(salonOwner.Token).PutJsonAsync($"/api/Companies/{salon.Id}/members/{salonMemberId}/position", new { position = "Manager" });
        salonChange.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY37-16")]
    public async Task Members_CompanyAcceptsAtMost30Staff()
    {
        var company = await CreateStaysCompanyAsync();
        var c = AuthedClient(company.OwnerToken);
        HttpResponseMessage? refused = null;
        var accepted = 0;
        for (var i = 0; i < 34 && refused is null; i++)
        {
            var u = await RegisterAsync();
            var r = await c.PostJsonAsync($"/api/Companies/{company.Id}/members",
                new { phone = u.Phone, firstName = "С", lastName = $"{i}", role = "Master", position = "Housekeeper" });
            if (r.IsSuccessStatusCode) accepted++; else refused = r;
        }
        refused.Should().NotBeNull("в компании не больше 30 сотрудников");
        refused!.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("В компании не может быть больше 30 сотрудников.");
        accepted.Should().BeInRange(29, 30, "лимит — 30 сотрудников на компанию (владелец может считаться или не считаться)");
    }

    [Fact, TestCase("CY37-17")]
    public async Task BlockedStaysCompany_DoesNotAcceptBookings_PagesUnavailable_HousesLeaveCatalog()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company);
        var ci = InDays(10);
        var co = InDays(12);
        var quote = await QuoteAsync(house.Id, ci, co);

        var admin = await LoginAsSuperAdminAsync();
        var block = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/companies/{company.Id}",
            new { name = company.Company.Name, isActive = false, allowSelfBooking = false });
        block.StatusCode.Should().Be(HttpStatusCode.NoContent, await block.Content.ReadAsStringAsync());

        var r = await PostBookingAsync(house.Id, Booking(ci, co, quote.TotalRub));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(r);
        body.GetProperty("code").GetString().Should().Be("NotAcceptingBookings");
        body.GetProperty("reasonCode").GetString().Should().Be("CompanyBlocked");

        var page = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}"));
        page.GetProperty("available").GetBoolean().Should().BeFalse();
        page.GetProperty("houses").GetArrayLength().Should().Be(0);
        var housePage = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}"));
        housePage.GetProperty("available").GetBoolean().Should().BeFalse();
        InvalidateCatalog();
        var catalog = await J(await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50"));
        catalog.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("houseId").GetGuid()).Should().NotContain(house.Id);

        // разблокировка возвращает всё
        (await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/companies/{company.Id}",
            new { name = company.Company.Name, isActive = true, allowSelfBooking = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        InvalidateCatalog();
        var after = await J(await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50"));
        after.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("houseId").GetGuid()).Should().Contain(house.Id);
    }

    [Fact, TestCase("CY37-18")]
    public async Task AdminCompanyList_FiltersByStaysKind()
    {
        var company = await CreateStaysCompanyAsync(slug: Unique("dom-adm-"));
        var admin = await LoginAsSuperAdminAsync();
        var list = await J(await AuthedClient(admin.Token).GetAsync("/api/admin/companies?kind=Stays&pageSize=100"));
        var items = list.ValueKind == JsonValueKind.Array ? list : list.GetProperty("items");
        var mine = items.EnumerateArray().SingleOrDefault(c => c.GetProperty("id").GetGuid() == company.Id);
        mine.ValueKind.Should().Be(JsonValueKind.Object, "компания «Дома» видна в админке с фильтром kind=Stays");
        mine.GetProperty("kind").GetString().Should().Be("Stays");
        mine.GetProperty("publicUrl").GetString().Should().Be($"https://dom.ezbook.ru/{company.Slug}");
        items.EnumerateArray().Should().OnlyContain(c => c.GetProperty("kind").GetString() == "Stays");
    }
}
