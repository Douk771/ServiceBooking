using System.Net;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Общие помощники функциональных тестов цикла 24 («Заказы»: время, приём, уведомления, тарифы). Только подготовка данных через
/// HTTP по API_CONTRACT_CYCLE24.md; проверяемое поведение в помощниках не спрятано. Время магазина считается по его часовому поясу
/// (ShopManageDto.TimeZoneId), а не по часам машины, чтобы тесты не зависели от того, где и когда они запущены.
/// </summary>
public abstract class Cycle24TestBase(TestDatabaseFixture fixture) : Cycle23TestBase(fixture)
{
    protected static async Task<JsonElement> J(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    protected static async Task<string> Code(HttpResponseMessage r) => (await J(r)).GetProperty("code").GetString()!;

    protected static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// <summary>Локальное «сейчас» магазина.</summary>
    protected static DateTime ShopNow(ShopCtx shop) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(shop.Shop.TimeZoneId));

    private readonly HashSet<Guid> _allDayShops = [];

    /// <summary>
    /// Текущий РАБОЧИЙ день магазина (API_CONTRACT_CYCLE24.md §470): интервал через полночь относится ко дню начала. У «круглосуточного» магазина
    /// помощника цикла 23 (04:05 → 04:00 следующего дня) с 00:00 до 04:00 местного времени идёт «хвост» вчерашнего дня. Считается независимо от сервера.
    /// </summary>
    protected DateOnly ShopToday(ShopCtx shop)
    {
        var now = ShopNow(shop);
        var date = DateOnly.FromDateTime(now);
        return _allDayShops.Contains(shop.Id) && now.TimeOfDay < TimeSpan.FromHours(4) ? date.AddDays(-1) : date;
    }

    protected new async Task<ShopCtx> CreateShopAsync(
        string? slug = null, string? name = null, ShopSettingsInput? settings = null, bool paidPlan = true, bool openAllDay = true)
    {
        var shop = await base.CreateShopAsync(slug, name, settings, paidPlan, openAllDay);
        if (openAllDay) _allDayShops.Add(shop.Id);
        return shop;
    }

    protected new async Task OpenShopAllDayAsync(ShopCtx shop)
    {
        await base.OpenShopAllDayAsync(shop);
        _allDayShops.Add(shop.Id);
    }

    protected static WorkingDayInput Day(DayOfWeek d, params (string Start, string End)[] intervals) =>
        new(d, intervals.Select(i => new TimeIntervalInput(i.Start, i.End)).ToList());

    /// <summary>Одинаковые часы на все семь дней.</summary>
    protected async Task SetHoursAsync(ShopCtx shop, params (string Start, string End)[] intervals)
    {
        var days = Enum.GetValues<DayOfWeek>().Select(d => Day(d, intervals)).ToList();
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/working-hours", new WorkingHoursInput(days));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        _allDayShops.Remove(shop.Id);
    }

    protected async Task SetPickupAsync(
        ShopCtx shop, bool asap = true, bool scheduled = false, int step = 15, int preorderDays = 0, int minPrep = 0)
    {
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync(
            $"/api/shops/{shop.Id}/pickup-settings", new PickupSettingsDto(asap, scheduled, step, preorderDays, minPrep));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    /// <summary>Магазин с часами 09:00–12:00 каждый день, заказами ко времени на 3 дня вперёд, шаг 30 минут: «завтра» всегда 6 слотов.</summary>
    protected async Task<ShopCtx> CreateScheduledShopAsync(int preorderDays = 3, bool paidPlan = true, ShopSettingsInput? settings = null)
    {
        var shop = await CreateShopAsync(paidPlan: paidPlan, openAllDay: false, settings: settings);
        await SetHoursAsync(shop, ("09:00", "12:00"));
        await SetPickupAsync(shop, asap: true, scheduled: true, step: 30, preorderDays: preorderDays, minPrep: 0);
        return shop;
    }

    protected async Task<StorefrontDto> GetStorefrontAsync(string slug, DateOnly? date = null)
    {
        var url = $"/api/storefront/{slug}" + (date is null ? "" : $"?date={D(date.Value)}");
        var r = await AnonymousClient().GetAsync(url);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StorefrontDto>())!;
    }

    protected async Task<PickupSlotsDto> GetSlotsAsync(string slug, DateOnly date)
    {
        var r = await AnonymousClient().GetAsync($"/api/storefront/{slug}/pickup-slots?date={D(date)}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<PickupSlotsDto>())!;
    }

    /// <summary>Выбор слота: n-й слот даты (по умолчанию первый).</summary>
    protected async Task<PickupSelectionInput> SlotAsync(string slug, DateOnly date, int index = 0)
    {
        var slots = await GetSlotsAsync(slug, date);
        slots.Slots.Should().HaveCountGreaterThan(index, "на дату должны быть слоты: " + slots.ReasonText);
        return new PickupSelectionInput(PickupKind.Slot, date, slots.Slots[index].StartUtc);
    }

    protected CreateOrderInput GuestAt(
        IEnumerable<OrderLineInput> items, PickupSelectionInput? pickup, bool notifyByMessenger = false, string? phone = null, Guid? key = null) =>
        new(key ?? Guid.NewGuid(), items.ToList(), "Иван", phone ?? UniquePhone(), null, null, pickup, notifyByMessenger);

    protected async Task<OrderBoardDto> GetBoardAsync(ShopCtx shop, string? token = null)
    {
        var r = await AuthedClient(token ?? shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/order-board");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<OrderBoardDto>())!;
    }

    protected async Task<HttpResponseMessage> SetAcceptanceAsync(ShopCtx shop, object body, string? token = null) =>
        await AuthedClient(token ?? shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/acceptance", body);

    protected async Task<ShopManageDto> GetShopAsync(ShopCtx shop)
    {
        var r = await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ShopManageDto>())!;
    }

    protected async Task SetSoldOutAsync(ShopCtx shop, Guid productId, bool soldOut, string? scope, string? token = null)
    {
        object body = scope is null ? new { isSoldOut = soldOut } : new { isSoldOut = soldOut, scope };
        var r = await AuthedClient(token ?? shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/products/{productId}/sold-out", body);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    protected async Task PutWeekdaysAsync(ShopCtx shop, ProductDto p, params DayOfWeek[]? weekdays)
    {
        var r = await AuthedClient(shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/products/{p.Id}", new ProductInput(
            p.CategoryId, p.Name, p.Description, p.Unit, p.Price, p.PortionText, p.WeightStepGrams,
            p.Unit == ProductUnit.Weight ? p.MinQuantity : null, p.IsPublished, null, weekdays?.ToList()));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    protected static IEnumerable<StorefrontProductDto> AllProducts(StorefrontDto sf) => sf.Categories.SelectMany(c => c.Products);
}
