using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 33, «Вызов 2»: «Устройства и уведомления» в профиле, одно включение на оба сайта (US-33-03, 04, 05, 07; Q-33-2, Q-33-5).
/// Написано по SPEC.md цикла 33 и API_CONTRACT_CYCLE33.md §33.21-§33.27, а не по реализации. Идентификаторы CY33- совпадают с
/// ARCHITECTURE_CYCLE33.md §33.13.1, где кейс там есть; CY33-14+ добавлены QA (граничные случаи, выход, согласованность ролей).
/// Хост push-включён: <see cref="PushDispatchTestFactory"/> без автоматического тика, проход диспетчера запускается явно.
/// </summary>
public class Cycle33UnifiedPushTests(TestDatabaseFixture fixture) : Cycle24TestBase(fixture)
{
    private static HttpClient PushClient(PushDispatchTestFactory push, string? token = null)
    {
        var client = push.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private PushDispatchTestFactory NewPush() => new(ConnectionString, disableAutomaticTicking: true);

    private sealed record Sub(Guid Id, string Site);

    private static async Task<Sub> Subscribe(HttpClient c, CompanyKind? site, string? endpoint = null, string label = "Chrome на Android")
    {
        endpoint ??= $"https://push.example.test/cy33/{site}/{Guid.NewGuid():N}";
        var r = await c.PostAsJsonAsync("/api/push/subscriptions",
            new CreatePushSubscriptionInput(endpoint, new CreatePushSubscriptionKeysInput("p256dh", "auth"), label, site));
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        var j = await Json(r);
        return new Sub(j.GetProperty("id").GetGuid(), j.GetProperty("site").GetString()!);
    }

    /// <summary>Существующий пользователь становится сотрудником ещё одной компании (салона или магазина). Возвращает свежий токен.</summary>
    private async Task<string> AddExistingUserAsync(string ownerToken, Guid companyId, AuthResponseDto user)
    {
        var r = await AuthedClient(ownerToken).PostAsJsonAsync($"/api/companies/{companyId}/members",
            new { phone = user.Phone, firstName = user.FirstName, lastName = user.LastName, role = "Master", bio = (string?)null, email = (string?)null });
        r.EnsureSuccessStatusCode();
        return (await LoginAsync(user.Phone, "Password123!")).Token;
    }

    private sealed record Both(
        AuthResponseDto Owner, CompanyDto Salon, ShopCtx Shop, ServiceDto Service, DateOnly Date, AuthResponseDto Worker, string WorkerToken, ProductDto Product);

    /// <summary>Салон A и магазин B (разные владельцы), один сотрудник работает в обоих.</summary>
    private async Task<Both> SetUpBothAsync()
    {
        var (owner, salon) = await CreateOwnerWithCompanyAsync();
        var shop = await CreateShopAsync();
        var worker = await RegisterAsync();
        var token = await AddExistingUserAsync(owner.Token, salon.Id, worker);
        token = await AddExistingUserAsync(shop.OwnerToken, shop.Id, worker);
        var service = await CreateServiceAsync(owner.Token, salon.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, worker.UserId, salon.Id, date);
        var product = await CreateProductAsync(shop, "Шаурма", 250m);
        return new Both(owner, salon, shop, service, date, worker, token, product);
    }

    private async Task<BookingDto> BookAsync(PushDispatchTestFactory push, Both s, Guid? companyId = null, Guid? serviceId = null, int hour = 10)
    {
        var clientUser = await RegisterAsync();
        var r = await PushClient(push, clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyId ?? s.Salon.Id, serviceId ?? s.Service.Id, s.Worker.UserId, s.Date, new TimeOnly(hour, 0), null, null, null, null, null));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<BookingDto>())!;
    }

    private async Task<int> OrderAsync(PushDispatchTestFactory push, Both s)
    {
        var r = await PushClient(push).PostJsonAsync($"/api/storefront/{s.Shop.Slug}/orders", Guest([Line(s.Product, 1)]));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<CreateOrderResponse>())!.Order.Number;
    }

    private List<StaffPushNotification> Rows(Func<StaffPushNotification, bool> filter)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.StaffPushNotifications.Include(x => x.Subscription).AsEnumerable().Where(filter).ToList();
    }

    private static string Url(StaffPushNotification row) => JsonDocument.Parse(row.Payload).RootElement.GetProperty("url").GetString()!;

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    // ── US-33-03: одно включение, оба вида ───────────────────────────────────────

    [Fact, TestCase("CY33-01")]
    public async Task Booking_GoesToDevicesOfBothSites_GoodsDeviceGetsAbsoluteUrl()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);
        var ez = await Subscribe(c, null);
        var goods = await Subscribe(c, CompanyKind.Orders);
        var cfg = await Json(await c.GetAsync("/api/push/config?allSites=true"));
        var servicesBase = cfg.GetProperty("siteUrls").GetProperty("services").GetString()!;

        var booking = await BookAsync(push, s);

        var rows = Rows(x => x.BookingId == booking.Id);
        rows.Should().HaveCount(2, "одно событие, два устройства: записи приходят и на подписку goods");
        rows.Select(x => x.SubscriptionId).Should().BeEquivalentTo(new Guid?[] { ez.Id, goods.Id });
        Url(rows.Single(x => x.SubscriptionId == ez.Id)).Should().StartWith("/my-bookings");
        Url(rows.Single(x => x.SubscriptionId == goods.Id)).Should().StartWith(servicesBase + "/my-bookings",
            "на подписке другого сайта адрес должен быть абсолютным, иначе воркер goods заменит его на /cabinet");
    }

    [Fact, TestCase("CY33-02")]
    public async Task Order_GoesToDevicesOfBothSites_EzbookDeviceGetsAbsoluteUrl()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);
        var ez = await Subscribe(c, null);
        var goods = await Subscribe(c, CompanyKind.Orders);
        var cfg = await Json(await c.GetAsync("/api/push/config?allSites=true"));
        var ordersBase = cfg.GetProperty("siteUrls").GetProperty("orders").GetString()!;

        var number = await OrderAsync(push, s);

        var rows = Rows(x => x.CompanyId == s.Shop.Id && x.UserId == s.Worker.UserId);
        rows.Should().HaveCount(2);
        rows.Select(x => x.SubscriptionId).Should().BeEquivalentTo(new Guid?[] { ez.Id, goods.Id });
        Url(rows.Single(x => x.SubscriptionId == ez.Id)).Should().StartWith($"{ordersBase}/cabinet/{s.Shop.Id}/orders");
        Url(rows.Single(x => x.SubscriptionId == goods.Id)).Should().StartWith($"/cabinet/{s.Shop.Id}/orders");
        rows.Should().OnlyContain(x => x.Payload.Contains($"Новый заказ № {number}"));
    }

    [Fact, TestCase("CY33-03")]
    public async Task SingleKindStaff_OnForeignSite_GetsOnlyTheirKind_NoExtraRows()
    {
        // сотрудник только магазина включил на ezbook; мастер только салона включил на goods
        var (owner, salon) = await CreateOwnerWithCompanyAsync();
        var shop = await CreateShopAsync();
        var shopStaff = await AddShopStaffAsync(shop);
        var master = await AddMasterAsync(owner.Token, salon.Id);
        var service = await CreateServiceAsync(owner.Token, salon.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, salon.Id, date);
        var product = await CreateProductAsync(shop);
        await using var push = NewPush();
        var shopStaffEz = await Subscribe(PushClient(push, shopStaff.Token), null);
        var masterGoods = await Subscribe(PushClient(push, master.Token), CompanyKind.Orders);

        var client = await RegisterAsync();
        var br = await PushClient(push, client.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(salon.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        br.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await br.Content.ReadJsonAsync<BookingDto>())!;
        (await PushClient(push).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(product, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);

        var bookingRows = Rows(x => x.BookingId == booking.Id);
        bookingRows.Should().ContainSingle().Which.SubscriptionId.Should().Be(masterGoods.Id, "мастер салона получает запись на устройство goods");
        var orderRows = Rows(x => x.CompanyId == shop.Id && x.OrderId != null);
        orderRows.Select(x => x.SubscriptionId).Should().Contain(shopStaffEz.Id, "сотрудник магазина получает заказ на устройство ezbook");
        orderRows.Should().NotContain(x => x.SubscriptionId == masterGoods.Id, "мастер салона не сотрудник магазина");
        Rows(x => x.SubscriptionId == shopStaffEz.Id).Should().OnlyContain(x => x.CompanyId == shop.Id, "у сотрудника магазина нет строк про салон");
        Rows(x => x.SubscriptionId == masterGoods.Id).Should().OnlyContain(x => x.CompanyId == salon.Id, "у мастера салона нет строк про магазин");
    }

    [Fact, TestCase("CY33-04")]
    public async Task ShopDisablesStaffPush_OrdersStopEverywhere_BookingsKeepComing_AndAlreadyQueuedAreSkipped()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);
        var ez = await Subscribe(c, null);
        var goods = await Subscribe(c, CompanyKind.Orders);

        // строка в очереди ДО выключения
        await OrderAsync(push, s);
        var queued = Rows(x => x.CompanyId == s.Shop.Id && x.UserId == s.Worker.UserId);
        queued.Should().HaveCount(2);

        (await AuthedClient(s.Shop.OwnerToken).PutJsonAsync($"/api/shops/{s.Shop.Id}/notification-settings",
            new ShopNotificationSettingsInput(false, true, false, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        await OrderAsync(push, s);
        Rows(x => x.CompanyId == s.Shop.Id && x.UserId == s.Worker.UserId).Should().HaveCount(2, "после выключения новых строк о заказах нет ни на одном устройстве");

        var booking = await BookAsync(push, s);
        Rows(x => x.BookingId == booking.Id).Should().HaveCount(2, "записи салона приходят как раньше");

        await push.RunStaffPushDispatchPassAsync();
        var after = Rows(x => queued.Select(q => q.Id).Contains(x.Id));
        after.Should().OnlyContain(x => x.Status == NotificationStatus.Skipped && x.Reason == NotificationReason.StaffPushDisabledByCompany);
        push.Sender.Calls.Should().NotContain(x => queued.Select(q => q.SubscriptionId).Contains(x.SubscriptionId) && x.PayloadJson.Contains("заказ"));
    }

    [Fact, TestCase("CY33-05")]
    public async Task WorkerRemovedFromShopAfterQueueing_OrderRowOnEzbookDeviceIsSkipped()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var ez = await Subscribe(PushClient(push, s.WorkerToken), null);
        await OrderAsync(push, s);
        var row = Rows(x => x.CompanyId == s.Shop.Id && x.SubscriptionId == ez.Id).Single();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CompanyMembers.RemoveRange(db.CompanyMembers.Where(m => m.UserId == s.Worker.UserId && m.CompanyId == s.Shop.Id));
            await db.SaveChangesAsync();
        }
        await push.RunStaffPushDispatchPassAsync();

        var after = Rows(x => x.Id == row.Id).Single();
        after.Status.Should().Be(NotificationStatus.Skipped);
        after.Reason.Should().Be(NotificationReason.MasterNoLongerInCompany);
        push.Sender.Calls.Should().NotContain(x => x.SubscriptionId == ez.Id && x.PayloadJson.Contains("заказ"));
    }

    [Fact, TestCase("CY33-06")]
    public async Task SharedComputer_EzbookEndpointTakenByAnotherUser_QueuedOrderRowIsNotLeaked()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var endpoint = "https://push.example.test/cy33-shared/" + Guid.NewGuid();
        var ez = await Subscribe(PushClient(push, s.WorkerToken), null, endpoint);
        await OrderAsync(push, s);
        var row = Rows(x => x.CompanyId == s.Shop.Id && x.SubscriptionId == ez.Id).Single();

        var other = await RegisterAsync();
        await Subscribe(PushClient(push, other.Token), null, endpoint);   // тот же браузер, вошёл другой человек
        await push.RunStaffPushDispatchPassAsync();

        var after = Rows(x => x.Id == row.Id).Single();
        after.Status.Should().Be(NotificationStatus.Skipped);
        after.Reason.Should().Be(NotificationReason.PushSubscriptionReassigned);
        push.Sender.Calls.Should().NotContain(x => x.Endpoint == endpoint);
    }

    // ── US-33-05: общий список устройств ─────────────────────────────────────────

    [Fact, TestCase("CY33-07")]
    public async Task Subscriptions_AllSites_ReturnsBothWithSite_CurrentOnlyForSameSite_DefaultUnchanged()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);
        var endpoint = "https://push.example.test/cy33-same/" + Guid.NewGuid();
        var ez = await Subscribe(c, null, endpoint, "Chrome на Android");
        await Task.Delay(20);
        var goods = await Subscribe(c, CompanyKind.Orders, null, "Safari на iOS");
        var q = Uri.EscapeDataString(endpoint);

        var all = await Json(await c.GetAsync($"/api/push/subscriptions?allSites=true&currentEndpoint={q}&site=Services"));
        var items = all.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items[0].GetProperty("id").GetGuid().Should().Be(goods.Id, "порядок createdAtUtc по убыванию");
        items[0].GetProperty("site").GetString().Should().Be("Orders");
        items[1].GetProperty("site").GetString().Should().Be("Services");
        items.Single(i => i.GetProperty("id").GetGuid() == ez.Id).GetProperty("isCurrent").GetBoolean().Should().BeTrue();
        items.Single(i => i.GetProperty("id").GetGuid() == goods.Id).GetProperty("isCurrent").GetBoolean().Should().BeFalse();

        // тот же endpoint, но спрашивает другой сайт: строка Services не «это устройство»
        var fromGoods = await Json(await c.GetAsync($"/api/push/subscriptions?allSites=true&currentEndpoint={q}&site=Orders"));
        fromGoods.GetProperty("items").EnumerateArray().Should().OnlyContain(i => !i.GetProperty("isCurrent").GetBoolean());

        var legacy = await Json(await c.GetAsync("/api/push/subscriptions"));
        legacy.GetProperty("items").GetArrayLength().Should().Be(1, "без allSites прежнее поведение: только Services");
        legacy.GetRawText().Should().NotContain(endpoint).And.NotContain("p256dh");
        (await c.GetAsync("/api/push/subscriptions?allSites=abc")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY33-08")]
    public async Task Config_AllSites_BothKindsOrderedSalonsFirst_SiteUrls_LegacyFiltersBySite_Bad400()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);

        var all = await Json(await c.GetAsync("/api/push/config?allSites=true"));
        var companies = all.GetProperty("companies").EnumerateArray().ToList();
        companies.Select(x => x.GetProperty("companyId").GetGuid()).Should().Equal(s.Salon.Id, s.Shop.Id);
        companies.Select(x => x.GetProperty("kind").GetString()).Should().Equal("Services", "Orders");
        all.GetProperty("siteUrls").GetProperty("services").GetString().Should().StartWith("http").And.NotEndWith("/");
        all.GetProperty("siteUrls").GetProperty("orders").GetString().Should().StartWith("http").And.NotEndWith("/");

        var legacy = await Json(await c.GetAsync("/api/push/config?site=Orders"));
        legacy.GetProperty("companies").EnumerateArray().Select(x => x.GetProperty("companyId").GetGuid()).Should().Equal(s.Shop.Id);
        legacy.GetProperty("companies")[0].GetProperty("kind").GetString().Should().Be("Orders");
        legacy.TryGetProperty("siteUrls", out _).Should().BeTrue("siteUrls есть в обоих режимах");

        (await c.GetAsync("/api/push/config?allSites=abc")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.GetAsync("/api/push/config?site=Nope&allSites=true")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await push.CreateClient().GetAsync("/api/push/config?allSites=true")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("CY33-14")]
    public async Task Config_ClientWithoutStaffRole_HasEmptyCompanies_SectionStaysHidden()
    {
        await using var push = NewPush();
        var client = await RegisterAsync();
        var cfg = await Json(await PushClient(push, client.Token).GetAsync("/api/push/config?allSites=true"));
        cfg.GetProperty("companies").GetArrayLength().Should().Be(0, "Q-33-5: клиенту раздел не показывается");
    }

    [Fact, TestCase("CY33-09")]
    public async Task DeleteGoodsDeviceFromOtherSite_Works_ForeignId404_CustomerOrderSubscriptionSurvives_NoMoreRows()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);
        var endpoint = "https://push.example.test/cy33-browser/" + Guid.NewGuid();
        var goods = await Subscribe(c, CompanyKind.Orders, endpoint);

        // тот же браузер — подписка покупателя на статус заказа
        var placed = await PlaceOrderAsync(s.Shop.Slug, Guest([Line(s.Product, 1)]));
        var custKeys = new PushKeysInput("BKey-p256dh-" + Guid.NewGuid().ToString("N"), "auth-" + Guid.NewGuid().ToString("N")[..12]);
        (await PushClient(push).PostAsJsonAsync($"/api/orders/public/{placed.Order.Token}/push-subscription",
            new OrderPushSubscribeInput(endpoint, custKeys, "buyer"))).IsSuccessStatusCode.Should().BeTrue();

        var stranger = await RegisterAsync();
        (await PushClient(push, stranger.Token).DeleteAsync($"/api/push/subscriptions/{goods.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.DeleteAsync($"/api/push/subscriptions/{goods.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.DeleteAsync($"/api/push/subscriptions/{goods.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.PushSubscriptions.AnyAsync(x => x.Endpoint == endpoint)).Should().BeFalse();
        (await db.OrderPushSubscriptions.AnyAsync(x => x.Endpoint == endpoint)).Should().BeTrue("подписка покупателя в том же браузере не затронута");

        await OrderAsync(push, s);
        Rows(x => x.SubscriptionId == goods.Id).Should().BeEmpty();
    }

    [Fact, TestCase("CY33-10")]
    public async Task DeviceLimit_IsPerSite_NewGoodsDeviceNeverEvictsEzbook_EleventhEzbookEvictsOldestEzbook()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);
        var max = (await Json(await c.GetAsync("/api/push/config?allSites=true"))).GetProperty("maxSubscriptionsPerUser").GetInt32();

        var ez = new List<Sub>();
        for (var i = 0; i < max; i++) { ez.Add(await Subscribe(c, null)); await Task.Delay(5); }
        var goods = await Subscribe(c, CompanyKind.Orders);

        async Task<List<JsonElement>> All() => (await Json(await c.GetAsync("/api/push/subscriptions?allSites=true"))).GetProperty("items").EnumerateArray().ToList();
        (await All()).Should().HaveCount(max + 1, "устройство другого сайта ничего не вытесняет");

        await Subscribe(c, null);
        var items = await All();
        items.Should().HaveCount(max + 1);
        items.Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(ez[0].Id).And.Contain(goods.Id);
        items.Count(i => i.GetProperty("site").GetString() == "Orders").Should().Be(1);
    }

    [Fact, TestCase("CY33-11")]
    public async Task DeleteCurrent_ForGoodsEndpoint_RemovesOnlyStaffRow_CustomerSubscriptionStays()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);
        var endpoint = "https://push.example.test/cy33-logout/" + Guid.NewGuid();
        await Subscribe(c, CompanyKind.Orders, endpoint);
        var placed = await PlaceOrderAsync(s.Shop.Slug, Guest([Line(s.Product, 1)]));
        (await PushClient(push).PostAsJsonAsync($"/api/orders/public/{placed.Order.Token}/push-subscription",
            new OrderPushSubscribeInput(endpoint, new PushKeysInput("BKey-" + Guid.NewGuid().ToString("N"), "auth-" + Guid.NewGuid().ToString("N")[..12]), null)))
            .IsSuccessStatusCode.Should().BeTrue();

        (await c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions/current") { Content = JsonContent.Create(new { endpoint }) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.PushSubscriptions.AnyAsync(x => x.Endpoint == endpoint)).Should().BeFalse();
        (await db.OrderPushSubscriptions.AnyAsync(x => x.Endpoint == endpoint)).Should().BeTrue();
    }

    // ── Текст уведомления, граничные случаи ──────────────────────────────────────

    [Fact, TestCase("CY33-12")]
    public async Task BookingBody_NamesTheSalon_NoPhone_NoUnicodeEscapes_FitsLimitWithLongNames()
    {
        var (owner, salon) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, salon.Id);
        var longName = new string('Я', 200);
        using (var scope0 = Factory.Services.CreateScope())
        {
            var db0 = scope0.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db0.Companies.SingleAsync(x => x.Id == salon.Id)).Name = longName;
            await db0.SaveChangesAsync();
        }
        var svc = new List<ServiceDto>();
        for (var i = 0; i < 20; i++) svc.Add(await CreateServiceAsync(owner.Token, salon.Id, "Услуга номер " + i + " " + new string('х', 40), 30));
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, salon.Id, date);
        await using var push = NewPush();
        await Subscribe(PushClient(push, master.Token), CompanyKind.Orders);
        var client = await RegisterAsync();

        var r = await PushClient(push, client.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(salon.Id, svc[0].Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        r.StatusCode.Should().Be(HttpStatusCode.Created, "длинные названия не должны ронять создание записи: " + await r.Content.ReadAsStringAsync());
        var booking = (await r.Content.ReadJsonAsync<BookingDto>())!;

        var row = Rows(x => x.BookingId == booking.Id).Single();
        row.Payload.Length.Should().BeLessOrEqualTo(1000);
        row.Payload.Should().NotContain("\\u").And.NotContain(client.Phone.TrimStart('+'));
        var body = JsonDocument.Parse(row.Payload).RootElement.GetProperty("body").GetString()!;
        body.Should().Contain("Я", "в теле есть название салона (пусть и усечённое)");
    }

    [Fact, TestCase("CY33-12b")]
    public async Task BookingBody_ContainsSalonName()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        await Subscribe(PushClient(push, s.WorkerToken), null);
        var booking = await BookAsync(push, s);
        var row = Rows(x => x.BookingId == booking.Id).Single();
        JsonDocument.Parse(row.Payload).RootElement.GetProperty("body").GetString().Should().Contain(s.Salon.Name,
            "US-33-03: текст однозначно говорит, в какой компании запись");
    }

    [Fact, TestCase("CY33-18")]
    public async Task UnknownSiteValue_OnSubscribe_Is400()
    {
        await using var push = NewPush();
        var u = await RegisterAsync();
        var r = await PushClient(push, u.Token).PostAsJsonAsync("/api/push/subscriptions",
            new { endpoint = "https://push.example.test/x/" + Guid.NewGuid(), keys = new { p256dh = "p", auth = "a" }, deviceLabel = "d", site = "Nope" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY33-15")]
    public async Task SameEndpointOnBothSites_IsNotSilentlyDoubled_OneEventAtMostOneMessagePerEndpoint()
    {
        // US-33-07: один браузер включил на обоих сайтах. Сервер видит два разных адреса подписки, а если endpoint один и тот же —
        // строка одна (upsert), и одно событие не должно дать двух отправок на него.
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);
        var endpoint = "https://push.example.test/cy33-dup/" + Guid.NewGuid();
        await Subscribe(c, null, endpoint);
        await Subscribe(c, CompanyKind.Orders, endpoint);

        var booking = await BookAsync(push, s);
        Rows(x => x.BookingId == booking.Id).Count(x => x.Subscription!.Endpoint == endpoint).Should().BeLessOrEqualTo(1);
        await push.RunStaffPushDispatchPassAsync();
        push.Sender.Calls.Count(x => x.Endpoint == endpoint && x.PayloadJson.Contains("Новая запись")).Should().BeLessOrEqualTo(1);
    }

    [Fact, TestCase("CY33-16")]
    public async Task ConcurrentSubscribe_SameEndpoint_ProducesSingleRow()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);
        var endpoint = "https://push.example.test/cy33-race/" + Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => c.PostAsJsonAsync("/api/push/subscriptions",
            new CreatePushSubscriptionInput(endpoint, new CreatePushSubscriptionKeysInput("p", "a"), "d", CompanyKind.Orders))));
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK,
            string.Join(",", results.Select(r => (int)r.StatusCode)));
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.PushSubscriptions.CountAsync(x => x.Endpoint == endpoint)).Should().Be(1);
    }

    [Fact, TestCase("CY33-17")]
    public async Task OrderBody_StillHasNoCustomerData_OnBothDevices()
    {
        var s = await SetUpBothAsync();
        await using var push = NewPush();
        var c = PushClient(push, s.WorkerToken);
        await Subscribe(c, null);
        await Subscribe(c, CompanyKind.Orders);
        var phone = UniquePhone();
        var r = await PushClient(push).PostJsonAsync($"/api/storefront/{s.Shop.Slug}/orders",
            new CreateOrderInput(Guid.NewGuid(), [Line(s.Product, 1)], "Секретный Покупатель", phone, null, null));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var rows = Rows(x => x.CompanyId == s.Shop.Id && x.UserId == s.Worker.UserId);
        rows.Should().NotBeEmpty().And.OnlyContain(x => !x.Payload.Contains("Секретный") && !x.Payload.Contains(phone.TrimStart('+')) && !x.Payload.Contains("\\u"));
    }
}
