using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Общие помощники функциональных тестов цикла 25 («Заказы»: MAX персоналу, история, сводка, лист сборки, карточка покупателя, каталог).
/// Только подготовка данных и доступ к БД для проверки последствий; проверяемое поведение в помощниках не спрятано.
/// Формы запросов и ответов — по API_CONTRACT_CYCLE25.md и contracts/cycle25/openapi.yaml.
/// </summary>
public abstract class Cycle25TestBase(TestDatabaseFixture fixture) : Cycle24TestBase(fixture)
{
    protected static HttpClient ClientOn(WebApplicationFactory<Program> host, string? token = null)
    {
        var client = host.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // ── магазин «без щели» ─────────────────────────────────────────────────────────

    /// <summary>
    /// Магазин, открытый «круглосуточно» так, что 5-минутная щель между интервалами (интервалы не могут соприкасаться) лежит за 12 часов от «сейчас»:
    /// в отличие от помощника цикла 23 (щель 04:00–04:05 в один и тот же час каждые сутки) сценарий не падает, если прогон случился в эти пять минут.
    /// «Как можно скорее» доступно, время приготовления 0.
    /// </summary>
    protected async Task<ShopCtx> CreateRoundClockShopAsync(string? slug = null, string? name = null)
    {
        var shop = await CreateShopAsync(slug: slug, name: name, openAllDay: false);
        var gapStart = ShopNow(shop).AddHours(12);
        gapStart = gapStart.AddMinutes(-(gapStart.Minute % 5));
        await SetHoursAsync(shop, (gapStart.AddMinutes(5).ToString("HH:mm"), gapStart.ToString("HH:mm")));
        await SetPickupAsync(shop, asap: true, scheduled: false, step: 15, preorderDays: 0, minPrep: 0);
        return shop;
    }

    /// <summary>Текущий рабочий день магазина — дата витрины (проверена независимо в CY25-60/61/62 и CY24-35).</summary>
    protected async Task<DateOnly> WorkingDayAsync(ShopCtx shop) => (await GetStorefrontAsync(shop.Slug)).Date;

    // ── заказы ───────────────────────────────────────────────────────────────────

    /// <summary>Оформляет заказ гостя (по умолчанию «как можно скорее») и возвращает его карточку глазами персонала.</summary>
    protected async Task<StaffOrderDto> PlaceStaffViewAsync(
        ShopCtx shop, IEnumerable<OrderLineInput> items, string? phone = null, string name = "Иван", string? comment = null,
        PickupSelectionInput? pickup = null, string? buyerToken = null)
    {
        var input = new CreateOrderInput(Guid.NewGuid(), items.ToList(), name, phone ?? UniquePhone(), comment, null, pickup);
        var placed = await PlaceOrderAsync(shop.Slug, input, buyerToken);
        return await GetStaffOrderAsync(shop, placed.Order.Token);
    }

    /// <summary>Проводит заказ по цепочке действий (accept, ready, issue …) владельцем; возвращает итоговую карточку.</summary>
    protected async Task<StaffOrderDto> DriveAsync(ShopCtx shop, StaffOrderDto order, params string[] actions)
    {
        foreach (var action in actions) order = await ActOkAsync(shop, order, action);
        return order;
    }

    protected async Task CancelByCustomerAsync(string publicToken)
    {
        var r = await AnonymousClient().PostAsync($"/api/orders/public/{publicToken}/cancel", null);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    // ── отчёты ───────────────────────────────────────────────────────────────────

    protected Task<HttpResponseMessage> HistoryAsync(ShopCtx shop, object? body, string? token = null) =>
        AuthedClient(token ?? shop.OwnerToken).PostJsonAsync($"/api/shops/{shop.Id}/order-history", body ?? new { });

    protected async Task<OrderHistoryPageDto> HistoryOkAsync(ShopCtx shop, object? body = null, string? token = null)
    {
        var r = await HistoryAsync(shop, body, token);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<OrderHistoryPageDto>())!;
    }

    protected Task<HttpResponseMessage> SummaryAsync(ShopCtx shop, string query = "", string? token = null) =>
        AuthedClient(token ?? shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/summary{query}");

    protected async Task<ShopSummaryDto> SummaryOkAsync(ShopCtx shop, string query = "", string? token = null)
    {
        var r = await SummaryAsync(shop, query, token);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ShopSummaryDto>())!;
    }

    protected Task<HttpResponseMessage> PickListAsync(ShopCtx shop, string query = "", string? token = null) =>
        AuthedClient(token ?? shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/picklist{query}");

    protected async Task<PickListDto> PickListOkAsync(ShopCtx shop, string query = "", string? token = null)
    {
        var r = await PickListAsync(shop, query, token);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<PickListDto>())!;
    }

    protected Task<HttpResponseMessage> CardAsync(ShopCtx shop, Guid customerRef, string? token = null, string query = "") =>
        AuthedClient(token ?? shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/customers/{customerRef}{query}");

    protected async Task<ShopCustomerCardDto> CardOkAsync(ShopCtx shop, Guid customerRef, string? token = null)
    {
        var r = await CardAsync(shop, customerRef, token);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ShopCustomerCardDto>())!;
    }

    protected Task<HttpResponseMessage> PutNoteAsync(ShopCtx shop, Guid customerRef, string? text, string? token = null) =>
        AuthedClient(token ?? shop.OwnerToken).PutJsonAsync($"/api/shops/{shop.Id}/customers/{customerRef}/note", new { text });

    protected async Task<ShopCustomerNoteStateDto> GetNoteOkAsync(ShopCtx shop, Guid customerRef, string? token = null)
    {
        var r = await AuthedClient(token ?? shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/customers/{customerRef}/note");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ShopCustomerNoteStateDto>())!;
    }

    // ── тарифы ───────────────────────────────────────────────────────────────────

    /// <summary>Назначает аккаунту владельца новый тариф линейки «Заказы» через админские маршруты (как в тестах цикла 24).</summary>
    protected async Task<Guid> GiveOrdersPlanAsync(
        string ownerUserId, int? maxShops = null, int? maxSeats = null, int? maxProducts = null, int? maxOrders = null, bool allowPublicListing = true)
    {
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var created = await admin.PostJsonAsync("/api/admin/plans", new
        {
            name = Unique("Заказы · "), description = "тест", highlights = new[] { "заказы" }, pricePerMonth = 1m,
            maxEmployees = maxSeats, maxCompanies = maxShops, isPublic = true, isActive = true, line = "Orders",
            maxProductsPerShop = maxProducts, maxOrdersPerMonth = maxOrders, allowOrders = true, allowPublicListing,
        });
        created.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await created.Content.ReadAsStringAsync());
        var planId = (await J(created)).GetProperty("id").GetGuid();
        var accountId = await WithDbAsync(db => db.BillingAccounts.Where(a => a.OwnerUserId == ownerUserId).Select(a => a.Id).FirstAsync());
        var assigned = await admin.PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", new
        {
            planId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            options = Array.Empty<object>(), line = "Orders", confirmLimitOverflow = true,
        });
        assigned.StatusCode.Should().Be(HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
        return planId;
    }

    // ── канал сообщений покупателю ─────────────────────────────────────────────────

    /// <summary>Подключённый и оплаченный канал аккаунта владельца магазина, назначенный магазину (тот же приём, что в тестах цикла 24).</summary>
    protected async Task<Guid> ConnectShopChannelAsync(ShopCtx shop)
    {
        Guid channelId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var company = await db.Companies.AsNoTracking().SingleAsync(c => c.Id == shop.Id);
            var accountId = company.BillingAccountId
                ?? (await db.BillingAccounts.SingleAsync(a => a.OwnerUserId == shop.Owner.UserId)).Id;
            var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig).SingleAsync(s => s.OwnerUserId == shop.Owner.UserId);
            sub.PlanConfig!.AllowNotificationChannel = true;
            await NotificationTestBase.EnsureWhatsAppPlanRuleAsync(db, sub.PlanConfigId!.Value);
            await db.SaveChangesAsync();
            await NotificationTestBase.EnsureWhatsAppPaidAsync(db, accountId);
            var channel = new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = shop.Owner.UserId, BillingAccountId = accountId, State = ChannelState.Connected,
                PhoneNumber = UniquePhone().TrimStart('+'), ProviderInstanceId = Unique("instance"),
                ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1),
            };
            channel.ProviderSecretCiphertext = ServiceBooking.API.Services.Notifications.SecretProtector.Encrypt(
                "test-provider-token", NotificationDispatchTestFactory.TestEncryptionKeyBase64, channel.Id);
            db.NotificationChannels.Add(channel);
            await db.SaveChangesAsync();
            channelId = channel.Id;
        }
        // Cycle 40 (BE-40-2): no assignment step — the number of the owner's account serves the shop.
        return channelId;
    }

    // ── участники ────────────────────────────────────────────────────────────────

    /// <summary>Владелец удаляет сотрудника из магазина (тот же вызов, что в CY24-69).</summary>
    protected async Task RemoveStaffAsync(ShopCtx shop, AuthResponseDto staff)
    {
        var members = await J(await AuthedClient(shop.OwnerToken).GetAsync($"/api/companies/{shop.Id}/members"));
        var memberId = members.EnumerateArray().First(m => m.GetProperty("userId").GetString() == staff.UserId).GetProperty("id").GetString();
        var r = await AuthedClient(shop.OwnerToken).DeleteAsync($"/api/companies/{shop.Id}/members/{memberId}");
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
    }

    // ── часовой пояс магазина ────────────────────────────────────────────────────

    /// <summary>
    /// Ставит магазину пояс с ФИКСИРОВАННЫМ смещением (Etc/GMT±N) так, чтобы его местное время сейчас было 00:мм — «хвост» ночного интервала.
    /// Возвращает местное «сейчас» и идентификатор пояса. Так сценарии границы суток проверяются в любое время прогона, а не только ночью по Москве.
    /// </summary>
    protected async Task<(DateTime Local, string ZoneId)> PlaceShopInAfterMidnightAsync(ShopCtx shop)
    {
        var utc = DateTime.UtcNow;
        var offset = (24 - utc.Hour) % 24;
        if (offset > 14) offset -= 24;
        var zone = offset == 0 ? "Etc/GMT" : offset > 0 ? $"Etc/GMT-{offset}" : $"Etc/GMT+{-offset}";
        await WithDbAsync(async db =>
        {
            var company = await db.Companies.SingleAsync(c => c.Id == shop.Id);
            company.TimeZoneId = zone;
            await db.SaveChangesAsync();
        });
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(zone));
        local.Hour.Should().Be(0, "магазин переведён в пояс, где сейчас 00:мм");
        return (local, zone); // ВНИМАНИЕ: ShopCtx.Shop.TimeZoneId остаётся прежним — это снимок при создании; верный пояс — второй элемент
    }

    protected async Task MoveShopToCityAsync(ShopCtx shop, int cityId) =>
        await WithDbAsync(async db =>
        {
            var company = await db.Companies.SingleAsync(c => c.Id == shop.Id);
            company.CityId = cityId;
            await db.SaveChangesAsync();
        });

    protected static string MaskOf(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return $"+7 (···) ···-{digits[^4..^2]}-{digits[^2..]}";
    }

    protected static JsonElement Prop(JsonElement e, string name) => e.GetProperty(name);
}
