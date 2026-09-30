using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 25, «Вызов 2», блок D: лист сборки (US-25-08). API_CONTRACT_CYCLE25.md §528. В лист попадают «Принят» всегда и «Новый» по
/// переключателю; «Готов» и конечные — нет. Ни имени, ни телефона покупателя [legal L18]. «Сегодня» — рабочий день магазина.
/// </summary>
public class Cycle25PickListTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private sealed record Kit(
        ShopCtx Shop, ProductDto Soup, ProductDto Cheese, StaffOrderDto O1, StaffOrderDto O2, StaffOrderDto NewOrder,
        StaffOrderDto ReadyOrder, StaffOrderDto IssuedOrder, StaffOrderDto RejectedOrder);

    private const string Nosy = "Ужасно Любопытный";

    private async Task<Kit> SeedAsync()
    {
        var shop = await CreateRoundClockShopAsync();
        var soups = await CreateCategoryAsync(shop, "Супы");
        var deli = await CreateCategoryAsync(shop, "Гастрономия");
        var soup = await CreateProductAsync(shop, "Борщ", 300m, categoryId: soups.Id, portion: "350 мл");
        var cheese = await CreateProductAsync(shop, "Сыр", 540m, ProductUnit.Weight, categoryId: deli.Id);

        var o1 = await PlaceStaffViewAsync(shop, [Line(soup, 2), Line(cheese, 500)], name: Nosy, comment: "без лука");
        o1 = await ActOkAsync(shop, o1, "accept");
        var o2 = await ActOkAsync(shop, await PlaceStaffViewAsync(shop, [Line(cheese, 1200)], name: Nosy), "accept");
        var pending = await PlaceStaffViewAsync(shop, [Line(soup, 1)], name: Nosy);
        var ready = await DriveAsync(shop, await PlaceStaffViewAsync(shop, [Line(soup, 5)], name: Nosy), "accept", "ready");
        var issued = await DriveAsync(shop, await PlaceStaffViewAsync(shop, [Line(soup, 7)], name: Nosy), "accept", "ready", "issue");
        var rejected = await ActOkAsync(shop, await PlaceStaffViewAsync(shop, [Line(soup, 9)], name: Nosy), "reject", reason: "нет");
        return new Kit(shop, soup, cheese, o1, o2, pending, ready, issued, rejected);
    }

    [Fact, TestCase("CY25-40")]
    public async Task ByProduct_SumsAcceptedAndNew_WeightBreakdown_CategoryOrder_ExcludesReadyAndFinal()
    {
        var kit = await SeedAsync();
        var shop = kit.Shop;

        var list = await PickListOkAsync(shop);
        list.ShopName.Should().Be(shop.Shop.Name);
        list.Date.Should().Be((await WorkingDayAsync(shop)), "по умолчанию — текущий рабочий день");
        list.IncludeNew.Should().BeTrue("«Включая непринятые» по умолчанию включён");
        list.OrderCount.Should().Be(3, "Принят ×2 + Новый; Готов, Выдан и Отклонён в лист не попадают");
        list.From.Should().BeNull();
        list.IntervalLabel.Should().Be("Весь день");
        list.GeneratedAtText.Should().MatchRegex(@"^по состоянию на \d{1,2}:\d\d$");
        list.GeneratedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));
        list.EmptyText.Should().BeNull();

        list.ByProduct.Select(p => p.Name).Should().Equal(new[] { "Борщ", "Сыр" }, "порядок — по категориям и порядку каталога");
        list.ByProduct.Select(p => p.CategoryName).Should().Equal(new[] { "Супы", "Гастрономия" });
        var soup = list.ByProduct[0];
        soup.TotalQuantity.Should().Be(3, "2 в принятом + 1 в новом; 5 (готов), 7 (выдан), 9 (отклонён) не считаются");
        soup.QuantityText.Should().Be("3 шт");
        soup.OrderCount.Should().Be(2);
        soup.HasUnaccepted.Should().BeTrue("в сумму входит непринятый заказ");
        soup.WeightBreakdownText.Should().BeNull("разбивка — только у весовых");
        var cheese = list.ByProduct[1];
        cheese.TotalQuantity.Should().Be(1700, "500 + 1200 г");
        cheese.QuantityText.Should().MatchRegex(@"^1,7 кг$");
        cheese.OrderCount.Should().Be(2);
        cheese.HasUnaccepted.Should().BeFalse();
        cheese.WeightBreakdownText.Should().StartWith("2 заказа: ").And.Contain("500 г").And.Contain("1,2 кг");

        // переключатель «Включая непринятые» выключен: только «Принят»
        var acceptedOnly = await PickListOkAsync(shop, "?includeNew=false");
        acceptedOnly.IncludeNew.Should().BeFalse();
        acceptedOnly.OrderCount.Should().Be(2);
        acceptedOnly.ByProduct.Single(p => p.Name == "Борщ").TotalQuantity.Should().Be(2);
        acceptedOnly.ByProduct.Should().OnlyContain(p => !p.HasUnaccepted);

        // сотрудник тоже видит лист
        (await PickListAsync(shop, "", (await AddShopStaffAsync(shop)).Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY25-41")]
    public async Task ByTime_OrdersWithLinesAndComment_UnacceptedMarked_NoCustomerNameOrPhone()
    {
        var kit = await SeedAsync();
        var shop = kit.Shop;
        var phone1 = kit.O1.CustomerPhone!;

        var response = await PickListAsync(shop);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain(Nosy).And.NotContain("Любопытный").And.NotContain(phone1).And.NotContain(phone1.TrimStart('+'), "в листе нет имени и телефона [legal L18]");
        var list = System.Text.Json.JsonSerializer.Deserialize<PickListDto>(raw, JsonHelpers.Options)!;

        var orders = list.ByTime.SelectMany(g => g.Orders).ToList();
        orders.Select(o => o.Number).Should().BeEquivalentTo(new[] { kit.O1.Number, kit.O2.Number, kit.NewOrder.Number });
        list.ByTime.Sum(g => g.Orders.Count).Should().Be(list.OrderCount);

        var first = orders.Single(o => o.Number == kit.O1.Number);
        first.Status.Should().Be(OrderStatus.Accepted);
        first.IsUnaccepted.Should().BeFalse();
        first.Comment.Should().Be("без лука", "комментарий к заказу в листе есть");
        first.PickupText.Should().StartWith("≈ ", "«как можно скорее» показывается по ориентиру");
        first.Lines.Should().HaveCount(2);
        first.Lines.Single(l => l.Name == "Борщ").QuantityText.Should().Be("2 шт");
        first.Lines.Single(l => l.Name == "Борщ").PortionText.Should().Be("350 мл");
        first.Lines.Single(l => l.Name == "Сыр").QuantityText.Should().Be("500 г");

        orders.Single(o => o.Number == kit.NewOrder.Number).IsUnaccepted.Should().BeTrue("непринятые помечены");
        orders.Select(o => o.Number).Should().NotContain(new[] { kit.ReadyOrder.Number, kit.IssuedOrder.Number, kit.RejectedOrder.Number });

        // группы по слотам — по возрастанию
        list.ByTime.Where(g => g.From is not null).Select(g => TimeSpan.Parse(g.From!)).Should().BeInAscendingOrder();
        list.Slots.Should().NotBeEmpty("сетка слотов дня — для выбора интервала");
        list.Slots.Should().OnlyContain(s => Regex.IsMatch(s.Label, @"^\d{2}:\d\d–\d{2}:\d\d$"));
    }

    [Fact, TestCase("CY25-42")]
    public async Task Interval_FromTo_SelectsByPickupTime_EmptyIntervalExplained_ThroughMidnightAndValidation()
    {
        var kit = await SeedAsync();
        var shop = kit.Shop;
        var day = (await WorkingDayAsync(shop));

        // окно ±30 минут вокруг ориентира заказа (в минутах от начала рабочего дня — «хвост» после полуночи даёт значения ≥ 1440)
        var history = await HistoryOkAsync(shop, new { period = "Today", number = kit.O1.Number });
        var start = history.Items.Single(i => i.OrderId == kit.O1.Id).PickupStartUtc;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.Shop.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(start, zone);
        var minutes = (int)(local - day.ToDateTime(TimeOnly.MinValue)).TotalMinutes;

        static string Hhmm(int m) { m = ((m % 1440) + 1440) % 1440; return $"{m / 60:00}:{m % 60:00}"; }
        // Окно строится так, чтобы попасть в заказ и в «хвост» после полуночи: интервал «через полночь» задаётся как «с вечера до утра».
        var inTail = minutes >= 1440;
        var fromMin = inTail ? 12 * 60 : minutes - 30;
        var toMin = minutes + 30;
        var around = await PickListOkAsync(shop, $"?from={Hhmm(fromMin)}&to={Hhmm(toMin)}");
        around.OrderCount.Should().Be(3, "все три заказа стоят в этом окне");
        around.From.Should().Be(Hhmm(fromMin));
        around.To.Should().Be(Hhmm(toMin));
        around.IntervalLabel.Should().Be($"{Hhmm(fromMin)}–{Hhmm(toMin)}");

        var (awayFrom, awayTo) = minutes + 180 < 1440 ? (minutes + 120, minutes + 180) : (minutes - 240, minutes - 180);
        if (inTail) (awayFrom, awayTo) = (60, 120); // 01:00–02:00 рабочего дня: раньше любого «хвоста» и до открытия — заказов там нет
        var away = await PickListOkAsync(shop, $"?from={Hhmm(awayFrom)}&to={Hhmm(awayTo)}");
        away.OrderCount.Should().Be(0);
        away.ByProduct.Should().BeEmpty();
        away.ByTime.Should().BeEmpty();
        away.EmptyText.Should().Be($"На {Hhmm(awayFrom)}–{Hhmm(awayTo)} заказов нет");

        // валидация
        async Task ExpectAsync(string query, string text)
        {
            var r = await PickListAsync(shop, query);
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, query);
            (await r.Content.ReadAsStringAsync()).Should().Contain(text, query);
        }
        await ExpectAsync("?from=10:00", "Укажите начало и конец интервала");
        await ExpectAsync("?to=10:00", "Укажите начало и конец интервала");
        await ExpectAsync("?from=25:00&to=26:00", "Укажите время в формате ЧЧ:ММ");
        await ExpectAsync("?from=10-00&to=11:00", "Укажите время в формате ЧЧ:ММ");
        await ExpectAsync("?from=10:00&to=abc", "Укажите время в формате ЧЧ:ММ");
        (await PickListAsync(shop, "?from=10:00&to=10:00")).StatusCode.Should().Be(HttpStatusCode.OK, "to ≤ from — через полночь, не ошибка");
        (await PickListAsync(shop, "?from=00:00&to=00:00")).StatusCode.Should().Be(HttpStatusCode.OK, "to=00:00 — до полуночи");
    }

    [Fact, TestCase("CY25-43")]
    public async Task Date_PastAllowedForViewing_FutureLimitedByPreorderDays_EmptyDayExplained()
    {
        var shop = await CreateRoundClockShopAsync();
        var p = await CreateProductAsync(shop);
        var today = (await WorkingDayAsync(shop));

        var yesterday = await PickListOkAsync(shop, $"?date={D(today.AddDays(-1))}");
        yesterday.Date.Should().Be(today.AddDays(-1));
        yesterday.OrderCount.Should().Be(0);
        yesterday.EmptyText.Should().MatchRegex(@"^На \d{1,2} [а-я]+ заказов нет$");
        yesterday.DateLabel.Should().NotBeNullOrEmpty();

        // заказы ко времени разрешены на 2 дня вперёд → +2 можно, +3 — нет
        await SetPickupAsync(shop, asap: true, scheduled: true, step: 30, preorderDays: 2, minPrep: 0);
        (await PickListAsync(shop, $"?date={D(today.AddDays(2))}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var tooFar = await PickListAsync(shop, $"?date={D(today.AddDays(3))}");
        tooFar.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooFar.Content.ReadAsStringAsync()).Should().StartWith("Дата — не позже ");

        // предзаказ на завтра попадает в лист завтрашнего дня и не попадает в сегодняшний
        var tomorrow = today.AddDays(1);
        var slot = await SlotAsync(shop.Slug, tomorrow, 0);
        var placed = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 3)], slot));
        var card = await GetStaffOrderAsync(shop, placed.Order.Token);
        await ActOkAsync(shop, card, "accept");
        var tomorrowList = await PickListOkAsync(shop, $"?date={D(tomorrow)}");
        tomorrowList.OrderCount.Should().Be(1);
        tomorrowList.ByProduct.Single().TotalQuantity.Should().Be(3);
        tomorrowList.ByTime.Single().Orders.Single().PickupText.Should().StartWith("к ", "слот — «к 9:00», без ≈");
        tomorrowList.Slots.Should().NotBeEmpty();
        (await PickListOkAsync(shop)).OrderCount.Should().Be(0, "сегодня этого заказа нет");
    }

    [Fact, TestCase("CY25-44")]
    public async Task List_TakesTheCurrentContent_StaffEditsAreVisible_ChangingStatusMovesOrderOut()
    {
        var kit = await SeedAsync();
        var shop = kit.Shop;
        var before = await PickListOkAsync(shop);
        before.ByProduct.Single(p => p.Name == "Борщ").TotalQuantity.Should().Be(3);

        // персонал правит состав: борщ 2 → 5 (вес 500 г остаётся)
        var current = await GetStaffOrderAsync(shop, (await CreatePublicTokenAsync(kit.O1.Id)));
        var soupLine = current.Items.Single(i => i.ProductId == kit.Soup.Id);
        var cheeseLine = current.Items.Single(i => i.ProductId == kit.Cheese.Id);
        var edit = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/orders/{current.Id}/items",
            new EditOrderInput(current.Version, [new EditOrderLineInput(soupLine.Id, null, 5), new EditOrderLineInput(cheeseLine.Id, null, 500)], null));
        edit.StatusCode.Should().Be(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        var after = await PickListOkAsync(shop);
        after.ByProduct.Single(p => p.Name == "Борщ").TotalQuantity.Should().Be(6, "5 после правки + 1 в новом");

        // заказ стал «Готов» — из листа ушёл: он уже собран
        var ready = await ActOkAsync(shop, kit.O2, "ready");
        ready.Status.Should().Be(OrderStatus.Ready);
        var third = await PickListOkAsync(shop);
        third.OrderCount.Should().Be(2);
        third.ByProduct.Single(p => p.Name == "Сыр").TotalQuantity.Should().Be(500);
        third.ByProduct.Single(p => p.Name == "Сыр").WeightBreakdownText.Should().Contain("500 г");
    }

    private async Task<string> CreatePublicTokenAsync(Guid orderId) =>
        await WithDbAsync(db => db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.PublicToken).SingleAsync());
}
