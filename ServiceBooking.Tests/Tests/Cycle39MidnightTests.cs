using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39, «Вызов 2»: сеансы через полночь и граница бизнес-дня (Р39-16, SPEC §4.1–§4.4, §4.9, US-39-02/04/08/14/16/18; ARCHITECTURE_CYCLE39.md §39.3, §39.6.1). Баня «Пт 22:00–01:00»:
/// цена по дню начала сеанса, граница 06:00, зазор через сутки и через границу бизнес-дня, две календарные даты гостю, «сегодня» после полуночи. Часы — <see cref="FakeStaysClock"/>.
/// </summary>
public class Cycle39MidnightTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private const int FridayBit = 0b0010000;
    private const int SaturdayBit = 0b0100000;

    /// <summary>Баня с окном Пт 18:00–06:00(след.) и Сб 06:00–… ; цены: Пт 18–26 → 2000, Сб 18–26 → 3000; шаг 30 минут.</summary>
    private async Task<(StaysCtx Company, SvcCtx Svc, DateOnly Friday, DateOnly Saturday)> SceneAsync(
        int fridayFrom = 6, int fridayTo = 30, int buffer = 30, int step = 30, int minHours = 1, (int, int)[]? fridayWindows = null)
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, priceRub: null, buffer: buffer, step: step, minHours: minHours, maxHours: 6, publish: false);
        await AddRuleAsync(company, svc.Id, FridayBit, fridayFrom, fridayTo, 2000);
        await AddRuleAsync(company, svc.Id, SaturdayBit, 18, 26, 3000);
        await AddRuleAsync(company, svc.Id, SaturdayBit, 6, 18, 1000);
        var days = Enumerable.Range(1, 7).Select(d => new WeeklyDayInput(d,
            d == 5 ? (fridayWindows ?? [(1080, 1800)]).Select(w => new ServiceWindowInput(w.Item1, w.Item2)).ToList()
            : d == 6 ? [new ServiceWindowInput(360, 1560)] : [new ServiceWindowInput(480, 1320)])).ToList();
        (await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/weekly-schedule", new WeeklyScheduleInput(days)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var pub = await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/publish", new EmptyInput());
        pub.StatusCode.Should().Be(HttpStatusCode.OK, await pub.Content.ReadAsStringAsync());
        var friday = NextWeekday(DayOfWeek.Friday, 10);
        return (company, svc, friday, friday.AddDays(1));
    }

    [Fact, TestCase("CY39-70")]
    public async Task Friday_22to01_PricedByStartBusinessDay_FridayRules_NotSaturdays()
    {
        var (_, svc, friday, _) = await SceneAsync();
        var quote = await QuoteServiceAsync(svc.Id, friday, 1320, 3); // Пт 22:00 – Сб 01:00
        quote.Ok.Should().BeTrue(string.Join("; ", quote.Problems.Select(p => p.Message)));
        quote.TotalRub.Should().Be(6000, "часы 22–23, 23–24 и 00–01 — все по правилу пятницы: 3 × 2 000");
        quote.HourPrices.Should().HaveCount(3).And.OnlyContain(h => h.PriceRub == 2000);
        quote.Time!.StartUtc.Should().Be(StartUtc(friday, 1320));
        quote.Time.EndUtc.Should().Be(StartUtc(friday, 1500));
        quote.Time.EndMinute.Should().Be(1500);

        // старт «ночь на субботу» 00:30 — тоже правила пятницы
        var night = await QuoteServiceAsync(svc.Id, friday, 1470, 2); // Пт 00:30 (ночь на сб) – 02:30
        night.Ok.Should().BeTrue(string.Join("; ", night.Problems.Select(p => p.Message)));
        night.TotalRub.Should().Be(4000);

        // суббота в 22:00 — по субботним правилам
        (await QuoteServiceAsync(svc.Id, friday.AddDays(1), 1320, 2)).TotalRub.Should().Be(6000, "суббота 18–26 → 3 000");
    }

    [Fact, TestCase("CY39-71")]
    public async Task GuestSeesTwoCalendarDates_NeverBusinessDayWords_StaffSeesSpecForm()
    {
        var (company, svc, friday, _) = await SceneAsync();
        var created = await OrderOkAsync(svc.Id, friday, 1320, 3);
        var guestLabel = created.Order.Time.Label;
        var sat = friday.AddDays(1);
        // «пт 15 янв, 22:00 — сб 16 янв, 01:00»
        guestLabel.Should().MatchRegex(@"^пт \d{1,2} \p{L}+, 22:00 — сб \d{1,2} \p{L}+, 01:00$", "двумя календарными датами (ЮР39-8)");
        guestLabel.Should().Contain($"сб {sat.Day} ");
        var raw = await (await AnonymousClient().GetAsync($"/api/stays/service-orders/public/{created.Token}")).Content.ReadAsStringAsync();
        raw.Should().NotContainEquivalentOf("бизнес").And.NotContain("часы 6").And.NotContain("(след. дня)", "формат «след. дня» — только для персонала-редактора");
        foreach (var path in new[] { $"/api/stays/public/services/{svc.Id}/starts?date={D(friday)}", $"/api/stays/public/companies/{company.Slug}/services/{svc.Slug}" })
            (await (await AnonymousClient().GetAsync(path)).Content.ReadAsStringAsync()).Should().NotContainEquivalentOf("бизнес-день");

        // персонал — по SPEC §4.9: «Пт, 15 янв · 22:00 – 01:00 (сб)»
        var sessionId = await SessionIdOfOrderAsync(created.Token);
        var card = await SessionCardAsync(company, sessionId);
        card.Time.Label.Should().MatchRegex(@"^Пт, \d{1,2} \p{L}+ · 22:00 – 01:00 \(сб\)$");
        card.PreparedUntilLabel.Should().Contain("01:30").And.Contain("сб");

        // старт после полуночи
        var nightOrder = await OrderOkAsync(svc.Id, friday, 1740, 1); // 05:00 ночь на сб
        var nightCard = await SessionCardAsync(company, await SessionIdOfOrderAsync(nightOrder.Token));
        nightCard.Time.Label.Should().Contain("05:00 (ночь на сб)");
        nightOrder.Order.Time.Label.Should().MatchRegex(@"^сб \d{1,2} \p{L}+, 05:00 — 06:00$", "гостю начало после полуночи — своей календарной датой");
    }

    [Fact, TestCase("CY39-72")]
    public async Task OnlyFridayRuleToMidnight_Session23to01_IsUnavailable_NoPriceForHour0()
    {
        var (_, svc, friday, _) = await SceneAsync(fridayFrom: 18, fridayTo: 24);
        var quote = await QuoteServiceAsync(svc.Id, friday, 1380, 2); // 23:00–01:00: у часа 00:00–01:00 нет цены
        quote.Ok.Should().BeFalse();
        quote.Problems.Should().Contain(p => p.Code == ServiceRefusalCode.NoPriceForHours || p.Code == ServiceRefusalCode.StartUnavailable);
        var starts = (await StartsAsync(svc.Id, friday)).Starts;
        starts.Single(s => s.StartMinute == 1380).MaxHours.Should().Be(1, "при правиле «Пт 18–24» 2-часовой сеанс с 23:00 недоступен: у часа 00:00 нет цены");
    }

    [Fact, TestCase("CY39-73")]
    public async Task OnlyFridayRuleToMidnight_ShorterSessionBeforeMidnight_StillAvailable_MaxHoursStopsAtMidnight()
    {
        var (_, svc, friday, _) = await SceneAsync(fridayFrom: 18, fridayTo: 24);
        var starts = (await StartsAsync(svc.Id, friday)).Starts;
        starts.Single(s => s.StartMinute == 1320).MaxHours.Should().Be(2, "22:00 → не дольше полуночи: часы без цены дальше обрезают максимум");
        starts.Single(s => s.StartMinute == 1380).MaxHours.Should().Be(1);
        starts.Select(s => s.StartMinute).Should().NotContain(1440).And.NotContain(1470, "после полуночи цен нет вовсе");
        var r = await PostOrderAsync(svc.Id, OrderInput(friday, 1380, 2, 4000, UniquePhone()));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(r)).Should().BeOneOf("NoPriceForHours", "StartUnavailable");
    }

    [Fact, TestCase("CY39-74")]
    public async Task BufferCrossesBusinessDayBorder_Friday04to06_BlocksSaturday0600()
    {
        var (company, svc, friday, saturday) = await SceneAsync();
        await OrderOkAsync(svc.Id, friday, 1680, 2); // Пт 04:00–06:00 (ночь на сб), зазор до 06:30
        var saturdayStarts = (await StartsAsync(svc.Id, saturday)).Starts.Select(s => s.StartMinute).ToList();
        saturdayStarts.Should().NotContain(360, "старт субботы 06:00 попадает в зазор пятничного сеанса");
        // 06:30 = ровно конец зазора → разрешено (полуоткрытый интервал)
        saturdayStarts.Should().Contain(390);
        var r = await PostOrderAsync(svc.Id, OrderInput(saturday, 360, 1, 1000, UniquePhone()));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(r)).Should().Be("SlotTaken");
        _ = company;
    }

    [Fact, TestCase("CY39-75")]
    public async Task BufferEndsExactlyAtNextStart_HalfOpenInterval_Allowed()
    {
        var (_, svc, friday, saturday) = await SceneAsync();
        await OrderOkAsync(svc.Id, friday, 1680, 2); // занято до 06:30 субботы
        var q = await QuoteServiceAsync(svc.Id, saturday, 390, 1);
        q.Ok.Should().BeTrue(string.Join("; ", q.Problems.Select(p => p.Message)));
        var r = await PostOrderAsync(svc.Id, OrderInput(saturday, 390, 1, q.TotalRub, UniquePhone()));
        r.StatusCode.Should().Be(HttpStatusCode.Created, "06:30 — ровно конец зазора: интервалы [start, end+buffer) не пересекаются");
        var before = await PostOrderAsync(svc.Id, OrderInput(saturday, 360, 1, 1000, UniquePhone()));
        before.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY39-76")]
    public async Task Fri2300to0100_And_Fri0030Night_AreSameRealTime_SecondRefused()
    {
        var (_, svc, friday, _) = await SceneAsync(buffer: 0);
        var first = await OrderOkAsync(svc.Id, friday, 1380, 2); // 23:00–01:00
        var starts = (await StartsAsync(svc.Id, friday)).Starts.Select(s => s.StartMinute).ToList();
        starts.Should().NotContain(1410).And.NotContain(1440).And.NotContain(1470 - 60, "всё внутри уже занятого");
        var second = await PostOrderAsync(svc.Id, OrderInput(friday, 1470, 2, 4000, UniquePhone())); // «00:30 (ночь на сб)» – 02:30
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(second)).Should().Be("SlotTaken");
        // а ровно после — можно: 01:00 (1500)
        (await PostOrderAsync(svc.Id, OrderInput(friday, 1500, 1, 2000, UniquePhone()))).StatusCode.Should().Be(HttpStatusCode.Created);
        _ = first;
    }

    [Fact, TestCase("CY39-77")]
    public async Task WindowCrossingMidnight_SecondWindowOfNextBusinessDay_DoesNotOverlap_ByConstruction()
    {
        var (company, svc, friday, saturday) = await SceneAsync(fridayWindows: [(480, 720), (1080, 1800)]);
        (await StartsAsync(svc.Id, friday)).Starts.Select(s => s.StartMinute).Should().Contain(480).And.Contain(1080).And.Contain(1740, "последний час окна: 05:00–06:00");
        (await StartsAsync(svc.Id, friday)).Starts.Single(s => s.StartMinute == 1740).MaxHours.Should().Be(1);
        // окно за границей бизнес-дня не принимается
        var bad = await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/date-overrides/{D(friday)}",
            new DateOverrideInput(false, [new ServiceWindowInput(1080, 1830)], null));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _ = saturday;
    }

    [Fact, TestCase("CY39-78")]
    public async Task MonthBorder_SessionAcrossMonthEnd_BelongsToLastDayOfMonth_TwoDatesToGuest()
    {
        var (_, svc, _, _) = await SceneAsync();
        // ближайший последний день месяца не ближе 5 дней
        var d = InDays(5);
        d = new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        var q = await QuoteServiceAsync(svc.Id, d, 1380, 2); // 23:00–01:00 следующего месяца
        q.Ok.Should().BeTrue(string.Join("; ", q.Problems.Select(p => p.Message)));
        q.Time!.BusinessDate.Should().Be(d);
        var next = d.AddDays(1);
        q.Time.Label.Should().Contain($"{next.Day} ", "вторая календарная дата — первое число следующего месяца");
        q.Time.EndUtc.Should().Be(StartUtc(d, 1380).AddHours(2));
    }

    [Fact, TestCase("CY39-79")]
    public async Task TodayAfterMidnight_IsStillYesterdaysBusinessDay_Until0600()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (_, svc, friday, saturday) = await SceneAsync();
        var client = host.Client();
        // Сб 01:00 по Шерегешу: «сегодня» для услуги — ещё пятница
        host.StaysClock.Set(LocalToUtc(saturday, 1));
        var page = await client.GetAsync($"/api/stays/public/services/{svc.Id}/availability?from={D(friday)}&days=2");
        var cal = (await page.Content.ReadJsonAsync<ServiceAvailabilityDto>())!;
        cal.Today.Should().Be(friday, "в 01:00 субботы дата «сегодня» для расписания услуг — пятница");
        var starts = await client.GetAsync($"/api/stays/public/services/{svc.Id}/starts?date={D(friday)}");
        var s = (await starts.Content.ReadJsonAsync<ServiceStartsDto>())!;
        s.Reason.Should().NotBe(StartsReason.DateInPast, "пятничный бизнес-день ещё идёт");
        s.Starts.Should().NotBeEmpty();
        s.Starts.Should().OnlyContain(x => x.StartUtc >= host.StaysClock.UtcNow, "прошедшие старты не показываются");

        // Сб 05:59 — всё ещё пятница; 06:00 — уже суббота
        host.StaysClock.Set(LocalToUtc(saturday, 5, 59));
        (await (await client.GetAsync($"/api/stays/public/services/{svc.Id}/availability?from={D(friday)}&days=1")).Content.ReadJsonAsync<ServiceAvailabilityDto>())!.Today.Should().Be(friday);
        host.StaysClock.Set(LocalToUtc(saturday, 6, 0));
        (await (await client.GetAsync($"/api/stays/public/services/{svc.Id}/availability?from={D(friday)}&days=1")).Content.ReadJsonAsync<ServiceAvailabilityDto>())!.Today.Should().Be(saturday);
        (await StartsAsync(svc.Id, friday, client)).Reason.Should().Be(StartsReason.DateInPast, "с 06:00 пятничный бизнес-день закончился");
    }

    [Fact, TestCase("CY39-80")]
    public async Task ServiceDay_CarryOverBuffer_ShownOnNextBusinessDay_FirstBar()
    {
        var (company, svc, friday, saturday) = await SceneAsync();
        await OrderOkAsync(svc.Id, friday, 1680, 2); // Пт 04:00–06:00, подготовка до 06:30 субботы
        var c = AuthedClient(company.OwnerToken);
        var fri = (await (await c.GetAsync($"/api/stays/companies/{company.Id}/service-day?date={D(friday)}")).Content.ReadJsonAsync<ServiceDayDto>())!;
        fri.Services.Single(x => x.Id == svc.Id).Bars.Should().Contain(b => b.Kind == ServiceDayBarKind.Session && b.StartMinute == 1680 && b.EndMinute == 1800);
        var sat = (await (await c.GetAsync($"/api/stays/companies/{company.Id}/service-day?date={D(saturday)}")).Content.ReadJsonAsync<ServiceDayDto>())!;
        var carry = sat.Services.Single(x => x.Id == svc.Id).Bars;
        carry.Should().Contain(b => b.Kind == ServiceDayBarKind.CarryOverBuffer && b.StartMinute == 360 && b.EndMinute == 390, "зазор за 06:00 виден и в следующем бизнес-дне первой полосой");
        carry.Where(b => b.Kind == ServiceDayBarKind.CarryOverBuffer).Should().OnlyContain(b => b.Label.Length > 0);
    }

    [Fact, TestCase("CY39-81")]
    public async Task Housekeeper_ScheduleShowsNightSession_OnBusinessDayOfStart_WithEndMarker()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, priceRub: 2000);
        var hk = await AddStaffAsync(company, "Housekeeper");
        var friday = NextWeekday(DayOfWeek.Friday, 10);
        await OrderOkAsync(svc.Id, friday, 1320, 3);
        var sched = await ScheduleAsync(company, hk.Token, friday.AddDays(-1), 4);
        sched.Days.Single(d => d.Date == friday).Sessions.Should().ContainSingle().Which.TimeLabel.Should().Contain("22:00").And.Contain("01:00");
        sched.Days.Single(d => d.Date == friday.AddDays(1)).Sessions.Should().BeNullOrEmpty("сеанс на бизнес-дне начала, а не по календарной дате конца");
    }
}
