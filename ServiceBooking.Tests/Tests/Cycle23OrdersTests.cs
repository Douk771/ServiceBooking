using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 23, «Вызов 2»: витрина, корзина, оформление, страница заказа, отмена (US-23-18…22), остатки (US-23-17),
/// параллельность и идемпотентность. Пишется по SPEC.md/API_CONTRACT_CYCLE23.md.
/// </summary>
public class Cycle23OrdersTests(TestDatabaseFixture fixture) : Cycle23TestBase(fixture)
{
    private static async Task<JsonElement> J(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string> Code(HttpResponseMessage r) => (await J(r)).GetProperty("code").GetString()!;

    // ── Витрина и корзина ────────────────────────────────────────────────────────

    [Fact, TestCase("CY23-30")]
    public async Task Storefront_UnknownSlug404_ContentHasNoStockNumbers()
    {
        (await AnonymousClient().GetAsync("/api/storefront/no-such-shop-xyz")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var shop = await CreateShopAsync(settings: Settings(trackStock: true));
        var p = await CreateProductAsync(shop);
        await SetStockAsync(shop, p.Id, 7);
        var raw = await (await AnonymousClient().GetAsync($"/api/storefront/{shop.Slug}")).Content.ReadAsStringAsync();
        raw.Should().NotContain("onHand").And.NotContain("reserved").And.NotContain("\"stock\"",
            "покупатель точного остатка не видит");
        var sf = JsonSerializer.Deserialize<StorefrontDto>(raw, JsonHelpers.Options)!;
        sf.IsAvailable.Should().BeTrue();
        sf.AcceptingOrders.Should().BeTrue();
        sf.CustomerMode.Should().Be(ShopCustomerMode.Anyone);
    }

    [Fact, TestCase("CY23-31")]
    public async Task Quote_EmptyOk_Duplicates400_Over50Lines400_Problems()
    {
        var shop = await CreateShopAsync();
        var piece = await CreateProductAsync(shop);
        var weight = await CreateProductAsync(shop, "Сыр", 540m, ProductUnit.Weight);
        var c = AnonymousClient();
        var url = $"/api/storefront/{shop.Slug}/quote";

        var empty = await c.PostJsonAsync(url, new QuoteInput([]));
        empty.StatusCode.Should().Be(HttpStatusCode.OK);
        var e = (await empty.Content.ReadJsonAsync<QuoteDto>())!;
        e.Lines.Should().BeEmpty();
        e.Total.Should().Be(0);

        var dup = await c.PostJsonAsync(url, new QuoteInput([new(piece.Id, 1), new(piece.Id, 2)]));
        dup.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await dup.Content.ReadAsStringAsync()).Should().Contain("Товар в корзине повторяется");

        var many = Enumerable.Range(0, 51).Select(_ => new CartLineInput(Guid.NewGuid(), 1)).ToList();
        var big = await c.PostJsonAsync(url, new QuoteInput(many));
        big.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await big.Content.ReadAsStringAsync()).Should().Contain("В корзине не больше 50 позиций");

        var q = (await (await c.PostJsonAsync(url, new QuoteInput([
            new(piece.Id, 2), new(weight.Id, 500), new(Guid.NewGuid(), 1)]))).Content.ReadJsonAsync<QuoteDto>())!;
        q.Lines.Should().HaveCount(3);
        q.HasProblems.Should().BeTrue();
        q.Lines.Last().Problem!.Reason.Should().Be(OrderProblemReason.NotFound);
        q.Lines[0].LineTotal.Should().Be(500m);
        q.Lines[1].IsApproximate.Should().BeTrue();
        q.Lines[1].LineTotal.Should().Be(270m, "500 г × 540 ₽/кг");
        q.Total.Should().Be(770m, "итог — по строкам без проблем");
        q.IsApproximate.Should().BeTrue();

        // граница количества
        var weight2 = await CreateProductAsync(shop, "Сыр 2", 540m, ProductUnit.Weight, weightStep: 100, minGrams: 200);
        var bad = (await (await c.PostJsonAsync(url, new QuoteInput([new(weight2.Id, 100), new(piece.Id, 100)]))).Content.ReadJsonAsync<QuoteDto>())!;
        bad.Lines[0].Problem!.Reason.Should().Be(OrderProblemReason.BelowMinimum);
        bad.Lines[1].Problem!.Reason.Should().Be(OrderProblemReason.InvalidQuantity, "штучных не больше 99");
        var notStep = (await (await c.PostJsonAsync(url, new QuoteInput([new(weight.Id, 250)]))).Content.ReadJsonAsync<QuoteDto>())!;
        notStep.Lines[0].Problem!.Reason.Should().Be(OrderProblemReason.InvalidQuantity, "не кратно шагу 100 г");
        var over = (await (await c.PostJsonAsync(url, new QuoteInput([new(weight.Id, 10_100)]))).Content.ReadJsonAsync<QuoteDto>())!;
        over.Lines[0].Problem.Should().NotBeNull("больше 10 кг на позицию");
    }

    // ── Оформление ───────────────────────────────────────────────────────────────

    [Fact, TestCase("CY23-32")]
    public async Task GuestOrder_Created_PublicPageMasksPhone_TokenUnguessable_NumbersIncrement()
    {
        var shop = await CreateShopAsync(); // без тарифа — Q1 не влияет; paidPlan по умолчанию true, ниже отдельный тест
        var p = await CreateProductAsync(shop, price: 250m);
        var phone = UniquePhone();

        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 2)], "Иван", phone, "без лука"));
        placed.OrderUrl.Should().Contain(placed.Order.Token).And.Contain("/o/");
        placed.OrderUrl.Should().StartWith("https://goods.ezbook.ru/");
        placed.Order.Token.Length.Should().BeGreaterThanOrEqualTo(22, "не меньше 128 бит случайности в base64url");
        placed.Order.IsGuest.Should().BeTrue();
        placed.Order.Status.Should().Be(OrderStatus.New);
        placed.Order.StatusText.Should().Be("Новый");
        placed.Order.Total.Should().Be(500m);
        placed.Order.Comment.Should().Be("без лука");
        placed.Order.Items.Single().UnitPrice.Should().Be(250m);
        placed.Order.Timeline.Select(t => t.Status).Should().Equal(OrderStatus.New, OrderStatus.Accepted, OrderStatus.Ready, OrderStatus.Issued);
        placed.Order.Timeline[0].Reached.Should().BeTrue();
        placed.Order.Timeline[1].Reached.Should().BeFalse();
        placed.Order.CanCancel.Should().BeTrue();

        var page = await GetPublicOrderAsync(placed.Order.Token);
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        page.CustomerPhoneMasked.Should().NotBeNull().And.NotContain(digits[1..], "полного номера по ссылке нет");
        page.CustomerPhoneMasked!.Should().NotBe(phone);
        page.Shop.Slug.Should().Be(shop.Slug);

        var second = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        second.Order.Number.Should().Be(placed.Order.Number + 1);
        second.Order.Token.Should().NotBe(placed.Order.Token);
    }

    [Fact, TestCase("CY23-33")]
    public async Task OrderCreation_DoesNotDependOnOwnerTariff()
    {
        var shop = await CreateShopAsync(paidPlan: false);
        var p = await CreateProductAsync(shop);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        placed.Order.Status.Should().Be(OrderStatus.New);
    }

    [Fact, TestCase("CY23-34")]
    public async Task OrderCreation_ModelValidation_ReturnsRussianStrings()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var items = new[] { Line(p, 1) };

        async Task Expect(CreateOrderInput input, HttpStatusCode code, string text)
        {
            var r = await PostOrderAsync(shop.Slug, input);
            r.StatusCode.Should().Be(code, text);
            (await r.Content.ReadAsStringAsync()).Should().Contain(text);
        }

        await Expect(Guest(items, name: "  "), HttpStatusCode.BadRequest, "Укажите имя");
        await Expect(Guest(items, name: new string('я', 101)), HttpStatusCode.BadRequest, "Укажите имя");
        await Expect(Guest(items, comment: new string('x', 501)), HttpStatusCode.BadRequest, "Комментарий — не длиннее 500 символов");
        await Expect(Guest(items) with { CustomerPhone = "" }, HttpStatusCode.BadRequest, "Укажите телефон");
        await Expect(Guest(items) with { CustomerPhone = "+1 202 555 0100" }, HttpStatusCode.BadRequest, "Введите номер телефона в формате");
        await Expect(Guest([Line(p, 1), Line(p, 2)]), HttpStatusCode.BadRequest, "Товар в корзине повторяется");
        await Expect(Guest([]), HttpStatusCode.Conflict, "Корзина пуста");

        // граница 500 символов — допустимо
        var edge = await PostOrderAsync(shop.Slug, Guest(items, comment: new string('x', 500)));
        edge.StatusCode.Should().Be(HttpStatusCode.Created);
        // 1 и 99 штук — допустимо, 100 — нет
        (await PostOrderAsync(shop.Slug, Guest([Line(p, 99)]))).StatusCode.Should().Be(HttpStatusCode.Created);
        var over = await PostOrderAsync(shop.Slug, Guest([Line(p, 100)]));
        over.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(over)).Should().Be("ItemsUnavailable");
        var zero = await PostOrderAsync(shop.Slug, Guest([Line(p, 0)]));
        zero.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY23-35")]
    public async Task PriceChanged_Returns409WithNewPrice_ConfirmedRetryWithSameKeyCreatesOneOrder()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop, price: 250m);
        var key = Guid.NewGuid();

        var upd = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/products/{p.Id}",
            new ProductInput(null, p.Name, null, ProductUnit.Piece, 270m, null, null, null, true, null));
        upd.StatusCode.Should().Be(HttpStatusCode.OK);

        var input = Guest([Line(p, 1)], key: key);
        var stale = await PostOrderAsync(shop.Slug, input);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(stale);
        body.GetProperty("code").GetString().Should().Be("PriceChanged");
        body.GetProperty("problems")[0].GetProperty("currentUnitPrice").GetDecimal().Should().Be(270m);

        var confirmed = input with { Items = [new OrderLineInput(p.Id, 1, 270m)] };
        var ok = await PostOrderAsync(shop.Slug, confirmed);
        ok.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = (await ok.Content.ReadJsonAsync<CreateOrderResponse>())!.Order;
        order.Items.Single().UnitPrice.Should().Be(270m);
        order.Total.Should().Be(270m);

        // Смена цены после заказа не меняет заказ
        await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/products/{p.Id}",
            new ProductInput(null, p.Name, null, ProductUnit.Piece, 999m, null, null, null, true, null));
        (await GetPublicOrderAsync(order.Token)).Total.Should().Be(270m);
    }

    [Fact, TestCase("CY23-36")]
    public async Task Idempotency_RepeatedKey_Returns200WithSameOrder_ParallelDoubleClickCreatesOne()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var input = Guest([Line(p, 1)]);

        var first = await PostOrderAsync(shop.Slug, input);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var a = (await first.Content.ReadJsonAsync<CreateOrderResponse>())!;
        var again = await PostOrderAsync(shop.Slug, input);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadJsonAsync<CreateOrderResponse>())!.Order.Token.Should().Be(a.Order.Token);

        // параллельный двойной клик
        var input2 = Guest([Line(p, 1)]);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostOrderAsync(shop.Slug, input2)));
        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.OK,
            "повторы одного ключа не дают ошибок");
        var tokens = new HashSet<string>();
        foreach (var r in results) tokens.Add((await r.Content.ReadJsonAsync<CreateOrderResponse>())!.Order.Token);
        tokens.Should().HaveCount(1, "двойное нажатие не создаёт два заказа");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Orders.CountAsync(o => o.CompanyId == shop.Id)).Should().Be(2);
    }

    [Fact, TestCase("CY23-37")]
    public async Task AutoAcceptance_OrderIsImmediatelyAccepted()
    {
        var shop = await CreateShopAsync(settings: Settings(accept: OrderAcceptanceMode.Auto));
        var p = await CreateProductAsync(shop);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        placed.Order.Status.Should().Be(OrderStatus.Accepted);
        placed.Order.Timeline[1].Reached.Should().BeTrue();
        var board = (await (await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/order-board")).Content.ReadJsonAsync<OrderBoardDto>())!;
        board.Accepted!.Should().ContainSingle(c => c.Number == placed.Order.Number);
        board.NewOrders.Should().BeEmpty();
    }

    [Fact, TestCase("CY23-38")]
    public async Task LoggedInBuyer_OrderUsesAccountPhone_ListedInMyOrders_GuestOrdersOnSamePhoneNot()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var buyer = await RegisterAsync();

        var placed = await PlaceOrderAsync(shop.Slug,
            Guest([Line(p, 1)], "Пётр", phone: UniquePhone()), token: buyer.Token);
        var staffView = await GetStaffOrderAsync(shop, placed.Order.Token);
        staffView.CustomerPhone.Should().Contain(new string(buyer.Phone.Where(char.IsDigit).ToArray())[1..],
            "телефон заказа — номер аккаунта, поле тела игнорируется");
        staffView.CustomerKind.Should().Be(OrderActorKind.Customer);
        placed.Order.IsGuest.Should().BeFalse();

        // гостевой заказ на тот же номер
        var guest = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)], "Гость", phone: buyer.Phone));

        var mine = (await (await AuthedClient(buyer.Token).GetAsync("/api/orders/my")).Content.ReadJsonAsync<List<MyOrderSummaryDto>>())!;
        mine.Should().ContainSingle(o => o.Token == placed.Order.Token);
        mine.Should().NotContain(o => o.Token == guest.Order.Token, "гостевые заказы на тот же номер не подтягиваются");
        mine.Single().IsActive.Should().BeTrue();

        (await AnonymousClient().GetAsync("/api/orders/my")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // вошедший открывает свой заказ по той же ссылке
        (await GetPublicOrderAsync(placed.Order.Token, buyer.Token)).Number.Should().Be(placed.Order.Number);
    }

    // ── Страница заказа и отмена ─────────────────────────────────────────────────

    [Fact, TestCase("CY23-39")]
    public async Task PublicOrder_UnknownOrMalformedToken_404_NoOracle()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));

        foreach (var bad in new[] { "nope", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", placed.Order.Token[..^2] + "zz" })
        {
            var r = await AnonymousClient().GetAsync($"/api/orders/public/{bad}");
            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await r.Content.ReadAsStringAsync()).Should().BeEmpty("не оракул: пустое тело");
            (await AnonymousClient().PostAsync($"/api/orders/public/{bad}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact, TestCase("CY23-40")]
    public async Task CustomerCancel_NewOrder_ReleasesReserve_AndSecondCancelIsRejected()
    {
        var shop = await CreateShopAsync(settings: Settings(trackStock: true));
        var p = await CreateProductAsync(shop);
        await SetStockAsync(shop, p.Id, 3);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 2)]));
        (await GetProductAsync(shop, p.Id)).Stock.Reserved.Should().Be(2);

        var cancel = await AnonymousClient().PostAsync($"/api/orders/public/{placed.Order.Token}/cancel", null);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await cancel.Content.ReadJsonAsync<PublicOrderDto>())!;
        dto.Status.Should().Be(OrderStatus.CancelledByCustomer);
        dto.StatusText.Should().Be("Отменён покупателем");
        dto.CanCancel.Should().BeFalse();

        var stock = (await GetProductAsync(shop, p.Id)).Stock;
        stock.Reserved.Should().Be(0);
        stock.OnHand.Should().Be(3);

        var again = await AnonymousClient().PostAsync($"/api/orders/public/{placed.Order.Token}/cancel", null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(again)).Should().Be("CancelNotAllowed");
    }

    [Fact, TestCase("CY23-41")]
    public async Task CustomerCancel_ReadyOrder_RejectedWithAlreadyReady()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        var token = placed.Order.Token;
        var order = await GetStaffOrderAsync(shop, token);
        order = await ActOkAsync(shop, order, "accept");
        order = await ActOkAsync(shop, order, "ready");

        var r = await AnonymousClient().PostAsync($"/api/orders/public/{token}/cancel", null);
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(r);
        body.GetProperty("code").GetString().Should().Be("AlreadyReady");
        body.GetProperty("message").GetString().Should().Contain("Заказ уже собран");
        (await GetPublicOrderAsync(token)).Status.Should().Be(OrderStatus.Ready);
    }

    [Fact, TestCase("CY23-42")]
    public async Task CancelSetting_AppliesToNewOrdersOnly()
    {
        var shop = await CreateShopAsync(); // отмена разрешена
        var p = await CreateProductAsync(shop);
        var before = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));

        await UpdateSettingsAsync(shop, Settings(cancel: false));
        var after = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));

        (await GetPublicOrderAsync(before.Order.Token)).CanCancel.Should().BeTrue("у созданного раньше заказа кнопка остаётся");
        after.Order.CanCancel.Should().BeFalse();

        var denied = await AnonymousClient().PostAsync($"/api/orders/public/{after.Order.Token}/cancel", null);
        denied.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(denied)).Should().Be("CancelNotAllowed");
        (await AnonymousClient().PostAsync($"/api/orders/public/{before.Order.Token}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Остатки ──────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY23-43")]
    public async Task Stock_TrackingOn_InsufficientStockRefusedWithAvailableQuantity_ReserveMath()
    {
        var shop = await CreateShopAsync(settings: Settings(trackStock: true));
        var p = await CreateProductAsync(shop);
        await SetStockAsync(shop, p.Id, 3);

        var tooMany = await PostOrderAsync(shop.Slug, Guest([Line(p, 4)]));
        tooMany.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(tooMany);
        body.GetProperty("code").GetString().Should().Be("ItemsUnavailable");
        var problem = body.GetProperty("problems")[0];
        problem.GetProperty("reason").GetString().Should().Be("InsufficientStock");
        problem.GetProperty("availableQuantity").GetInt32().Should().Be(3);
        problem.GetProperty("message").GetString().Should().Contain("Осталось только 3 шт");

        var ok = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 3)]));
        var stock = (await GetProductAsync(shop, p.Id)).Stock;
        (stock.OnHand, stock.Reserved, stock.Free).Should().Be((3, 3, 0));
        var full = (await GetProductAsync(shop, p.Id));
        full.AvailableToCustomers.Should().BeFalse("свободного остатка нет — «Закончилось»");

        (await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        _ = ok;
    }

    [Fact, TestCase("CY23-44")]
    public async Task Stock_EmptyStock_MeansNotTracked_TrackingOff_IgnoresStock()
    {
        var shop = await CreateShopAsync(settings: Settings(trackStock: true));
        var free = await CreateProductAsync(shop, "Без учёта");
        var counted = await CreateProductAsync(shop, "С учётом");
        await SetStockAsync(shop, counted.Id, 1);
        (await PostOrderAsync(shop.Slug, Guest([Line(free, 50)]))).StatusCode.Should().Be(HttpStatusCode.Created,
            "пустой остаток — товар без ограничения");

        // выключение учёта: перестаёт проверять и резервировать
        await UpdateSettingsAsync(shop, Settings(trackStock: false));
        (await PostOrderAsync(shop.Slug, Guest([Line(counted, 5)]))).StatusCode.Should().Be(HttpStatusCode.Created);
        var stock = (await GetProductAsync(shop, counted.Id)).Stock;
        stock.OnHand.Should().Be(1, "остаток не трогается");
        stock.Reserved.Should().Be(0);

        var neg = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/products/{counted.Id}/stock", new StockInput(-1));
        neg.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await neg.Content.ReadAsStringAsync()).Should().Contain("Остаток — целое число от 0");

        await SetStockAsync(shop, counted.Id, null);
        (await GetProductAsync(shop, counted.Id)).Stock.OnHand.Should().BeNull();
    }

    [Fact, TestCase("CY23-45")]
    public async Task Stock_ParallelOrdersForLastUnit_ExactlyOneWins_ReserveNeverExceedsStock()
    {
        var shop = await CreateShopAsync(settings: Settings(trackStock: true));
        var p = await CreateProductAsync(shop);
        await SetStockAsync(shop, p.Id, 1);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))));
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "последняя единица продаётся один раз");
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(11);

        var stock = (await GetProductAsync(shop, p.Id)).Stock;
        (stock.OnHand, stock.Reserved, stock.Free).Should().Be((1, 1, 0));
    }

    [Fact, TestCase("CY23-46")]
    public async Task Stock_ParallelOrdersOfWeightedProduct_NeverOversell()
    {
        var shop = await CreateShopAsync(settings: Settings(trackStock: true));
        var w = await CreateProductAsync(shop, "Сыр", 540m, ProductUnit.Weight);
        await SetStockAsync(shop, w.Id, 1000); // 1 кг
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostOrderAsync(shop.Slug, Guest([Line(w, 300)]))));
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(3, "3×300 г влезает в 1 кг, 4-й — нет");
        var stock = (await GetProductAsync(shop, w.Id)).Stock;
        stock.Reserved.Should().Be(900);
        stock.Free.Should().Be(100);
        // 100 г свободно = минимум (шаг) — ещё доступно
        (await PostOrderAsync(shop.Slug, Guest([Line(w, 100)]))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await GetProductAsync(shop, w.Id)).AvailableToCustomers.Should().BeFalse();
        var problemBody = await J(await PostOrderAsync(shop.Slug, Guest([Line(w, 100)])));
        problemBody.GetProperty("problems")[0].GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact, TestCase("CY23-47")]
    public async Task Stock_ReserveInvariant_AfterCreateEditCancelIssueRejectNotPickedUp_ReserveIsZero()
    {
        var shop = await CreateShopAsync(settings: Settings(trackStock: true));
        var p = await CreateProductAsync(shop);
        var q = await CreateProductAsync(shop, "Кола", 100m);
        await SetStockAsync(shop, p.Id, 20);
        await SetStockAsync(shop, q.Id, 20);

        async Task<StaffOrderDto> Make(int a, int b) => await PlaceAndLoadAsync(shop, [Line(p, a), Line(q, b)]);
        async Task<(int Reserved, int OnHand)> S(Guid id) { var s = (await GetProductAsync(shop, id)).Stock; return (s.Reserved, s.OnHand!.Value); }

        // 1) создание + правка вверх/вниз + отмена магазином
        var o1 = await Make(2, 1);
        (await S(p.Id)).Reserved.Should().Be(2);
        var edit = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/orders/{o1.Id}/items", new EditOrderInput(o1.Version,
            [new EditOrderLineInput(o1.Items.Single(i => i.ProductId == p.Id).Id, null, 5), new EditOrderLineInput(o1.Items.Single(i => i.ProductId == q.Id).Id, null, 1)], "Довезём"));
        edit.StatusCode.Should().Be(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        o1 = (await edit.Content.ReadJsonAsync<StaffOrderDto>())!;
        (await S(p.Id)).Reserved.Should().Be(5, "правка пересчитывает резерв");
        o1 = await ActOkAsync(shop, o1, "accept");
        o1 = await ActOkAsync(shop, o1, "cancel", reason: "нет мяса");
        (await S(p.Id)).Reserved.Should().Be(0);

        // 2) выдача списывает со склада
        var o2 = await Make(3, 0 + 1);
        o2 = await ActOkAsync(shop, o2, "accept");
        o2 = await ActOkAsync(shop, o2, "ready");
        o2 = await ActOkAsync(shop, o2, "issue");
        (await S(p.Id)).Should().Be((0, 17), "выдача: резерв снят, склад уменьшен на выданное");

        // 3) отклонение
        var o3 = await Make(4, 1);
        o3 = await ActOkAsync(shop, o3, "reject", reason: "закрыто");
        (await S(p.Id)).Should().Be((0, 17));

        // 4) «Не забран»
        var o4 = await Make(2, 2);
        o4 = await ActOkAsync(shop, o4, "accept");
        o4 = await ActOkAsync(shop, o4, "ready");
        (await S(p.Id)).Reserved.Should().Be(2);
        o4 = await ActOkAsync(shop, o4, "not-picked-up");
        (await S(p.Id)).Should().Be((0, 17), "«Не забран» возвращает резерв, склад не меняется");
        (await S(q.Id)).Should().Be((0, 19));
        o4.Status.Should().Be(OrderStatus.NotPickedUp);
    }
}
