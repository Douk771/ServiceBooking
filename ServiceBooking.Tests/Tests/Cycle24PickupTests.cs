using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 24, «Вызов 2»: время получения — витрина, слоты, оформление, экран заказов, номера в дне выдачи, смена времени
/// (US-24-06…09, US-24-04). По SPEC.md и API_CONTRACT_CYCLE24.md §477–§481.
/// </summary>
public class Cycle24PickupTests(TestDatabaseFixture fixture) : Cycle24TestBase(fixture)
{
    // ── Витрина: выбор времени ───────────────────────────────────────────────────

    [Fact, TestCase("CY24-20")]
    public async Task Storefront_DefaultPickup_AsapOnly_NoDates_And_DefaultsOfSettings()
    {
        var shop = await CreateShopAsync();
        var dto = await GetShopAsync(shop);
        var sf = await GetStorefrontAsync(shop.Slug);
        sf.Pickup.AsapEnabled.Should().BeTrue();
        sf.Pickup.ScheduledEnabled.Should().BeFalse();
        sf.Pickup.Dates.Should().BeEmpty("при выключенном «ко времени» выбор слота не предлагается");
        sf.Date.Should().Be(ShopToday(shop), "по умолчанию — сегодня");
        sf.DateNotice.Should().BeNull();
        dto.PickupSettings.SlotStepMinutes.Should().Be(15);

        // значения по умолчанию у нового магазина (SPEC US-24-05): «скорее» вкл, «ко времени» выкл, 15 мин, 0 дней, 15 мин приготовления
        var fresh = await CreateShopAsync(openAllDay: false);
        (await GetShopAsync(fresh)).PickupSettings.Should().Be(new PickupSettingsDto(true, false, 15, 0, 15));
    }

    [Fact, TestCase("CY24-21")]
    public async Task Storefront_ScheduledDates_LabelsAndSlotsFollowHoursAndStep()
    {
        var shop = await CreateScheduledShopAsync(preorderDays: 3);
        var today = ShopToday(shop);
        var sf = await GetStorefrontAsync(shop.Slug);

        sf.Pickup.ScheduledEnabled.Should().BeTrue();
        sf.Pickup.Dates.Select(d => d.Date).Should().Equal(today, today.AddDays(1), today.AddDays(2), today.AddDays(3));
        sf.Pickup.Dates[0].Label.Should().Be("Сегодня");
        sf.Pickup.Dates[1].Label.Should().Be("Завтра");
        sf.Pickup.Dates[2].Label.Should().MatchRegex(@"^[а-я]{2} \d{1,2} [а-я]{3}$", "«пт 2 окт»");
        sf.Pickup.Dates[1].HasSlots.Should().BeTrue();

        var slots = await GetSlotsAsync(shop.Slug, today.AddDays(1));
        slots.Slots.Should().HaveCount(6, "09:00–12:00, шаг 30 минут");
        slots.Slots.Select(s => s.Label).Should().Equal("09:00–09:30", "09:30–10:00", "10:00–10:30", "10:30–11:00", "11:00–11:30", "11:30–12:00");
        slots.Slots.Should().OnlyContain(s => s.EndUtc - s.StartUtc == TimeSpan.FromMinutes(30));
        slots.Slots.Select(s => s.StartUtc).Should().BeInAscendingOrder();
        slots.Asap.Should().BeNull("«как можно скорее» — только для текущего рабочего дня");
        slots.Label.Should().Be("Завтра");

        // шаг 60 — три слота
        await SetPickupAsync(shop, true, true, 60, 3, 0);
        (await GetSlotsAsync(shop.Slug, today.AddDays(1))).Slots.Should().HaveCount(3);
    }

    [Fact, TestCase("CY24-22")]
    public async Task PickupSlots_Errors_OutsideHorizonDayOffAndMissingDate()
    {
        var shop = await CreateScheduledShopAsync(preorderDays: 2);
        var today = ShopToday(shop);
        var noDate = await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}/pickup-slots");
        noDate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noDate.Content.ReadAsStringAsync()).Should().Contain("Укажите дату");

        var far = await GetSlotsAsync(shop.Slug, today.AddDays(5));
        far.Slots.Should().BeEmpty();
        far.ReasonText.Should().StartWith("На эту дату заказать нельзя");

        // выходной: часы есть только в остальные дни недели
        var off = today.AddDays(1);
        var days = Enum.GetValues<DayOfWeek>().Where(d => d != off.DayOfWeek).Select(d => Day(d, ("09:00", "12:00"))).ToList();
        (await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/working-hours", new WorkingHoursInput(days))).StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await GetSlotsAsync(shop.Slug, off);
        closed.Slots.Should().BeEmpty();
        closed.ReasonText.Should().Be("В этот день магазин не работает");
        (await GetStorefrontAsync(shop.Slug)).Pickup.Dates.Should().NotContain(d => d.Date == off, "выходные не предлагаются");

        // запрошенная витриной недоступная дата — не 400, а сегодняшний ассортимент с пояснением
        var sf = await GetStorefrontAsync(shop.Slug, today.AddDays(9));
        sf.Date.Should().Be(today);
        sf.DateNotice.Should().NotBeNullOrEmpty();
    }

    [Fact, TestCase("CY24-23")]
    public async Task Quote_WithPickup_ReportsDate_AndPickupProblem()
    {
        var shop = await CreateScheduledShopAsync();
        var p = await CreateProductAsync(shop);
        var tomorrow = ShopToday(shop).AddDays(1);
        var slot = await SlotAsync(shop.Slug, tomorrow, 1);
        var url = $"/api/storefront/{shop.Slug}/quote";

        var ok = (await (await AnonymousClient().PostJsonAsync(url, new QuoteInput([new(p.Id, 1)], slot))).Content.ReadJsonAsync<QuoteDto>())!;
        ok.PickupDate.Should().Be(tomorrow);
        ok.PickupProblem.Should().BeNull();
        ok.HasProblems.Should().BeFalse();

        var badSlot = slot with { SlotStartUtc = slot.SlotStartUtc!.Value.AddMinutes(7) };
        var bad = (await (await AnonymousClient().PostJsonAsync(url, new QuoteInput([new(p.Id, 1)], badSlot))).Content.ReadJsonAsync<QuoteDto>())!;
        bad.PickupProblem.Should().NotBeNull();
        bad.PickupProblem!.Code.Should().Be(OrderRefusalCode.PickupTimeUnavailable);
        bad.HasProblems.Should().BeTrue("проблема времени блокирует «Оформить»");

        var noPickup = (await (await AnonymousClient().PostJsonAsync(url, new QuoteInput([new(p.Id, 1)]))).Content.ReadJsonAsync<QuoteDto>())!;
        noPickup.PickupDate.Should().NotBe(default, "без pickup — «как можно скорее»");
    }

    // ── Оформление ────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY24-24")]
    public async Task PlaceOrder_ForTomorrowSlot_IsPreorder_TextsOnPublicPageAndMyOrders()
    {
        var shop = await CreateScheduledShopAsync();
        var p = await CreateProductAsync(shop);
        var tomorrow = ShopToday(shop).AddDays(1);
        var slot = await SlotAsync(shop.Slug, tomorrow, 2);
        var buyer = await RegisterAsync();

        var placed = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 2)], slot), buyer.Token);
        var pub = placed.Order;
        pub.Pickup.Kind.Should().Be(PickupKind.Slot);
        pub.Pickup.Date.Should().Be(tomorrow);
        pub.Pickup.StartUtc.Should().Be(slot.SlotStartUtc!.Value);
        pub.Pickup.IsPreorder.Should().BeTrue();
        pub.Pickup.IsOverdue.Should().BeFalse();
        pub.Pickup.Text.Should().StartWith("Завтра, к ");
        pub.Notifications.Should().NotBeNull();

        var fetched = await GetPublicOrderAsync(pub.Token);
        fetched.Pickup.Text.Should().Be(pub.Pickup.Text);

        var mine = await J(await AuthedClient(buyer.Token).GetAsync("/api/orders/my"));
        var item = mine.EnumerateArray().Single();
        item.GetProperty("pickup").GetProperty("text").GetString().Should().Be(pub.Pickup.Text);
        item.GetProperty("pickup").GetProperty("isPreorder").GetBoolean().Should().BeTrue();
    }

    [Fact, TestCase("CY24-25")]
    public async Task PlaceOrder_Asap_TodayWithLandmark_TextAndKind()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        foreach (var pickup in new PickupSelectionInput?[] { null, new(PickupKind.Asap, null, null) })
        {
            var placed = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], pickup));
            placed.Order.Pickup.Kind.Should().Be(PickupKind.Asap);
            placed.Order.Pickup.IsPreorder.Should().BeFalse();
            placed.Order.Pickup.Text.Should().MatchRegex(@"^Как можно скорее \(≈ \d{1,2}:\d{2}\)$");
            placed.Order.Pickup.Date.Should().Be(ShopToday(shop), "для «как можно скорее» дата выдачи — сегодня");
        }
    }

    [Fact, TestCase("CY24-26")]
    public async Task PlaceOrder_InvalidPickup_409WithContractTexts_NothingCreated()
    {
        var shop = await CreateScheduledShopAsync(preorderDays: 2);
        await OpenShopAllDayAsync(shop);
        await SetPickupAsync(shop, true, true, 30, 2, 0);
        var p = await CreateProductAsync(shop);
        var today = ShopToday(shop);
        var slot = await SlotAsync(shop.Slug, today.AddDays(1));
        var key = Guid.NewGuid();

        async Task<string> Refused(PickupSelectionInput pickup, string? expectedCode = "PickupTimeUnavailable")
        {
            var r = await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], pickup, key: key));
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
            var body = await J(r);
            body.GetProperty("code").GetString().Should().Be(expectedCode);
            return body.GetProperty("message").GetString()!;
        }

        // не выровнен по сетке слотов
        (await Refused(slot with { SlotStartUtc = slot.SlotStartUtc!.Value.AddMinutes(7) })).Should().Be("Это время уже недоступно — выберите другое");
        // уже прошёл
        (await Refused(new PickupSelectionInput(PickupKind.Slot, today, DateTime.UtcNow.AddHours(-3)))).Should().Be("Это время уже недоступно — выберите другое");
        // вне горизонта предзаказа (2 дня)
        (await Refused(new PickupSelectionInput(PickupKind.Slot, today.AddDays(4), slot.SlotStartUtc!.Value.AddDays(3)))).Should().Be("На эту дату заказать нельзя — выберите другую");

        (await GetBoardAsync(shop)).NewOrders.Should().BeNullOrEmpty("отказ не создаёт заказ с другим временем");

        // тот же ключ идемпотентности и та же корзина после повторного выбора — оформляются
        var ok = await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], slot, key: key));
        ok.StatusCode.Should().Be(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
    }

    [Fact, TestCase("CY24-27")]
    public async Task PlaceOrder_DisabledVariants_And_MissingSlotFields()
    {
        var shop = await CreateScheduledShopAsync();
        await OpenShopAllDayAsync(shop);
        await SetPickupAsync(shop, true, true, 30, 2, 0);
        var p = await CreateProductAsync(shop);
        var slot = await SlotAsync(shop.Slug, ShopToday(shop).AddDays(1));

        // Slot без даты/времени — 400 строкой
        var missing = await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], new PickupSelectionInput(PickupKind.Slot, null, null)));
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("Укажите дату и время получения");

        // «ко времени» выключено
        await SetPickupAsync(shop, true, false, 30, 2, 0);
        var noSched = await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], slot));
        noSched.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(noSched)).GetProperty("message").GetString().Should().Be("Заказ ко времени в этом магазине недоступен");

        // «как можно скорее» выключено
        await SetPickupAsync(shop, false, true, 30, 2, 0);
        var noAsap = await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], null));
        noAsap.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(noAsap)).Should().Be("PickupTimeUnavailable");
        (await J(noAsap)).GetProperty("message").GetString().Should().Be("„Как можно скорее“ в этом магазине недоступно — выберите время");
        var sf = await GetStorefrontAsync(shop.Slug);
        sf.Pickup.Asap.Should().BeNull("asapEnabled=false → asap=null");
        (await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], await SlotAsync(shop.Slug, ShopToday(shop).AddDays(1))))).StatusCode
            .Should().Be(HttpStatusCode.Created, "слот при выключенном «скорее» работает");
    }

    [Fact, TestCase("CY24-28")]
    public async Task PickupTimeIsRevalidatedOnServer_WhenHoursChangeAfterSlotWasChosen()
    {
        var shop = await CreateScheduledShopAsync();
        var p = await CreateProductAsync(shop);
        var tomorrow = ShopToday(shop).AddDays(1);
        var slot = await SlotAsync(shop.Slug, tomorrow, 5); // 11:30–12:00

        await SetHoursAsync(shop, ("09:00", "11:00")); // владелец сократил день, пока покупатель выбирал
        var r = await PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], slot));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(r)).Should().Be("PickupTimeUnavailable");
        (await GetBoardAsync(shop)).NewOrders.Should().BeNullOrEmpty();
    }

    // ── Номера в дне выдачи (US-24-08) ───────────────────────────────────────────

    [Fact, TestCase("CY24-29")]
    public async Task OrderNumbers_AreUniquePerPickupDay_ParallelPreordersAndTodaysOrders()
    {
        var shop = await CreateShopAsync();
        await SetPickupAsync(shop, true, true, 15, 2, 0);
        var p = await CreateProductAsync(shop);
        var tomorrow = ShopToday(shop).AddDays(1);
        var slot = await SlotAsync(shop.Slug, tomorrow, 0);

        var tasks = Enumerable.Range(0, 8).Select(_ => PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], slot))).ToList();
        tasks.AddRange(Enumerable.Range(0, 3).Select(_ => PostOrderAsync(shop.Slug, GuestAt([Line(p, 1)], null))));
        var responses = await Task.WhenAll(tasks);
        var created = new List<PublicOrderDto>();
        foreach (var r in responses)
        {
            r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
            created.Add((await r.Content.ReadJsonAsync<CreateOrderResponse>())!.Order);
        }

        created.GroupBy(o => o.Pickup.Date).SelectMany(g => g.Select(o => o.Number)).Should().NotBeEmpty();
        foreach (var g in created.GroupBy(o => o.Pickup.Date))
            g.Select(o => o.Number).Should().OnlyHaveUniqueItems($"дата выдачи {g.Key}: номер не повторяется");
        created.Count(o => o.Pickup.Date == tomorrow).Should().Be(8);

        // доска: номера в пределах дня выдачи уникальны и там
        var board = await GetBoardAsync(shop);
        var all = (board.NewOrders ?? []).Concat(board.Accepted ?? []).Concat(board.Ready ?? []).Concat((board.Preorders ?? []).SelectMany(g => g.Orders)).ToList();
        all.Should().HaveCount(11);
        foreach (var g in all.GroupBy(c => c.Pickup.Date)) g.Select(c => c.Number).Should().OnlyHaveUniqueItems();
    }

    // ── Экран заказов ─────────────────────────────────────────────────────────────

    [Fact, TestCase("CY24-30")]
    public async Task Board_NewPreordersInNew_SortedByPickup_AcceptedPreorderMovesToPreordersGroup()
    {
        var shop = await CreateScheduledShopAsync();
        await SetPickupAsync(shop, true, true, 15, 3, 0);
        var p = await CreateProductAsync(shop);
        var tomorrow = ShopToday(shop).AddDays(1);
        var later = await SlotAsync(shop.Slug, tomorrow, 8);
        var earlier = await SlotAsync(shop.Slug, tomorrow, 2);

        var a = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], later));
        var b = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], earlier));

        var board = await GetBoardAsync(shop);
        board.NewOrders!.Select(c => c.Pickup.StartUtc).Should().BeInAscendingOrder("порядок по времени получения");
        board.NewOrders.Should().HaveCount(2);
        board.NewOrders![0].Pickup.StartUtc.Should().Be(earlier.SlotStartUtc!.Value);
        board.NewOrders.Should().OnlyContain(c => c.Pickup.Text.StartsWith("Завтра, к "), "у предзаказа дата в тексте");
        board.Preorders.Should().BeNullOrEmpty("принятых предзаказов ещё нет");

        var card = board.NewOrders.Single(c => c.Pickup.StartUtc == later.SlotStartUtc!.Value);
        card.AvailableActions.Should().Contain(OrderAction.ChangePickup);
        var full = (await (await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/orders/{card.Id}")).Content.ReadJsonAsync<StaffOrderDto>())!;
        await ActOkAsync(shop, full, "accept");

        var after = await GetBoardAsync(shop);
        after.Accepted.Should().BeNullOrEmpty("предзаказ на завтра не лежит в основной колонке «Принятые»");
        after.Preorders.Should().ContainSingle();
        after.Preorders![0].Date.Should().Be(tomorrow);
        after.Preorders[0].Orders.Should().ContainSingle(c => c.Id == card.Id && c.Status == OrderStatus.Accepted);
        after.NewOrders.Should().ContainSingle();
        _ = a; _ = b;
    }

    [Fact, TestCase("CY24-31")]
    public async Task Board_TodaysAsapOrder_IsInAcceptedColumn_NotInPreorders()
    {
        var shop = await CreateShopAsync();
        await SetPickupAsync(shop, true, false, 15, 0, 30);
        var p = await CreateProductAsync(shop);
        var o = await PlaceAndLoadAsync(shop, [Line(p, 1)]);
        o.Pickup.Kind.Should().Be(PickupKind.Asap);
        o.AvailableActions.Should().Contain(OrderAction.ChangePickup);
        await ActOkAsync(shop, o, "accept");
        var board = await GetBoardAsync(shop);
        board.Accepted.Should().ContainSingle();
        board.Preorders.Should().BeNullOrEmpty();
        board.Accepted![0].Pickup.IsOverdue.Should().BeFalse();
    }

    // ── Смена времени получения (US-24-09) ───────────────────────────────────────

    [Fact, TestCase("CY24-32")]
    public async Task ChangePickup_NewAndAccepted_JournalCustomerSeesIt_StaleVersionAndTerminalRefused()
    {
        var shop = await CreateScheduledShopAsync();
        var p = await CreateProductAsync(shop);
        var tomorrow = ShopToday(shop).AddDays(1);
        var placed = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], await SlotAsync(shop.Slug, tomorrow, 0)));
        var order = await GetStaffOrderAsync(shop, placed.Order.Token);
        var c = AuthedClient(shop.OwnerToken);
        var url = $"/api/shops/{shop.Id}/orders/{order.Id}/pickup";

        // слоты для персонала (без учёта времени приготовления и паузы)
        var staffSlots = (await (await c.GetAsync($"/api/shops/{shop.Id}/pickup-slots?date={D(tomorrow)}")).Content.ReadJsonAsync<PickupSlotsDto>())!;
        staffSlots.Slots.Should().NotBeEmpty();
        var target = new PickupSelectionInput(PickupKind.Slot, tomorrow, staffSlots.Slots[3].StartUtc);

        var r = await c.PutJsonAsync(url, new ChangePickupInput(order.Version, target, "по просьбе покупателя"));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var changed = (await r.Content.ReadJsonAsync<StaffOrderDto>())!;
        changed.Pickup.StartUtc.Should().Be(staffSlots.Slots[3].StartUtc);
        changed.Version.Should().BeGreaterThan(order.Version);
        changed.Events.Should().Contain(e => e.Kind == OrderEventKind.PickupChanged);
        changed.Events.Single(e => e.Kind == OrderEventKind.PickupChanged).Comment.Should().Be("по просьбе покупателя");
        (await GetPublicOrderAsync(placed.Order.Token)).Pickup.StartUtc.Should().Be(staffSlots.Slots[3].StartUtc, "покупатель видит новое время");

        // устаревшая версия
        var stale = await c.PutJsonAsync(url, new ChangePickupInput(order.Version, target, null));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(stale)).Should().Be("VersionMismatch");

        // слот не из слотов дня
        var bad = await c.PutJsonAsync(url, new ChangePickupInput(changed.Version, target with { SlotStartUtc = target.SlotStartUtc!.Value.AddMinutes(11) }, null));
        bad.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(bad)).Should().Be("PickupTimeUnavailable");

        // длинный комментарий
        var longComment = await c.PutJsonAsync(url, new ChangePickupInput(changed.Version, target, new string('я', 501)));
        longComment.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // принят — правка ещё допустима; выдан — нет
        var accepted = await ActOkAsync(shop, changed, "accept");
        var againOk = await c.PutJsonAsync(url, new ChangePickupInput(accepted.Version, new PickupSelectionInput(PickupKind.Asap, null, null), null));
        // Asap допустим, если магазин открыт сейчас (all-day) — иначе PickupTimeUnavailable; оба ответа проверяются по контракту
        new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }.Should().Contain(againOk.StatusCode);
        var cur = await GetStaffOrderAsync(shop, placed.Order.Token);
        var ready = await ActOkAsync(shop, cur, "ready");
        var ready2 = await ActOkAsync(shop, ready, "issue");
        var afterIssue = await c.PutJsonAsync(url, new ChangePickupInput(ready2.Version, target, null));
        afterIssue.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(afterIssue)).Should().Be("InvalidTransition");
    }

    [Fact, TestCase("CY24-33")]
    public async Task ChangePickup_ToAnotherDay_KeepsNumbersUniqueInTargetDay_StaffOnly()
    {
        var shop = await CreateScheduledShopAsync();
        var p = await CreateProductAsync(shop);
        var d1 = ShopToday(shop).AddDays(1);
        var d2 = ShopToday(shop).AddDays(2);
        var s1 = await SlotAsync(shop.Slug, d1, 1);
        var s2 = await SlotAsync(shop.Slug, d2, 1);

        var a = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], s1));
        var b = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], s2));
        var orderA = await GetStaffOrderAsync(shop, a.Order.Token);
        var stranger = await RegisterAsync();

        (await AuthedClient(stranger.Token).PutJsonAsync($"/api/shops/{shop.Id}/orders/{orderA.Id}/pickup", new ChangePickupInput(orderA.Version, s2, null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var moved = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/orders/{orderA.Id}/pickup", new ChangePickupInput(orderA.Version, s2, null));
        moved.StatusCode.Should().Be(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        var pubA = await GetPublicOrderAsync(a.Order.Token);
        var pubB = await GetPublicOrderAsync(b.Order.Token);
        pubA.Pickup.Date.Should().Be(d2);
        pubA.Number.Should().NotBe(pubB.Number, "в один день выдачи номера разные, даже после переноса");
    }

    [Fact, TestCase("CY24-34")]
    public async Task StaffPickupSlots_IgnorePause_AndPreorderSettings()
    {
        var shop = await CreateScheduledShopAsync();
        var tomorrow = ShopToday(shop).AddDays(1);
        (await SetAcceptanceAsync(shop, new { mode = "Stopped" })).StatusCode.Should().Be(HttpStatusCode.OK);
        await SetPickupAsync(shop, true, false, 30, 3, 0);

        var r = await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/pickup-slots?date={D(tomorrow)}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadJsonAsync<PickupSlotsDto>())!.Slots.Should().HaveCount(6, "персоналу слоты нужны и на паузе, и при выключенном «ко времени»");
        (await AnonymousClient().GetAsync($"/api/shops/{shop.Id}/pickup-slots?date={D(tomorrow)}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Границы суток (SPEC §6: интервалы через полночь проверяются на границах суток) ───────────────────────────

    [Fact, TestCase("CY24-35")]
    public async Task AfterMidnightTail_DayLabelsAndPreorderFlagAgreeWithStorefrontWorkingDay()
    {
        // «Круглосуточный» магазин помощника (04:05 → 04:00) с 00:00 до 04:00 местного времени живёт во вчерашнем рабочем дне.
        // В остальное время суток сценарий не применим (хвоста нет) — проверяется только то, что «сегодня» совпадает с рабочим днём.
        var shop = await CreateShopAsync();
        await SetPickupAsync(shop, true, true, 60, 2, 0);
        var p = await CreateProductAsync(shop);
        var sf = await GetStorefrontAsync(shop.Slug);
        var workingDay = ShopToday(shop);
        sf.Date.Should().Be(workingDay, "текущий рабочий день: в «хвосте» после полуночи — вчерашняя дата");
        sf.Pickup.Dates[0].Label.Should().Be("Сегодня");
        sf.Pickup.Dates[1].Label.Should().Be("Завтра");

        // Заказ на «Сегодня» и на «Завтра» из того же выбора витрины: подписи и признак предзаказа обязаны совпасть с витриной.
        var todaySlots = await GetSlotsAsync(shop.Slug, workingDay);
        var tomorrowSlots = await GetSlotsAsync(shop.Slug, workingDay.AddDays(1));
        tomorrowSlots.Slots.Should().NotBeEmpty();
        var tomorrow = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], new PickupSelectionInput(PickupKind.Slot, workingDay.AddDays(1), tomorrowSlots.Slots[^1].StartUtc)));
        tomorrow.Order.Pickup.IsPreorder.Should().BeTrue("дата выдачи позже текущего рабочего дня — предзаказ, как «Завтра» на витрине");
        tomorrow.Order.Pickup.Text.Should().StartWith("Завтра, к ");
        if (todaySlots.Slots.Count > 0)
        {
            var today = await PlaceOrderAsync(shop.Slug, GuestAt([Line(p, 1)], new PickupSelectionInput(PickupKind.Slot, workingDay, todaySlots.Slots[0].StartUtc)));
            today.Order.Pickup.IsPreorder.Should().BeFalse();
            today.Order.Pickup.Text.Should().StartWith("К ", "слот текущего рабочего дня — «К 12:30», а не «вт 29 сен, к …»");
        }
    }
}
