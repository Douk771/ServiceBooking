using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 24, «Вызов 2»: уведомления — настройки магазина, web-push покупателю без аккаунта, push персоналу, сообщения покупателю
/// в мессенджер, изоляция салонных маршрутов (US-24-15, 18…22, US-24-23). API_CONTRACT_CYCLE24.md §479, §483, §484, §486.
/// </summary>
public class Cycle24NotificationsTests(TestDatabaseFixture fixture) : Cycle24TestBase(fixture)
{
    private static HttpClient PushClient(PushEnabledFactory push, string? token = null)
    {
        var client = push.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static OrderPushSubscribeInput Sub(string endpoint, string? label = null) =>
        new(endpoint, new PushKeysInput("BKey-p256dh-" + Guid.NewGuid().ToString("N"), "auth-" + Guid.NewGuid().ToString("N")[..12]), label);

    /// <summary>Подключённый и оплаченный канал аккаунта владельца магазина (запись в БД, как у NotificationTestBase.CreateConnectedChannelAsync).</summary>
    private async Task<Guid> SeedFundedChannelAsync(ShopCtx shop)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await db.Companies.AsNoTracking().SingleAsync(c => c.Id == shop.Id);
        var accountId = company.BillingAccountId
            ?? (await db.BillingAccounts.SingleAsync(a => a.OwnerUserId == shop.Owner.UserId)).Id;

        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig).SingleAsync(s => s.OwnerUserId == shop.Owner.UserId);
        sub.PlanConfig!.AllowNotificationChannel = true;
        await NotificationTestBase.EnsureWhatsAppPlanRuleAsync(db, sub.PlanConfigId!.Value);
        await db.SaveChangesAsync();
        await NotificationTestBase.EnsureWhatsAppPaidAsync(db, accountId);

        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = shop.Owner.UserId, BillingAccountId = accountId, State = ChannelState.Connected,
            PhoneNumber = UniquePhone().TrimStart('+'), ProviderInstanceId = Unique("instance"),
            ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1),
        };
        channel.ProviderSecretCiphertext = ServiceBooking.API.Services.Notifications.SecretProtector.Encrypt(
            "test-provider-token", NotificationDispatchTestFactory.TestEncryptionKeyBase64, channel.Id);
        db.NotificationChannels.Add(channel);
        await db.SaveChangesAsync();
        return channel.Id;
    }

    private async Task<Guid> ConnectShopChannelAsync(ShopCtx shop)
    {
        var channelId = await SeedFundedChannelAsync(shop);
        var r = await AuthedClient(shop.OwnerToken).PostJsonAsync($"/api/notification-channels/{channelId}/companies",
            new { companyId = shop.Id, warningAcknowledged = true });
        r.StatusCode.Should().Be(HttpStatusCode.Created, "для магазина назначение канала больше не закрыто: " + await r.Content.ReadAsStringAsync());
        return channelId;
    }

    // ── US-24-18: настройки уведомлений магазина ─────────────────────────────────

    [Fact, TestCase("CY24-60")]
    public async Task NotificationSettings_Defaults_OwnerOnlyWrite_MessengerUnavailableWithoutChannel()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var url = $"/api/shops/{shop.Id}/notification-settings";

        var s = (await (await AuthedClient(shop.OwnerToken).GetAsync(url)).Content.ReadJsonAsync<ShopNotificationSettingsDto>())!;
        s.StaffPushEnabled.Should().BeTrue("по умолчанию включено");
        s.CustomerWebPushEnabled.Should().BeTrue("web-push покупателям бесплатно, по умолчанию включён");
        s.CustomerMessengerEnabled.Should().BeFalse("мессенджер по умолчанию выключен");
        s.MessengerAvailable.Should().BeFalse();
        s.MessengerUnavailableText.Should().Be("Подключите номер для сообщений покупателям");
        s.Channels.Should().BeEmpty();

        (await AuthedClient(staff.Token).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, "читает персонал");
        var body = new ShopNotificationSettingsInput(true, true, false, null, null);
        (await AuthedClient(staff.Token).PutJsonAsync(url, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "меняет только владелец");
        (await AnonymousClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var noChannel = await AuthedClient(shop.OwnerToken).PutJsonAsync(url, body with { CustomerMessengerEnabled = true });
        noChannel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var j = await J(noChannel);
        j.GetProperty("code").GetString().Should().Be("MessengerUnavailable");
        j.GetProperty("message").GetString().Should().Be("Сначала подключите и оплатите номер для сообщений покупателям");

        var off = await AuthedClient(shop.OwnerToken).PutJsonAsync(url, body with { StaffPushEnabled = false, CustomerWebPushEnabled = false });
        off.StatusCode.Should().Be(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        var after = (await off.Content.ReadJsonAsync<ShopNotificationSettingsDto>())!;
        after.StaffPushEnabled.Should().BeFalse();
        after.CustomerWebPushEnabled.Should().BeFalse();
    }

    [Fact, TestCase("CY24-61")]
    public async Task ShopChannel_AssignmentAllowed_SettingsShowChannel_MessengerCanBeEnabled_PriorityMustBePaid()
    {
        var shop = await CreateShopAsync();
        var channelId = await ConnectShopChannelAsync(shop);
        var c = AuthedClient(shop.OwnerToken);
        var url = $"/api/shops/{shop.Id}/notification-settings";

        var s = (await (await c.GetAsync(url)).Content.ReadJsonAsync<ShopNotificationSettingsDto>())!;
        s.Channels.Should().ContainSingle(x => x.ChannelId == channelId);
        s.Channels[0].Funded.Should().BeTrue();
        s.Channels[0].IsConnected.Should().BeTrue();
        s.Channels[0].PhoneMasked.Should().Contain("*", "номер только маской");
        s.MessengerAvailable.Should().BeTrue();

        var on = await c.PutJsonAsync(url, new ShopNotificationSettingsInput(true, true, true, null, null));
        on.StatusCode.Should().Be(HttpStatusCode.OK, await on.Content.ReadAsStringAsync());
        (await GetStorefrontAsync(shop.Slug)).CustomerNotifications.MessengerOffered.Should().BeTrue();

        // приоритетный транспорт без оплаченного канала этого транспорта
        var badPriority = await c.PutJsonAsync(url, new ShopNotificationSettingsInput(true, true, true, NotificationDeliveryMode.PriorityChannel,
            s.Channels[0].Transport == NotificationTransport.Max ? NotificationTransport.WhatsApp : NotificationTransport.Max));
        badPriority.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badPriority.Content.ReadAsStringAsync()).Should().Contain("Приоритетный канал должен быть среди оплаченных каналов магазина");

        // повторное назначение того же канала тому же магазину — идемпотентно (201), как у салона
        var again = await c.PostJsonAsync($"/api/notification-channels/{channelId}/companies", new { companyId = shop.Id, warningAcknowledged = true });
        again.StatusCode.Should().Be(HttpStatusCode.Created);

        // другой номер того же мессенджера тому же магазину — 409 с текстом для магазина
        var second = await SeedFundedChannelAsync(shop);
        var conflict = await c.PostJsonAsync($"/api/notification-channels/{second}/companies", new { companyId = shop.Id, warningAcknowledged = true });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadAsStringAsync()).Should().Contain("Магазин уже привязан к другому номеру этого мессенджера");
    }

    [Fact, TestCase("CY24-62")]
    public async Task SalonRoutes_StayClosedForShop_409()
    {
        var shop = await CreateShopAsync();
        var c = AuthedClient(shop.OwnerToken);
        foreach (var path in new[] { "notification-settings", "staff-push-settings" })
            (await c.GetAsync($"/api/companies/{shop.Id}/{path}")).StatusCode.Should().Be(HttpStatusCode.Conflict, path);

        // и салонные записи-маршруты по-прежнему закрыты для магазина
        (await c.GetAsync($"/api/companies/{shop.Id}/services")).StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    // ── US-24-21: web-push покупателю без аккаунта ───────────────────────────────

    [Fact, TestCase("CY24-63")]
    public async Task OrderPush_Subscribe_Upsert_Remove_Idempotent_Limit5_TokenIsTheOnlyKey()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        var token = placed.Order.Token;
        var push = Fixture.ClassHost("push", cs => new PushEnabledFactory(cs));
        var c = PushClient(push);
        var url = $"/api/orders/public/{token}/push-subscription";

        (await GetPublicOrderAsync(token)).Notifications.WebPush.Available.Should().BeFalse("платформа выключена в тестовой конфигурации; на push-хосте ниже — иначе");
        var pubOnPush = (await (await c.GetAsync($"/api/orders/public/{token}")).Content.ReadJsonAsync<PublicOrderDto>())!;
        pubOnPush.Notifications.WebPush.Available.Should().BeTrue();
        pubOnPush.Notifications.MessengerRequested.Should().BeFalse();

        var e1 = "https://push.example.test/cy24/" + Guid.NewGuid();
        var created = await c.PostJsonAsync(url, Sub(e1, "Chrome"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await created.Content.ReadJsonAsync<OrderPushStateDto>())!.Should().Be(new OrderPushStateDto(true, 1));
        var again = await c.PostJsonAsync(url, Sub(e1));
        again.StatusCode.Should().Be(HttpStatusCode.OK, "тот же endpoint — обновление, а не вторая строка");
        (await again.Content.ReadJsonAsync<OrderPushStateDto>())!.SubscriptionCount.Should().Be(1);

        // потолок 5: шестая молча вытесняет самую старую
        for (var i = 0; i < 5; i++)
            (await c.PostJsonAsync(url, Sub($"https://push.example.test/cy24/{Guid.NewGuid()}"))).StatusCode.Should().Be(HttpStatusCode.Created);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orderId = await db.Orders.Where(o => o.PublicToken == token).Select(o => o.Id).SingleAsync();
            var rows = await db.OrderPushSubscriptions.Where(s => s.OrderId == orderId).ToListAsync();
            rows.Should().HaveCount(5);
            rows.Should().NotContain(r => r.Endpoint == e1, "вытеснена самая старая");
            rows.Should().OnlyContain(r => !r.P256dhCiphertext.Contains("BKey") && !r.AuthCiphertext.Contains("auth-"), "ключи хранятся зашифрованно");
        }

        // удаление идемпотентно
        var e2 = "https://push.example.test/cy24/" + Guid.NewGuid();
        await c.PostJsonAsync(url, Sub(e2));
        (await c.PostJsonAsync(url + "/remove", new OrderPushUnsubscribeInput(e2))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.PostJsonAsync(url + "/remove", new OrderPushUnsubscribeInput(e2))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // заказ не выдаёт ни endpoint, ни ключи
        var raw = await (await c.GetAsync($"/api/orders/public/{token}")).Content.ReadAsStringAsync();
        raw.Should().NotContain("push.example.test").And.NotContain("p256dh");
    }

    [Fact, TestCase("CY24-64")]
    public async Task OrderPush_Errors_UnknownTokenBodyValidation_ShopOff_FinishedOrder_PlatformOff()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        var token = placed.Order.Token;
        var push = Fixture.ClassHost("push", cs => new PushEnabledFactory(cs));
        var c = PushClient(push);
        var url = $"/api/orders/public/{token}/push-subscription";
        var good = Sub("https://push.example.test/cy24/" + Guid.NewGuid());

        // токен — единственный ключ: чужой/неверный — пустой 404 без оракула
        foreach (var bad in new[] { Guid.NewGuid().ToString("N"), "x", new string('a', 64) })
        {
            var r = await c.PostJsonAsync($"/api/orders/public/{bad}/push-subscription", good);
            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await r.Content.ReadAsStringAsync()).Should().BeEmpty();
        }
        (await c.PostJsonAsync("/api/orders/public/zzz/push-subscription/remove", new OrderPushUnsubscribeInput("https://x.test/a"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // формат тела (400, те же фразы, что у POST /api/push/subscriptions)
        async Task<string> Bad(OrderPushSubscribeInput input)
        {
            var r = await c.PostJsonAsync(url, input);
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            return await r.Content.ReadAsStringAsync();
        }
        (await Bad(good with { Endpoint = "not a url" })).Should().Contain("Некорректный адрес подписки (endpoint).");
        (await Bad(good with { Endpoint = "" })).Should().Contain("Некорректный адрес подписки (endpoint).");
        (await Bad(good with { Keys = new PushKeysInput("", "a") })).Should().Contain("Некорректный ключ подписки (p256dh).");
        (await Bad(good with { Keys = new PushKeysInput("k", "") })).Should().Contain("Некорректный ключ подписки (auth).");
        (await Bad(good with { DeviceLabel = new string('я', 101) })).Should().Contain("Слишком длинное название устройства.");

        // платформа выключена (обычный тестовый хост)
        var off = await AnonymousClient().PostJsonAsync(url, good);
        off.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await off.Content.ReadAsStringAsync()).Should().Contain("Уведомления в браузере пока не включены на платформе.");
        var pub = await GetPublicOrderAsync(token);
        pub.Notifications.WebPush.Available.Should().BeFalse();
        pub.Notifications.WebPush.UnavailableText.Should().Be("Уведомления в браузере пока не включены на платформе");

        // магазин выключил web-push
        var settings = $"/api/shops/{shop.Id}/notification-settings";
        (await AuthedClient(shop.OwnerToken).PutJsonAsync(settings, new ShopNotificationSettingsInput(true, false, false, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        var shopOff = await c.PostJsonAsync(url, good);
        shopOff.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await shopOff.Content.ReadAsStringAsync()).Should().Contain("Магазин не отправляет уведомления в браузер");
        var pubOff = (await (await c.GetAsync($"/api/orders/public/{token}")).Content.ReadJsonAsync<PublicOrderDto>())!;
        pubOff.Notifications.WebPush.Available.Should().BeFalse();
        pubOff.Notifications.WebPush.UnavailableText.Should().BeNull("кнопку не показывать");
        (await GetStorefrontAsync(shop.Slug)).CustomerNotifications.WebPushOffered.Should().BeFalse();
        (await AuthedClient(shop.OwnerToken).PutJsonAsync(settings, new ShopNotificationSettingsInput(true, true, false, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);

        // заказ в конечном статусе
        var order = await GetStaffOrderAsync(shop, token);
        var rejected = await ActOkAsync(shop, order, "reject", reason: "нет товара");
        rejected.Status.Should().Be(OrderStatus.Rejected);
        var done = await c.PostJsonAsync(url, good);
        done.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await done.Content.ReadAsStringAsync()).Should().Contain("Заказ уже завершён — уведомления по нему не приходят");
        (await (await c.GetAsync($"/api/orders/public/{token}")).Content.ReadJsonAsync<PublicOrderDto>())!.Notifications.WebPush.UnavailableText.Should().BeNull();
    }

    // ── US-24-15: push персоналу и устройства ────────────────────────────────────

    [Fact, TestCase("CY24-65")]
    public async Task PushConfigAndDevices_AreSeparatedBySite_ShopsOnlyOnOrders_InvalidSite400()
    {
        var owner = await RegisterAsync();
        var company = await CreateCompanyAsync(owner.Token);
        var (token, shopDto) = await CreateShopForAsync(owner.Token);
        await GiveActivePaidPlanAsync(owner.UserId);
        var push = Fixture.ClassHost("push", cs => new PushEnabledFactory(cs));
        var c = PushClient(push, token);

        var services = await J(await c.GetAsync("/api/push/config"));
        services.GetProperty("companies").EnumerateArray().Select(x => x.GetProperty("companyId").GetGuid()).Should().Contain(company.Id).And.NotContain(shopDto.Id);
        var orders = await J(await c.GetAsync("/api/push/config?site=Orders"));
        orders.GetProperty("companies").EnumerateArray().Select(x => x.GetProperty("companyId").GetGuid()).Should().Contain(shopDto.Id).And.NotContain(company.Id);
        orders.GetProperty("site").GetString().Should().Be("Orders");
        (await c.GetAsync("/api/push/config?site=Nope")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var goodsEndpoint = "https://push.example.test/cy24-goods/" + Guid.NewGuid();
        var ezEndpoint = "https://push.example.test/cy24-ez/" + Guid.NewGuid();
        (await c.PostJsonAsync("/api/push/subscriptions", new CreatePushSubscriptionInput(goodsEndpoint, new CreatePushSubscriptionKeysInput("p", "a"), "goods", CompanyKind.Orders)))
            .IsSuccessStatusCode.Should().BeTrue();
        (await c.PostJsonAsync("/api/push/subscriptions", new CreatePushSubscriptionInput(ezEndpoint, new CreatePushSubscriptionKeysInput("p", "a"), "ezbook")))
            .IsSuccessStatusCode.Should().BeTrue();

        var listDefault = await J(await c.GetAsync("/api/push/subscriptions"));
        listDefault.GetProperty("items").GetArrayLength().Should().Be(1, "по умолчанию — устройства ezbook");
        var listOrders = await J(await c.GetAsync("/api/push/subscriptions?site=Orders"));
        listOrders.GetProperty("items").GetArrayLength().Should().Be(1);
        listOrders.GetProperty("items")[0].GetProperty("deviceLabel").GetString().Should().Be("goods");
        listOrders.GetRawText().Should().NotContain("p256dh").And.NotContain(goodsEndpoint);
    }

    [Fact, TestCase("CY24-66")]
    public async Task NewOrder_QueuesStaffPushToAllDevices_TextWithoutCustomerData_SettingOffStopsIt()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop, "Шаурма", 250m);
        var push = Fixture.ClassHost("push", cs => new PushEnabledFactory(cs));

        async Task Subscribe(string token, string endpoint, CompanyKind? site) =>
            (await PushClient(push, token).PostJsonAsync("/api/push/subscriptions",
                new CreatePushSubscriptionInput(endpoint, new CreatePushSubscriptionKeysInput("p", "a"), "d", site))).IsSuccessStatusCode.Should().BeTrue();
        await Subscribe(shop.OwnerToken, "https://push.example.test/cy24-o-goods/" + Guid.NewGuid(), CompanyKind.Orders);
        await Subscribe(shop.OwnerToken, "https://push.example.test/cy24-o-ez/" + Guid.NewGuid(), null);
        await Subscribe(staff.Token, "https://push.example.test/cy24-s-goods/" + Guid.NewGuid(), CompanyKind.Orders);

        var customerName = "Секретный Покупатель";
        var phone = UniquePhone();
        var input = new CreateOrderInput(Guid.NewGuid(), [Line(p, 2)], customerName, phone, null, null);
        var r = await PushClient(push).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", input);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var number = (await r.Content.ReadJsonAsync<CreateOrderResponse>())!.Order.Number;

        List<StaffPushNotification> Rows() { using var scope = Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); return db.StaffPushNotifications.Include(x => x.Subscription).Where(x => x.CompanyId == shop.Id).ToList(); }
        var rows = Rows();
        rows.Should().HaveCount(3, "цикл 33: по одной строке на каждое устройство получателя, любого сайта (2 у владельца, 1 у сотрудника)");
        rows.Should().OnlyContain(x => x.OrderId != null && x.Type == NotificationType.StaffOrderCreated);
        rows.Select(x => x.UserId).Should().BeEquivalentTo(new[] { shop.Owner.UserId, shop.Owner.UserId, staff.UserId });
        rows.Single(x => x.Subscription!.Site == CompanyKind.Services).Payload.Should().Contain("\"url\":\"http", "на устройство ezbook url абсолютный");
        rows.Where(x => x.Subscription!.Site == CompanyKind.Orders).Should().OnlyContain(x => x.Payload.Contains("\"url\":\"/cabinet/"));
        foreach (var row in rows)
        {
            row.Payload.Should().Contain($"Новый заказ № {number}");
            row.Payload.Should().NotContain(customerName).And.NotContain(phone).And.NotContain(phone.TrimStart('+'));
            row.Payload.Should().Contain("/cabinet/").And.Contain("orders");
            row.Payload.Should().NotContain("\\u", "кириллица без unicode-escapes");
        }

        // выключатель магазина «Push сотрудникам о новых заказах» — новых строк нет
        (await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/notification-settings",
            new ShopNotificationSettingsInput(false, true, false, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PushClient(push).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);
        Rows().Should().HaveCount(3);
    }

    // ── US-24-20/22: сообщения покупателю в мессенджер ───────────────────────────

    [Fact, TestCase("CY24-67")]
    public async Task MessengerRequested_StatusMessagesQueuedPerEvent_NoNameNoPhoneInText_CustomerCancelSilent()
    {
        var shop = await CreateShopAsync();
        await ConnectShopChannelAsync(shop);
        (await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/notification-settings",
            new ShopNotificationSettingsInput(true, true, true, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        var p = await CreateProductAsync(shop);

        var phone = UniquePhone();
        var placed = await PlaceOrderAsync(shop.Slug, new CreateOrderInput(Guid.NewGuid(), [Line(p, 1)], "Тайное Имя", phone, null, null, null, true));
        placed.Order.Notifications.MessengerRequested.Should().BeTrue();
        var order = await GetStaffOrderAsync(shop, placed.Order.Token);
        order.NotifyByMessenger.Should().BeTrue();

        List<OutboundNotification> Out()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return db.OutboundNotifications.Where(n => n.OrderId == order.Id).OrderBy(n => n.CreatedAt).ToList();
        }
        Out().Should().BeEmpty("при ручном приёме сообщение уйдёт, когда персонал примет заказ");

        var accepted = await ActOkAsync(shop, order, "accept");
        var afterAccept = Out();
        afterAccept.Should().ContainSingle(n => n.Type == NotificationType.OrderAccepted);
        var msg = afterAccept.Single().Body;
        msg.Should().Contain($"№ {order.Number}").And.Contain(shop.Shop.Name).And.Contain("принят");
        msg.Should().Contain("/o/", "ссылка на страницу заказа");
        msg.Should().NotContain("Тайное Имя").And.NotContain(phone);
        afterAccept.Single().RecipientPhone.Should().NotBeNullOrEmpty();

        var ready = await ActOkAsync(shop, accepted, "ready");
        Out().Should().Contain(n => n.Type == NotificationType.OrderReady);
        var card = await GetStaffOrderAsync(shop, placed.Order.Token);
        card.Messenger.Should().NotBeNull();
        card.Messenger!.Requested.Should().BeTrue();
        card.Messenger.Status.Should().NotBeNull("статус доставки берётся из журнала (US-24-23)");
        var issued = await ActOkAsync(shop, ready, "issue");
        Out().Count.Should().Be(2, "«выдан» — сообщения нет");
        _ = issued;

        // отказ магазина — с причиной; отмена самим покупателем — без сообщения
        var p2 = await PlaceOrderAsync(shop.Slug, new CreateOrderInput(Guid.NewGuid(), [Line(p, 1)], "Тайное Имя", UniquePhone(), null, null, null, true));
        var o2 = await GetStaffOrderAsync(shop, p2.Order.Token);
        await ActOkAsync(shop, o2, "reject", reason: "закончились продукты");
        OutboundBy(o2.Id).Should().ContainSingle(n => n.Type == NotificationType.OrderRejected && n.Body.Contains("закончились продукты"));

        var p3 = await PlaceOrderAsync(shop.Slug, new CreateOrderInput(Guid.NewGuid(), [Line(p, 1)], "Тайное Имя", UniquePhone(), null, null, null, true));
        var cancel = await AnonymousClient().PostAsync($"/api/orders/public/{p3.Order.Token}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var o3 = await GetStaffOrderAsync(shop, p3.Order.Token);
        OutboundBy(o3.Id).Should().BeEmpty("если заказ отменил сам покупатель, сообщение не уходит");

        // без галочки — нет сообщений вообще
        var p4 = await PlaceOrderAsync(shop.Slug, new CreateOrderInput(Guid.NewGuid(), [Line(p, 1)], "Иван", UniquePhone(), null, null, null, false));
        var o4 = await GetStaffOrderAsync(shop, p4.Order.Token);
        o4.NotifyByMessenger.Should().BeFalse();
        await ActOkAsync(shop, o4, "accept");
        OutboundBy(o4.Id).Should().BeEmpty();

        List<OutboundNotification> OutboundBy(Guid orderId)
        {
            using var scope = Factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboundNotifications.Where(n => n.OrderId == orderId).ToList();
        }
    }

    [Fact, TestCase("CY24-68")]
    public async Task MessengerFlag_IgnoredWhenShopDoesNotOfferIt_AndOffSwitchStopsMessages()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        // магазин мессенджер не предлагает: галочка гостя игнорируется, заказ создаётся
        var placed = await PlaceOrderAsync(shop.Slug, new CreateOrderInput(Guid.NewGuid(), [Line(p, 1)], "Иван", UniquePhone(), null, null, null, true));
        placed.Order.Notifications.MessengerRequested.Should().BeFalse();

        await ConnectShopChannelAsync(shop);
        var settings = $"/api/shops/{shop.Id}/notification-settings";
        (await AuthedClient(shop.OwnerToken).PutJsonAsync(settings, new ShopNotificationSettingsInput(true, true, true, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        var placed2 = await PlaceOrderAsync(shop.Slug, new CreateOrderInput(Guid.NewGuid(), [Line(p, 1)], "Иван", UniquePhone(), null, null, null, true));
        placed2.Order.Notifications.MessengerRequested.Should().BeTrue();

        // владелец выключил мессенджер: подписка на созданный заказ не отменяется, но новые сообщения по нему не уходят
        (await AuthedClient(shop.OwnerToken).PutJsonAsync(settings, new ShopNotificationSettingsInput(true, true, false, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        var o2 = await GetStaffOrderAsync(shop, placed2.Order.Token);
        await ActOkAsync(shop, o2, "accept");
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboundNotifications.CountAsync(n => n.OrderId == o2.Id)).Should().Be(0);
    }

    [Fact, TestCase("CY24-69")]
    public async Task RemovedStaff_StopsGettingShopPushAtOnce_CustomerCancelPushedToRemainingStaff()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        var push = Fixture.ClassHost("push", cs => new PushEnabledFactory(cs));
        foreach (var t in new[] { shop.OwnerToken, staff.Token })
            (await PushClient(push, t).PostJsonAsync("/api/push/subscriptions", new CreatePushSubscriptionInput(
                "https://push.example.test/cy24-rm/" + Guid.NewGuid(), new CreatePushSubscriptionKeysInput("p", "a"), "d", CompanyKind.Orders)))
                .IsSuccessStatusCode.Should().BeTrue();

        var members = await J(await AuthedClient(shop.OwnerToken).GetAsync($"/api/companies/{shop.Id}/members"));
        var memberId = members.EnumerateArray().First(m => m.GetProperty("userId").GetString() == staff.UserId).GetProperty("id").GetString();
        (await AuthedClient(shop.OwnerToken).DeleteAsync($"/api/companies/{shop.Id}/members/{memberId}")).IsSuccessStatusCode.Should().BeTrue();

        var placed = await PushClient(push).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]));
        placed.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = (await placed.Content.ReadJsonAsync<CreateOrderResponse>())!.Order;

        List<StaffPushNotification> Rows()
        {
            using var scope = Factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().StaffPushNotifications.Where(x => x.CompanyId == shop.Id).ToList();
        }
        Rows().Should().ContainSingle().Which.UserId.Should().Be(shop.Owner.UserId, "удалённый сотрудник получателем не становится");

        // P1: покупатель сам отменил заказ — push персоналу «Покупатель отменил заказ № N»
        var cancel = await PushClient(push).PostAsync($"/api/orders/public/{order.Token}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = Rows();
        rows.Should().Contain(x => x.Type == NotificationType.StaffOrderCancelledByCustomer && x.Payload.Contains($"Покупатель отменил заказ № {order.Number}"));
    }

    [Fact, TestCase("CY24-93")]
    public async Task MessengerMessages_ForShopEditAndPickupChange_QueuedWithContractWording()
    {
        var shop = await CreateScheduledShopAsync();
        await ConnectShopChannelAsync(shop);
        (await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/notification-settings",
            new ShopNotificationSettingsInput(true, true, true, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        var p = await CreateProductAsync(shop, "Плов", 300m);
        var tomorrow = ShopToday(shop).AddDays(1);
        var placed = await PlaceOrderAsync(shop.Slug, new CreateOrderInput(Guid.NewGuid(), [Line(p, 2)], "Иван", UniquePhone(), null, null,
            await SlotAsync(shop.Slug, tomorrow, 0), true));
        var order = await GetStaffOrderAsync(shop, placed.Order.Token);
        var accepted = await ActOkAsync(shop, order, "accept");

        List<OutboundNotification> Out()
        {
            using var scope = Factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboundNotifications.Where(n => n.OrderId == order.Id).ToList();
        }
        Out().Single(n => n.Type == NotificationType.OrderAccepted).Body.Should().Contain("завтра к ", "время строчными: «завтра к 12:30»");

        var edit = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/orders/{order.Id}/items",
            new EditOrderInput(accepted.Version, [new EditOrderLineInput(accepted.Items[0].Id, null, 1)], "не хватило"));
        edit.StatusCode.Should().Be(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        var edited = (await edit.Content.ReadJsonAsync<StaffOrderDto>())!;
        Out().Should().Contain(n => n.Type == NotificationType.OrderEditedByShop && n.Body.Contains("изменил заказ") && n.Body.Contains("300"));

        var slots = (await (await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/pickup-slots?date={D(tomorrow)}")).Content.ReadJsonAsync<PickupSlotsDto>())!;
        var change = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/orders/{order.Id}/pickup",
            new ChangePickupInput(edited.Version, new PickupSelectionInput(PickupKind.Slot, tomorrow, slots.Slots[4].StartUtc), null));
        change.StatusCode.Should().Be(HttpStatusCode.OK, await change.Content.ReadAsStringAsync());
        Out().Should().Contain(n => n.Type == NotificationType.OrderPickupChanged && n.Body.Contains("изменено время получения"));
    }

    [Fact, TestCase("CY24-94")]
    public async Task OwnerGetsLimitWarningPush_At80AndAt100Percent_OncePerLevel()
    {
        var shop = await CreateShopAsync();
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var plan = await J(await admin.PostJsonAsync("/api/admin/plans", new
        {
            name = Unique("Лимит · "), pricePerMonth = 1m, maxEmployees = 5, maxCompanies = 3, isPublic = true, isActive = true, line = "Orders",
            maxProductsPerShop = 100, maxOrdersPerMonth = 5, allowOrders = true,
        }));
        var accountId = await Task.Run(async () =>
        {
            using var scope = Factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().BillingAccounts.Where(a => a.OwnerUserId == shop.Owner.UserId).Select(a => a.Id).FirstAsync();
        });
        (await admin.PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", new
        {
            planId = plan.GetProperty("id").GetGuid(), isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            options = Array.Empty<object>(), line = "Orders",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var push = Fixture.ClassHost("push", cs => new PushEnabledFactory(cs));
        (await PushClient(push, shop.OwnerToken).PostJsonAsync("/api/push/subscriptions", new CreatePushSubscriptionInput(
            "https://push.example.test/cy24-lim/" + Guid.NewGuid(), new CreatePushSubscriptionKeysInput("p", "a"), "d", CompanyKind.Orders)))
            .IsSuccessStatusCode.Should().BeTrue();
        var p = await CreateProductAsync(shop);

        List<StaffPushNotification> Warn()
        {
            using var scope = Factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().StaffPushNotifications
                .Where(x => x.Type == NotificationType.OwnerOrderLimitWarning && x.UserId == shop.Owner.UserId).ToList();
        }
        for (var i = 0; i < 3; i++) (await PushClient(push).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);
        Warn().Should().BeEmpty("до 80 % предупреждения нет");
        (await PushClient(push).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);
        Warn().Should().ContainSingle().Which.Payload.Should().Contain("80").And.Contain("4 из 5");
        (await PushClient(push).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);
        Warn().Should().HaveCount(2);
        Warn().Select(x => x.Payload).Should().Contain(x => x.Contains("Лимит заказов исчерпан"));
        (await PushClient(push).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        Warn().Should().HaveCount(2, "предупреждение — один раз на уровень");
    }
}
