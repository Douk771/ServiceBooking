using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 24, «Вызов 2»: часы работы, особые дни, пауза/выключатель, настройки времени получения, единое правило приёма
/// (US-24-01…05). Пишется по SPEC.md и API_CONTRACT_CYCLE24.md §473–§476, без взгляда на реализацию.
/// </summary>
public class Cycle24HoursAcceptanceTests(TestDatabaseFixture fixture) : Cycle24TestBase(fixture)
{
    // ── US-24-01: часы работы ────────────────────────────────────────────────────

    [Fact, TestCase("CY24-01")]
    public async Task NewShopWithoutHours_DoesNotAcceptOrders_ChecklistAndCustomerText()
    {
        var shop = await CreateShopAsync(openAllDay: false);
        var p = await CreateProductAsync(shop);

        var dto = await GetShopAsync(shop);
        dto.WorkingHoursSet.Should().BeFalse();
        dto.AcceptingOrders.Should().BeFalse();
        dto.NotAcceptingCode.Should().Be(ShopNotAcceptingCode.NoWorkingHours);
        dto.NotAcceptingReason.Should().Contain("Задайте часы работы");
        dto.SetupChecklist.Should().ContainSingle(i => i.Code == "WorkingHours" && !i.Done);
        dto.OpenState.Text.Should().Be("Часы работы не заданы");

        var hours = await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/working-hours");
        var h = await J(hours);
        h.GetProperty("isSet").GetBoolean().Should().BeFalse();
        h.GetProperty("days").GetArrayLength().Should().Be(7, "всегда семь дней");
        h.GetProperty("days")[0].GetProperty("dayOfWeek").GetString().Should().Be("Monday", "недельный список начинается с понедельника");
        h.GetProperty("days")[0].GetProperty("text").GetString().Should().Be("не заданы");

        var sf = await GetStorefrontAsync(shop.Slug);
        sf.AcceptingOrders.Should().BeFalse();
        sf.NotAcceptingReason.Should().Be("Магазин пока не принимает заказы");
        sf.NotAcceptingCode.Should().Be(ShopNotAcceptingCode.NoWorkingHours);

        var r = await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(r);
        body.GetProperty("code").GetString().Should().Be("ShopNotAcceptingOrders");
        body.GetProperty("notAcceptingCode").GetString().Should().Be("NoWorkingHours");
        body.GetProperty("message").GetString().Should().Be("Магазин пока не принимает заказы");
    }

    [Fact, TestCase("CY24-02")]
    public async Task PutWorkingHours_ReturnsTexts_CrossMidnightMarked_AndShopBecomesAcceptingWhenOpen()
    {
        var shop = await CreateShopAsync(openAllDay: false);
        var days = new List<WorkingDayInput>
        {
            Day(DayOfWeek.Monday, ("09:00", "14:00"), ("15:00", "21:00")),
            Day(DayOfWeek.Friday, ("18:00", "03:00")),
            Day(DayOfWeek.Saturday, ("10:00", "00:00")),
        };
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/working-hours", new WorkingHoursInput(days));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var dto = (await r.Content.ReadJsonAsync<WorkingHoursDto>())!;
        dto.IsSet.Should().BeTrue();
        dto.Days.Should().HaveCount(7);
        dto.Days[0].Text.Should().Be("09:00–14:00, 15:00–21:00");
        dto.Days[0].Intervals.Should().HaveCount(2);
        dto.Days[1].Text.Should().Be("выходной", "день не передан — выходной");
        var fri = dto.Days.Single(x => x.DayOfWeek == DayOfWeek.Friday);
        fri.Text.Should().Be("18:00–03:00 (до утра)");
        fri.Intervals.Single().CrossesMidnight.Should().BeTrue();
        dto.Days.Single(x => x.DayOfWeek == DayOfWeek.Saturday).Intervals.Single().CrossesMidnight.Should().BeFalse("до полуночи — не «через»");

        // повторное чтение — то же
        var again = (await (await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/working-hours")).Content.ReadJsonAsync<WorkingHoursDto>())!;
        again.Days.Select(x => x.Text).Should().Equal(dto.Days.Select(x => x.Text));

        (await GetShopAsync(shop)).WorkingHoursSet.Should().BeTrue();
    }

    [Theory, TestCase("CY24-03")]
    [InlineData("9:00", "10:00", "Укажите время в формате ЧЧ:ММ")]
    [InlineData("25:00", "26:00", "Укажите время в формате ЧЧ:ММ")]
    [InlineData("09:00", "24:00", "Укажите время в формате ЧЧ:ММ")]
    [InlineData("09:00", "23:58", "Время указывается с шагом 5 минут")]
    [InlineData("09:03", "10:00", "Время указывается с шагом 5 минут")]
    [InlineData("09:00", "09:00", "Интервал не может быть нулевой длины")]
    public async Task PutWorkingHours_InvalidTime_400WithContractText(string start, string end, string expected)
    {
        var shop = await CreateShopAsync(openAllDay: false);
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/working-hours",
            new WorkingHoursInput([Day(DayOfWeek.Monday, (start, end))]));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain(expected);
        (await GetShopAsync(shop)).WorkingHoursSet.Should().BeFalse("невалидное не сохраняется");
    }

    [Fact, TestCase("CY24-04")]
    public async Task PutWorkingHours_StructureErrors_400WithContractTexts()
    {
        var shop = await CreateShopAsync(openAllDay: false);
        async Task<string> Bad(params WorkingDayInput[] days)
        {
            var r = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/working-hours", new WorkingHoursInput(days.ToList()));
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            return await r.Content.ReadAsStringAsync();
        }

        (await Bad(Day(DayOfWeek.Monday, ("09:00", "10:00")), Day(DayOfWeek.Monday, ("11:00", "12:00")))).Should().Contain("День недели указан дважды");
        (await Bad(Day(DayOfWeek.Monday, ("08:00", "09:00"), ("10:00", "11:00"), ("12:00", "13:00"), ("14:00", "15:00"))))
            .Should().Contain("В дне не больше трёх интервалов");
        (await Bad(Day(DayOfWeek.Monday, ("10:00", "12:00"), ("11:00", "13:00")))).Should().Contain("Интервалы должны идти по порядку и не пересекаться");
        (await Bad(Day(DayOfWeek.Monday, ("10:00", "12:00"), ("12:00", "13:00")))).Should().Contain("Интервалы должны идти по порядку и не пересекаться", "соприкасающиеся — тоже ошибка");
        (await Bad(Day(DayOfWeek.Monday, ("13:00", "14:00"), ("10:00", "12:00")))).Should().Contain("Интервалы должны идти по порядку и не пересекаться");
        (await Bad(Day(DayOfWeek.Monday, ("22:00", "02:00"), ("23:00", "23:30")))).Should().Contain("Через полночь может переходить только последний интервал дня");
        // «хвост» пятницы (до 03:00) заходит на первый интервал субботы (01:00–…)
        (await Bad(Day(DayOfWeek.Friday, ("18:00", "03:00")), Day(DayOfWeek.Saturday, ("01:00", "05:00"))))
            .Should().Contain("Часы после полуночи пересекаются с часами следующего дня");
        (await Bad(Day(DayOfWeek.Sunday, ("18:00", "03:00")), Day(DayOfWeek.Monday, ("02:00", "05:00"))))
            .Should().Contain("Часы после полуночи пересекаются с часами следующего дня", "воскресенье → понедельник: неделя замыкается");
    }

    [Fact, TestCase("CY24-05")]
    public async Task WorkingHours_OnlyOwnerWrites_StaffReads_AnonymousAndStrangerDenied_SalonIs404()
    {
        var shop = await CreateShopAsync(openAllDay: false);
        var staff = await AddShopStaffAsync(shop);
        var stranger = await RegisterAsync();
        var url = $"/api/shops/{shop.Id}/working-hours";
        var body = new WorkingHoursInput([Day(DayOfWeek.Monday, ("09:00", "10:00"))]);

        (await AuthedClient(staff.Token).PutJsonAsync(url, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "меняет только владелец");
        (await AuthedClient(staff.Token).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnonymousClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AuthedClient(stranger.Token).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetShopAsync(shop)).WorkingHoursSet.Should().BeFalse();

        // изоляция: id салона на маршрутах магазина — 404 (не оракул)
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var c = AuthedClient(owner.Token);
        foreach (var path in new[] { "working-hours", "pickup-slots?date=2030-01-01", "ordering-status", "notification-settings", "daily-menus", "special-days" })
            (await c.GetAsync($"/api/shops/{company.Id}/{path}")).StatusCode.Should().Be(HttpStatusCode.NotFound, path);
        (await c.PutJsonAsync($"/api/shops/{company.Id}/working-hours", body)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutJsonAsync($"/api/shops/{company.Id}/acceptance", new { mode = "Stopped" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutJsonAsync($"/api/shops/{company.Id}/pickup-settings", new PickupSettingsDto(true, false, 15, 0, 15))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY24-06")]
    public async Task OpenState_WordsNotColors_OpenAndClosedTomorrow()
    {
        var shop = await CreateShopAsync(openAllDay: false);
        var todayDow = ShopToday(shop).DayOfWeek;
        var tomorrowDow = ShopToday(shop).AddDays(1).DayOfWeek;

        // открыто: сегодня 00:00–23:55 (пять минут в конце суток пропускаем — устойчивость прогона)
        await SetHoursAsync(shop, ("00:00", "23:55"));
        var sf = await GetStorefrontAsync(shop.Slug);
        if (ShopNow(shop).TimeOfDay < TimeSpan.FromHours(23) + TimeSpan.FromMinutes(50))
        {
            sf.OpenState.IsOpen.Should().BeTrue();
            sf.OpenState.Text.Should().Be("Открыто до 23:55");
            sf.WorkingHours.Lines.Should().ContainSingle().Which.Text.Should().Be("00:00–23:55", "одинаковые дни склеены в одну строку");
        }

        // закрыто до завтра: часы только в завтрашний день недели
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/working-hours",
            new WorkingHoursInput([Day(tomorrowDow, ("09:00", "18:00"))]));
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await GetStorefrontAsync(shop.Slug);
        closed.OpenState.IsOpen.Should().BeFalse();
        closed.OpenState.Text.Should().Be("Закрыто, откроемся завтра в 9:00", "время без ведущего нуля в тексте");
        closed.Pickup.Asap!.Available.Should().BeFalse();
        closed.Pickup.Asap.Text.Should().Be("Закрыто, откроемся завтра в 9:00", "у «как можно скорее» при закрытом магазине — статус");

        // все дни выходные — допустимо: часы «заданы», но приёма нет
        var off = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/working-hours", new WorkingHoursInput([]));
        off.StatusCode.Should().Be(HttpStatusCode.OK);
        var st = await J(await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/ordering-status"));
        st.GetProperty("workingHoursSet").GetBoolean().Should().BeTrue();
        st.GetProperty("acceptingOrders").GetBoolean().Should().BeFalse();
        st.GetProperty("notAcceptingCode").GetString().Should().Be("NoPickupTimeAvailable");
        _ = todayDow;
    }

    // ── US-24-02: особые дни ─────────────────────────────────────────────────────

    [Fact, TestCase("CY24-07")]
    public async Task SpecialDay_ClosedDate_OverridesWeek_AndIsNotOfferedForPreorder()
    {
        var shop = await CreateScheduledShopAsync();
        var tomorrow = ShopToday(shop).AddDays(1);
        var c = AuthedClient(shop.OwnerToken);

        var put = await c.PutJsonAsync($"/api/shops/{shop.Id}/special-days/{D(tomorrow)}", new { isClosed = true });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var list = await J(await c.GetAsync($"/api/shops/{shop.Id}/special-days"));
        list.EnumerateArray().Should().ContainSingle(x => x.GetProperty("date").GetString() == D(tomorrow) && x.GetProperty("isClosed").GetBoolean());

        var sf = await GetStorefrontAsync(shop.Slug);
        sf.Pickup.Dates.Should().NotContain(d => d.Date == tomorrow, "закрытый особый день не предлагается");
        (await GetSlotsAsync(shop.Slug, tomorrow)).Slots.Should().BeEmpty();

        // сокращённый день — свои интервалы
        var day2 = ShopToday(shop).AddDays(2);
        (await c.PutJsonAsync($"/api/shops/{shop.Id}/special-days/{D(day2)}", new { isClosed = false, intervals = new[] { new { start = "10:00", end = "11:00" } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetSlotsAsync(shop.Slug, day2)).Slots.Should().HaveCount(2, "10:00–11:00 при шаге 30 минут");

        // удаление возвращает дату к недельному расписанию, повтор — тоже 204
        (await c.DeleteAsync($"/api/shops/{shop.Id}/special-days/{D(tomorrow)}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.DeleteAsync($"/api/shops/{shop.Id}/special-days/{D(tomorrow)}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetSlotsAsync(shop.Slug, tomorrow)).Slots.Should().HaveCount(6);
    }

    [Fact, TestCase("CY24-08")]
    public async Task SpecialDay_DateRange400_AndOnlyOwner()
    {
        var shop = await CreateScheduledShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var c = AuthedClient(shop.OwnerToken);
        var today = ShopToday(shop);

        var past = await c.PutJsonAsync($"/api/shops/{shop.Id}/special-days/{D(today.AddDays(-1))}", new { isClosed = true });
        past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await past.Content.ReadAsStringAsync()).Should().Contain("Дата — от сегодня до 90 дней вперёд");
        var far = await c.PutJsonAsync($"/api/shops/{shop.Id}/special-days/{D(today.AddDays(91))}", new { isClosed = true });
        far.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutJsonAsync($"/api/shops/{shop.Id}/special-days/{D(today.AddDays(90))}", new { isClosed = true })).StatusCode
            .Should().Be(HttpStatusCode.OK, "90-й день ещё в границе");

        (await AuthedClient(staff.Token).PutJsonAsync($"/api/shops/{shop.Id}/special-days/{D(today.AddDays(5))}", new { isClosed = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AuthedClient(staff.Token).GetAsync($"/api/shops/{shop.Id}/special-days")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY24-09")]
    public async Task SpecialDay_MakingDayClosedWithActiveOrders_409ListsOrders_ConfirmSavesAndLeavesOrdersAlone()
    {
        var shop = await CreateScheduledShopAsync();
        var p = await CreateProductAsync(shop);
        var tomorrow = ShopToday(shop).AddDays(1);
        var placed = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], await SlotAsync(shop.Slug, tomorrow)));
        var c = AuthedClient(shop.OwnerToken);
        var url = $"/api/shops/{shop.Id}/special-days/{D(tomorrow)}";

        var refused = await c.PutJsonAsync(url, new { isClosed = true });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(refused);
        body.GetProperty("code").GetString().Should().Be("ScheduleConflictsWithOrders");
        body.GetProperty("message").GetString().Should().Contain("На этот день уже есть заказы вне новых часов");
        var conflicting = body.GetProperty("conflictingOrders").EnumerateArray().ToList();
        conflicting.Should().ContainSingle();
        conflicting[0].GetProperty("number").GetInt32().Should().Be(placed.Order.Number);
        (await c.GetAsync($"/api/shops/{shop.Id}/special-days")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await J(await c.GetAsync($"/api/shops/{shop.Id}/special-days"))).GetArrayLength().Should().Be(0, "без подтверждения не сохранено");

        var confirmed = await c.PutJsonAsync(url, new { isClosed = true, confirmConflicts = true });
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        var order = await GetPublicOrderAsync(placed.Order.Token);
        order.Status.Should().Be(OrderStatus.New, "заказы автоматически не отменяются");
        order.Pickup.Date.Should().Be(tomorrow);
    }

    // ── US-24-05: настройки времени получения ────────────────────────────────────

    [Theory, TestCase("CY24-10")]
    [InlineData(false, false, 15, 0, 15, "Включите хотя бы один вариант времени получения")]
    [InlineData(true, false, 20, 0, 15, "Шаг слотов — 15, 30 или 60 минут")]
    [InlineData(true, true, 15, 15, 15, "Предзаказ — от 0 до 14 дней вперёд")]
    [InlineData(true, true, 15, -1, 15, "Предзаказ — от 0 до 14 дней вперёд")]
    [InlineData(true, true, 15, 0, 181, "Время приготовления — от 0 до 180 минут")]
    [InlineData(true, true, 15, 0, -5, "Время приготовления — от 0 до 180 минут")]
    public async Task PickupSettings_Invalid_400WithContractText(bool asap, bool scheduled, int step, int days, int prep, string expected)
    {
        var shop = await CreateShopAsync();
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/pickup-settings", new PickupSettingsDto(asap, scheduled, step, days, prep));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain(expected);
    }

    [Fact, TestCase("CY24-11")]
    public async Task PickupSettings_BoundariesAccepted_OwnerOnly_ReturnedInShopDto()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var c = AuthedClient(shop.OwnerToken);

        var r = await c.PutJsonAsync($"/api/shops/{shop.Id}/pickup-settings", new PickupSettingsDto(false, true, 60, 14, 180));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var dto = (await r.Content.ReadJsonAsync<ShopManageDto>())!;
        dto.PickupSettings.Should().Be(new PickupSettingsDto(false, true, 60, 14, 180));
        (await c.PutJsonAsync($"/api/shops/{shop.Id}/pickup-settings", new PickupSettingsDto(true, false, 15, 0, 0))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await AuthedClient(staff.Token).PutJsonAsync($"/api/shops/{shop.Id}/pickup-settings", new PickupSettingsDto(true, true, 15, 1, 15)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetShopAsync(shop)).PickupSettings.PreorderDays.Should().Be(0, "отказ ничего не менял");
    }

    // ── US-24-03: пауза и выключатель ────────────────────────────────────────────

    [Fact, TestCase("CY24-12")]
    public async Task Pause_ByStaff_StopsAsapAndScheduled_CartSurvives_ResumeRestores()
    {
        var shop = await CreateScheduledShopAsync();
        await OpenShopAllDayAsync(shop);
        await SetPickupAsync(shop, asap: true, scheduled: true, step: 30, preorderDays: 2, minPrep: 0);
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        var slot = await SlotAsync(shop.Slug, ShopToday(shop).AddDays(1));

        var pause = await SetAcceptanceAsync(shop, new { mode = "Paused", pause = "Hour1" }, staff.Token);
        pause.StatusCode.Should().Be(HttpStatusCode.OK, await pause.Content.ReadAsStringAsync());
        var acc = (await pause.Content.ReadJsonAsync<ShopAcceptanceDto>())!;
        acc.Mode.Should().Be(ShopAcceptanceMode.Paused);
        acc.PausedUntilUtc.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(2));
        acc.StatusText.Should().StartWith("Пауза до ");
        acc.ChangedText.Should().StartWith("Изменено: ");

        var sf = await GetStorefrontAsync(shop.Slug);
        sf.AcceptingOrders.Should().BeFalse();
        sf.NotAcceptingCode.Should().Be(ShopNotAcceptingCode.Paused);
        sf.NotAcceptingReason.Should().StartWith("Магазин временно не принимает заказы — до ");

        foreach (var pickup in new PickupSelectionInput?[] { null, new PickupSelectionInput(PickupKind.Asap, null, null), slot })
        {
            var r = await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], pickup));
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, "пауза останавливает и «как можно скорее», и ко времени");
            (await Code(r)).Should().Be("ShopNotAcceptingOrders");
        }

        // корзина не теряется: расчёт по-прежнему работает, но «принимает» — нет
        var quote = await AnonymousClient().PostJsonAsync($"/api/storefront/{shop.Slug}/quote", new QuoteInput([new(p.Id, 2)]));
        var q = (await quote.Content.ReadJsonAsync<QuoteDto>())!;
        q.Total.Should().Be(500m);
        q.AcceptingOrders.Should().BeFalse();

        var status = await J(await AuthedClient(staff.Token).GetAsync($"/api/shops/{shop.Id}/ordering-status"));
        status.GetProperty("acceptingOrders").GetBoolean().Should().BeFalse();
        status.GetProperty("ownerText").GetString().Should().StartWith("Пауза до ");
        status.GetProperty("acceptance").GetProperty("mode").GetString().Should().Be("Paused");

        var resume = await SetAcceptanceAsync(shop, new { mode = "Accepting" }, staff.Token);
        resume.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resume.Content.ReadJsonAsync<ShopAcceptanceDto>())!.StatusText.Should().Be("Принимаем заказы");
        (await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], null))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CY24-13")]
    public async Task Stopped_RefusesOrders_UntilTurnedOn_ExistingOrdersStillWork()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var existing = await PlaceAndLoadAsync(shop, [Line(p, 1)]);

        var stop = await SetAcceptanceAsync(shop, new { mode = "Stopped" });
        stop.StatusCode.Should().Be(HttpStatusCode.OK);
        (await stop.Content.ReadJsonAsync<ShopAcceptanceDto>())!.StatusText.Should().Be("Не принимаем, пока не включите");

        var r = await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(r);
        body.GetProperty("notAcceptingCode").GetString().Should().Be("Stopped");
        body.GetProperty("message").GetString().Should().Be("Магазин временно не принимает заказы");
        (await GetShopAsync(shop)).NotAcceptingReason.Should().Be("Приём заказов выключен", "владельцу — свой текст");

        // созданные заказы обрабатываются как обычно
        var accepted = await ActOkAsync(shop, existing, "accept");
        accepted.Status.Should().Be(OrderStatus.Accepted);
        (await GetBoardAsync(shop)).Acceptance.Mode.Should().Be(ShopAcceptanceMode.Stopped);

        (await SetAcceptanceAsync(shop, new { mode = "Accepting" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CY24-14")]
    public async Task Acceptance_Validation_Permissions_AllPauseDurations()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var stranger = await RegisterAsync();

        var noDuration = await SetAcceptanceAsync(shop, new { mode = "Paused" });
        noDuration.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noDuration.Content.ReadAsStringAsync()).Should().Contain("Укажите длительность паузы");

        (await SetAcceptanceAsync(shop, new { mode = "Stopped" }, stranger.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AnonymousClient().PutJsonAsync($"/api/shops/{shop.Id}/acceptance", new { mode = "Stopped" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetShopAsync(shop)).Acceptance.Mode.Should().Be(ShopAcceptanceMode.Accepting, "отказы ничего не меняли");

        foreach (var d in new[] { "Minutes15", "Minutes30", "Hour1", "EndOfDay" })
        {
            var r = await SetAcceptanceAsync(shop, new { mode = "Paused", pause = d }, staff.Token);
            r.StatusCode.Should().Be(HttpStatusCode.OK, d + ": " + await r.Content.ReadAsStringAsync());
            var a = (await r.Content.ReadJsonAsync<ShopAcceptanceDto>())!;
            a.PausedUntilUtc.Should().BeAfter(DateTime.UtcNow, d);
        }
        // «до конца дня» не дальше суток
        var last = (await (await SetAcceptanceAsync(shop, new { mode = "Paused", pause = "EndOfDay" })).Content.ReadJsonAsync<ShopAcceptanceDto>())!;
        last.PausedUntilUtc.Should().BeBefore(DateTime.UtcNow.AddHours(25));
    }

    [Fact, TestCase("CY24-15")]
    public async Task ExpiredPause_AcceptsAgain_WithoutAnyAction()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        (await SetAcceptanceAsync(shop, new { mode = "Paused", pause = "Minutes15" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Истечение — состоянием, без фоновой задачи (SPEC: возобновление не позже чем через минуту): сдвигаем срок в БД в прошлое.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
            var settings = await db.ShopSettings.SingleAsync(s => s.CompanyId == shop.Id);
            settings.PausedUntilUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        (await GetShopAsync(shop)).Acceptance.Mode.Should().Be(ShopAcceptanceMode.Accepting, "истекшая пауза отдаётся как «Принимаем»");
        (await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CY24-16")]
    public async Task OrderingStatus_And_Board_CarryAcceptanceAndLimit_InEveryAnswer()
    {
        var shop = await CreateShopAsync();
        var board = await GetBoardAsync(shop);
        board.Acceptance.Should().NotBeNull();
        board.Acceptance.Mode.Should().Be(ShopAcceptanceMode.Accepting);

        // повтор с известной ревизией — «ничего не изменилось», а acceptance всё равно на месте
        var again = await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/order-board?sinceRevision={board.Revision}");
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var j = await J(again);
        j.TryGetProperty("acceptance", out var acc).Should().BeTrue("acceptance приходит в каждом ответе, в том числе changed:false");
        acc.GetProperty("mode").GetString().Should().Be("Accepting");

        var st = (await (await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/ordering-status")).Content.ReadJsonAsync<ShopOrderingStatusDto>())!;
        st.AcceptingOrders.Should().BeTrue();
        st.WorkingHoursSet.Should().BeTrue();
        st.OrderLimit.Should().NotBeNull();
        (await AnonymousClient().GetAsync($"/api/shops/{shop.Id}/ordering-status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── US-24-04: единое правило и порядок проверок оформления ───────────────────

    [Fact, TestCase("CY24-17")]
    public async Task IdempotentRetry_IsCheckedBeforeAcceptanceRule_Returns200SameOrder()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var input = Guest([Line(p, 1)]);
        var first = await PlaceOrderAsync(shop.Slug, input);

        (await SetAcceptanceAsync(shop, new { mode = "Stopped" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var retry = await PostOrderAsync(shop.Slug, input);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, "повтор того же ключа отдаёт уже созданный заказ, а не отказ приёма");
        (await retry.Content.ReadJsonAsync<CreateOrderResponse>())!.Order.Token.Should().Be(first.Order.Token);

        // новый ключ в тот же момент — отказ
        (await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY24-18")]
    public async Task ExistingShopStorefrontAndOrder_BlockedShop_StaysUnavailable_NotInAcceptingRule()
    {
        // Приём в закрытый магазин: вне часов «как можно скорее» не принимается, предзаказ по слоту — принимается (Q-24-4).
        var shop = await CreateScheduledShopAsync();
        var p = await CreateProductAsync(shop);
        var tomorrow = ShopToday(shop).AddDays(1);
        // часы только на завтра => сегодня закрыто
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/working-hours",
            new WorkingHoursInput([Day(tomorrow.DayOfWeek, ("09:00", "12:00"))]));
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var asap = await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], new PickupSelectionInput(PickupKind.Asap, null, null)));
        asap.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var msg = (await J(asap)).GetProperty("message").GetString()!;
        msg.Should().MatchRegex("не успеем|Закрыто|не принимает", "причина словами, не «ошибка»");

        var slot = await SlotAsync(shop.Slug, tomorrow);
        (await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], slot))).StatusCode.Should().Be(HttpStatusCode.Created,
            "заказ ко времени можно оформить в любое время суток, если слот внутри часов работы");
    }
}
