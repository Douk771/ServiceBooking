using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 23, «Вызов 2»: экран заказов, переходы статусов, конкурентность, правка, выдача по факт. весу, журнал
/// (US-23-23…26). Пишется по SPEC.md/API_CONTRACT_CYCLE23.md.
/// </summary>
public class Cycle23StaffOrdersTests(TestDatabaseFixture fixture) : Cycle23TestBase(fixture)
{
    private static async Task<JsonElement> J(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private Task<HttpResponseMessage> Edit(ShopCtx shop, StaffOrderDto o, IEnumerable<EditOrderLineInput> items, string? comment = null, string? token = null) =>
        AuthedClient(token ?? shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/orders/{o.Id}/items",
            new EditOrderInput(o.Version, items.ToList(), comment));

    private async Task<OrderBoardDto> BoardAsync(ShopCtx shop, long? since = null, DateOnly? date = null, string? token = null)
    {
        var q = since is null ? "" : $"?sinceRevision={since}&businessDate={date:yyyy-MM-dd}";
        var r = await AuthedClient(token ?? shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/order-board{q}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadJsonAsync<OrderBoardDto>())!;
    }

    // ── Доска ────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY23-50")]
    public async Task Board_ColumnsOldestFirst_PollWithRevisionIsCheapAndDetectsChanges()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var first = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        var second = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 2)]));

        var b = await BoardAsync(shop);
        b.Changed.Should().BeTrue();
        b.NewOrders!.Select(c => c.Number).Should().Equal(new[] { first.Order.Number, second.Order.Number }, "от старых к новым");
        b.Accepted.Should().BeEmpty();
        b.Ready.Should().BeEmpty();
        b.CompletedToday.Should().BeEmpty();
        b.ServerTimeUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        var same = await BoardAsync(shop, b.Revision, b.BusinessDate);
        same.Changed.Should().BeFalse();
        same.NewOrders.Should().BeNull("пустой опрос дёшев: массивов нет");
        same.Accepted.Should().BeNull();

        var card = b.NewOrders![0];
        await ActOkAsync(shop, await GetStaffOrderAsync(shop, first.Order.Token), "accept");
        var after = await BoardAsync(shop, b.Revision, b.BusinessDate);
        after.Changed.Should().BeTrue("изменение статуса видно на следующем опросе");
        after.Accepted!.Single().Number.Should().Be(first.Order.Number);
        after.NewOrders!.Single().Number.Should().Be(second.Order.Number);
        card.AvailableActions.Should().BeEquivalentTo([OrderAction.Accept, OrderAction.Reject, OrderAction.Edit]);

        // покупатель отменил — доска тоже меняется
        await AnonymousClient().PostAsync($"/api/orders/public/{second.Order.Token}/cancel", null);
        var last = await BoardAsync(shop, after.Revision, after.BusinessDate);
        last.Changed.Should().BeTrue();
        last.CompletedToday!.Single().Status.Should().Be(OrderStatus.CancelledByCustomer);
    }

    [Fact, TestCase("CY23-51")]
    public async Task Board_OtherShopOrderIsInvisible_OtherShopOrderId404()
    {
        var a = await CreateShopAsync();
        var b = await CreateShopAsync();
        var pa = await CreateProductAsync(a);
        var pb = await CreateProductAsync(b);
        var oa = await PlaceOrderAsync(a.Slug, Guest([Line(pa, 1)]));
        await PlaceOrderAsync(b.Slug, Guest([Line(pb, 1)]));
        var cardA = await GetStaffOrderAsync(a, oa.Order.Token);

        (await BoardAsync(b)).NewOrders!.Should().HaveCount(1).And.NotContain(c => c.Id == cardA.Id);
        (await AuthedClient(b.OwnerToken).GetAsync($"/api/shops/{b.Id}/orders/{cardA.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ActionAsync(b, cardA.Id, "accept", cardA.Version)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // владелец чужого магазина не может дёргать действия по путям чужого магазина
        (await AuthedClient(b.OwnerToken).GetAsync($"/api/shops/{a.Id}/order-board")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AnonymousClient().GetAsync($"/api/shops/{a.Id}/order-board")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Переходы ─────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY23-52")]
    public async Task HappyPath_NewAcceptedReadyIssued_ByStaff_JournalRecordsEveryStep_CustomerSeesStatuses()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 2)]));
        var o = await GetStaffOrderAsync(shop, placed.Order.Token, staff.Token);
        o.AvailableActions.Should().BeEquivalentTo([OrderAction.Accept, OrderAction.Reject, OrderAction.Edit]);

        o = await ActOkAsync(shop, o, "accept", staff.Token);
        (await GetPublicOrderAsync(placed.Order.Token)).StatusText.Should().Be("Принят");
        o.AvailableActions.Should().BeEquivalentTo([OrderAction.MarkReady, OrderAction.Cancel, OrderAction.Edit]);
        o = await ActOkAsync(shop, o, "ready", staff.Token);
        var pubReady = await GetPublicOrderAsync(placed.Order.Token);
        pubReady.StatusText.Should().Be("Готов к выдаче");
        pubReady.Timeline.Count(t => t.Reached).Should().Be(3);
        o.AvailableActions.Should().BeEquivalentTo([OrderAction.Issue, OrderAction.NotPickedUp, OrderAction.Cancel, OrderAction.Edit]);
        o = await ActOkAsync(shop, o, "issue", staff.Token);
        o.Status.Should().Be(OrderStatus.Issued);
        o.AvailableActions.Should().BeEmpty();
        o.Total.Should().Be(500m);
        o.CompletedAtUtc.Should().NotBeNull();

        var pub = await GetPublicOrderAsync(placed.Order.Token);
        pub.Status.Should().Be(OrderStatus.Issued);
        pub.Timeline.Should().OnlyContain(t => t.Reached);
        pub.CanCancel.Should().BeFalse();

        o.Events.Select(e => e.Kind).Should().Equal(
            OrderEventKind.Created, OrderEventKind.Accepted, OrderEventKind.MarkedReady, OrderEventKind.Issued);
        o.Events.Skip(1).Should().OnlyContain(e => e.ActorKind == OrderActorKind.Staff && !string.IsNullOrEmpty(e.ActorName));
        o.Events.Should().OnlyContain(e => e.OccurredAtUtc > DateTime.UtcNow.AddMinutes(-5));

        var board = await BoardAsync(shop);
        board.CompletedToday!.Should().ContainSingle(c => c.Number == placed.Order.Number);
    }

    [Fact, TestCase("CY23-53")]
    public async Task InvalidTransitions_Return409InvalidTransition_WithCurrentOrder()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var o = await PlaceAndLoadAsync(shop, [Line(p, 1)]);

        foreach (var action in new[] { "ready", "issue", "not-picked-up" })
        {
            var r = await ActionAsync(shop, o.Id, action, o.Version);
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, action + " из «Новый»");
            var body = await J(r);
            body.GetProperty("code").GetString().Should().Be("InvalidTransition");
            body.GetProperty("order").GetProperty("status").GetString().Should().Be("New");
        }
        // cancel из New недоступен (только Reject)
        (await ActionAsync(shop, o.Id, "cancel", o.Version)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        o = await ActOkAsync(shop, o, "accept");
        (await ActionAsync(shop, o.Id, "accept", o.Version)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ActionAsync(shop, o.Id, "reject", o.Version)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        o = await ActOkAsync(shop, o, "cancel", reason: "нет продукта");
        // конечный статус — никаких действий и правок
        foreach (var action in new[] { "accept", "ready", "issue", "reject", "cancel", "not-picked-up" })
            (await ActionAsync(shop, o.Id, action, o.Version)).StatusCode.Should().Be(HttpStatusCode.Conflict, action);
        (await Edit(shop, o, [new EditOrderLineInput(o.Items[0].Id, null, 2)])).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY23-54")]
    public async Task TwoStaff_ActingOnSameVersion_SecondGetsVersionMismatchWithActualOrder()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        var o = await PlaceAndLoadAsync(shop, [Line(p, 1)]);

        var accept = await ActionAsync(shop, o.Id, "accept", o.Version, staff.Token);
        accept.StatusCode.Should().Be(HttpStatusCode.OK);
        var reject = await ActionAsync(shop, o.Id, "reject", o.Version, shop.OwnerToken, "передумал");
        reject.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(reject);
        body.GetProperty("code").GetString().Should().Be("VersionMismatch");
        body.GetProperty("order").GetProperty("status").GetString().Should().Be("Accepted", "видит актуальное состояние, действие не применено");
    }

    [Fact, TestCase("CY23-55")]
    public async Task ParallelActionsOnSameOrder_ExactlyOneApplied()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var p = await CreateProductAsync(shop);
        var o = await PlaceAndLoadAsync(shop, [Line(p, 1)]);

        var tasks = new[]
        {
            ActionAsync(shop, o.Id, "accept", o.Version, staff.Token),
            ActionAsync(shop, o.Id, "accept", o.Version, shop.OwnerToken),
            ActionAsync(shop, o.Id, "reject", o.Version, staff.Token, "нет"),
            ActionAsync(shop, o.Id, "accept", o.Version, staff.Token),
        };
        var res = await Task.WhenAll(tasks);
        res.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        res.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(3);
        var final = await GetStaffOrderAsync(shop, (await LatestTokenAsync(shop)));
        final.Events.Count(e => e.Kind != OrderEventKind.Created).Should().Be(1, "в журнале одно действие");
    }

    private async Task<string> LatestTokenAsync(ShopCtx shop)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
        return await db.Orders.Where(x => x.CompanyId == shop.Id).OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.PublicToken).FirstAsync();
    }

    [Fact, TestCase("CY23-56")]
    public async Task Reject_ReasonShownToCustomer_ReasonLengthLimited_StaffCanBeMissingReason()
    {
        var shop = await CreateShopAsync();
        var p = await CreateProductAsync(shop);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        var o = await GetStaffOrderAsync(shop, placed.Order.Token);

        var tooLong = await ActionAsync(shop, o.Id, "reject", o.Version, reason: new string('я', 301));
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).Should().Contain("Причина — не длиннее 300 символов");

        var rejected = await ActOkAsync(shop, o, "reject", reason: new string('я', 300));
        rejected.Status.Should().Be(OrderStatus.Rejected);
        var pub = await GetPublicOrderAsync(placed.Order.Token);
        pub.StatusText.Should().Be("Отклонён");
        pub.Reason.Should().Be(new string('я', 300));
        pub.Timeline.Count(t => t.Reached).Should().Be(1, "шкала до последнего достигнутого шага");

        // без причины
        var p2 = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        var o2 = await GetStaffOrderAsync(shop, p2.Order.Token);
        (await ActOkAsync(shop, o2, "reject")).Status.Should().Be(OrderStatus.Rejected);
    }

    [Fact, TestCase("CY23-57")]
    public async Task RemovedStaffCannotAct_StaffCannotTouchForeignShop_StatusesArePlainText()
    {
        var shop = await CreateShopAsync();
        var other = await CreateShopAsync();
        var staffOfOther = await AddShopStaffAsync(other);
        var p = await CreateProductAsync(shop);
        var o = await PlaceAndLoadAsync(shop, [Line(p, 1)]);

        (await ActionAsync(shop, o.Id, "accept", o.Version, staffOfOther.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AuthedClient(staffOfOther.Token).GetAsync($"/api/shops/{shop.Id}/order-board")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        o.StatusText.Should().Be("Новый", "статус передаётся текстом");
    }

    // ── Правка ───────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY23-58")]
    public async Task Edit_ChangeQuantity_Remove_Replace_RecalculatesTotal_ShowsShopChangesToCustomer_Journal()
    {
        var shop = await CreateShopAsync();
        var a = await CreateProductAsync(shop, "Шаурма", 250m);
        var b = await CreateProductAsync(shop, "Кола", 100m);
        var c = await CreateProductAsync(shop, "Чай", 80m);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(a, 2), Line(b, 1)]));
        var o = await GetStaffOrderAsync(shop, placed.Order.Token);
        var ia = o.Items.Single(i => i.ProductId == a.Id);
        var ib = o.Items.Single(i => i.ProductId == b.Id);

        // уменьшить шаурму до 1, кола -> чай (замена = убрать + добавить), комментарий покупателю
        var r = await Edit(shop, o, [new EditOrderLineInput(ia.Id, null, 1), new EditOrderLineInput(null, c.Id, 3)], "Колы нет, есть чай");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var edited = (await r.Content.ReadJsonAsync<StaffOrderDto>())!;
        edited.IsModified.Should().BeTrue();
        JsonSerializer.Serialize(edited.Items.Select(i => new { i.Name, i.QuantityOrdered, i.UnitPrice, i.LineTotal })).Should().NotBeNull();
        var reloaded = await GetStaffOrderAsync(shop, placed.Order.Token);
        var probePub = await GetPublicOrderAsync(placed.Order.Token);
        var probe = $"response.Total={edited.Total}; reloaded.Total={reloaded.Total}; public.Total={probePub.Total}; event.After={edited.Events.Last().TotalAfter}; lines={edited.Items.Sum(i => i.LineTotal)}";
        edited.Total.Should().Be(250m + 3 * 80m, probe);
        edited.Items.Should().HaveCount(2).And.NotContain(i => i.Id == ib.Id);
        edited.Version.Should().BeGreaterThan(o.Version);
        var ev = edited.Events.Last();
        ev.Kind.Should().Be(OrderEventKind.Edited);
        ev.Comment.Should().Be("Колы нет, есть чай");
        ev.TotalBefore.Should().Be(600m);
        ev.TotalAfter.Should().Be(490m);
        ev.Changes.Should().NotBeNullOrEmpty();

        var pub = await GetPublicOrderAsync(placed.Order.Token);
        pub.Total.Should().Be(490m);
        pub.ShopChanges.Should().HaveCount(1);
        pub.ShopChanges[0].Comment.Should().Be("Колы нет, есть чай");
        pub.ShopChanges[0].Changes.Should().NotBeEmpty();
        var rawPub = JsonSerializer.Serialize(pub, JsonHelpers.Options);
        rawPub.Should().NotContain("ActorName").And.NotContain("actorName", "без имён сотрудников");

        // замена по текущей цене каталога
        await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/products/{c.Id}",
            new ProductInput(null, "Чай", null, ProductUnit.Piece, 90m, null, null, null, true, null));
        var second = await Edit(shop, edited, [new EditOrderLineInput(edited.Items.First(i => i.ProductId == a.Id).Id, null, 1), new EditOrderLineInput(null, c.Id, 1)]);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var e2 = (await second.Content.ReadJsonAsync<StaffOrderDto>())!;
        e2.Total.Should().Be(250m + 90m, "старая позиция чая убрана, новая по текущей цене каталога");
    }

    [Fact, TestCase("CY23-59")]
    public async Task Edit_Validation_LastItemEmptyForeignItemUnavailableProductStockAndSteps()
    {
        var shop = await CreateShopAsync(settings: Settings(trackStock: true));
        var a = await CreateProductAsync(shop, "Шаурма", 250m);
        var w = await CreateProductAsync(shop, "Сыр", 540m, ProductUnit.Weight);
        await SetStockAsync(shop, a.Id, 3);
        var o = await PlaceAndLoadAsync(shop, [Line(a, 2), Line(w, 500)]);
        var ia = o.Items.Single(i => i.ProductId == a.Id);
        var iw = o.Items.Single(i => i.ProductId == w.Id);

        var empty = await Edit(shop, o, []);
        empty.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(empty)).GetProperty("code").GetString().Should().Be("LastItemCannotBeRemoved");

        var foreign = await Edit(shop, o, [new EditOrderLineInput(Guid.NewGuid(), null, 1)]);
        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await foreign.Content.ReadAsStringAsync()).Should().Contain("Состав заказа указан с ошибкой");
        (await Edit(shop, o, [new EditOrderLineInput(ia.Id, Guid.NewGuid(), 1)])).StatusCode.Should().Be(HttpStatusCode.BadRequest, "и itemId и productId");
        (await Edit(shop, o, [new EditOrderLineInput(null, null, 1)])).StatusCode.Should().Be(HttpStatusCode.BadRequest, "ни того ни другого");

        var ghost = await Edit(shop, o, [new EditOrderLineInput(ia.Id, null, 1), new EditOrderLineInput(null, Guid.NewGuid(), 1)]);
        ghost.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(ghost)).GetProperty("code").GetString().Should().Be("ProductUnavailable");

        var badStep = await Edit(shop, o, [new EditOrderLineInput(iw.Id, null, 250)]);
        badStep.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(badStep)).GetProperty("code").GetString().Should().Be("InvalidQuantity");

        // рост сверх свободного остатка (свободно 1, было 2 — итого 3 макс)
        var over = await Edit(shop, o, [new EditOrderLineInput(ia.Id, null, 4)]);
        over.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var overBody = await J(over);
        overBody.GetProperty("code").GetString().Should().Be("InsufficientStock");
        overBody.GetProperty("problems")[0].GetProperty("availableQuantity").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        (await GetProductAsync(shop, a.Id)).Stock.Reserved.Should().Be(2, "неудачная правка резерв не трогает");

        var ok = await Edit(shop, o, [new EditOrderLineInput(ia.Id, null, 3)]);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetProductAsync(shop, a.Id)).Stock.Reserved.Should().Be(3);

        // устаревшая версия
        var stale = await Edit(shop, o, [new EditOrderLineInput(ia.Id, null, 1)]);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(stale)).GetProperty("code").GetString().Should().Be("VersionMismatch");
    }

    [Fact, TestCase("CY23-60")]
    public async Task Edit_AllowedInReady_NotInTerminal_StaffMayEditOwnerNotRequired()
    {
        var shop = await CreateShopAsync();
        var staff = await AddShopStaffAsync(shop);
        var a = await CreateProductAsync(shop, "Шаурма", 250m);
        var o = await PlaceAndLoadAsync(shop, [Line(a, 2)]);
        o = await ActOkAsync(shop, o, "accept");
        o = await ActOkAsync(shop, o, "ready");
        var r = await Edit(shop, o, [new EditOrderLineInput(o.Items[0].Id, null, 1)], token: staff.Token);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var e = (await r.Content.ReadJsonAsync<StaffOrderDto>())!;
        e.Status.Should().Be(OrderStatus.Ready);
        e.Total.Should().Be(250m);
        e = await ActOkAsync(shop, e, "issue");
        (await Edit(shop, e, [new EditOrderLineInput(e.Items[0].Id, null, 2)])).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY23-65")]
    public async Task Edit_AddNewItem_TotalEqualsSumOfLines_PersistedAndVisibleToCustomer()
    {
        // Отдельный узкий сценарий: только добавление позиции (без удаления и замены), чтобы локализовать расхождение итога.
        var shop = await CreateShopAsync();
        var a = await CreateProductAsync(shop, "Шаурма", 250m);
        var b = await CreateProductAsync(shop, "Кола", 100m);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(a, 1)]));
        var o = await GetStaffOrderAsync(shop, placed.Order.Token);

        var r = await Edit(shop, o, [new EditOrderLineInput(o.Items[0].Id, null, 1), new EditOrderLineInput(null, b.Id, 2)]);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var edited = (await r.Content.ReadJsonAsync<StaffOrderDto>())!;
        edited.Items.Should().HaveCount(2);
        edited.Total.Should().Be(edited.Items.Sum(i => i.LineTotal), "итог заказа равен сумме строк");
        edited.Total.Should().Be(450m);
        (await GetPublicOrderAsync(placed.Order.Token)).Total.Should().Be(450m);
        (await GetStaffOrderAsync(shop, placed.Order.Token)).Total.Should().Be(450m);
    }

    // ── Выдача по факт. весу ─────────────────────────────────────────────────────

    [Fact, TestCase("CY23-61")]
    public async Task Issue_WeightItems_RequireActualWeights_QuoteThenIssueGiveExactTotal()
    {
        var shop = await CreateShopAsync();
        var w = await CreateProductAsync(shop, "Сыр", 540m, ProductUnit.Weight);
        var a = await CreateProductAsync(shop, "Шаурма", 250m);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(w, 500), Line(a, 1)]));
        placed.Order.TotalIsApproximate.Should().BeTrue();
        placed.Order.Total.Should().Be(520m);
        var o = await GetStaffOrderAsync(shop, placed.Order.Token);
        o.HasWeightItems.Should().BeTrue();
        o.TotalIsApproximate.Should().BeTrue();
        o = await ActOkAsync(shop, o, "accept");
        o = await ActOkAsync(shop, o, "ready");
        var iw = o.Items.Single(i => i.ProductId == w.Id);
        var ia = o.Items.Single(i => i.ProductId == a.Id);
        var c = AuthedClient(shop.OwnerToken);

        // без весов
        var none = await c.PostJsonAsync($"/api/shops/{shop.Id}/orders/{o.Id}/issue", new IssueInput(o.Version, []));
        none.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await none.Content.ReadAsStringAsync()).Should().Contain("Укажите фактический вес каждой весовой позиции");
        // запись для штучной
        var piece = await c.PostJsonAsync($"/api/shops/{shop.Id}/orders/{o.Id}/issue",
            new IssueInput(o.Version, [new ActualQuantityInput(iw.Id, 512), new ActualQuantityInput(ia.Id, 1)]));
        piece.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // лишняя (чужой itemId)
        (await c.PostJsonAsync($"/api/shops/{shop.Id}/orders/{o.Id}/issue",
            new IssueInput(o.Version, [new ActualQuantityInput(iw.Id, 512), new ActualQuantityInput(Guid.NewGuid(), 1)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetStaffOrderAsync(shop, placed.Order.Token)).Status.Should().Be(OrderStatus.Ready, "неудачная выдача заказ не меняет");

        var quote = await c.PostJsonAsync($"/api/shops/{shop.Id}/orders/{o.Id}/issue-quote", new IssueQuoteInput([new ActualQuantityInput(iw.Id, 512)]));
        quote.StatusCode.Should().Be(HttpStatusCode.OK, await quote.Content.ReadAsStringAsync());
        var q = (await quote.Content.ReadJsonAsync<IssueQuoteDto>())!;
        q.FinalTotal.Should().Be(276.48m + 250m, "512 г × 540 ₽/кг = 276,48");
        (await GetStaffOrderAsync(shop, placed.Order.Token)).Status.Should().Be(OrderStatus.Ready, "issue-quote ничего не меняет");

        var issued = await c.PostJsonAsync($"/api/shops/{shop.Id}/orders/{o.Id}/issue", new IssueInput(o.Version, [new ActualQuantityInput(iw.Id, 512)]));
        issued.StatusCode.Should().Be(HttpStatusCode.OK, await issued.Content.ReadAsStringAsync());
        var done = (await issued.Content.ReadJsonAsync<StaffOrderDto>())!;
        done.Total.Should().Be(526.48m);
        done.TotalIsApproximate.Should().BeFalse();
        done.Items.Single(i => i.ProductId == w.Id).QuantityActual.Should().Be(512);
        done.Items.Single(i => i.ProductId == w.Id).LineTotal.Should().Be(276.48m);

        var pub = await GetPublicOrderAsync(placed.Order.Token);
        pub.Total.Should().Be(526.48m);
        pub.TotalIsApproximate.Should().BeFalse("после выдачи точная сумма видна покупателю");
    }

    [Fact, TestCase("CY23-62")]
    public async Task Issue_ActualWeightMoreThanStock_WritesOffToZero_NoNegativeStock()
    {
        var shop = await CreateShopAsync(settings: Settings(trackStock: true));
        var w = await CreateProductAsync(shop, "Сыр", 540m, ProductUnit.Weight);
        await SetStockAsync(shop, w.Id, 500);
        var o = await PlaceAndLoadAsync(shop, [Line(w, 500)]);
        o = await ActOkAsync(shop, o, "accept");
        o = await ActOkAsync(shop, o, "ready");
        var r = await AuthedClient(shop.OwnerToken).PostJsonAsync($"/api/shops/{shop.Id}/orders/{o.Id}/issue",
            new IssueInput(o.Version, [new ActualQuantityInput(o.Items[0].Id, 620)]));
        r.StatusCode.Should().Be(HttpStatusCode.OK, "нехватка остатка при выдаче — не ошибка");
        var done = (await r.Content.ReadJsonAsync<StaffOrderDto>())!;
        done.Total.Should().Be(334.80m);
        var stock = (await GetProductAsync(shop, w.Id)).Stock;
        stock.OnHand.Should().Be(0);
        stock.Reserved.Should().Be(0);
        done.Events.Should().Contain(e => e.Kind == OrderEventKind.Issued);
    }

    [Fact, TestCase("CY23-63")]
    public async Task NotPickedUp_OnlyFromReady_ReturnsReserve_CustomerSeesStatus()
    {
        var shop = await CreateShopAsync(settings: Settings(trackStock: true));
        var a = await CreateProductAsync(shop);
        await SetStockAsync(shop, a.Id, 5);
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(a, 2)]));
        var o = await GetStaffOrderAsync(shop, placed.Order.Token);
        (await ActionAsync(shop, o.Id, "not-picked-up", o.Version)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        o = await ActOkAsync(shop, o, "accept");
        (await ActionAsync(shop, o.Id, "not-picked-up", o.Version)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        o = await ActOkAsync(shop, o, "ready");
        o = await ActOkAsync(shop, o, "not-picked-up");
        (await GetPublicOrderAsync(placed.Order.Token)).StatusText.Should().Be("Не забран");
        (await GetProductAsync(shop, a.Id)).Stock.Reserved.Should().Be(0);
    }

    [Fact, TestCase("CY23-64")]
    public async Task StaffCard_ShowsFullPhoneAndCommentToStaff_PublicPageDoesNot()
    {
        var shop = await CreateShopAsync();
        var a = await CreateProductAsync(shop);
        var phone = UniquePhone();
        var placed = await PlaceOrderAsync(shop.Slug, Guest([Line(a, 1)], "Иван", phone, "заберу к 13:00"));
        var o = await GetStaffOrderAsync(shop, placed.Order.Token);
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        new string(o.CustomerPhone!.Where(char.IsDigit).ToArray()).Should().Be(digits, "у персонала телефон целиком — для набора");
        o.Comment.Should().Be("заберу к 13:00");
        o.CustomerName.Should().Be("Иван");
        o.CustomerKind.Should().Be(OrderActorKind.Guest);
        var raw = JsonSerializer.Serialize(placed.Order, JsonHelpers.Options);
        raw.Should().NotContain(digits[1..]);
    }
}
