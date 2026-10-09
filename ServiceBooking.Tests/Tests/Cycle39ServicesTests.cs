using System.Net;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39, «Вызов 2»: услуги-слоты «Домов» глазами владельца и гостя (SPEC_CYCLE39_STAYS_SLOTS_ICAL.md US-39-01…06, 26; ARCHITECTURE_CYCLE39.md §39.2–§39.3, §39.12).
/// Написано по SPEC и contracts/cycle39/openapi.yaml, не по реализации. Сеансы, заказы и деньги — Cycle39Order*/Cycle39Session*; здесь: карточка, расписание, цены, позиции,
/// публикация, права и изоляция.
/// </summary>
public class Cycle39ServicesTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private string Base(StaysCtx c) => $"/api/stays/companies/{c.Id}/services";

    // ── карточка ─────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-01")]
    public async Task Create_List_Setup_Validation_And_Defaults()
    {
        var company = await CreateStaysCompanyAsync();
        var c = AuthedClient(company.OwnerToken);

        (await c.PostJsonAsync(Base(company), new ServiceCreateInput("  "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostJsonAsync(Base(company), new ServiceCreateInput(new string('я', 101)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var created = await c.PostJsonAsync(Base(company), new ServiceCreateInput("Баня"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var svc = (await created.Content.ReadJsonAsync<ServiceManageDto>())!;
        // значения по умолчанию SPEC §4.6 (зазор 30 мин — Р39-3; гостю не показывается; не опубликована; доступна для броней домов)
        svc.BufferMinutes.Should().Be(30);
        svc.ShowBufferToGuests.Should().BeFalse();
        svc.IsPublished.Should().BeFalse();
        svc.AvailableForHouseBookings.Should().BeTrue();
        svc.StandalonePrepayPercent.Should().BeNull("предоплата по умолчанию «нет»");
        svc.CancellationPolicy.Should().Be(StayServiceCancellationPolicy.NoDeductions, "«Без удержаний» по умолчанию");
        svc.CancellationBoundaryHours.Should().Be(12);
        svc.CancellationBoundaryRange.Min.Should().Be(3);
        svc.CancellationBoundaryRange.Max.Should().Be(24);
        svc.PublishProblems.Should().NotBeEmpty("без цены и окон опубликовать нельзя");

        ServiceSetupInput Setup(Func<ServiceSetupInput, ServiceSetupInput> f) => f(new("Баня", Unique("svc-").ToLowerInvariant(), 2, 6, 60, 30, false, 60, null, StayServiceCancellationPolicy.NoDeductions, 12, true));
        async Task<HttpStatusCode> Put(ServiceSetupInput s) => (await c.PutJsonAsync($"{Base(company)}/{svc.Id}/setup", s)).StatusCode;

        (await Put(Setup(s => s))).Should().Be(HttpStatusCode.OK);
        (await Put(Setup(s => s with { MinHours = 0 }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Setup(s => s with { MinHours = 13, MaxHours = 13 }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Setup(s => s with { MinHours = 4, MaxHours = 3 }))).Should().Be(HttpStatusCode.BadRequest, "максимум не меньше минимума");
        (await Put(Setup(s => s with { StepMinutes = 45 }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Setup(s => s with { BufferMinutes = 20 }))).Should().Be(HttpStatusCode.BadRequest, "зазор — шаг 15");
        (await Put(Setup(s => s with { BufferMinutes = 255 }))).Should().Be(HttpStatusCode.BadRequest, "зазор до 240");
        (await Put(Setup(s => s with { BufferMinutes = 0 }))).Should().Be(HttpStatusCode.OK, "нулевой зазор допустим");
        (await Put(Setup(s => s with { MinLeadMinutes = 45 }))).Should().Be(HttpStatusCode.BadRequest, "шаг 30 минут");
        (await Put(Setup(s => s with { MinLeadMinutes = 2910 }))).Should().Be(HttpStatusCode.BadRequest, "не больше 48 часов");
        (await Put(Setup(s => s with { StandalonePrepayPercent = 0 }))).Should().Be(HttpStatusCode.BadRequest, "предоплата 1…100 или «нет»");
        (await Put(Setup(s => s with { StandalonePrepayPercent = 101 }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Setup(s => s with { StandalonePrepayPercent = 100 }))).Should().Be(HttpStatusCode.OK);
        (await Put(Setup(s => s with { CancellationBoundaryHours = 2 }))).Should().Be(HttpStatusCode.BadRequest, "рубеж 3…24 ч");
        (await Put(Setup(s => s with { CancellationBoundaryHours = 25 }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Setup(s => s with { CancellationBoundaryHours = 3, CancellationPolicy = StayServiceCancellationPolicy.PreparationCosts }))).Should().Be(HttpStatusCode.OK);
        (await Put(Setup(s => s with { CancellationPolicy = (StayServiceCancellationPolicy)7 }))).Should().Be(HttpStatusCode.BadRequest, "шаблона «Стандартный» не существует");
        (await Put(Setup(s => s with { Slug = "A" }))).Should().Be(HttpStatusCode.BadRequest);

        var list = await c.GetAsync(Base(company));
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadJsonAsync<List<ServiceListItemDto>>())!.Should().ContainSingle(x => x.Id == svc.Id);
    }

    [Fact, TestCase("CY39-02")]
    public async Task SlugTaken_Conflict_And_DescriptionLimit()
    {
        var company = await CreateStaysCompanyAsync();
        var a = await CreateServiceAsync(company, "Баня", publish: false);
        var b = await CreateServiceAsync(company, "Чан", publish: false);
        var c = AuthedClient(company.OwnerToken);
        var r = await c.PutJsonAsync($"{Base(company)}/{b.Id}/setup",
            new ServiceSetupInput("Чан", a.Slug, 2, 6, 60, 30, false, 0, null, StayServiceCancellationPolicy.NoDeductions, 12, true));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(r)).Should().Be("SlugTaken");

        (await c.PutJsonAsync($"{Base(company)}/{a.Id}/content", new ServiceContentInput(new string('а', 2000)))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.PutJsonAsync($"{Base(company)}/{a.Id}/content", new ServiceContentInput(new string('а', 2001)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY39-03")]
    public async Task ServiceLimit_TwentyPerCompany_AndDeleteVsArchive()
    {
        var company = await CreateStaysCompanyAsync();
        var c = AuthedClient(company.OwnerToken);
        for (var i = 0; i < 20; i++)
            (await c.PostJsonAsync(Base(company), new ServiceCreateInput($"Услуга {i}"))).StatusCode.Should().Be(HttpStatusCode.Created);
        var over = await c.PostJsonAsync(Base(company), new ServiceCreateInput("Лишняя"));
        over.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(over)).Should().Be("ServiceLimitReached");

        // услуга без сеансов удаляется; услуга с сеансом — только в архив
        var other = await CreateStaysCompanyAsync();
        var plain = await CreateServiceAsync(other, publish: false);
        (await AuthedClient(other.OwnerToken).DeleteAsync($"{Base(other)}/{plain.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AuthedClient(other.OwnerToken).GetAsync($"{Base(other)}/{plain.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var used = await CreateServiceAsync(other);
        await EnableOrdersWithoutStayAsync(other);
        await OrderOkAsync(used.Id, InDays(10), 720, 2);
        var del = await AuthedClient(other.OwnerToken).DeleteAsync($"{Base(other)}/{used.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(del)).Should().Be("ServiceHasSessions");

        var archived = await AuthedClient(other.OwnerToken).PostJsonAsync($"{Base(other)}/{used.Id}/archive", new EmptyInput());
        archived.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await archived.Content.ReadJsonAsync<ServiceManageDto>())!;
        dto.IsArchived.Should().BeTrue();
        dto.IsPublished.Should().BeFalse("архивная услуга пропадает с публичных страниц");
        // API_CONTRACT_CYCLE39 §39.22.1: страница архивной услуги — 200 с available=false; из списка компании и из выбора времени она пропадает
        var archivedPage = await AnonymousClient().GetAsync($"/api/stays/public/companies/{other.Slug}/services/{used.Slug}");
        archivedPage.StatusCode.Should().Be(HttpStatusCode.OK);
        (await archivedPage.Content.ReadJsonAsync<PublicServiceDto>())!.Available.Should().BeFalse();
        (await (await AnonymousClient().GetAsync($"/api/stays/public/companies/{other.Slug}")).Content.ReadJsonAsync<PublicStaysCompanyDto>())!
            .Services.Should().NotContain(x => x.Id == used.Id);
        (await AnonymousClient().GetAsync($"/api/stays/public/services/{used.Id}/starts?date={D(InDays(10))}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ActiveSessionsAsync(used.Id)).Should().HaveCount(1, "сеансы архивной услуги сохраняются");
        var republish = await AuthedClient(other.OwnerToken).PostJsonAsync($"{Base(other)}/{used.Id}/publish", new EmptyInput());
        republish.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(republish)).Should().Be("ServiceArchived");
    }

    // ── публикация ───────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-04")]
    public async Task Publish_Requires_PriceRule_And_Windows_WithReasons_And_Unpublish_HidesFromGuests()
    {
        var company = await CreateStaysCompanyAsync();
        var c = AuthedClient(company.OwnerToken);
        var noPrice = await CreateServiceAsync(company, priceRub: null, publish: false);
        var r1 = await c.PostJsonAsync($"{Base(company)}/{noPrice.Id}/publish", new EmptyInput());
        r1.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(r1)).Should().Be("ServiceNoPrice");

        var created = (await (await c.PostJsonAsync(Base(company), new ServiceCreateInput("Чан"))).Content.ReadJsonAsync<ServiceManageDto>())!;
        await AddRuleAsync(company, created.Id, 127, 6, 30, 1500);
        var r2 = await c.PostJsonAsync($"{Base(company)}/{created.Id}/publish", new EmptyInput());
        r2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(r2)).Should().Be("ServiceNoWindows", "правило есть, окон ни в шаблоне, ни в ручных датах нет");
        (await r2.Content.ReadAsStringAsync()).Should().Contain("свободное время");

        var ok = await CreateServiceAsync(company, "Фурако");
        ok.Service.IsPublished.Should().BeTrue();
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/services/{ok.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await c.PostJsonAsync($"{Base(company)}/{ok.Id}/unpublish", new EmptyInput())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/services/{ok.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync($"/api/stays/public/services/{ok.Id}/starts?date={D(InDays(5))}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var pub = (await (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}")).Content.ReadJsonAsync<PublicStaysCompanyDto>())!;
        (pub.Services ?? []).Should().NotContain(s => s.Id == ok.Id, "неопубликованная услуга гостям не видна");
        // а персоналу видна
        (await GetServiceAsync(company, ok.Id)).IsPublished.Should().BeFalse();
    }

    // ── расписание ───────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-05")]
    public async Task WeeklySchedule_Windows_Validation_And_MidnightWindow()
    {
        var company = await CreateStaysCompanyAsync();
        var svc = await CreateServiceAsync(company, publish: false);
        var c = AuthedClient(company.OwnerToken);
        string Url() => $"{Base(company)}/{svc.Id}/weekly-schedule";
        Task<HttpResponseMessage> Put(params (int Start, int End)[] w) =>
            c.PutJsonAsync(Url(), new WeeklyScheduleInput(Enumerable.Range(1, 7)
                .Select(d => new WeeklyDayInput(d, d == 5 ? w.Select(x => new ServiceWindowInput(x.Start, x.End)).ToList() : [new ServiceWindowInput(600, 900)])).ToList()));

        var midnight = await Put((1080, 1560)); // 18:00 – 02:00 (след. дня)
        midnight.StatusCode.Should().Be(HttpStatusCode.OK, await midnight.Content.ReadAsStringAsync());
        var saved = (await midnight.Content.ReadJsonAsync<ScheduleSaveResultDto>())!;
        saved.Weekly!.Days.Single(d => d.DayOfWeek == 5).Windows.Should().ContainSingle().Which.Label.Should().Contain("след. дня");

        var before = await Put((300, 600)); // начало раньше 06:00
        before.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await before.Content.ReadAsStringAsync()).Should().Contain("Окно должно уложиться с 06:00 до 06:00 следующего дня");
        (await Put((1080, 1801))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "позже 06:00 следующих суток");
        (await Put((1080, 1800))).StatusCode.Should().Be(HttpStatusCode.OK, "ровно до 06:00 следующих суток можно");
        (await Put((600, 600))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Put((900, 800))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Put((615, 800))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "шаг 30 минут");
        var overlap = await Put((600, 780), (720, 900));
        overlap.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await overlap.Content.ReadAsStringAsync()).Should().Contain("пересекаются");
        (await Put((600, 720), (720, 840))).StatusCode.Should().Be(HttpStatusCode.OK, "соприкасающиеся окна допустимы");
        (await Put((360, 420), (450, 510), (600, 690), (900, 960))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Put((420, 480), (600, 720), (810, 900))).StatusCode.Should().Be(HttpStatusCode.OK, "три окна — максимум");
        (await Put()).StatusCode.Should().Be(HttpStatusCode.OK, "день можно закрыть: без окон");

        var weekly = (await (await c.GetAsync(Url())).Content.ReadJsonAsync<WeeklyScheduleDto>())!;
        weekly.Days.Should().HaveCount(7);
        weekly.Days.Single(d => d.DayOfWeek == 5).Windows.Should().BeEmpty();
    }

    [Fact, TestCase("CY39-06")]
    public async Task WeeklyTemplate_AppliesToFutureDaysAtOnce_And_ManualDateReplacesTemplate()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, minHours: 2, windows: [(720, 960)]); // 12:00–16:00
        var date = InDays(8);

        var s1 = await StartsAsync(svc.Id, date);
        s1.Starts.Select(s => s.StartMinute).Should().Equal(720, 780, 840); // 12, 13, 14 (два часа до 16:00)

        // шаблон меняется — будущее меняется сразу, без шага «применить» (§4.2 п. 5)
        var c = AuthedClient(company.OwnerToken);
        var days = Enumerable.Range(1, 7).Select(d => new WeeklyDayInput(d, [new ServiceWindowInput(1080, 1260)])).ToList();
        (await c.PutJsonAsync($"{Base(company)}/{svc.Id}/weekly-schedule", new WeeklyScheduleInput(days))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StartsAsync(svc.Id, date)).Starts.Select(s => s.StartMinute).Should().Equal(1080, 1140);

        // ручная дата ПОЛНОСТЬЮ заменяет шаблон
        var put = await c.PutJsonAsync($"{Base(company)}/{svc.Id}/date-overrides/{D(date)}", new DateOverrideInput(false, [new ServiceWindowInput(600, 780)], "Праздник"));
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        (await StartsAsync(svc.Id, date)).Starts.Select(s => s.StartMinute).Should().Equal(600, 660);
        (await StartsAsync(svc.Id, date.AddDays(1))).Starts.Select(s => s.StartMinute).Should().Equal(new[] { 1080, 1140 }, "соседний день по шаблону");

        // закрыто
        (await c.PutJsonAsync($"{Base(company)}/{svc.Id}/date-overrides/{D(date)}", new DateOverrideInput(true, [], null))).StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await StartsAsync(svc.Id, date);
        closed.Starts.Should().BeEmpty();
        closed.Reason.Should().Be(StartsReason.Closed);
        closed.NoStartsText.Should().NotBeNullOrWhiteSpace();

        // удаление ручной даты возвращает шаблон; повторное удаление — не ошибка сервера
        (await c.DeleteAsync($"{Base(company)}/{svc.Id}/date-overrides/{D(date)}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StartsAsync(svc.Id, date)).Starts.Select(s => s.StartMinute).Should().Equal(1080, 1140);
        ((int)(await c.DeleteAsync($"{Base(company)}/{svc.Id}/date-overrides/{D(date)}")).StatusCode).Should().BeLessThan(500);

        // календарь месяца
        var month = await c.GetAsync($"{Base(company)}/{svc.Id}/date-overrides?month={date:yyyy-MM}");
        month.StatusCode.Should().Be(HttpStatusCode.OK);
        (await month.Content.ReadJsonAsync<ServiceMonthDto>())!.Days.Should().NotBeEmpty();
        (await c.GetAsync($"{Base(company)}/{svc.Id}/date-overrides?month=2026-13")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutJsonAsync($"{Base(company)}/{svc.Id}/date-overrides/2026-02-30", new DateOverrideInput(true, [], null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutJsonAsync($"{Base(company)}/{svc.Id}/date-overrides/{D(date)}", new DateOverrideInput(false, [new ServiceWindowInput(300, 400)], null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "ручная дата подчиняется тем же границам окна");
    }

    [Fact, TestCase("CY39-07")]
    public async Task ScheduleChange_DoesNotCancelSessions_ButWarnsAboutSessionsOutsideNewWindows()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, windows: [(720, 1200)]);
        var date = InDays(9);
        var order = await OrderOkAsync(svc.Id, date, 780, 2);

        var c = AuthedClient(company.OwnerToken);
        var r = await c.PutJsonAsync($"{Base(company)}/{svc.Id}/date-overrides/{D(date)}", new DateOverrideInput(true, [], "Закрыто на ремонт"));
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await r.Content.ReadJsonAsync<ScheduleSaveResultDto>())!;
        result.OutsideSessions.Should().ContainSingle();
        result.WarningText.Should().NotBeNullOrWhiteSpace().And.Contain("1");

        (await GetOrderAsync(order.Token)).Status.Should().Be(StayBookingStatus.Confirmed, "изменение расписания сеанс не отменяет");
        (await ActiveSessionsAsync(svc.Id)).Should().HaveCount(1);
    }

    // ── цены ─────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-08")]
    public async Task PriceRules_Validation_Overlap_And_Matrix()
    {
        var company = await CreateStaysCompanyAsync();
        var svc = await CreateServiceAsync(company, priceRub: null, publish: false);
        var first = await AddRuleAsync(company, svc.Id, 0b0010000, 18, 26, 2000); // пятница 18:00 – 02:00 (след. дня)
        first.Rules.Should().ContainSingle().Which.Label.Should().Contain("след. дня");

        var overlap = await AddRuleRawAsync(company, svc.Id, 0b0110000, 20, 24, 3000);
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = (await overlap.Content.ReadJsonAsync<StaysServiceConflictDto>())!;
        body.Code.Should().Be("PriceRuleOverlap");
        body.ConflictingRule.Should().NotBeNull();
        body.ConflictingRule!.Id.Should().Be(first.Rules.Single().Id, "конфликт указывает на правило, с которым пересеклось");

        (await AddRuleRawAsync(company, svc.Id, 0b0100000, 20, 24, 3000)).StatusCode.Should().Be(HttpStatusCode.Created, "суббота — другой день недели");
        (await AddRuleRawAsync(company, svc.Id, 0b0010000, 26, 28, 3000)).StatusCode.Should().Be(HttpStatusCode.Created, "соседнее по часам правило той же пятницы");
        (await AddRuleRawAsync(company, svc.Id, 0, 8, 10, 100)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "не выбраны дни");
        (await AddRuleRawAsync(company, svc.Id, 128, 8, 10, 100)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AddRuleRawAsync(company, svc.Id, 1, 5, 10, 100)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "раньше 06:00");
        (await AddRuleRawAsync(company, svc.Id, 1, 8, 31, 100)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "позже 06:00 следующих суток");
        (await AddRuleRawAsync(company, svc.Id, 1, 10, 10, 100)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AddRuleRawAsync(company, svc.Id, 1, 8, 10, 0)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "цена от 1 ₽");
        (await AddRuleRawAsync(company, svc.Id, 1, 8, 10, 100_001)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AddRuleRawAsync(company, svc.Id, 1, 8, 10, 100_000)).StatusCode.Should().Be(HttpStatusCode.Created);

        var rules = await (await AuthedClient(company.OwnerToken).GetAsync($"{Base(company)}/{svc.Id}/price-rules")).Content.ReadJsonAsync<PriceRulesDto>();
        rules!.Matrix.Should().HaveCount(7, "таблица «день недели × час»");
        rules.Matrix.First().Cells.Should().HaveCount(24, "24 часа бизнес-дня: 06:00 … 06:00 следующих суток");
        rules.Matrix.First().Cells.First().Hour.Should().Be(6);
        rules.Matrix.First().Cells.Last().Hour.Should().Be(29);
        var friday = rules.Matrix.Single(r => r.DayOfWeek == 5);
        friday.Cells.Single(x => x.Hour == 25).PriceRub.Should().Be(2000, "час 01:00 следующих суток тарифицируется правилом пятницы");
        friday.Cells.Single(x => x.Hour == 10).PriceRub.Should().BeNull();

        // правка и удаление правил
        var c = AuthedClient(company.OwnerToken);
        var id = first.Rules.Single().Id;
        (await c.PutJsonAsync($"{Base(company)}/{svc.Id}/price-rules/{id}", new PriceRuleInput(0b0010000, 18, 24, 2500))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.DeleteAsync($"{Base(company)}/{svc.Id}/price-rules/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.DeleteAsync($"{Base(company)}/{svc.Id}/price-rules/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY39-09")]
    public async Task PriceChange_DoesNotChangeExistingSessionSnapshot()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, priceRub: 2000);
        var date = InDays(11);
        var order = await OrderOkAsync(svc.Id, date, 720, 2);
        (await GetOrderAsync(order.Token)).TotalRub.Should().Be(4000);

        var rules = (await (await AuthedClient(company.OwnerToken).GetAsync($"{Base(company)}/{svc.Id}/price-rules")).Content.ReadJsonAsync<PriceRulesDto>())!;
        (await AuthedClient(company.OwnerToken).PutJsonAsync($"{Base(company)}/{svc.Id}/price-rules/{rules.Rules.Single().Id}", new PriceRuleInput(127, 6, 30, 5000)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetOrderAsync(order.Token)).TotalRub.Should().Be(4000, "снимок цены в сеансе");
        (await QuoteServiceAsync(svc.Id, date, 900, 2)).TotalRub.Should().Be(10000, "новые сеансы — по новой цене");
    }

    // ── позиции ──────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-10")]
    public async Task Items_Validation_Limit_Order_And_InactiveHiddenFromGuests()
    {
        var company = await CreateStaysCompanyAsync();
        var svc = await CreateServiceAsync(company);
        var c = AuthedClient(company.OwnerToken);
        string Url(string tail = "") => $"{Base(company)}/{svc.Id}/items{tail}";

        (await c.PostJsonAsync(Url(), new ServiceItemInput(" ", 100, 5, true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostJsonAsync(Url(), new ServiceItemInput(new string('в', 101), 100, 5, true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostJsonAsync(Url(), new ServiceItemInput("Веник", -1, 5, true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostJsonAsync(Url(), new ServiceItemInput("Веник", 100_001, 5, true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostJsonAsync(Url(), new ServiceItemInput("Веник", 100, 0, true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostJsonAsync(Url(), new ServiceItemInput("Веник", 100, 51, true))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var free = await c.PostJsonAsync(Url(), new ServiceItemInput("Полотенце", 0, 2, true));
        free.StatusCode.Should().Be(HttpStatusCode.Created, "цена 0 = «бесплатно»");

        var broom = await AddItemAsync(company, svc.Id, "Веник", 300, 5);
        for (var i = 0; i < 18; i++) await AddItemAsync(company, svc.Id, $"Позиция {i}");
        var over = await c.PostJsonAsync(Url(), new ServiceItemInput("Двадцать первая", 1, 1, true));
        over.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(over)).Should().Be("ItemLimitReached");

        var page = (await (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/services/{svc.Slug}")).Content.ReadJsonAsync<PublicServiceDto>())!;
        page.Items.Should().Contain(i => i.Id == broom.Id);
        (await c.PutJsonAsync(Url($"/{broom.Id}"), new ServiceItemInput("Веник", 300, 5, false))).StatusCode.Should().Be(HttpStatusCode.OK);
        page = (await (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/services/{svc.Slug}")).Content.ReadJsonAsync<PublicServiceDto>())!;
        page.Items.Should().NotContain(i => i.Id == broom.Id, "выключенная позиция пропадает из формы гостя");

        // порядок: нужен полный список
        var all = (await (await c.GetAsync(Url())).Content.ReadJsonAsync<List<ServiceItemDto>>())!;
        (await c.PutJsonAsync(Url("/order"), new IdsOrderInput(all.Select(x => x.Id).Take(3).ToList()))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var reversed = all.Select(x => x.Id).Reverse().ToList();
        var ordered = await c.PutJsonAsync(Url("/order"), new IdsOrderInput(reversed));
        ordered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ordered.Content.ReadJsonAsync<List<ServiceItemDto>>())!.Select(x => x.Id).Should().Equal(reversed);
        (await c.DeleteAsync(Url($"/{broom.Id}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.DeleteAsync(Url($"/{broom.Id}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY39-11")]
    public async Task DeletedOrDisabledItem_KeepsSnapshotInCreatedSession()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company);
        var broom = await AddItemAsync(company, svc.Id, "Веник берёзовый", 300, 5);
        var date = InDays(12);
        var order = await OrderOkAsync(svc.Id, date, 720, 2, items: [new ItemSelectionInput(broom.Id, 2)]);
        var dto = await GetOrderAsync(order.Token);
        dto.ItemsAmountRub.Should().Be(600);
        dto.TotalRub.Should().Be(4600);

        (await AuthedClient(company.OwnerToken).DeleteAsync($"{Base(company)}/{svc.Id}/items/{broom.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await GetOrderAsync(order.Token);
        after.Items.Should().ContainSingle(i => i.Name == "Веник берёзовый" && i.Quantity == 2 && i.AmountRub == 600);
        after.TotalRub.Should().Be(4600);
    }

    // ── права ────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-12")]
    public async Task Permissions_Owner_Manager_Housekeeper_Stranger_Anonymous()
    {
        var company = await CreateStaysCompanyAsync();
        var svc = await CreateServiceAsync(company);
        var item = await AddItemAsync(company, svc.Id);
        var manager = await AddStaffAsync(company, "Manager");
        var housekeeper = await AddStaffAsync(company, "Housekeeper");
        var stranger = await RegisterAsync();
        var date = InDays(15);

        var setup = new ServiceSetupInput("Баня", svc.Slug, 2, 6, 60, 30, false, 0, null, StayServiceCancellationPolicy.NoDeductions, 12, true);
        var weekly = new WeeklyScheduleInput(Enumerable.Range(1, 7).Select(d => new WeeklyDayInput(d, [new ServiceWindowInput(600, 900)])).ToList());
        var overrideBody = new DateOverrideInput(true, [], "Закрыто");
        var b = Base(company) + "/" + svc.Id;
        // имя, метод, адрес, тело, ожидаемый статус по ролям: владелец / управляющий / горничная
        var matrix = new (string Name, HttpMethod M, string Url, object? Body, HttpStatusCode Owner, HttpStatusCode Mgr, HttpStatusCode Hk)[]
        {
            ("list", HttpMethod.Get, Base(company), null, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("get", HttpMethod.Get, b, null, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("create", HttpMethod.Post, Base(company), new ServiceCreateInput("Новая"), HttpStatusCode.Created, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("setup", HttpMethod.Put, b + "/setup", setup, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("content", HttpMethod.Put, b + "/content", new ServiceContentInput("Описание"), HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("price-rules GET", HttpMethod.Get, b + "/price-rules", null, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("price-rule POST", HttpMethod.Post, b + "/price-rules", new PriceRuleInput(1, 6, 8, 100), HttpStatusCode.Conflict, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("items GET", HttpMethod.Get, b + "/items", null, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("item POST", HttpMethod.Post, b + "/items", new ServiceItemInput("Простыня", 50, 3, true), HttpStatusCode.Created, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("weekly GET", HttpMethod.Get, b + "/weekly-schedule", null, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("weekly PUT", HttpMethod.Put, b + "/weekly-schedule", weekly, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("overrides GET", HttpMethod.Get, b + $"/date-overrides?month={date:yyyy-MM}", null, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("override PUT", HttpMethod.Put, b + $"/date-overrides/{D(date)}", overrideBody, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("override DELETE", HttpMethod.Delete, b + $"/date-overrides/{D(date)}", null, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("publish", HttpMethod.Post, b + "/publish", new EmptyInput(), HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("unpublish", HttpMethod.Post, b + "/unpublish", new EmptyInput(), HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("starts (cabinet)", HttpMethod.Get, b + $"/starts?date={D(date)}", null, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("service-day", HttpMethod.Get, $"/api/stays/companies/{company.Id}/service-day?date={D(date)}", null, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("sessions list", HttpMethod.Get, $"/api/stays/companies/{company.Id}/service-sessions", null, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden),
            ("arrival-reminder GET", HttpMethod.Get, $"/api/stays/companies/{company.Id}/arrival-reminder", null, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
            ("arrival-reminder history", HttpMethod.Get, $"/api/stays/companies/{company.Id}/arrival-reminder/history", null, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
        };
        foreach (var (name, method, url, body, owner, mgr, hk) in matrix)
        {
            async Task<HttpStatusCode> Call(string? token)
            {
                var c = token is null ? AnonymousClient() : AuthedClient(token);
                var req = new HttpRequestMessage(method, url);
                if (body is not null) req.Content = System.Net.Http.Json.JsonContent.Create(body, options: JsonHelpers.Options);
                return (await c.SendAsync(req)).StatusCode;
            }
            (await Call(company.OwnerToken)).Should().Be(owner, $"{name}: владелец");
            (await Call(manager.Token)).Should().Be(mgr, $"{name}: управляющий");
            (await Call(housekeeper.Token)).Should().Be(hk, $"{name}: горничная");
            (await Call(stranger.Token)).Should().Be(HttpStatusCode.NotFound, $"{name}: не участник компании — 404, неотличимо от несуществующей");
            (await Call(null)).Should().Be(HttpStatusCode.Unauthorized, $"{name}: без токена");
        }
        _ = item;
    }

    [Fact, TestCase("CY39-13")]
    public async Task Permissions_OnlyOwner_CanSwitchServiceOrders_And_ReminderSettings()
    {
        var company = await CreateStaysCompanyAsync();
        var manager = await AddStaffAsync(company, "Manager");
        var settings = (await GetCompanyAsync(company)).Settings!;
        var r = await AuthedClient(manager.Token).PutJsonAsync($"/api/stays/companies/{company.Id}/settings", settings with { AcceptServiceOrdersWithoutStay = true });
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden, "настройка «заказ услуг без проживания» — только владелец");
        (await GetCompanyAsync(company)).Settings!.AcceptServiceOrdersWithoutStay.Should().NotBe(true);
        await EnableOrdersWithoutStayAsync(company);
        (await GetCompanyAsync(company)).Settings!.AcceptServiceOrdersWithoutStay.Should().BeTrue();
    }

    [Fact, TestCase("CY39-14")]
    public async Task Isolation_OtherKinds_OtherStaysCompany_Idor()
    {
        var a = await CreateStaysCompanyAsync();
        var b = await CreateStaysCompanyAsync();
        var svcA = await CreateServiceAsync(a);
        var svcB = await CreateServiceAsync(b);
        var (salonOwner, salon) = await CreateOwnerWithCompanyAsync();
        var shop = await CreateShopAsync();

        // участник чужого вида/чужой компании — 404 на маршрутах услуг «Домов»
        (await AuthedClient(salonOwner.Token).GetAsync($"/api/stays/companies/{salon.Id}/services")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuthedClient(shop.OwnerToken).GetAsync($"/api/stays/companies/{shop.Id}/services")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuthedClient(salonOwner.Token).PostJsonAsync($"/api/stays/companies/{salon.Id}/services", new ServiceCreateInput("Баня"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuthedClient(salonOwner.Token).GetAsync($"/api/stays/companies/{salon.Id}/service-sessions")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuthedClient(shop.OwnerToken).GetAsync($"/api/stays/companies/{shop.Id}/arrival-reminder")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // владелец A в чужой компании B
        (await AuthedClient(a.OwnerToken).GetAsync($"{Base(b)}/{svcB.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // своя компания, чужая услуга (IDOR по идентификатору услуги)
        var mine = AuthedClient(a.OwnerToken);
        (await mine.GetAsync($"{Base(a)}/{svcB.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mine.PutJsonAsync($"{Base(a)}/{svcB.Id}/content", new ServiceContentInput("взлом"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mine.PostJsonAsync($"{Base(a)}/{svcB.Id}/price-rules", new PriceRuleInput(1, 6, 8, 1))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mine.DeleteAsync($"{Base(a)}/{svcB.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mine.PostJsonAsync($"{Base(a)}/{svcB.Id}/archive", new EmptyInput())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mine.GetAsync($"{Base(a)}/{svcB.Id}/starts?date={D(InDays(5))}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // позиция и правило чужой услуги через адрес своей
        var itemB = await AddItemAsync(b, svcB.Id);
        (await mine.PutJsonAsync($"{Base(a)}/{svcA.Id}/items/{itemB.Id}", new ServiceItemInput("x", 1, 1, true))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mine.DeleteAsync($"{Base(a)}/{svcA.Id}/items/{itemB.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetServiceAsync(b, svcB.Id)).Name.Should().Be("Баня");
        // публичная страница услуги чужой компании под своим slug
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{a.Slug}/services/{svcB.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // публичные маршруты «Домов» не видят салон/магазин
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{salon.Slug}/services/{svcA.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // сеанс/заказ чужой компании по своему адресу кабинета
        await EnableOrdersWithoutStayAsync(b);
        var order = await OrderOkAsync(svcB.Id, InDays(10), 720, 2);
        var sessionId = await SessionIdOfOrderAsync(order.Token);
        (await mine.GetAsync($"/api/stays/companies/{a.Id}/service-sessions/{sessionId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mine.PostJsonAsync($"/api/stays/companies/{a.Id}/service-sessions/{sessionId}/cancel", new ExpectedVersionReasonInput(1, "x"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mine.PostJsonAsync($"/api/stays/companies/{a.Id}/service-sessions/{sessionId}/confirm-payment", new ExpectedVersionInput(1))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── публичные страницы ───────────────────────────────────────────────────────

    [Fact, TestCase("CY39-15")]
    public async Task PublicPages_CompanyBlock_ServicePage_NoBufferUnlessShown_NoTouristTax()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company);
        var svc = await CreateServiceAsync(company, "Баня", minHours: 2, buffer: 45, forHouses: true, priceRub: 1800);
        var hidden = await CreateServiceAsync(company, "Чан", forHouses: false, buffer: 30, showBuffer: true);
        var anon = AnonymousClient();

        var comp = (await (await anon.GetAsync($"/api/stays/public/companies/{company.Slug}")).Content.ReadJsonAsync<PublicStaysCompanyDto>())!;
        comp.AcceptsServiceOrdersWithoutStay.Should().BeFalse("по умолчанию заказ без проживания выключен");
        comp.Services.Should().HaveCount(2);
        var summary = comp.Services!.Single(s => s.Id == svc.Id);
        summary.PriceFromRub.Should().Be(1800);
        summary.MinHours.Should().Be(2);
        summary.CanOrderWithoutStay.Should().BeFalse();
        summary.AvailableForHouseBookings.Should().BeTrue();

        var pageResp = await anon.GetAsync($"/api/stays/public/companies/{company.Slug}/services/{svc.Slug}");
        pageResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await pageResp.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<PublicServiceDto>(raw, JsonHelpers.Options)!;
        page.BufferMinutes.Should().BeNull("зазор гостю не показывается (Р39-3)");
        page.PriceTable.Should().NotBeEmpty();
        page.Standalone.Ordering.Should().BeFalse();
        raw.Should().NotContain("бизнес-день", "слово «бизнес-день» гостю не используется (ЮР39-8)");
        raw.Should().NotContain("не меньше 0");
        raw.Should().NotContainEquivalentOf("туристическ", "строка StayTouristTaxNotice на странице услуги не показывается (L39-9)");

        var shown = (await (await anon.GetAsync($"/api/stays/public/companies/{company.Slug}/services/{hidden.Slug}")).Content.ReadJsonAsync<PublicServiceDto>())!;
        shown.BufferMinutes.Should().Be(30, "владелец включил «Показывать время на подготовку»");

        // страница дома: «К проживанию можно добавить» — только услуги с «доступна для броней домов»
        var houseDto = (await (await anon.GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}")).Content.ReadJsonAsync<PublicHouseDto>())!;
        houseDto.ServicesForStay.Should().ContainSingle(s => s.ServiceId == svc.Id || s.Name == "Баня");
        houseDto.ServicesForStay!.Should().NotContain(s => s.Name == "Чан");

        await EnableOrdersWithoutStayAsync(company);
        comp = (await (await anon.GetAsync($"/api/stays/public/companies/{company.Slug}")).Content.ReadJsonAsync<PublicStaysCompanyDto>())!;
        comp.Services!.Single(s => s.Id == svc.Id).CanOrderWithoutStay.Should().BeTrue();
    }

    [Fact, TestCase("CY39-16")]
    public async Task BlockedCompany_And_ExpiredPlan_PagesVisible_BookingUnavailable()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company);
        await GiveStaysPlanAsync(company.Id, StaysPlans.UnlimitedSeedId, paidUntil: DateTime.UtcNow.AddDays(-1), isActive: false);

        var page = (await (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/services/{svc.Slug}")).Content.ReadJsonAsync<PublicServiceDto>())!;
        page.AcceptingBookings.Should().BeFalse();
        page.NotAcceptingText.Should().NotBeNullOrWhiteSpace();
        var date = InDays(10);
        var quote = await QuoteServiceAsync(svc.Id, date, 720, 2);
        quote.AcceptingBookings.Should().BeFalse();
        var r = await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, quote.TotalRub, UniquePhone()));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(r)).Should().Be("NotAcceptingBookings");
        (await ActiveSessionsAsync(svc.Id)).Should().BeEmpty();
    }
}
