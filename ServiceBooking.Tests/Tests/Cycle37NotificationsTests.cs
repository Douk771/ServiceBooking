using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 37, «Вызов 2»: уведомления вертикали «Дома» (блок J, US-37-29/30; ARCHITECTURE_CYCLE37.md §37.12). Web Push включён, отправитель
/// записывающий (сети нет). Персоналу — владелец и управляющие, горничной — никогда; push гостя — без имён, телефонов, адресов и кодов.
/// </summary>
public class Cycle37NotificationsTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    private static async Task<Guid> SubscribeStaffAsync(HttpClient client, string tag)
    {
        var r = await client.PostAsJsonAsync("/api/push/subscriptions",
            new { endpoint = $"https://push.example.test/cy37/{tag}/{Guid.NewGuid():N}", keys = new { p256dh = "p256dh-key", auth = "auth-key" }, deviceLabel = "Chrome", site = "Stays" });
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<List<StaffPushNotification>> StaffRowsAsync(Guid bookingId) =>
        WithDbAsync(db => db.StaffPushNotifications.AsNoTracking().Where(n => n.StayBookingId == bookingId).ToListAsync());

    [Fact, TestCase("CY37-120")]
    public async Task StaffPush_GoesToOwnerAndManagerDevices_NeverToHousekeeper_WithoutGuestPersonalData()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var manager = await AddStaffAsync(company, "Manager");
        var housekeeper = await AddStaffAsync(company, "Housekeeper");
        var ownerSub = await SubscribeStaffAsync(host.Client(company.OwnerToken), "owner");
        var managerSub = await SubscribeStaffAsync(host.Client(manager.Token), "manager");
        var housekeeperSub = await SubscribeStaffAsync(host.Client(housekeeper.Token), "housekeeper");

        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12), phone: "+79051234567", name: "Секретный Гость", client: host.Client());
        var id = await BookingIdAsync(booked.Token);
        var rows = await StaffRowsAsync(id);
        rows.Select(r => r.SubscriptionId).Should().BeEquivalentTo(new Guid?[] { ownerSub, managerSub }, "новая бронь — владельцу и управляющему, не горничной");
        rows.Should().NotContain(r => r.SubscriptionId == housekeeperSub);
        foreach (var row in rows)
        {
            var payload = JsonDocument.Parse(row.Payload).RootElement;
            payload.GetProperty("title").GetString().Should().Be(company.Company.Name);
            payload.GetProperty("url").GetString().Should().Be($"https://dom.ezbook.ru/cabinet/{company.Id}/bookings/{id}");
            payload.GetProperty("body").GetString().Should().StartWith("Новая бронь").And.Contain(house.House.Name);
            row.Payload.Should().NotContain("Секретный").And.NotContain("9051234567").And.NotContain("Сбербанк");
        }

        // чек -> второе событие "приложено подтверждение"; второй файл новых строк не даёт
        await AttachProofOkAsync(booked.Token, host.Client());
        (await StaffRowsAsync(id)).Count.Should().Be(4);
        await AttachProofAsync(booked.Token, host.Client(), width: 70);
        (await StaffRowsAsync(id)).Count.Should().Be(4, "оповещает только первый файл");
        // отмена гостем — тоже оповещает
        await host.Client().PostAsync($"/api/stays/bookings/public/{booked.Token}/cancel", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        (await StaffRowsAsync(id)).Count.Should().Be(6);
        (await StaffRowsAsync(id)).Should().OnlyContain(r => !r.Payload.Contains("9051234567"));
    }

    [Fact, TestCase("CY37-121")]
    public async Task StaffPushDispatcher_SkipsAMemberWhoBecameHousekeeper_AndSendsToManager()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var manager = await AddStaffAsync(company, "Manager");
        var demoted = await AddStaffAsync(company, "Manager");
        var managerSub = await SubscribeStaffAsync(host.Client(manager.Token), "m");
        var demotedSub = await SubscribeStaffAsync(host.Client(demoted.Token), "d");

        var booked = await BookOkAsync(house.Id, InDays(10), InDays(11), client: host.Client());
        var id = await BookingIdAsync(booked.Token);
        (await StaffRowsAsync(id)).Select(r => r.SubscriptionId).Should().Contain([managerSub, demotedSub]);

        // уже поставленное в очередь сообщение: сотрудника переводят в горничные до отправки
        (await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/Companies/{company.Id}/members/{demoted.MemberId}/position", new { position = "Housekeeper" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await host.RunTaskAsync("staff-push-dispatch");

        var rows = await StaffRowsAsync(id);
        rows.Single(r => r.SubscriptionId == demotedSub).Status.Should().Be(NotificationStatus.Skipped, "диспетчер пропускает горничную");
        rows.Single(r => r.SubscriptionId == managerSub).Status.Should().Be(NotificationStatus.Sent);
        host.Sender.Calls.Should().NotContain(c => c.PayloadJson.Contains("Секрет"));
        host.Sender.Calls.Count(c => rows.Where(r => r.SubscriptionId == demotedSub).Any(_ => c.SubscriptionId == demotedSub)).Should().Be(0, "на устройство горничной ничего не ушло");
    }

    [Fact, TestCase("CY37-122")]
    public async Task GuestPush_SubscribeOnBookingPage_PayloadHasNoPersonalData_AndIsSentOncePerEvent()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var client = host.Client();
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12), phone: "+79057654321", name: "Тайный Гость", client: client);
        var id = await BookingIdAsync(booked.Token);

        var page = await GetPublicBookingAsync(booked.Token, client);
        page.Notifications.WebPush.Available.Should().BeTrue();
        var endpoint = $"https://push.example.test/cy37-guest/{Guid.NewGuid():N}";
        var body = new PushSubscriptionInput(endpoint, new PushKeysInput("p256dh-key", "auth-key"), "Safari");
        (await client.PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription", body)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription", body)).StatusCode.Should().Be(HttpStatusCode.NoContent, "повтор — upsert");
        (await WithDbAsync(db => db.StayGuestPushSubscriptions.CountAsync(s => s.StayBookingId == id))).Should().Be(1);
        (await client.PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription", new PushSubscriptionInput("not-a-url", body.Keys, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostJsonAsync($"/api/stays/bookings/public/{Guid.NewGuid():N}/push-subscription", body)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // ключи в БД не открытым текстом
        var stored = await WithDbAsync(db => db.StayGuestPushSubscriptions.AsNoTracking().SingleAsync(s => s.StayBookingId == id));
        stored.P256dhCiphertext.Should().NotContain("p256dh-key");
        stored.AuthCiphertext.Should().NotContain("auth-key");

        // событие: оплата подтверждена -> push гостю; повтор прохода диспетчера не шлёт второй раз
        await AttachProofOkAsync(booked.Token, client);
        // событие обрабатывает хост, принявший запрос: подтверждаем через хост с включённым Web Push
        var confirm = await host.Client(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/bookings/{id}/confirm-payment",
            new ExpectedVersionInput((await StaffCardAsync(company, id)).Version));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        var queued = await WithDbAsync(db => db.StayGuestPushNotifications.AsNoTracking().Where(n => n.StayBookingId == id).ToListAsync());
        queued.Should().NotBeEmpty();
        foreach (var n in queued)
        {
            n.Payload.Should().NotContain("Тайный").And.NotContain("7654321").And.NotContain("Лесная").And.NotContain("Сбербанк");
            var p = JsonDocument.Parse(n.Payload).RootElement;
            p.GetProperty("title").GetString().Should().Be("ezbook · Дома");
            p.GetProperty("url").GetString().Should().Be($"/b/{booked.Token}");
        }
        queued.Select(n => n.IdempotencyKey).Should().OnlyHaveUniqueItems();
        await host.RunTaskAsync("stays-guest-push-dispatch");
        var sentOnce = host.Sender.Calls.Count(c => c.Endpoint == endpoint);
        sentOnce.Should().BeGreaterThanOrEqualTo(1);
        await host.RunTaskAsync("stays-guest-push-dispatch");
        host.Sender.Calls.Count(c => c.Endpoint == endpoint).Should().Be(sentOnce, "второй проход не шлёт те же сообщения");
        host.Sender.Calls.Where(c => c.Endpoint == endpoint).Should().OnlyContain(c => !c.PayloadJson.Contains("Тайный") && !c.PayloadJson.Contains("7654321"));

        // отписка идемпотентна
        (await client.PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription/remove", new PushSubscriptionRemoveInput(endpoint))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription/remove", new PushSubscriptionRemoveInput(endpoint))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.StayGuestPushSubscriptions.CountAsync(s => s.StayBookingId == id))).Should().Be(0);
    }

    [Fact, TestCase("CY37-123")]
    public async Task GuestPush_Refused_WhenCompanyDisabledIt_OrBookingIsFinished_AndLimitFivePerBooking()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var client = host.Client();
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12), client: client);
        var id = await BookingIdAsync(booked.Token);
        PushSubscriptionInput Sub(int i) => new($"https://push.example.test/cy37-lim/{i}-{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null);

        for (var i = 0; i < 7; i++)
            (await client.PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription", Sub(i))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.StayGuestPushSubscriptions.CountAsync(s => s.StayBookingId == id))).Should().Be(5, "не больше 5 браузеров на бронь, старый вытесняется");

        var settings = await AuthedClient(company.OwnerToken).GetFromJsonAsync<JsonElement>($"/api/stays/companies/{company.Id}/notification-settings");
        var off = await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/notification-settings",
            new StaysNotificationSettingsInput(settings.GetProperty("staffPushEnabled").GetBoolean(), settings.GetProperty("staffMaxEnabled").GetBoolean(), false, false, null, null));
        off.StatusCode.Should().Be(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        var disabled = await client.PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription", Sub(99));
        disabled.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await disabled.Content.ReadAsStringAsync()).Should().Be("Компания отключила уведомления о бронях");

        await client.PostAsync($"/api/stays/bookings/public/{booked.Token}/cancel", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/notification-settings",
            new StaysNotificationSettingsInput(true, false, true, false, null, null));
        var finished = await client.PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription", Sub(100));
        finished.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await finished.Content.ReadAsStringAsync()).Should().Be("Бронь завершена — уведомления не нужны");

        // сообщения мессенджера без подключённого канала включить нельзя
        var noChannel = await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/notification-settings",
            new StaysNotificationSettingsInput(true, false, true, true, null, null));
        noChannel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(noChannel)).Should().Be("MessengerUnavailable");
    }

    [Fact, TestCase("CY37-124")]
    public async Task ScheduledMessages_TenMinuteWarning_ArrivalReminder_AreQueuedOnce_AndReminderCanBeSwitchedOff()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var client = host.Client();
        var company = await CreateStaysCompanyAsync(settings: s => s with { HoldMinutes = 30 });
        var house = await CreateHouseAsync(company, price: 2000);
        var arrival = InDays(5);
        var booked = await BookOkAsync(house.Id, arrival, arrival.AddDays(2), client: client);
        var id = await BookingIdAsync(booked.Token);
        var sub = $"https://push.example.test/cy37-sched/{Guid.NewGuid():N}";
        (await client.PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/push-subscription", new PushSubscriptionInput(sub, new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var hold = (await WithDbAsync(db => db.StayBookings.Where(b => b.Id == id).Select(b => b.HoldExpiresAtUtc).SingleAsync()))!.Value;

        async Task<List<string>> Bodies() => (await WithDbAsync(db => db.StayGuestPushNotifications.AsNoTracking().Where(n => n.StayBookingId == id).Select(n => n.Payload).ToListAsync()))
            .Select(p => JsonDocument.Parse(p).RootElement.GetProperty("body").GetString()!).ToList();

        host.StaysClock.Set(hold.AddMinutes(-11));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await Bodies()).Should().NotContain("Осталось 10 минут, чтобы приложить подтверждение оплаты");
        host.StaysClock.Set(hold.AddMinutes(-9));
        await host.RunTaskAsync("stays-scheduled-messages");
        await host.RunTaskAsync("stays-scheduled-messages");
        (await Bodies()).Count(b => b == "Осталось 10 минут, чтобы приложить подтверждение оплаты").Should().Be(1, "однократно, без дубля при повторном проходе");

        // подтверждаем бронь; накануне заезда в 18:00 по времени компании — напоминание
        await AttachProofOkAsync(booked.Token, client);
        (await host.Client(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/bookings/{id}/confirm-payment",
            new ExpectedVersionInput((await StaffCardAsync(company, id)).Version))).StatusCode.Should().Be(HttpStatusCode.OK);
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 17, 59));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await Bodies()).Should().NotContain("Завтра заезд — откройте бронь");
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 18, 0));
        await host.RunTaskAsync("stays-scheduled-messages");
        await host.RunTaskAsync("stays-scheduled-messages");
        (await Bodies()).Count(b => b == "Завтра заезд — откройте бронь").Should().Be(1);

        // выключенное напоминание не ставится
        var off = await CreateStaysCompanyAsync(settings: s => s with { ArrivalReminderEnabled = false });
        var offHouse = await CreateHouseAsync(off, price: 2000);
        var b2 = await BookOkAsync(offHouse.Id, arrival, arrival.AddDays(2), client: client);
        var id2 = await BookingIdAsync(b2.Token);
        await client.PostJsonAsync($"/api/stays/bookings/public/{b2.Token}/push-subscription", new PushSubscriptionInput($"https://push.example.test/cy37-off/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null));
        await AttachProofOkAsync(b2.Token, client);
        await StaffActionOkAsync(off, id2, "confirm-payment", (await StaffCardAsync(off, id2)).Version);
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 18, 5));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await WithDbAsync(db => db.StayGuestPushNotifications.CountAsync(n => n.StayBookingId == id2 && n.Payload.Contains("Завтра заезд")))).Should().Be(0);
    }
}
