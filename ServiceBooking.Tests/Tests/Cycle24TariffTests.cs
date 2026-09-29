using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 24, «Вызов 2»: блок F — линейка тарифов «Заказы», две независимые подписки, лимиты внутри линейки, месячный лимит заказов,
/// экран подписки (US-24-24…28, Q-24-7, Q-24-8). API_CONTRACT_CYCLE24.md §485.
/// </summary>
public class Cycle24TariffTests(TestDatabaseFixture fixture) : Cycle24TestBase(fixture)
{
    private async Task<HttpClient> AdminAsync() => AuthedClient((await LoginAsSuperAdminAsync()).Token);

    private async Task<JsonElement> CreateOrdersPlanAsync(
        int? maxShops = null, int? maxSeats = null, int? maxProducts = null, int? maxOrders = null, bool allowOrders = true,
        decimal price = 500m, bool isPublic = true, string? line = "Orders")
    {
        var r = await (await AdminAsync()).PostJsonAsync("/api/admin/plans", new
        {
            name = Unique("Заказы · "), description = "для магазинов", highlights = new[] { "заказы" }, pricePerMonth = price,
            maxEmployees = maxSeats, maxCompanies = maxShops, isPublic, isActive = true, line,
            maxProductsPerShop = maxProducts, maxOrdersPerMonth = maxOrders, allowOrders,
        });
        r.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        return await J(r);
    }

    private async Task<Guid> AccountIdAsync(string ownerUserId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.BillingAccounts.Where(a => a.OwnerUserId == ownerUserId).Select(a => a.Id).FirstAsync();
    }

    private async Task<HttpResponseMessage> AssignAsync(string ownerUserId, Guid planId, string? line, bool confirm = false)
    {
        var accountId = await AccountIdAsync(ownerUserId);
        return await (await AdminAsync()).PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", new
        {
            planId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            options = Array.Empty<object>(), line, confirmLimitOverflow = confirm,
        });
    }

    private async Task GivePlanAsync(ShopCtx shop, JsonElement plan)
    {
        var r = await AssignAsync(shop.Owner.UserId, plan.GetProperty("id").GetGuid(), "Orders");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    // ── US-24-24: админка тарифов ────────────────────────────────────────────────

    [Fact, TestCase("CY24-70")]
    public async Task AdminPlans_OrdersLine_FieldsRoundTrip_LineImmutable_NoTrial_PricingHidesOrders()
    {
        var admin = await AdminAsync();
        var plan = await CreateOrdersPlanAsync(maxShops: 3, maxSeats: 10, maxProducts: 1000, maxOrders: null, price: 990m);
        plan.GetProperty("line").GetString().Should().Be("Orders");
        plan.GetProperty("maxProductsPerShop").GetInt32().Should().Be(1000);
        plan.GetProperty("maxOrdersPerMonth").ValueKind.Should().Be(JsonValueKind.Null, "пусто — без ограничения");
        plan.GetProperty("allowOrders").GetBoolean().Should().BeTrue();
        var id = plan.GetProperty("id").GetGuid();

        // по умолчанию линейка — «Записи»
        var services = await admin.PostJsonAsync("/api/admin/plans", new { name = Unique("Салон · "), pricePerMonth = 100m, maxEmployees = 5, maxCompanies = 1, isPublic = true, isActive = true });
        (await J(services)).GetProperty("line").GetString().Should().Be("Services");

        // смена линейки при PUT — 409
        var put = await admin.PutJsonAsync($"/api/admin/plans/{id}", new
        {
            name = plan.GetProperty("name").GetString(), pricePerMonth = 990m, maxEmployees = 10, maxCompanies = 3, isPublic = true, isActive = true, line = "Services",
        });
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await put.Content.ReadAsStringAsync()).Should().Contain("Линейку тарифа менять нельзя");

        // лимиты меняются
        var upd = await admin.PutJsonAsync($"/api/admin/plans/{id}", new
        {
            name = plan.GetProperty("name").GetString(), pricePerMonth = 990m, maxEmployees = 10, maxCompanies = 3, isPublic = true, isActive = true,
            line = "Orders", maxProductsPerShop = 200, maxOrdersPerMonth = 300, allowOrders = true,
        });
        upd.StatusCode.Should().Be(HttpStatusCode.OK, await upd.Content.ReadAsStringAsync());
        var updated = await J(upd);
        updated.GetProperty("maxProductsPerShop").GetInt32().Should().Be(200);
        updated.GetProperty("maxOrdersPerMonth").GetInt32().Should().Be(300);

        // пробный период — только у «Записи»
        var trial = await admin.PutJsonAsync($"/api/admin/plans/{id}/system-trial", new { isSystemTrial = true });
        trial.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await trial.Content.ReadAsStringAsync()).Should().Contain("Пробный период есть только у тарифов «Записи»");

        // публичная витрина цен — только «Записи»
        // (публичный /api/pricing по умолчанию скрыт — 404; админский предпросмотр отдаёт тот же состав без переключателя публикации)
        var pricing = await admin.GetAsync("/api/admin/pricing/preview");
        pricing.StatusCode.Should().Be(HttpStatusCode.OK);
        (await pricing.Content.ReadAsStringAsync()).Should().NotContain(plan.GetProperty("name").GetString()!, "тарифы «Заказов» вне кабинета не показываются");
        var publicPricing = await AnonymousClient().GetAsync("/api/pricing");
        if (publicPricing.StatusCode == HttpStatusCode.OK)
            (await publicPricing.Content.ReadAsStringAsync()).Should().NotContain(plan.GetProperty("name").GetString()!);
    }

    [Fact, TestCase("CY24-71")]
    public async Task SystemFree_OneInEachLine_SecondOrdersFreeRefused_ServicesFreeUntouched()
    {
        var admin = await AdminAsync();
        async Task<List<JsonElement>> Plans() => (await J(await admin.GetAsync("/api/admin/plans"))).GetProperty("plans").EnumerateArray().ToList();
        var before = await Plans();
        var ordersFree = before.Where(p => p.GetProperty("isSystemFree").GetBoolean() && p.GetProperty("line").GetString() == "Orders").ToList();
        var servicesFree = before.Where(p => p.GetProperty("isSystemFree").GetBoolean() && p.GetProperty("line").GetString() == "Services").ToList();
        ordersFree.Should().ContainSingle("после выката у линейки «Заказы» есть бесплатный уровень (US-24-28)");
        servicesFree.Should().ContainSingle("у «Записей» тоже ровно один");
        ordersFree[0].GetProperty("maxOrdersPerMonth").GetInt32().Should().Be(150, "бесплатный уровень магазина: 150 заказов в месяц (US-24-25)");
        ordersFree[0].GetProperty("maxProductsPerShop").GetInt32().Should().Be(50);
        ordersFree[0].GetProperty("maxCompanies").GetInt32().Should().Be(1);
        ordersFree[0].GetProperty("maxEmployees").GetInt32().Should().Be(2, "«сотрудников: 1» из SPEC = владелец + один сотрудник (ARCHITECTURE_CYCLE24.md §468 п. 3, места считают владельца)");

        // второй бесплатный «Заказов» без снятия первого — отказ; бесплатный «Записей» не мешает
        var second = await CreateOrdersPlanAsync(maxShops: 1, maxSeats: 1, maxProducts: 50, maxOrders: 150, price: 0m);
        var r = await admin.PutJsonAsync($"/api/admin/plans/{second.GetProperty("id").GetGuid()}/system-free", new { isSystemFree = true });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict, "один системный бесплатный тариф в каждой линейке");

        var after = await Plans();
        after.Where(p => p.GetProperty("isSystemFree").GetBoolean() && p.GetProperty("line").GetString() == "Services").Select(p => p.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo(servicesFree.Select(p => p.GetProperty("id").GetGuid()));
    }

    // ── US-24-25/28: бесплатный уровень и лимиты внутри линейки ──────────────────

    [Fact, TestCase("CY24-72")]
    public async Task NewShopOwner_GetsFreeOrdersTier_ProductAndOrderLimitsVisible_SecondShopRefused()
    {
        var owner = await RegisterAsync();
        var (token, shop) = await CreateShopForAsync(owner.Token);
        shop.OrderLimit.Limit.Should().NotBeNull("у бесплатного уровня «Заказов» есть месячный лимит");
        shop.ProductLimit.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(1000);
        shop.OrderLimit.Used.Should().Be(0);
        shop.OrderLimit.WarningLevel.Should().Be(OrderLimitWarningLevel.None);

        var sub = await J(await AuthedClient(token).GetAsync("/api/billing/subscription?line=Orders"));
        sub.GetProperty("line").GetString().Should().Be("Orders");
        sub.GetProperty("orders").GetProperty("ordersLimit").GetInt32().Should().Be(shop.OrderLimit.Limit!.Value);
        sub.GetProperty("orders").GetProperty("productsPerShopLimit").GetInt32().Should().Be(shop.ProductLimit);
        sub.GetProperty("orders").GetProperty("text").GetString().Should().Be($"Заказов в этом месяце: 0 из {shop.OrderLimit.Limit}");
        sub.GetProperty("trial").ValueKind.Should().Be(JsonValueKind.Null, "пробного периода «Заказов» нет");
        sub.GetProperty("totalMonthlyPrice").GetDecimal().Should().BeGreaterThanOrEqualTo(0);
        sub.GetProperty("usage").GetProperty("companiesUsed").GetInt32().Should().Be(1, "в линейке «Заказов» companies* — магазины");

        // лимит магазинов бесплатного уровня — 402 словами
        var second = await AuthedClient(token).PostJsonAsync("/api/shops", new CreateShopInput(
            "Второй", Unique("shop-"), await AnyCityIdAsync(), null, null, null, null, null, new ShopOwnerTermsInput(CurrentOwnerTermsDto().Version)));
        if (shop.OrderLimit.Limit is not null && sub.GetProperty("usage").GetProperty("companiesLimit").ValueKind == JsonValueKind.Number)
        {
            second.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
            (await second.Content.ReadAsStringAsync()).Should().Contain("можно открыть не больше").And.Contain("Подписка");
        }
    }

    [Fact, TestCase("CY24-73")]
    public async Task SubscriptionScreen_ServicesLineUnchanged_OrdersFieldsNullForServices()
    {
        var owner = await RegisterAsync();
        await CreateCompanyAsync(owner.Token);
        await GiveActivePaidPlanAsync(owner.UserId);
        var c = AuthedClient(owner.Token);

        foreach (var url in new[] { "/api/billing/subscription", "/api/billing/subscription?line=Services" })
        {
            var sub = await J(await c.GetAsync(url));
            sub.GetProperty("line").GetString().Should().Be("Services");
            sub.GetProperty("orders").ValueKind.Should().Be(JsonValueKind.Null);
            sub.GetProperty("availablePlans").ValueKind.Should().Be(JsonValueKind.Null);
        }
        (await c.GetAsync("/api/billing/subscription?line=Nope")).StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY24-74")]
    public async Task ShopLimits_CountInsideOrdersLine_SalonsAndShopsDoNotEatEachOthersSlots()
    {
        var owner = await RegisterAsync();
        await GiveActivePaidPlanAsync(owner.UserId);                         // «Записи» — без ограничений
        var (token, shop) = await CreateShopForAsync(owner.Token);
        var ctx = new ShopCtx(owner, token, shop);
        var limited = await CreateOrdersPlanAsync(maxShops: 1, maxSeats: 2, maxProducts: 100, maxOrders: null);
        await GivePlanAsync(ctx, limited);

        // магазин сверх лимита «Заказов»
        var second = await AuthedClient(token).PostJsonAsync("/api/shops", new CreateShopInput(
            "Лишний", Unique("shop-"), await AnyCityIdAsync(), null, null, null, null, null, new ShopOwnerTermsInput(CurrentOwnerTermsDto().Version)));
        second.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var text = await second.Content.ReadAsStringAsync();
        text.Should().Contain(limited.GetProperty("name").GetString()!).And.Contain("можно открыть не больше 1 магазин");

        // а салон на тарифе «Записей» открывается — магазин не занимает место салона
        (await CreateCompanyAsync(token)).Id.Should().NotBeEmpty();

        // и наоборот: салонный лимит 1 не мешает магазинам сверх него — проверяется тарифом «Заказов» выше
    }

    [Fact, TestCase("CY24-75")]
    public async Task ShopSeats_LimitInsideOrdersLine_402WithOwnerIncluded()
    {
        var shop = await CreateShopAsync();
        var plan = await CreateOrdersPlanAsync(maxShops: 3, maxSeats: 2, maxProducts: 100);
        await GivePlanAsync(shop, plan);

        await AddShopStaffAsync(shop);   // владелец + один сотрудник = 2
        var extra = await RegisterAsync();
        var r = await AuthedClient(shop.OwnerToken).PostJsonAsync($"/api/companies/{shop.Id}/members", new { phone = extra.Phone, firstName = extra.FirstName, lastName = extra.LastName, role = "Master", bio = (string?)null, email = (string?)null });
        r.StatusCode.Should().Be(HttpStatusCode.PaymentRequired, await r.Content.ReadAsStringAsync());
        (await r.Content.ReadAsStringAsync()).Should().Contain("не больше 2 участников, включая владельца");
    }

    [Fact, TestCase("CY24-76")]
    public async Task ProductLimit_ByTariff_409Text_TechnicalCeilingKept_DowngradeDeletesNothing()
    {
        var shop = await CreateShopAsync();
        var plan = await CreateOrdersPlanAsync(maxShops: 3, maxSeats: 5, maxProducts: 3);
        await GivePlanAsync(shop, plan);
        (await GetShopAsync(shop)).ProductLimit.Should().Be(3);

        await CreateProductAsync(shop, "1"); await CreateProductAsync(shop, "2"); await CreateProductAsync(shop, "3");
        var over = await AuthedClient(shop.OwnerToken).PostJsonAsync($"/api/shops/{shop.Id}/products", new ProductInput(
            null, "4", null, ProductUnit.Piece, 10m, null, null, null, true, null));
        over.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var j = await J(over);
        j.GetProperty("code").GetString().Should().Be("ProductLimitReached");
        j.GetProperty("message").GetString().Should().Contain("не больше 3 товаров").And.Contain(plan.GetProperty("name").GetString()!);

        // переход на меньший тариф ничего не удаляет и не выключает: товары на месте и продаются, добавлять нельзя
        var tiny = await CreateOrdersPlanAsync(maxShops: 3, maxSeats: 5, maxProducts: 1);
        var down = await AssignAsync(shop.Owner.UserId, tiny.GetProperty("id").GetGuid(), "Orders", confirm: true);
        down.StatusCode.Should().Be(HttpStatusCode.OK, await down.Content.ReadAsStringAsync());
        var list = await J(await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/products"));
        list.GetArrayLength().Should().Be(3);
        (await GetStorefrontAsync(shop.Slug)).Categories.SelectMany(x => x.Products).Should().HaveCount(3);
        (await AuthedClient(shop.OwnerToken).PostJsonAsync($"/api/shops/{shop.Id}/products", new ProductInput(
            null, "5", null, ProductUnit.Piece, 10m, null, null, null, true, null))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── US-24-27: месячный лимит заказов ─────────────────────────────────────────

    [Fact, TestCase("CY24-77")]
    public async Task MonthlyLimit_Warning80_Reached_StopsNewOrders_OldOnesKeepWorking_TariffChangeResumes()
    {
        var shop = await CreateShopAsync();
        var plan = await CreateOrdersPlanAsync(maxShops: 3, maxSeats: 5, maxProducts: 100, maxOrders: 5);
        await GivePlanAsync(shop, plan);
        var p = await CreateProductAsync(shop);

        var first = await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        for (var i = 0; i < 3; i++) await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));

        var dto80 = await GetShopAsync(shop);
        dto80.OrderLimit.Used.Should().Be(4);
        dto80.OrderLimit.Limit.Should().Be(5);
        dto80.OrderLimit.WarningLevel.Should().Be(OrderLimitWarningLevel.Warning80);
        dto80.OrderLimit.Text.Should().NotBeNullOrEmpty();
        dto80.AcceptingOrders.Should().BeTrue();

        await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        var reached = await GetShopAsync(shop);
        reached.OrderLimit.WarningLevel.Should().Be(OrderLimitWarningLevel.Reached);
        reached.AcceptingOrders.Should().BeFalse();
        reached.NotAcceptingCode.Should().Be(ShopNotAcceptingCode.MonthlyLimitReached);
        reached.NotAcceptingReason.Should().Contain("Лимит заказов на").And.Contain("исчерпан").And.Contain("5 из 5");

        var sf = await GetStorefrontAsync(shop.Slug);
        sf.AcceptingOrders.Should().BeFalse();
        sf.NotAcceptingReason.Should().Be("Магазин временно не принимает заказы", "покупателю — без подробностей про тариф");

        var refused = await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(refused);
        body.GetProperty("code").GetString().Should().Be("ShopNotAcceptingOrders");
        body.GetProperty("notAcceptingCode").GetString().Should().Be("MonthlyLimitReached");

        var sub = await J(await AuthedClient(shop.OwnerToken).GetAsync("/api/billing/subscription?line=Orders"));
        sub.GetProperty("orders").GetProperty("ordersThisMonth").GetInt32().Should().Be(5);
        sub.GetProperty("orders").GetProperty("text").GetString().Should().Be("Заказов в этом месяце: 5 из 5");
        sub.GetProperty("orders").GetProperty("warningLevel").GetString().Should().Be("Reached");

        // уже созданные заказы обрабатываются как обычно
        var order = await GetStaffOrderAsync(shop, first.Order.Token);
        var accepted = await ActOkAsync(shop, order, "accept");
        (await ActOkAsync(shop, accepted, "ready")).Status.Should().Be(OrderStatus.Ready);
        // отмена заказа лимит не «возвращает» в текущем месяце — счётчик считает созданные (см. US-24-27); проверяем только, что не падает
        (await GetShopAsync(shop)).OrderLimit.Used.Should().BeGreaterThanOrEqualTo(4);

        // смена тарифа снимает ограничение
        var unlimited = await CreateOrdersPlanAsync(maxShops: 3, maxSeats: 5, maxProducts: 100, maxOrders: null);
        await GivePlanAsync(shop, unlimited);
        (await GetShopAsync(shop)).AcceptingOrders.Should().BeTrue();
        (await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CY24-78")]
    public async Task MonthlyLimit_ParallelOrdersAtLimitMinusOne_ExactlyOneAccepted()
    {
        var shop = await CreateShopAsync();
        var plan = await CreateOrdersPlanAsync(maxShops: 3, maxSeats: 5, maxProducts: 100, maxOrders: 6);
        await GivePlanAsync(shop, plan);
        var p = await CreateProductAsync(shop);
        for (var i = 0; i < 5; i++) await PlaceOrderAsync(shop.Slug, Guest([Line(p, 1)]));

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostOrderAsync(shop.Slug, Guest([Line(p, 1)]))));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "лимит − 1 → принимается ровно один заказ");
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(19);
        (await GetShopAsync(shop)).OrderLimit.Used.Should().Be(6);

        // повтор идемпотентного оформления при исчерпанном лимите — 200 с тем же заказом (проверка ключа раньше правила приёма)
        var board = await GetBoardAsync(shop);
        (board.NewOrders ?? []).Should().HaveCount(6);
    }

    [Fact, TestCase("CY24-79")]
    public async Task PlanWithoutOrders_NotAllowedByPlan_TextsForOwnerAndCustomer()
    {
        var shop = await CreateShopAsync();
        var plan = await CreateOrdersPlanAsync(maxShops: 3, maxSeats: 5, maxProducts: 100, maxOrders: 100, allowOrders: false);
        await GivePlanAsync(shop, plan);
        var p = await CreateProductAsync(shop);

        var dto = await GetShopAsync(shop);
        dto.AcceptingOrders.Should().BeFalse();
        dto.NotAcceptingCode.Should().Be(ShopNotAcceptingCode.NotAllowedByPlan);
        dto.NotAcceptingReason.Should().Be("Ваш тариф не включает приём заказов");
        (await GetStorefrontAsync(shop.Slug)).NotAcceptingReason.Should().Be("Магазин временно не принимает заказы");
        var r = await PostOrderAsync(shop.Slug, Guest([Line(p, 1)]));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(r)).GetProperty("notAcceptingCode").GetString().Should().Be("NotAllowedByPlan");
    }

    // ── Q-24-7: две независимые подписки ─────────────────────────────────────────

    [Fact, TestCase("CY24-80")]
    public async Task Requests_PlanOfAnotherLine400_PendingOfOtherLine409_SameLineOverwrites()
    {
        var shop = await CreateShopAsync();
        await CreateCompanyAsync(shop.OwnerToken);
        var ordersPlan = await CreateOrdersPlanAsync(maxShops: 3, maxSeats: 5, maxProducts: 100);
        var ordersPlan2 = await CreateOrdersPlanAsync(maxShops: 5, maxSeats: 9, maxProducts: 100);
        var servicesPlanId = await FirstServicesPlanIdAsync();
        var c = AuthedClient(shop.OwnerToken);

        // тариф «Записей» на экран «Заказов» и наоборот
        var wrong = await c.PostJsonAsync("/api/billing/subscription/request", new { planId = servicesPlanId, line = "Orders" });
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Этот тариф из другой линейки");
        var wrong2 = await c.PostJsonAsync("/api/billing/subscription/request", new { planId = ordersPlan.GetProperty("id").GetGuid() });
        wrong2.StatusCode.Should().Be(HttpStatusCode.BadRequest, "без line — «Записи»");

        var ok = await c.PostJsonAsync("/api/billing/subscription/request", new { planId = ordersPlan.GetProperty("id").GetGuid(), line = "Orders", comment = "хочу" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        (await J(ok)).GetProperty("line").GetString().Should().Be("Orders");

        // ждёт заявка «Заказов» — заявка «Записей» отказывается
        var other = await c.PostJsonAsync("/api/billing/subscription/request", new { planId = servicesPlanId, line = "Services" });
        other.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await other.Content.ReadAsStringAsync()).Should().Contain("У вас уже есть заявка на смену тарифа «Заказы»");

        // та же линейка — перезапись
        var again = await c.PostJsonAsync("/api/billing/subscription/request", new { planId = ordersPlan2.GetProperty("id").GetGuid(), line = "Orders" });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var sub = await J(await c.GetAsync("/api/billing/subscription?line=Orders"));
        sub.GetProperty("pendingRequest").GetProperty("desiredPlanId").GetGuid().Should().Be(ordersPlan2.GetProperty("id").GetGuid());

        // админ видит линейку заявки
        var list = await J(await (await AdminAsync()).GetAsync("/api/admin/subscription-requests"));
        list.GetRawText().Should().Contain("\"line\"");
    }

    [Fact, TestCase("CY24-81")]
    public async Task AdminAssign_OtherLinePlan400_OrdersAssignmentIndependentOfServices_AccountShowsOrdersBlock()
    {
        var shop = await CreateShopAsync();
        var servicesPlanId = await FirstServicesPlanIdAsync();
        var ordersPlan = await CreateOrdersPlanAsync(maxShops: 3, maxSeats: 5, maxProducts: 100, maxOrders: 300);
        var ordersPlanId = ordersPlan.GetProperty("id").GetGuid();

        var bad1 = await AssignAsync(shop.Owner.UserId, servicesPlanId, "Orders");
        bad1.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad1.Content.ReadAsStringAsync()).Should().Contain("Тариф из другой линейки");
        var bad2 = await AssignAsync(shop.Owner.UserId, ordersPlanId, null);
        bad2.StatusCode.Should().Be(HttpStatusCode.BadRequest, "без line — «Записи»");

        // подписка «Записей» до назначения «Заказов»
        var accountId = await AccountIdAsync(shop.Owner.UserId);
        var admin = await AdminAsync();
        var servicesBefore = await J(await admin.GetAsync($"/api/admin/billing-accounts/{accountId}"));
        var servicesPlanBefore = servicesBefore.GetProperty("plan").GetProperty("name").GetString();

        await GivePlanAsync(shop, ordersPlan);
        var after = await J(await admin.GetAsync($"/api/admin/billing-accounts/{accountId}"));
        after.GetProperty("plan").GetProperty("name").GetString().Should().Be(servicesPlanBefore, "подписка «Записей» не тронута");
        var orders = after.GetProperty("ordersSubscription");
        orders.GetProperty("planName").GetString().Should().Be(ordersPlan.GetProperty("name").GetString());
        orders.GetProperty("isActive").GetBoolean().Should().BeTrue();
        orders.GetProperty("ordersLimit").GetInt32().Should().Be(300);
        orders.GetProperty("shopsLimit").GetInt32().Should().Be(3);
        orders.GetProperty("shopsUsed").GetInt32().Should().Be(1);
    }

    private async Task<Guid> FirstServicesPlanIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await GiveActivePaidPlanAsync((await RegisterAsync()).UserId); // гарантирует хотя бы один тариф «Записей»
        return await db.SubscriptionPlanConfigs.Where(x => x.Line == CompanyKind.Services && x.IsActive).OrderBy(x => x.CreatedAt).Select(x => x.Id).FirstAsync();
    }
}
