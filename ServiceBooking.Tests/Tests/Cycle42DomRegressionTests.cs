using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: регресс «Домов» (CY42-100…105). Писано по API_CONTRACT_CYCLE42.md §42.21 («изменения существующих маршрутов»), §42.36.1 («строки „Домов“ не меняются
/// байт-в-байт») и тексту цикла 39 (<c>develop:ServiceBooking.API/Services/Stays/Services/ServiceTexts.cs</c>, <c>ServiceNotificationTexts.cs</c>): ожидаемые строки ниже —
/// литералы, а не вызов кода реализации. Ответы «Домов» не получают новых полей <c>capacity</c>, <c>cityName</c>, <c>localTimeNote</c>, <c>guestsCount</c>, <c>sessionReminder</c>,
/// <c>bookAgainUrl</c>; публикация услуги «Домов» не требует вместимости и тарифа «Бань».
/// </summary>
public class Cycle42DomRegressionTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private static readonly string[] NewFields = ["capacity", "cityName", "localTimeNote", "guestsCount", "sessionReminder", "bookAgainUrl"];
    private const string GuestTime = @"\p{L}{2} \d{1,2} \p{L}+, 12:00 — 14:00";
    private const string StaffTime = @"\p{L}{2}, \d{1,2} \p{L}+ · 12:00 – 14:00";

    private async Task<(StaysCtx Company, SvcCtx Svc, string Phone)> SceneAsync(int? prepay = 30)
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        await WithDbAsync(async db =>
        {
            var c = await db.Companies.SingleAsync(x => x.Id == company.Id);
            c.Address = "ул. Тайная, 77, Шерегеш";
            await db.SaveChangesAsync();
        });
        var svc = await CreateServiceAsync(company, prepay: prepay);
        var phone = await WithDbAsync(db => db.Companies.AsNoTracking().Where(c => c.Id == company.Id).Select(c => c.Phone).SingleAsync());
        return (company, svc, phone!);
    }

    private static void HasNone(JsonElement e, string where)
    {
        foreach (var name in NewFields)
            e.TryGetProperty(name, out _).Should().BeFalse($"{where}: поле «{name}» у «Домов» не появляется");
    }

    private static void AbsentOrNull(JsonElement e, string name, string where)
    {
        (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null).Should().BeTrue($"{where}: «{name}» у «Домов» пусто");
    }

    // ── CY42-100: формы ответов «Домов» без новых полей ─────────────────────────

    [Fact, TestCase("CY42-100")]
    public async Task DomResponses_DoNotGetBathsFields_PublicPage_OrderPage_StaffCard_Export()
    {
        var (company, svc, _) = await SceneAsync();
        var guest = await RegisterAsync();
        var guestClient = AuthedClient(guest.Token);

        var page = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/services/{svc.Slug}"));
        HasNone(page, "публичная страница услуги");
        page.GetProperty("standalone").GetProperty("ordering").GetBoolean().Should().BeTrue();

        // число гостей во входе игнорируется: у услуг «Домов» вместимости нет
        var quote = await QuoteServiceAsync(svc.Id, InDays(10), 720, 2);
        var body = new
        {
            businessDate = InDays(10), startMinute = 720, hours = 2, items = Array.Empty<object>(), guestName = "Ольга Гостева", guestPhone = guest.Phone, comment = (string?)null,
            notifyByMessenger = false, expectedTotalRub = quote.TotalRub, idempotencyKey = Guid.NewGuid(), captchaToken = (string?)null, guestsCount = 7
        };
        var created = await guestClient.PostJsonAsync($"/api/stays/public/services/{svc.Id}/orders", body);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var token = (await J(created)).GetProperty("token").GetString()!;
        (await J(created)).GetProperty("orderUrl").GetString().Should().StartWith("https://dom.ezbook.ru/s/");

        var order = await J(await AnonymousClient().GetAsync($"/api/stays/service-orders/public/{token}"));
        HasNone(order, "страница заказа");
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().Where(o => o.PublicToken == token).Select(o => o.GuestsCount).SingleAsync())).Should().BeNull("число гостей у «Домов» не сохраняется");

        var sessionId = await SessionIdOfOrderAsync(token);
        var card = await J(await AuthedClient(company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/service-sessions/{sessionId}"));
        HasNone(card, "карточка сеанса");

        var manage = await J(await AuthedClient(company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}"));
        AbsentOrNull(manage, "capacity", "карточка услуги в кабинете");
        (!manage.TryGetProperty("contentWarnings", out var cw) || cw.ValueKind == JsonValueKind.Null || (cw.ValueKind == JsonValueKind.Array && cw.GetArrayLength() == 0)).Should().BeTrue(
            "у чистого описания предупреждений нет");

        var export = await J(await guestClient.GetAsync("/api/profile/export"));
        var exported = export.GetProperty("stayServiceOrders").EnumerateArray().Single();
        AbsentOrNull(exported, "guestsCount", "выгрузка");
        exported.GetProperty("site").GetString().Should().Be("Stays");
        exported.GetProperty("orderUrl").GetString().Should().StartWith("https://dom.ezbook.ru/s/");
    }

    // ── CY42-101: тексты гостю на странице заказа «Домов» — байт-в-байт ──────────

    [Fact, TestCase("CY42-101")]
    public async Task DomGuestTexts_AreByteForByteTheOnesOfCycle39_InEveryStatus()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (company, svc, phone) = await SceneAsync();
        var guest = host.Client();
        async Task<JsonElement> Page(string token) => await J(await guest.GetAsync($"/api/stays/service-orders/public/{token}"));
        async Task<HttpResponseMessage> Staff(Guid sessionId, string action, string? reason = null)
        {
            var version = (await SessionCardAsync(company, sessionId)).Version;
            return await host.Client(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions/{sessionId}/{action}",
                action == "confirm-payment" ? (object)new ExpectedVersionInput(version) : new ExpectedVersionReasonInput(version, reason));
        }
        async Task<(string Token, Guid SessionId)> Order(int start)
        {
            var o = await OrderOkAsync(svc.Id, InDays(10), start, 2, client: guest);
            return (o.Token, await SessionIdOfOrderAsync(o.Token));
        }

        // ожидает оплаты → проверка → подтверждён
        var a = await Order(600);
        var p = await Page(a.Token);
        p.GetProperty("statusText").GetString().Should().Be("Ожидает оплаты");
        p.GetProperty("cancellation").GetProperty("summary").GetString().Should().Be("Без удержаний: отмена до начала — вся предоплата возвращается");
        (await AttachOrderProofAsync(a.Token, guest)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Page(a.Token)).GetProperty("statusText").GetString().Should().Be("Ожидает проверки оплаты");
        (await Staff(a.SessionId, "confirm-payment")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Page(a.Token)).GetProperty("statusText").GetString().Should().Be("Подтверждён");

        // отмена гостем: итоговый текст и отказы повторной отмены / подтверждения оплаты
        (await CancelOrderAsync(a.Token, guest)).StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await Page(a.Token);
        cancelled.GetProperty("statusText").GetString().Should().Be("Отменён гостем");
        cancelled.GetProperty("outcomeText").GetString().Should().Be("Вы отменили заказ.");
        var again = await CancelOrderAsync(a.Token, guest);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(again)).GetProperty("message").GetString().Should().BeOneOf("Заказ уже завершён — отменять нечего", "Заказ уже отменён");
        var lateProof = await AttachOrderProofAsync(a.Token, guest);
        lateProof.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await lateProof.Content.ReadAsStringAsync()).Should().Contain("Заказ уже отменён гостем — подтверждение оплаты не нужно");

        // оплата отклонена
        var r = await Order(900);
        await AttachOrderProofAsync(r.Token, guest);
        (await Staff(r.SessionId, "reject-payment", "Оплата не поступила")).StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await Page(r.Token);
        rejected.GetProperty("statusText").GetString().Should().Be("Оплата не подтверждена");
        rejected.GetProperty("outcomeText").GetString().Should().Be(
            $"Компания не подтвердила оплату. Причина: Оплата не поступила. Если вы платили, компания обязана вернуть деньги или восстановить заказ, если время свободно. Контакт компании: {phone}.");

        // отменена компанией: без оплаты и после оплаты
        var u = await Order(1140);
        (await Staff(u.SessionId, "cancel", "Авария")).StatusCode.Should().Be(HttpStatusCode.OK);
        var unpaid = await Page(u.Token);
        unpaid.GetProperty("statusText").GetString().Should().Be("Отменён компанией");
        unpaid.GetProperty("outcomeText").GetString().Should().Be($"Компания отменила заказ. Причина: Авария. Оплата за сеанс не вносилась. Контакт компании: {phone}.");
        var paid = await Order(1260);
        await AttachOrderProofAsync(paid.Token, guest);
        (await Staff(paid.SessionId, "confirm-payment")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Staff(paid.SessionId, "cancel", "Авария")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Page(paid.Token)).GetProperty("outcomeText").GetString().Should().Be(
            $"Компания отменила заказ. Причина: Авария. Внесённая предоплата должна быть возвращена полностью. Вы также вправе требовать возмещения убытков. Контакт компании: {phone}.");

        // снят по таймеру
        var e = await Order(480);
        var hold = (await Page(e.Token)).GetProperty("holdExpiresAtUtc").GetDateTime();
        host.StaysClock.Set(hold.AddMinutes(1));
        await host.RunTaskAsync("stays-hold-expiry");
        var expired = await Page(e.Token);
        expired.GetProperty("statusText").GetString().Should().Be("Снят: не оплачен");
        expired.GetProperty("outcomeText").GetString().Should().Be(
            $"Время на оплату истекло, заказ снят. Если вы успели оплатить — свяжитесь с компанией. Контакт компании: {phone}.");
    }

    // ── CY42-102: сообщения гостю в мессенджер у «Домов» — байт-в-байт ───────────

    private async Task SeedFundedChannelAsync(StaysCtx c)
    {
        await WithDbAsync(async db =>
        {
            var accountId = await db.Companies.Where(x => x.Id == c.Id).Select(x => x.BillingAccountId!.Value).SingleAsync();
            var channel = new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = c.Owner.UserId, BillingAccountId = accountId, State = ChannelState.Connected, PhoneNumber = UniquePhone().TrimStart('+'),
                ProviderInstanceId = Unique("instance"), ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1)
            };
            db.NotificationChannels.Add(channel);
            db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
            {
                Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = c.Id, BillingAccountId = accountId, AssignedByUserId = c.Owner.UserId
            });
            await db.SaveChangesAsync();
            await NotificationTestBase.EnsureWhatsAppPaidAsync(db, accountId);
        });
    }

    private static Regex Template(string pattern) => new("^" + pattern + @"(\n\nОтписаться от сообщений: \S+)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    [Fact, TestCase("CY42-102")]
    public async Task DomGuestMessenger_TextsAreByteForByteTheOnesOfCycle39()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (company, svc, phone) = await SceneAsync();
        await SeedFundedChannelAsync(company);
        var settings = await host.Client(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/notification-settings",
            new StaysNotificationSettingsInput(true, false, true, true, null, null));
        settings.StatusCode.Should().Be(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        var companyName = company.Company.Name;
        var guestPhone = UniquePhone();

        var quote = await QuoteServiceAsync(svc.Id, InDays(10), 720, 2, client: host.Client());
        var created = await host.Client().PostJsonAsync($"/api/stays/public/services/{svc.Id}/orders",
            OrderInput(InDays(10), 720, 2, quote.TotalRub, guestPhone, "Анна", messenger: true));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var token = (await J(created)).GetProperty("token").GetString()!;
        var orderId = await OrderIdAsync(token);
        var sessionId = await SessionIdOfOrderAsync(token);

        List<OutboundNotification> Rows() => WithDbAsync(db => db.OutboundNotifications.AsNoTracking().Where(n => n.StayServiceOrderId == orderId).ToListAsync()).GetAwaiter().GetResult();
        var createdRow = Rows().Single(n => n.Type == NotificationType.ServiceGuestOrderCreated);
        createdRow.Status.Should().Be(NotificationStatus.Pending, createdRow.Reason?.ToString());
        Template(Regex.Escape($"{companyName}: заказ «Баня», ") + GuestTime + Regex.Escape(" создан. Чтобы он сохранился, внесите предоплату ") + @"[^\n]+? до \d\d:\d\d \d\d\.\d\d" +
                 Regex.Escape($" по реквизитам: {PaymentDetailsText}. Назначение платежа: {PaymentPurposeText}. Затем приложите подтверждение оплаты на странице заказа: https://dom.ezbook.ru/s/{token}"))
            .IsMatch(createdRow.Body).Should().BeTrue("создан с предоплатой: " + createdRow.Body);

        (await AttachOrderProofAsync(token, host.Client())).StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await SessionCardAsync(company, sessionId);
        (await host.Client(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions/{sessionId}/confirm-payment", new ExpectedVersionInput(card.Version))).StatusCode
            .Should().Be(HttpStatusCode.OK);
        var confirmed = Rows().Single(n => n.Type == NotificationType.ServiceGuestConfirmed);
        Template(Regex.Escape($"{companyName}: оплата подтверждена. «Баня», ") + GuestTime + Regex.Escape($". Адрес: ул. Тайная, 77, Шерегеш. Заказ: https://dom.ezbook.ru/s/{token}"))
            .IsMatch(confirmed.Body).Should().BeTrue("оплата подтверждена: " + confirmed.Body);

        var card2 = await SessionCardAsync(company, sessionId);
        (await host.Client(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions/{sessionId}/cancel", new ExpectedVersionReasonInput(card2.Version, "Авария"))).StatusCode
            .Should().Be(HttpStatusCode.OK);
        var cancelled = Rows().Single(n => n.Type == NotificationType.ServiceGuestCancelledByOwner);
        Template(Regex.Escape($"{companyName}: заказ «Баня», ") + GuestTime +
                 Regex.Escape($" отменён компанией. Причина: Авария. Предоплата возвращается полностью; вы вправе требовать возмещения убытков. Телефон компании: {phone}. https://dom.ezbook.ru/s/{token}"))
            .IsMatch(cancelled.Body).Should().BeTrue("отменён компанией, после оплаты: " + cancelled.Body);
    }

    // ── CY42-103: push «Домов» — заголовок и тексты прежние ─────────────────────

    [Fact, TestCase("CY42-103")]
    public async Task DomPush_StaffAndGuest_TitlesTextsAndLinksAreTheOnesOfCycle39()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (company, svc, _) = await SceneAsync();
        var sub = await host.Client(company.OwnerToken).PostAsJsonAsync("/api/push/subscriptions",
            new { endpoint = $"https://push.example.test/cy42-dom/{Guid.NewGuid():N}", keys = new { p256dh = "p256dh-key", auth = "auth-key" }, deviceLabel = "Chrome", site = "Stays" });
        sub.IsSuccessStatusCode.Should().BeTrue(await sub.Content.ReadAsStringAsync());

        var o = await OrderOkAsync(svc.Id, InDays(10), 720, 2, client: host.Client(), phone: "+79054441122", name: "Секретная Гостья");
        var orderId = await OrderIdAsync(o.Token);
        var sessionId = await SessionIdOfOrderAsync(o.Token);
        (await host.Client().PostJsonAsync($"/api/stays/service-orders/public/{o.Token}/push-subscription",
            new PushSubscriptionInput($"https://push.example.test/cy42-dom-guest/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var staff = await WithDbAsync(db => db.StaffPushNotifications.AsNoTracking().Where(n => n.StayServiceOrderId == orderId).ToListAsync());
        var created = staff.Single(n => n.Type == NotificationType.StaffServiceOrderCreated);
        var payload = JsonDocument.Parse(created.Payload).RootElement;
        payload.GetProperty("title").GetString().Should().Be(company.Company.Name);
        Regex.IsMatch(payload.GetProperty("body").GetString()!, @"^Новый заказ услуги · Баня, " + StaffTime + "$").Should().BeTrue(payload.GetProperty("body").GetString());
        payload.GetProperty("url").GetString().Should().Be($"https://dom.ezbook.ru/cabinet/{company.Id}/service-sessions/{sessionId}");
        payload.GetProperty("tag").GetString().Should().Be($"ss-{sessionId}");

        (await CancelOrderAsync(o.Token, host.Client())).StatusCode.Should().Be(HttpStatusCode.OK);
        var cancel = (await WithDbAsync(db => db.StaffPushNotifications.AsNoTracking().Where(n => n.StayServiceOrderId == orderId && n.Type == NotificationType.StaffServiceSessionCancelledByGuest).ToListAsync())).Single();
        Regex.IsMatch(JsonDocument.Parse(cancel.Payload).RootElement.GetProperty("body").GetString()!, @"^Гость отменил сеанс · Баня, " + StaffTime + "$").Should().BeTrue();

        // гостю: заголовок «ezbook · Дома», «заказа» (не «брони»), ссылка относительная
        var other = await OrderOkAsync(svc.Id, InDays(11), 720, 2, client: host.Client(), phone: "+79054441123");
        (await host.Client().PostJsonAsync($"/api/stays/service-orders/public/{other.Token}/push-subscription",
            new PushSubscriptionInput($"https://push.example.test/cy42-dom-guest/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AttachOrderProofAsync(other.Token, host.Client());
        var sid = await SessionIdOfOrderAsync(other.Token);
        var card = await SessionCardAsync(company, sid);
        (await host.Client(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions/{sid}/confirm-payment", new ExpectedVersionInput(card.Version))).StatusCode
            .Should().Be(HttpStatusCode.OK);
        var guestRow = (await WithDbAsync(db => db.StayGuestPushNotifications.AsNoTracking().Where(n => n.StayServiceOrderId == (db.StayServiceOrders.Where(x => x.PublicToken == other.Token).Select(x => x.Id).First())).ToListAsync()))
            .Single(n => n.Type == NotificationType.ServiceGuestConfirmed);
        var gp = JsonDocument.Parse(guestRow.Payload).RootElement;
        gp.GetProperty("title").GetString().Should().Be("ezbook · Дома");
        gp.GetProperty("body").GetString().Should().Be("Статус вашего заказа изменился");
        gp.GetProperty("url").GetString().Should().Be($"/s/{other.Token}");
    }

    // ── CY42-104: публикация услуги «Домов» не требует вместимости и тарифа «Бань»; тексты гейта прежние ──

    [Fact, TestCase("CY42-104")]
    public async Task DomPublish_NeedsNoCapacityNoBathsPlan_AndGateTextsAreUnchanged()
    {
        // услуга «Домов» без вместимости публикуется; подписки «Бань» у аккаунта нет вовсе
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, publish: false);
        (await WithDbAsync(db => db.BathsSubscriptions.CountAsync(s => s.BillingAccountId == db.Companies.Where(c => c.Id == company.Id).Select(c => c.BillingAccountId!.Value).First()))).Should().Be(0);
        var publish = await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/publish", new EmptyInput());
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());
        var manage = await J(publish);
        manage.GetProperty("isPublished").GetBoolean().Should().BeTrue();
        AbsentOrNull(manage, "capacity", "ответ публикации");

        // вместимость у «Домов» необязательна и в setup: null принимается
        var setup = await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/setup",
            new ServiceSetupInput(svc.Service.Name, svc.Slug, 2, 6, 60, 30, false, 0, null, StayServiceCancellationPolicy.NoDeductions, 12, true));
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());

        // тексты гейта «Домов» прежние
        var noPlan = await CreateStaysCompanyAsync(plan: false);
        (await GetCompanyAsync(noPlan)).Gate.ReasonText.Should().Be("Выберите тариф, чтобы принимать брони: пробный период закончился или тариф не выбран");
        var limited = await CreateStaysCompanyAsync();
        await CreateHouseAsync(limited, price: 2000);
        await CreateHouseAsync(limited, price: 2000);
        await GiveStaysPlanAsync(limited.Id, StaysPlans.OneHouseSeedId);
        var gate = (await GetCompanyAsync(limited)).Gate;
        gate.Accepting.Should().BeFalse();
        gate.ReasonText.Should().Be("Опубликовано 2 домов при лимите 1: гости не могут бронировать. Снимите лишние дома с публикации или смените тариф");
        var nobody = await CreateStaysCompanyAsync(provider: false);
        (await GetCompanyAsync(nobody)).Gate.ReasonText.Should().Be("Заполните сведения об исполнителе — без них гости не могут бронировать");
    }

    // ── CY42-105: слова «Домов» на ответах отказов ──────────────────────────────

    [Fact, TestCase("CY42-105")]
    public async Task DomRefusals_KeepOrderWording_LimitsAndSlotTaken()
    {
        var (company, svc, _) = await SceneAsync();
        var date = InDays(10);
        var first = await OrderOkAsync(svc.Id, date, 720, 2);
        first.Token.Should().NotBeNullOrEmpty();

        // занятое время: слово «заказ» не заменено на «бронь»
        var quote = await QuoteServiceAsync(svc.Id, date, 780, 2);
        var taken = await PostOrderAsync(svc.Id, OrderInput(date, 780, 2, quote.TotalRub > 0 ? quote.TotalRub : 4000, UniquePhone()));
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await taken.Content.ReadAsStringAsync();
        body.Should().Contain("Это время уже занято. Выберите другое");
        body.Should().NotContain("брон", "в ответах «Домов» слово гостя не заменено словом бань");

        // лимит по номеру — на втором хосте с боевыми значениями
        await using var host = new StaysTestFactory(ConnectionString, phoneLimits: true);
        var phone = UniquePhone();
        (await OrderOkAsync(svc.Id, date, 900, 2, phone: phone, client: host.Client())).Token.Should().NotBeNullOrEmpty();
        var q2 = await QuoteServiceAsync(svc.Id, date, 1140, 2, client: host.Client());
        var second = await PostOrderAsync(svc.Id, OrderInput(date, 1140, 2, q2.TotalRub, phone), host.Client());
        second.StatusCode.Should().Be((HttpStatusCode)429);
        (await second.Content.ReadAsStringAsync()).Should().Be("Слишком много неоплаченных заказов. Оплатите или отмените текущий заказ");
        _ = company;
    }
}
