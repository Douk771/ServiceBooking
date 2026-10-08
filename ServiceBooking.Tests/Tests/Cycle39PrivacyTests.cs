using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Retention;
using ServiceBooking.API.Services.Retention.Rules;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39, «Вызов 2»: персональные данные услуг (US-39-27, ЮР39-5, ЮР39-9, Т39-10/11; ARCHITECTURE_CYCLE39.md §39.13): выгрузка данных субъекта с сеансами и заказами, удаление
/// аккаунта (обезличивание, расписание владельца цело), отзыв согласия, retention (5 новых правил + расширенные), стирание снимка напоминания, график без лишнего.
/// </summary>
public class Cycle39PrivacyTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private async Task<RetentionOutcome> RunRuleAsync<T>(Func<AppDbContext, FileStorage, T> create, bool dryRun, DateTime? now = null) where T : IRetentionRule
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
        var periods = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RetentionPeriods>>().Value;
        return await create(db, storage).ApplyAsync(new RetentionContext(now ?? DateTime.UtcNow, periods, 100, dryRun), CancellationToken.None);
    }

    private async Task<(StaysCtx Company, SvcCtx Svc)> SceneAsync(int? prepay = null)
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: prepay);
        return (company, svc);
    }

    // ── выгрузка ─────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-150")]
    public async Task Export_ContainsServiceOrders_AndSessionsOfBookings_GuestOrdersOnlyForVerifiedPhone()
    {
        var (company, svc) = await SceneAsync(prepay: 30);
        var house = await CreateHouseAsync(company, price: 2000);
        var guest = await RegisterAsync(firstName: "Ольга", lastName: "Гостева");
        var broom = await AddItemAsync(company, svc.Id, "Веник", 300, 5);
        var date = InDays(10);

        var mine = await OrderOkAsync(svc.Id, date, 720, 2, client: AuthedClient(guest.Token), name: "Ольга Гостева", items: [new ItemSelectionInput(broom.Id, 1)]);
        await AttachOrderProofAsync(mine.Token, AuthedClient(guest.Token));
        var asGuest = await OrderOkAsync(svc.Id, date, 1080, 2, phone: guest.Phone, name: "Ольга по телефону");
        var booking = await BookOkAsync(house.Id, InDays(12), InDays(14), client: AuthedClient(guest.Token), name: "Ольга Гостева");
        await AddSessionOkAsync(booking.Token, svc.Id, InDays(13), 720, 2);

        var before = await J(await AuthedClient(guest.Token).GetAsync("/api/profile/export"));
        var orders = before.GetProperty("stayServiceOrders").EnumerateArray().ToList();
        orders.Should().ContainSingle("заказ на тот же номер без подтверждённого телефона не подтягивается (SUBJECT-PHONE-GATE)");
        var order = orders[0];
        order.GetProperty("serviceName").GetString().Should().Be("Баня");
        order.GetProperty("companyName").GetString().Should().Be(company.Company.Name);
        order.GetProperty("guestName").GetString().Should().Be("Ольга Гостева");
        order.GetProperty("guestPhone").GetString().Should().NotBeNullOrEmpty();
        order.GetProperty("totalRub").GetInt32().Should().Be(4300);
        order.GetProperty("prepayRub").GetInt32().Should().Be(1200);
        order.GetProperty("timeLabel").GetString().Should().MatchRegex(@"^\p{L}{2} \d{1,2} \p{L}+, 12:00 — 14:00$");
        order.GetProperty("orderUrl").GetString().Should().StartWith("https://dom.ezbook.ru/s/");
        order.GetProperty("paymentProofs").EnumerateArray().Should().ContainSingle();
        order.GetProperty("events").EnumerateArray().Should().NotBeEmpty();
        var bookingExport = before.GetProperty("stayBookings").EnumerateArray().Single();
        bookingExport.GetProperty("sessions").EnumerateArray().Should().ContainSingle().Which.GetProperty("serviceName").GetString().Should().Be("Баня");

        await MarkPhoneVerifiedAsync(guest.Phone, guest.UserId);
        var after = await J(await AuthedClient(guest.Token).GetAsync("/api/profile/export"));
        after.GetProperty("stayServiceOrders").EnumerateArray().Should().HaveCount(2, "с подтверждённым номером подтягивается и гостевой заказ");
        var raw = after.GetProperty("stayServiceOrders").GetRawText();
        raw.Should().NotContain("storageKey").And.NotContain("StorageKey").And.NotContain(company.Owner.FirstName + " " + company.Owner.LastName, "имён персонала в выгрузке нет");
        _ = asGuest;
    }

    // ── удаление аккаунта ────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-151")]
    public async Task DeleteAccount_ErasesServiceOrders_KeepsOwnersScheduleAndMoney_RemovesProofFilesAndPush()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (company, svc) = await SceneAsync(prepay: 30);
        var guest = await RegisterAsync(firstName: "Ольга", lastName: "Гостева");
        var date = InDays(10);
        var created = await OrderOkAsync(svc.Id, date, 720, 2, client: host.Client(guest.Token), name: "Ольга Гостева");
        (await AttachOrderProofAsync(created.Token, host.Client(guest.Token))).StatusCode.Should().Be(HttpStatusCode.Created);
        var sessionId = await SessionIdOfOrderAsync(created.Token);
        var card = await SessionCardAsync(company, sessionId);
        await SessionActionAsync(company, sessionId, "confirm-payment", card.Version);
        var key = await WithDbAsync(db => db.StayPaymentProofs.Where(p => p.StayServiceOrderId == db.StayServiceOrders.Where(o => o.PublicToken == created.Token).Select(o => o.Id).First()).Select(p => p.StorageKey).SingleAsync());
        key.Should().NotBeNull();
        (await host.Client(guest.Token).PostJsonAsync($"/api/stays/service-orders/public/{created.Token}/push-subscription",
            new PushSubscriptionInput($"https://push.example.test/cy39-del/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var preview = await J(await AuthedClient(guest.Token).GetAsync("/api/profile/delete-account/preview"));
        preview.GetProperty("stayServiceOrders").GetInt32().Should().Be(1);
        var del = await AuthedClient(guest.Token).PostJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        del.StatusCode.Should().Be(HttpStatusCode.NoContent, await del.Content.ReadAsStringAsync());

        var after = await SessionCardAsync(company, sessionId);
        after.GuestName.Should().BeNull();
        after.GuestPhone.Should().BeNull();
        after.Comment.Should().BeNull();
        after.OrderStatus.Should().Be(StayBookingStatus.Confirmed, "активный заказ не отменяется");
        after.TotalRub.Should().Be(4000);
        after.PaymentConfirmed.Should().NotBeNull("факт оплаты остаётся");
        after.PaymentProofs.Should().OnlyContain(p => p.Purged, "файлы подтверждений удаляются сразу");
        after.Events.Where(e => e.ActorText.Contains("Ольга") || e.ActorText.Contains("Гостева")).Should().BeEmpty("имя гостя в журнале заменено");
        after.AddedBy.Name.Should().NotContain("Ольга", "в карточке сеанса имя гостя после стирания не показывается");
        (await ActiveSessionsAsync(svc.Id)).Should().ContainSingle("расписание владельца цело");
        (await StartsAsync(svc.Id, date)).Starts.Select(s => s.StartMinute).Should().NotContain(720);
        var stored = await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.PublicToken == created.Token));
        stored.PersonalDataErased.Should().BeTrue();
        stored.GuestUserId.Should().BeNull();
        (await WithDbAsync(db => db.StayGuestPushSubscriptions.CountAsync(s => s.StayServiceOrderId == stored.Id))).Should().Be(0);
        using var scope = Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
        var open = () => storage.OpenPrivate(key!);
        open.Should().Throw<FileNotFoundException>("файл удалён с диска");

        // ПДн в самой строке сеанса: имя и аккаунт автора сеанса (гость) не должны пережить стирание
        var session = (await AllSessionsAsync(svc.Id)).Single();
        session.AddedByNameSnapshot.Should().NotContain("Ольга", "ПДн субъекта: имя гостя в строке сеанса стирается вместе с заказом");
        session.AddedByUserId.Should().BeNull("идентификатор удалённого аккаунта в строке сеанса не остаётся");
    }

    [Fact, TestCase("CY39-152")]
    public async Task DeleteAccount_ErasesSessionsAuthor_BookingReminderSnapshot_AndPageStaysConsistentForOwner()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var svc = await CreateServiceAsync(company);
        await PutReminderAsync(company, "{ИмяГостя}, ждём вас!");
        var guest = await RegisterAsync(firstName: "Ольга", lastName: "Гостева");
        var arrival = InDays(5);
        var booked = await BookOkAsync(house.Id, arrival, arrival.AddDays(2), client: host.Client(guest.Token), name: "Ольга Гостева");
        var id = await BookingIdAsync(booked.Token);
        var withSession = await AddSessionViaClientAsync(host.Client(guest.Token), booked.Token, svc.Id, arrival.AddDays(1), 720, 2);
        withSession.Sessions.Should().ContainSingle();
        await AttachProofOkAsync(booked.Token, host.Client());
        await StaffActionOkAsync(company, id, "confirm-payment", (await StaffCardAsync(company, id)).Version);
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 18, 5));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await GetPublicBookingAsync(booked.Token, host.Client())).ArrivalReminder!.Text.Should().Contain("Ольга Гостева");

        (await AuthedClient(guest.Token).PostJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await WithDbAsync(db => db.StayBookings.AsNoTracking().SingleAsync(b => b.Id == id));
        stored.PersonalDataErased.Should().BeTrue();
        stored.ArrivalReminderPageText.Should().BeNull("снимок напоминания содержит имя гостя — стирается вместе с бронью (ЮР39-5)");
        (await GetPublicBookingAsync(booked.Token, host.Client())).ArrivalReminder.Should().BeNull();
        var session = (await AllSessionsAsync(svc.Id)).Single();
        session.AddedByNameSnapshot.Should().NotContain("Ольга");
        session.AddedByUserId.Should().BeNull();
        (await ActiveSessionsAsync(svc.Id)).Should().ContainSingle("расписание владельца цело");
        var cardAfter = await StaffCardAsync(company, id);
        cardAfter.Sessions.Should().ContainSingle();
        var json = JsonSerializer.Serialize(cardAfter, JsonHelpers.Options);
        json.Should().NotContain("Ольга").And.NotContain("Гостева");
    }

    private async Task PutReminderAsync(StaysCtx company, string template)
    {
        var r = await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/arrival-reminder", new ArrivalReminderInput("18:00", template, false, true, "v1", null));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    private async Task<PublicStayBookingDto> AddSessionViaClientAsync(HttpClient client, string token, Guid serviceId, DateOnly date, int start, int hours)
    {
        var q = await client.PostJsonAsync($"/api/stays/bookings/public/{token}/services/{serviceId}/quote", new PublicServiceQuoteInput(date, start, hours, [], null, null, null));
        var total = (await q.Content.ReadJsonAsync<ServiceQuoteDto>())!.TotalRub;
        var r = await AddSessionAsync(token, SessionInput(serviceId, date, start, hours, total), client);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<PublicStayBookingDto>())!;
    }

    // ── отзыв согласия ───────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-153")]
    public async Task RevokeConsent_SwitchesOffMessengerOfActiveServiceOrders_PreviewAgrees()
    {
        var (company, svc) = await SceneAsync(prepay: 30);
        var guest = await RegisterAsync();
        var created = await OrderOkAsync(svc.Id, InDays(10), 720, 2, client: AuthedClient(guest.Token));
        await WithDbAsync(async db =>
        {
            var o = await db.StayServiceOrders.SingleAsync(x => x.PublicToken == created.Token);
            o.NotifyByMessenger = true;
            o.MessengerConsentAtUtc = DateTime.UtcNow;
            o.MessengerConsentVersion = "v-test";
            await db.SaveChangesAsync();
        });
        var preview = await J(await AuthedClient(guest.Token).GetAsync("/api/profile/consents/revoke-preview"));
        var expected = preview.GetProperty("stayBookingsMessengerDisabled").GetInt32();
        expected.Should().BeGreaterOrEqualTo(1);
        var revoke = await AuthedClient(guest.Token).PostJsonAsync("/api/profile/consents/revoke", new { documentKey = "PdnConsent", purpose = (string?)null, reason = "не хочу" });
        revoke.StatusCode.Should().Be(HttpStatusCode.OK, await revoke.Content.ReadAsStringAsync());
        (await J(revoke)).GetProperty("effects").GetProperty("stayBookingsMessengerDisabled").GetInt32().Should().Be(expected, "предпросмотр и факт совпадают");
        var stored = await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.PublicToken == created.Token));
        stored.NotifyByMessenger.Should().BeFalse();
        stored.MessengerConsentVersion.Should().Be("v-test", "снимок согласия — правовые данные, не удаляется");
        stored.Status.Should().Be(StayBookingStatus.Held, "заказ не отменяется");
        _ = company;
    }

    // ── retention ────────────────────────────────────────────────────────────────

    private async Task<(string Token, Guid OrderId, Guid SessionId)> TerminalOrderAsync(StaysCtx company, SvcCtx svc, int shift, bool unpaid, bool withProof = true)
    {
        var created = await OrderOkAsync(svc.Id, InDays(10 + shift), 720, 2, name: "Retention Гость");
        var orderId = await OrderIdAsync(created.Token);
        if (withProof) await AttachOrderProofAsync(created.Token);
        var sessionId = await SessionIdOfOrderAsync(created.Token);
        if (unpaid)
        {
            await WithDbAsync(async db =>
            {
                var o = await db.StayServiceOrders.SingleAsync(x => x.Id == orderId);
                o.Status = StayBookingStatus.ExpiredUnpaid;
                o.TerminalAtUtc = DateTime.UtcNow.AddDays(-31);
                await db.SaveChangesAsync();
            });
        }
        else
        {
            var card = await SessionCardAsync(company, sessionId);
            await SessionActionAsync(company, sessionId, "confirm-payment", card.Version);
        }
        return (created.Token, orderId, sessionId);
    }

    private async Task MoveSessionAsync(Guid sessionId, Guid orderId, int endedDaysAgo, int? terminalDaysAgo)
    {
        await WithDbAsync(async db =>
        {
            var s = await db.StayServiceSessions.SingleAsync(x => x.Id == sessionId);
            s.EndUtc = DateTime.UtcNow.AddDays(-endedDaysAgo);
            s.StartUtc = s.EndUtc.AddHours(-2);
            s.OccupiedUntilUtc = s.EndUtc.AddMinutes(30);
            var o = await db.StayServiceOrders.SingleAsync(x => x.Id == orderId);
            o.TerminalAtUtc = terminalDaysAgo is { } t ? DateTime.UtcNow.AddDays(-t) : o.TerminalAtUtc;
            await db.SaveChangesAsync();
        });
    }

    [Fact, TestCase("CY39-154")]
    public async Task Retention_OrderPaymentProofs_After90DaysFromLaterOfSessionEndAndFinalStatus_DryRunChangesNothing()
    {
        var (company, svc) = await SceneAsync(prepay: 30);
        var a = await TerminalOrderAsync(company, svc, 0, unpaid: false);
        var b = await TerminalOrderAsync(company, svc, 1, unpaid: false);
        var keyA = await WithDbAsync(db => db.StayPaymentProofs.Where(p => p.StayServiceOrderId == a.OrderId).Select(p => p.StorageKey).SingleAsync());
        var keyB = await WithDbAsync(db => db.StayPaymentProofs.Where(p => p.StayServiceOrderId == b.OrderId).Select(p => p.StorageKey).SingleAsync());
        // A: сеанс закончился 100 дней назад, статус — 100 дней назад → удаляется. B: сеанс 100 дней назад, но статус — 10 дней назад → отсчёт от более поздней даты, остаётся
        await MoveSessionAsync(a.SessionId, a.OrderId, 100, 100);
        await MoveSessionAsync(b.SessionId, b.OrderId, 101, 10);

        var dry = await RunRuleAsync((db, st) => new StayServiceOrderPaymentProofRule(db, st), dryRun: true);
        dry.Affected.Should().BeGreaterOrEqualTo(1);
        (await WithDbAsync(db => db.StayPaymentProofs.CountAsync(p => (p.StayServiceOrderId == a.OrderId || p.StayServiceOrderId == b.OrderId) && p.StorageKey != null))).Should().Be(2, "сухой прогон ничего не меняет");

        await RunRuleAsync((db, st) => new StayServiceOrderPaymentProofRule(db, st), dryRun: false);
        (await WithDbAsync(db => db.StayPaymentProofs.SingleAsync(p => p.StayServiceOrderId == a.OrderId))).StorageKey.Should().BeNull();
        (await WithDbAsync(db => db.StayPaymentProofs.SingleAsync(p => p.StayServiceOrderId == b.OrderId))).StorageKey.Should().Be(keyB, "статус свежее 90 дней — файл остаётся");
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.Id == a.OrderId))).PaymentProofsPurgedAtUtc.Should().NotBeNull();
        using var scope = Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
        var gone = () => storage.OpenPrivate(keyA!);
        gone.Should().Throw<FileNotFoundException>();
    }

    [Fact, TestCase("CY39-155")]
    public async Task Retention_UnpaidOrders_Depersonalised30Days_ConfirmedAfter3Years_OthersUntouched()
    {
        var (company, svc) = await SceneAsync(prepay: 30);
        var unpaid = await TerminalOrderAsync(company, svc, 0, unpaid: true, withProof: false);
        var fresh = await TerminalOrderAsync(company, svc, 1, unpaid: true, withProof: false);
        await WithDbAsync(async db =>
        {
            var o = await db.StayServiceOrders.SingleAsync(x => x.Id == fresh.OrderId);
            o.TerminalAtUtc = DateTime.UtcNow.AddDays(-5);
            await db.SaveChangesAsync();
        });
        var confirmedOld = await TerminalOrderAsync(company, svc, 2, unpaid: false, withProof: false);
        var confirmedRecent = await TerminalOrderAsync(company, svc, 3, unpaid: false, withProof: false);
        await MoveSessionAsync(confirmedOld.SessionId, confirmedOld.OrderId, 1200, 1200);
        await MoveSessionAsync(confirmedRecent.SessionId, confirmedRecent.OrderId, 400, 400);

        var dry = await RunRuleAsync((db, st) => new StayServiceOrderUnpaidPersonalizationRule(db), dryRun: true);
        dry.Affected.Should().BeGreaterOrEqualTo(1);
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.Id == unpaid.OrderId))).GuestName.Should().NotBeNull("сухой прогон ничего не меняет");
        await RunRuleAsync((db, st) => new StayServiceOrderUnpaidPersonalizationRule(db), dryRun: false);
        await RunRuleAsync((db, st) => new StayServiceOrderPersonalizationRule(db), dryRun: false);

        async Task<StayServiceOrder> Get(Guid id) => await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.Id == id));
        var u = await Get(unpaid.OrderId);
        (u.PersonalDataErased, u.GuestName, u.GuestPhone, u.Comment).Should().Be((true, null, null, null));
        (await Get(fresh.OrderId)).GuestName.Should().NotBeNull("неоплаченному меньше 30 дней — не трогаем");
        (await Get(confirmedOld.OrderId)).PersonalDataErased.Should().BeTrue("прочее — через 3 года");
        (await Get(confirmedRecent.OrderId)).PersonalDataErased.Should().BeFalse("400 дней — рано");
        var session = await WithDbAsync(db => db.StayServiceSessions.AsNoTracking().SingleAsync(s => s.Id == unpaid.SessionId));
        session.ServiceNameSnapshot.Should().Be("Баня", "расписание и деньги остаются");
        session.AddedByNameSnapshot.Should().NotContain("Retention", "ПДн гостя в строке сеанса стираются вместе с заказом");
    }

    [Fact, TestCase("CY39-156")]
    public async Task Retention_EventsAndScheduleJournal_After3Years_RulesRegisteredByName()
    {
        var (company, svc) = await SceneAsync();
        var order = await OrderOkAsync(svc.Id, InDays(10), 720, 2);
        var orderId = await OrderIdAsync(order.Token);
        await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/date-overrides/{D(InDays(30))}", new DateOverrideInput(true, [], "Закрыто"));
        await WithDbAsync(async db =>
        {
            foreach (var e in db.StayServiceOrderEvents.Where(e => e.StayServiceOrderId == orderId)) e.OccurredAtUtc = DateTime.UtcNow.AddDays(-1200);
            foreach (var e in db.StayServiceScheduleEvents.Where(e => e.ServiceId == svc.Id)) e.OccurredAtUtc = DateTime.UtcNow.AddDays(-1200);
            await db.SaveChangesAsync();
        });
        await RunRuleAsync((db, st) => new StayServiceOrderEventRule(db), dryRun: false);
        await RunRuleAsync((db, st) => new StayServiceScheduleEventRule(db), dryRun: false);
        (await WithDbAsync(db => db.StayServiceOrderEvents.CountAsync(e => e.StayServiceOrderId == orderId))).Should().Be(0);
        (await WithDbAsync(db => db.StayServiceScheduleEvents.CountAsync(e => e.ServiceId == svc.Id))).Should().Be(0);

        using var scope = Factory.Services.CreateScope();
        var rules = scope.ServiceProvider.GetServices<IRetentionRule>().Select(r => r.Name).ToList();
        rules.Should().Contain(["stay-service-order-payment-proofs", "stay-service-order-unpaid-personalization", "stay-service-order-personalization", "stay-service-order-events", "stay-service-schedule-events"],
            "пять новых правил цикла 39 зарегистрированы поимённо");
        var periods = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RetentionPeriods>>().Value;
        periods.StayServiceOrderUnpaidDays.Should().Be(30);
        periods.StayServiceOrderPersonalDataDays.Should().Be(1095);
    }

    [Fact, TestCase("CY39-157")]
    public async Task Retention_BookingUnpaidAndPersonalization_EraseReminderSnapshot()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        await PutReminderAsync(company, "{ИмяГостя}, ждём вас!");
        var arrival = InDays(5);
        var booked = await BookOkAsync(house.Id, arrival, arrival.AddDays(2), name: "Снимок Гость", client: host.Client());
        var id = await BookingIdAsync(booked.Token);
        await AttachProofOkAsync(booked.Token, host.Client());
        await StaffActionOkAsync(company, id, "confirm-payment", (await StaffCardAsync(company, id)).Version);
        host.StaysClock.Set(LocalToUtc(arrival.AddDays(-1), 18, 5));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await WithDbAsync(db => db.StayBookings.AsNoTracking().Where(b => b.Id == id).Select(b => b.ArrivalReminderPageText).SingleAsync())).Should().Contain("Снимок Гость");
        await WithDbAsync(async db =>
        {
            var b = await db.StayBookings.SingleAsync(x => x.Id == id);
            b.CheckOutDate = InDays(-1500);
            b.CheckInDate = b.CheckOutDate.AddDays(-2);
            b.TerminalAtUtc = DateTime.UtcNow.AddDays(-1500);
            await db.SaveChangesAsync();
        });
        using var scope = Factory.Services.CreateScope();
        var rule = scope.ServiceProvider.GetServices<IRetentionRule>().Where(r => r.Name.Contains("stay-") && r.Name.Contains("personal")).ToList();
        foreach (var r in rule)
            await r.ApplyAsync(new RetentionContext(DateTime.UtcNow, scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RetentionPeriods>>().Value, 100, false), CancellationToken.None);
        var stored = await WithDbAsync(db => db.StayBookings.AsNoTracking().SingleAsync(b => b.Id == id));
        stored.PersonalDataErased.Should().BeTrue();
        stored.ArrivalReminderPageText.Should().BeNull("снимок текста напоминания обезличивается вместе с бронью");
    }

    // ── график без лишнего ───────────────────────────────────────────────────────

    [Fact, TestCase("CY39-158")]
    public async Task Schedule_Housekeeper_SessionShape_HasNoPhoneSumsOrPayments_CommentOnlyWhenOwnerAllows()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: null);
        var hk = await AddStaffAsync(company, "Housekeeper");
        var date = InDays(8);
        var created = await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, 4000, "+79054441122", "Тайный Гость", comment: "Аллергия на мёд"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var raw = await AuthedClient(hk.Token).GetStringAsync($"/api/stays/companies/{company.Id}/schedule?from={D(date)}&days=3");
        raw.Should().Contain("Тайный Гость").And.NotContain("9054441122").And.NotContain("Аллергия").And.NotContain("totalRub").And.NotContain("prepay").And.NotContain("4000");
        // владелец включил «горничная видит комментарий» (ЮР-5)
        var settings = (await GetCompanyAsync(company)).Settings!;
        await PutSettingsAsync(company, settings with { HousekeeperSeesGuestComment = true });
        var rawWith = await AuthedClient(hk.Token).GetStringAsync($"/api/stays/companies/{company.Id}/schedule?from={D(date)}&days=3");
        rawWith.Should().Contain("Аллергия на мёд").And.NotContain("9054441122").And.NotContain("totalRub");
    }
}
