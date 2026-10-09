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
/// Цикл 42, «Бани»: персональные данные и хранение на банной компании (CY42-80…85). Писано по SPEC_CYCLE42_BANI.md (US-42-25), API_CONTRACT_CYCLE42.md §42.37.1 и
/// LEGAL_REVIEW_CYCLE42.md §6 (число гостей), без чтения реализации: выгрузка (<c>guestsCount</c>, <c>site = Baths</c>, <c>orderUrl</c> на bani), <c>SUBJECT-PHONE-GATE</c> на
/// неподтверждённом номере, удаление аккаунта (обезличивание, число гостей остаётся), отзыв согласия <c>ProviderDelivery</c>, правила retention на брони бань.
/// </summary>
public class Cycle42PrivacyTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private async Task<RetentionOutcome> RunRuleAsync<T>(Func<AppDbContext, FileStorage, T> create, bool dryRun) where T : IRetentionRule
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
        var periods = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RetentionPeriods>>().Value;
        return await create(db, storage).ApplyAsync(new RetentionContext(DateTime.UtcNow, periods, 100, dryRun), CancellationToken.None);
    }

    private Task<HttpResponseMessage> BathProofAsync(string token, HttpClient? client = null) =>
        (client ?? AnonymousClient()).PostAsync($"/api/baths/service-orders/public/{token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "check.jpg"));

    private async Task<JsonElement> BathCardAsync(BathCtx c, Guid sessionId)
    {
        var r = await AuthedClient(c.Token).GetAsync($"/api/baths/companies/{c.CompanyId}/service-sessions/{sessionId}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await J(r);
    }

    private async Task ConfirmPaymentAsync(BathCtx c, Guid sessionId)
    {
        var card = await BathCardAsync(c, sessionId);
        var r = await AuthedClient(c.Token).PostJsonAsync($"/api/baths/companies/{c.CompanyId}/service-sessions/{sessionId}/confirm-payment", new ExpectedVersionInput(card.GetProperty("version").GetInt32()));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    private async Task<(BathCtx Company, ResCtx Res)> SceneAsync(int? prepay = 30)
    {
        var c = await CreateBathAsync(await NthCityIdAsync(50));
        var res = await AddResourceAsync(c, "Баня ПДн", prepay: prepay);
        return (c, res);
    }

    private async Task<string> BookAsGuestAsync(ResCtx res, HttpClient client, DateOnly date, int start, int guests, string phone, string name = "Ольга Гостева", string? comment = null)
    {
        var quote = await J(await BathQuoteAsync(res.Id, date, start, 2, client));
        var r = await client.PostJsonAsync($"/api/baths/public/services/{res.Id}/orders", BathOrderBody(date, start, 2, guests, quote.GetProperty("totalRub").GetInt32(), phone, name: name, comment: comment));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await J(r)).GetProperty("token").GetString()!;
    }

    // ── CY42-80: выгрузка ───────────────────────────────────────────────────────

    [Fact, TestCase("CY42-80")]
    public async Task Export_BathOrder_HasGuestsCount_SiteBaths_AndOrderUrlOnBani_StaysOrderKeepsItsShape()
    {
        var (c, res) = await SceneAsync();
        var guest = await RegisterAsync(firstName: "Ольга", lastName: "Гостева");
        var guestClient = AuthedClient(guest.Token);
        var bathToken = await BookAsGuestAsync(res, guestClient, InDays(10), 720, guests: 5, phone: guest.Phone, comment: "Нужны полотенца");
        (await BathProofAsync(bathToken, guestClient)).StatusCode.Should().Be(HttpStatusCode.Created);

        // заказ «Дома» на тот же аккаунт: свой сайт и без числа гостей
        var staysCompany = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(staysCompany);
        var staysSvc = await CreateServiceAsync(staysCompany, prepay: 30);
        var staysOrder = await OrderOkAsync(staysSvc.Id, InDays(11), 720, 2, client: guestClient, name: "Ольга Гостева");

        var export = await J(await guestClient.GetAsync("/api/profile/export"));
        var orders = export.GetProperty("stayServiceOrders").EnumerateArray().ToList();
        orders.Should().HaveCount(2);
        var bath = orders.Single(o => o.GetProperty("companyName").GetString() == c.Name);
        bath.GetProperty("guestsCount").GetInt32().Should().Be(5, "число гостей входит в выгрузку (LEGAL_REVIEW_CYCLE42 §6.2 п. 3)");
        bath.GetProperty("site").GetString().Should().Be("Baths");
        bath.GetProperty("orderUrl").GetString().Should().StartWith("https://bani.ezbook.ru/s/").And.EndWith(bathToken);
        bath.GetProperty("serviceName").GetString().Should().Be("Баня ПДн");
        bath.GetProperty("guestName").GetString().Should().Be("Ольга Гостева");
        bath.GetProperty("guestPhone").GetString().Should().NotBeNullOrEmpty();
        bath.GetProperty("comment").GetString().Should().Be("Нужны полотенца");
        bath.GetProperty("hours").GetInt32().Should().Be(2);
        bath.GetProperty("totalRub").GetInt32().Should().Be(4000);
        bath.GetProperty("prepayRub").GetInt32().Should().Be(1200);
        bath.GetProperty("timeLabel").GetString().Should().MatchRegex(@"^\p{L}{2} \d{1,2} \p{L}+, 12:00 — 14:00$");
        bath.GetProperty("paymentProofs").EnumerateArray().Should().ContainSingle();
        bath.GetProperty("events").EnumerateArray().Should().NotBeEmpty();
        bath.GetRawText().Should().NotContain("storageKey", "ключей хранилища в выгрузке нет").And.NotContain(c.Owner.FirstName + " " + c.Owner.LastName, "имён персонала в выгрузке нет");

        var dom = orders.Single(o => o.GetProperty("companyName").GetString() == staysCompany.Company.Name);
        dom.GetProperty("site").GetString().Should().Be("Stays");
        dom.GetProperty("orderUrl").GetString().Should().StartWith("https://dom.ezbook.ru/s/").And.EndWith(staysOrder.Token);
        (!dom.TryGetProperty("guestsCount", out var g) || g.ValueKind == JsonValueKind.Null).Should().BeTrue("у услуг «Домов» числа гостей нет");

        // чужой аккаунт чужих броней бани в выгрузке не получает
        var stranger = await RegisterAsync();
        (await J(await AuthedClient(stranger.Token).GetAsync("/api/profile/export"))).GetProperty("stayServiceOrders").GetArrayLength().Should().Be(0);
    }

    // ── CY42-81: SUBJECT-PHONE-GATE ─────────────────────────────────────────────

    [Fact, TestCase("CY42-81")]
    public async Task SubjectPhoneGate_UnconfirmedNumber_SeesNothingOfGuestBathOrders_ConfirmedSeesOnlyOwnNumber()
    {
        var (_, res) = await SceneAsync(prepay: null);
        var guest = await RegisterAsync(firstName: "Ольга", lastName: "Гостева");
        var guestClient = AuthedClient(guest.Token);

        // гостевая бронь на номер аккаунта (аноним), бронь на чужой номер и бронь самого аккаунта
        var byPhone = await BookAsGuestAsync(res, AnonymousClient(), InDays(10), 720, 3, guest.Phone, name: "Аноним по телефону");
        var foreign = await BookAsGuestAsync(res, AnonymousClient(), InDays(10), 900, 2, UniquePhone(), name: "Чужой Гость");
        var own = await BookAsGuestAsync(res, guestClient, InDays(11), 720, 4, guest.Phone, name: "Ольга Гостева");

        // 1. номер не подтверждён: по номеру ничего не подтягивается — ни в выгрузке, ни в «Мои брони»
        var exportBefore = (await J(await guestClient.GetAsync("/api/profile/export"))).GetProperty("stayServiceOrders").EnumerateArray().ToList();
        exportBefore.Should().ContainSingle("гостевая бронь на тот же номер без подтверждения не подтягивается").Which.GetProperty("orderUrl").GetString().Should().EndWith(own);
        var myBefore = (await J(await guestClient.GetAsync("/api/baths/service-orders/my"))).GetRawText();
        myBefore.Should().Contain(own).And.NotContain(byPhone).And.NotContain(foreign);
        (await J(await guestClient.GetAsync("/api/profile/delete-account/preview"))).GetProperty("stayServiceOrders").GetInt32().Should().Be(1);

        // 2. номер подтверждён: добавляется бронь на ЭТОТ номер, чужой номер не появляется никогда
        await MarkPhoneVerifiedAsync(guest.Phone, guest.UserId);
        var exportAfter = (await J(await guestClient.GetAsync("/api/profile/export"))).GetProperty("stayServiceOrders").EnumerateArray()
            .Select(o => o.GetProperty("orderUrl").GetString()!).ToList();
        exportAfter.Should().HaveCount(2);
        exportAfter.Should().Contain(u => u.EndsWith(own)).And.Contain(u => u.EndsWith(byPhone)).And.NotContain(u => u.EndsWith(foreign));
        var myAfter = (await J(await guestClient.GetAsync("/api/baths/service-orders/my"))).GetRawText();
        myAfter.Should().Contain(own).And.Contain(byPhone).And.NotContain(foreign);
        // удаление аккаунта обезличивает брони аккаунта; гостевые брони на номер — по правилам хранения (поведение «Домов» цикла 39 не меняется)
        (await J(await guestClient.GetAsync("/api/profile/delete-account/preview"))).GetProperty("stayServiceOrders").GetInt32().Should().Be(1);

        // 3. чужой аккаунт с подтверждённым другим номером видит только своё
        var other = await RegisterAsync();
        await MarkPhoneVerifiedAsync(other.Phone, other.UserId);
        (await J(await AuthedClient(other.Token).GetAsync("/api/profile/export"))).GetProperty("stayServiceOrders").GetArrayLength().Should().Be(0);
        (await J(await AuthedClient(other.Token).GetAsync("/api/baths/service-orders/my"))).GetRawText().Should().NotContain(own).And.NotContain(byPhone).And.NotContain(foreign);
    }

    // ── CY42-82: удаление аккаунта ──────────────────────────────────────────────

    [Fact, TestCase("CY42-82")]
    public async Task DeleteAccount_ErasesBathOrder_KeepsGuestsCountScheduleAndMoney_RemovesProofFiles()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (c, res) = await SceneAsync();
        var guest = await RegisterAsync(firstName: "Ольга", lastName: "Гостева");
        var guestClient = host.Client(guest.Token);
        var date = InDays(10);
        var token = await BookAsGuestAsync(res, guestClient, date, 720, guests: 5, phone: guest.Phone, name: "Ольга Гостева", comment: "Аллергия на дым");
        (await BathProofAsync(token, guestClient)).StatusCode.Should().Be(HttpStatusCode.Created);
        var sessionId = await SessionIdOfOrderAsync(token);
        await ConfirmPaymentAsync(c, sessionId);
        var key = await WithDbAsync(db => db.StayPaymentProofs.Where(p => p.StayServiceOrderId == db.StayServiceOrders.Where(o => o.PublicToken == token).Select(o => o.Id).First())
            .Select(p => p.StorageKey).SingleAsync());
        key.Should().NotBeNull();
        (await guestClient.PostJsonAsync($"/api/baths/service-orders/public/{token}/push-subscription",
            new PushSubscriptionInput($"https://push.example.test/cy42-del/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var del = await guestClient.PostJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        del.StatusCode.Should().Be(HttpStatusCode.NoContent, await del.Content.ReadAsStringAsync());

        var after = await BathCardAsync(c, sessionId);
        after.GetProperty("guestName").ValueKind.Should().Be(JsonValueKind.Null);
        after.GetProperty("guestPhone").ValueKind.Should().Be(JsonValueKind.Null);
        after.GetProperty("comment").ValueKind.Should().Be(JsonValueKind.Null);
        after.GetProperty("orderStatus").GetString().Should().Be("Confirmed", "активная бронь не отменяется");
        after.GetProperty("totalRub").GetInt32().Should().Be(4000, "деньги остаются");
        after.GetProperty("guestsCount").GetInt32().Should().Be(5, "число гостей остаётся: само по себе к человеку оно не относится (LEGAL_REVIEW_CYCLE42 §6.2 п. 3)");
        after.GetProperty("paymentProofs").EnumerateArray().Should().OnlyContain(p => p.GetProperty("purged").GetBoolean(), "файлы удаляются сразу");
        after.GetRawText().Should().NotContain("Ольга").And.NotContain("Гостева").And.NotContain("Аллергия");

        var stored = await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.PublicToken == token));
        stored.PersonalDataErased.Should().BeTrue();
        stored.GuestUserId.Should().BeNull();
        stored.GuestName.Should().BeNull();
        stored.GuestPhone.Should().BeNull();
        stored.Comment.Should().BeNull();
        stored.GuestsCount.Should().Be(5);
        (await WithDbAsync(db => db.StayGuestPushSubscriptions.CountAsync(s => s.StayServiceOrderId == stored.Id))).Should().Be(0);
        var session = (await AllSessionsAsync(res.Id)).Single();
        session.AddedByNameSnapshot.Should().NotContain("Ольга");
        session.AddedByUserId.Should().BeNull();

        // расписание владельца и банщика цело: время занято, брони видны без имени
        (await ActiveSessionsAsync(res.Id)).Should().ContainSingle();
        var starts = await J(await AnonymousClient().GetAsync($"/api/baths/public/services/{res.Id}/starts?date={D(date)}"));
        starts.GetProperty("starts").EnumerateArray().Select(s => s.GetProperty("startMinute").GetInt32()).Should().NotContain(720);
        var schedule = await AuthedClient(c.Token).GetStringAsync($"/api/baths/companies/{c.CompanyId}/schedule?from={D(date)}&days=1");
        schedule.Should().NotContain("Ольга").And.NotContain("Гостева");

        using var scope = Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
        var open = () => storage.OpenPrivate(key!);
        open.Should().Throw<FileNotFoundException>("файл подтверждения удалён с диска");
    }

    // ── CY42-83: отзыв согласия ─────────────────────────────────────────────────

    [Fact, TestCase("CY42-83")]
    public async Task RevokeProviderDelivery_SwitchesOffMessengerOfActiveBathOrders_PreviewAgrees_OtherOrdersUntouched()
    {
        var (_, res) = await SceneAsync(prepay: null);
        var guest = await RegisterAsync();
        var other = await RegisterAsync();
        var mine = await BookAsGuestAsync(res, AuthedClient(guest.Token), InDays(10), 720, 2, guest.Phone);
        var others = await BookAsGuestAsync(res, AuthedClient(other.Token), InDays(10), 1080, 2, other.Phone);
        await WithDbAsync(async db =>
        {
            foreach (var o in await db.StayServiceOrders.Where(x => x.PublicToken == mine || x.PublicToken == others).ToListAsync())
            {
                o.NotifyByMessenger = true;
                o.MessengerConsentAtUtc = DateTime.UtcNow;
                o.MessengerConsentVersion = "v-bani-test";
            }
            await db.SaveChangesAsync();
        });

        var preview = await J(await AuthedClient(guest.Token).GetAsync("/api/profile/consents/revoke-preview?purpose=ProviderDelivery"));
        var expected = preview.GetProperty("stayBookingsMessengerDisabled").GetInt32();
        expected.Should().BeGreaterOrEqualTo(1, "бронь бани входит в предпросмотр");
        var revoke = await AuthedClient(guest.Token).PostJsonAsync("/api/profile/consents/revoke", new { documentKey = "PdnConsent", purpose = "ProviderDelivery", reason = "не хочу" });
        revoke.StatusCode.Should().Be(HttpStatusCode.OK, await revoke.Content.ReadAsStringAsync());
        (await J(revoke)).GetProperty("effects").GetProperty("stayBookingsMessengerDisabled").GetInt32().Should().Be(expected, "предпросмотр и факт совпадают");

        var stored = await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.PublicToken == mine));
        stored.NotifyByMessenger.Should().BeFalse();
        stored.MessengerConsentVersion.Should().Be("v-bani-test", "снимок согласия — правовые данные, не удаляется");
        stored.Status.Should().Be(StayBookingStatus.Confirmed, "бронь не отменяется, всё доступно на странице");
        (await BathOrderPageAsync(mine)).GetProperty("status").GetString().Should().Be("Confirmed");
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.PublicToken == others))).NotifyByMessenger.Should().BeTrue("чужая бронь не тронута");
    }

    // ── retention ───────────────────────────────────────────────────────────────

    private async Task<(string Token, Guid OrderId, Guid SessionId)> TerminalAsync(BathCtx c, ResCtx res, int shift, bool unpaid, bool withProof = true, int guests = 4)
    {
        var token = await BookAsGuestAsync(res, AnonymousClient(), InDays(10 + shift), 720, guests, UniquePhone(), name: "Retention Гость", comment: "Комментарий");
        var orderId = await OrderIdAsync(token);
        if (withProof || !unpaid) (await BathProofAsync(token)).StatusCode.Should().Be(HttpStatusCode.Created);
        var sessionId = await SessionIdOfOrderAsync(token);
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
        else await ConfirmPaymentAsync(c, sessionId);
        return (token, orderId, sessionId);
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

    // ── CY42-84: подтверждения оплаты и неоплаченные ────────────────────────────

    [Fact, TestCase("CY42-84")]
    public async Task Retention_BathProofs_After90Days_UnpaidDepersonalised30Days_GuestsCountKept_DryRunChangesNothing()
    {
        var (c, res) = await SceneAsync();
        var a = await TerminalAsync(c, res, 0, unpaid: false);
        var b = await TerminalAsync(c, res, 1, unpaid: false);
        var keyA = await WithDbAsync(db => db.StayPaymentProofs.Where(p => p.StayServiceOrderId == a.OrderId).Select(p => p.StorageKey).SingleAsync());
        var keyB = await WithDbAsync(db => db.StayPaymentProofs.Where(p => p.StayServiceOrderId == b.OrderId).Select(p => p.StorageKey).SingleAsync());
        // A: сеанс и статус старше 90 дней → файл удаляется. B: сеанс старый, но статус свежий (10 дней) → отсчёт от более поздней даты, файл остаётся
        await MoveSessionAsync(a.SessionId, a.OrderId, 100, 100);
        await MoveSessionAsync(b.SessionId, b.OrderId, 101, 10);

        var dry = await RunRuleAsync((db, st) => new StayServiceOrderPaymentProofRule(db, st), dryRun: true);
        dry.Affected.Should().BeGreaterOrEqualTo(1, "правило видит брони банной компании");
        (await WithDbAsync(db => db.StayPaymentProofs.CountAsync(p => (p.StayServiceOrderId == a.OrderId || p.StayServiceOrderId == b.OrderId) && p.StorageKey != null))).Should().Be(2,
            "сухой прогон ничего не меняет");
        await RunRuleAsync((db, st) => new StayServiceOrderPaymentProofRule(db, st), dryRun: false);
        (await WithDbAsync(db => db.StayPaymentProofs.SingleAsync(p => p.StayServiceOrderId == a.OrderId))).StorageKey.Should().BeNull();
        (await WithDbAsync(db => db.StayPaymentProofs.SingleAsync(p => p.StayServiceOrderId == b.OrderId))).StorageKey.Should().Be(keyB);
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.Id == a.OrderId))).PaymentProofsPurgedAtUtc.Should().NotBeNull();
        using (var scope = Factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
            var gone = () => storage.OpenPrivate(keyA!);
            gone.Should().Throw<FileNotFoundException>();
        }

        // «Снят: не оплачен» старше 30 дней — обезличивание; свежее (5 дней) — нет; число гостей остаётся
        var unpaid = await TerminalAsync(c, res, 2, unpaid: true, withProof: false, guests: 3);
        var fresh = await TerminalAsync(c, res, 3, unpaid: true, withProof: false);
        await WithDbAsync(async db =>
        {
            var o = await db.StayServiceOrders.SingleAsync(x => x.Id == fresh.OrderId);
            o.TerminalAtUtc = DateTime.UtcNow.AddDays(-5);
            await db.SaveChangesAsync();
        });
        var dryUnpaid = await RunRuleAsync((db, st) => new StayServiceOrderUnpaidPersonalizationRule(db), dryRun: true);
        dryUnpaid.Affected.Should().BeGreaterOrEqualTo(1);
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.Id == unpaid.OrderId))).GuestName.Should().NotBeNull("сухой прогон ничего не меняет");
        await RunRuleAsync((db, st) => new StayServiceOrderUnpaidPersonalizationRule(db), dryRun: false);
        var u = await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.Id == unpaid.OrderId));
        (u.PersonalDataErased, u.GuestName, u.GuestPhone, u.Comment).Should().Be((true, null, null, null));
        u.GuestsCount.Should().Be(3, "число гостей при обезличивании остаётся");
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.Id == fresh.OrderId))).GuestName.Should().NotBeNull("неоплаченному меньше 30 дней — не трогаем");
        (await WithDbAsync(db => db.StayServiceSessions.AsNoTracking().SingleAsync(s => s.Id == unpaid.SessionId))).AddedByNameSnapshot.Should().NotContain("Retention");
    }

    // ── CY42-85: остальные правила и журнал ─────────────────────────────────────

    [Fact, TestCase("CY42-85")]
    public async Task Retention_BathOrders_After3Years_EventsAndScheduleJournal_RulesRegistered_NewerOrdersUntouched()
    {
        var (c, res) = await SceneAsync();
        var old = await TerminalAsync(c, res, 0, unpaid: false, withProof: false, guests: 6);
        var recent = await TerminalAsync(c, res, 1, unpaid: false, withProof: false);
        await MoveSessionAsync(old.SessionId, old.OrderId, 1200, 1200);
        await MoveSessionAsync(recent.SessionId, recent.OrderId, 400, 400);
        (await AuthedClient(c.Token).PutJsonAsync($"/api/baths/companies/{c.CompanyId}/services/{res.Id}/date-overrides/{D(InDays(30))}", new DateOverrideInput(true, [], "Закрыто")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await WithDbAsync(async db =>
        {
            foreach (var e in db.StayServiceOrderEvents.Where(e => e.StayServiceOrderId == old.OrderId)) e.OccurredAtUtc = DateTime.UtcNow.AddDays(-1200);
            foreach (var e in db.StayServiceScheduleEvents.Where(e => e.ServiceId == res.Id)) e.OccurredAtUtc = DateTime.UtcNow.AddDays(-1200);
            await db.SaveChangesAsync();
        });
        var recentEvents = await WithDbAsync(db => db.StayServiceOrderEvents.CountAsync(e => e.StayServiceOrderId == recent.OrderId));

        await RunRuleAsync((db, st) => new StayServiceOrderPersonalizationRule(db), dryRun: false);
        await RunRuleAsync((db, st) => new StayServiceOrderEventRule(db), dryRun: false);
        await RunRuleAsync((db, st) => new StayServiceScheduleEventRule(db), dryRun: false);

        var o = await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(x => x.Id == old.OrderId));
        o.PersonalDataErased.Should().BeTrue("прочее — через 3 года от конца сеанса");
        o.GuestName.Should().BeNull();
        o.GuestsCount.Should().Be(6);
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(x => x.Id == recent.OrderId))).PersonalDataErased.Should().BeFalse("400 дней — рано");
        (await WithDbAsync(db => db.StayServiceOrderEvents.CountAsync(e => e.StayServiceOrderId == old.OrderId))).Should().Be(0, "журнал брони — 3 года");
        (await WithDbAsync(db => db.StayServiceOrderEvents.CountAsync(e => e.StayServiceOrderId == recent.OrderId))).Should().Be(recentEvents, "свежий журнал цел");
        (await WithDbAsync(db => db.StayServiceScheduleEvents.CountAsync(e => e.ServiceId == res.Id))).Should().Be(0);
        (await WithDbAsync(db => db.StayServiceSessions.AsNoTracking().SingleAsync(s => s.Id == old.SessionId))).ServiceNameSnapshot.Should().Be("Баня ПДн", "расписание и деньги остаются");

        using var scope = Factory.Services.CreateScope();
        var names = scope.ServiceProvider.GetServices<IRetentionRule>().Select(r => r.Name).ToList();
        names.Should().Contain(["stay-service-order-payment-proofs", "stay-service-order-unpaid-personalization", "stay-service-order-personalization", "stay-service-order-events", "stay-service-schedule-events"],
            "правила хранения броней услуг зарегистрированы и охватывают бани");
    }
}
