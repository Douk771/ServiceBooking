using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39, «Вызов 2»: уведомления услуг (US-39-17; ARCHITECTURE_CYCLE39.md §39.9): 12 новых типов (27…38), персоналу — владельцу и управляющим (горничной никогда) без имени и телефона
/// гостя, гостю — push без ПДн, адресов и сумм, тексты всех типов, маска салона не затронута. Web Push включён, отправитель записывающий (сети нет).
/// </summary>
public class Cycle39NotificationsTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private static async Task<Guid> SubscribeStaffAsync(HttpClient client, string tag)
    {
        var r = await client.PostAsJsonAsync("/api/push/subscriptions",
            new { endpoint = $"https://push.example.test/cy39/{tag}/{Guid.NewGuid():N}", keys = new { p256dh = "p256dh-key", auth = "auth-key" }, deviceLabel = "Chrome", site = "Stays" });
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<List<StaffPushNotification>> StaffRowsAsync(Guid? bookingId = null, Guid? orderId = null) =>
        WithDbAsync(db => db.StaffPushNotifications.AsNoTracking()
            .Where(n => (bookingId != null && n.StayBookingId == bookingId) || (orderId != null && n.StayServiceOrderId == orderId)).OrderBy(n => n.ExpiresAtUtc).ToListAsync());

    private Task<List<StayGuestPushNotification>> GuestRowsAsync(Guid? bookingId = null, Guid? orderId = null) =>
        WithDbAsync(db => db.StayGuestPushNotifications.AsNoTracking()
            .Where(n => (bookingId != null && n.StayBookingId == bookingId) || (orderId != null && n.StayServiceOrderId == orderId)).OrderBy(n => n.CreatedAt).ToListAsync());

    private static string Body(string payload) => JsonDocument.Parse(payload).RootElement.GetProperty("body").GetString()!;

    /// <summary>Действие персонала над сеансом через ТОТ ЖЕ хост (уведомления планирует хост, в котором включён Web Push).</summary>
    private Task<HttpResponseMessage> HostActionAsync(StaysTestFactory host, StaysCtx company, Guid sessionId, string action, int version, string? reason = null) =>
        host.Client(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions/{sessionId}/{action}",
            action == "confirm-payment" ? (object)new ExpectedVersionInput(version) : new ExpectedVersionReasonInput(version, reason));

    private async Task SubscribeGuestOrderAsync(HttpClient client, string token) =>
        (await client.PostJsonAsync($"/api/stays/service-orders/public/{token}/push-subscription",
            new PushSubscriptionInput($"https://push.example.test/cy39-guest/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NoContent);

    // ── персонал ─────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-160")]
    public async Task StaffPush_ForOrders_ToOwnerAndManagerOnly_NoGuestPersonalData_CabinetLink()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: 30);
        var manager = await AddStaffAsync(company, "Manager");
        var housekeeper = await AddStaffAsync(company, "Housekeeper");
        var ownerSub = await SubscribeStaffAsync(host.Client(company.OwnerToken), "owner");
        var managerSub = await SubscribeStaffAsync(host.Client(manager.Token), "manager");
        var hkSub = await SubscribeStaffAsync(host.Client(housekeeper.Token), "housekeeper");

        var created = await OrderOkAsync(svc.Id, InDays(10), 720, 2, phone: "+79054441122", name: "Секретная Гостья", client: host.Client());
        var orderId = await OrderIdAsync(created.Token);
        var sessionId = await SessionIdOfOrderAsync(created.Token);
        var rows = await StaffRowsAsync(orderId: orderId);
        rows.Select(r => r.SubscriptionId).Should().BeEquivalentTo(new Guid?[] { ownerSub, managerSub }, "новый заказ — владельцу и управляющему, не горничной");
        rows.Should().OnlyContain(r => r.Type == NotificationType.StaffServiceOrderCreated);
        foreach (var row in rows)
        {
            var payload = JsonDocument.Parse(row.Payload).RootElement;
            payload.GetProperty("url").GetString().Should().Contain($"/cabinet/{company.Id}").And.Contain(sessionId.ToString(), "ссылка на карточку сеанса в кабинете");
            row.Payload.Should().NotContain("Секретная").And.NotContain("9054441122").And.NotContain("Сбербанк");
            Body(row.Payload).Should().Contain("Баня");
        }

        // первый файл подтверждения — второе событие; второй файл новых строк не даёт
        (await AttachOrderProofAsync(created.Token, host.Client())).StatusCode.Should().Be(HttpStatusCode.Created);
        var afterProof = await StaffRowsAsync(orderId: orderId);
        afterProof.Count(r => r.Type == NotificationType.StaffServiceOrderPaymentProofUploaded).Should().Be(2);
        (await AttachOrderProofAsync(created.Token, host.Client())).StatusCode.Should().Be(HttpStatusCode.Created);
        (await StaffRowsAsync(orderId: orderId)).Count.Should().Be(afterProof.Count, "оповещает только первый файл");

        // отмена гостем
        (await CancelOrderAsync(created.Token, host.Client())).StatusCode.Should().Be(HttpStatusCode.OK);
        var final = await StaffRowsAsync(orderId: orderId);
        final.Count(r => r.Type == NotificationType.StaffServiceSessionCancelledByGuest).Should().Be(2);
        final.Should().NotContain(r => r.SubscriptionId == hkSub);
        final.Select(r => r.Payload).Should().OnlyContain(p => !p.Contains("Секретная") && !p.Contains("9054441122"));
    }

    [Fact, TestCase("CY39-161")]
    public async Task StaffPush_ForSessionsInBookings_AddedByGuest_AndNewBookingWithServices_TextOnlyWithServicesCount()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        var house = await CreateHouseAsync(company, price: 2000);
        var svc = await CreateServiceAsync(company);
        var ownerSub = await SubscribeStaffAsync(host.Client(company.OwnerToken), "owner");

        // бронь без услуг: текст байт-в-байт прежний (без « · услуги»)
        var plain = await BookOkAsync(house.Id, InDays(10), InDays(12), name: "Без Услуг", client: host.Client());
        var plainId = await BookingIdAsync(plain.Token);
        var plainRows = await StaffRowsAsync(bookingId: plainId);
        plainRows.Should().ContainSingle(r => r.Type == NotificationType.StaffStayCreated);
        Body(plainRows.Single().Payload).Should().StartWith("Новая бронь").And.NotContain("услуги");

        // бронь с двумя сеансами: « · услуги: 2»
        var ci = InDays(14);
        var sel1 = new StayServiceSelectionInput(svc.Id, ci.AddDays(1), 600, 2, []);
        var sel2 = new StayServiceSelectionInput(svc.Id, ci.AddDays(1), 900, 2, []);
        var q = await QuoteAsync(house.Id, ci, ci.AddDays(3));
        var with = await host.Client().PostJsonAsync($"/api/stays/public/houses/{house.Id}/bookings", Booking(ci, ci.AddDays(3), q.TotalRub + 8000) with { Services = [sel1, sel2] });
        with.StatusCode.Should().Be(HttpStatusCode.Created, await with.Content.ReadAsStringAsync());
        var withToken = (await with.Content.ReadJsonAsync<CreateStayBookingResponse>())!.Token;
        var withRows = await StaffRowsAsync(bookingId: await BookingIdAsync(withToken));
        Body(withRows.Single(r => r.Type == NotificationType.StaffStayCreated).Payload).Should().EndWith(" · услуги: 2");

        // гость добавил сеанс к брони
        var added = await AddSessionOkAsync(plain.Token, svc.Id, InDays(11), 1080, 2);
        added.Sessions.Should().ContainSingle();
        var rows = await StaffRowsAsync(bookingId: plainId);
        var sessionRow = rows.Single(r => r.Type == NotificationType.StaffStaySessionAdded);
        sessionRow.SubscriptionId.Should().Be(ownerSub);
        sessionRow.Payload.Should().NotContain("Без Услуг");
        Body(sessionRow.Payload).Should().Contain("Баня");

        // гость отменил сеанс — персоналу сообщение «отменил» (тип 30) по сеансу брони
        var sessionId = added.Sessions!.Single().Id;
        (await host.Client().PostJsonAsync($"/api/stays/bookings/public/{plain.Token}/sessions/{sessionId}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StaffRowsAsync(bookingId: plainId)).Should().Contain(r => r.Type == NotificationType.StaffServiceSessionCancelledByGuest);
    }

    // ── гость ────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-162")]
    public async Task GuestPush_ForOrder_NoPersonalDataAddressesOrAmounts_Lifecycle()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        await WithDbAsync(async db =>
        {
            var c = await db.Companies.SingleAsync(x => x.Id == company.Id);
            c.Address = "ул. Тайная, 77, Шерегеш";
            await db.SaveChangesAsync();
        });
        var svc = await CreateServiceAsync(company, prepay: 30);
        var client = host.Client();

        var created = await OrderOkAsync(svc.Id, InDays(10), 720, 2, phone: "+79054441122", name: "Секретная Гостья", client: client);
        var orderId = await OrderIdAsync(created.Token);
        await SubscribeGuestOrderAsync(client, created.Token);
        var holdUntil = (await GetOrderAsync(created.Token, client)).HoldExpiresAtUtc!.Value;

        // «осталось 10 минут»
        host.StaysClock.Set(holdUntil.AddMinutes(-9));
        await host.RunTaskAsync("stays-scheduled-messages");
        // снят по таймеру → другой заказ: подтверждён, отклонён, отменён компанией
        var proofOrder = await OrderOkAsync(svc.Id, InDays(11), 720, 2, phone: "+79054441123", name: "Секретная Гостья", client: client);
        await SubscribeGuestOrderAsync(client, proofOrder.Token);
        await AttachOrderProofAsync(proofOrder.Token, client);
        var sid = await SessionIdOfOrderAsync(proofOrder.Token);
        (await HostActionAsync(host, company, sid, "confirm-payment", (await SessionCardAsync(company, sid)).Version)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await HostActionAsync(host, company, sid, "cancel", (await SessionCardAsync(company, sid)).Version, "Авария")).StatusCode.Should().Be(HttpStatusCode.OK);

        host.StaysClock.Set(holdUntil.AddMinutes(1));
        await host.RunTaskAsync("stays-hold-expiry");
        (await GetOrderAsync(created.Token, client)).Status.Should().Be(StayBookingStatus.ExpiredUnpaid);

        var rows = await GuestRowsAsync(orderId: orderId);
        rows.Select(r => r.Type).Should().Contain([NotificationType.ServiceGuestHoldExpiring, NotificationType.ServiceGuestHoldExpired]);
        var all = rows.Concat(await GuestRowsAsync(orderId: await OrderIdAsync(proofOrder.Token))).ToList();
        all.Select(r => r.Type).Should().Contain([NotificationType.ServiceGuestConfirmed, NotificationType.ServiceGuestCancelledByOwner]);
        foreach (var row in all)
        {
            var payload = JsonDocument.Parse(row.Payload).RootElement;
            payload.GetProperty("title").GetString().Should().Be("ezbook · Дома");
            var body = payload.GetProperty("body").GetString()!;
            body.Should().NotContain("Секретная").And.NotContain("9054441122").And.NotContain("Тайная").And.NotContain("₽").And.NotContain("Авария").And.NotContainEquivalentOf("баня");
            payload.GetProperty("url").GetString().Should().StartWith("/s/", "ссылка на страницу заказа лежит в зашифрованной нагрузке, не в тексте");
            payload.GetProperty("tag").GetString().Should().StartWith("so-");
        }
        all.Single(r => r.Type == NotificationType.ServiceGuestHoldExpiring).Payload.Should().Contain("Осталось 10 минут, чтобы приложить подтверждение оплаты");
    }

    [Fact, TestCase("CY39-163")]
    public async Task GuestPush_ForSessionInBooking_OnlyWhenStaffAdded_AndWhenOwnerCancels_NoPersonalData()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        var house = await CreateHouseAsync(company, price: 2000, address: "ул. Тайная, 77");
        var svc = await CreateServiceAsync(company);
        var client = host.Client();
        var ci = InDays(12);
        var booked = await BookOkAsync(house.Id, ci, ci.AddDays(3), name: "Секретная Гостья", phone: "+79054441122", client: client);
        var id = await BookingIdAsync(booked.Token);
        (await client.PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription",
            new PushSubscriptionInput($"https://push.example.test/cy39-bk/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var before = (await GuestRowsAsync(bookingId: id)).Count;

        // гость добавил сам → push гостю не нужен
        await AddSessionOkAsync(booked.Token, svc.Id, ci.AddDays(1), 600, 2);
        (await GuestRowsAsync(bookingId: id)).Count(r => r.Type == NotificationType.StayGuestSessionAdded).Should().Be(0, "гость сам добавил сеанс — уведомление о нём гостю не нужно");

        // персонал добавил «по просьбе гостя» → push
        var card = await host.Client(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/bookings/{id}/sessions",
            new StaffAddSessionInput(svc.Id, ci.AddDays(1), 900, 2, [], StayServiceRequestBasis.Phone, Guid.NewGuid()));
        card.StatusCode.Should().Be(HttpStatusCode.Created, await card.Content.ReadAsStringAsync());
        var rows = await GuestRowsAsync(bookingId: id);
        var added = rows.Single(r => r.Type == NotificationType.StayGuestSessionAdded);
        Body(added.Payload).Should().ContainEquivalentOf("добавлен").And.Contain("откройте бронь");

        // владелец отменил сеанс → push гостю
        var staffCard = (await card.Content.ReadJsonAsync<StaffStayBookingCardDto>())!;
        var s = staffCard.Sessions!.Single(x => x.AddedByText.Length > 0 && x.State == StayServiceSessionState.Active && x.Time.StartMinute == 900);
        (await HostActionAsync(host, company, s.Id, "cancel", s.Version, "Авария")).StatusCode.Should().Be(HttpStatusCode.OK);
        rows = await GuestRowsAsync(bookingId: id);
        rows.Should().Contain(r => r.Type == NotificationType.StayGuestSessionCancelledByOwner);
        foreach (var row in rows.Skip(before))
        {
            var payload = JsonDocument.Parse(row.Payload).RootElement;
            var body = payload.GetProperty("body").GetString()!;
            body.Should().NotContain("Секретная").And.NotContain("9054441122").And.NotContain("Тайная").And.NotContain("₽").And.NotContain("Авария");
            payload.GetProperty("url").GetString().Should().StartWith("/b/");
        }
    }

    // ── тексты и маска ───────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-164")]
    public void EveryNewNotificationType_HasText_IsStayType_AndHasNoBitInSalonMask()
    {
        var all = Enum.GetValues<NotificationType>();
        foreach (var type in all)
            NotificationTexts.TypeText(type).Should().NotBeNullOrWhiteSpace($"у типа {type} есть название для экранов");
        NotificationTypeCatalog.ServiceTypes.Should().HaveCount(13, "12 типов цикла 39 и 41 — напоминание перед сеансом (цикл 42)");
        ((int)NotificationType.StaffStaySessionAdded).Should().Be(27);
        ((int)NotificationType.StayGuestSessionCancelledByOwner).Should().Be(38);
        foreach (var t in NotificationTypeCatalog.ServiceTypes)
        {
            NotificationTypeCatalog.IsStayType(t).Should().BeTrue(t.ToString());
            NotificationTypeCatalog.IsBookingType(t).Should().BeFalse($"{t} не имеет бита в салонной маске");
            (((int)t) is >= 27 and <= 38 or 41).Should().BeTrue(t.ToString());
        }
        all.Select(t => (int)t).Should().OnlyHaveUniqueItems("значения перечисления не повторяются");
        all.Select(t => (int)t).Where(v => v is 39 or 40).Should().BeEmpty("39 и 40 оставлены циклу 40");
    }
}
