using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.Services.Orders.Reports;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 25, «Вызов 2», блоки B–C: история заказов и сводка (US-25-05, US-25-06, US-25-07), права и изоляция новых маршрутов.
/// Написано по SPEC.md и API_CONTRACT_CYCLE25.md §522, §526–§527, а не по реализации. «Сегодня» — рабочий день магазина, поэтому
/// сценарии одинаково работают и внутри 00:00–04:00 местного времени (заказы «как можно скорее» получают дату выдачи рабочего дня).
/// </summary>
public class Cycle25ReportsTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private sealed record Seed(ShopCtx Shop, ProductDto Soup, ProductDto Cheese, string RepeatPhone, Dictionary<string, StaffOrderDto> Orders);

    /// <summary>
    /// Восемь заказов на сегодня, по одному в каждом из восьми статусов, кроме двух «Выдан» (одна покупательница — два заказа):
    /// Выдан ×2 (600 ₽ и 300 ₽ + 512 г сыра = 576,48 ₽), Отклонён, Отменён покупателем, Отменён магазином, Не забран, Новый, Принят.
    /// </summary>
    private async Task<Seed> SeedAsync(ShopCtx? existing = null)
    {
        var shop = existing ?? await CreateRoundClockShopAsync();
        var soup = await CreateProductAsync(shop, "Борщ", 300m);
        var cheese = await CreateProductAsync(shop, "Сыр", 540m, ProductUnit.Weight);
        var repeat = UniquePhone();
        var orders = new Dictionary<string, StaffOrderDto>();

        var issued1 = await PlaceStaffViewAsync(shop, [Line(soup, 2)], phone: repeat, name: "Анна Иванова");
        orders["issued1"] = await DriveAsync(shop, issued1, "accept", "ready", "issue");

        var issued2 = await PlaceStaffViewAsync(shop, [Line(cheese, 500), Line(soup, 1)], phone: repeat, name: "Анна");
        issued2 = await DriveAsync(shop, issued2, "accept", "ready");
        var weightItem = issued2.Items.Single(i => i.ProductId == cheese.Id);
        var issue = await AuthedClient(shop.OwnerToken).PostJsonAsync(
            $"/api/shops/{shop.Id}/orders/{issued2.Id}/issue", new IssueInput(issued2.Version, [new ActualQuantityInput(weightItem.Id, 512)]));
        issue.StatusCode.Should().Be(HttpStatusCode.OK, await issue.Content.ReadAsStringAsync());
        orders["issued2"] = (await issue.Content.ReadJsonAsync<StaffOrderDto>())!;

        orders["rejected"] = await ActOkAsync(shop, await PlaceStaffViewAsync(shop, [Line(soup, 1)], name: "Борис"), "reject", reason: "нет продуктов");

        var byCustomer = await PlaceOrderAsync(shop.Slug, Guest([Line(soup, 1)], name: "Вера"));
        await CancelByCustomerAsync(byCustomer.Order.Token);
        orders["cancelledByCustomer"] = await GetStaffOrderAsync(shop, byCustomer.Order.Token);

        var byShop = await PlaceStaffViewAsync(shop, [Line(soup, 1)], name: "Глеб");
        byShop = await ActOkAsync(shop, byShop, "accept");
        orders["cancelledByShop"] = await ActOkAsync(shop, byShop, "cancel", reason: "авария");

        var notPicked = await PlaceStaffViewAsync(shop, [Line(soup, 1)], name: "Дарья");
        orders["notPickedUp"] = await DriveAsync(shop, notPicked, "accept", "ready", "not-picked-up");

        orders["new"] = await PlaceStaffViewAsync(shop, [Line(soup, 1)], name: "Егор");
        orders["accepted"] = await ActOkAsync(shop, await PlaceStaffViewAsync(shop, [Line(soup, 1)], name: "Зоя"), "accept");

        foreach (var (key, expected) in new Dictionary<string, OrderStatus>
                 {
                     ["issued1"] = OrderStatus.Issued, ["issued2"] = OrderStatus.Issued, ["rejected"] = OrderStatus.Rejected,
                     ["cancelledByCustomer"] = OrderStatus.CancelledByCustomer, ["cancelledByShop"] = OrderStatus.CancelledByShop,
                     ["notPickedUp"] = OrderStatus.NotPickedUp, ["new"] = OrderStatus.New, ["accepted"] = OrderStatus.Accepted,
                 })
            orders[key].Status.Should().Be(expected, key);
        return new Seed(shop, soup, cheese, repeat, orders);
    }

    // ── US-25-06 / критерий приёмки: сводка за день совпадает с историей ───────────────

    [Fact, TestCase("CY25-20")]
    public async Task Summary_Today_AgreesWithHistory_OrdersTotalAndIssuedAmount_AndAllFigures()
    {
        var seed = await SeedAsync();
        var shop = seed.Shop;

        var summary = await SummaryOkAsync(shop);
        var history = await HistoryOkAsync(shop, new { period = "Today" });

        summary.Period.Preset.Should().Be(ReportPeriodPreset.Today);
        summary.Period.Days.Should().Be(1);
        summary.Period.From.Should().Be((await WorkingDayAsync(shop))).And.Be(summary.Period.To);
        summary.Period.Label.Should().StartWith("Сегодня, ");

        summary.OrdersTotal.Should().Be(8);
        summary.OrdersTotal.Should().Be(history.TotalCount, "критерий приёмки: заказов в сводке = найдено в истории");
        summary.IssuedAmount.Should().Be(history.IssuedAmount);
        summary.IssuedCount.Should().Be(history.IssuedCount);
        summary.IssuedCount.Should().Be(2);
        summary.IssuedAmount.Should().Be(1176.48m, "600 ₽ + (512 г × 540 ₽/кг = 276,48 ₽ + 300 ₽)");

        summary.InProgress.Should().Be(2);
        summary.InProgressText.Should().Be("из них в работе: 2");
        summary.TerminalCount.Should().Be(6);
        summary.PaymentNote.Should().Be("Оплата на месте, платформа её не видит");
        summary.AverageCheck.Should().Be(588.24m);
        summary.AverageCheckText.Should().MatchRegex(@"^588,24\s₽$");

        summary.Cancellations.Select(c => $"{c.Status}:{c.Count}").Should().Equal("Rejected:1", "CancelledByShop:1", "CancelledByCustomer:1");
        summary.Cancellations.Select(c => c.Label).Should().Equal("Отклонён магазином", "Отменён магазином", "Отменён покупателем");
        summary.Cancellations.Should().OnlyContain(c => c.Share!.Value > 0.16m && c.Share.Value < 0.17m, "доля — от заказов в КОНЕЧНЫХ статусах (1 из 6)");
        summary.Cancellations.Should().OnlyContain(c => Regex.IsMatch(c.ShareText, @"^17\s%$"));
        summary.NotPickedUp.Status.Should().Be(OrderStatus.NotPickedUp);
        summary.NotPickedUp.Label.Should().Be("Не забран");
        summary.NotPickedUp.Count.Should().Be(1);

        summary.Top.Sort.Should().Be(SummaryTopSort.Amount);
        summary.Top.Items.Select(i => i.Name).Should().Equal("Борщ", "Сыр");
        summary.Top.Items[0].Quantity.Should().Be(3, "по выданным заказам: 2 + 1");
        summary.Top.Items[0].QuantityText.Should().Be("3 шт");
        summary.Top.Items[0].Amount.Should().Be(900m);
        summary.Top.Items[1].Quantity.Should().Be(512, "весовые — в граммах");
        summary.Top.Items[1].QuantityText.Should().MatchRegex(@"^(512 г|0,512 кг)$");
        summary.Top.Items[1].Amount.Should().Be(276.48m);
        summary.Top.Items.Should().OnlyContain(i => !i.IsDeleted);
        summary.Days.Should().BeNull("при периоде в один день таблицы по дням нет");
        summary.Previous.Should().BeNull();

        var byQuantity = await SummaryOkAsync(shop, "?top=Quantity");
        byQuantity.Top.Sort.Should().Be(SummaryTopSort.Quantity);
        byQuantity.Top.Items.Select(i => i.Name).Should().Equal("Борщ", "Сыр");

        // те же цифры при тех же периоде и статусах: выданные в истории == выданные в сводке
        var issuedOnly = await HistoryOkAsync(shop, new { period = "Today", statuses = new[] { "Issued" } });
        issuedOnly.TotalCount.Should().Be(summary.IssuedCount);
        issuedOnly.IssuedAmount.Should().Be(summary.IssuedAmount);
        history.SummaryText.Should().MatchRegex(@"^Найдено 8 заказов, выдано на 1\s176,48\s₽$");
    }

    [Fact, TestCase("CY25-21")]
    public async Task Summary_PeriodsByPickupDate_DaysTableAndComparison_RelabelledByTheServer()
    {
        var seed = await SeedAsync();
        var shop = seed.Shop;
        var today = (await WorkingDayAsync(shop));

        // переносим два выданных заказа на вчера и на позавчера прямо в БД: показатели считаются по ДАТЕ ВЫДАЧИ (Q-25-4)
        await WithDbAsync(async db =>
        {
            var o1 = await db.Orders.SingleAsync(o => o.Id == seed.Orders["issued1"].Id);
            var o2 = await db.Orders.SingleAsync(o => o.Id == seed.Orders["issued2"].Id);
            o1.PickupDate = today.AddDays(-1);
            o2.PickupDate = today.AddDays(-2);
            await db.SaveChangesAsync();
        });

        var todayS = await SummaryOkAsync(shop);
        todayS.OrdersTotal.Should().Be(6);
        todayS.IssuedCount.Should().Be(0);
        todayS.AverageCheck.Should().BeNull();
        todayS.AverageCheckText.Should().Be("—", "при 0 выданных — прочерк");
        todayS.Top.Items.Should().BeEmpty();

        var yesterday = await SummaryOkAsync(shop, "?period=Yesterday");
        yesterday.Period.From.Should().Be(today.AddDays(-1));
        yesterday.Period.Label.Should().StartWith("Вчера, ");
        yesterday.OrdersTotal.Should().Be(1);
        yesterday.IssuedAmount.Should().Be(600m);

        var week = await SummaryOkAsync(shop, "?period=Last7Days&compare=true");
        week.Period.Days.Should().Be(7);
        week.Period.To.Should().Be(today);
        week.Period.From.Should().Be(today.AddDays(-6));
        week.OrdersTotal.Should().Be(8);
        week.IssuedAmount.Should().Be(1176.48m);
        week.Days.Should().HaveCount(7).And.BeInAscendingOrder(d => d.Date);
        week.Days!.Sum(d => d.Orders).Should().Be(8);
        week.Days!.Sum(d => d.IssuedAmount).Should().Be(1176.48m);
        week.Days!.Single(d => d.Date == today.AddDays(-1)).IssuedCount.Should().Be(1);
        week.Previous.Should().NotBeNull();
        week.Previous!.Period.To.Should().Be(today.AddDays(-7), "предыдущий период той же длины прилегает к текущему");
        week.Previous.OrdersTotal.Should().Be(0);
        week.Previous.OrdersDeltaText.Should().Be("—", "при нулевой базе разницы нет");

        var custom = await SummaryOkAsync(shop, $"?period=Custom&from={D(today.AddDays(-2))}&to={D(today.AddDays(-2))}");
        custom.OrdersTotal.Should().Be(1);
        custom.IssuedAmount.Should().Be(576.48m);
        custom.Period.Preset.Should().Be(ReportPeriodPreset.Custom);

        // from/to без Custom игнорируются, а не считаются ошибкой
        (await SummaryOkAsync(shop, $"?period=Yesterday&from={D(today.AddDays(-30))}&to={D(today)}")).Period.From.Should().Be(today.AddDays(-1));

        var month = await SummaryOkAsync(shop, "?period=ThisMonth");
        month.Period.From.Day.Should().Be(1);
        month.Period.To.Month.Should().Be(month.Period.From.Month);
        var lastMonth = await SummaryOkAsync(shop, "?period=LastMonth");
        lastMonth.Period.To.Should().Be(month.Period.From.AddDays(-1));
    }

    [Fact, TestCase("CY25-22")]
    public async Task Periods_Validation_Returns400WithRussianText_ForSummaryAndHistory()
    {
        var shop = await CreateRoundClockShopAsync();
        var today = (await WorkingDayAsync(shop));

        async Task ExpectAsync(HttpResponseMessage r, string text)
        {
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
            (await r.Content.ReadAsStringAsync()).Should().Contain(text);
        }

        await ExpectAsync(await SummaryAsync(shop, "?period=Custom"), "Укажите начало и конец периода");
        await ExpectAsync(await SummaryAsync(shop, $"?period=Custom&from={D(today)}"), "Укажите начало и конец периода");
        await ExpectAsync(await SummaryAsync(shop, $"?period=Custom&from={D(today)}&to={D(today.AddDays(-1))}"), "Конец периода раньше начала");
        await ExpectAsync(await SummaryAsync(shop, $"?period=Custom&from={D(today.AddDays(-366))}&to={D(today)}"), "Период — не длиннее 366 дней");
        (await SummaryAsync(shop, $"?period=Custom&from={D(today.AddDays(-365))}&to={D(today)}")).StatusCode.Should().Be(HttpStatusCode.OK, "ровно 366 дней допустимо");

        await ExpectAsync(await HistoryAsync(shop, new { period = "Custom", from = D(today) }), "Укажите начало и конец периода");
        await ExpectAsync(await HistoryAsync(shop, new { period = "Custom", from = D(today), to = D(today.AddDays(-1)) }), "Конец периода раньше начала");
        await ExpectAsync(await HistoryAsync(shop, new { period = "Custom", from = D(today.AddDays(-400)), to = D(today) }), "Период — не длиннее 366 дней");
        await ExpectAsync(await HistoryAsync(shop, new { customer = "а" }), "Введите не меньше 2 букв имени или 4 цифр телефона");
        await ExpectAsync(await HistoryAsync(shop, new { amountFrom = -1 }), "Сумма не может быть отрицательной");
        await ExpectAsync(await HistoryAsync(shop, new { amountFrom = 500, amountTo = 100 }), "Сумма «от» больше суммы «до»");
        await ExpectAsync(await HistoryAsync(shop, new { number = 0 }), "Номер заказа — число от 1 до 9999");
        await ExpectAsync(await HistoryAsync(shop, new { number = 10000 }), "Номер заказа — число от 1 до 9999");
        await ExpectAsync(await HistoryAsync(shop, new { page = 0 }), "Номер страницы — от 1");

        // сервер не падает на мусоре: неизвестный вид сортировки — 400, а не 500
        (await SummaryAsync(shop, "?top=Nonsense")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SummaryAsync(shop, "?period=Nonsense")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SummaryAsync(shop, "?period=Custom&from=not-a-date&to=2026-01-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AuthedClient(shop.OwnerToken).PostAsync($"/api/shops/{shop.Id}/order-history", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── US-25-05: история ───────────────────────────────────────────────────────────

    [Fact, TestCase("CY25-23")]
    public async Task History_Filters_Statuses_Customer_Amount_Number_Sort_MaskedPhone()
    {
        var seed = await SeedAsync();
        var shop = seed.Shop;

        // по умолчанию — 7 дней, все статусы, новые сверху
        var all = await HistoryOkAsync(shop);
        all.Period.Preset.Should().Be(ReportPeriodPreset.Last7Days);
        all.Period.Days.Should().Be(7);
        all.TotalCount.Should().Be(8);
        all.PageSize.Should().Be(50);
        all.Items.Should().BeInDescendingOrder(i => i.PickupStartUtc);
        all.EmptyText.Should().BeNull();

        var asc = await HistoryOkAsync(shop, new { sort = "PickupAsc" });
        asc.Items.Should().BeInAscendingOrder(i => i.PickupStartUtc);
        asc.Items.Select(i => i.OrderId).Should().BeEquivalentTo(all.Items.Select(i => i.OrderId));

        // мультивыбор статусов
        var multi = await HistoryOkAsync(shop, new { period = "Today", statuses = new[] { "Issued", "NotPickedUp" } });
        multi.TotalCount.Should().Be(3);
        multi.Items.Select(i => i.Status).Should().BeEquivalentTo(new[] { OrderStatus.Issued, OrderStatus.Issued, OrderStatus.NotPickedUp });
        multi.Items.Select(i => i.StatusText).Should().Contain("Выдан").And.Contain("Не забран");

        // покупатель: часть имени без учёта регистра
        var byName = await HistoryOkAsync(shop, new { customer = "анна" });
        byName.TotalCount.Should().Be(2);
        byName.Items.Should().OnlyContain(i => i.CustomerName!.StartsWith("Анна"));
        (await HistoryOkAsync(shop, new { customer = "АННА ИВ" })).TotalCount.Should().Be(1);

        // покупатель: не меньше 4 цифр телефона, формат ввода не важен
        var digits = seed.RepeatPhone.TrimStart('+');
        (await HistoryOkAsync(shop, new { customer = digits[^6..] })).TotalCount.Should().Be(2);
        (await HistoryOkAsync(shop, new { customer = $"+{digits[..1]} ({digits[1..4]}) {digits[4..]}" })).TotalCount.Should().Be(2, "скобки, пробелы и плюс отбрасываются");
        (await HistoryOkAsync(shop, new { customer = "Несуществующий" })).EmptyText.Should().Be("По этим условиям заказов нет");

        // маска телефона в строке: только последние четыре цифры; полного номера в списке нет
        var row = byName.Items[0];
        row.CustomerPhoneMasked.Should().Be(MaskOf(seed.RepeatPhone));
        row.CustomerPhoneMasked.Should().MatchRegex(@"^\+7 \(···\) ···-\d\d-\d\d$");
        var raw = await (await HistoryAsync(shop, new { period = "Today" })).Content.ReadAsStringAsync();
        raw.Should().NotContain(digits).And.NotContain(seed.RepeatPhone);

        // сумма — по итогу заказа: у выданного фактический (576,48), у остальных ориентир
        var range = await HistoryOkAsync(shop, new { amountFrom = 550, amountTo = 700 });
        range.Items.Select(i => i.Total).Should().BeEquivalentTo(new[] { 600m, 576.48m });
        (await HistoryOkAsync(shop, new { amountFrom = 600, amountTo = 600 })).TotalCount.Should().Be(1);
        (await HistoryOkAsync(shop, new { amountTo = 299.99 })).TotalCount.Should().Be(0);
        var issuedWeight = all.Items.Single(i => i.OrderId == seed.Orders["issued2"].Id);
        issuedWeight.TotalIsApproximate.Should().BeFalse("у выданного итог фактический");
        issuedWeight.Total.Should().Be(576.48m);
        issuedWeight.ItemCount.Should().Be(2);

        // номер — точный, внутри периода
        var number = seed.Orders["accepted"].Number;
        var byNumber = await HistoryOkAsync(shop, new { number });
        byNumber.Items.Should().OnlyContain(i => i.Number == number);
        byNumber.Items.Should().Contain(i => i.OrderId == seed.Orders["accepted"].Id);

        // страница за пределами — пустой список, а не ошибка; итог по фильтру остаётся
        var beyond = await HistoryOkAsync(shop, new { page = 5 });
        beyond.Items.Should().BeEmpty();
        beyond.TotalCount.Should().Be(8);

        // пустой период — своё объяснение
        var empty = await HistoryOkAsync(shop, new { period = "Yesterday" });
        empty.TotalCount.Should().Be(0);
        empty.Items.Should().BeEmpty();
        empty.EmptyText.Should().Be("За выбранный период заказов нет");
        empty.SummaryText.Should().MatchRegex(@"^Найдено 0 заказов, выдано на 0\s₽$");
    }

    [Fact, TestCase("CY25-24")]
    public async Task History_NumberRepeatsAcrossDays_ShowsAllMatchesWithDates_PickupDateDecidesThePeriod()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        var a = await PlaceStaffViewAsync(shop, [Line(p, 1)]);
        var b = await PlaceStaffViewAsync(shop, [Line(p, 1)]);
        var today = (await WorkingDayAsync(shop));

        // «вчерашний» заказ с тем же номером, что у сегодняшнего (номер уникален в пределах дня выдачи)
        await WithDbAsync(async db =>
        {
            var y = await db.Orders.SingleAsync(o => o.Id == a.Id);
            y.PickupDate = today.AddDays(-1);
            y.BusinessDate = today.AddDays(-1);
            y.Number = b.Number;
            await db.SaveChangesAsync();
        });

        var both = await HistoryOkAsync(shop, new { period = "Last7Days", number = b.Number });
        both.TotalCount.Should().Be(2, "поиск по номеру показывает все совпадения периода");
        both.Items.Select(i => i.PickupDate).Should().BeEquivalentTo(new[] { today, today.AddDays(-1) });
        both.Items.Select(i => i.PickupText).Distinct().Should().HaveCount(2, "у совпадений разные даты в подписи");

        (await HistoryOkAsync(shop, new { period = "Today", number = b.Number })).TotalCount.Should().Be(1);
        (await HistoryOkAsync(shop, new { period = "Yesterday" })).TotalCount.Should().Be(1);
        (await HistoryOkAsync(shop, new { period = "Custom", from = D(today.AddDays(-3)), to = D(today.AddDays(-2)) })).TotalCount.Should().Be(0);
    }

    [Fact, TestCase("CY25-25")]
    public async Task History_ErasedOrder_ShownWithoutPersonalData_NotFoundByCustomerSearch_NoCard()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        var phone = UniquePhone();
        var kept = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: phone, name: "Сохранённый");
        var erased = await PlaceStaffViewAsync(shop, [Line(p, 1)], phone: phone, name: "Обезличенный");
        await WithDbAsync(async db =>
        {
            var o = await db.Orders.SingleAsync(x => x.Id == erased.Id);
            o.PersonalDataErased = true;
            o.CustomerName = null;
            o.CustomerPhone = null;
            await db.SaveChangesAsync();
        });

        var page = await HistoryOkAsync(shop);
        var row = page.Items.Single(i => i.OrderId == erased.Id);
        row.PersonalDataErased.Should().BeTrue();
        row.CustomerName.Should().BeNull();
        row.CustomerPhoneMasked.Should().BeNull();
        page.Items.Single(i => i.OrderId == kept.Id).PersonalDataErased.Should().BeFalse();

        (await HistoryOkAsync(shop, new { customer = "Обезличенный" })).TotalCount.Should().Be(0, "обезличенный заказ в поиск по покупателю не попадает");
        (await HistoryOkAsync(shop, new { customer = phone.TrimStart('+')[^6..] })).Items.Select(i => i.OrderId).Should().BeEquivalentTo(new[] { kept.Id });

        (await CardAsync(shop, erased.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound, "карточки покупателя у обезличенного заказа нет");
        var card = await CardOkAsync(shop, kept.Id);
        card.OrdersTotal.Should().Be(1, "обезличенные заказы в карточку не входят");
    }

    [Fact, TestCase("CY25-29")]
    public async Task History_HostileCustomerInput_WildcardsAreLiteral_SurrogatePairAtTheLimit_NeverA500()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        var order = await PlaceStaffViewAsync(shop, [Line(p, 1)], name: "Иван");

        // подстановочные знаки LIKE — обычные символы, а не «любой текст»
        foreach (var wildcard in new[] { "%%", "__", "и%", "\\\\", "_ван" })
            (await HistoryOkAsync(shop, new { customer = wildcard })).TotalCount.Should().Be(0, $"«{wildcard}» — буквальная подстрока");
        (await HistoryOkAsync(shop, new { customer = "ван" })).TotalCount.Should().Be(1);

        // пара суррогатов, разрезанная границей в 100 символов, не роняет запрос
        var emoji = new string('а', 99) + "😀😀😀";
        var r = await HistoryAsync(shop, new { customer = emoji });
        ((int)r.StatusCode).Should().BeLessThan(500, await r.Content.ReadAsStringAsync());
        var digits = new string('7', 99) + "😀";
        ((int)(await HistoryAsync(shop, new { customer = digits })).StatusCode).Should().BeLessThan(500);

        // нулевой символ и одиночный суррогат в теле — 4xx или 200, но не 500
        var serverErrors = new List<string>();
        foreach (var raw in new[] { "{\"customer\":\"ab\\ud83d\"}", "{\"customer\":123}", "{\"statuses\":[\"Nope\"]}", "{\"statuses\":\"Issued\"}", "[]", "null" })
        {
            var resp = await AuthedClient(shop.OwnerToken).PostAsync($"/api/shops/{shop.Id}/order-history", new StringContent(raw, System.Text.Encoding.UTF8, "application/json"));
            if ((int)resp.StatusCode >= 500) serverErrors.Add($"order-history {raw} -> {(int)resp.StatusCode}");
        }

        // то же для заметки: юникод и эмодзи сохраняются как есть, NUL и одиночный суррогат не дают 500
        var emojiNote = "аллергия 🥜 на орехи — «звонить»";
        var ok = await PutNoteAsync(shop, order.Id, emojiNote);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetNoteOkAsync(shop, order.Id)).Note!.Text.Should().Be(emojiNote);
        foreach (var raw in new[] { "{\"text\":\"ab\\ud83d\"}", "{\"text\":42}", "{\"text\":[\"x\"]}" })
        {
            var resp = await AuthedClient(shop.OwnerToken).PutAsync($"/api/shops/{shop.Id}/customers/{order.Id}/note", new StringContent(raw, System.Text.Encoding.UTF8, "application/json"));
            if ((int)resp.StatusCode >= 500) serverErrors.Add($"note {raw} -> {(int)resp.StatusCode}");
        }
        serverErrors.Should().BeEmpty("ввод пользователя не должен давать 500");
        var longEmoji = string.Concat(Enumerable.Repeat("🥜", 500)); // 1000 UTF-16 единиц, 500 символов
        (await PutNoteAsync(shop, order.Id, longEmoji)).StatusCode.Should().Be(HttpStatusCode.OK, "предел считается по символам текста, эмодзи не удваивается до отказа");
    }

    /// <summary>
    /// BUG-25-02 / BUG-25-03 (найдено QA): нулевой символ U+0000 в поле «покупатель» истории и в тексте заметки даёт 500 — PostgreSQL не принимает его
    /// в тексте, а значение доходит до запроса без проверки. Ждём 4xx (или игнорирование символа), как для одиночного суррогата.
    /// Подозреваемое место: CustomerSearchTerm.Parse / ShopReportService.HistoryAsync и ShopCustomerService.PutNoteAsync (убрать или отклонить
    /// управляющие символы до обращения к БД).
    /// </summary>
    [Fact, TestCase("CY25-29b")]
    public async Task NulCharacter_InHistoryCustomerAndNoteText_IsRefusedOrIgnored_NeverA500()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        var order = await PlaceStaffViewAsync(shop, [Line(p, 1)]);
        var history = await AuthedClient(shop.OwnerToken).PostAsync($"/api/shops/{shop.Id}/order-history",
            new StringContent("{\"customer\":\"ab\\u0000cd\"}", System.Text.Encoding.UTF8, "application/json"));
        ((int)history.StatusCode).Should().BeLessThan(500, "order-history: " + await history.Content.ReadAsStringAsync());
        var note = await AuthedClient(shop.OwnerToken).PutAsync($"/api/shops/{shop.Id}/customers/{order.Id}/note",
            new StringContent("{\"text\":\"ab\\u0000cd\"}", System.Text.Encoding.UTF8, "application/json"));
        ((int)note.StatusCode).Should().BeLessThan(500, "note: " + await note.Content.ReadAsStringAsync());
    }

    // ── права и изоляция (§540.5, Q-25-5) ───────────────────────────────────────────

    [Fact, TestCase("CY25-26")]
    public async Task Permissions_StaffGets403OnSummaryOnly_OwnerAndSuperAdminAllowed_RemovedStaffLosesEverythingAtOnce()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        var staff = await AddShopStaffAsync(shop);
        var outsider = await CreateRoundClockShopAsync();
        var order = await PlaceStaffViewAsync(shop, [Line(p, 1)]);
        var admin = await LoginAsSuperAdminAsync();

        // сотрудник: история, лист сборки, карточка, заметка — да; сводка — 403 с пустым телом
        (await HistoryAsync(shop, new { }, staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PickListAsync(shop, "", staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CardAsync(shop, order.Id, staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutNoteAsync(shop, order.Id, "звонить перед выдачей", staff.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var forbidden = await SummaryAsync(shop, "", staff.Token);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadAsStringAsync()).Should().BeEmpty("403 — пустым телом");

        // владелец и SuperAdmin видят сводку
        (await SummaryAsync(shop)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SummaryAsync(shop, "", admin.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        // чужой магазин и аноним
        foreach (var token in new[] { outsider.OwnerToken })
        {
            (await HistoryAsync(shop, new { }, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await SummaryAsync(shop, "", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await PickListAsync(shop, "", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CardAsync(shop, order.Id, token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await PutNoteAsync(shop, order.Id, "x", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await AuthedClient(token).GetAsync($"/api/shops/{shop.Id}/customers/{order.Id}/note")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        (await AnonymousClient().PostAsync($"/api/shops/{shop.Id}/order-history", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AnonymousClient().GetAsync($"/api/shops/{shop.Id}/summary")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AnonymousClient().GetAsync($"/api/shops/{shop.Id}/picklist")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AnonymousClient().GetAsync($"/api/shops/{shop.Id}/customers/{order.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // сотрудник, удалённый из магазина, теряет доступ сразу — по каждому новому маршруту, по тому же токену
        await RemoveStaffAsync(shop, staff);
        (await HistoryAsync(shop, new { }, staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PickListAsync(shop, "", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CardAsync(shop, order.Id, staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AuthedClient(staff.Token).GetAsync($"/api/shops/{shop.Id}/customers/{order.Id}/note")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PutNoteAsync(shop, order.Id, "после удаления", staff.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AuthedClient(staff.Token).GetAsync($"/api/shops/{shop.Id}/catalog-listing")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetNoteOkAsync(shop, order.Id)).Note!.Text.Should().Be("звонить перед выдачей", "заметка сотрудника остаётся у магазина");
    }

    [Fact, TestCase("CY25-27")]
    public async Task EveryNewShopRoute_OnASalonId_Returns404_AndOnAnUnknownId_Returns404()
    {
        var (salonOwner, salon) = await CreateOwnerWithCompanyAsync();
        var c = AuthedClient(salonOwner.Token);
        var someOrder = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var shop = await CreateRoundClockShopAsync();

        foreach (var (id, token) in new[] { (salon.Id, salonOwner.Token), (unknown, shop.OwnerToken) })
        {
            var client = AuthedClient(token);
            var who = id == salon.Id ? "салон" : "неизвестный id";
            (await client.PostJsonAsync($"/api/shops/{id}/order-history", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound, $"order-history: {who}");
            (await client.GetAsync($"/api/shops/{id}/summary")).StatusCode.Should().Be(HttpStatusCode.NotFound, $"summary: {who}");
            (await client.GetAsync($"/api/shops/{id}/picklist")).StatusCode.Should().Be(HttpStatusCode.NotFound, $"picklist: {who}");
            (await client.GetAsync($"/api/shops/{id}/customers/{someOrder}")).StatusCode.Should().Be(HttpStatusCode.NotFound, $"customer card: {who}");
            (await client.GetAsync($"/api/shops/{id}/customers/{someOrder}/note")).StatusCode.Should().Be(HttpStatusCode.NotFound, $"note GET: {who}");
            (await client.PutJsonAsync($"/api/shops/{id}/customers/{someOrder}/note", new { text = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound, $"note PUT: {who}");
            (await client.GetAsync($"/api/shops/{id}/catalog-listing")).StatusCode.Should().Be(HttpStatusCode.NotFound, $"catalog-listing GET: {who}");
            (await client.PutJsonAsync($"/api/shops/{id}/catalog-listing", new { showInCatalog = true })).StatusCode.Should().Be(HttpStatusCode.NotFound, $"catalog-listing PUT: {who}");
            (await client.GetAsync($"/api/shops/{id}/notification-settings")).StatusCode.Should().Be(HttpStatusCode.NotFound, $"notification-settings: {who}");
        }
        _ = c;
    }

    [Fact, TestCase("CY25-28")]
    public async Task ForeignCustomerRef_Returns404_OnCardAndNote_NoOracleBetweenShops()
    {
        var a = await CreateRoundClockShopAsync();
        var b = await CreateRoundClockShopAsync();
        var pa = await CreateProductAsync(a);
        var pb = await CreateProductAsync(b);
        var phone = UniquePhone();
        var orderA = await PlaceStaffViewAsync(a, [Line(pa, 1)], phone: phone);
        var orderB = await PlaceStaffViewAsync(b, [Line(pb, 1)], phone: phone);

        // id заказа магазина А под ключом магазина Б — 404, как и неизвестный id: различить нельзя
        var foreign = await CardAsync(b, orderA.Id);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().BeEmpty();
        var unknown = await CardAsync(b, Guid.NewGuid());
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync()).Should().BeEmpty();
        (await AuthedClient(b.OwnerToken).GetAsync($"/api/shops/{b.Id}/customers/{orderA.Id}/note")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PutNoteAsync(b, orderA.Id, "подмена", b.OwnerToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // свой заказ — 200, и карточка содержит только свой магазин, хотя номер у покупателя один и тот же
        var card = await CardOkAsync(b, orderB.Id);
        card.OrdersTotal.Should().Be(1);
        card.Orders.Items.Should().ContainSingle(i => i.OrderId == orderB.Id);
        (await GetNoteOkAsync(a, orderA.Id)).Note.Should().BeNull();
    }
}
