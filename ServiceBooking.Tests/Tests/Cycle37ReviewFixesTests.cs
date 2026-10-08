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
/// QA цикл 37, «Вызов 3»: сценарии по находкам ревью/QA №3–№6 и правкам после них. Написано по API_CONTRACT_CYCLE37.md (§37.21.4, §37.26.4, §37.29.1, §37.34)
/// и SPEC_CYCLE37_STAYS_HOUSES.md, а не по реализации. Каталог здесь НЕ сбрасывается вручную (<c>InvalidateCatalog</c>) — это и есть предмет проверки.
/// </summary>
public class Cycle37ReviewFixesTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    private static bool HasValue(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null;

    private async Task<List<Guid>> CatalogHouseIdsAsync() =>
        (await J(await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50"))).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("houseId").GetGuid()).ToList();

    // ── каталог обновляется сразу, без ручного сброса кеша ──────────────────────

    [Fact, TestCase("CY37-130")]
    public async Task Catalog_ReflectsPublishUnpublishArchiveAndAdminBlock_AtOnce_WithoutManualCacheReset()
    {
        var company = await CreateStaysCompanyAsync();
        var draft = await CreateHouseAsync(company, publish: false);
        var other = await CreateHouseAsync(company);
        var c = AuthedClient(company.OwnerToken);
        var hp = $"/api/stays/companies/{company.Id}/houses";

        (await CatalogHouseIdsAsync()).Should().Contain(other.Id).And.NotContain(draft.Id); // кеш «прогрет» состоянием ДО правок

        await PublishHouseAsync(company, draft.House);
        (await CatalogHouseIdsAsync()).Should().Contain(draft.Id, "опубликованный дом виден в каталоге сразу, не через 30 с кеша");

        (await c.PostJsonAsync($"{hp}/{draft.Id}/unpublish", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CatalogHouseIdsAsync()).Should().NotContain(draft.Id, "снятый с публикации дом пропадает сразу");

        (await c.PostJsonAsync($"{hp}/{other.Id}/archive", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CatalogHouseIdsAsync()).Should().NotContain(other.Id, "архивный дом пропадает сразу");

        var third = await CreateHouseAsync(company);
        (await CatalogHouseIdsAsync()).Should().Contain(third.Id);
        var admin = await LoginAsSuperAdminAsync();
        (await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/companies/{company.Id}",
            new { name = company.Company.Name, isActive = false, allowSelfBooking = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CatalogHouseIdsAsync()).Should().NotContain(third.Id, "дома заблокированной админом компании уходят из каталога сразу");

        (await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/companies/{company.Id}",
            new { name = company.Company.Name, isActive = true, allowSelfBooking = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CatalogHouseIdsAsync()).Should().Contain(third.Id, "разблокировка возвращает дома сразу");
    }

    // ── retention policy: шесть сроков «Домов» ──────────────────────────────────

    [Fact, TestCase("CY37-131")]
    public async Task AdminRetentionPolicy_ExposesSixStayPeriods_WithConfiguredValues()
    {
        var admin = await LoginAsSuperAdminAsync();
        var policy = await J(await AuthedClient(admin.Token).GetAsync("/api/admin/retention/policy"));
        policy.GetProperty("stayPaymentProofDays").GetInt32().Should().Be(90);
        policy.GetProperty("stayUnpaidBookingDays").GetInt32().Should().Be(30);
        policy.GetProperty("stayBookingPersonalDataDays").GetInt32().Should().Be(1095);
        policy.GetProperty("stayBookingEventDays").GetInt32().Should().Be(1095);
        policy.GetProperty("stayGuestPushSubscriptionDays").GetInt32().Should().Be(7);
        policy.GetProperty("stayGuestPushNotificationDays").GetInt32().Should().Be(90);
    }

    // ── maxHouses в availablePlans ───────────────────────────────────────────────

    [Fact, TestCase("CY37-132")]
    public async Task AvailablePlans_StaysLine_HaveMaxHouses_UnlimitedOmitsIt_OrdersLineHasNone()
    {
        var company = await CreateStaysCompanyAsync(plan: false);
        await GiveStaysPlanAsync(company.Id, StaysPlans.UpToThreeSeedId);
        var sub = await J(await AuthedClient(company.OwnerToken).GetAsync("/api/billing/subscription?line=Stays"));
        var plans = sub.GetProperty("availablePlans").EnumerateArray().ToList();
        JsonElement Plan(string name) => plans.Single(p => p.GetProperty("name").GetString() == name);
        Plan("Один дом").GetProperty("maxHouses").GetInt32().Should().Be(1);
        Plan("До 3 домов").GetProperty("maxHouses").GetInt32().Should().Be(3);
        Plan("Без ограничения").TryGetProperty("maxHouses", out var unlimited).Should().BeFalse("у «Без ограничения» лимита нет — поле опускается (схема AvailablePlanDto строгая)");
        _ = unlimited;

        var shop = await CreateShopAsync();
        var orders = await J(await AuthedClient(shop.OwnerToken).GetAsync("/api/billing/subscription?line=Orders"));
        var orderPlans = orders.GetProperty("availablePlans").EnumerateArray().ToList();
        orderPlans.Should().NotBeEmpty();
        orderPlans.Should().OnlyContain(p => !HasValue(p, "maxHouses"), "у линейки «Заказы» maxHouses не применим");
    }

    // ── отмена гостем истёкшего удержания ───────────────────────────────────────

    [Fact, TestCase("CY37-133")]
    public async Task GuestCancel_OfExpiredHold_Is409WithTimeToPayExpiredText_AndFinishesTheExpiry()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12), client: host.Client());
        var id = await BookingIdAsync(booked.Token);
        var expires = (await WithDbAsync(db => db.StayBookings.AsNoTracking().Where(b => b.Id == id).Select(b => b.HoldExpiresAtUtc).SingleAsync()))!.Value;
        host.StaysClock.Set(expires.AddSeconds(5)); // таймер вышел, задача снятия ещё не отработала

        var page = await host.Client().GetAsync($"/api/stays/bookings/public/{booked.Token}");
        var cannotCancel = (await J(page)).GetProperty("cancellation").GetProperty("cannotCancelText").GetString();
        cannotCancel.Should().Contain("Время на оплату истекло").And.NotContain("Время заезда наступило");

        var cancel = await host.Client().PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/cancel", new { });
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(cancel);
        body.GetProperty("code").GetString().Should().Be("CancelNotAllowed");
        body.GetProperty("message").GetString().Should().Contain("Время на оплату истекло, бронь снята").And.NotContain("Время заезда наступило");
        body.GetProperty("booking").GetProperty("status").GetString().Should().Be("ExpiredUnpaid", "в теле — реальный статус, не «Удержана»");

        (await WithDbAsync(db => db.StayBookings.AsNoTracking().Where(b => b.Id == id).Select(b => b.Status).SingleAsync())).Should().Be(StayBookingStatus.ExpiredUnpaid);
        (await WithDbAsync(db => db.StayBookingEvents.CountAsync(e => e.StayBookingId == id && e.Kind == StayBookingEventKind.HoldExpired))).Should().Be(1);
        (await CalendarAsync(house.Id, InDays(9), InDays(13), host.Client())).Days.Should().OnlyContain(d => d.State == CalendarDayState.Free, "даты освобождены");

        // повторная отмена уже снятой брони — тот же текст, второго события нет
        var again = await host.Client().PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/cancel", new { });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(again)).GetProperty("message").GetString().Should().Contain("Время на оплату истекло");
        (await WithDbAsync(db => db.StayBookingEvents.CountAsync(e => e.StayBookingId == id && e.Kind == StayBookingEventKind.HoldExpired))).Should().Be(1);
    }

    // ── блокировка: комментарий в шахматке, сохранение при PUT, архивный дом ────

    [Fact, TestCase("CY37-134")]
    public async Task BoardBlock_CarriesComment_PutWithThatCommentKeepsIt_PublicCalendarNeverShowsIt()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var c = AuthedClient(company.OwnerToken);
        var block = await BlockAsync(company, house.Id, InDays(20), InDays(23), HouseBlockKind.Repair, "Меняем печь, мастер Сергей");

        async Task<JsonElement> BoardItem()
        {
            var board = await J(await c.GetAsync($"/api/stays/companies/{company.Id}/board?from={D(InDays(18))}&days=14"));
            return board.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "Block" && i.GetProperty("id").GetGuid() == block.Id);
        }
        (await BoardItem()).GetProperty("comment").GetString().Should().Be("Меняем печь, мастер Сергей");

        // как диалог правки: берёт комментарий из шахматки и отправляет обратно, меняя только даты
        var comment = (await BoardItem()).GetProperty("comment").GetString();
        var put = await c.PutJsonAsync($"/api/stays/companies/{company.Id}/blocks/{block.Id}", new HouseBlockInput(house.Id, InDays(20), InDays(24), HouseBlockKind.Repair, comment));
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        (await put.Content.ReadJsonAsync<HouseBlockDto>())!.Comment.Should().Be("Меняем печь, мастер Сергей");
        var after = await BoardItem();
        after.GetProperty("comment").GetString().Should().Be("Меняем печь, мастер Сергей", "правка дат не стирает комментарий");
        after.GetProperty("endDate").GetString().Should().Be(D(InDays(24)));

        // явная очистка комментария по-прежнему возможна
        var cleared = await c.PutJsonAsync($"/api/stays/companies/{company.Id}/blocks/{block.Id}", new HouseBlockInput(house.Id, InDays(20), InDays(24), HouseBlockKind.Repair, null));
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        var clearedItem = await BoardItem();
        (clearedItem.TryGetProperty("comment", out var cc) ? cc.ValueKind : JsonValueKind.Null).Should().Be(JsonValueKind.Null);

        // у брони комментарий в шахматке пуст, а гость комментарий блокировки не видит никогда
        await PutComment(c, company, block.Id, house.Id, "Секретный комментарий блокировки");
        var pub = await AnonymousClient().GetStringAsync($"/api/stays/public/houses/{house.Id}/calendar?from={D(InDays(18))}&to={D(InDays(30))}");
        pub.Should().NotContain("Секретный комментарий");
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12));
        var items = (await J(await c.GetAsync($"/api/stays/companies/{company.Id}/board?from={D(InDays(8))}&days=14"))).GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("kind").GetString() == "Booking").ToList();
        items.Should().NotBeEmpty();
        items.Should().OnlyContain(i => !HasValue(i, "comment"));
        _ = booked;
    }

    private static async Task PutComment(HttpClient c, StaysCtx company, Guid blockId, Guid houseId, string comment)
    {
        var r = await c.PutJsonAsync($"/api/stays/companies/{company.Id}/blocks/{blockId}", new HouseBlockInput(houseId, InDays(20), InDays(24), HouseBlockKind.Repair, comment));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    [Fact, TestCase("CY37-135")]
    public async Task Block_OnArchivedHouse_Is409HouseArchived_AndNothingIsCreated()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        (await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}/archive", new { })).StatusCode.Should().Be(HttpStatusCode.OK);

        var r = await PostBlockAsync(company, house.Id, InDays(10), InDays(12));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
        (await Code(r)).Should().Be("HouseArchived");
        (await WithDbAsync(db => db.HouseBlocks.CountAsync(b => b.HouseId == house.Id))).Should().Be(0);

        // чужой/несуществующий дом по-прежнему 404, а не 409
        (await PostBlockAsync(company, Guid.NewGuid(), InDays(10), InDays(12))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── ручная бронь не пишет гостю в мессенджер ────────────────────────────────

    [Fact, TestCase("CY37-136")]
    public async Task ManualBooking_WithPhoneAndNotifyTick_NeverQueuesMessengerToGuest_EvenWhenCompanySwitchIsOn()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        await WithDbAsync(async db =>
        {
            var s = await db.StaysSettings.SingleAsync(x => x.CompanyId == company.Id);
            s.GuestMessengerEnabled = true; // переключатель компании включён напрямую: канала нет, но флаг брони решает
            await db.SaveChangesAsync();
        });

        var r = await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/bookings",
            new ManualStayBookingInput(house.Id, InDays(10), InDays(12), 2, 0, 0, false, "Гость по телефону", UniquePhone(), true, null, null));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var card = (await r.Content.ReadJsonAsync<StaffStayBookingCardDto>())!;
        card.IsManual.Should().BeTrue();
        card.Status.Should().Be(StayBookingStatus.Confirmed);

        (await WithDbAsync(db => db.StayBookings.AsNoTracking().Where(b => b.Id == card.Id).Select(b => b.NotifyByMessenger).SingleAsync()))
            .Should().BeFalse("согласия гостя на мессенджер у вручную занесённой брони нет");
        (await WithDbAsync(db => db.OutboundNotifications.CountAsync(n => n.StayBookingId == card.Id))).Should().Be(0);
    }

    // ── гонка: отмена гостем против решения персонала ───────────────────────────

    [Fact, TestCase("CY37-137")]
    public async Task GuestCancel_RacingStaffRejectPayment_ExactlyOneWins_NoServerErrors_JournalAndCalendarAgree()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        for (var round = 0; round < 12; round++)
        {
            var ci = InDays(10 + round * 3);
            var booked = await BookOkAsync(house.Id, ci, ci.AddDays(2));
            var id = await BookingIdAsync(booked.Token);
            await AttachProofOkAsync(booked.Token);
            var version = (await StaffCardAsync(company, id)).Version;

            var gate = new TaskCompletionSource();
            var guest = Task.Run(async () => { await gate.Task; return await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/cancel", new { }); });
            var staff = Task.Run(async () => { await gate.Task; return await StaffActionAsync(company, id, "reject-payment", version, "Оплата не пришла"); });
            gate.SetResult();
            var (g, s) = (await guest, await staff);

            new[] { g.StatusCode, s.StatusCode }.Should().OnlyContain(x => x == HttpStatusCode.OK || x == HttpStatusCode.Conflict, $"раунд {round}: g={g.StatusCode} s={s.StatusCode}");
            (g.StatusCode == HttpStatusCode.OK ^ s.StatusCode == HttpStatusCode.OK).Should().BeTrue($"раунд {round}: ровно один исход (g={g.StatusCode}, s={s.StatusCode})");
            var final = await StaffCardAsync(company, id);
            final.Status.Should().Be(g.StatusCode == HttpStatusCode.OK ? StayBookingStatus.CancelledByGuest : StayBookingStatus.PaymentRejected, $"раунд {round}");
            final.Events.Count(e => e.Kind is nameof(StayBookingEventKind.CancelledByGuest) or nameof(StayBookingEventKind.PaymentRejected)).Should().Be(1, $"раунд {round}: в журнале одно решение");
            (await CalendarAsync(house.Id, ci.AddDays(-1), ci.AddDays(3))).Days.Should().OnlyContain(d => d.State == CalendarDayState.Free, $"раунд {round}: даты свободны");
        }
    }

    [Fact, TestCase("CY37-138")]
    public async Task GuestCancel_RacingStaffConfirmPayment_NoServerErrors_FinalStateMatchesTheResponses()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        for (var round = 0; round < 12; round++)
        {
            var ci = InDays(10 + round * 3);
            var booked = await BookOkAsync(house.Id, ci, ci.AddDays(2));
            var id = await BookingIdAsync(booked.Token);
            await AttachProofOkAsync(booked.Token);
            var version = (await StaffCardAsync(company, id)).Version;

            var gate = new TaskCompletionSource();
            var guest = Task.Run(async () => { await gate.Task; return await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/cancel", new { }); });
            var staff = Task.Run(async () => { await gate.Task; return await StaffActionAsync(company, id, "confirm-payment", version); });
            gate.SetResult();
            var (g, s) = (await guest, await staff);

            new[] { g.StatusCode, s.StatusCode }.Should().OnlyContain(x => x == HttpStatusCode.OK || x == HttpStatusCode.Conflict, $"раунд {round}: g={g.StatusCode} s={s.StatusCode}");
            var final = await StaffCardAsync(company, id);
            if (g.StatusCode == HttpStatusCode.OK)
            {
                final.Status.Should().Be(StayBookingStatus.CancelledByGuest, $"раунд {round}: отмена применена — бронь отменена");
                (await CalendarAsync(house.Id, ci.AddDays(-1), ci.AddDays(3))).Days.Should().OnlyContain(d => d.State == CalendarDayState.Free, $"раунд {round}");
            }
            else
            {
                s.StatusCode.Should().Be(HttpStatusCode.OK, $"раунд {round}: отмена отклонена — значит подтверждение прошло");
                final.Status.Should().Be(StayBookingStatus.Confirmed);
                (await CalendarAsync(house.Id, ci, ci.AddDays(2))).Days.Should().Contain(d => d.State == CalendarDayState.Occupied);
            }
            final.Events.Count(e => e.Kind == nameof(StayBookingEventKind.PaymentConfirmed)).Should().Be(s.StatusCode == HttpStatusCode.OK ? 1 : 0, $"раунд {round}");
        }
    }
}
