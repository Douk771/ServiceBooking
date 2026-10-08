using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Общие помощники функциональных тестов цикла 39 («Дома», услуги-слоты). Только подготовка данных через HTTP-API по contracts/cycle39/openapi.yaml; проверяемое
/// поведение в помощниках не спрятано. Время суток услуги — МИНУТЫ от 00:00 даты бизнес-дня (360 = 06:00, 1380 = 23:00, 1560 = 02:00 следующих суток).
/// Даты берутся от «сегодня» по Шерегешу + смещение: бизнес-дата «сегодня» после полуночи (до 06:00) — ещё вчерашняя, поэтому тесты берут даты не ближе 3 дней.
/// </summary>
public abstract class Cycle39TestBase(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    protected const string Tz = "Asia/Novokuznetsk";

    /// <summary>Окно по умолчанию: 08:00 — 02:00 следующих суток, каждый день недели.</summary>
    protected static readonly (int Start, int End) DefaultWindow = (480, 1560);

    protected sealed record SvcCtx(StaysCtx Company, ServiceManageDto Service)
    {
        public Guid Id => Service.Id;
        public string Slug => Service.Slug;
    }

    /// <summary>Ближайший день недели не раньше чем через <paramref name="minDaysAhead"/> дней.</summary>
    protected static DateOnly NextWeekday(DayOfWeek day, int minDaysAhead = 3)
    {
        var d = InDays(minDaysAhead);
        while (d.DayOfWeek != day) d = d.AddDays(1);
        return d;
    }

    protected static DateTime StartUtc(DateOnly businessDate, int minute) => BusinessClock.ToUtc(Tz, businessDate, minute);

    protected static ItemSelectionInput[] NoItems => [];

    // ── услуга ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Услуга: создана, настроена, окна (<paramref name="windows"/> или 08:00–02:00 каждый день), правило цены на все дни 06:00–06:00 (если <paramref name="priceRub"/> не null),
    /// опубликована. Зазор по умолчанию 30 минут, минимальное время до начала — 0, чтобы тесты не зависели от «сейчас».
    /// </summary>
    protected async Task<SvcCtx> CreateServiceAsync(
        StaysCtx company, string? name = null, int minHours = 2, int maxHours = 6, int step = 60, int buffer = 30, int minLeadMinutes = 0, int? prepay = null,
        StayServiceCancellationPolicy policy = StayServiceCancellationPolicy.NoDeductions, int boundaryHours = 12, bool forHouses = true, bool publish = true,
        int? priceRub = 2000, (int Start, int End)[]? windows = null, bool showBuffer = false)
    {
        var client = AuthedClient(company.OwnerToken);
        var created = await client.PostJsonAsync($"/api/stays/companies/{company.Id}/services", new ServiceCreateInput(name ?? "Баня"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var svc = (await created.Content.ReadJsonAsync<ServiceManageDto>())!;
        var slug = Unique("svc-").ToLowerInvariant();
        var setup = await client.PutJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/setup",
            new ServiceSetupInput(name ?? "Баня", slug, minHours, maxHours, step, buffer, showBuffer, minLeadMinutes, prepay, policy, boundaryHours, forHouses));
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());

        var w = windows ?? [DefaultWindow];
        var days = Enumerable.Range(1, 7).Select(d => new WeeklyDayInput(d, w.Select(x => new ServiceWindowInput(x.Start, x.End)).ToList())).ToList();
        var weekly = await client.PutJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/weekly-schedule", new WeeklyScheduleInput(days));
        weekly.StatusCode.Should().Be(HttpStatusCode.OK, await weekly.Content.ReadAsStringAsync());

        if (priceRub is { } price) await AddRuleAsync(company, svc.Id, 127, 6, 30, price);

        var current = await GetServiceAsync(company, svc.Id);
        if (publish)
        {
            var pub = await client.PostJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/publish", new EmptyInput());
            pub.StatusCode.Should().Be(HttpStatusCode.OK, await pub.Content.ReadAsStringAsync());
            current = (await pub.Content.ReadJsonAsync<ServiceManageDto>())!;
        }
        return new SvcCtx(company, current);
    }

    protected async Task<ServiceManageDto> GetServiceAsync(StaysCtx company, Guid serviceId, string? token = null)
    {
        var r = await AuthedClient(token ?? company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/services/{serviceId}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ServiceManageDto>())!;
    }

    protected async Task<PriceRulesDto> AddRuleAsync(StaysCtx company, Guid serviceId, int daysMask, int fromHour, int toHour, int priceRub)
    {
        var r = await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/services/{serviceId}/price-rules",
            new PriceRuleInput(daysMask, fromHour, toHour, priceRub));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<PriceRulesDto>())!;
    }

    protected Task<HttpResponseMessage> AddRuleRawAsync(StaysCtx company, Guid serviceId, int daysMask, int fromHour, int toHour, int priceRub, string? token = null) =>
        AuthedClient(token ?? company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/services/{serviceId}/price-rules",
            new PriceRuleInput(daysMask, fromHour, toHour, priceRub));

    protected async Task<ServiceItemDto> AddItemAsync(StaysCtx company, Guid serviceId, string name = "Веник берёзовый", int price = 300, int max = 10, bool active = true)
    {
        var r = await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/services/{serviceId}/items", new ServiceItemInput(name, price, max, active));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ServiceItemDto>())!;
    }

    protected async Task EnableOrdersWithoutStayAsync(StaysCtx company, bool enabled = true)
    {
        var current = (await GetCompanyAsync(company)).Settings!;
        await PutSettingsAsync(company, current with { AcceptServiceOrdersWithoutStay = enabled });
    }

    // ── публичные чтения ─────────────────────────────────────────────────────────

    protected async Task<ServiceStartsDto> StartsAsync(Guid serviceId, DateOnly date, HttpClient? client = null)
    {
        var r = await (client ?? AnonymousClient()).GetAsync($"/api/stays/public/services/{serviceId}/starts?date={D(date)}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ServiceStartsDto>())!;
    }

    protected async Task<ServiceQuoteDto> QuoteServiceAsync(Guid serviceId, DateOnly date, int startMinute, int hours, ItemSelectionInput[]? items = null, HttpClient? client = null)
    {
        var r = await (client ?? AnonymousClient()).PostJsonAsync($"/api/stays/public/services/{serviceId}/quote",
            new PublicServiceQuoteInput(date, startMinute, hours, (items ?? NoItems).ToList(), null, null, null));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<ServiceQuoteDto>())!;
    }

    // ── заказ без проживания ─────────────────────────────────────────────────────

    protected static CreateServiceOrderInput OrderInput(
        DateOnly date, int startMinute, int hours, int expectedTotal, string? phone = null, string name = "Анна Гость", ItemSelectionInput[]? items = null,
        Guid? key = null, bool messenger = false, string? comment = null) =>
        new(date, startMinute, hours, (items ?? NoItems).ToList(), name, phone, comment, messenger, expectedTotal, key ?? Guid.NewGuid(), null);

    protected Task<HttpResponseMessage> PostOrderAsync(Guid serviceId, CreateServiceOrderInput input, HttpClient? client = null) =>
        (client ?? AnonymousClient()).PostJsonAsync($"/api/stays/public/services/{serviceId}/orders", input);

    /// <summary>Quote → create, как форма. Телефон по умолчанию уникальный.</summary>
    protected async Task<CreateServiceOrderResponse> OrderOkAsync(
        Guid serviceId, DateOnly date, int startMinute, int hours, string? phone = null, ItemSelectionInput[]? items = null, HttpClient? client = null, string name = "Анна Гость")
    {
        var quote = await QuoteServiceAsync(serviceId, date, startMinute, hours, items, client);
        quote.Ok.Should().BeTrue("расчёт должен быть без проблем: " + string.Join("; ", quote.Problems.Select(p => p.Message)));
        var r = await PostOrderAsync(serviceId, OrderInput(date, startMinute, hours, quote.TotalRub, phone ?? UniquePhone(), name, items), client);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<CreateServiceOrderResponse>())!;
    }

    protected async Task<PublicServiceOrderDto> GetOrderAsync(string token, HttpClient? client = null)
    {
        var r = await (client ?? AnonymousClient()).GetAsync($"/api/stays/service-orders/public/{token}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<PublicServiceOrderDto>())!;
    }

    protected Task<HttpResponseMessage> AttachOrderProofAsync(string token, HttpClient? client = null) =>
        (client ?? AnonymousClient()).PostAsync($"/api/stays/service-orders/public/{token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "check.jpg"));

    protected Task<HttpResponseMessage> CancelOrderAsync(string token, HttpClient? client = null) =>
        (client ?? AnonymousClient()).PostJsonAsync($"/api/stays/service-orders/public/{token}/cancel", new { });

    protected Task<Guid> SessionIdOfOrderAsync(string token) =>
        WithDbAsync(db => db.StayServiceSessions.AsNoTracking()
            .Where(s => db.StayServiceOrders.Any(o => o.PublicToken == token && o.Id == s.StayServiceOrderId)).Select(s => s.Id).SingleAsync());

    protected Task<Guid> OrderIdAsync(string token) =>
        WithDbAsync(db => db.StayServiceOrders.AsNoTracking().Where(o => o.PublicToken == token).Select(o => o.Id).SingleAsync());

    // ── кабинет: сеансы ──────────────────────────────────────────────────────────

    protected async Task<StaffServiceSessionCardDto> SessionCardAsync(StaysCtx company, Guid sessionId, string? token = null)
    {
        var r = await AuthedClient(token ?? company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/service-sessions/{sessionId}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaffServiceSessionCardDto>())!;
    }

    protected Task<HttpResponseMessage> SessionActionAsync(StaysCtx company, Guid sessionId, string action, int version, string? reason = null, string? token = null) =>
        AuthedClient(token ?? company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions/{sessionId}/{action}",
            action == "confirm-payment" ? (object)new ExpectedVersionInput(version) : new ExpectedVersionReasonInput(version, reason));

    // ── сеанс в брони дома ───────────────────────────────────────────────────────

    protected Task<HttpResponseMessage> AddSessionAsync(string bookingToken, AddSessionInput input, HttpClient? client = null) =>
        (client ?? AnonymousClient()).PostJsonAsync($"/api/stays/bookings/public/{bookingToken}/sessions", input);

    protected AddSessionInput SessionInput(Guid serviceId, DateOnly date, int startMinute, int hours, int expectedTotal, ItemSelectionInput[]? items = null, Guid? key = null) =>
        new(serviceId, date, startMinute, hours, (items ?? NoItems).ToList(), expectedTotal, key ?? Guid.NewGuid());

    /// <summary>Сеанс в брони через quote → add (как кнопка «Добавить услугу» со страницы брони).</summary>
    protected async Task<PublicStayBookingDto> AddSessionOkAsync(string bookingToken, Guid serviceId, DateOnly date, int startMinute, int hours, ItemSelectionInput[]? items = null)
    {
        var q = await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{bookingToken}/services/{serviceId}/quote",
            new PublicServiceQuoteInput(date, startMinute, hours, (items ?? NoItems).ToList(), null, null, null));
        q.StatusCode.Should().Be(HttpStatusCode.OK, await q.Content.ReadAsStringAsync());
        var quote = (await q.Content.ReadJsonAsync<ServiceQuoteDto>())!;
        quote.Ok.Should().BeTrue("расчёт сеанса в брони: " + string.Join("; ", quote.Problems.Select(p => p.Message)));
        var r = await AddSessionAsync(bookingToken, SessionInput(serviceId, date, startMinute, hours, quote.TotalRub, items));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<PublicStayBookingDto>())!;
    }

    // ── БД ───────────────────────────────────────────────────────────────────────

    protected Task<List<StayServiceSession>> ActiveSessionsAsync(Guid serviceId) =>
        WithDbAsync(db => db.StayServiceSessions.AsNoTracking().Where(s => s.ServiceId == serviceId && s.ReleasedAtUtc == null).OrderBy(s => s.StartUtc).ToListAsync());

    protected Task<List<StayServiceSession>> AllSessionsAsync(Guid serviceId) =>
        WithDbAsync(db => db.StayServiceSessions.AsNoTracking().Where(s => s.ServiceId == serviceId).OrderBy(s => s.StartUtc).ToListAsync());
}
