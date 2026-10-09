using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Общие помощники функциональных тестов «Бань» (цикл 42): компания с пробным периодом, ресурс (баня/чан) с окнами и ценой, публичные запросы. Только подготовка данных через
/// HTTP-API по API_CONTRACT_CYCLE42.md; проверяемое поведение в помощниках не спрятано. Время суток ресурса — МИНУТЫ бизнес-дня (1380 = 23:00, 1560 = 02:00 следующих суток).
/// </summary>
public abstract class Cycle42TestBase(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    protected sealed record BathCtx(AuthResponseDto Owner, string Token, Guid CompanyId, string Slug, string Name, int CityId);

    protected sealed record ResCtx(BathCtx Company, Guid Id, string Slug, string Name);

    /// <summary>Цена часа в правилах ресурса по умолчанию: плоская на все дни и часы.</summary>
    protected const int HourPrice = 2000;

    protected static readonly (int Start, int End) WideWindow = (360, 1800);

    protected Task<int> NthCityIdAsync(int n) => WithDbAsync(db => db.Cities.Where(c => c.IsActive).OrderBy(c => c.Id).Skip(n).Select(c => c.Id).FirstAsync());

    protected void InvalidateBathsCatalog()
    {
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BathsCatalogService>().InvalidateBase();
    }

    /// <summary>
    /// Банная компания, принимающая брони: пробный период (владелец с подтверждённым номером принимает условия), исполнитель, реквизиты. Без ресурсов.
    /// <paramref name="trial"/> = false — компания без тарифа (гейт закрыт, <c>NoPlan</c>).
    /// </summary>
    protected async Task<BathCtx> CreateBathAsync(int? cityId = null, bool trial = true, bool provider = true, bool paymentDetails = true, string? name = null, AuthResponseDto? owner = null)
    {
        owner ??= await RegisterAsync();
        await MarkPhoneVerifiedAsync(owner.Phone, owner.UserId);
        var city = cityId ?? await AnyCityIdAsync();
        var companyName = name ?? Unique("Баня ");
        var r = await AuthedClient(owner.Token).PostJsonAsync("/api/baths/companies", new
        {
            name = companyName, slug = Unique("bn-").ToLowerInvariant(), cityId = city, address = "Шерегеш, ул. Лесная, 5", phone = "+79001112233",
            description = "Банный комплекс", ownerTermsVersion = CurrentOwnerTermsDto().Version, trialTermsVersion = trial ? BathsTrialTerms.Version : null
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var created = (await r.Content.ReadJsonAsync<BathsCompanyCreatedDto>())!;
        if (trial) created.Trial!.Granted.Should().BeTrue(created.Trial.Message);
        var client = AuthedClient(created.Token);
        var id = created.Company.Id;
        if (provider)
            (await client.PutJsonAsync($"/api/baths/companies/{id}/provider",
                new ProviderInput(StayProviderStatus.SelfEmployed, "Иванов Иван Иванович", ValidPersonInn, null, "г. Новокузнецк, ул. Мира, 1"))).StatusCode.Should().Be(HttpStatusCode.OK);
        if (paymentDetails)
            (await client.PutJsonAsync($"/api/baths/companies/{id}/payment-details", new PaymentDetailsDto(PaymentDetailsText, "Бронь бани"))).StatusCode.Should().Be(HttpStatusCode.OK);
        return new BathCtx(owner, created.Token, id, created.Company.Slug, companyName, city);
    }

    /// <summary>
    /// Ресурс: создан, настроен (вместимость, шаг 30 мин, зазор, предоплата), окна на каждый день недели, плоская цена, опубликован (если <paramref name="publish"/>).
    /// <paramref name="windowsByDay"/> — окна по дням недели 1…7 (переопределяют <paramref name="windows"/>).
    /// </summary>
    protected async Task<ResCtx> AddResourceAsync(
        BathCtx c, string? name = null, int capacity = 6, int minHours = 1, int maxHours = 6, int step = 30, int buffer = 30, int? prepay = 30, (int Start, int End)[]? windows = null,
        Dictionary<int, (int Start, int End)[]>? windowsByDay = null, bool publish = true, int priceRub = HourPrice)
    {
        var client = AuthedClient(c.Token);
        var baseUrl = $"/api/baths/companies/{c.CompanyId}/services";
        var resName = name ?? "Русская баня";
        var created = await client.PostJsonAsync(baseUrl, new ServiceCreateInput(resName));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var svc = (await created.Content.ReadJsonAsync<ServiceManageDto>())!;
        var slug = Unique("res-").ToLowerInvariant();
        var setup = await client.PutJsonAsync($"{baseUrl}/{svc.Id}/setup", new ServiceSetupInput(resName, slug, minHours, maxHours, step, buffer, false, 0, prepay,
            StayServiceCancellationPolicy.NoDeductions, 12, false, Capacity: capacity));
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());
        var w = windows ?? [WideWindow];
        // расписание передаётся на все 7 дней; день без окон — пустой список
        var days = Enumerable.Range(1, 7)
            .Select(d => new WeeklyDayInput(d, (windowsByDay is null ? w : windowsByDay.GetValueOrDefault(d) ?? []).Select(x => new ServiceWindowInput(x.Start, x.End)).ToList())).ToList();
        var weekly = await client.PutJsonAsync($"{baseUrl}/{svc.Id}/weekly-schedule", new WeeklyScheduleInput(days));
        weekly.StatusCode.Should().Be(HttpStatusCode.OK, await weekly.Content.ReadAsStringAsync());
        (await client.PostJsonAsync($"{baseUrl}/{svc.Id}/price-rules", new PriceRuleInput(127, 6, 30, priceRub))).StatusCode.Should().Be(HttpStatusCode.Created);
        if (publish)
        {
            var pub = await client.PostJsonAsync($"{baseUrl}/{svc.Id}/publish", new { });
            pub.StatusCode.Should().Be(HttpStatusCode.OK, await pub.Content.ReadAsStringAsync());
        }
        return new ResCtx(c, svc.Id, slug, resName);
    }

    // ── публичные запросы ────────────────────────────────────────────────────────

    protected Task<HttpResponseMessage> BathQuoteAsync(Guid serviceId, DateOnly date, int startMinute, int hours, HttpClient? client = null) =>
        (client ?? AnonymousClient()).PostJsonAsync($"/api/baths/public/services/{serviceId}/quote", new { businessDate = date, startMinute, hours, items = Array.Empty<object>() });

    protected static object BathOrderBody(DateOnly date, int startMinute, int hours, int? guests, int total, string? phone, Guid? key = null, string name = "Анна Гость", string? comment = null) => new
    {
        businessDate = date, startMinute, hours, items = Array.Empty<object>(), guestsCount = guests, guestName = name, guestPhone = phone, comment, notifyByMessenger = false,
        expectedTotalRub = total, idempotencyKey = key ?? Guid.NewGuid(), captchaToken = (string?)null
    };

    /// <summary>Создание брони без расчёта: сумма — <paramref name="hours"/> × <see cref="HourPrice"/> (плоская цена ресурса по умолчанию).</summary>
    protected Task<HttpResponseMessage> PostBathOrderAsync(Guid serviceId, DateOnly date, int startMinute, int hours, int? guests = 2, string? phone = null, HttpClient? client = null,
        Guid? key = null, int? total = null) =>
        (client ?? AnonymousClient()).PostJsonAsync($"/api/baths/public/services/{serviceId}/orders",
            BathOrderBody(date, startMinute, hours, guests, total ?? hours * HourPrice, phone ?? UniquePhone(), key));

    /// <summary>Расчёт → создание, как форма. Возвращает токен брони.</summary>
    protected async Task<string> BookBathAsync(Guid serviceId, DateOnly date, int startMinute, int hours, int guests = 2, string? phone = null, HttpClient? client = null)
    {
        var quote = await BathQuoteAsync(serviceId, date, startMinute, hours, client);
        quote.StatusCode.Should().Be(HttpStatusCode.OK, await quote.Content.ReadAsStringAsync());
        var q = await J(quote);
        q.GetProperty("ok").GetBoolean().Should().BeTrue(q.ToString());
        var r = await (client ?? AnonymousClient()).PostJsonAsync($"/api/baths/public/services/{serviceId}/orders",
            BathOrderBody(date, startMinute, hours, guests, q.GetProperty("totalRub").GetInt32(), phone ?? UniquePhone()));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await J(r)).GetProperty("token").GetString()!;
    }

    protected async Task<JsonElement> BathOrderPageAsync(string token, HttpClient? client = null)
    {
        var r = await (client ?? AnonymousClient()).GetAsync($"/api/baths/service-orders/public/{token}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await J(r);
    }

    protected async Task<JsonElement> CatalogAsync(string query = "")
    {
        var r = await AnonymousClient().GetAsync("/api/baths/catalog" + query);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await J(r);
    }

    protected static List<Guid> CatalogIds(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("resourceId").GetGuid()).ToList();

    /// <summary>Активные сеансы ресурса не пересекаются по [старт, конец + зазор).</summary>
    protected async Task AssertBathNoOverlapAsync(Guid serviceId)
    {
        var rows = await ActiveSessionsAsync(serviceId);
        for (var i = 0; i < rows.Count; i++)
            for (var j = i + 1; j < rows.Count; j++)
                (rows[i].StartUtc < rows[j].OccupiedUntilUtc && rows[j].StartUtc < rows[i].OccupiedUntilUtc).Should().BeFalse(
                    $"два активных сеанса одного ресурса пересекаются: {rows[i].StartUtc:O}–{rows[i].OccupiedUntilUtc:O} и {rows[j].StartUtc:O}–{rows[j].OccupiedUntilUtc:O}");
    }
}
