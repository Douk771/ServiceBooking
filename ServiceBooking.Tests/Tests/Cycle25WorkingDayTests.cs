using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 25, «Вызов 2», блок G: T-25-04 («сегодня» везде считается от рабочего дня магазина) и T-25-03 (повтор дня недели → 400).
/// Критерий SPEC: при часах «пт 18:00–03:00» заказ в сб 01:00 на 01:30 в push, сообщении и «Моих заказах» называется «к 1:30» без «завтра» и без даты.
/// Чтобы это проверялось В ЛЮБОЕ время прогона, а не только ночью, магазину ставится пояс с фиксированным смещением (Etc/GMT±N), в котором
/// сейчас 00:мм, — то есть «хвост» ночного интервала (<see cref="Cycle25TestBase.PlaceShopInAfterMidnightAsync"/>).
/// </summary>
public class Cycle25WorkingDayTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private sealed record Night(ShopCtx Shop, ProductDto Product, DateOnly WorkingDay, DateTime Local, string Zone, PickupSelectionInput Slot0130);

    private static readonly Regex DateInText = new(@"\d{1,2} (янв|фев|мар|апр|мая|июн|июл|авг|сен|окт|ноя|дек)", RegexOptions.IgnoreCase);

    /// <summary>Магазин с часами «каждый день 18:00–03:00», в поясе, где сейчас 00:мм; слот 01:30 сегодняшнего календарного дня — в рабочем дне «вчера».</summary>
    private async Task<Night> NightShopAsync(bool asap = false)
    {
        var shop = await CreateShopAsync(openAllDay: false);
        var (local, zoneId) = await PlaceShopInAfterMidnightAsync(shop);
        await SetHoursAsync(shop, ("18:00", "03:00"));
        await SetPickupAsync(shop, asap: asap, scheduled: true, step: 30, preorderDays: 2, minPrep: 0);
        var product = await CreateProductAsync(shop, "Шаурма", 250m);
        var workingDay = DateOnly.FromDateTime(local).AddDays(-1);

        var storefront = await GetStorefrontAsync(shop.Slug);
        storefront.Date.Should().Be(workingDay, "в «хвосте» после полуночи текущий рабочий день — вчерашний");
        var slots = await GetSlotsAsync(shop.Slug, workingDay);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var slot = slots.Slots.Single(s =>
        {
            var t = TimeZoneInfo.ConvertTimeFromUtc(s.StartUtc, zone);
            return DateOnly.FromDateTime(t) == DateOnly.FromDateTime(local) && t.Hour == 1 && t.Minute == 30;
        });
        return new Night(shop, product, workingDay, local, zoneId, new PickupSelectionInput(PickupKind.Slot, workingDay, slot.StartUtc));
    }

    private static HttpClient Authed(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, string? token) => ClientOn(host, token);

    // ── T-25-04: тексты персоналу — push и MAX ───────────────────────────────────────

    [Fact, TestCase("CY25-60")]
    public async Task StaffPushAndMaxTexts_CountTodayFromTheWorkingDay_SlotAfterMidnightReadsK130()
    {
        var night = await NightShopAsync();
        var shop = night.Shop;

        // push персоналу
        await using (var push = new PushEnabledFactory(ConnectionString))
        {
            var sub = await Authed(push, shop.OwnerToken).PostJsonAsync("/api/push/subscriptions", new CreatePushSubscriptionInput(
                "https://push.example.test/cy25-night/" + Guid.NewGuid(), new CreatePushSubscriptionKeysInput("p", "a"), "d", CompanyKind.Orders));
            sub.IsSuccessStatusCode.Should().BeTrue(await sub.Content.ReadAsStringAsync());
            var r = await Authed(push, null).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", GuestAt([Line(night.Product, 1)], night.Slot0130));
            r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
            var number = (await r.Content.ReadJsonAsync<CreateOrderResponse>())!.Order.Number;

            var row = await WithDbAsync(db => db.StaffPushNotifications.AsNoTracking().SingleAsync(x => x.CompanyId == shop.Id && x.Type == NotificationType.StaffOrderCreated));
            row.Payload.Should().Contain($"Новый заказ № {number}").And.Contain("к 1:30 · 1 позиция");
            DateInText.IsMatch(row.Payload).Should().BeFalse("«сегодня» — рабочий день: ни даты, ни «завтра» в тексте нет");
            row.Payload.Should().NotContainEquivalentOf("завтра");
        }

        // MAX персоналу
        await WithDbAsync(db => db.StaffMaxMessages.ExecuteDeleteAsync());
        await using var mx = new StaffMaxTestFactory(ConnectionString);
        mx.EnsureWebhookSubscribed();
        var owner = Authed(mx, shop.OwnerToken);
        var session = (await (await owner.PostAsync("/api/staff-max/link-sessions", null)).Content.ReadJsonAsync<ServiceBooking.API.DTOs.StaffMax.StaffMaxLinkSessionDto>())!;
        var payload = System.Web.HttpUtility.ParseQueryString(new Uri(session.DeepLink).Query)["start"];
        var chat = "chat25-night-" + Guid.NewGuid().ToString("N")[..8];
        await owner.PostAsJsonAsync($"/api/phone-verification/max/webhook/{StaffMaxTestFactory.WebhookToken}",
            new { update_type = "bot_started", payload, user = new { user_id = "night-user" }, chat = new { chat_id = chat } });

        var placed = await Authed(mx, null).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", GuestAt([Line(night.Product, 1)], night.Slot0130));
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var n2 = (await placed.Content.ReadJsonAsync<CreateOrderResponse>())!.Order;
        await mx.RunDispatchPassAsync();
        var text = mx.Messenger.TextsTo(chat).Should().ContainSingle().Subject;
        text.Split('\n')[0].Should().StartWith($"Новый заказ № {n2.Number} · к 1:30 · 1 позиция · ≈ 250");
        DateInText.IsMatch(text.Split('\n')[0]).Should().BeFalse();

        // и отмена покупателем — то же время без даты
        (await Authed(mx, null).PostAsync($"/api/orders/public/{n2.Token}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await mx.RunDispatchPassAsync();
        mx.Messenger.TextsTo(chat).Last().Split('\n')[0].Should().Be($"Покупатель отменил заказ № {n2.Number} (к 1:30) · {shop.Shop.Name}");
    }

    // ── T-25-04: «Мои заказы» и сообщения покупателю ─────────────────────────────────

    [Fact, TestCase("CY25-61")]
    public async Task MyOrdersAndCustomerMessages_CountTodayFromTheWorkingDay_NextWorkingDayStillSaysTomorrow()
    {
        var night = await NightShopAsync();
        var shop = night.Shop;
        var buyer = await RegisterAsync();

        var placed = await PlaceOrderAsync(shop.Slug, GuestAt([Line(night.Product, 1)], night.Slot0130), buyer.Token);
        placed.Order.Pickup.Text.Should().Be("К 1:30", "слот рабочего дня после полуночи — «К 1:30», без «завтра» и без даты");
        placed.Order.Pickup.IsPreorder.Should().BeFalse();

        var mine = await J(await AuthedClient(buyer.Token).GetAsync("/api/orders/my"));
        var item = mine.EnumerateArray().Single(o => o.GetProperty("token").GetString() == placed.Order.Token);
        item.GetProperty("pickup").GetProperty("text").GetString().Should().Be("К 1:30", "«Мои заказы»: то же, что на странице заказа");
        item.GetProperty("pickup").GetProperty("isPreorder").GetBoolean().Should().BeFalse();
        (await GetPublicOrderAsync(placed.Order.Token)).Pickup.Text.Should().Be("К 1:30");

        // контрольный: слот 01:30 СЛЕДУЮЩЕГО рабочего дня — предзаказ, «Завтра, к 1:30»
        var zone = TimeZoneInfo.FindSystemTimeZoneById(night.Zone);
        var nextDay = night.WorkingDay.AddDays(1);
        var nextSlot = (await GetSlotsAsync(shop.Slug, nextDay)).Slots.Single(s =>
        {
            var t = TimeZoneInfo.ConvertTimeFromUtc(s.StartUtc, zone);
            return DateOnly.FromDateTime(t) == nextDay.AddDays(1) && t.Hour == 1 && t.Minute == 30;
        });
        var tomorrow = await PlaceOrderAsync(shop.Slug, GuestAt([Line(night.Product, 1)], new PickupSelectionInput(PickupKind.Slot, nextDay, nextSlot.StartUtc)), buyer.Token);
        tomorrow.Order.Pickup.Text.Should().Be("Завтра, к 1:30");
        tomorrow.Order.Pickup.IsPreorder.Should().BeTrue();
        var mine2 = await J(await AuthedClient(buyer.Token).GetAsync("/api/orders/my"));
        mine2.EnumerateArray().Single(o => o.GetProperty("token").GetString() == tomorrow.Order.Token).GetProperty("pickup").GetProperty("text").GetString()
            .Should().Be("Завтра, к 1:30");

        // web-push покупателю без аккаунта
        await using var push = new PushEnabledFactory(ConnectionString);
        var guestOrder = await Authed(push, null).PostJsonAsync($"/api/storefront/{shop.Slug}/orders", GuestAt([Line(night.Product, 1)], night.Slot0130));
        var guest = (await guestOrder.Content.ReadJsonAsync<CreateOrderResponse>())!.Order;
        var subscribed = await Authed(push, null).PostJsonAsync($"/api/orders/public/{guest.Token}/push-subscription",
            new OrderPushSubscribeInput("https://push.example.test/cy25-guest/" + Guid.NewGuid(), new PushKeysInput("p256dh-key", "auth-key"), null));
        subscribed.StatusCode.Should().Be(HttpStatusCode.Created, await subscribed.Content.ReadAsStringAsync());
        var card = await GetStaffOrderAsync(shop, guest.Token);
        (await Authed(push, shop.OwnerToken).PostJsonAsync($"/api/shops/{shop.Id}/orders/{card.Id}/accept", new VersionInput(card.Version))).StatusCode.Should().Be(HttpStatusCode.OK);
        var pushRows = await WithDbAsync(db => db.CustomerOrderPushNotifications.AsNoTracking().Where(x => x.OrderId == card.Id).ToListAsync());
        pushRows.Should().NotBeEmpty("на принятие поставлено сообщение в браузер покупателя");
        pushRows.Should().OnlyContain(x => x.Payload.Contains("сегодня к 1:30") || x.Payload.Contains("к 1:30"));
        pushRows.Should().OnlyContain(x => !x.Payload.ToLower().Contains("завтра") && !DateInText.IsMatch(x.Payload));

        // сообщение покупателю в мессенджер
        await ConnectShopChannelAsync(shop);
        var settings = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/notification-settings", new ShopNotificationSettingsInput(true, true, true, null, null));
        settings.StatusCode.Should().Be(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        var withMessenger = await PlaceOrderAsync(shop.Slug, GuestAt([Line(night.Product, 1)], night.Slot0130, notifyByMessenger: true));
        var mCard = await GetStaffOrderAsync(shop, withMessenger.Order.Token);
        await ActOkAsync(shop, mCard, "accept");
        var body = (await WithDbAsync(db => db.OutboundNotifications.AsNoTracking().Where(n => n.OrderId == mCard.Id).ToListAsync())).Single(n => n.Type == NotificationType.OrderAccepted).Body;
        body.Should().Contain("1:30").And.NotContainEquivalentOf("завтра");
        DateInText.IsMatch(body).Should().BeFalse("дата в тексте — признак «не сегодня»");
    }

    // ── T-25-04: «сегодня» в отчётах в «хвосте» после полуночи ───────────────────────────

    [Fact, TestCase("CY25-62")]
    public async Task InTheAfterMidnightTail_TodayMeansTheWorkingDay_InHistorySummaryPickListAndBoard()
    {
        var night = await NightShopAsync(asap: true);
        var shop = night.Shop;
        var order = await PlaceStaffViewAsync(shop, [Line(night.Product, 1)]);
        order.Pickup.IsPreorder.Should().BeFalse("заказ «как можно скорее» в хвосте — не предзаказ");
        order.Pickup.Date.Should().Be(night.WorkingDay, "дата выдачи — рабочий день, а не календарная дата");

        var today = await HistoryOkAsync(shop, new { period = "Today" });
        today.Period.From.Should().Be(night.WorkingDay);
        today.Items.Should().ContainSingle(i => i.OrderId == order.Id);
        today.Items.Single().PickupDate.Should().Be(night.WorkingDay);
        (await HistoryOkAsync(shop, new { period = "Yesterday" })).TotalCount.Should().Be(0, "вчера относительно рабочего дня — позавчера по календарю");

        var summary = await SummaryOkAsync(shop);
        summary.Period.From.Should().Be(night.WorkingDay);
        summary.OrdersTotal.Should().Be(1);
        summary.InProgress.Should().Be(1);

        var pickList = await PickListOkAsync(shop);
        pickList.Date.Should().Be(night.WorkingDay);
        pickList.OrderCount.Should().Be(1, "«весь день» рабочего дня захватывает хвост после полуночи");
        pickList.IncludeNew.Should().BeTrue();

        var board = await GetBoardAsync(shop);
        board.NewOrders.Should().ContainSingle(c => c.Id == order.Id, "заказ рабочего дня виден на экране заказов в «Новых»");
        board.Preorders.Should().BeNullOrEmpty();
    }

    /// <summary>
    /// BUG-25-01 (найдено QA, не блокер цикла; поведение цикла 24, SPEC T-25-04 доску прямо не называет, но требует «сегодня везде от рабочего дня»):
    /// в «хвосте» после полуночи экран заказов (<c>GET order-board</c>) считает «сегодня» по КАЛЕНДАРЮ (<c>ShopClock.BusinessDate</c>), а не по рабочему дню.
    /// Следствия: <c>businessDate</c> доски ≠ дате витрины, а предзаказ на СЛЕДУЮЩИЙ рабочий день (дата выдачи = календарное «сегодня»)
    /// лежит в колонке «Принятые», хотя его карточка говорит «Завтра, к 1:30» и <c>isPreorder = true</c>. Тест включить, когда доска перейдёт
    /// на <c>pickupContext.WorkingDay</c> (ShopOrdersController.GetBoard).
    /// </summary>
    [Fact(Skip = "BUG-25-01: ShopOrdersController.GetBoard считает «сегодня» по календарю в «хвосте» ночного интервала"), TestCase("CY25-64")]
    public async Task InTheAfterMidnightTail_BoardBusinessDayAndPreorderColumn_FollowTheWorkingDay()
    {
        var night = await NightShopAsync();
        var shop = night.Shop;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(night.Zone);
        var nextDay = night.WorkingDay.AddDays(1);
        var nextSlot = (await GetSlotsAsync(shop.Slug, nextDay)).Slots.First();
        var placed = await PlaceOrderAsync(shop.Slug, GuestAt([Line(night.Product, 1)], new PickupSelectionInput(PickupKind.Slot, nextDay, nextSlot.StartUtc)));
        var card = await GetStaffOrderAsync(shop, placed.Order.Token);
        card.Pickup.IsPreorder.Should().BeTrue();
        await ActOkAsync(shop, card, "accept");

        var board = await GetBoardAsync(shop);
        board.BusinessDate.Should().Be(night.WorkingDay, "дата доски совпадает с датой витрины");
        board.Accepted.Should().BeNullOrEmpty("предзаказ следующего рабочего дня не лежит в основной колонке");
        board.Preorders.Should().ContainSingle().Which.Date.Should().Be(nextDay);
        _ = zone;
    }

    // ── T-25-03: повтор дня недели → 400 строкой ─────────────────────────────────────

    [Fact, TestCase("CY25-63")]
    public async Task RepeatedWeekday_OnProductAndCategory_Returns400WithRussianString()
    {
        var shop = await CreateRoundClockShopAsync();
        var category = await CreateCategoryAsync(shop, "Супы");
        var product = await CreateProductAsync(shop, "Борщ", 300m, categoryId: category.Id);
        var c = AuthedClient(shop.OwnerToken);

        async Task ExpectDuplicate(HttpResponseMessage r, string where)
        {
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, where);
            var text = await r.Content.ReadAsStringAsync();
            text.Should().Contain("День недели указан дважды", where);
            text.Should().NotStartWith("{", "400 контракта — голая строка");
        }

        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Monday };
        await ExpectDuplicate(await c.PostJsonAsync($"/api/shops/{shop.Id}/products",
            new ProductInput(category.Id, "Суп дня", null, ProductUnit.Piece, 100m, null, null, null, true, null, weekdays.ToList())), "POST products");
        await ExpectDuplicate(await c.PutJsonAsync($"/api/shops/{shop.Id}/products/{product.Id}",
            new ProductInput(category.Id, product.Name, product.Description, product.Unit, product.Price, product.PortionText, product.WeightStepGrams, null, true, null, weekdays.ToList())),
            "PUT products");
        await ExpectDuplicate(await c.PutJsonAsync($"/api/shops/{shop.Id}/categories/{category.Id}/weekdays", new { weekdays }), "PUT categories/weekdays");

        // без повтора — принимается, и ничего из отвергнутого не сохранилось
        await PutWeekdaysAsync(shop, product, DayOfWeek.Monday, DayOfWeek.Tuesday);
        (await GetProductAsync(shop, product.Id)).AvailableWeekdays.Should().BeEquivalentTo(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday }, "отвергнутые запросы ничего не сохранили");
        var catOk = await c.PutJsonAsync($"/api/shops/{shop.Id}/categories/{category.Id}/weekdays", new { weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Friday } });
        catOk.StatusCode.Should().Be(HttpStatusCode.OK, await catOk.Content.ReadAsStringAsync());
        (await c.GetAsync($"/api/shops/{shop.Id}/products")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
