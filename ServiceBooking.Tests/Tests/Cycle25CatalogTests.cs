using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Catalog;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 25, «Вызов 2», блок F: каталог магазинов по городу на главной goods и переключатель показа (US-25-12, US-25-13, Q-25-7).
/// API_CONTRACT_CYCLE25.md §531–§533. Хост каталога — <see cref="CatalogTestFactory"/> (без кеша), чтобы изменение условий было видно сразу.
/// Каждый сценарий берёт СВОЙ город справочника: каталог считает «город» целиком, и чужие магазины класса не должны попадать в подсчёты.
/// </summary>
public class Cycle25CatalogTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private async Task<int> CityAsync(int skip) =>
        await WithDbAsync(db => db.Cities.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Id).Skip(skip).Select(c => c.Id).FirstAsync());

    private async Task<GoodsCatalogPageDto> CatalogAsync(CatalogTestFactory host, string query)
    {
        var r = await host.CreateClient().GetAsync("/api/goods/catalog" + query);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<GoodsCatalogPageDto>())!;
    }

    /// <summary>Часы задаются с шагом 5 минут (§ контракта цикла 24): время округляется вниз.</summary>
    private static string F(DateTime t) => t.AddMinutes(-(t.Minute % 5)).ToString("HH:mm");

    /// <summary>Часы «вокруг сейчас»: с −3 ч до +3 ч местного времени каждый день (интервал через полночь — допустим), чтобы магазин был открыт в любой час прогона.</summary>
    private async Task OpenAroundNowAsync(ShopCtx shop, int beforeMinutes = 180, int afterMinutes = 180)
    {
        var now = ShopNow(shop);
        await SetHoursAsync(shop, (F(now.AddMinutes(-beforeMinutes)), F(now.AddMinutes(afterMinutes))));
    }

    /// <summary>Часы, которые сейчас заведомо закрыты: окно в 2 часа через 6 часов.</summary>
    private async Task ClosedNowAsync(ShopCtx shop)
    {
        var now = ShopNow(shop);
        await SetHoursAsync(shop, (F(now.AddHours(6)), F(now.AddHours(8))));
    }

    private async Task<ShopCtx> VisibleShopAsync(int city, string? name = null, Func<ShopCtx, Task>? tune = null)
    {
        var shop = await CreateShopAsync(name: name, openAllDay: false);
        await MoveShopToCityAsync(shop, city);
        await CreateProductAsync(shop);
        await OpenAroundNowAsync(shop);
        if (tune is not null) await tune(shop);
        return shop;
    }

    // ── условия видимости (Q-25-7) ────────────────────────────────────────────────

    [Fact, TestCase("CY25-50")]
    public async Task Visibility_AllFiveConditions_HiddenShopsNeverAppear_NotByCardNotByCountNotBySearch()
    {
        var city = await CityAsync(11);
        await using var host = new CatalogTestFactory(ConnectionString);
        var visible = await VisibleShopAsync(city, "Видимый Магазин");

        // 1. нет часов работы
        var noHours = await CreateShopAsync(name: "Без Часов Магазин", openAllDay: false);
        await MoveShopToCityAsync(noHours, city);
        await CreateProductAsync(noHours);
        // 2. нет опубликованных товаров
        var noProducts = await CreateShopAsync(name: "Без Товаров Магазин", openAllDay: false);
        await MoveShopToCityAsync(noProducts, city);
        await CreateProductAsync(noProducts, published: false);
        await OpenAroundNowAsync(noProducts);
        // 3. владелец выключил показ
        var hidden = await VisibleShopAsync(city, "Скрытый Владельцем Магазин");
        var off = await AuthedClient(hidden.OwnerToken).PutJsonAsync($"/api/shops/{hidden.Id}/catalog-listing", new { showInCatalog = false });
        off.StatusCode.Should().Be(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        // 4. тариф не разрешает показ
        var noPlan = await VisibleShopAsync(city, "Тариф Без Показа Магазин");
        await GiveOrdersPlanAsync(noPlan.Owner.UserId, allowPublicListing: false);
        // 5. магазин заблокирован
        var blocked = await VisibleShopAsync(city, "Заблокированный Магазин");
        await WithDbAsync(async db =>
        {
            var c = await db.Companies.SingleAsync(x => x.Id == blocked.Id);
            c.IsActive = false;
            await db.SaveChangesAsync();
        });

        var page = await CatalogAsync(host, $"?cityId={city}");
        page.Items.Select(i => i.Slug).Should().Equal(visible.Slug);
        page.TotalCount.Should().Be(1, "скрытые не входят в totalCount — ни числом, ни карточкой");
        page.City!.Id.Should().Be(city);
        page.EmptyText.Should().BeNull();

        foreach (var h in new[] { noHours, noProducts, hidden, noPlan, blocked })
        {
            var byName = await CatalogAsync(host, $"?cityId={city}&search={Uri.EscapeDataString(h.Shop.Name)}");
            byName.Items.Should().BeEmpty($"«{h.Shop.Name}» скрыт и не находится поиском");
            byName.TotalCount.Should().Be(0);
            byName.EmptyText.Should().Be("Ничего не найдено");
            var bySlug = await CatalogAsync(host, $"?search={Uri.EscapeDataString(h.Slug)}");
            bySlug.Items.Should().BeEmpty();
        }
        var all = await host.CreateClient().GetStringAsync("/api/goods/catalog");
        foreach (var h in new[] { noHours, noProducts, hidden, noPlan, blocked }) all.Should().NotContain(h.Slug);

        // чек-лист у владельца объясняет, почему магазина нет
        async Task<CatalogListingDto> Listing(ShopCtx s) => (await (await AuthedClient(s.OwnerToken).GetAsync($"/api/shops/{s.Id}/catalog-listing")).Content.ReadJsonAsync<CatalogListingDto>())!;
        var l1 = await Listing(noHours);
        l1.Visible.Should().BeFalse();
        l1.StatusText.Should().Be("Магазина сейчас нет в каталоге");
        l1.Checklist.Should().Contain(c => c.Code == CatalogListingCheckCode.NoWorkingHours && !c.Done && c.Text == "Задайте часы работы");
        (await Listing(noProducts)).Checklist.Should().Contain(c => c.Code == CatalogListingCheckCode.NoPublishedProducts && !c.Done && c.Text == "Опубликуйте хотя бы один товар");
        (await Listing(hidden)).Checklist.Should().Contain(c => c.Code == CatalogListingCheckCode.HiddenByOwner && !c.Done);
        var lp = await Listing(noPlan);
        lp.AllowedByPlan.Should().BeFalse();
        lp.NotAllowedByPlanText.Should().Be("Показ в каталоге не входит в ваш тариф");
        lp.Checklist.Should().Contain(c => c.Code == CatalogListingCheckCode.NotAllowedByPlan && !c.Done);
        var lv = await Listing(visible);
        lv.Visible.Should().BeTrue();
        lv.StatusText.Should().Be("Магазин виден в каталоге goods.ezbook.ru");
        lv.Checklist.Should().OnlyContain(c => c.Done, "visible ⇔ все пункты выполнены");
        (await Listing(blocked)).Checklist.Should().Contain(c => c.Code == CatalogListingCheckCode.ShopBlocked && !c.Done);

        // устранили причину — магазин появился (без деплоя, без ожидания кеша на этом хосте)
        await SetHoursAsync(noHours, ("09:00", "18:00"));
        await OpenAroundNowAsync(noHours);
        (await AuthedClient(hidden.OwnerToken).PutJsonAsync($"/api/shops/{hidden.Id}/catalog-listing", new { showInCatalog = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await CatalogAsync(host, $"?cityId={city}");
        after.Items.Select(i => i.Slug).Should().BeEquivalentTo(new[] { visible.Slug, noHours.Slug, hidden.Slug });
        after.TotalCount.Should().Be(3);
    }

    [Fact, TestCase("CY25-51")]
    public async Task ListedShop_StateInWords_OrderAcceptingThenPreorderThenNotAccepting_ByName_NoPricesPhotosRatings()
    {
        var city = await CityAsync(12);
        await using var host = new CatalogTestFactory(ConnectionString);
        var yabloko = await VisibleShopAsync(city, "Яблоко");
        var abrikos = await VisibleShopAsync(city, "Абрикос");
        var arbuz = await VisibleShopAsync(city, "Арбуз", async s => await SetPickupAsync(s, asap: false, scheduled: true, step: 30, preorderDays: 1, minPrep: 0));
        var apelsin = await VisibleShopAsync(city, "Апельсин", async s => (await SetAcceptanceAsync(s, new { mode = "Stopped" })).StatusCode.Should().Be(HttpStatusCode.OK));
        var ananas = await VisibleShopAsync(city, "Ананас", ClosedNowAsync);

        var page = await CatalogAsync(host, $"?cityId={city}");
        page.Items.Select(i => i.Name).Should().Equal(new[] { "Абрикос", "Яблоко", "Арбуз", "Ананас", "Апельсин" },
            "1) принимают «как можно скорее» сейчас, 2) только ко времени, 3) не принимают; внутри групп — по названию");
        page.Items.Select(i => i.Acceptance).Should().Equal(
            CatalogAcceptance.AcceptingNow, CatalogAcceptance.AcceptingNow, CatalogAcceptance.PreorderOnly, CatalogAcceptance.NotAccepting, CatalogAcceptance.NotAccepting);
        page.Items.Select(i => i.AcceptanceText).Should().Equal(
            "Принимает заказы", "Принимает заказы", "Можно заказать заранее", "Временно не принимает заказы", "Временно не принимает заказы");
        page.TotalCount.Should().Be(5);
        page.PageSize.Should().Be(20);

        var open = page.Items.Single(i => i.Slug == abrikos.Slug);
        open.OpenState.IsOpen.Should().BeTrue();
        open.OpenState.Text.Should().StartWith("Открыто до ");
        open.Path.Should().Be("/" + abrikos.Slug);
        open.CityName.Should().NotBeNullOrEmpty();
        var closed = page.Items.Single(i => i.Slug == ananas.Slug);
        closed.OpenState.IsOpen.Should().BeFalse();
        closed.OpenState.Text.Should().StartWith("Закрыто");
        page.Items.Single(i => i.Slug == apelsin.Slug).OpenState.IsOpen.Should().BeTrue("пауза приёма не закрывает часы: состояние «открыто» и «не принимает» независимы");

        // карточка — только перечисленное в контракте: ни цен, ни фото, ни рейтингов, ни реквизитов продавца [legal L19]
        var raw = await host.CreateClient().GetStringAsync($"/api/goods/catalog?cityId={city}");
        using var doc = JsonDocument.Parse(raw);
        var props = doc.RootElement.GetProperty("items")[0].EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToList();
        props.Should().Equal("acceptance", "acceptanceText", "address", "cityName", "name", "openState", "path", "slug");
        raw.Should().NotContainEquivalentOf("price").And.NotContainEquivalentOf("rating").And.NotContainEquivalentOf("review")
            .And.NotContainEquivalentOf("logo").And.NotContainEquivalentOf("photo").And.NotContainEquivalentOf("inn");

        // фильтр «Открыто сейчас»
        var openNow = await CatalogAsync(host, $"?cityId={city}&openNow=true");
        openNow.Items.Select(i => i.Name).Should().NotContain("Ананас").And.Contain(new[] { "Абрикос", "Яблоко", "Арбуз", "Апельсин" });
        openNow.TotalCount.Should().Be(4);
        (await CatalogAsync(host, $"?cityId={city}&openNow=true&search=ананас")).EmptyText.Should().Be("Ничего не найдено");

        // состояние считается тем же правилом приёма, что на странице магазина: пауза снята — карточка поднялась в первую группу
        (await SetAcceptanceAsync(apelsin, new { mode = "Accepting" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await CatalogAsync(host, $"?cityId={city}");
        after.Items.Select(i => i.Name).Take(3).Should().Equal("Абрикос", "Апельсин", "Яблоко");
        var storefront = await GetStorefrontAsync(apelsin.Slug);
        storefront.AcceptingOrders.Should().BeTrue("страница магазина и каталог согласованы");
        _ = yabloko; _ = arbuz;
    }

    [Fact, TestCase("CY25-52")]
    public async Task Search_ByNameAndAddress_CaseInsensitive_EmptyTexts_UnknownCity_PageParsing_Anonymous()
    {
        var city = await CityAsync(13);
        var emptyCity = await CityAsync(14);
        await using var host = new CatalogTestFactory(ConnectionString);
        var a = await VisibleShopAsync(city, "Пекарня Колос");
        var b = await VisibleShopAsync(city, "Столовая Ромашка");
        await WithDbAsync(async db =>
        {
            (await db.Companies.SingleAsync(c => c.Id == b.Id)).Address = "ул. Ленина, 5";
            await db.SaveChangesAsync();
        });

        (await CatalogAsync(host, $"?cityId={city}&search=пЕкАрНя")).Items.Select(i => i.Slug).Should().Equal(a.Slug);
        var byAddress = await CatalogAsync(host, $"?cityId={city}&search=ЛЕНИНА");
        byAddress.Items.Should().ContainSingle().Which.Address.Should().Be("ул. Ленина, 5");
        (await CatalogAsync(host, $"?cityId={city}&search=%20%20")).TotalCount.Should().Be(2, "пустой поиск — без фильтра");
        (await CatalogAsync(host, $"?cityId={city}&search={new string('я', 300)}")).TotalCount.Should().Be(0, "длинный поиск обрезается, а не роняет запрос");
        foreach (var hostile in new[] { "?search=%00", "?cityId=abc", "?openNow=maybe", "?cityId=99999999999999", "?page=99999999999999999999", "?search=%F0%9F%98%80", "?search=%25%25" })
            ((int)(await host.CreateClient().GetAsync("/api/goods/catalog" + hostile)).StatusCode).Should().BeLessThan(500, hostile);

        var emptyPage = await CatalogAsync(host, $"?cityId={emptyCity}");
        emptyPage.Items.Should().BeEmpty();
        emptyPage.EmptyText.Should().Be("В этом городе пока нет магазинов на goods");
        emptyPage.City!.Id.Should().Be(emptyCity);
        emptyPage.City.Label.Should().Contain(",");

        var unknown = await CatalogAsync(host, "?cityId=987654");
        unknown.Items.Should().BeEmpty("неизвестный город — пустая страница, не 404");
        unknown.City.Should().BeNull();

        (await CatalogAsync(host, $"?cityId={city}&page=abc")).Page.Should().Be(1, "некорректная страница — как 1");
        (await CatalogAsync(host, $"?cityId={city}&page=-4")).Page.Should().Be(1);
        var beyond = await CatalogAsync(host, $"?cityId={city}&page=9");
        beyond.Items.Should().BeEmpty();
        beyond.TotalCount.Should().Be(2);

        // «Все города»: карточка называет город
        var everywhere = await CatalogAsync(host, "");
        everywhere.Items.Select(i => i.Slug).Should().Contain(new[] { a.Slug, b.Slug });
        everywhere.City.Should().BeNull();
        everywhere.Items.Should().OnlyContain(i => i.CityName != "");

        // без токена — 200; с мусорным токеном тоже не падает
        var garbage = host.CreateClient();
        garbage.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "garbage");
        (await garbage.GetAsync("/api/goods/catalog")).StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Unauthorized]);
    }

    [Fact, TestCase("CY25-53")]
    public async Task SalonsAndShops_NeverMeet_SalonListsHaveNoShops_ShopCatalogHasNoSalons()
    {
        var city = await CityAsync(15);
        await using var host = new CatalogTestFactory(ConnectionString);
        var shop = await VisibleShopAsync(city, "Магазин Витрина");
        var (_, salon) = await CreateOwnerWithCompanyAsync();
        await WithDbAsync(async db =>
        {
            var c = await db.Companies.SingleAsync(x => x.Id == salon.Id);
            c.CityId = city;
            await db.SaveChangesAsync();
        });

        var catalog = await host.CreateClient().GetStringAsync($"/api/goods/catalog?cityId={city}");
        catalog.Should().Contain(shop.Slug).And.NotContain(salon.Slug).And.NotContain(salon.Name);

        foreach (var url in new[] { "/api/companies", "/api/companies/public", $"/api/companies/public?cityId={city}" })
        {
            var r = await AnonymousClient().GetAsync(url);
            var body = await r.Content.ReadAsStringAsync();
            body.Should().NotContain(shop.Slug, url).And.NotContain(shop.Shop.Name, url);
        }
        var kinds = await AnonymousClient().GetStringAsync("/api/companies/public");
        kinds.Should().Contain(salon.Name, "салон в своём каталоге остался");
    }

    // ── US-25-12: переключатель в настройках магазина, тариф, админка ─────────────────

    [Fact, TestCase("CY25-54")]
    public async Task CatalogListingSwitch_Defaults_Permissions_Validation_TariffGate_AdminFlagAppliesWithoutDeploy()
    {
        var city = await CityAsync(16);
        await using var host = new CatalogTestFactory(ConnectionString);
        var shop = await VisibleShopAsync(city, "Переключатель");
        var staff = await AddShopStaffAsync(shop);
        var url = $"/api/shops/{shop.Id}/catalog-listing";

        var initial = (await (await AuthedClient(shop.OwnerToken).GetAsync(url)).Content.ReadJsonAsync<CatalogListingDto>())!;
        initial.ShowInCatalog.Should().BeTrue("по умолчанию включён");
        initial.AllowedByPlan.Should().BeTrue("у бесплатного тарифа линейки «Заказы» показ включён");
        initial.Visible.Should().BeTrue();
        (await AuthedClient(staff.Token).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, "читает персонал");
        (await AuthedClient(staff.Token).PutJsonAsync(url, new { showInCatalog = false })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "меняет владелец");
        (await AnonymousClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AnonymousClient().PutJsonAsync(url, new { showInCatalog = false })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 400 строкой, если значение не указано; 415 на не-JSON
        var missing = await AuthedClient(shop.OwnerToken).PutJsonAsync(url, new { });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync()).Should().NotStartWith("{", "400 контракта — голая строка, не ProblemDetails");
        (await AuthedClient(shop.OwnerToken).PutAsync(url, new StringContent("showInCatalog=false", Encoding.UTF8, "text/plain"))).StatusCode
            .Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await GetShopAsync(shop)).Should().NotBeNull();
        (await (await AuthedClient(shop.OwnerToken).GetAsync(url)).Content.ReadJsonAsync<CatalogListingDto>())!.ShowInCatalog.Should().BeTrue("неудачные запросы значение не меняют");

        // владелец выключил и включил — без ожидания кеша (хост без кеша), витрина магазина по прямой ссылке работает в обоих случаях
        (await AuthedClient(shop.OwnerToken).PutJsonAsync(url, new { showInCatalog = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CatalogAsync(host, $"?cityId={city}")).TotalCount.Should().Be(0);
        (await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK, "прямая ссылка на магазин работает и скрытому");
        (await AuthedClient(shop.OwnerToken).PutJsonAsync(url, new { showInCatalog = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CatalogAsync(host, $"?cityId={city}")).TotalCount.Should().Be(1);

        // тариф без показа: включить нельзя (409, JSON), выключить всегда можно
        var planId = await GiveOrdersPlanAsync(shop.Owner.UserId, allowPublicListing: false);
        (await CatalogAsync(host, $"?cityId={city}")).TotalCount.Should().Be(0, "магазин с включённым флагом, но без права по тарифу, скрыт");
        var listing = (await (await AuthedClient(shop.OwnerToken).GetAsync(url)).Content.ReadJsonAsync<CatalogListingDto>())!;
        listing.AllowedByPlan.Should().BeFalse();
        listing.NotAllowedByPlanText.Should().Be("Показ в каталоге не входит в ваш тариф");
        (await AuthedClient(shop.OwnerToken).PutJsonAsync(url, new { showInCatalog = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        var conflict = await AuthedClient(shop.OwnerToken).PutJsonAsync(url, new { showInCatalog = true });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(conflict);
        body.GetProperty("code").GetString().Should().Be("CatalogListingNotAllowedByPlan");
        body.GetProperty("message").GetString().Should().Be("Показ в каталоге не входит в ваш тариф");

        // администратор меняет флаг тарифа «Заказов» — действует без деплоя
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var upd = await admin.PutJsonAsync($"/api/admin/plans/{planId}", new
        {
            name = Unique("Заказы · разрешён показ "), pricePerMonth = 1m, maxEmployees = (int?)null, maxCompanies = (int?)null, isPublic = true, isActive = true,
            line = "Orders", maxProductsPerShop = (int?)null, maxOrdersPerMonth = (int?)null, allowOrders = true, allowPublicListing = true,
        });
        upd.StatusCode.Should().Be(HttpStatusCode.OK, await upd.Content.ReadAsStringAsync());
        (await AuthedClient(shop.OwnerToken).PutJsonAsync(url, new { showInCatalog = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CatalogAsync(host, $"?cityId={city}")).TotalCount.Should().Be(1);

        // системный бесплатный тариф линейки «Заказы» — с показом
        var plans = await J(await admin.GetAsync("/api/admin/plans"));
        var planList = plans.GetProperty("plans");
        var ordersPlans = planList.EnumerateArray().Where(p => p.GetProperty("line").GetString() == "Orders").ToList();
        ordersPlans.Should().NotBeEmpty();
        ordersPlans.Should().Contain(p => p.GetProperty("allowPublicListing").GetBoolean(), "у бесплатного тарифа линейки показ включён");
    }

    [Fact, TestCase("CY25-55")]
    public async Task NewShopOnTheFreeOrdersPlan_IsListedByDefault_OnceHoursAndProductExist()
    {
        var city = await CityAsync(17);
        await using var host = new CatalogTestFactory(ConnectionString);
        var shop = await CreateShopAsync(name: "Новичок", paidPlan: false, openAllDay: false);
        await MoveShopToCityAsync(shop, city);
        (await CatalogAsync(host, $"?cityId={city}")).TotalCount.Should().Be(0, "нет часов и товаров");
        await CreateProductAsync(shop);
        (await CatalogAsync(host, $"?cityId={city}")).TotalCount.Should().Be(0, "нет часов");
        await OpenAroundNowAsync(shop);
        var page = await CatalogAsync(host, $"?cityId={city}");
        page.Items.Should().ContainSingle().Which.Slug.Should().Be(shop.Slug);
        (await GetShopAsync(shop)).Slug.Should().Be(shop.Slug);
    }
}
