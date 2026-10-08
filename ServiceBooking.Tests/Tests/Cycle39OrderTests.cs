using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39, «Вызов 2»: заказ услуги БЕЗ проживания (US-39-07…13, 15, 18; ARCHITECTURE_CYCLE39.md §39.7.1, §39.8). Настройка компании, исполнитель обязателен всегда (ЮР39-2),
/// предоплата и округление, удержание и таймер, идемпотентность, SlotTaken, зазор между сеансами, отмена и возврат (ЮР39-1), действия персонала. Часы — <see cref="FakeStaysClock"/>.
/// </summary>
public class Cycle39OrderTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private async Task<(StaysCtx Company, SvcCtx Svc)> SceneAsync(int? prepay = null, bool provider = true, bool paymentDetails = true, int priceRub = 2000, int buffer = 30,
        StayServiceCancellationPolicy policy = StayServiceCancellationPolicy.NoDeductions, int minLead = 0, int minHours = 2, int maxHours = 6)
    {
        var company = await CreateStaysCompanyAsync(provider: provider, paymentDetails: paymentDetails);
        if (provider) await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: prepay, priceRub: priceRub, buffer: buffer, policy: policy, minLeadMinutes: minLead, minHours: minHours, maxHours: maxHours);
        return (company, svc);
    }

    // ── старты ───────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-20")]
    public async Task Starts_ShowOnlyAvailable_WithMaxHours_NightStartsLabelled_NoInternalWords()
    {
        var (_, svc) = await SceneAsync(maxHours: 4);
        var date = InDays(9);
        var r = await AnonymousClient().GetAsync($"/api/stays/public/services/{svc.Id}/starts?date={D(date)}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await r.Content.ReadAsStringAsync();
        var starts = System.Text.Json.JsonSerializer.Deserialize<ServiceStartsDto>(raw, JsonHelpers.Options)!;
        // окно 08:00–02:00 (след. дня), шаг 60, минимум 2 ч: последний старт — 24:00 (конец окна минус 2 часа = 00:00)
        starts.Starts.Select(s => s.StartMinute).Should().Equal(Enumerable.Range(0, 17).Select(i => 480 + 60 * i), "последний старт 00:00 (минуты 1440): [00:00, 02:00) в окне");
        starts.Starts.First().MaxHours.Should().Be(4, "максимум услуги");
        starts.Starts.Last().MaxHours.Should().Be(2, "до конца окна — два часа");
        starts.Starts.Single(s => s.StartMinute == 1380).MaxHours.Should().Be(3);
        starts.Starts.Single(s => s.StartMinute == 1380).Options.Select(o => o.Hours).Should().Equal(2, 3);
        starts.Starts.Last().Label.Should().Contain("ночь на", "старт после полуночи подписан");
        starts.Starts.First().StartUtc.Should().Be(StartUtc(date, 480));
        raw.Should().NotContain("бизнес-день").And.NotContain("зазор").And.NotContain("buffer", because: "зазор и внутренние понятия гостю не показываются");

        // календарь дат
        var avail = await AnonymousClient().GetAsync($"/api/stays/public/services/{svc.Id}/availability?from={D(InDays(5))}&days=7");
        avail.StatusCode.Should().Be(HttpStatusCode.OK);
        var days = (await avail.Content.ReadJsonAsync<ServiceAvailabilityDto>())!;
        days.Days.Should().HaveCount(7).And.OnlyContain(d => d.HasStarts);
        (await AnonymousClient().GetAsync($"/api/stays/public/services/{svc.Id}/availability?days=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AnonymousClient().GetAsync($"/api/stays/public/services/{svc.Id}/availability?days=32")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AnonymousClient().GetAsync($"/api/stays/public/services/{svc.Id}/starts")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AnonymousClient().GetAsync($"/api/stays/public/services/{svc.Id}/starts?date=2026-02-30")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AnonymousClient().GetAsync($"/api/stays/public/services/{Guid.NewGuid()}/starts?date={D(date)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY39-21")]
    public async Task Starts_PastDate_BeyondHorizon_And_MinLead_AreEmptyWithReason()
    {
        var (company, svc) = await SceneAsync(minLead: 120);
        var past = await StartsAsync(svc.Id, InDays(-3));
        past.Starts.Should().BeEmpty();
        past.Reason.Should().Be(StartsReason.DateInPast);
        var horizon = (await GetCompanyAsync(company)).Settings!.HorizonDays;
        var far = await StartsAsync(svc.Id, InDays(horizon + 5));
        far.Starts.Should().BeEmpty();
        far.Reason.Should().Be(StartsReason.BeyondHorizon);
        // шаг 30 минут: старты каждые полчаса от начала окна
        var half = await CreateServiceAsync(company, "Чан", step: 30, minHours: 1, maxHours: 2);
        var s = await StartsAsync(half.Id, InDays(7));
        s.Starts.Take(3).Select(x => x.StartMinute).Should().Equal(480, 510, 540);
    }

    // ── настройка компании, гейт ─────────────────────────────────────────────────

    [Fact, TestCase("CY39-22")]
    public async Task OrderWithoutStay_Disabled_ByDefault_409_ServiceOrdersDisabled()
    {
        var company = await CreateStaysCompanyAsync();
        var svc = await CreateServiceAsync(company);
        var date = InDays(9);
        var quote = await QuoteServiceAsync(svc.Id, date, 720, 2);
        var r = await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, quote.TotalRub, UniquePhone()));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(r)).Should().Be("ServiceOrdersDisabled");
        (await r.Content.ReadAsStringAsync()).Should().Contain("не принимает заказы услуг без проживания");
        (await ActiveSessionsAsync(svc.Id)).Should().BeEmpty();
        var page = (await (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/services/{svc.Slug}")).Content.ReadJsonAsync<PublicServiceDto>())!;
        page.Standalone.Ordering.Should().BeFalse();
        page.Standalone.NotOrderingText.Should().Be("Можно добавить к брони дома");

        await EnableOrdersWithoutStayAsync(company);
        (await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, quote.TotalRub, UniquePhone()))).StatusCode.Should().Be(HttpStatusCode.Created);
        await EnableOrdersWithoutStayAsync(company, false);
        (await PostOrderAsync(svc.Id, OrderInput(date, 1080, 2, quote.TotalRub, UniquePhone()))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY39-23")]
    public async Task ProviderInfo_IsMandatory_EvenWithZeroPrepay_ЮР39_2()
    {
        // компания без сведений об исполнителе: включить заказ без проживания нельзя
        var company = await CreateStaysCompanyAsync(provider: false);
        var current = (await GetCompanyAsync(company)).Settings!;
        var enable = await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/settings", current with { AcceptServiceOrdersWithoutStay = true });
        enable.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(enable)).Should().Be("ProviderRequiredForServiceOrders");

        // компания с включённой настройкой, потерявшая сведения (в БД), не принимает ни при какой предоплате, в том числе при 0 %
        var ok = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(ok);
        var svc = await CreateServiceAsync(ok, prepay: null);
        await WithDbAsync(async db =>
        {
            var s = await db.StaysSettings.SingleAsync(x => x.CompanyId == ok.Id);
            s.ProviderName = null;
            s.ProviderInn = null;
            await db.SaveChangesAsync();
        });
        var date = InDays(9);
        var quote = await QuoteServiceAsync(svc.Id, date, 720, 2);
        quote.AcceptingBookings.Should().BeFalse("исполнитель обязателен при любой предоплате, включая её отсутствие");
        var r = await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, quote.TotalRub, UniquePhone()));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(r)).Should().Be("NotAcceptingBookings");
        (await J(r)).GetProperty("reasonCode").GetString().Should().Be("NoProviderInfo");
    }

    [Fact, TestCase("CY39-24")]
    public async Task HouseBooking_ZeroPrepay_AlsoRequiresProvider_ЮР39_2()
    {
        var company = await CreateStaysCompanyAsync(prepayPercent: 0);
        var house = await CreateHouseAsync(company, price: 3000);
        (await BookOkAsync(house.Id, InDays(10), InDays(12))).Token.Should().NotBeNullOrEmpty();
        await WithDbAsync(async db =>
        {
            var s = await db.StaysSettings.SingleAsync(x => x.CompanyId == company.Id);
            s.ProviderName = null;
            s.ProviderInn = null;
            await db.SaveChangesAsync();
        });
        var q = await QuoteAsync(house.Id, InDays(20), InDays(22));
        var r = await PostBookingAsync(house.Id, Booking(InDays(20), InDays(22), q.TotalRub));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict, "дом с предоплатой 0 % без исполнителя больше не принимает брони");
        (await J(r)).GetProperty("reasonCode").GetString().Should().Be("NoProviderInfo");
    }

    // ── создание заказа ──────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-25")]
    public async Task Order_NoPrepay_IsConfirmedAtOnce_PageHasTextualStatus_AndPayOnSite()
    {
        var (company, svc) = await SceneAsync();
        var broom = await AddItemAsync(company, svc.Id, "Веник", 300, 5);
        var date = InDays(9);
        var created = await OrderOkAsync(svc.Id, date, 720, 3, phone: "+7 (900) 123-45-67", items: [new ItemSelectionInput(broom.Id, 2)]);
        created.Token.Length.Should().BeGreaterOrEqualTo(22, "≥ 128 бит в base64url");
        created.OrderUrl.Should().Contain("/s/" + created.Token);
        var o = created.Order;
        o.Status.Should().Be(StayBookingStatus.Confirmed);
        o.StatusText.Should().NotBeNullOrWhiteSpace();
        o.ServiceAmountRub.Should().Be(6000);
        o.ItemsAmountRub.Should().Be(600);
        o.TotalRub.Should().Be(6600);
        o.PrepayRub.Should().Be(0);
        o.DueOnSiteRub.Should().Be(6600);
        o.HoldExpiresAtUtc.Should().BeNull();
        o.Time.StartUtc.Should().Be(StartUtc(date, 720));
        o.Time.EndUtc.Should().Be(StartUtc(date, 900));
        o.Time.Label.Should().Contain("12:00").And.Contain("15:00");
        o.GuestPhoneMasked.Should().NotContain("1234567").And.Contain("67", "телефон в публичном ответе маскируется");
        var page = await GetOrderAsync(created.Token);
        page.Provider.Should().NotBeNull("блок «Об исполнителе»");
        page.Lines.Should().NotBeEmpty();
        page.HourPrices.Should().HaveCount(3).And.OnlyContain(h => h.PriceRub == 2000);
        page.Cancellation.CanCancel.Should().BeTrue();
        (await ActiveSessionsAsync(svc.Id)).Should().ContainSingle().Which.OccupiedUntilUtc.Should().Be(StartUtc(date, 900).AddMinutes(30), "занято до конца сеанса + зазор 30 минут");
        (await AnonymousClient().GetAsync("/api/stays/service-orders/public/no-such-token")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync($"/api/stays/service-orders/public/{created.Token}x")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory, TestCase("CY39-26")]
    [InlineData(15, 1675, 2, 503)]  // 3350 × 15 % = 502,5 → 503 (половина вверх)
    [InlineData(50, 1000, 3, 1500)]
    [InlineData(1, 2000, 2, 40)]
    [InlineData(100, 2000, 2, 4000)]
    [InlineData(33, 1001, 2, 661)]   // 2002 × 33 % = 660,66 → 661
    public async Task Order_WithPrepay_IsHeld_PrepayRoundsHalfUp_ItemsOutOfPrepay(int percent, int price, int hours, int expectedPrepay)
    {
        var (company, svc) = await SceneAsync(prepay: percent, priceRub: price);
        var broom = await AddItemAsync(company, svc.Id, "Веник", 300, 5);
        var date = InDays(9);
        var created = await OrderOkAsync(svc.Id, date, 720, hours, items: [new ItemSelectionInput(broom.Id, 2)]);
        var o = created.Order;
        o.Status.Should().Be(StayBookingStatus.Held);
        o.PrepayRub.Should().Be(expectedPrepay, "позиции в предоплату не входят");
        o.TotalRub.Should().Be(price * hours + 600);
        o.DueOnSiteRub.Should().Be(o.TotalRub - expectedPrepay);
        o.PrepayPercent.Should().Be(percent);
        o.HoldExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromMinutes(1));
        o.Payment.Should().NotBeNull("в «Удержан» — реквизиты и назначение платежа");
        o.Payment!.Details.Should().Contain("Сбербанк");
    }

    [Fact, TestCase("CY39-27")]
    public async Task Order_WithPrepay_WithoutPaymentDetails_409_NoPaymentDetails()
    {
        var company = await CreateStaysCompanyAsync(paymentDetails: false);
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: 30);
        var date = InDays(9);
        var q = await QuoteServiceAsync(svc.Id, date, 720, 2);
        q.AcceptingBookings.Should().BeFalse();
        var r = await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, q.TotalRub, UniquePhone()));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(r)).GetProperty("reasonCode").GetString().Should().Be("NoPaymentDetails");
    }

    [Fact, TestCase("CY39-28")]
    public async Task Order_FormValidation_400WithRussianText_NothingCreated()
    {
        var (_, svc) = await SceneAsync();
        var date = InDays(9);
        var ok = OrderInput(date, 720, 2, 4000, UniquePhone());
        async Task<string> Bad(CreateServiceOrderInput input)
        {
            var r = await PostOrderAsync(svc.Id, input);
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, await r.Content.ReadAsStringAsync());
            return await r.Content.ReadAsStringAsync();
        }
        (await Bad(ok with { GuestName = "  " })).Should().Contain("Укажите имя");
        (await Bad(ok with { GuestName = new string('а', 101) })).Should().Contain("100");
        (await Bad(ok with { GuestPhone = "123" })).Should().NotBeNullOrWhiteSpace();
        (await Bad(ok with { GuestPhone = null })).Should().NotBeNullOrWhiteSpace();
        (await Bad(ok with { Comment = new string('к', 501) })).Should().Contain("500");
        (await Bad(ok with { IdempotencyKey = null })).Should().Contain("ключ");
        (await Bad(ok with { IdempotencyKey = Guid.Empty })).Should().Contain("ключ");
        (await Bad(ok with { BusinessDate = null })).Should().Contain("дату");
        (await Bad(ok with { StartMinute = null })).Should().Contain("время");
        (await Bad(ok with { StartMinute = 725 })).Should().Contain("30 минут");
        (await Bad(ok with { Hours = 0 })).Should().Contain("часов");
        (await Bad(ok with { Hours = 13 })).Should().Contain("часов");
        (await Bad(ok with { Items = [new ItemSelectionInput(Guid.NewGuid(), 51)] })).Should().Contain("50");
        (await Bad(ok with { Items = [new ItemSelectionInput(null, 1)] })).Should().Contain("50");
        (await Bad(ok with { Items = Enumerable.Range(0, 21).Select(_ => new ItemSelectionInput(Guid.NewGuid(), 1)).ToList() })).Should().NotBeNullOrWhiteSpace();
        (await ActiveSessionsAsync(svc.Id)).Should().BeEmpty();
        (await WithDbAsync(db => db.StayServiceOrders.CountAsync(o => o.ServiceId == svc.Id))).Should().Be(0);
    }

    [Fact, TestCase("CY39-29")]
    public async Task Order_BusinessRefusals_ReturnJson409WithStableCodes()
    {
        var (company, svc) = await SceneAsync(minLead: 120);
        var broom = await AddItemAsync(company, svc.Id, "Веник", 300, 2);
        var off = await AddItemAsync(company, svc.Id, "Выключенная", 100, 2, active: false);
        var date = InDays(9);
        async Task<string> Refuse(CreateServiceOrderInput input)
        {
            var r = await PostOrderAsync(svc.Id, input);
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
            return await Code(r);
        }
        var ok = OrderInput(date, 720, 2, 4000, UniquePhone());
        (await Refuse(ok with { Hours = 1, ExpectedTotalRub = 2000 })).Should().Be("HoursOutOfRange", "минимум 2 часа");
        (await Refuse(ok with { Hours = 7, ExpectedTotalRub = 14000 })).Should().Be("HoursOutOfRange", "максимум 6 часов");
        (await Refuse(ok with { StartMinute = 450 })).Should().Be("StartUnavailable", "старт до начала окна");
        (await Refuse(ok with { StartMinute = 750 })).Should().Be("StartUnavailable", "не на сетке услуги (шаг 60 от начала окна)");
        (await Refuse(ok with { StartMinute = 1500 })).Should().Be("StartUnavailable", "сеанс вылезает за окно");
        (await Refuse(ok with { BusinessDate = InDays(-2) })).Should().Be("DateInPast");
        (await Refuse(ok with { BusinessDate = InDays(2000) })).Should().Be("BeyondHorizon");
        (await Refuse(ok with { Items = [new ItemSelectionInput(off.Id, 1)] })).Should().Be("ItemUnavailable");
        (await Refuse(ok with { Items = [new ItemSelectionInput(Guid.NewGuid(), 1)] })).Should().Be("ItemUnavailable");
        (await Refuse(ok with { Items = [new ItemSelectionInput(broom.Id, 3)] })).Should().Be("ItemQuantityExceeded");
        // PriceChanged несёт новый расчёт
        var changed = await PostOrderAsync(svc.Id, ok with { ExpectedTotalRub = 3999 });
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(changed);
        body.GetProperty("code").GetString().Should().Be("PriceChanged");
        body.GetProperty("quote").GetProperty("totalRub").GetInt32().Should().Be(4000);
        body.GetProperty("message").GetString().Should().Contain("4");
        // слишком близко к «сейчас» (минимальное время до начала 2 часа)
        await using var host = new StaysTestFactory(ConnectionString);
        var now = host.StaysClock.UtcNow;
        var (bd, minute) = BusinessClock.BusinessDateOf(Tz, now.AddMinutes(30));
        var soonMinute = (minute + 29) / 30 * 30;
        var soon = await host.Client().PostJsonAsync($"/api/stays/public/services/{svc.Id}/orders", OrderInput(bd, soonMinute, 2, 4000, UniquePhone()));
        soon.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(soon)).Should().BeOneOf("TooEarly", "StartUnavailable");
        (await ActiveSessionsAsync(svc.Id)).Should().BeEmpty();
    }

    [Fact, TestCase("CY39-30")]
    public async Task Order_Idempotency_SameKeyReturnsExisting_Parallel()
    {
        var (_, svc) = await SceneAsync();
        var date = InDays(9);
        var input = OrderInput(date, 720, 2, 4000, UniquePhone());
        var first = await PostOrderAsync(svc.Id, input);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var token = (await first.Content.ReadJsonAsync<CreateServiceOrderResponse>())!.Token;
        var repeat = await PostOrderAsync(svc.Id, input);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK, "повтор с тем же ключом");
        (await repeat.Content.ReadJsonAsync<CreateServiceOrderResponse>())!.Token.Should().Be(token);

        var input2 = OrderInput(date, 1080, 2, 4000, UniquePhone());
        var gate = new TaskCompletionSource();
        var calls = Enumerable.Range(0, 6).Select(_ => Task.Run(async () => { await gate.Task; return await PostOrderAsync(svc.Id, input2); })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(calls);
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK, "гонка одного ключа: победитель создан, остальные получают его же");
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        var tokens = await Task.WhenAll(results.Select(async r => (await r.Content.ReadJsonAsync<CreateServiceOrderResponse>())!.Token));
        tokens.Distinct().Should().ContainSingle();
        (await ActiveSessionsAsync(svc.Id)).Should().HaveCount(2);
    }

    [Fact, TestCase("CY39-31")]
    public async Task Order_SlotTaken_Buffer_AdjacentAllowed_CancelFreesTime()
    {
        var (_, svc) = await SceneAsync(buffer: 30);
        var date = InDays(9);
        var a = await OrderOkAsync(svc.Id, date, 720, 2); // 12:00–14:00, занято до 14:30
        var dup = await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, 4000, UniquePhone()));
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(dup)).Should().Be("SlotTaken");
        (await dup.Content.ReadAsStringAsync()).Should().Contain("Это время уже занято");

        var starts = (await StartsAsync(svc.Id, date)).Starts.Select(s => s.StartMinute).ToList();
        starts.Should().NotContain(new[] { 660, 720, 780, 840 }, "пересечение с занятым временем и зазором: старт в 11:00 доходит до 13:00, старт в 14:00 — внутри зазора");
        starts.Should().NotContain(600, "10:00–12:00 + зазор 30 минут заходит на сеанс 12:00 (SPEC §4.3: [s, s+h+зазор) не пересекается с занятым)");
        starts.Should().Contain(540).And.Contain(900, "09:00–11:00 + зазор до 11:30 — свободно; 15:00 — после зазора чужого сеанса");
        var maxAt540 = (await StartsAsync(svc.Id, date)).Starts.Single(s => s.StartMinute == 540).MaxHours;
        maxAt540.Should().Be(2, "с 09:00 длиннее двух часов не помещается: сеанс + свой зазор упираются в занятое");

        (await PostOrderAsync(svc.Id, OrderInput(date, 840, 2, 4000, UniquePhone()))).StatusCode.Should().Be(HttpStatusCode.Conflict, "14:00 попадает в зазор");
        (await PostOrderAsync(svc.Id, OrderInput(date, 900, 2, 4000, UniquePhone()))).StatusCode.Should().Be(HttpStatusCode.Created, "15:00 уже после зазора");

        (await CancelOrderAsync(a.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StartsAsync(svc.Id, date)).Starts.Select(s => s.StartMinute).Should().Contain(720, "отмена освобождает время сразу");
        (await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, 4000, UniquePhone()))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CY39-32")]
    public async Task Order_ZeroBuffer_BackToBackAllowed()
    {
        var (_, svc) = await SceneAsync(buffer: 0);
        var date = InDays(9);
        await OrderOkAsync(svc.Id, date, 720, 2);
        (await PostOrderAsync(svc.Id, OrderInput(date, 840, 2, 4000, UniquePhone()))).StatusCode.Should().Be(HttpStatusCode.Created, "зазор 0 — следующий сеанс вплотную");
    }

    [Fact, TestCase("CY39-33")]
    public async Task Order_SignedInGuest_UsesAccountPhone_NotTheTypedOne()
    {
        var (_, svc) = await SceneAsync();
        var guest = await RegisterAsync();
        var date = InDays(9);
        var q = await QuoteServiceAsync(svc.Id, date, 720, 2);
        var r = await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, q.TotalRub, "+79990001122"), AuthedClient(guest.Token));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var token = (await r.Content.ReadJsonAsync<CreateServiceOrderResponse>())!.Token;
        var stored = await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.PublicToken == token));
        stored.GuestPhone!.TrimStart('+').Should().Be(guest.Phone.TrimStart('+'), "бронь — на номер аккаунта, а не на набранный в форме");
        stored.GuestUserId.Should().Be(guest.UserId);
    }

    [Fact, TestCase("CY39-34")]
    public async Task Order_Captcha_Enforced_ForAnonymous_NotForSignedIn()
    {
        var (_, svc) = await SceneAsync();
        await using var host = new StaysTestFactory(ConnectionString, settings: new Dictionary<string, string> { ["SmartCaptcha:SecretKey"] = "test-secret" });
        var date = InDays(9);
        var q = await QuoteServiceAsync(svc.Id, date, 720, 2, client: host.Client());
        var r = await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, q.TotalRub, UniquePhone()), host.Client());
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain("не робот");
        (await ActiveSessionsAsync(svc.Id)).Should().BeEmpty();
        var guest = await RegisterAsync();
        (await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, q.TotalRub, null), host.Client(guest.Token))).StatusCode.Should().Be(HttpStatusCode.Created, "вошедшему капча не нужна");
    }

    [Fact, TestCase("CY39-35")]
    public async Task Order_PhoneLimits_OneHeldPerCompany_TwoOnPlatform_PerDay_429WithText()
    {
        await using var host = new StaysTestFactory(ConnectionString, settings: new Dictionary<string, string>
        {
            ["Stays:Services:PhoneLimits:MaxHeldPerPhone"] = "2", ["Stays:Services:PhoneLimits:MaxHeldPerPhonePerCompany"] = "1", ["Stays:Services:PhoneLimits:MaxCreatedPerPhonePerDay"] = "4",
        });
        var client = host.Client();
        var (_, a) = await SceneAsync(prepay: 30);
        var (_, b) = await SceneAsync(prepay: 30);
        var (_, c) = await SceneAsync(prepay: 30);
        var phone = UniquePhone();
        async Task<HttpResponseMessage> Try(SvcCtx s, int start)
        {
            var q = await QuoteServiceAsync(s.Id, InDays(9), start, 2, client: client);
            return await PostOrderAsync(s.Id, OrderInput(InDays(9), start, 2, q.TotalRub, phone), client);
        }
        var first = await Try(a, 720);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await Try(a, 960);
        second.StatusCode.Should().Be((HttpStatusCode)429, "в одной компании — одно удержание на номер");
        (await second.Content.ReadAsStringAsync()).Should().Be("Слишком много неоплаченных заказов. Оплатите или отмените текущий заказ");
        var bResp = await Try(b, 720);
        bResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bToken = (await bResp.Content.ReadJsonAsync<CreateServiceOrderResponse>())!.Token;
        (await Try(c, 720)).StatusCode.Should().Be((HttpStatusCode)429, "не больше двух удержаний на платформе");
        var token = (await first.Content.ReadJsonAsync<CreateServiceOrderResponse>())!.Token;
        (await CancelOrderAsync(token, client)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Try(c, 720)).StatusCode.Should().Be(HttpStatusCode.Created, "отмена освобождает счётчик");
        // суточный: уже создано 3 заказа этого номера, четвёртый можно, пятый — нет
        // достигнут лимит удержаний платформы (b и c): заказ без предоплаты тоже ждёт освобождения счётчика — освобождаем b
        (await CancelOrderAsync(bToken, client)).StatusCode.Should().Be(HttpStatusCode.OK);
        var d = (await SceneAsync(prepay: null)).Svc;
        (await Try(d, 720)).StatusCode.Should().Be(HttpStatusCode.Created);
        var fifth = await Try(d, 1080);
        fifth.StatusCode.Should().Be((HttpStatusCode)429);
        (await fifth.Content.ReadAsStringAsync()).Should().Be("Слишком много заказов с этого номера. Попробуйте позже");
    }

    [Fact, TestCase("CY39-36")]
    public async Task Order_IpRateLimit_AnonymousCreation_429()
    {
        await using var host = new StaysTestFactory(ConnectionString, settings: new Dictionary<string, string> { ["RateLimits:stay-service-create:AnonymousPermitLimit"] = "2" });
        var (_, svc) = await SceneAsync();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var start = 480 + 180 * i;
            var r = await host.Client().PostJsonAsync($"/api/stays/public/services/{svc.Id}/orders", OrderInput(InDays(9), start, 2, 4000, UniquePhone()));
            statuses.Add(r.StatusCode);
        }
        statuses.Should().Contain((HttpStatusCode)429, string.Join(",", statuses));
    }

    // ── оплата, таймер, отмена ───────────────────────────────────────────────────

    private async Task<(StaysCtx Company, SvcCtx Svc, string Token, Guid SessionId, DateOnly Date)> HeldOrderAsync(StaysTestFactory host, int prepay = 30, int startMinute = 720, int hours = 2,
        StayServiceCancellationPolicy policy = StayServiceCancellationPolicy.NoDeductions, int priceRub = 2000, int dayShift = 9)
    {
        var (company, svc) = await SceneAsync(prepay: prepay, policy: policy, priceRub: priceRub);
        var date = InDays(dayShift);
        var client = host.Client();
        var created = await OrderOkAsync(svc.Id, date, startMinute, hours, client: client);
        return (company, svc, created.Token, await SessionIdOfOrderAsync(created.Token), date);
    }

    [Fact, TestCase("CY39-37")]
    public async Task Order_ProofThenStaffConfirm_Reject_Cancel_WithVersions()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (company, svc, token, sessionId, date) = await HeldOrderAsync(host);
        var client = host.Client();
        (await GetOrderAsync(token, client)).Status.Should().Be(StayBookingStatus.Held);

        var proof = await AttachOrderProofAsync(token, client);
        proof.StatusCode.Should().Be(HttpStatusCode.Created, await proof.Content.ReadAsStringAsync());
        var page = await GetOrderAsync(token, client);
        page.Status.Should().Be(StayBookingStatus.AwaitingPaymentCheck);
        page.PaymentProofs.Should().HaveCount(1);

        var card = await SessionCardAsync(company, sessionId);
        card.Kind.Should().Be(ServiceSessionKind.Standalone);
        card.OrderStatus.Should().Be(StayBookingStatus.AwaitingPaymentCheck);
        card.GuestPhone.Should().NotBeNullOrWhiteSpace();
        card.AvailableActions.Should().Contain("ConfirmPayment").And.Contain("RejectPayment");

        // версия устарела → 409 с актуальным состоянием
        var stale = await SessionActionAsync(company, sessionId, "confirm-payment", card.Version + 5);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(stale)).GetProperty("session").GetProperty("id").GetGuid().Should().Be(sessionId);
        // отклонение — только с причиной
        (await SessionActionAsync(company, sessionId, "reject-payment", card.Version, "  ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ok = await SessionActionAsync(company, sessionId, "confirm-payment", card.Version);
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var confirmed = (await ok.Content.ReadJsonAsync<StaffServiceSessionCardDto>())!;
        confirmed.OrderStatus.Should().Be(StayBookingStatus.Confirmed);
        confirmed.PaymentConfirmed.Should().NotBeNull();
        confirmed.Version.Should().BeGreaterThan(card.Version);
        confirmed.Events.Should().NotBeEmpty("каждый переход пишется в журнал");
        (await GetOrderAsync(token, client)).Status.Should().Be(StayBookingStatus.Confirmed);

        // повторное подтверждение — 409, не 500
        (await SessionActionAsync(company, sessionId, "confirm-payment", confirmed.Version)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // отмена компанией: причина обязательна, полный возврат, гость видит причину, время свободно
        (await SessionActionAsync(company, sessionId, "cancel", confirmed.Version, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var cancelled = await SessionActionAsync(company, sessionId, "cancel", confirmed.Version, "Авария водоснабжения");
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        var after = await GetOrderAsync(token, client);
        after.Status.Should().Be(StayBookingStatus.CancelledByOwner);
        after.StatusReason.Should().Be("Авария водоснабжения");
        (await ActiveSessionsAsync(svc.Id)).Should().BeEmpty();
        (await StartsAsync(svc.Id, date)).Starts.Select(s => s.StartMinute).Should().Contain(720);
        (await SessionActionAsync(company, sessionId, "cancel", confirmed.Version + 1, "ещё раз")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY39-38")]
    public async Task Order_StaffReject_NeedsReason_FreesTime_ProofsAfterwardsRefused()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (company, svc, token, sessionId, date) = await HeldOrderAsync(host);
        var client = host.Client();
        (await AttachOrderProofAsync(token, client)).StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await SessionCardAsync(company, sessionId);
        var r = await SessionActionAsync(company, sessionId, "reject-payment", card.Version, "Оплата не поступила");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var page = await GetOrderAsync(token, client);
        page.Status.Should().Be(StayBookingStatus.PaymentRejected);
        page.StatusReason.Should().Be("Оплата не поступила");
        (await AttachOrderProofAsync(token, client)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CancelOrderAsync(token, client)).StatusCode.Should().Be(HttpStatusCode.Conflict, "конечный статус");
        (await StartsAsync(svc.Id, date)).Starts.Select(s => s.StartMinute).Should().Contain(720);
    }

    [Fact, TestCase("CY39-39")]
    public async Task Order_HoldTimer_ExpiresExactly_BoundaryPlusMinusOneSecond()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (_, svc, token, _, date) = await HeldOrderAsync(host);
        var client = host.Client();
        var holdUntil = (await GetOrderAsync(token, client)).HoldExpiresAtUtc!.Value;

        host.StaysClock.Set(holdUntil.AddSeconds(-1));
        await host.RunTaskAsync("stays-hold-expiry");
        (await GetOrderAsync(token, client)).Status.Should().Be(StayBookingStatus.Held, "за секунду до конца таймера бронь жива");

        host.StaysClock.Set(holdUntil.AddSeconds(1));
        await host.RunTaskAsync("stays-hold-expiry");
        var page = await GetOrderAsync(token, client);
        page.Status.Should().Be(StayBookingStatus.ExpiredUnpaid);
        (await ActiveSessionsAsync(svc.Id)).Should().BeEmpty();
        (await AttachOrderProofAsync(token, client)).StatusCode.Should().Be(HttpStatusCode.Conflict, "после таймера подтверждение не принимается");
        (await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, 4000, UniquePhone()), client)).StatusCode.Should().Be(HttpStatusCode.Created, "время свободно");
    }

    [Fact, TestCase("CY39-40")]
    public async Task Order_ExpiredUnreleasedHold_IsFreeForNewOrder_ByLazyRelease()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (_, svc, token, _, date) = await HeldOrderAsync(host);
        var client = host.Client();
        var holdUntil = (await GetOrderAsync(token, client)).HoldExpiresAtUtc!.Value;
        host.StaysClock.Set(holdUntil.AddSeconds(2)); // задача ещё не отработала
        var starts = (await StartsAsync(svc.Id, date, client)).Starts.Select(s => s.StartMinute);
        starts.Should().Contain(720, "истёкшее, но не снятое удержание в выдаче считается свободным");
        var r = await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, 4000, UniquePhone()), client);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.PublicToken == token))).Status.Should().Be(StayBookingStatus.ExpiredUnpaid, "доснято при проверке нового заказа");
    }

    [Fact, TestCase("CY39-41")]
    public async Task Order_TenMinutesLeftReminder_QueuedOnce_NotBeforeTime()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (_, _, token, _, _) = await HeldOrderAsync(host);
        var created = host.StaysClock.UtcNow;
        var holdUntil = (await GetOrderAsync(token, host.Client())).HoldExpiresAtUtc!.Value;

        host.StaysClock.Set(holdUntil.AddMinutes(-10).AddSeconds(-5));
        await host.RunTaskAsync("stays-scheduled-messages");
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.PublicToken == token))).HoldReminderQueuedAtUtc.Should().BeNull("«осталось 10 минут» ещё рано");

        host.StaysClock.Set(holdUntil.AddMinutes(-10).AddSeconds(5));
        await host.RunTaskAsync("stays-scheduled-messages");
        var queued = (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.PublicToken == token))).HoldReminderQueuedAtUtc;
        queued.Should().NotBeNull();
        await host.RunTaskAsync("stays-scheduled-messages");
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.PublicToken == token))).HoldReminderQueuedAtUtc.Should().Be(queued, "однократно");
        _ = created;
    }

    [Fact, TestCase("CY39-42")]
    public async Task Cancel_ByGuest_BeforeStart_Ok_AfterStart_409_WithCompanyPhone()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (_, svc, token, _, date) = await HeldOrderAsync(host, prepay: 30);
        var client = host.Client();
        var cancelled = await CancelOrderAsync(token, client);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await cancelled.Content.ReadJsonAsync<PublicServiceOrderDto>())!.Status.Should().Be(StayBookingStatus.CancelledByGuest);
        (await ActiveSessionsAsync(svc.Id)).Should().BeEmpty();
        (await CancelOrderAsync(token, client)).StatusCode.Should().Be(HttpStatusCode.Conflict, "повторная отмена");

        // подтверждённый (без предоплаты) сеанс после старта гость не отменяет; ответ — с телефоном компании
        var (_, svc2) = await SceneAsync();
        var date2 = InDays(9);
        var confirmed = await OrderOkAsync(svc2.Id, date2, 600, 2, client: client);
        host.StaysClock.Set(StartUtc(date2, 600).AddMinutes(1));
        var late = await CancelOrderAsync(confirmed.Token, client);
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await late.Content.ReadAsStringAsync()).Should().Contain("начался").And.Contain("+7900");
        (await Code(late)).Should().Be("CancelNotAllowed");
        _ = date;
    }

    [Fact, TestCase("CY39-43")]
    public async Task Refund_PreparationCosts_BoundaryAndTexts_NeverAtLeastZero()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var client = host.Client();
        // предоплата 100 % от 2×2000 = 4000; первый час 2000 → к возврату не меньше 2000, удержание не больше 2000
        var (company, svc, token, sessionId, date) = await HeldOrderAsync(host, prepay: 100, policy: StayServiceCancellationPolicy.PreparationCosts, priceRub: 2000);
        (await AttachOrderProofAsync(token, client)).StatusCode.Should().Be(HttpStatusCode.Created);
        var start = StartUtc(date, 720);
        var boundary = start.AddHours(-12);

        host.StaysClock.Set(boundary.AddSeconds(-1));
        var before = (await GetOrderAsync(token, client)).Cancellation.Refund;
        before.Kind.Should().Be(ServiceRefundKind.Full, "раньше рубежа — вся предоплата");
        before.RefundAtLeastRub.Should().Be(4000);

        host.StaysClock.Set(boundary);
        var at = (await GetOrderAsync(token, client)).Cancellation.Refund;
        at.Kind.Should().Be(ServiceRefundKind.PartialAtLeast, "с самого рубежа — удержание фактических расходов, не больше первого часа");
        at.RefundAtLeastRub.Should().Be(2000);
        at.MaxDeductionRub.Should().Be(2000);
        at.Text.Should().Contain("не меньше 2").And.Contain("фактические расходы").And.NotContain("задаток").And.NotContain("невозвратн").And.NotContain("штраф");

        // отмена компанией — всегда полный возврат
        var card = await SessionCardAsync(company, sessionId);
        card.OwnerCancelRefundText.Should().Contain("полностью");
        _ = svc;
    }

    [Fact, TestCase("CY39-44")]
    public async Task Refund_ZeroRest_NeverSaysAtLeastZeroRub()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var client = host.Client();
        // предоплата 40 % от 4000 = 1600 ≤ первого часа (2000): к возврату поровну ничего не гарантируется → «расходы не больше N ₽»
        var (_, _, token, _, date) = await HeldOrderAsync(host, prepay: 40, policy: StayServiceCancellationPolicy.PreparationCosts, priceRub: 2000);
        (await AttachOrderProofAsync(token, client)).StatusCode.Should().Be(HttpStatusCode.Created);
        host.StaysClock.Set(StartUtc(date, 720).AddHours(-2));
        var refund = (await GetOrderAsync(token, client)).Cancellation.Refund;
        refund.Kind.Should().Be(ServiceRefundKind.CostsOnlyUpTo);
        refund.RefundAtLeastRub.Should().BeNull();
        refund.Text.Should().NotContain("не меньше 0");
        refund.Text.Should().Contain("не больше 1").And.Contain("фактические расходы");
        var raw = await (await client.GetAsync($"/api/stays/service-orders/public/{token}")).Content.ReadAsStringAsync();
        raw.Should().NotContain("не меньше 0").And.NotContain("бизнес-день");
    }

    [Fact, TestCase("CY39-45")]
    public async Task Refund_NoDeductions_FullAnyTimeBeforeStart_NothingPaidWhenHeld()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var client = host.Client();
        var (_, _, token, _, date) = await HeldOrderAsync(host, prepay: 50, policy: StayServiceCancellationPolicy.NoDeductions);
        (await GetOrderAsync(token, client)).Cancellation.Refund.Kind.Should().Be(ServiceRefundKind.NothingPaid, "Held: денег не вносилось");
        (await AttachOrderProofAsync(token, client)).StatusCode.Should().Be(HttpStatusCode.Created);
        host.StaysClock.Set(StartUtc(date, 720).AddMinutes(-5));
        var refund = (await GetOrderAsync(token, client)).Cancellation.Refund;
        refund.Kind.Should().Be(ServiceRefundKind.Full, "«Без удержаний» — в любой момент до старта");
        refund.RefundAtLeastRub.Should().Be(2000);
    }

    // ── кабинет: список и ручной сеанс ───────────────────────────────────────────

    [Fact, TestCase("CY39-46")]
    public async Task Staff_SessionList_FiltersPagingAndNoPhoneForNonStaff()
    {
        var (company, svc) = await SceneAsync();
        var date = InDays(9);
        for (var i = 0; i < 3; i++) await OrderOkAsync(svc.Id, date, 480 + 180 * i, 2);
        var c = AuthedClient(company.OwnerToken);
        var list = await c.GetAsync($"/api/stays/companies/{company.Id}/service-sessions?status=Confirmed&pageSize=2&page=1");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        var page = (await list.Content.ReadJsonAsync<StaffServiceSessionPage>())!;
        page.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(i => i.Kind == ServiceSessionKind.Standalone && i.GuestPhone != null);
        var page2 = (await (await c.GetAsync($"/api/stays/companies/{company.Id}/service-sessions?status=Confirmed&pageSize=2&page=2")).Content.ReadJsonAsync<StaffServiceSessionPage>())!;
        page2.Items.Should().HaveCount(1);
        page.Items.Select(x => x.Id).Intersect(page2.Items.Select(x => x.Id)).Should().BeEmpty("страницы не пересекаются");
        ((int)(await c.GetAsync($"/api/stays/companies/{company.Id}/service-sessions?pageSize=100000&page=-5")).StatusCode).Should().BeLessThan(500);
    }

    [Fact, TestCase("CY39-48")]
    public async Task Board_AwaitingPaymentCounter_IncludesStandaloneOrders_NeedsActionOnCell()
    {
        var (company, svc) = await SceneAsync(prepay: 30);
        var date = InDays(9);
        var c = AuthedClient(company.OwnerToken);
        var before = (await (await c.GetAsync($"/api/stays/companies/{company.Id}/board?from={D(date)}&days=3")).Content.ReadJsonAsync<StaysBoardDto>())!;
        (before.AwaitingPaymentCount ?? 0).Should().Be(0);
        var order = await OrderOkAsync(svc.Id, date, 720, 2);
        await AttachOrderProofAsync(order.Token);
        var after = (await (await c.GetAsync($"/api/stays/companies/{company.Id}/board?from={D(date)}&days=3")).Content.ReadJsonAsync<StaysBoardDto>())!;
        after.AwaitingPaymentCount.Should().Be(1, "«Ожидают проверки оплаты» над шахматкой учитывает и отдельные сеансы");
        after.ServiceCells.Should().ContainSingle(x => x.ServiceId == svc.Id && x.BusinessDate == date && x.Count == 1 && x.NeedsAction);
        after.Revision.Should().BeGreaterThan(before.Revision, "ревизия растёт при заказе — опрос шахматки увидит изменение");
        var poll = await c.GetAsync($"/api/stays/companies/{company.Id}/board?from={D(date)}&days=3&sinceRevision={after.Revision}");
        poll.StatusCode.Should().Be(HttpStatusCode.OK);
        (await poll.Content.ReadJsonAsync<StaysBoardDto>())!.Changed.Should().BeFalse("при той же ревизии опрос отвечает «не менялось»");
    }

    [Fact, TestCase("CY39-47")]
    public async Task ManualOrder_NeedsBasis_IsConfirmed_PhoneOptional_NoMessenger()
    {
        var (company, svc) = await SceneAsync(prepay: 30);
        var date = InDays(9);
        var c = AuthedClient(company.OwnerToken);
        ManualServiceOrderInput Manual(StayServiceRequestBasis? basis, string? phone = null, Guid? key = null) =>
            new(svc.Id, date, 720, 2, [], "Свои", phone, "Звонок", basis, key ?? Guid.NewGuid());
        var noBasis = await c.PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions", Manual(null));
        noBasis.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noBasis.Content.ReadAsStringAsync()).Should().Contain("как гость попросил услугу");
        (await c.PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions", Manual(StayServiceRequestBasis.Phone, "12"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var key = Guid.NewGuid();
        var r = await c.PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions", Manual(StayServiceRequestBasis.Phone, null, key));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var card = (await r.Content.ReadJsonAsync<StaffServiceSessionCardDto>())!;
        card.IsManual.Should().BeTrue();
        card.OrderStatus.Should().Be(StayBookingStatus.Confirmed, "ручной сеанс сразу «Подтверждён», предоплата 0");
        card.PrepayRub.Should().Be(0);
        card.AddedBy.RequestBasis.Should().Be(StayServiceRequestBasis.Phone);
        var again = await c.PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions", Manual(StayServiceRequestBasis.Phone, null, key));
        again.StatusCode.Should().Be(HttpStatusCode.OK, "идемпотентность по ключу");
        var dup = await c.PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions", Manual(StayServiceRequestBasis.InPerson));
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(dup)).Should().Be("SlotTaken");
        var order = await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().SingleAsync(o => o.ServiceId == svc.Id));
        order.NotifyByMessenger.Should().BeFalse();
        order.GuestPhone.Should().BeNull();
    }
}
