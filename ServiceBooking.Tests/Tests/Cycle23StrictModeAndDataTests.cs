using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 23, «Вызов 2»: строгий режим «только с подтверждённым телефоном» (Q2, US-23-10/19), ограничения частоты
/// и персональные данные заказов (US-23-27). Пишется по SPEC.md/API_CONTRACT_CYCLE23.md.
/// </summary>
public class Cycle23StrictModeAndDataTests(TestDatabaseFixture fixture) : Cycle23TestBase(fixture)
{
    private static async Task<JsonElement> J(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static HttpClient Authed(HttpClient c, string? token)
    {
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private async Task<ShopCtx> StrictShopAsync(PhoneVerificationEnabledFactory enabled)
    {
        var shop = await CreateShopAsync();
        var r = await Authed(enabled.CreateClient(), shop.OwnerToken)
            .PutJsonAsync($"/api/shops/{shop.Id}/settings", Settings(ShopCustomerMode.VerifiedPhoneOnly));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return shop;
    }

    // ── Строгий режим ────────────────────────────────────────────────────────────

    [Fact, TestCase("CY23-70")]
    public async Task Strict_Anonymous_GetsLoginRequired_UnverifiedAccountGetsVerificationRequired_VerifiedOrders()
    {
        var enabled = Fixture.ClassHost("phv", cs => new PhoneVerificationEnabledFactory(cs));
        var shop = await StrictShopAsync(enabled);
        var p = await CreateProductAsync(shop);
        var dto = await (await Authed(enabled.CreateClient(), shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}")).Content.ReadJsonAsync<ShopManageDto>();
        dto!.PhoneVerificationAvailable.Should().BeTrue();

        var sf = (await (await enabled.CreateClient().GetAsync($"/api/storefront/{shop.Slug}")).Content.ReadJsonAsync<StorefrontDto>())!;
        sf.CustomerMode.Should().Be(ShopCustomerMode.VerifiedPhoneOnly);

        // гость (имя+телефон+капча) в строгом режиме не проходит
        var anon = await enabled.CreateClient().PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]));
        anon.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var anonBody = await J(anon);
        anonBody.GetProperty("code").GetString().Should().Be("LoginRequired");
        anonBody.GetProperty("message").GetString().Should().Contain("Войдите или зарегистрируйтесь");

        var buyer = await RegisterAsync();
        var unverified = await Authed(enabled.CreateClient(), buyer.Token)
            .PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]));
        unverified.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(unverified)).GetProperty("code").GetString().Should().Be("PhoneVerificationRequired");

        await MarkPhoneVerifiedAsync(buyer.Phone, buyer.UserId);
        var ok = await Authed(enabled.CreateClient(), buyer.Token)
            .PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)], phone: UniquePhone()));
        ok.StatusCode.Should().Be(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
        var created = (await ok.Content.ReadJsonAsync<CreateOrderResponse>())!;
        var staff = await GetStaffOrderAsync(shop, created.Order.Token);
        staff.CustomerPhoneVerified.Should().BeTrue();
        staff.CustomerKind.Should().Be(OrderActorKind.Customer);
        new string(staff.CustomerPhone!.Where(char.IsDigit).ToArray()).Should().EndWith(new string(buyer.Phone.Where(char.IsDigit).ToArray())[1..],
            "заказ создаётся на номер аккаунта");
    }

    [Fact, TestCase("CY23-71")]
    public async Task Strict_SubsystemGoesDown_UnverifiedGets409Unavailable_VerifiedStillOrders_AnonymousStillLoginRequired()
    {
        var enabled = Fixture.ClassHost("phv", cs => new PhoneVerificationEnabledFactory(cs));
        var shop = await StrictShopAsync(enabled);
        var p = await CreateProductAsync(shop);

        // основной хост — подсистема выключена
        var buyer = await RegisterAsync();
        var r = await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]), buyer.Token);
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(r)).GetProperty("code").GetString().Should().Be("PhoneVerificationUnavailable");
        (await J(await PostOrderAsync(shop.Slug, Guest([Line(p, 1)])))).GetProperty("code").GetString().Should().Be("LoginRequired");

        await MarkPhoneVerifiedAsync(buyer.Phone, buyer.UserId);
        (await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]), buyer.Token)).StatusCode.Should().Be(HttpStatusCode.Created,
            "уже подтверждённому номеру недоступность подсистемы не мешает");
    }

    [Fact, TestCase("CY23-72")]
    public async Task Strict_SwitchBackToAnyone_AppliesToNewOrders_GuestsAllowedAgain()
    {
        var enabled = Fixture.ClassHost("phv", cs => new PhoneVerificationEnabledFactory(cs));
        var shop = await StrictShopAsync(enabled);
        var p = await CreateProductAsync(shop);
        (await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await UpdateSettingsAsync(shop, Settings(ShopCustomerMode.Anyone));
        (await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Ограничения частоты ──────────────────────────────────────────────────────

    [Fact, TestCase("CY23-73")]
    public async Task PhoneLimit_TooManyActiveOrdersOnOneNumber_Returns429WithText_OtherNumbersUnaffected()
    {
        using var limited = Factory.WithWebHostBuilder(b => b.UseSetting("Orders:PhoneLimits:MaxActivePerShop", "2"));
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var phone = UniquePhone();
        var c = limited.CreateClient();
        for (var i = 0; i < 2; i++)
            (await c.PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)], phone: phone))).StatusCode.Should().Be(HttpStatusCode.Created);
        var third = await c.PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)], phone: phone));
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await third.Content.ReadAsStringAsync()).Should().Contain("Слишком много заказов на этот номер");
        (await c.PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)], phone: UniquePhone()))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CY23-74")]
    public async Task OrderCreateRateLimit_PerAddress_Returns429Text()
    {
        using var limited = Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimits:order-create:AnonymousPermitLimit", "3");
            b.UseSetting("RateLimits:order-create:WindowMinutes", "60");
        });
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var c = limited.CreateClient();
        HttpStatusCode last = default;
        string body = "";
        for (var i = 0; i < 6; i++)
        {
            var r = await c.PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]));
            last = r.StatusCode;
            if (last == HttpStatusCode.TooManyRequests) { body = await r.Content.ReadAsStringAsync(); break; }
        }
        last.Should().Be(HttpStatusCode.TooManyRequests);
        body.Should().Contain("Слишком много заказов подряд");
    }

    [Fact, TestCase("CY23-75")]
    public async Task StorefrontAndPublicOrderRateLimits_Return429WithText()
    {
        using var limited = Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimits:storefront:PermitLimit", "3");
            b.UseSetting("RateLimits:order-public:PermitLimit", "3");
        });
        var shop = await CreateShopAsync();
        var c = limited.CreateClient();
        HttpResponseMessage? r = null;
        for (var i = 0; i < 6; i++)
        {
            r = await c.GetAsync($"/api/storefront/{shop.Slug}");
            if (r.StatusCode == HttpStatusCode.TooManyRequests) break;
        }
        r!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await r.Content.ReadAsStringAsync()).Should().Contain("Слишком много запросов");

        for (var i = 0; i < 6; i++)
        {
            r = await c.GetAsync("/api/orders/public/some-random-token-value-1234567890");
            if (r.StatusCode == HttpStatusCode.TooManyRequests) break;
        }
        r.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── Персональные данные ──────────────────────────────────────────────────────

    [Fact, TestCase("CY23-80")]
    public async Task Export_ContainsAccountOrders_GuestOrdersOnSamePhoneOnlyAfterVerification()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var buyer = await RegisterAsync();
        var mine = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 2)], "Пётр", comment: "мой заказ"), buyer.Token);
        var guest = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)], "Гость", buyer.Phone, "гостевой"));

        async Task<JsonElement> Export() =>
            await J(await AuthedClient(buyer.Token).GetAsync("/api/profile/export"));

        var before = await Export();
        var ordersBefore = before.GetProperty("orders").EnumerateArray().ToList();
        ordersBefore.Should().ContainSingle();
        ordersBefore[0].GetProperty("source").GetString().Should().Be("Account");
        ordersBefore[0].GetProperty("shopName").GetString().Should().Be(shop.Shop.Name);
        ordersBefore[0].GetProperty("comment").GetString().Should().Be("мой заказ");
        ordersBefore[0].GetProperty("number").GetInt32().Should().Be(mine.Order.Number);
        ordersBefore[0].GetProperty("total").GetDecimal().Should().Be(500m);
        before.GetRawText().Should().NotContain("гостевой", "гостевые заказы без подтверждённого номера не выгружаются");

        await MarkPhoneVerifiedAsync(buyer.Phone, buyer.UserId);
        var after = await Export();
        var orders = after.GetProperty("orders").EnumerateArray().ToList();
        orders.Should().HaveCount(2);
        orders.Single(o => o.GetProperty("number").GetInt32() == guest.Order.Number).GetProperty("source").GetString().Should().Be("GuestSamePhone");

        // чужой аккаунт не видит этих заказов
        var stranger = await RegisterAsync();
        (await J(await AuthedClient(stranger.Token).GetAsync("/api/profile/export"))).GetProperty("orders").GetArrayLength().Should().Be(0);
    }

    [Fact, TestCase("CY23-81")]
    public async Task DeleteAccount_AnonymizesAccountOrders_ShopAccountingIntact_GuestOrdersOnUnverifiedPhoneUntouched()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop, price: 250m);
        var buyer = await RegisterAsync();
        var mine = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 2)], "Пётр", comment: "персональный комментарий"), buyer.Token);
        var strangerGuest = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)], "Гость", buyer.Phone, "чужой гостевой"));

        var del = await AuthedClient(buyer.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        del.StatusCode.Should().Be(HttpStatusCode.NoContent, "удаление не падает из-за наличия заказов");

        var pub = await GetPublicOrderAsync(mine.Order.Token);
        pub.CustomerName.Should().BeNull();
        pub.CustomerPhoneMasked.Should().BeNull();
        pub.Comment.Should().BeNull("комментарий обезличен");
        pub.Total.Should().Be(500m, "учёт магазина цел");
        pub.Items.Should().ContainSingle();

        var staff = await GetStaffOrderAsync(shop, mine.Order.Token);
        staff.CustomerName.Should().BeNull();
        staff.CustomerPhone.Should().BeNull();
        staff.Total.Should().Be(500m);
        staff.Status.Should().Be(OrderStatus.New);

        // гостевой заказ на тот же номер, но номер не был подтверждён — не трогаем (гейт цикла 16)
        var guestStaff = await GetStaffOrderAsync(shop, strangerGuest.Order.Token);
        guestStaff.CustomerName.Should().Be("Гость");
        guestStaff.Comment.Should().Be("чужой гостевой");

        // магазин по-прежнему может работать с обезличенным заказом
        await ActOkAsync(shop, staff, "accept");
    }

    [Fact, TestCase("CY23-82")]
    public async Task DeleteAccount_VerifiedPhone_AnonymizesGuestOrdersOnSameNumberToo()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var buyer = await RegisterAsync();
        await MarkPhoneVerifiedAsync(buyer.Phone, buyer.UserId);
        var guest = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)], "Я же", buyer.Phone, "мой гостевой"));

        (await AuthedClient(buyer.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var staff = await GetStaffOrderAsync(shop, guest.Order.Token);
        staff.CustomerName.Should().BeNull();
        staff.CustomerPhone.Should().BeNull();
        staff.Comment.Should().BeNull();
    }

    [Fact, TestCase("CY23-83")]
    public async Task DeleteAccount_ShopOwner_Refused409_LikeSalonOwner()
    {
        var shop = await CreateShopAsync();
        var r = await AuthedClient(shop.OwnerToken).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY23-84")]
    public async Task ShopStaffMember_CanDeleteOwnAccount_ShopKeepsWorking()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        var r = await AuthedClient(staff.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        r.StatusCode.Should().Be(HttpStatusCode.NoContent, "сотрудник — не владелец, удаление разрешено");
        (await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
