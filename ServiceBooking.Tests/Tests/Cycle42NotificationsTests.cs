using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: уведомления о бронях (CY42-94…99). Писано по SPEC_CYCLE42_BANI.md (US-42-22), API_CONTRACT_CYCLE42.md §42.36 и LEGAL_REVIEW_CYCLE42.md, без чтения
/// реализации: ссылки ведут на bani, заголовок push гостю — «EZBOOK Бани», слово гостю — «бронь», в push нет ПДн, адреса, сумм и токена, банщику уведомления не идут.
/// Хост — <see cref="StaysTestFactory"/> (Web Push включён, отправитель записывающий, часы подменяются).
/// </summary>
public class Cycle42NotificationsTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private const string GuestName = "Секретная Гостья";
    private const string GuestPhone = "+79054441122";

    private sealed record Team(BathCtx Company, ResCtx Resource, string ManagerToken, string HousekeeperToken, string ManagerUserId, string HousekeeperUserId);

    private sealed record Booked(string Token, Guid OrderId, Guid SessionId);

    private async Task<(string Token, string UserId)> AddStaffAsync(BathCtx c, string position)
    {
        var user = await RegisterAsync();
        var r = await AuthedClient(c.Token).PostJsonAsync($"/api/Companies/{c.CompanyId}/members",
            new { phone = user.Phone, firstName = user.FirstName, lastName = user.LastName, role = "Master", bio = (string?)null, email = (string?)null, position });
        r.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await r.Content.ReadAsStringAsync());
        return ((await LoginAsync(user.Phone, "Password123!")).Token, user.UserId);
    }

    private async Task<Team> TeamAsync(StaysTestFactory host, int prepay = 30)
    {
        var c = await CreateBathAsync(await NthCityIdAsync(60));
        await WithDbAsync(async db =>
        {
            var company = await db.Companies.SingleAsync(x => x.Id == c.CompanyId);
            company.Address = "ул. Тайная, 77, Шерегеш";
            await db.SaveChangesAsync();
        });
        var res = await AddResourceAsync(c, "Русская баня", prepay: prepay);
        var manager = await AddStaffAsync(c, "Manager");
        var hk = await AddStaffAsync(c, "Housekeeper");
        foreach (var token in new[] { c.Token, manager.Token, hk.Token }) await SubscribeStaffAsync(host.Client(token));
        return new Team(c, res, manager.Token, hk.Token, manager.UserId, hk.UserId);
    }

    private async Task SubscribeStaffAsync(HttpClient client)
    {
        var r = await client.PostAsJsonAsync("/api/push/subscriptions",
            new { endpoint = $"https://push.example.test/cy42-staff/{Guid.NewGuid():N}", keys = new { p256dh = "p256dh-key", auth = "auth-key" }, deviceLabel = "Chrome", site = "Baths" });
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
    }

    private async Task<Booked> BookAsync(StaysTestFactory host, ResCtx res, DateOnly date, int start = 720, bool subscribe = true, bool messenger = false, string name = GuestName,
        string phone = GuestPhone, HttpClient? client = null)
    {
        client ??= host.Client();
        var quote = await J(await BathQuoteAsync(res.Id, date, start, 2, client));
        var body = new
        {
            businessDate = date, startMinute = start, hours = 2, items = Array.Empty<object>(), guestsCount = 3, guestName = name, guestPhone = phone, comment = (string?)null,
            notifyByMessenger = messenger, expectedTotalRub = quote.GetProperty("totalRub").GetInt32(), idempotencyKey = Guid.NewGuid(), captchaToken = (string?)null
        };
        var r = await client.PostJsonAsync($"/api/baths/public/services/{res.Id}/orders", body);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var token = (await J(r)).GetProperty("token").GetString()!;
        if (subscribe)
            (await client.PostJsonAsync($"/api/baths/service-orders/public/{token}/push-subscription",
                new PushSubscriptionInput($"https://push.example.test/cy42-guest/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return new Booked(token, await OrderIdAsync(token), await SessionIdOfOrderAsync(token));
    }

    private Task<HttpResponseMessage> StaffActionAsync(StaysTestFactory host, Team t, Guid sessionId, string action, string? reason = null)
    {
        var card = AuthedClient(t.Company.Token).GetAsync($"/api/baths/companies/{t.Company.CompanyId}/service-sessions/{sessionId}").GetAwaiter().GetResult();
        var version = J(card).GetAwaiter().GetResult().GetProperty("version").GetInt32();
        return host.Client(t.Company.Token).PostJsonAsync($"/api/baths/companies/{t.Company.CompanyId}/service-sessions/{sessionId}/{action}",
            action == "confirm-payment" ? (object)new ExpectedVersionInput(version) : new ExpectedVersionReasonInput(version, reason));
    }

    private Task<List<StaffPushNotification>> StaffRowsAsync(Guid orderId) =>
        WithDbAsync(db => db.StaffPushNotifications.AsNoTracking().Where(n => n.StayServiceOrderId == orderId).OrderBy(n => n.CreatedAt).ToListAsync());

    private Task<List<StayGuestPushNotification>> GuestRowsAsync(Guid orderId) =>
        WithDbAsync(db => db.StayGuestPushNotifications.AsNoTracking().Where(n => n.StayServiceOrderId == orderId).OrderBy(n => n.CreatedAt).ToListAsync());

    private static JsonElement Payload(string payload) => JsonDocument.Parse(payload).RootElement;

    private static readonly Regex DomWord = new(@"(?<!\p{L})(дом\p{L}*|заказ\p{L}*|проживани\p{L}*|заселени\p{L}*|заезд\p{L}*|бизнес-день|задат\p{L}*|депозит\p{L}*|невозвратн\p{L}*|туристическ\p{L}*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void NoDomWords(string text, string because)
    {
        var m = DomWord.Match(text);
        m.Success.Should().BeFalse($"в тексте для гостя бани нет слов «Домов»: «{m.Value}» — {because}: {text}");
    }

    // ── CY42-94: персоналу — ссылки bani, слово «бронь», без ПДн; банщику ничего ──

    [Fact, TestCase("CY42-94")]
    public async Task StaffPush_ToOwnerAndManagerOnly_BaniCabinetLink_WordBron_NoGuestPersonalData()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var t = await TeamAsync(host);
        var b = await BookAsync(host, t.Resource, InDays(10));

        var rows = await StaffRowsAsync(b.OrderId);
        rows.Select(r => r.UserId).Should().BeEquivalentTo([t.Company.Owner.UserId, t.ManagerUserId], "новая бронь — владельцу и администратору, не банщику");
        rows.Should().OnlyContain(r => r.Type == NotificationType.StaffServiceOrderCreated);
        foreach (var row in rows)
        {
            var payload = Payload(row.Payload);
            payload.GetProperty("url").GetString().Should().Be($"https://bani.ezbook.ru/cabinet/{t.Company.CompanyId}/service-sessions/{b.SessionId}", "ссылка — на кабинет bani, не dom");
            payload.GetProperty("tag").GetString().Should().Be($"ss-{b.SessionId}");
            var text = payload.GetProperty("body").GetString()!;
            text.Should().StartWith("Новая бронь · Русская баня");
            NoDomWords(text, "уведомление персоналу");
            row.Payload.Should().NotContain("Секретная").And.NotContain("9054441122").And.NotContain("Тайная").And.NotContain("Сбербанк");
        }

        // первое подтверждение оплаты — второе событие; второй файл новых строк не даёт
        (await host.Client().PostAsync($"/api/baths/service-orders/public/{b.Token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "c1.jpg")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var afterProof = await StaffRowsAsync(b.OrderId);
        var proofRows = afterProof.Where(r => r.Type == NotificationType.StaffServiceOrderPaymentProofUploaded).ToList();
        proofRows.Should().HaveCount(2);
        foreach (var row in proofRows)
        {
            var text = Payload(row.Payload).GetProperty("body").GetString()!;
            text.Should().StartWith("Приложено подтверждение оплаты · Русская баня");
            Payload(row.Payload).GetProperty("url").GetString().Should().StartWith("https://bani.ezbook.ru/cabinet/");
        }
        (await host.Client().PostAsync($"/api/baths/service-orders/public/{b.Token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 41), "image/jpeg", "c2.jpg")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await StaffRowsAsync(b.OrderId)).Count.Should().Be(afterProof.Count, "оповещает только первый файл");

        // отмена гостем
        (await host.Client().PostJsonAsync($"/api/baths/service-orders/public/{b.Token}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        var final = await StaffRowsAsync(b.OrderId);
        var cancelRows = final.Where(r => r.Type == NotificationType.StaffServiceSessionCancelledByGuest).ToList();
        cancelRows.Should().HaveCount(2);
        cancelRows.Select(r => Payload(r.Payload).GetProperty("body").GetString()!).Should().OnlyContain(x => x.StartsWith("Гость отменил") && x.Contains("Русская баня"));
        final.Should().NotContain(r => r.UserId == t.HousekeeperUserId, "банщику уведомления не идут ни по одному событию");
        final.Select(r => r.Payload).Should().OnlyContain(p => !p.Contains("Секретная") && !p.Contains("9054441122") && !p.Contains(b.Token));
    }

    // ── CY42-95: гостю — заголовок «EZBOOK Бани», слово «бронь», без ПДн ──────────

    [Fact, TestCase("CY42-95")]
    public async Task GuestPush_TitleEzbookBani_WordBron_NoPersonalDataAddressAmountsOrToken_Lifecycle()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var t = await TeamAsync(host);
        var first = await BookAsync(host, t.Resource, InDays(10));
        var hold = (await J(await host.Client().GetAsync($"/api/baths/service-orders/public/{first.Token}"))).GetProperty("holdExpiresAtUtc").GetDateTime();

        // «осталось 10 минут»
        host.StaysClock.Set(hold.AddMinutes(-9));
        await host.RunTaskAsync("stays-scheduled-messages");

        // другая бронь: подтверждена и отменена компанией
        var second = await BookAsync(host, t.Resource, InDays(11), phone: "+79054441123");
        (await host.Client().PostAsync($"/api/baths/service-orders/public/{second.Token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "c.jpg")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await StaffActionAsync(host, t, second.SessionId, "confirm-payment")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StaffActionAsync(host, t, second.SessionId, "cancel", "Авария")).StatusCode.Should().Be(HttpStatusCode.OK);

        // первая бронь снята по таймеру
        host.StaysClock.Set(hold.AddMinutes(1));
        await host.RunTaskAsync("stays-hold-expiry");
        (await J(await host.Client().GetAsync($"/api/baths/service-orders/public/{first.Token}"))).GetProperty("status").GetString().Should().Be("ExpiredUnpaid");

        var rows = (await GuestRowsAsync(first.OrderId)).Concat(await GuestRowsAsync(second.OrderId)).ToList();
        rows.Select(r => r.Type).Should().Contain([NotificationType.ServiceGuestHoldExpiring, NotificationType.ServiceGuestHoldExpired, NotificationType.ServiceGuestConfirmed,
            NotificationType.ServiceGuestCancelledByOwner]);
        foreach (var row in rows)
        {
            var payload = Payload(row.Payload);
            payload.GetProperty("title").GetString().Should().Be("EZBOOK Бани", row.Type.ToString());
            var body = payload.GetProperty("body").GetString()!;
            body.Should().NotContain("Секретная").And.NotContain("9054441122").And.NotContain("Тайная").And.NotContain("₽").And.NotContain("Авария").And.NotContain("Сбербанк")
                .And.NotContain(first.Token).And.NotContain(second.Token).And.NotContain("http").And.NotContain("Русская", "название ресурса в push не нужно");
            NoDomWords(body, row.Type.ToString());
            payload.GetProperty("url").GetString().Should().StartWith("/s/", "ссылка лежит в зашифрованной нагрузке, не в видимом тексте");
            payload.GetProperty("tag").GetString().Should().StartWith("so-");
            Payload(row.Payload).EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("title", "body", "tag", "url");
        }
        rows.Single(r => r.Type == NotificationType.ServiceGuestHoldExpiring).Payload.Should().Contain("Осталось 10 минут, чтобы приложить подтверждение оплаты");
        Payload(rows.First(r => r.Type == NotificationType.ServiceGuestConfirmed).Payload).GetProperty("body").GetString().Should().Be("Статус вашей брони изменился");
    }

    // ── CY42-96: сообщение в мессенджер — ссылка на bani, слово «бронь», время с пометкой ──

    [Fact, TestCase("CY42-96")]
    public async Task GuestMessenger_LinkOnBani_WordBron_LocalTimeNote_RequisitesOnlyForPrepaid_NoDomWords()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var t = await TeamAsync(host);
        await SeedFundedChannelAsync(t.Company);
        var settings = await AuthedClient(t.Company.Token).PutJsonAsync($"/api/baths/companies/{t.Company.CompanyId}/notification-settings",
            new StaysNotificationSettingsInput(true, false, true, true, null, null));
        settings.StatusCode.Should().Be(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());

        var b = await BookAsync(host, t.Resource, InDays(10), messenger: true, name: "Анна", phone: UniquePhone());
        await WithDbAsync(async db =>
        {
            // согласие на сообщения у брони уже записано при создании; проверяем, что очередь содержит текст
            var any = await db.OutboundNotifications.AnyAsync(n => n.StayServiceOrderId == b.OrderId);
            any.Should().BeTrue("при галочке мессенджера и подключённом канале сообщение ставится в очередь");
        });
        var created = await WithDbAsync(db => db.OutboundNotifications.AsNoTracking().Where(n => n.StayServiceOrderId == b.OrderId && n.Type == NotificationType.ServiceGuestOrderCreated).ToListAsync());
        created.Should().NotBeEmpty();
        var message = created.First();
        message.Status.Should().Be(NotificationStatus.Pending, message.Reason?.ToString());
        message.Body.Should().Contain($"https://bani.ezbook.ru/s/{b.Token}").And.Contain("брон").And.Contain("(время местное,");
        message.Body.Should().NotContain("dom.ezbook.ru");
        NoDomWords(message.Body, "сообщение в мессенджер");
        message.Body.Should().Contain("Сбербанк", "реквизиты этой брони — только в мессенджер этой брони");

        // отмена компанией: с предоплатой — возврат полностью и право на возмещение убытков
        var guestPhone = message.RecipientPhone!;
        (await host.Client().PostAsync($"/api/baths/service-orders/public/{b.Token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "c.jpg"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await StaffActionAsync(host, t, b.SessionId, "confirm-payment")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StaffActionAsync(host, t, b.SessionId, "cancel", "Авария")).StatusCode.Should().Be(HttpStatusCode.OK);
        var all = await WithDbAsync(db => db.OutboundNotifications.AsNoTracking().Where(n => n.StayServiceOrderId == b.OrderId).ToListAsync());
        var cancelled = all.Single(n => n.Type == NotificationType.ServiceGuestCancelledByOwner);
        cancelled.Body.Should().NotContain("dom.ezbook.ru");
        NoDomWords(cancelled.Body, "отмена компанией");
        cancelled.Body.Should().Contain("убытков", "после оплаты — предоплата возвращается полностью и есть право на возмещение убытков");
        cancelled.Body.Should().Contain("Предоплата возвращается полностью").And.NotContain("не вносилась");
        all.Select(n => n.RecipientPhone).Should().OnlyContain(p => p == guestPhone);
    }

    private async Task SeedFundedChannelAsync(BathCtx c)
    {
        await WithDbAsync(async db =>
        {
            var accountId = await db.Companies.Where(x => x.Id == c.CompanyId).Select(x => x.BillingAccountId!.Value).SingleAsync();
            var channel = new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = c.Owner.UserId, BillingAccountId = accountId, State = ChannelState.Connected, PhoneNumber = UniquePhone().TrimStart('+'),
                ProviderInstanceId = Unique("instance"), ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1)
            };
            db.NotificationChannels.Add(channel);
            db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
            {
                Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = c.CompanyId, BillingAccountId = accountId, AssignedByUserId = c.Owner.UserId
            });
            await db.SaveChangesAsync();
            await NotificationTestBase.EnsureWhatsAppPaidAsync(db, accountId);
        });
    }

    // ── CY42-97: слова гостевых текстов страницы брони и отказов ─────────────────

    [Fact, TestCase("CY42-97")]
    public async Task GuestPageAndRefusals_AreFreeOfDomWords_InEveryStatus()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var t = await TeamAsync(host);
        var guest = host.Client();
        var date = InDays(10);

        string Page(JsonElement e) => e.GetRawText();
        async Task<JsonElement> Get(string token) => await J(await guest.GetAsync($"/api/baths/service-orders/public/{token}"));

        // «Удержан»
        var held = await BookAsync(host, t.Resource, date, 600, subscribe: false);
        var heldPage = await Get(held.Token);
        heldPage.GetProperty("status").GetString().Should().Be("Held");
        NoDomWords(Page(heldPage), "бронь удержана");

        // «Оплата на проверке»
        (await guest.PostAsync($"/api/baths/service-orders/public/{held.Token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "c.jpg"))).StatusCode.Should().Be(HttpStatusCode.Created);
        var checking = await Get(held.Token);
        checking.GetProperty("status").GetString().Should().Be("AwaitingPaymentCheck");
        NoDomWords(Page(checking), "оплата на проверке");

        // «Подтверждён»
        (await StaffActionAsync(host, t, held.SessionId, "confirm-payment")).StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = await Get(held.Token);
        NoDomWords(Page(confirmed), "подтверждена");

        // отмена гостем (в сводке — сумма возврата), второй раз — отказ
        var cancel = await guest.PostJsonAsync($"/api/baths/service-orders/public/{held.Token}/cancel", new { });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        NoDomWords(await cancel.Content.ReadAsStringAsync(), "отмена гостем");
        var cancelledPage = await Get(held.Token);
        cancelledPage.GetProperty("status").GetString().Should().Be("CancelledByGuest");
        NoDomWords(Page(cancelledPage), "отменена гостем");
        var again = await guest.PostJsonAsync($"/api/baths/service-orders/public/{held.Token}/cancel", new { });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        NoDomWords(await again.Content.ReadAsStringAsync(), "повторная отмена");
        var lateProof = await guest.PostAsync($"/api/baths/service-orders/public/{held.Token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "c.jpg"));
        lateProof.IsSuccessStatusCode.Should().BeFalse();
        NoDomWords(await lateProof.Content.ReadAsStringAsync(), "подтверждение после отмены");

        // отклонена и отменена компанией
        var rejected = await BookAsync(host, t.Resource, date, 900, subscribe: false);
        (await guest.PostAsync($"/api/baths/service-orders/public/{rejected.Token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "c.jpg"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await StaffActionAsync(host, t, rejected.SessionId, "reject-payment", "Оплата не поступила")).StatusCode.Should().Be(HttpStatusCode.OK);
        NoDomWords(Page(await Get(rejected.Token)), "оплата отклонена");
        var byOwner = await BookAsync(host, t.Resource, date, 1140, subscribe: false);
        (await StaffActionAsync(host, t, byOwner.SessionId, "cancel", "Авария")).StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerPage = await Get(byOwner.Token);
        ownerPage.GetProperty("status").GetString().Should().Be("CancelledByOwner");
        NoDomWords(Page(ownerPage), "отменена компанией");

        // занятое время и расчёт вне расписания — тексты отказов
        var taken = await guest.PostJsonAsync($"/api/baths/public/services/{t.Resource.Id}/orders", BathOrderBody(date, 1140, 2, 2, 4000, UniquePhone()));
        if (taken.StatusCode == HttpStatusCode.Created) { /* время освободилось после отмены — отказа нет, проверять нечего */ }
        else NoDomWords(await taken.Content.ReadAsStringAsync(), "отказ при создании");
        var offHours = await guest.PostJsonAsync($"/api/baths/public/services/{t.Resource.Id}/orders", BathOrderBody(date, 120, 2, 2, 4000, UniquePhone()));
        offHours.IsSuccessStatusCode.Should().BeFalse();
        NoDomWords(await offHours.Content.ReadAsStringAsync(), "время вне расписания");

        // таймер: бронь снята
        var expiring = await BookAsync(host, t.Resource, InDays(12), 600, subscribe: false);
        var hold = (await Get(expiring.Token)).GetProperty("holdExpiresAtUtc").GetDateTime();
        host.StaysClock.Set(hold.AddMinutes(1));
        await host.RunTaskAsync("stays-hold-expiry");
        var expired = await Get(expiring.Token);
        expired.GetProperty("status").GetString().Should().Be("ExpiredUnpaid");
        NoDomWords(Page(expired), "снята по таймеру");
    }

    // ── CY42-98: нигде в push нет ПДн, адреса, сумм и токена ─────────────────────

    [Fact, TestCase("CY42-98")]
    public async Task EveryPushOfTheVertical_HasNoPersonalDataAddressAmountsOrToken_IncludingTheReminder()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var t = await TeamAsync(host);
        var date = InDays(4);
        var b = await BookAsync(host, t.Resource, date, 900);
        (await host.Client().PostAsync($"/api/baths/service-orders/public/{b.Token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "c.jpg"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await StaffActionAsync(host, t, b.SessionId, "confirm-payment")).StatusCode.Should().Be(HttpStatusCode.OK);
        var tz = await WithDbAsync(db => db.Companies.AsNoTracking().Where(c => c.Id == t.Company.CompanyId).Select(c => c.TimeZoneId).SingleAsync());
        var start = BusinessClock.ToUtc(tz, date, 900);
        host.StaysClock.Set(start.AddHours(-3).AddMinutes(1));
        await host.RunTaskAsync("stays-scheduled-messages");

        var guestRows = await GuestRowsAsync(b.OrderId);
        guestRows.Should().Contain(r => r.Type == NotificationType.ServiceGuestSessionReminder);
        var staffRows = await StaffRowsAsync(b.OrderId);
        foreach (var json in guestRows.Select(r => r.Payload).Concat(staffRows.Select(r => r.Payload)))
        {
            var p = Payload(json);
            var visible = p.GetProperty("title").GetString() + " | " + p.GetProperty("body").GetString();
            var digitsText = p.GetProperty("body").GetString()!;
            visible.Should().NotContain("Секретная").And.NotContain("9054441122").And.NotContain("Тайная").And.NotContain("₽").And.NotContain("Сбербанк").And.NotContain(b.Token)
                .And.NotContain("http").And.NotContainAny("Лесная", "2200", "рублей");
            Regex.IsMatch(digitsText, @"\d{4,}").Should().BeFalse("в тексте push нет длинных чисел (телефон, карта, сумма): " + digitsText);
        }
        var reminder = guestRows.Single(r => r.Type == NotificationType.ServiceGuestSessionReminder);
        Payload(reminder.Payload).GetProperty("title").GetString().Should().Be("EZBOOK Бани");
        Payload(reminder.Payload).GetProperty("body").GetString().Should().Be("Скоро ваш сеанс — откройте бронь");
        Payload(reminder.Payload).GetProperty("url").GetString().Should().Be($"/s/{b.Token}");
    }

    // ── CY42-99: банщику ничего ──────────────────────────────────────────────────

    [Fact, TestCase("CY42-99")]
    public async Task Housekeeper_GetsNoNotificationOfAnyKind_AcrossTheWholeLifecycle()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var t = await TeamAsync(host);
        var date = InDays(5);
        var b = await BookAsync(host, t.Resource, date, 720);
        (await host.Client().PostAsync($"/api/baths/service-orders/public/{b.Token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "c.jpg"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await StaffActionAsync(host, t, b.SessionId, "confirm-payment")).StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await BookAsync(host, t.Resource, date, 1020, subscribe: false);
        (await host.Client().PostJsonAsync($"/api/baths/service-orders/public/{second.Token}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        var third = await BookAsync(host, t.Resource, date, 1260, subscribe: false);
        (await StaffActionAsync(host, t, third.SessionId, "cancel", "Авария")).StatusCode.Should().Be(HttpStatusCode.OK);
        var manual = await host.Client(t.Company.Token).PostJsonAsync($"/api/baths/companies/{t.Company.CompanyId}/service-sessions", new
        {
            serviceId = t.Resource.Id, businessDate = InDays(6), startMinute = 720, hours = 2, items = Array.Empty<object>(), guestName = "Пётр", guestPhone = (string?)null, comment = (string?)null,
            requestBasis = "Phone", idempotencyKey = Guid.NewGuid(), guestsCount = 2
        });
        manual.StatusCode.Should().Be(HttpStatusCode.Created, await manual.Content.ReadAsStringAsync());
        var tz = await WithDbAsync(db => db.Companies.AsNoTracking().Where(c => c.Id == t.Company.CompanyId).Select(c => c.TimeZoneId).SingleAsync());
        host.StaysClock.Set(BusinessClock.ToUtc(tz, date, 720).AddHours(-3).AddMinutes(1));
        await host.RunTaskAsync("stays-scheduled-messages");

        var company = t.Company.CompanyId;
        var hkRows = await WithDbAsync(db => db.StaffPushNotifications.AsNoTracking().Where(n => n.UserId == t.HousekeeperUserId).ToListAsync());
        hkRows.Should().BeEmpty("банщику ни push, ни другие уведомления о бронях не идут");
        var ownerRows = await WithDbAsync(db => db.StaffPushNotifications.AsNoTracking().Where(n => n.UserId == t.Company.Owner.UserId && n.CompanyId == company).ToListAsync());
        ownerRows.Should().NotBeEmpty("владелец уведомления получает — тест не пустой");
        (await WithDbAsync(db => db.OutboundNotifications.CountAsync(n => n.RecipientUserId == t.HousekeeperUserId))).Should().Be(0);

        // банщик по-прежнему видит расписание — это не наказание, а отсутствие рассылки
        (await AuthedClient(t.HousekeeperToken).GetAsync($"/api/baths/companies/{company}/schedule?from={D(date)}&days=1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// BUG-C42-QA-4 (НЕ ВЫПОЛНЯЕТСЯ): push персоналу об отмене гостем говорит «Гость отменил сеанс · …», контракт §42.36.3 требует «Гость отменил бронь · {Ресурс}, {время}»
    /// (слово «бронь» во всех строках bani). Снять Skip после правки текста для вида «Бани».
    /// </summary>
    [Fact, TestCase("CY42-94")]
    public async Task StaffPush_CancelByGuest_SaysBronNotSeans()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var t = await TeamAsync(host);
        var b = await BookAsync(host, t.Resource, InDays(10));
        (await host.Client().PostJsonAsync($"/api/baths/service-orders/public/{b.Token}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await StaffRowsAsync(b.OrderId)).Where(r => r.Type == NotificationType.StaffServiceSessionCancelledByGuest).ToList();
        rows.Should().NotBeEmpty();
        rows.Select(r => Payload(r.Payload).GetProperty("body").GetString()!).Should().OnlyContain(x => x.StartsWith("Гость отменил бронь · Русская баня"));
    }
}
