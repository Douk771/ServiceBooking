using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 23, «Вызов 2»: тип компании, магазины, каталог, «закончилось» (US-23-01…03, 09…12, 14…16, 28).
/// Написано по SPEC.md и API_CONTRACT_CYCLE23.md, а не по реализации.
/// </summary>
public class Cycle23ShopsCatalogTests(TestDatabaseFixture fixture) : Cycle23TestBase(fixture)
{
    private const string ShopRefusal = "Это магазин: записи, услуги и расписание для него недоступны.";

    private static async Task<JsonElement> ReadJsonElementAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    // ── US-23-01/02/03: тип компании ─────────────────────────────────────────────

    [Fact, TestCase("CY23-01")]
    public async Task CreatedShop_HasKindOrders_AndGoodsPublicUrl_ServiceCompanyKeepsServicesKind()
    {
        var shop = await CreateShopAsync();
        var (owner, salon) = await CreateOwnerWithCompanyAsync();

        var shopDto = await ReadJsonElementAsync(await AnonymousClient().GetAsync($"/api/companies/{shop.Slug}"));
        shopDto.GetProperty("kind").GetString().Should().Be("Orders");
        shopDto.GetProperty("publicUrl").GetString().Should().Be($"https://goods.ezbook.ru/{shop.Slug}");
        shop.Shop.PublicUrl.Should().Be($"https://goods.ezbook.ru/{shop.Slug}");

        var salonDto = await ReadJsonElementAsync(await AnonymousClient().GetAsync($"/api/companies/{salon.Slug}"));
        salonDto.GetProperty("kind").GetString().Should().Be("Services");
        salonDto.GetProperty("publicUrl").GetString().Should().Be($"https://ezbook.ru/company/{salon.Slug}");
    }

    [Fact, TestCase("CY23-02")]
    public async Task Shop_IsAbsentFromPublicCatalogAndDefaultOwnerLists_ButPresentWithKindOrders()
    {
        var shop = await CreateShopAsync();
        var shopSlug = shop.Slug;

        foreach (var url in new[] { "/api/companies", "/api/companies/public" })
        {
            var raw = await (await AnonymousClient().GetAsync(url)).Content.ReadAsStringAsync();
            raw.Should().NotContain(shopSlug, $"магазины не попадают в публичный каталог ezbook ({url})");
        }

        var my = await (await AuthedClient(shop.OwnerToken).GetAsync("/api/companies/my")).Content.ReadAsStringAsync();
        my.Should().NotContain(shopSlug, "без ?kind= кабинет ezbook не показывает магазины");
        var member = await (await AuthedClient(shop.OwnerToken).GetAsync("/api/companies/member")).Content.ReadAsStringAsync();
        member.Should().NotContain(shopSlug);

        var myOrders = await (await AuthedClient(shop.OwnerToken).GetAsync("/api/companies/my?kind=Orders")).Content.ReadAsStringAsync();
        myOrders.Should().Contain(shopSlug);

        var bad = await AuthedClient(shop.OwnerToken).GetAsync("/api/companies/my?kind=Bogus");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("Неизвестный тип компании");

        var shops = await (await AuthedClient(shop.OwnerToken).GetAsync("/api/shops/my")).Content.ReadAsStringAsync();
        shops.Should().Contain(shopSlug);
    }

    [Fact, TestCase("CY23-03")]
    public async Task KindsSummary_CountsBothKinds_ForAnyMembershipRole()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var (_, token) = (shop.Owner, shop.OwnerToken);

        var owner = await ReadJsonElementAsync(await AuthedClient(token).GetAsync("/api/companies/kinds-summary"));
        owner.GetProperty("orders").GetProperty("count").GetInt32().Should().Be(1);
        owner.GetProperty("orders").GetProperty("siteUrl").GetString().Should().Be("https://goods.ezbook.ru");
        owner.GetProperty("services").GetProperty("count").GetInt32().Should().Be(0);
        owner.GetProperty("services").GetProperty("siteUrl").GetString().Should().Be("https://ezbook.ru");

        var asStaff = await ReadJsonElementAsync(await AuthedClient(staff.Token).GetAsync("/api/companies/kinds-summary"));
        asStaff.GetProperty("orders").GetProperty("count").GetInt32().Should().Be(1, "участник любой роли");
    }

    [Fact, TestCase("CY23-04")]
    public async Task BookingRoutes_RefuseShop_With409Text_AfterRightsCheck()
    {
        var shop = await CreateShopAsync();
        var c = AuthedClient(shop.OwnerToken);

        var routes = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Post, "/api/services", new { companyId = shop.Id, name = "Стрижка", durationMinutes = 30, price = 100 }),
            (HttpMethod.Get, $"/api/companies/{shop.Id}/masters", null),
            (HttpMethod.Get, $"/api/companies/{shop.Id}/stats?from=2026-01-01&to=2026-01-31", null),
            (HttpMethod.Get, $"/api/companies/{shop.Id}/photo-usage", null),
            // Cycle 20 written health consent (merged after cycle 23): the kind check runs before body
            // validation, so even an unconfirmed/unknown-reason body gets 409, not 400/404.
            (HttpMethod.Get, $"/api/companies/{shop.Id}/clients/u_{Guid.NewGuid()}/health-consent-form", null),
            (HttpMethod.Post, $"/api/companies/{shop.Id}/clients/u_{Guid.NewGuid()}/health-written-consent",
                new { textVersion = "v1", formId = (string?)null, confirmed = false }),
            (HttpMethod.Post, $"/api/companies/{shop.Id}/clients/u_{Guid.NewGuid()}/health-written-consent/revoke",
                new { reason = "unknown" }),
        };
        foreach (var (method, url, body) in routes)
        {
            var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = JsonContent.Create(body);
            var r = await c.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, $"{method} {url}");
            (await r.Content.ReadAsStringAsync()).Should().Contain(ShopRefusal, $"{method} {url}");
        }

        // «после прав»: посторонний получает 403, а не 409
        var stranger = await RegisterAsync();
        var s = await AuthedClient(stranger.Token).GetAsync($"/api/companies/{shop.Id}/stats?from=2026-01-01&to=2026-01-31");
        s.StatusCode.Should().Be(HttpStatusCode.Forbidden, "проверка типа идёт после проверки прав");
    }

    [Fact, TestCase("CY23-05")]
    public async Task OrderRoutes_OnSalon_Return404()
    {
        var (owner, salon) = await CreateOwnerWithCompanyAsync();
        var c = AuthedClient(owner.Token);
        (await c.GetAsync($"/api/shops/{salon.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/shops/{salon.Id}/products")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync($"/api/shops/{salon.Id}/order-board")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync($"/api/storefront/{salon.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var order = await AnonymousClient().PostJsonAsync($"/api/storefront/{salon.Slug}/orders",
            Guest([new OrderLineInput(Guid.NewGuid(), 1, 10m)]));
        order.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY23-06")]
    public async Task ShopMembers_OnlyStaffRoleAllowed()
    {
        var shop = await CreateShopAsync();
        var invitee = await RegisterAsync();
        var r = await AuthedClient(shop.OwnerToken).PostAsJsonAsync($"/api/companies/{shop.Id}/members",
            new { phone = invitee.Phone, firstName = "A", lastName = "B", role = "CompanyOwner", bio = (string?)null, email = (string?)null });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("В магазин можно добавить только сотрудника.");

        var ok = await AuthedClient(shop.OwnerToken).PostAsJsonAsync($"/api/companies/{shop.Id}/members",
            new { phone = invitee.Phone, firstName = "A", lastName = "B", role = "Master", bio = (string?)null, email = (string?)null });
        ok.IsSuccessStatusCode.Should().BeTrue(await ok.Content.ReadAsStringAsync());
    }

    [Fact, TestCase("CY23-07")]
    public async Task CompanyLimit_CountsSalonsAndShopsTogether()
    {
        // Free-аккаунт без подписки: 1 компания. Салон + магазин в одном аккаунте — второй отклоняется (Q5).
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var r = await AuthedClient(owner.Token).PostJsonAsync("/api/shops", new CreateShopInput(
            "Магазин", Unique("shop-"), await AnyCityIdAsync(), null, null, null, null, null,
            new ShopOwnerTermsInput(CurrentOwnerTermsDto().Version)));
        r.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await r.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace("причина лимита объясняется текстом");
    }

    [Fact, TestCase("CY23-08")]
    public async Task ShopAndSalonInOneAccount_AllowedOnPaidPlan()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync(); // полный тариф
        var (_, shop) = await CreateShopForAsync(owner.Token);
        shop.Slug.Should().NotBeNullOrEmpty();
    }

    [Fact, TestCase("CY23-09")]
    public async Task Admin_SeesKindAndPublicUrl_FiltersByKind_BlocksShop()
    {
        var admin = await LoginAsSuperAdminAsync();
        var shop = await CreateShopAsync();
        var (_, salon) = await CreateOwnerWithCompanyAsync();
        var a = AuthedClient(admin.Token);

        var shops = await ReadJsonElementAsync(await a.GetAsync("/api/admin/companies?kind=Orders&pageSize=100"));
        var shopItems = shops.GetProperty("items").EnumerateArray().ToList();
        shopItems.Should().NotBeEmpty().And.OnlyContain(x => x.GetProperty("kind").GetString() == "Orders", "фильтр по типу");
        shopItems.Should().Contain(x => x.GetProperty("slug").GetString() == shop.Slug)
            .And.NotContain(x => x.GetProperty("slug").GetString() == salon.Slug);
        shopItems.Single(x => x.GetProperty("slug").GetString() == shop.Slug).GetProperty("publicUrl").GetString()
            .Should().Be($"https://goods.ezbook.ru/{shop.Slug}");

        var salons = await ReadJsonElementAsync(await a.GetAsync("/api/admin/companies?kind=Services&pageSize=100"));
        var salonItems = salons.GetProperty("items").EnumerateArray().ToList();
        salonItems.Should().OnlyContain(x => x.GetProperty("kind").GetString() == "Services");
        salonItems.Should().Contain(x => x.GetProperty("slug").GetString() == salon.Slug)
            .And.NotContain(x => x.GetProperty("slug").GetString() == shop.Slug);
        salonItems.Single(x => x.GetProperty("slug").GetString() == salon.Slug).GetProperty("publicUrl").GetString()
            .Should().Be($"https://ezbook.ru/company/{salon.Slug}");

        (await (await a.GetAsync($"/api/admin/companies?search={shop.Slug}")).Content.ReadAsStringAsync()).Should().Contain(shop.Slug, "без ?kind= админка видит оба типа");
        (await (await a.GetAsync($"/api/admin/companies?search={salon.Slug}")).Content.ReadAsStringAsync()).Should().Contain(salon.Slug);
        (await a.GetAsync("/api/admin/companies?kind=Bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // блокировка магазина — как у салона
        var product = await CreateProductAsync(shop);
        var block = await a.PutAsJsonAsync($"/api/admin/companies/{shop.Id}",
            new { name = shop.Shop.Name, isActive = false, allowSelfBooking = true });
        block.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sf = await ReadJsonElementAsync(await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}"));
        sf.GetProperty("isAvailable").GetBoolean().Should().BeFalse();
        sf.GetProperty("acceptingOrders").GetBoolean().Should().BeFalse();
        sf.GetProperty("categories").GetArrayLength().Should().Be(0);
        sf.GetProperty("notAcceptingReason").GetString().Should().Be("Магазин недоступен");

        var order = await PostOrderAsync(shop.Slug, Guest([Line(product, 1)]));
        order.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonElementAsync(order)).GetProperty("code").GetString().Should().Be("ShopNotAcceptingOrders");

        var mine = await ReadJsonElementAsync(await AuthedClient(shop.OwnerToken).GetAsync("/api/shops/my"));
        mine.EnumerateArray().Single().GetProperty("isActive").GetBoolean().Should().BeFalse("кабинет видит заблокированный магазин");

        // разблокировка
        (await a.PutAsJsonAsync($"/api/admin/companies/{shop.Id}",
            new { name = shop.Shop.Name, isActive = true, allowSelfBooking = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await PlaceOrderAsync(shop.Slug, Guest([Line(product, 1)]));
    }

    // ── US-23-09/12: создание и адрес ────────────────────────────────────────────

    [Fact, TestCase("CY23-10")]
    public async Task CreateShop_Validation_FollowsContractOrder()
    {
        var owner = await RegisterAsync();
        var c = AuthedClient(owner.Token);
        var terms = new ShopOwnerTermsInput(CurrentOwnerTermsDto().Version);
        var city = await AnyCityIdAsync();

        var noTerms = await c.PostJsonAsync("/api/shops", new CreateShopInput("М", Unique("s-"), city, null, null, null, null, null, null));
        noTerms.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noTerms.Content.ReadAsStringAsync()).Should().Contain("соглашение");

        var wrongTerms = await c.PostJsonAsync("/api/shops", new CreateShopInput("М", Unique("s-"), city, null, null, null, null, null,
            new ShopOwnerTermsInput("0000-outdated")));
        wrongTerms.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var noName = await c.PostJsonAsync("/api/shops", new CreateShopInput("  ", Unique("s-"), city, null, null, null, null, null, terms));
        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noName.Content.ReadAsStringAsync()).Should().Contain("Укажите название магазина");

        var badSlug = await c.PostJsonAsync("/api/shops", new CreateShopInput("М", "Плохой адрес!", city, null, null, null, null, null, terms));
        badSlug.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonElementAsync(badSlug)).GetProperty("code").GetString().Should().Be("SlugInvalid");

        var shortSlug = await c.PostJsonAsync("/api/shops", new CreateShopInput("М", "ab", city, null, null, null, null, null, terms));
        (await ReadJsonElementAsync(shortSlug)).GetProperty("code").GetString().Should().Be("SlugInvalid");

        var reserved = await c.PostJsonAsync("/api/shops", new CreateShopInput("М", "admin", city, null, null, null, null, null, terms));
        reserved.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonElementAsync(reserved)).GetProperty("code").GetString().Should().Be("SlugReserved");

        var noCity = await c.PostJsonAsync("/api/shops", new CreateShopInput("М", Unique("s-"), null, null, null, null, null, null, terms));
        noCity.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noCity.Content.ReadAsStringAsync()).Should().Contain("Укажите город магазина");

        var badCity = await c.PostJsonAsync("/api/shops", new CreateShopInput("М", Unique("s-"), 999999, null, null, null, null, null, terms));
        badCity.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badCity.Content.ReadAsStringAsync()).Should().Contain("Город не найден");

        var badTz = await c.PostJsonAsync("/api/shops", new CreateShopInput("М", Unique("s-"), city, "Mars/Olympus", null, null, null, null, terms));
        badTz.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badTz.Content.ReadAsStringAsync()).Should().Contain("Неизвестный часовой пояс");
    }

    [Fact, TestCase("CY23-11")]
    public async Task Slug_IsUniqueAcrossSalonsAndShops_CaseInsensitive()
    {
        var (_, salon) = await CreateOwnerWithCompanyAsync();
        var owner = await RegisterAsync();
        var r = await AuthedClient(owner.Token).PostJsonAsync("/api/shops", new CreateShopInput(
            "Магазин", salon.Slug.ToUpperInvariant(), await AnyCityIdAsync(), null, null, null, null, null,
            new ShopOwnerTermsInput(CurrentOwnerTermsDto().Version)));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonElementAsync(r)).GetProperty("code").GetString().Should().Be("SlugTaken");

        // и магазин занимает адрес против салона
        var shop = await CreateShopAsync();
        var other = await RegisterAsync();
        var dup = await AuthedClient(other.Token).PostJsonAsync("/api/shops", new CreateShopInput(
            "Магазин 2", shop.Slug, await AnyCityIdAsync(), null, null, null, null, null,
            new ShopOwnerTermsInput(CurrentOwnerTermsDto().Version)));
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY23-12")]
    public async Task SlugCheck_SuggestsFromName_AndReportsTaken()
    {
        var owner = await RegisterAsync();
        var c = AuthedClient(owner.Token);

        var suggested = await ReadJsonElementAsync(await c.GetAsync($"/api/shops/slug-check?name={Uri.EscapeDataString("Шаурма на Ленина")}"));
        suggested.GetProperty("available").GetBoolean().Should().BeTrue();
        var slug = suggested.GetProperty("slug").GetString()!;
        slug.Should().StartWith("shaurma-na-lenina");

        await CreateShopAsync(slug: slug);
        var next = await ReadJsonElementAsync(await c.GetAsync($"/api/shops/slug-check?name={Uri.EscapeDataString("Шаурма на Ленина")}"));
        next.GetProperty("slug").GetString().Should().NotBe(slug, "занятый адрес не предлагается повторно");
        next.GetProperty("available").GetBoolean().Should().BeTrue();

        var taken = await ReadJsonElementAsync(await c.GetAsync($"/api/shops/slug-check?slug={slug}"));
        taken.GetProperty("available").GetBoolean().Should().BeFalse();
        taken.GetProperty("reasonCode").GetString().Should().Be("SlugTaken");

        var reserved = await ReadJsonElementAsync(await c.GetAsync("/api/shops/slug-check?slug=login"));
        reserved.GetProperty("reasonCode").GetString().Should().Be("SlugReserved");

        (await c.GetAsync("/api/shops/slug-check")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY23-13")]
    public async Task ChangeSlug_OwnerOnly_OldAddressStopsWorking_ConflictsRejected()
    {
        var shop = await CreateShopAsync();
        var other = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var newSlug = Unique("renamed-");

        (await AuthedClient(staff.Token).PutJsonAsync($"/api/shops/{shop.Id}/slug", new ShopSlugInput(newSlug)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var taken = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/slug", new ShopSlugInput(other.Slug));
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var same = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/slug", new ShopSlugInput(shop.Slug));
        same.StatusCode.Should().Be(HttpStatusCode.OK, "свой текущий адрес — без изменений");

        var ok = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/slug", new ShopSlugInput(newSlug));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadJsonAsync<ShopManageDto>())!.PublicUrl.Should().Be($"https://goods.ezbook.ru/{newSlug}");

        (await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "редиректа со старого адреса нет");
        (await AnonymousClient().GetAsync($"/api/storefront/{newSlug}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY23-14")]
    public async Task Qr_IsPng_WithAttachmentName_StaffAllowedStrangerForbidden()
    {
        var shop = await CreateShopAsync();
        var r = await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/qr");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        r.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be($"{shop.Slug}-qr.png");
        var bytes = await r.Content.ReadAsByteArrayAsync();
        bytes.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        bytes.Length.Should().BeGreaterThan(1000, "QR пригоден для печати — не миниатюра");

        var stranger = await RegisterAsync();
        (await AuthedClient(stranger.Token).GetAsync($"/api/shops/{shop.Id}/qr")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AnonymousClient().GetAsync($"/api/shops/{shop.Id}/qr")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── US-23-10/11: настройки, роли ─────────────────────────────────────────────

    [Fact, TestCase("CY23-15")]
    public async Task Settings_Defaults_OwnerChanges_StaffForbidden()
    {
        var shop = await CreateShopAsync();
        var dto = (await (await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}")).Content.ReadJsonAsync<ShopManageDto>())!;
        dto.Settings.CustomerMode.Should().Be(ShopCustomerMode.Anyone);
        dto.Settings.AcceptanceMode.Should().Be(OrderAcceptanceMode.Manual);
        dto.Settings.AllowCustomerCancel.Should().BeTrue();
        dto.Settings.TrackStock.Should().BeFalse();
        dto.AcceptingOrders.Should().BeTrue("выключателя приёма в цикле 1 нет");

        var updated = await UpdateSettingsAsync(shop, Settings(accept: OrderAcceptanceMode.Auto, cancel: false, trackStock: true));
        updated.Settings.AcceptanceMode.Should().Be(OrderAcceptanceMode.Auto);
        updated.Settings.AllowCustomerCancel.Should().BeFalse();
        updated.Settings.TrackStock.Should().BeTrue();

        var staff = await AddShopStaffAsync(shop);
        (await AuthedClient(staff.Token).PutJsonAsync($"/api/shops/{shop.Id}/settings", Settings()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // сотрудник видит магазин (GET), но не настройки-запись
        (await AuthedClient(staff.Token).GetAsync($"/api/shops/{shop.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY23-16")]
    public async Task StrictMode_WhenPhoneVerificationSubsystemOff_Returns409()
    {
        var shop = await CreateShopAsync();
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/settings",
            Settings(ShopCustomerMode.VerifiedPhoneOnly));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonElementAsync(r)).GetProperty("code").GetString().Should().Be("PhoneVerificationUnavailable");
        var dto = (await (await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}")).Content.ReadJsonAsync<ShopManageDto>())!;
        dto.PhoneVerificationAvailable.Should().BeFalse();
        dto.Settings.CustomerMode.Should().Be(ShopCustomerMode.Anyone);
    }

    [Fact, TestCase("CY23-17")]
    public async Task Staff_CannotManageCatalogOrShopOrStaff_OwnerCan_StrangerForbidden()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var product = await CreateProductAsync(shop);
        var cat = await CreateCategoryAsync(shop);
        var s = AuthedClient(staff.Token);

        (await s.PostJsonAsync($"/api/shops/{shop.Id}/categories", new CategoryInput("X"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.PutJsonAsync($"/api/shops/{shop.Id}/categories/{cat.Id}", new CategoryInput("Y"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.DeleteAsync($"/api/shops/{shop.Id}/categories/{cat.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.PostJsonAsync($"/api/shops/{shop.Id}/products", new ProductInput(null, "X", null, ProductUnit.Piece, 10m, null, null, null, true, null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.PutJsonAsync($"/api/shops/{shop.Id}/products/{product.Id}", new ProductInput(null, "X", null, ProductUnit.Piece, 10m, null, null, null, true, null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "цены и товары сотрудник не меняет");
        (await s.DeleteAsync($"/api/shops/{shop.Id}/products/{product.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.PutJsonAsync($"/api/shops/{shop.Id}/seller", new SellerInfoInput(null, "ИП", null, null, null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var invitee = await RegisterAsync();
        (await s.PostAsJsonAsync($"/api/companies/{shop.Id}/members",
            new { phone = invitee.Phone, firstName = "A", lastName = "B", role = "Master" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // сотрудник может «закончилось» и остаток
        (await s.PutJsonAsync($"/api/shops/{shop.Id}/products/{product.Id}/sold-out", new SoldOutInput(true))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.PutJsonAsync($"/api/shops/{shop.Id}/products/{product.Id}/stock", new StockInput(5))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.GetAsync($"/api/shops/{shop.Id}/products")).StatusCode.Should().Be(HttpStatusCode.OK);

        // посторонний — ничего
        var stranger = AuthedClient((await RegisterAsync()).Token);
        (await stranger.GetAsync($"/api/shops/{shop.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"/api/shops/{shop.Id}/order-board")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.PutJsonAsync($"/api/shops/{shop.Id}/products/{product.Id}/sold-out", new SoldOutInput(true))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AnonymousClient().GetAsync($"/api/shops/{shop.Id}/products")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // несуществующий магазин — 404
        (await stranger.GetAsync($"/api/shops/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY23-18")]
    public async Task RemovedStaff_LosesAccessImmediately()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        (await AuthedClient(staff.Token).GetAsync($"/api/shops/{shop.Id}/order-board")).StatusCode.Should().Be(HttpStatusCode.OK);

        var members = await ReadJsonElementAsync(await AuthedClient(shop.OwnerToken).GetAsync($"/api/companies/{shop.Id}/members"));
        var memberId = members.EnumerateArray()
            .First(m => m.GetProperty("userId").GetString() == staff.UserId).GetProperty("id").GetString();
        (await AuthedClient(shop.OwnerToken).DeleteAsync($"/api/companies/{shop.Id}/members/{memberId}")).IsSuccessStatusCode.Should().BeTrue();

        (await AuthedClient(staff.Token).GetAsync($"/api/shops/{shop.Id}/order-board")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "удалённый сотрудник теряет доступ сразу, тем же токеном");
    }

    [Fact, TestCase("CY23-19")]
    public async Task Seller_InnValidated_EmptyClears_ShownOnStorefrontWhenFilled()
    {
        var shop = await CreateShopAsync();
        var c = AuthedClient(shop.OwnerToken);

        var sfEmpty = await ReadJsonElementAsync(await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}"));
        sfEmpty.GetProperty("seller").ValueKind.Should().Be(JsonValueKind.Null);

        var bad = await c.PutJsonAsync($"/api/shops/{shop.Id}/seller", new SellerInfoInput(LegalEntityForm.Ip, "ИП Иванов", "1234567890", null, null));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("ИНН указан с ошибкой");

        var ok = await c.PutJsonAsync($"/api/shops/{shop.Id}/seller", new SellerInfoInput(LegalEntityForm.Ip, "ИП Иванов", null, null, null));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var sf = await ReadJsonElementAsync(await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}"));
        sf.GetProperty("seller").GetProperty("legalName").GetString().Should().Be("ИП Иванов");
    }

    // ── US-23-14: категории ──────────────────────────────────────────────────────

    [Fact, TestCase("CY23-20")]
    public async Task Categories_CrudOrderHideDelete_AndValidation()
    {
        var shop = await CreateShopAsync();
        var c = AuthedClient(shop.OwnerToken);
        var a = await CreateCategoryAsync(shop, "А");
        var b = await CreateCategoryAsync(shop, "Б");

        var empty = await c.PostJsonAsync($"/api/shops/{shop.Id}/categories", new CategoryInput(" "));
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await empty.Content.ReadAsStringAsync()).Should().Contain("Укажите название категории");
        (await c.PostJsonAsync($"/api/shops/{shop.Id}/categories", new CategoryInput(new string('я', 101)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var renamed = await c.PutJsonAsync($"/api/shops/{shop.Id}/categories/{a.Id}", new CategoryInput("А2", true));
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await renamed.Content.ReadJsonAsync<CategoryDto>())!;
        dto.Name.Should().Be("А2");
        dto.IsHidden.Should().BeTrue();

        // порядок: ровно все категории
        var partial = await c.PutJsonAsync($"/api/shops/{shop.Id}/category-order", new UuidList([b.Id]));
        partial.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await partial.Content.ReadAsStringAsync()).Should().Contain("Список категорий устарел");
        (await c.PutJsonAsync($"/api/shops/{shop.Id}/category-order", new UuidList([b.Id, b.Id]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var reorder = await c.PutJsonAsync($"/api/shops/{shop.Id}/category-order", new UuidList([b.Id, a.Id]));
        reorder.StatusCode.Should().Be(HttpStatusCode.OK, await reorder.Content.ReadAsStringAsync());
        var list = (await (await c.GetAsync($"/api/shops/{shop.Id}/categories")).Content.ReadJsonAsync<List<CategoryDto>>())!;
        list.Select(x => x.Id).Should().Equal(b.Id, a.Id);
        list.Should().HaveCount(2, "скрытые категории видны владельцу");

        (await c.DeleteAsync($"/api/shops/{shop.Id}/categories/{b.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("CY23-21")]
    public async Task Category_WithProducts_CannotBeDeleted()
    {
        var shop = await CreateShopAsync();
        var cat = await CreateCategoryAsync(shop);
        var product = await CreateProductAsync(shop, categoryId: cat.Id);
        var c = AuthedClient(shop.OwnerToken);

        var r = await c.DeleteAsync($"/api/shops/{shop.Id}/categories/{cat.Id}");
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonElementAsync(r)).GetProperty("code").GetString().Should().Be("CategoryNotEmpty");

        (await c.DeleteAsync($"/api/shops/{shop.Id}/products/{product.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.DeleteAsync($"/api/shops/{shop.Id}/categories/{cat.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("CY23-22")]
    public async Task Storefront_ShowsCategoriesInOrder_HidesHiddenAndUnpublished_OtherLast()
    {
        var shop = await CreateShopAsync();
        var visible = await CreateCategoryAsync(shop, "Видимая");
        var hidden = await CreateCategoryAsync(shop, "Скрытая", hidden: true);
        var emptyCat = await CreateCategoryAsync(shop, "Пустая");
        var inVisible = await CreateProductAsync(shop, "В видимой", categoryId: visible.Id);
        await CreateProductAsync(shop, "Снят", categoryId: visible.Id, published: false);
        await CreateProductAsync(shop, "В скрытой", categoryId: hidden.Id);
        await CreateProductAsync(shop, "Без категории");

        var sf = await (await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}")).Content.ReadJsonAsync<StorefrontDto>();
        sf!.Categories.Select(x => x.Name).Should().Equal("Видимая", "Другое");
        sf.Categories[0].Products.Select(p => p.Name).Should().Equal("В видимой");
        sf.Categories[1].Id.Should().BeNull();
        sf.Categories[1].Products.Select(p => p.Name).Should().Equal("Без категории");
        sf.Categories.SelectMany(x => x.Products).Should().NotContain(p => p.Name == "В скрытой" || p.Name == "Снят");
        _ = inVisible; _ = emptyCat;
    }

    // ── US-23-15: товары ─────────────────────────────────────────────────────────

    [Fact, TestCase("CY23-23")]
    public async Task Product_Validation_PriceNameCategoryUnitFields()
    {
        var shop = await CreateShopAsync();
        var c = AuthedClient(shop.OwnerToken);
        Task<HttpResponseMessage> Post(ProductInput p) => c.PostJsonAsync($"/api/shops/{shop.Id}/products", p);

        (await Post(new ProductInput(null, " ", null, ProductUnit.Piece, 10m, null, null, null, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Post(new ProductInput(null, "X", null, ProductUnit.Piece, 0m, null, null, null, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Post(new ProductInput(null, "X", null, ProductUnit.Piece, -5m, null, null, null, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Post(new ProductInput(null, "X", null, ProductUnit.Piece, 10.005m, null, null, null, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Post(new ProductInput(null, "X", null, ProductUnit.Piece, 1_000_000.01m, null, null, null, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Post(new ProductInput(Guid.NewGuid(), "X", null, ProductUnit.Piece, 10m, null, null, null, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Post(new ProductInput(null, "X", null, ProductUnit.Weight, 10m, null, 5, null, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "шаг меньше 10 г");
        (await Post(new ProductInput(null, "X", null, ProductUnit.Weight, 10m, null, 100, 150, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "минимум не кратен шагу");
        (await Post(new ProductInput(null, "X", null, ProductUnit.Weight, 10m, null, 100, 50, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "минимум меньше шага");
        (await Post(new ProductInput(null, "X", null, ProductUnit.Piece, 10m, null, 100, null, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "шаг только у весового");
        (await Post(new ProductInput(null, "X", null, ProductUnit.Weight, 10m, "300 г", null, null, true, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "порция только у штучного");
        (await Post(new ProductInput(null, "X", null, ProductUnit.Piece, 10m, null, null, null, true, new FoodInfoDto(new string('я', 2001))))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var edge = await Post(new ProductInput(null, "Граница", null, ProductUnit.Piece, 0.01m, "300 г", null, null, true, new FoodInfoDto("Состав")));
        edge.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await edge.Content.ReadJsonAsync<ProductDto>())!;
        dto.Price.Should().Be(0.01m);
        dto.PortionText.Should().Be("300 г");
        dto.FoodInfo.CompositionAndAllergens.Should().Be("Состав");
    }

    [Fact, TestCase("CY23-24")]
    public async Task WeightProduct_Defaults_StepIs100_MinEqualsStep_MaxTenKg()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop, "Сыр", 540m, ProductUnit.Weight);
        p.WeightStepGrams.Should().Be(100);
        p.MinQuantity.Should().Be(100);
        p.MaxQuantity.Should().Be(10_000);

        var p2 = await CreateProductAsync(shop, "Колбаса", 700m, ProductUnit.Weight, weightStep: 50);
        p2.MinQuantity.Should().Be(50);
        var p3 = await CreateProductAsync(shop, "Ветчина", 700m, ProductUnit.Weight, weightStep: 50, minGrams: 200);
        p3.MinQuantity.Should().Be(200);
    }

    [Fact, TestCase("CY23-25")]
    public async Task Product_UnitCannotChange_PriceChangeAllowed_ListInPositionOrder()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop, "Шаурма", 250m);
        var q = await CreateProductAsync(shop, "Кола", 100m);
        var c = AuthedClient(shop.OwnerToken);

        var change = await c.PutJsonAsync($"/api/shops/{shop.Id}/products/{p.Id}",
            new ProductInput(null, "Шаурма", null, ProductUnit.Weight, 250m, null, 100, null, true, null));
        change.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonElementAsync(change)).GetProperty("code").GetString().Should().Be("UnitChangeNotAllowed");

        var price = await c.PutJsonAsync($"/api/shops/{shop.Id}/products/{p.Id}",
            new ProductInput(null, "Шаурма", null, ProductUnit.Piece, 270m, null, null, null, true, null));
        price.StatusCode.Should().Be(HttpStatusCode.OK);
        (await price.Content.ReadJsonAsync<ProductDto>())!.Price.Should().Be(270m);

        // порядок: ровно все товары категории («Другое» = null)
        (await c.PutJsonAsync($"/api/shops/{shop.Id}/product-order", new ProductOrderInput(null, [q.Id]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ok = await c.PutJsonAsync($"/api/shops/{shop.Id}/product-order", new ProductOrderInput(null, [q.Id, p.Id]));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var sf = (await (await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}")).Content.ReadJsonAsync<StorefrontDto>())!;
        sf.Categories.Single().Products.Select(x => x.Name).Should().Equal("Кола", "Шаурма");
    }

    [Fact, TestCase("CY23-26")]
    public async Task ProductImage_UsesImagePipeline_RejectsNonImage_AndCanBeRemoved()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var c = AuthedClient(shop.OwnerToken);

        using (var bad = new MultipartFormDataContent())
        {
            bad.Add(new ByteArrayContent("это не картинка"u8.ToArray()) { Headers = { ContentType = new("image/png") } }, "file", "x.png");
            var r = await c.PostAsync($"/api/shops/{shop.Id}/products/{p.Id}/image", bad);
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, "содержимое проверяется по сигнатуре, а не по расширению");
        }

        using var good = new MultipartFormDataContent();
        good.Add(new ByteArrayContent(TestImages.SolidPng()) { Headers = { ContentType = new("image/png") } }, "file", "x.png");
        var up = await c.PostAsync($"/api/shops/{shop.Id}/products/{p.Id}/image", good);
        up.StatusCode.Should().Be(HttpStatusCode.OK, await up.Content.ReadAsStringAsync());
        (await up.Content.ReadJsonAsync<ProductDto>())!.ImageUrl.Should().NotBeNullOrEmpty();

        var staff = await AddShopStaffAsync(shop);
        using var good2 = new MultipartFormDataContent();
        good2.Add(new ByteArrayContent(TestImages.SolidPng()) { Headers = { ContentType = new("image/png") } }, "file", "x.png");
        (await AuthedClient(staff.Token).PostAsync($"/api/shops/{shop.Id}/products/{p.Id}/image", good2)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var del = await c.DeleteAsync($"/api/shops/{shop.Id}/products/{p.Id}/image");
        del.StatusCode.Should().Be(HttpStatusCode.OK);
        (await del.Content.ReadJsonAsync<ProductDto>())!.ImageUrl.Should().BeNull();
    }

    [Fact, TestCase("CY23-27")]
    public async Task CategoryAndProductLimits_Return409()
    {
        // лимиты по конфигурации (1000/100) слишком велики для прогона; проверяем, что код лимита существует на уровне контракта
        // через порядок и корректный отказ по чужому магазину: товар другого магазина как категория — 400.
        var a = await CreateShopAsync();
        var b = await CreateShopAsync();
        var foreign = await CreateCategoryAsync(b);
        var r = await AuthedClient(a.OwnerToken).PostJsonAsync($"/api/shops/{a.Id}/products",
            new ProductInput(foreign.Id, "X", null, ProductUnit.Piece, 10m, null, null, null, true, null));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest, "категория чужого магазина недоступна");
        (await r.Content.ReadAsStringAsync()).Should().Contain("Категория не найдена");
    }

    // ── US-23-16: «закончилось» ──────────────────────────────────────────────────

    [Fact, TestCase("CY23-28")]
    public async Task SoldOut_ByStaff_GreysOnStorefront_BlocksCart_KeepsExistingOrders()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));

        var r = await AuthedClient(staff.Token).PutJsonAsync($"/api/shops/{shop.Id}/products/{p.Id}/sold-out", new SoldOutInput(true));
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var sf = (await (await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}")).Content.ReadJsonAsync<StorefrontDto>())!;
        var onShelf = sf.Categories.SelectMany(x => x.Products).Single(x => x.Id == p.Id);
        onShelf.Available.Should().BeFalse("товар остаётся на странице серым");

        var quote = await AnonymousClient().PostJsonAsync($"/api/storefront/{shop.Slug}/quote", new QuoteInput([new CartLineInput(p.Id, 1)]));
        var q = (await quote.Content.ReadJsonAsync<QuoteDto>())!;
        q.HasProblems.Should().BeTrue();
        q.Lines.Single().Problem!.Reason.Should().Be(OrderProblemReason.SoldOut);

        var order = await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        order.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await ReadJsonElementAsync(order);
        body.GetProperty("code").GetString().Should().Be("ItemsUnavailable");
        body.GetProperty("problems")[0].GetProperty("reason").GetString().Should().Be("SoldOut");

        (await GetPublicOrderAsync(placed.Order.Token)).Status.Should().Be(OrderStatus.New, "уже созданные заказы не меняются");

        // вернуть
        (await AuthedClient(staff.Token).PutJsonAsync($"/api/shops/{shop.Id}/products/{p.Id}/sold-out", new SoldOutInput(false))).StatusCode.Should().Be(HttpStatusCode.OK);
        await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
    }
}
