using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: матрица изоляции четырёх видов компаний — «Записи» (Services), «Заказы» (Orders), «Дома» (Stays), «Бани» (Baths) (CY42-01…09). Писано по
/// SPEC_CYCLE42_BANI.md (US-42-26), API_CONTRACT_CYCLE42.md §42.20, §42.21 и ARCHITECTURE_CYCLE42.md §42.15.4, без чтения реализации. Проверяются компании, публичные
/// страницы и каталоги, токены броней, push-сайты, ссылки и общее пространство адресов.
/// </summary>
public class Cycle42IsolationTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private const string BathsRefusal = "Это компания «Бани»: записи, услуги и расписание для неё недоступны.";

    private sealed record Four(
        string SalonToken, Guid SalonId, string SalonSlug,
        string ShopToken, Guid ShopId, string ShopSlug,
        StaysCtx Stays, BathCtx Baths);

    private async Task<Four> CreateFourAsync()
    {
        var (salonOwner, salon) = await CreateOwnerWithCompanyAsync();
        var shop = await CreateShopAsync();
        var stays = await CreateStaysCompanyAsync();
        var baths = await CreateBathAsync();
        return new Four(salonOwner.Token, salon.Id, salon.Slug, shop.OwnerToken, shop.Id, shop.Slug, stays, baths);
    }

    // ── CY42-01: банная компания не видна в чужих каталогах и публичных страницах ──

    [Fact, TestCase("CY42-01")]
    public async Task BathsCompany_IsInvisible_InSalonShopAndStaysPublicPlaces()
    {
        var f = await CreateFourAsync();
        var res = await AddResourceAsync(f.Baths, "Баня на дровах");
        InvalidateBathsCatalog();
        InvalidateCatalog();
        var anon = AnonymousClient();

        // карточка по адресу: салон отвечает «нет такой»; остальные виды по-прежнему отдают свою карточку
        (await anon.GetAsync($"/api/companies/{f.Baths.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "банная компания не открывается салонной страницей");
        (await anon.GetAsync($"/api/companies/{f.SalonSlug}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anon.GetAsync($"/api/companies/{f.Stays.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK, "«Дома» открываются как прежде (регресс CY37-12)");

        // ни один общий список не содержит адрес бани
        foreach (var url in new[] { "/api/companies/public", "/api/companies" })
        {
            var r = await anon.GetAsync(url);
            var body = r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync() : "";
            body.Should().NotContain(f.Baths.Slug, url);
        }
        var cityId = f.Baths.CityId;
        (await (await anon.GetAsync($"/api/goods/catalog?cityId={cityId}")).Content.ReadAsStringAsync()).Should().NotContain(f.Baths.Slug, "в каталоге goods бани нет");
        var staysCatalog = await (await anon.GetAsync("/api/stays/public/catalog?pageSize=50")).Content.ReadAsStringAsync();
        staysCatalog.Should().NotContain(f.Baths.Slug).And.NotContain(res.Id.ToString(), "в каталоге «Домов» бань нет");

        // витрина магазина и публичные страницы «Домов» по адресу бани — «нет такой»
        (await anon.GetAsync($"/api/storefront/{f.Baths.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync($"/api/stays/public/companies/{f.Baths.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync($"/api/stays/public/companies/{f.Baths.Slug}/services/{res.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // и наоборот: каталог бань не содержит салон, магазин и «Дома»
        var bathsCatalog = await (await anon.GetAsync($"/api/baths/catalog?pageSize=50")).Content.ReadAsStringAsync();
        bathsCatalog.Should().NotContain(f.SalonSlug).And.NotContain(f.ShopSlug).And.NotContain(f.Stays.Slug);
    }

    // ── CY42-02: салонные маршруты отвечают банной компании отказом вида ─────────

    [Fact, TestCase("CY42-02")]
    public async Task SalonRoutes_RefuseBathsCompany_With409Text_AfterRightsCheck()
    {
        var f = await CreateFourAsync();
        var id = f.Baths.CompanyId;
        var c = AuthedClient(f.Baths.Token);

        var routes = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Post, "/api/services", new { companyId = id, name = "Стрижка", durationMinutes = 30, price = 100 }),
            (HttpMethod.Get, $"/api/companies/{id}/masters", null),
            (HttpMethod.Get, $"/api/companies/{id}/stats?from=2026-01-01&to=2026-01-31", null),
            (HttpMethod.Get, $"/api/companies/{id}/clients/u_{Guid.NewGuid()}/health-consent-form", null),
        };
        foreach (var (method, url, body) in routes)
        {
            var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = JsonContent.Create(body);
            var r = await c.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, $"{method} {url}");
            (await r.Content.ReadAsStringAsync()).Should().Contain(BathsRefusal, $"{method} {url}");
        }

        // проверка прав раньше проверки вида: посторонний получает 403, а не 409
        var stranger = await RegisterAsync();
        (await AuthedClient(stranger.Token).GetAsync($"/api/companies/{id}/stats?from=2026-01-01&to=2026-01-31"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "проверка вида идёт после проверки прав");

        // магазин и «Дома» отвечают прежними текстами — их слова не подменились
        var shopRefusal = await AuthedClient(f.ShopToken).GetAsync($"/api/companies/{f.ShopId}/masters");
        shopRefusal.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await shopRefusal.Content.ReadAsStringAsync()).Should().Be("Это магазин: записи, услуги и расписание для него недоступны.");
        var staysRefusal = await AuthedClient(f.Stays.OwnerToken).GetAsync($"/api/companies/{f.Stays.Id}/masters");
        staysRefusal.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await staysRefusal.Content.ReadAsStringAsync()).Should().Contain("Это компания «Дома»: записи, услуги и расписание для неё недоступны.");

        // маршруты заказов магазина — «нет такой» (404), как у салона и «Домов»
        (await c.GetAsync($"/api/shops/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/shops/{id}/products")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/shops/{id}/order-board")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── CY42-03: /api/baths/* видит только компании «Бани» ──────────────────────

    [Fact, TestCase("CY42-03")]
    public async Task BathsRoutes_SeeOnlyBathsCompanies_OthersAre404WithEmptyBody()
    {
        var f = await CreateFourAsync();
        var foreign = new (string Token, Guid Id, string Slug, string What)[]
        {
            (f.SalonToken, f.SalonId, f.SalonSlug, "салон"), (f.ShopToken, f.ShopId, f.ShopSlug, "магазин"), (f.Stays.OwnerToken, f.Stays.Id, f.Stays.Slug, "«Дома»")
        };
        foreach (var (token, id, slug, what) in foreign)
        {
            var c = AuthedClient(token);
            foreach (var url in new[]
                     {
                         $"/api/baths/companies/{id}", $"/api/baths/companies/{id}/services", $"/api/baths/companies/{id}/schedule", $"/api/baths/companies/{id}/revision",
                         $"/api/baths/companies/{id}/qr", $"/api/baths/companies/{id}/service-day?date={D(InDays(5))}", $"/api/baths/companies/{id}/service-sessions",
                         $"/api/baths/companies/{id}/notification-settings"
                     })
            {
                var r = await c.GetAsync(url);
                r.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{what}: {url}");
                (await r.Content.ReadAsStringAsync()).Should().BeEmpty($"{what}: 404 без тела и без оракула — {url}");
            }
            (await c.PostJsonAsync($"/api/baths/companies/{id}/services", new ServiceCreateInput("Баня"))).StatusCode.Should().Be(HttpStatusCode.NotFound, what);
            // публичная страница комплекса по адресу чужого вида
            (await AnonymousClient().GetAsync($"/api/baths/public/companies/{slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound, what);
            // «моя банная компания» этих владельцев пуста
            var mine = await J(await c.GetAsync("/api/baths/companies/my"));
            mine.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().NotContain(id, what);
        }

        // несуществующая компания неотличима от чужого вида
        var ghost = await AuthedClient(f.Baths.Token).GetAsync($"/api/baths/companies/{Guid.NewGuid()}");
        ghost.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ghost.Content.ReadAsStringAsync()).Should().BeEmpty();

        // ресурс «Дома» под банным префиксом — 404, ресурс бани в чужой банной компании — 404
        await EnableOrdersWithoutStayAsync(f.Stays);
        var staysSvc = await CreateServiceAsync(f.Stays);
        (await AnonymousClient().GetAsync($"/api/baths/public/companies/{f.Stays.Slug}/services/{staysSvc.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync($"/api/baths/public/services/{staysSvc.Id}/starts?date={D(InDays(5))}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().PostJsonAsync($"/api/baths/public/services/{staysSvc.Id}/quote",
            new { businessDate = InDays(5), startMinute = 720, hours = 2, items = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().PostJsonAsync($"/api/baths/public/services/{staysSvc.Id}/orders", BathOrderBody(InDays(5), 720, 2, 2, 4000, UniquePhone())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "заказ на услугу «Домов» через банный маршрут не создаётся");
        (await ActiveSessionsAsync(staysSvc.Id)).Should().BeEmpty();

        var other = await CreateBathAsync();
        var otherRes = await AddResourceAsync(other);
        (await AuthedClient(f.Baths.Token).GetAsync($"/api/baths/companies/{f.Baths.CompanyId}/services/{otherRes.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "чужой ресурс под своей компанией — как несуществующий");
    }

    // ── CY42-04: /api/stays/* не видит банную компанию ───────────────────────────

    [Fact, TestCase("CY42-04")]
    public async Task StaysRoutes_DoNotSeeBathsCompany_ResourceOrBooking()
    {
        var f = await CreateFourAsync();
        var res = await AddResourceAsync(f.Baths, "Чан");
        var token = await BookBathAsync(res.Id, InDays(6), 720, 2);
        var sessionId = await SessionIdOfOrderAsync(token);
        var id = f.Baths.CompanyId;
        var c = AuthedClient(f.Baths.Token);

        foreach (var url in new[]
                 {
                     $"/api/stays/companies/{id}", $"/api/stays/companies/{id}/houses", $"/api/stays/companies/{id}/board", $"/api/stays/companies/{id}/schedule",
                     $"/api/stays/companies/{id}/bookings", $"/api/stays/companies/{id}/services", $"/api/stays/companies/{id}/services/{res.Id}",
                     $"/api/stays/companies/{id}/service-sessions", $"/api/stays/companies/{id}/service-sessions/{sessionId}", $"/api/stays/companies/{id}/service-day?date={D(InDays(6))}"
                 })
        {
            var r = await c.GetAsync(url);
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
            (await r.Content.ReadAsStringAsync()).Should().BeEmpty(url);
        }
        (await c.PostJsonAsync($"/api/stays/companies/{id}/services", new ServiceCreateInput("Баня"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostJsonAsync($"/api/stays/companies/{id}/services/{res.Id}/publish", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostJsonAsync($"/api/stays/companies/{id}/services/{res.Id}/items", new ServiceItemInput("Веник", 100, 5, true))).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "позиция ресурса бани через префикс «Домов» не создаётся");
        (await c.PostJsonAsync($"/api/stays/companies/{id}/service-sessions/{sessionId}/cancel", new ExpectedVersionReasonInput(1, "x"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // публично: страница, ресурс, расчёт, заказ — 404
        var anon = AnonymousClient();
        (await anon.GetAsync($"/api/stays/public/companies/{f.Baths.Slug}/services/{res.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync($"/api/stays/public/services/{res.Id}/starts?date={D(InDays(6))}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.PostJsonAsync($"/api/stays/public/services/{res.Id}/quote", new PublicServiceQuoteInput(InDays(6), 720, 2, [], null, null, null))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await anon.PostJsonAsync($"/api/stays/public/services/{res.Id}/orders", OrderInput(InDays(7), 720, 2, 4000, UniquePhone()))).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "бронь на ресурс бани через «Дома» не создаётся");
        (await ActiveSessionsAsync(res.Id)).Should().ContainSingle("прежняя бронь цела, новой нет");
    }

    // ── CY42-05: токены брони не открываются чужими маршрутами ───────────────────

    [Fact, TestCase("CY42-05")]
    public async Task OrderTokens_DoNotCrossVerticals_InEitherDirection()
    {
        var f = await CreateFourAsync();
        var bathRes = await AddResourceAsync(f.Baths, "Сауна", prepay: 30);
        await EnableOrdersWithoutStayAsync(f.Stays);
        var staysSvc = await CreateServiceAsync(f.Stays, prepay: 30);
        var bathToken = await BookBathAsync(bathRes.Id, InDays(6), 720, 2);
        var staysToken = (await OrderOkAsync(staysSvc.Id, InDays(6), 720, 2)).Token;
        var anon = AnonymousClient();

        // бронь бани — только на банных маршрутах
        (await anon.GetAsync($"/api/baths/service-orders/public/{bathToken}")).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var url in new[] { $"/api/stays/service-orders/public/{bathToken}", $"/api/stays/bookings/public/{bathToken}", $"/api/orders/public/{bathToken}" })
        {
            var r = await anon.GetAsync(url);
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, url);
            (await r.Content.ReadAsStringAsync()).Should().BeEmpty(url);
        }
        (await anon.PostJsonAsync($"/api/stays/service-orders/public/{bathToken}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AttachOrderProofAsync(bathToken, anon)).StatusCode.Should().Be(HttpStatusCode.NotFound, "подтверждение оплаты по чужому префиксу не принимается");
        (await anon.PostJsonAsync($"/api/stays/service-orders/public/{bathToken}/push-subscription",
            new PushSubscriptionInput($"https://push.example.test/cy42-iso/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // и наоборот
        (await anon.GetAsync($"/api/stays/service-orders/public/{staysToken}")).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var url in new[] { $"/api/baths/service-orders/public/{staysToken}", $"/api/orders/public/{staysToken}" })
            (await anon.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
        (await anon.PostJsonAsync($"/api/baths/service-orders/public/{staysToken}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.PostAsync($"/api/baths/service-orders/public/{staysToken}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "c.jpg")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // после попыток через чужой префикс обе брони живы и не изменились
        (await BathOrderPageAsync(bathToken)).GetProperty("status").GetString().Should().Be("Held");
        (await GetOrderAsync(staysToken)).Status.Should().Be(StayBookingStatus.Held);

        // токен владельца компании одного вида не даёт прав в компании другого вида (у владельца бани нет прав над «Домами» и наоборот)
        var staysSession = await SessionIdOfOrderAsync(staysToken);
        (await AuthedClient(f.Baths.Token).GetAsync($"/api/stays/companies/{f.Stays.Id}/service-sessions/{staysSession}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var bathSession = await SessionIdOfOrderAsync(bathToken);
        (await AuthedClient(f.Stays.OwnerToken).GetAsync($"/api/baths/companies/{f.Baths.CompanyId}/service-sessions/{bathSession}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── CY42-06: push-сайты ─────────────────────────────────────────────────────

    [Fact, TestCase("CY42-06")]
    public async Task PushSites_BathsIsItsOwnSite_ConfigListsAndUrlsDoNotMix()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var owner = await RegisterAsync();
        var bath = await CreateBathAsync(owner: owner);
        var stays = await CreateStaysCompanyAsync(ownerAccount: owner);
        var client = host.Client(bath.Token);

        string Endpoint(string tag) => $"https://push.example.test/cy42-site/{tag}/{Guid.NewGuid():N}";
        async Task<JsonElement> Subscribe(string site, string tag)
        {
            var r = await client.PostAsJsonAsync("/api/push/subscriptions", new { endpoint = Endpoint(tag), keys = new { p256dh = "p256dh-key", auth = "auth-key" }, deviceLabel = "Chrome", site });
            r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
            return await J(r);
        }
        var bathsSub = await Subscribe("Baths", "baths");
        bathsSub.GetProperty("site").GetString().Should().Be("Baths");
        var staysSub = await Subscribe("Stays", "stays");
        staysSub.GetProperty("site").GetString().Should().Be("Stays");

        // подписки сайта видны только на своём сайте
        var onBaths = (await J(await client.GetAsync("/api/push/subscriptions?site=Baths"))).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        onBaths.Should().Contain(bathsSub.GetProperty("id").GetGuid()).And.NotContain(staysSub.GetProperty("id").GetGuid());
        var onStays = (await J(await client.GetAsync("/api/push/subscriptions?site=Stays"))).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        onStays.Should().Contain(staysSub.GetProperty("id").GetGuid()).And.NotContain(bathsSub.GetProperty("id").GetGuid());
        var onSalon = (await J(await client.GetAsync("/api/push/subscriptions"))).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        onSalon.Should().NotContain([bathsSub.GetProperty("id").GetGuid(), staysSub.GetProperty("id").GetGuid()], "подписки ezbook, «Домов» и «Бань» — разные сайты");

        // настройка: на сайте «Бани» — только банные компании; ссылки на сайты — все четыре
        var cfg = await J(await client.GetAsync("/api/push/config?site=Baths"));
        cfg.GetProperty("companies").EnumerateArray().Select(x => x.GetProperty("companyId").GetGuid()).Should().Equal(new[] { bath.CompanyId });
        cfg.GetProperty("siteUrls").GetProperty("baths").GetString().Should().Be("https://bani.ezbook.ru");
        cfg.GetProperty("siteUrls").GetProperty("stays").GetString().Should().Be("https://dom.ezbook.ru");
        (await J(await client.GetAsync("/api/push/config?site=Stays"))).GetProperty("companies").EnumerateArray().Select(x => x.GetProperty("companyId").GetGuid())
            .Should().Equal(new[] { stays.Id });
        (await J(await client.GetAsync("/api/push/config?allSites=true"))).GetProperty("companies").EnumerateArray().Select(x => x.GetProperty("companyId").GetGuid())
            .Should().Contain([bath.CompanyId, stays.Id]);

        // неизвестный сайт — 400
        (await client.GetAsync("/api/push/config?site=Saunas")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/push/subscriptions", new { endpoint = Endpoint("bad"), keys = new { p256dh = "k", auth = "a" }, site = 99 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── CY42-07: ссылки вертикалей ──────────────────────────────────────────────

    [Fact, TestCase("CY42-07")]
    public async Task Links_EachKindPointsToItsOwnSite()
    {
        var owner = await RegisterAsync();
        var bath = await CreateBathAsync(owner: owner);
        var stays = await CreateStaysCompanyAsync(ownerAccount: owner);

        var summary = await J(await AuthedClient(bath.Token).GetAsync("/api/companies/kinds-summary"));
        summary.GetProperty("baths").GetProperty("siteUrl").GetString().Should().Be("https://bani.ezbook.ru");
        summary.GetProperty("baths").GetProperty("count").GetInt32().Should().Be(1);
        summary.GetProperty("stays").GetProperty("siteUrl").GetString().Should().Be("https://dom.ezbook.ru");
        summary.GetProperty("stays").GetProperty("count").GetInt32().Should().Be(1);
        summary.GetProperty("orders").GetProperty("siteUrl").GetString().Should().StartWith("https://goods.");

        var card = await J(await AuthedClient(bath.Token).GetAsync($"/api/baths/companies/{bath.CompanyId}"));
        card.GetProperty("publicUrl").GetString().Should().Be($"https://bani.ezbook.ru/{bath.Slug}");
        var res = await AddResourceAsync(bath);
        var service = await J(await AuthedClient(bath.Token).GetAsync($"/api/baths/companies/{bath.CompanyId}/services/{res.Id}"));
        service.GetProperty("publicUrl").GetString().Should().Be($"https://bani.ezbook.ru/{bath.Slug}/{res.Slug}");

        var token = await BookBathAsync(res.Id, InDays(6), 720, 2);
        // ссылка на страницу брони в мессенджере/выгрузке идёт на bani, не на dom
        var page = await BathOrderPageAsync(token);
        page.GetRawText().Should().NotContain("dom.ezbook.ru");

        // QR комплекса — PNG (адрес bani проверяется по контракту; здесь — что отдаётся картинка владельцу бани, а постороннему — 404)
        var qr = await AuthedClient(bath.Token).GetAsync($"/api/baths/companies/{bath.CompanyId}/qr");
        qr.StatusCode.Should().Be(HttpStatusCode.OK);
        qr.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var stranger = await RegisterAsync();
        (await AuthedClient(stranger.Token).GetAsync($"/api/baths/companies/{bath.CompanyId}/qr")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // «Мои компании» разных видов не смешиваются
        var mineStays = await J(await AuthedClient(stays.OwnerToken).GetAsync("/api/stays/companies/my"));
        mineStays.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().Equal(new[] { stays.Id });
        var mineBaths = await J(await AuthedClient(bath.Token).GetAsync("/api/baths/companies/my"));
        mineBaths.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().Equal(new[] { bath.CompanyId });
        mineBaths.EnumerateArray().Single().GetProperty("publicUrl").GetString().Should().Be($"https://bani.ezbook.ru/{bath.Slug}");
        var salonDefault = await J(await AuthedClient((await LoginAsync(owner.Phone, "Password123!")).Token).GetAsync("/api/companies/my"));
        salonDefault.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().NotContain([bath.CompanyId, stays.Id], "без параметра kind — салоны");
    }

    // ── CY42-08: один аккаунт — четыре вида ─────────────────────────────────────

    [Fact, TestCase("CY42-08")]
    public async Task OneAccount_OwnsAllFourKinds_EachSeesOnlyItself_AndNoneConsumesAnothersLimits()
    {
        var (owner, salon) = await CreateOwnerWithCompanyAsync();
        var (shopToken, shop) = await CreateShopForAsync(owner.Token);
        var stays = await CreateStaysCompanyForAsync(shopToken, Unique("dom-all-"));
        var bath = await CreateBathAsync(owner: owner);
        var token = (await LoginAsync(owner.Phone, "Password123!")).Token;
        var c = AuthedClient(token);

        var salons = (await J(await c.GetAsync("/api/companies/my"))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        salons.Should().Contain(salon.Id).And.NotContain([shop.Id, stays.Company.Id, bath.CompanyId]);
        var shops = (await J(await c.GetAsync("/api/companies/my?kind=Orders"))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        shops.Should().Contain(shop.Id).And.NotContain([salon.Id, stays.Company.Id, bath.CompanyId]);
        var houses = (await J(await c.GetAsync("/api/stays/companies/my"))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        houses.Should().ContainSingle().Which.Should().Be(stays.Company.Id);
        var baths = (await J(await c.GetAsync("/api/baths/companies/my"))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        baths.Should().ContainSingle().Which.Should().Be(bath.CompanyId);
        var byKind = (await J(await c.GetAsync("/api/companies/my?kind=Baths"))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        byKind.Should().Contain(bath.CompanyId).And.NotContain([salon.Id, shop.Id, stays.Company.Id]);

        // бани не потребили салонный лимит, магазин работает, «Дома» — тоже
        (await c.PostAsJsonAsync("/api/services", new { companyId = salon.Id, name = "Стрижка", durationMinutes = 30, price = 100 })).StatusCode
            .Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        (await c.GetAsync($"/api/shops/{shop.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuthedClient(stays.Token).GetAsync($"/api/stays/companies/{stays.Company.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuthedClient(bath.Token).GetAsync($"/api/baths/companies/{bath.CompanyId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // вторая банная компания того же аккаунта создаётся (лимита числа компаний у линейки нет) и не трогает остальные виды
        var second = await CreateBathAsync(owner: owner, trial: false);
        var after = (await J(await AuthedClient(second.Token).GetAsync("/api/baths/companies/my"))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        after.Should().BeEquivalentTo([bath.CompanyId, second.CompanyId]);
        (await J(await c.GetAsync("/api/stays/companies/my"))).GetArrayLength().Should().Be(1);
    }

    // ── CY42-09: общее пространство адресов и каталоги ──────────────────────────

    [Fact, TestCase("CY42-09")]
    public async Task Slugs_AreOneNamespaceForAllKinds_AndCatalogsDoNotLeakResources()
    {
        var f = await CreateFourAsync();
        var owner = await RegisterAsync();

        // адрес банной компании занят в любом виде — и наоборот
        async Task<HttpResponseMessage> CreateBaths(string slug) => await AuthedClient(owner.Token).PostJsonAsync("/api/baths/companies", new
        {
            name = Unique("Баня "), slug, cityId = await AnyCityIdAsync(), address = "Шерегеш", phone = "+79001112233", description = (string?)null,
            ownerTermsVersion = CurrentOwnerTermsDto().Version, trialTermsVersion = (string?)null
        });
        foreach (var taken in new[] { f.SalonSlug, f.ShopSlug, f.Stays.Slug, f.Baths.Slug.ToUpperInvariant() })
        {
            var r = await CreateBaths(taken);
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, $"адрес «{taken}» занят: {await r.Content.ReadAsStringAsync()}");
            (await J(r)).GetProperty("code").GetString().Should().Be("SlugTaken");
        }
        var staysTaken = await AuthedClient(owner.Token).PostJsonAsync("/api/stays/companies",
            new StaysCompanyCreateInput(Unique("Дома "), f.Baths.Slug, null, "+79001112233", CurrentOwnerTermsDto().Version, null));
        staysTaken.StatusCode.Should().Be(HttpStatusCode.Conflict, "адрес бани не отдаётся «Домам»");

        // резерв слов bani: служебные адреса нельзя занять
        foreach (var reserved in new[] { "cabinet", "s", "bookings", "privacy" })
        {
            var r = await CreateBaths(reserved);
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, reserved);
            (await J(r)).GetProperty("code").GetString().Should().BeOneOf("SlugReserved", "SlugInvalid", "SlugTaken");
        }

        // каталоги: ресурс бани и услуга «Домов» живут каждый в своём
        var bathRes = await AddResourceAsync(f.Baths, "Баня каталога");
        await EnableOrdersWithoutStayAsync(f.Stays);
        var staysSvc = await CreateServiceAsync(f.Stays);
        InvalidateBathsCatalog();
        InvalidateCatalog();
        CatalogIds(await CatalogAsync($"?cityId={f.Baths.CityId}&pageSize=50")).Should().Contain(bathRes.Id).And.NotContain(staysSvc.Id);
        var domCatalog = await (await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50")).Content.ReadAsStringAsync();
        domCatalog.Should().NotContain(bathRes.Id.ToString()).And.NotContain(bathRes.Slug);
    }
}
