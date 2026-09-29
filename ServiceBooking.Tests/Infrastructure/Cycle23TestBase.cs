using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Общие помощники функциональных тестов цикла 23 («Заказы», goods). Только подготовка данных через HTTP-API по
/// API_CONTRACT_CYCLE23.md — проверяемое поведение в помощниках не спрятано.
/// </summary>
public abstract class Cycle23TestBase(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    protected sealed record ShopCtx(AuthResponseDto Owner, string OwnerToken, ShopManageDto Shop)
    {
        public Guid Id => Shop.Id;
        public string Slug => Shop.Slug;
    }

    protected async Task<ShopCtx> CreateShopAsync(
        string? slug = null, string? name = null, ShopSettingsInput? settings = null, bool paidPlan = true, bool openAllDay = true)
    {
        var owner = await RegisterAsync();
        var (token, shop) = await CreateShopForAsync(owner.Token, slug, name);
        // Пользователь с токеном "владельца" — новый токен из ответа POST /api/shops (claim роли).
        var ctx = new ShopCtx(owner, token, shop);
        if (paidPlan) await GiveActivePaidPlanAsync(owner.UserId);
        // Цикл 24 (ARCHITECTURE_CYCLE24.md §449.5): магазин без часов работы не принимает заказы — тесты цикла 23 проверяют заказы,
        // а не часы, поэтому по умолчанию открываем магазин «круглосуточно» (см. OpenShopAllDayAsync). Тесты цикла 24 передают openAllDay: false.
        if (openAllDay) await OpenShopAllDayAsync(ctx);
        if (settings is not null)
        {
            var r = await AuthedClient(token).PutJsonAsync($"/api/shops/{shop.Id}/settings", settings);
            r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            ctx = ctx with { Shop = (await r.Content.ReadJsonAsync<ShopManageDto>())! };
        }
        return ctx;
    }

    /// <summary>
    /// Часы «почти круглосуточно» (каждый день 04:05 → 04:00 следующего дня — 5-минутная щель неизбежна: интервалы не могут соприкасаться) и
    /// время приготовления 0 мин, чтобы «как можно скорее» было доступно в любой момент прогона, кроме этой щели. Только подготовка данных.
    /// </summary>
    protected async Task OpenShopAllDayAsync(ShopCtx shop)
    {
        var client = AuthedClient(shop.OwnerToken);
        var days = Enum.GetValues<DayOfWeek>().Select(d => new WorkingDayInput(d, [new TimeIntervalInput("04:05", "04:00")])).ToList();
        var hours = await client.PutJsonAsync($"/api/shops/{shop.Id}/working-hours", new WorkingHoursInput(days));
        hours.StatusCode.Should().Be(HttpStatusCode.OK, await hours.Content.ReadAsStringAsync());
        var pickup = await client.PutJsonAsync($"/api/shops/{shop.Id}/pickup-settings", new PickupSettingsDto(true, false, 15, 0, 0));
        pickup.StatusCode.Should().Be(HttpStatusCode.OK, await pickup.Content.ReadAsStringAsync());
    }

    protected async Task<(string Token, ShopManageDto Shop)> CreateShopForAsync(string token, string? slug = null, string? name = null)
    {
        slug ??= Unique("shop-");
        var r = await AuthedClient(token).PostJsonAsync("/api/shops", new CreateShopInput(
            name ?? $"Магазин {slug}", slug, await AnyCityIdAsync(), null, null, null, null, null,
            new ShopOwnerTermsInput(CurrentOwnerTermsDto().Version)));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var body = (await r.Content.ReadJsonAsync<CreateShopResponse>())!;
        return (body.Token, body.Shop);
    }

    protected async Task<AuthResponseDto> AddShopStaffAsync(ShopCtx shop)
    {
        var staff = await AddMasterAsync(shop.OwnerToken, shop.Id);
        return staff;
    }

    protected async Task<CategoryDto> CreateCategoryAsync(ShopCtx shop, string name = "Горячее", bool hidden = false)
    {
        var r = await AuthedClient(shop.OwnerToken).PostJsonAsync($"/api/shops/{shop.Id}/categories", new CategoryInput(name, hidden));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<CategoryDto>())!;
    }

    protected async Task<ProductDto> CreateProductAsync(
        ShopCtx shop, string name = "Шаурма", decimal price = 250m, ProductUnit unit = ProductUnit.Piece,
        Guid? categoryId = null, int? weightStep = null, int? minGrams = null, bool published = true, string? portion = null)
    {
        var r = await AuthedClient(shop.OwnerToken).PostJsonAsync($"/api/shops/{shop.Id}/products", new ProductInput(
            categoryId, name, null, unit, price, unit == ProductUnit.Piece ? portion : null,
            unit == ProductUnit.Weight ? weightStep : null, unit == ProductUnit.Weight ? minGrams : null, published, null));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ProductDto>())!;
    }

    protected async Task SetStockAsync(ShopCtx shop, Guid productId, int? onHand, string? token = null)
    {
        var r = await AuthedClient(token ?? shop.OwnerToken).PutJsonAsync(
            $"/api/shops/{shop.Id}/products/{productId}/stock", new StockInput(onHand));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    protected async Task<ProductDto> GetProductAsync(ShopCtx shop, Guid productId)
    {
        var r = await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/products");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = (await r.Content.ReadJsonAsync<List<ProductDto>>())!;
        return list.Single(p => p.Id == productId);
    }

    protected Task<ShopManageDto> UpdateSettingsAsync(ShopCtx shop, ShopSettingsInput settings) =>
        PutSettingsAsync(shop, settings, HttpStatusCode.OK);

    private async Task<ShopManageDto> PutSettingsAsync(ShopCtx shop, ShopSettingsInput settings, HttpStatusCode expected)
    {
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/settings", settings);
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ShopManageDto>())!;
    }

    protected static ShopSettingsInput Settings(
        ShopCustomerMode mode = ShopCustomerMode.Anyone, OrderAcceptanceMode accept = OrderAcceptanceMode.Manual,
        bool cancel = true, bool trackStock = false) => new(mode, accept, cancel, trackStock);

    protected static OrderLineInput Line(ProductDto p, int quantity) => new(p.Id, quantity, p.Price);

    protected CreateOrderInput Guest(
        IEnumerable<OrderLineInput> items, string? name = "Иван", string? phone = null, string? comment = null,
        Guid? key = null) =>
        new(key ?? Guid.NewGuid(), items.ToList(), name, phone ?? UniquePhone(), comment, null);

    protected async Task<HttpResponseMessage> PostOrderAsync(string slug, CreateOrderInput input, string? token = null)
    {
        var client = token is null ? AnonymousClient() : AuthedClient(token);
        return await client.PostJsonAsync($"/api/storefront/{slug}/orders", input);
    }

    protected async Task<CreateOrderResponse> PlaceOrderAsync(string slug, CreateOrderInput input, string? token = null)
    {
        var r = await PostOrderAsync(slug, input, token);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<CreateOrderResponse>())!;
    }

    protected async Task<PublicOrderDto> GetPublicOrderAsync(string token, string? authToken = null)
    {
        var client = authToken is null ? AnonymousClient() : AuthedClient(authToken);
        var r = await client.GetAsync($"/api/orders/public/{token}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadJsonAsync<PublicOrderDto>())!;
    }

    protected async Task<StaffOrderDto> GetStaffOrderAsync(ShopCtx shop, string orderToken, string? actorToken = null)
    {
        // Заказ у персонала ищем по номеру через доску (id заказа в публичном DTO нет — и не должно быть).
        var pub = await GetPublicOrderAsync(orderToken);
        var r = await AuthedClient(actorToken ?? shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/order-board");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var board = (await r.Content.ReadJsonAsync<OrderBoardDto>())!;
        // Цикл 24: номер уникален в пределах ДНЯ ВЫДАЧИ, поэтому заказ ищем по паре (дата выдачи, номер); предзаказы лежат в группах.
        var card = (board.NewOrders ?? []).Concat(board.Accepted ?? []).Concat(board.Ready ?? []).Concat(board.CompletedToday ?? [])
            .Concat((board.Preorders ?? []).SelectMany(g => g.Orders))
            .Single(c => c.Number == pub.Number && c.Pickup.Date == pub.Pickup.Date);
        var one = await AuthedClient(actorToken ?? shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/orders/{card.Id}");
        one.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await one.Content.ReadJsonAsync<StaffOrderDto>())!;
    }

    protected async Task<StaffOrderDto> PlaceAndLoadAsync(ShopCtx shop, IEnumerable<OrderLineInput> items, string? phone = null)
    {
        var placed = await PlaceOrderAsync(shop.Slug, Guest(items, phone: phone));
        return await GetStaffOrderAsync(shop, placed.Order.Token);
    }

    protected Task<HttpResponseMessage> ActionAsync(
        ShopCtx shop, Guid orderId, string action, int version, string? token = null, string? reason = null) =>
        AuthedClient(token ?? shop.OwnerToken).PostJsonAsync(
            $"/api/shops/{shop.Id}/orders/{orderId}/{action}",
            action is "reject" or "cancel" ? new ReasonInput(version, reason) : (object)new VersionInput(version));

    protected async Task<StaffOrderDto> ActOkAsync(ShopCtx shop, StaffOrderDto order, string action, string? token = null, string? reason = null)
    {
        var r = await ActionAsync(shop, order.Id, action, order.Version, token, reason);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaffOrderDto>())!;
    }
}
