using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.PhoneVerification;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.DTOs.StaffMax;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.API.Services.PhoneVerification.Max;
using ServiceBooking.API.Services.StaffMax;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 25, «Вызов 2», блок A: «Заказы в MAX» для персонала (US-25-01…04, T-25 «одно событие — одно сообщение в чат»).
/// Написано по SPEC.md и API_CONTRACT_CYCLE25.md §523–§525, а не по реализации. Хост с включённым MAX — <see cref="StaffMaxTestFactory"/>
/// (обе границы MAX подменены записывающими двойниками, сети нет); проход диспетчера запускается тестом явно.
/// </summary>
public class Cycle25StaffMaxTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private const string Hmac = PhoneVerificationEnabledFactory.TestExternalKeyHmacBase64;

    /// <summary>
    /// Хост с MAX. Диспетчер просматривает ВСЮ очередь базы класса, а тесты класса делят одну базу, поэтому перед каждым тестом очередь
    /// очищается: строка другого теста иначе ушла бы через двойник этого хоста и исказила бы подсчёт его отправок.
    /// </summary>
    private async Task<StaffMaxTestFactory> MxAsync(bool platformEnabled = true, bool purgeQueue = true)
    {
        var host = new StaffMaxTestFactory(ConnectionString, platformEnabled);
        host.EnsureWebhookSubscribed();
        if (purgeQueue) await WithDbAsync(db => db.StaffMaxMessages.ExecuteDeleteAsync());
        return host;
    }

    private static string NewChat(string tag) => $"chat25-{tag}-{Guid.NewGuid():N}"[..28];

    private static Task<HttpResponseMessage> Webhook(HttpClient client, object body) =>
        client.PostAsJsonAsync($"/api/phone-verification/max/webhook/{StaffMaxTestFactory.WebhookToken}", body);

    private static object Start(string payload, string sender, string chat) => new
    {
        update_type = "bot_started", payload, user = new { user_id = sender }, chat = new { chat_id = chat }
    };

    private static object Stopped(string chat) => new { update_type = "bot_stopped", chat_id = chat, user = new { user_id = "stopper" } };

    private static string PayloadOf(string deepLink) => HttpUtility.ParseQueryString(new Uri(deepLink).Query)["start"]!;

    private static async Task<StaffMaxStatusDto> StatusOfAsync(HttpClient client)
    {
        var r = await client.GetAsync("/api/staff-max");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaffMaxStatusDto>())!;
    }

    private static async Task<StaffMaxLinkSessionDto> NewSessionAsync(HttpClient client)
    {
        var r = await client.PostAsync("/api/staff-max/link-sessions", null);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaffMaxLinkSessionDto>())!;
    }

    /// <summary>Полное подключение: одноразовая ссылка → «Начать» из чата <paramref name="chat"/> (отправитель <paramref name="sender"/>).</summary>
    private async Task<string> LinkAsync(StaffMaxTestFactory mx, string token, string chat, string? sender = null)
    {
        var client = ClientOn(mx, token);
        var session = await NewSessionAsync(client);
        var payload = PayloadOf(session.DeepLink);
        (await Webhook(client, Start(payload, sender ?? "u-" + Guid.NewGuid().ToString("N")[..8], chat))).StatusCode.Should().Be(HttpStatusCode.OK);
        return payload;
    }

    private async Task<CreateOrderResponse> PlaceViaAsync(StaffMaxTestFactory mx, ShopCtx shop, ProductDto product, string? buyerToken = null, string name = "Иван", string? phone = null)
    {
        var input = new CreateOrderInput(Guid.NewGuid(), [Line(product, 1)], name, phone ?? UniquePhone(), null, null);
        var r = await ClientOn(mx, buyerToken).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", input);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<CreateOrderResponse>())!;
    }

    private Task<List<StaffMaxMessage>> RowsAsync(ShopCtx shop) =>
        WithDbAsync(db => db.StaffMaxMessages.AsNoTracking().Where(m => m.CompanyId == shop.Id).OrderBy(m => m.CreatedAt).ToListAsync());

    /// <summary>Отмена покупателем — через хост с включённым MAX: сообщения персоналу ставит планировщик уведомлений ЭТОГО хоста.</summary>
    private static async Task CancelViaAsync(StaffMaxTestFactory mx, string publicToken)
    {
        var r = await ClientOn(mx).PostAsync($"/api/orders/public/{publicToken}/cancel", null);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    private static string KeyOf(string chat) => StaffMaxChatKey.Compute(Hmac, chat);

    // ── US-25-01 / Q-25-2: платформенный рубильник ──────────────────────────────────

    [Fact, TestCase("CY25-01")]
    public async Task PlatformSwitchOff_CabinetExplains_LinkRefused_ShopFlagStillSavable()
    {
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var outsider = await RegisterAsync();

        var st = await StatusOfAsync(AuthedClient(shop.OwnerToken));
        st.Available.Should().BeFalse("по умолчанию (репозиторий, стенд без STAFFMAX_ENABLED) функция выключена");
        st.CanLink.Should().BeFalse();
        st.UnavailableText.Should().Be("Сообщения в MAX пока не включены на платформе");
        st.Eligible.Should().BeTrue();
        st.Status.Should().Be(StaffMaxStatus.NotLinked);
        st.StatusText.Should().Be("Не подключено");
        st.PendingSession.Should().BeNull();
        st.Shops.Should().ContainSingle(s => s.ShopId == shop.Id && s.StaffMaxEnabled);
        st.PollIntervalSeconds.Should().BeGreaterThan(0);

        var refused = await AuthedClient(shop.OwnerToken).PostAsync("/api/staff-max/link-sessions", null);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Сообщения в MAX пока не включены на платформе");

        // не участник ни одного магазина: блок скрыт (eligible=false), ссылку не выдают — и это проверяется РАНЬШЕ рубильника
        (await StatusOfAsync(AuthedClient(outsider.Token))).Eligible.Should().BeFalse();
        var notMember = await AuthedClient(outsider.Token).PostAsync("/api/staff-max/link-sessions", null);
        notMember.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await notMember.Content.ReadAsStringAsync()).Should().Contain("Подключить MAX могут владельцы и сотрудники магазинов");
        (await AuthedClient(outsider.Token).DeleteAsync("/api/staff-max/link")).StatusCode.Should().Be(HttpStatusCode.NoContent, "отключение идемпотентно");
        (await AnonymousClient().GetAsync("/api/staff-max")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AnonymousClient().PostAsync("/api/staff-max/link-sessions", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // настройка магазина независима от рубильника платформы
        var url = $"/api/shops/{shop.Id}/notification-settings";
        var s0 = (await (await AuthedClient(shop.OwnerToken).GetAsync(url)).Content.ReadJsonAsync<ShopNotificationSettingsDto>())!;
        s0.StaffMaxEnabled.Should().BeTrue("по умолчанию включена");
        s0.StaffMaxAvailable.Should().BeFalse();
        s0.StaffMaxUnavailableText.Should().Be("Сообщения в MAX пока не включены на платформе");

        var off = await AuthedClient(shop.OwnerToken).PutJsonAsync(url, new { staffPushEnabled = true, customerWebPushEnabled = true, customerMessengerEnabled = false, staffMaxEnabled = false });
        off.StatusCode.Should().Be(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        (await off.Content.ReadJsonAsync<ShopNotificationSettingsDto>())!.StaffMaxEnabled.Should().BeFalse("сохранить можно и при выключенной платформе");

        // фронт цикла 24 не знает про поле: не передано — не менять
        var legacy = await AuthedClient(shop.OwnerToken).PutJsonAsync(url, new { staffPushEnabled = true, customerWebPushEnabled = false, customerMessengerEnabled = false });
        legacy.StatusCode.Should().Be(HttpStatusCode.OK, await legacy.Content.ReadAsStringAsync());
        var after = (await legacy.Content.ReadJsonAsync<ShopNotificationSettingsDto>())!;
        after.StaffMaxEnabled.Should().BeFalse("отсутствие поля не сбрасывает и не включает флаг");
        after.CustomerWebPushEnabled.Should().BeFalse();

        (await AuthedClient(staff.Token).PutJsonAsync(url, new { staffPushEnabled = true, customerWebPushEnabled = true, customerMessengerEnabled = false, staffMaxEnabled = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "флаг магазина меняет только владелец");
        (await AuthedClient(staff.Token).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── US-25-01: подключение ───────────────────────────────────────────────────────

    [Fact, TestCase("CY25-02")]
    public async Task Link_SessionOneTimePayload_StartLinksChat_CabinetShowsLinked_NoChatIdAnywhere()
    {
        var shop = await CreateRoundClockShopAsync();
        await GiveOrdersPlanAsync(shop.Owner.UserId, maxShops: 3);
        var second = await CreateShopForAsync(shop.OwnerToken, name: "Пекарня " + Unique("x"));
        await using var mx = await MxAsync();
        var client = ClientOn(mx, shop.OwnerToken);
        var chat = NewChat("own");

        var before = await StatusOfAsync(client);
        before.Available.Should().BeTrue();
        before.CanLink.Should().BeTrue();
        before.UnavailableText.Should().BeNull();

        var session = await NewSessionAsync(client);
        session.DeepLink.Should().StartWith($"https://max.ru/{StaffMaxTestFactory.BotUsername}?start=sm1.");
        session.WebLink.Should().StartWith($"https://web.max.ru/{StaffMaxTestFactory.BotUsername}?start=sm1.");
        session.QrPngBase64.Should().NotBeNullOrEmpty("на компьютере рядом QR");
        session.TtlSeconds.Should().Be(600);
        session.PollIntervalSeconds.Should().BeGreaterThan(0);
        session.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromMinutes(1));
        var payload = PayloadOf(session.DeepLink);
        payload.Length.Should().BeLessOrEqualTo(128);

        var pending = await StatusOfAsync(client);
        pending.Status.Should().Be(StaffMaxStatus.Pending);
        pending.StatusText.Should().Be("Ждём подтверждения в MAX…");
        pending.PendingSession!.SessionId.Should().Be(session.SessionId);

        (await Webhook(client, Start(payload, "sender-1", chat))).StatusCode.Should().Be(HttpStatusCode.OK, "вебхук отвечает 200 всегда");
        var linked = await StatusOfAsync(client);
        linked.Status.Should().Be(StaffMaxStatus.Linked, "«Подключено» сразу после «Начать» (требование — не позже 5 с)");
        linked.StatusText.Should().StartWith("Подключено ").And.MatchRegex(@"\d\d\.\d\d\.\d{4}$");
        linked.LinkedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        linked.PendingSession.Should().BeNull();
        linked.Shops.Should().HaveCount(2, "сообщения придут из обоих магазинов человека");

        // ответ бота: для каких магазинов и как отключить
        var reply = mx.BotClient.SentMessages.Single(m => m.ChatId == chat);
        reply.Text.Should().StartWith("Готово: сюда будут приходить новые заказы и отмены покупателями из магазинов:");
        reply.Text.Should().Contain($"«{shop.Shop.Name}»").And.Contain($"«{second.Shop.Name}»").And.Contain("Отключить можно");
        reply.RequestContact.Should().BeFalse("это не подтверждение телефона — контакт не запрашивается");

        // идентификатор чата не отдаётся никогда: ни в статусе, ни в сессии
        var raw = await (await client.GetAsync("/api/staff-max")).Content.ReadAsStringAsync();
        raw.Should().NotContain(chat).And.NotContainEquivalentOf("chatKey").And.NotContainEquivalentOf("chatId");
        JsonSessionDoesNotLeak(session, chat);

        // в БД — только производный ключ и шифртекст
        var link = await WithDbAsync(db => db.StaffMaxLinks.AsNoTracking().SingleAsync(l => l.UserId == shop.Owner.UserId));
        link.Status.Should().Be(StaffMaxLinkStatus.Active);
        link.ChatKey.Should().Be(KeyOf(chat)).And.NotContain(chat);
        link.ChatIdCiphertext.Should().NotBeNullOrEmpty().And.NotContain(chat);

        // повтор той же ссылки (повторное «Начать») — не дубль привязки
        mx.BotClient.SentMessages.Clear();
        (await Webhook(client, Start(payload, "sender-1", chat))).StatusCode.Should().Be(HttpStatusCode.OK);
        mx.BotClient.SentMessages.Single().Text.Should().Be("Этот чат уже подключён к заказам ezbook.");
        (await WithDbAsync(db => db.StaffMaxLinks.CountAsync(l => l.UserId == shop.Owner.UserId))).Should().Be(1);
    }

    private static void JsonSessionDoesNotLeak(StaffMaxLinkSessionDto session, string chat) =>
        System.Text.Json.JsonSerializer.Serialize(session).Should().NotContain(chat);

    [Fact, TestCase("CY25-03")]
    public async Task Link_UnknownExpiredAndSupersededPayloads_AreRefused_NothingLinked()
    {
        var shop = await CreateRoundClockShopAsync();
        await using var mx = await MxAsync();
        var client = ClientOn(mx, shop.OwnerToken);

        // неизвестная ссылка
        var chat1 = NewChat("unk");
        (await Webhook(client, Start("sm1.this-payload-was-never-issued", "s1", chat1))).StatusCode.Should().Be(HttpStatusCode.OK);
        mx.BotClient.SentMessages.Single(m => m.ChatId == chat1).Text.Should().StartWith("Ссылка устарела. Получите новую в кабинете goods.ezbook.ru");

        // истёкшая ссылка
        var s = await NewSessionAsync(client);
        await WithDbAsync(async db =>
        {
            var row = await db.StaffMaxLinkSessions.SingleAsync(x => x.Id == s.SessionId);
            row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });
        var chat2 = NewChat("exp");
        await Webhook(client, Start(PayloadOf(s.DeepLink), "s2", chat2));
        mx.BotClient.SentMessages.Single(m => m.ChatId == chat2).Text.Should().StartWith("Ссылка устарела.");
        (await StatusOfAsync(client)).Status.Should().Be(StaffMaxStatus.NotLinked, "истёкшая сессия не считается ожидающей");

        // новая ссылка заменяет прежнюю незавершённую: старая больше не работает
        var first = await NewSessionAsync(client);
        var newer = await NewSessionAsync(client);
        var chat3 = NewChat("old");
        await Webhook(client, Start(PayloadOf(first.DeepLink), "s3", chat3));
        mx.BotClient.SentMessages.Single(m => m.ChatId == chat3).Text.Should().StartWith("Ссылка устарела.");
        (await WithDbAsync(db => db.StaffMaxLinks.CountAsync(l => l.UserId == shop.Owner.UserId))).Should().Be(0);

        // а новая — работает
        var chat4 = NewChat("new");
        await Webhook(client, Start(PayloadOf(newer.DeepLink), "s4", chat4));
        (await StatusOfAsync(client)).Status.Should().Be(StaffMaxStatus.Linked);
    }

    [Fact, TestCase("CY25-04")]
    public async Task Link_Again_ReplacesTheChat_OneBindingPerPerson_MessagesGoToTheNewChatOnly()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        var chatOld = NewChat("old");
        var chatNew = NewChat("new");
        await LinkAsync(mx, shop.OwnerToken, chatOld);
        await LinkAsync(mx, shop.OwnerToken, chatNew);

        (await WithDbAsync(db => db.StaffMaxLinks.CountAsync(l => l.UserId == shop.Owner.UserId))).Should().Be(1, "у человека одна привязка (Q-25-3)");
        (await StatusOfAsync(ClientOn(mx, shop.OwnerToken))).Status.Should().Be(StaffMaxStatus.Linked);

        await PlaceViaAsync(mx, shop, p);
        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(chatNew).Should().ContainSingle();
        mx.Messenger.TextsTo(chatOld).Should().BeEmpty("прежний чат заменён");
    }

    // ── US-25-02: состав и получатели сообщений ─────────────────────────────────────

    [Fact, TestCase("CY25-05")]
    public async Task NewOrderAndCustomerCancel_MessageTextsAndLink_NoCustomerData_SelfOrderExcluded()
    {
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop, "Шаурма", 250m);
        await using var mx = await MxAsync();
        var chatOwner = NewChat("own");
        var chatStaff = NewChat("stf");
        await LinkAsync(mx, shop.OwnerToken, chatOwner);
        await LinkAsync(mx, staff.Token, chatStaff);

        var secretName = "Секретный Покупатель";
        var phone = UniquePhone();
        var placed = await PlaceViaAsync(mx, shop, p, name: secretName, phone: phone);
        var order = await GetStaffOrderAsync(shop, placed.Order.Token);
        var rows = await RowsAsync(shop);
        rows.Should().HaveCount(2, "по одной строке на чат");
        rows.Should().OnlyContain(r => r.Type == NotificationType.StaffOrderCreated && r.Status == NotificationStatus.Pending && r.OrderId == order.Id);

        await mx.RunDispatchPassAsync();
        foreach (var chat in new[] { chatOwner, chatStaff })
        {
            var texts = mx.Messenger.TextsTo(chat);
            texts.Should().ContainSingle();
            var lines = texts[0].Split('\n');
            lines[0].Should().StartWith($"Новый заказ № {order.Number} · как можно скорее · 1 позиция · ≈ 250").And.EndWith($" · {shop.Shop.Name}");
            lines[0].Should().MatchRegex(@"≈ 250\s₽ · ");
            lines[1].Should().StartWith("Открыть: https://").And.EndWith($"/cabinet/{shop.Id}/orders?order={order.Id}");
            texts[0].Should().NotContain(secretName).And.NotContain(phone).And.NotContain(phone.TrimStart('+'), "имени и телефона покупателя нет [legal L15]");
        }
        (await RowsAsync(shop)).Should().OnlyContain(r => r.Status == NotificationStatus.Sent);

        // отмена самим покупателем
        await CancelViaAsync(mx, placed.Order.Token);
        var rowsAfter = await RowsAsync(shop);
        rowsAfter.Count(r => r.Type == NotificationType.StaffOrderCancelledByCustomer).Should().Be(2);
        await mx.RunDispatchPassAsync();
        var cancelText = mx.Messenger.TextsTo(chatStaff).Last();
        cancelText.Split('\n')[0].Should().Be($"Покупатель отменил заказ № {order.Number} (как можно скорее) · {shop.Shop.Name}");
        cancelText.Should().Contain($"/cabinet/{shop.Id}/orders?order={order.Id}");

        // заказ, оформленный самим сотрудником, ему не приходит; владельцу — приходит
        mx.Messenger.Calls.Count.Should().Be(4, string.Join(" || ", mx.Messenger.Calls.Select(c => c.Text)));
        await PlaceViaAsync(mx, shop, p, buyerToken: staff.Token);
        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(chatStaff).Should().HaveCount(2, "своё же оформление сотрудник не получает");
        mx.Messenger.TextsTo(chatOwner).Should().HaveCount(3);
    }

    [Fact, TestCase("CY25-06")]
    public async Task OneChatLinkedToTwoAccounts_ExactlyOneMessagePerEvent_SecondPassAddsNothing()
    {
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        var shared = NewChat("shared");
        await LinkAsync(mx, shop.OwnerToken, shared, sender: "same-max-user");
        await LinkAsync(mx, staff.Token, shared, sender: "same-max-user");
        (await WithDbAsync(db => db.StaffMaxLinks.CountAsync(l => l.ChatKey == KeyOf(shared)))).Should().Be(2, "две привязки одного чата");

        var placed = await PlaceViaAsync(mx, shop, p);
        (await RowsAsync(shop)).Should().ContainSingle("одно событие — одна строка на ЧАТ, а не на аккаунт");

        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(shared).Should().ContainSingle("одно событие — не больше одного сообщения в один чат");

        // повторный проход и повторное событие «отмена» — без дублей
        await mx.RunDispatchPassAsync();
        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(shared).Should().ContainSingle("повтор прохода не шлёт дубль");
        (await RowsAsync(shop)).Single().Status.Should().Be(NotificationStatus.Sent);

        await CancelViaAsync(mx, placed.Order.Token);
        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(shared).Should().HaveCount(2);
        (await RowsAsync(shop)).Should().HaveCount(2);
    }

    [Fact, TestCase("CY25-07")]
    public async Task RemovedStaffMember_QueuedRowIsSkipped_OwnerStillGetsIt_NoNewRowsForTheRemoved()
    {
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        var chatOwner = NewChat("own");
        var chatStaff = NewChat("stf");
        await LinkAsync(mx, shop.OwnerToken, chatOwner);
        await LinkAsync(mx, staff.Token, chatStaff);

        await PlaceViaAsync(mx, shop, p);
        (await RowsAsync(shop)).Should().HaveCount(2);
        await RemoveStaffAsync(shop, staff); // после постановки в очередь, до отправки

        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(chatStaff).Should().BeEmpty("удалённый из магазина не получает сообщения этого магазина сразу, включая стоящие в очереди");
        mx.Messenger.TextsTo(chatOwner).Should().ContainSingle();
        var rows = await RowsAsync(shop);
        var removedRow = rows.Single(r => r.ChatKey == KeyOf(chatStaff));
        removedRow.Status.Should().Be(NotificationStatus.Skipped);
        removedRow.Reason.Should().Be(NotificationReason.StaffMaxNoRecipient);

        await PlaceViaAsync(mx, shop, p);
        (await RowsAsync(shop)).Count(r => r.ChatKey == KeyOf(chatStaff)).Should().Be(1, "на новый заказ строка для удалённого не ставится");
    }

    [Fact, TestCase("CY25-08")]
    public async Task ShopFlagOff_HoldsQueuedRows_AndStopsNewOnes_FlagBackOn_ResumesForNewOrders()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        var chat = NewChat("own");
        await LinkAsync(mx, shop.OwnerToken, chat);
        var url = $"/api/shops/{shop.Id}/notification-settings";
        object Body(bool on) => new { staffPushEnabled = true, customerWebPushEnabled = true, customerMessengerEnabled = false, staffMaxEnabled = on };

        await PlaceViaAsync(mx, shop, p);
        var queued = await RowsAsync(shop);
        queued.Should().ContainSingle().Which.Status.Should().Be(NotificationStatus.Pending);

        (await ClientOn(mx, shop.OwnerToken).PutJsonAsync(url, Body(false))).StatusCode.Should().Be(HttpStatusCode.OK);
        await mx.RunDispatchPassAsync();
        mx.Messenger.Calls.Should().BeEmpty("выключение останавливает и уже стоящие в очереди сообщения магазина");
        var held = (await RowsAsync(shop)).Single();
        held.Status.Should().Be(NotificationStatus.Skipped);
        held.Reason.Should().Be(NotificationReason.StaffMaxDisabledByShop);

        await PlaceViaAsync(mx, shop, p);
        (await RowsAsync(shop)).Should().HaveCount(1, "при выключенном флаге новые строки не ставятся");

        // подключение и push от флага MAX не зависят: привязка осталась
        (await StatusOfAsync(ClientOn(mx, shop.OwnerToken))).Status.Should().Be(StaffMaxStatus.Linked);

        (await ClientOn(mx, shop.OwnerToken).PutJsonAsync(url, Body(true))).StatusCode.Should().Be(HttpStatusCode.OK);
        await PlaceViaAsync(mx, shop, p);
        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(chat).Should().ContainSingle("после включения приходят новые заказы");
    }

    [Fact, TestCase("CY25-09")]
    public async Task Disconnect_StopsQueuedAndNewMessages_Idempotent_StatusNotLinked()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        var chat = NewChat("own");
        await LinkAsync(mx, shop.OwnerToken, chat);
        var client = ClientOn(mx, shop.OwnerToken);

        await PlaceViaAsync(mx, shop, p);
        (await client.DeleteAsync("/api/staff-max/link")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync("/api/staff-max/link")).StatusCode.Should().Be(HttpStatusCode.NoContent, "повторное отключение — тоже 204");

        await mx.RunDispatchPassAsync();
        mx.Messenger.Calls.Should().BeEmpty("после «Отключить» стоящие в очереди не уходят");
        (await RowsAsync(shop)).Single().Reason.Should().Be(NotificationReason.StaffMaxNoRecipient);
        var st = await StatusOfAsync(client);
        st.Status.Should().Be(StaffMaxStatus.NotLinked);
        st.StatusText.Should().Be("Не подключено");

        await PlaceViaAsync(mx, shop, p);
        (await RowsAsync(shop)).Should().HaveCount(1, "нового сообщения без привязки не ставят");
    }

    [Fact, TestCase("CY25-10")]
    public async Task BotStopped_UnlinksAllBindingsOfTheChat_QueuedRowsNotSent_NoReplyToTheChat()
    {
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        var chatOwner = NewChat("own");
        var chatShared = NewChat("shr");
        await LinkAsync(mx, shop.OwnerToken, chatOwner);
        await LinkAsync(mx, staff.Token, chatShared, sender: "shared-user");
        await PlaceViaAsync(mx, shop, p);

        var repliesBefore = mx.BotClient.SentMessages.Count;
        (await Webhook(ClientOn(mx), Stopped(chatShared))).StatusCode.Should().Be(HttpStatusCode.OK);
        mx.BotClient.SentMessages.Count.Should().Be(repliesBefore, "на остановку бота ответа в чат нет");

        var st = await StatusOfAsync(ClientOn(mx, staff.Token));
        st.Status.Should().Be(StaffMaxStatus.StoppedInMax);
        st.StatusText.Should().Be("Отключено: бот остановлен в MAX");
        st.StoppedAtUtc.Should().NotBeNull();
        (await StatusOfAsync(ClientOn(mx, shop.OwnerToken))).Status.Should().Be(StaffMaxStatus.Linked, "чужие привязки других чатов не затронуты");
        var link = await WithDbAsync(db => db.StaffMaxLinks.AsNoTracking().SingleAsync(l => l.UserId == staff.UserId));
        link.ChatIdCiphertext.Should().BeNull("идентификатор чата забыт");

        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(chatShared).Should().BeEmpty();
        mx.Messenger.TextsTo(chatOwner).Should().ContainSingle();

        // повторное подключение того же человека снова делает статус «Подключено»
        await LinkAsync(mx, staff.Token, NewChat("again"));
        (await StatusOfAsync(ClientOn(mx, staff.Token))).Status.Should().Be(StaffMaxStatus.Linked);
    }

    [Fact, TestCase("CY25-11")]
    public async Task MaxRefusesToSend_403_UnlinksTheChat_ButOrderAndItsActionsAreUntouched()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        var chat = NewChat("blk");
        await LinkAsync(mx, shop.OwnerToken, chat);
        mx.Messenger.SetOutcomeForChat(chat, new MaxSendOutcome.ChatUnavailable(403));

        await PlaceViaAsync(mx, shop, p);
        await mx.RunDispatchPassAsync();
        var row = (await RowsAsync(shop)).Single();
        row.Status.Should().Be(NotificationStatus.Skipped);
        row.Reason.Should().Be(NotificationReason.StaffMaxChatUnavailable);
        var st = await StatusOfAsync(ClientOn(mx, shop.OwnerToken));
        st.Status.Should().Be(StaffMaxStatus.StoppedInMax, "отказ при отправке — тоже «бот остановлен»");
        st.StatusText.Should().Be("Отключено: бот остановлен в MAX");
    }

    [Fact, TestCase("CY25-12")]
    public async Task MaxFailure_NeverRollsBackTheOrderAction_RowRetriedLater_CustomerSeesNothing()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        var chat = NewChat("flaky");
        await LinkAsync(mx, shop.OwnerToken, chat);
        mx.Messenger.SetDefaultOutcome(new MaxSendOutcome.Transient("HTTP 503 (test)"));

        var placed = await PlaceViaAsync(mx, shop, p);           // заказ оформляется при любом состоянии MAX
        var order = await GetStaffOrderAsync(shop, placed.Order.Token);
        await mx.RunDispatchPassAsync();                          // отправка падает
        var accepted = await ActOkAsync(shop, order, "accept");   // действие над заказом проходит
        accepted.Status.Should().Be(OrderStatus.Accepted);
        (await GetStaffOrderAsync(shop, placed.Order.Token)).Status.Should().Be(OrderStatus.Accepted);

        var row = (await RowsAsync(shop)).Single();
        row.Status.Should().Be(NotificationStatus.Pending, "временный сбой — строка остаётся ждать повтора");
        row.AttemptCount.Should().Be(1);
        row.NextAttemptAtUtc.Should().BeAfter(DateTime.UtcNow, "повтор отложен по шагу 1 / 5 / 15 минут");

        // немедленный второй проход строку до срока повтора не трогает — дубля и штурма нет
        var calls = mx.Messenger.Calls.Count;
        await mx.RunDispatchPassAsync();
        mx.Messenger.Calls.Count.Should().Be(calls);

        // покупатель о MAX персонала не узнаёт ничего
        var pub = await AnonymousClient().GetAsync($"/api/orders/public/{placed.Order.Token}");
        (await pub.Content.ReadAsStringAsync()).Should().NotContainEquivalentOf("max").And.NotContain(chat);

        // жёсткий отказ (400/401 — настройка бота) — строка Failed без повторов, заказ по-прежнему в порядке
        var chat2 = NewChat("rej");
        mx.Messenger.SetDefaultOutcome(new MaxSendOutcome.Rejected(401, "unauthorized (test)"));
        var staff = await AddShopStaffAsync(shop);
        await LinkAsync(mx, staff.Token, chat2);
        var second = await PlaceViaAsync(mx, shop, p);
        (await GetStaffOrderAsync(shop, second.Order.Token)).Status.Should().Be(OrderStatus.New);
        await mx.RunDispatchPassAsync();
        var failed = (await RowsAsync(shop)).Where(r => r.ChatKey == KeyOf(chat2)).ToList();
        failed.Should().ContainSingle().Which.Status.Should().Be(NotificationStatus.Failed);
    }

    [Fact, TestCase("CY25-13")]
    public async Task PlatformSwitchedOffAfterQueueing_HoldsRows_And_StartOnDisabledPlatformExplains()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        var chat = NewChat("own");
        StaffMaxLinkSessionDto stale;
        await using (var enabledHost = await MxAsync())
        {
            await LinkAsync(enabledHost, shop.OwnerToken, chat);
            stale = await NewSessionAsync(ClientOn(enabledHost, shop.OwnerToken));
            await PlaceViaAsync(enabledHost, shop, p);
        }

        await using var off = await MxAsync(platformEnabled: false, purgeQueue: false);
        await off.RunDispatchPassAsync();
        off.Messenger.Calls.Should().BeEmpty("рубильник платформы проверяется при отправке");
        (await RowsAsync(shop)).Single().Reason.Should().Be(NotificationReason.StaffMaxPlatformDisabled);

        var chat2 = NewChat("dis");
        await Webhook(ClientOn(off), Start(PayloadOf(stale.DeepLink), "s-dis", chat2));
        off.BotClient.SentMessages.Single(m => m.ChatId == chat2).Text.Should().Be("Сообщения о заказах в MAX пока не включены.");

        var st = await StatusOfAsync(ClientOn(off, shop.OwnerToken));
        st.Available.Should().BeFalse();
        st.UnavailableText.Should().Be("Сообщения в MAX пока не включены на платформе");
    }

    [Fact, TestCase("CY25-14")]
    public async Task StaffRemovedFromAllShops_LosesTheBlock_StartOfAnOldLinkExplainsAndLinksNothing()
    {
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        await using var mx = await MxAsync();
        var client = ClientOn(mx, staff.Token);
        (await StatusOfAsync(client)).Eligible.Should().BeTrue("сотрудник видит блок");
        var session = await NewSessionAsync(client);
        await RemoveStaffAsync(shop, staff);

        (await StatusOfAsync(client)).Eligible.Should().BeFalse("блок видят только участники хотя бы одного магазина");
        var again = await client.PostAsync("/api/staff-max/link-sessions", null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var chat = NewChat("gone");
        await Webhook(client, Start(PayloadOf(session.DeepLink), "s-gone", chat));
        mx.BotClient.SentMessages.Single(m => m.ChatId == chat).Text.Should().Be("Вы больше не состоите ни в одном магазине — подключать нечего.");
        (await WithDbAsync(db => db.StaffMaxLinks.CountAsync(l => l.UserId == staff.UserId))).Should().Be(0);
    }

    [Fact, TestCase("CY25-15")]
    public async Task ChatLinkedByForgedSender_SameChatDedupe_HoldsAcrossAccountsOfDifferentSenders()
    {
        // Подделка отправителя (§540.6): два разных аккаунта нажимают «Начать» в ОДНОМ чате от «разных» отправителей — дедупликация идёт по чату.
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        var chat = NewChat("forge");
        await LinkAsync(mx, shop.OwnerToken, chat, sender: "sender-A");
        await LinkAsync(mx, staff.Token, chat, sender: "sender-B");

        await PlaceViaAsync(mx, shop, p);
        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(chat).Should().ContainSingle();

        // отключился один из двух — второй по-прежнему получает, дубля нет
        (await ClientOn(mx, staff.Token).DeleteAsync("/api/staff-max/link")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await PlaceViaAsync(mx, shop, p);
        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(chat).Should().HaveCount(2);
    }

    // ── US-25-04 (P1): предупреждения о лимите владельцу в MAX ─────────────────────

    [Fact, TestCase("CY25-16")]
    public async Task OwnerGetsLimitWarningsInMax_At80And100Percent_OncePerLevel_StaffDoesNot()
    {
        var shop = await CreateRoundClockShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var plan = await J(await admin.PostJsonAsync("/api/admin/plans", new
        {
            name = Unique("Лимит MAX · "), pricePerMonth = 1m, maxEmployees = 5, maxCompanies = 3, isPublic = true, isActive = true, line = "Orders",
            maxProductsPerShop = 100, maxOrdersPerMonth = 5, allowOrders = true,
        }));
        var accountId = await WithDbAsync(db => db.BillingAccounts.Where(a => a.OwnerUserId == shop.Owner.UserId).Select(a => a.Id).FirstAsync());
        (await admin.PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", new
        {
            planId = plan.GetProperty("id").GetGuid(), isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            options = Array.Empty<object>(), line = "Orders",
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        var chatOwner = NewChat("own");
        var chatStaff = NewChat("stf");
        await LinkAsync(mx, shop.OwnerToken, chatOwner);
        await LinkAsync(mx, staff.Token, chatStaff);
        // сообщения о самих заказах не мешают: считаем только предупреждения
        Task<List<StaffMaxMessage>> Warnings() =>
            WithDbAsync(db => db.StaffMaxMessages.AsNoTracking().Where(m => m.CompanyId == shop.Id && m.Type == NotificationType.OwnerOrderLimitWarning).ToListAsync());

        for (var i = 0; i < 3; i++) await PlaceViaAsync(mx, shop, p);
        (await Warnings()).Should().BeEmpty("до 80 % предупреждения нет");
        await PlaceViaAsync(mx, shop, p);
        (await Warnings()).Should().ContainSingle().Which.Text.Should().Contain("Использовано 80 % лимита заказов").And.Contain("4 из 5");
        await PlaceViaAsync(mx, shop, p);
        (await Warnings()).Should().HaveCount(2);
        (await ClientOn(mx).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Warnings()).Should().HaveCount(2, "один раз на уровень");
        (await Warnings()).Select(w => w.ChatKey).Distinct().Should().BeEquivalentTo(new[] { KeyOf(chatOwner) }, "предупреждение — только владельцу аккаунта");

        await mx.RunDispatchPassAsync();
        var ownerTexts = mx.Messenger.TextsTo(chatOwner);
        ownerTexts.Should().Contain(t => t.StartsWith("Использовано 80 % лимита заказов") && t.Contains("Подписка: https://") && t.Contains("/cabinet/subscription"));
        ownerTexts.Should().Contain(t => t.StartsWith("Лимит заказов исчерпан: 5 из 5"));
        mx.Messenger.TextsTo(chatStaff).Should().NotContain(t => t.Contains("лимит", StringComparison.OrdinalIgnoreCase));
    }

    // ── риск R25-5: регресс подтверждения телефона (цикл 14) при включённых сообщениях персоналу ──

    private static string VCard(string phone) => $"BEGIN:VCARD\nVERSION:3.0\nFN:Test Contact\nTEL;TYPE=CELL:{phone}\nEND:VCARD";

    private static string SignHex(string vcf) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(StaffMaxTestFactory.BotToken), Encoding.UTF8.GetBytes(vcf)));

    private static object ContactBody(string sender, string chat, string vcf, string hash) => new
    {
        update_type = "message_created",
        message = new
        {
            sender = new { user_id = sender },
            recipient = new { chat_id = chat },
            body = new { attachments = new object[] { new { type = "contact", payload = new { vcf_info = vcf, hash, max_info = new { user_id = sender } } } } },
        },
    };

    [Fact, TestCase("CY25-17")]
    public async Task PhoneVerificationStillWorks_WithStaffMaxEnabled_PrefixesNeverCrossOver()
    {
        var shop = await CreateRoundClockShopAsync();
        await using var mx = await MxAsync();
        var client = ClientOn(mx, shop.OwnerToken);

        // (1) «sm1.» никогда не касается сессий подтверждения телефона
        var phoneSessionsBefore = await WithDbAsync(db => db.PhoneVerificationSessions.CountAsync());
        var staffSession = await NewSessionAsync(client);
        await Webhook(client, Start(PayloadOf(staffSession.DeepLink), "sm-user", NewChat("sm")));
        (await WithDbAsync(db => db.PhoneVerificationSessions.CountAsync())).Should().Be(phoneSessionsBefore);
        (await WithDbAsync(db => db.StaffMaxLinkSessions.SingleAsync(s => s.Id == staffSession.SessionId))).CompletedAtUtc.Should().NotBeNull();

        // (2) «v1.» никогда не касается привязок и сессий персонала — и подтверждение проходит целиком
        var phone = UniquePhone();
        var started = await ClientOn(mx).PostAsJsonAsync("/api/phone-verification/sessions", new StartPhoneVerificationRequestDto(phone));
        started.StatusCode.Should().Be(HttpStatusCode.Created, await started.Content.ReadAsStringAsync());
        var created = (await started.Content.ReadJsonAsync<PhoneVerificationSessionCreatedDto>())!;
        var payload = PayloadOf(created.DeepLink);
        payload.Should().NotStartWith("sm1.");

        var linksBefore = await WithDbAsync(db => db.StaffMaxLinks.CountAsync());
        var sessionsBefore = await WithDbAsync(db => db.StaffMaxLinkSessions.CountAsync());
        var senderId = "pv-" + Guid.NewGuid().ToString("N")[..8];
        var pvChat = NewChat("pv");
        await Webhook(ClientOn(mx), Start(payload, senderId, pvChat));
        (await WithDbAsync(db => db.StaffMaxLinks.CountAsync())).Should().Be(linksBefore);
        (await WithDbAsync(db => db.StaffMaxLinkSessions.CountAsync())).Should().Be(sessionsBefore);
        mx.BotClient.SentMessages.Should().Contain(m => m.ChatId == pvChat && m.Text == MaxBotTexts.Greeting && m.RequestContact,
            "бот подтверждения приветствует с кнопкой контакта, как в цикле 14");

        var vcf = VCard(phone);
        await Webhook(ClientOn(mx), ContactBody(senderId, pvChat, vcf, SignHex(vcf)));
        var status = await ClientOn(mx).GetAsync($"/api/phone-verification/sessions/{created.SessionId}?statusToken={created.StatusToken}");
        (await status.Content.ReadJsonAsync<PhoneVerificationSessionStatusDto>())!.Status.Should().Be(PhoneVerificationDisplayStatus.Verified);

        // (3) bot_stopped не ломает сессию подтверждения и отвечает 200
        var second = await ClientOn(mx).PostAsJsonAsync("/api/phone-verification/sessions", new StartPhoneVerificationRequestDto(UniquePhone()));
        var secondDto = (await second.Content.ReadJsonAsync<PhoneVerificationSessionCreatedDto>())!;
        var chat2 = NewChat("pv2");
        await Webhook(ClientOn(mx), Start(PayloadOf(secondDto.DeepLink), "pv-other", chat2));
        (await Webhook(ClientOn(mx), Stopped(chat2))).StatusCode.Should().Be(HttpStatusCode.OK);
        var st2 = await ClientOn(mx).GetAsync($"/api/phone-verification/sessions/{secondDto.SessionId}?statusToken={secondDto.StatusToken}");
        (await st2.Content.ReadJsonAsync<PhoneVerificationSessionStatusDto>())!.Status.Should().Be(PhoneVerificationDisplayStatus.Linked);

        // неверный токен вебхука по-прежнему 404, тело не разбирается
        (await ClientOn(mx).PostAsJsonAsync("/api/phone-verification/max/webhook/wrong", Start("sm1.x", "s", "c"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // и наоборот: неизвестная нагрузка без префикса — ответ цикла 14, привязки не создаёт
        var chat3 = NewChat("unk");
        await Webhook(ClientOn(mx), Start("totally-unknown-payload", "s-unknown", chat3));
        mx.BotClient.SentMessages.Single(m => m.ChatId == chat3).Text.Should().Be(MaxBotTexts.PayloadExpiredOrUnknown);
        (await WithDbAsync(db => db.StaffMaxLinks.CountAsync())).Should().Be(linksBefore);
    }

    [Fact, TestCase("CY25-18")]
    public async Task StaffLinkQueueRows_HaveNoCustomerPersonalData_AndRetentionPolicyExposesTheNewPeriods()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        await using var mx = await MxAsync();
        await LinkAsync(mx, shop.OwnerToken, NewChat("own"));
        var phone = UniquePhone();
        await PlaceViaAsync(mx, shop, p, name: "Тайное Имя", phone: phone);
        (await RowsAsync(shop)).Single().Text.Should().NotContain("Тайное Имя").And.NotContain(phone.TrimStart('+'));

        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var policy = await J(await admin.GetAsync("/api/admin/retention/policy"));
        policy.GetProperty("staffMaxStoppedLinkDays").GetInt32().Should().Be(30);
        policy.GetProperty("staffMaxLinkSessionDays").GetInt32().Should().Be(1);
        policy.GetProperty("staffMaxMessageDays").GetInt32().Should().Be(90);
        // Контракт §535 называет правила staff-max-links/-messages/shop-customer-notes; фактически политика отдаёт три поля *Days (у заметок собственного срока нет).
    }

    // salon owner ↔ MAX персонала: салонный владелец не «персонал магазина»
    [Fact, TestCase("CY25-19")]
    public async Task SalonOwnerIsNotEligibleForShopMaxBlock()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await using var mx = await MxAsync();
        var client = ClientOn(mx, owner.Token);
        (await StatusOfAsync(client)).Eligible.Should().BeFalse("блок «Заказы в MAX» — только для магазинов goods");
        (await client.PostAsync("/api/staff-max/link-sessions", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
