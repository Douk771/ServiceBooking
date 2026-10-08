using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Общие помощники функциональных тестов цикла 37 («Дома»). Только подготовка данных через HTTP-API по API_CONTRACT_CYCLE37.md и
/// contracts/cycle37/openapi.yaml (плюс прямая запись подписки линейки «Дома» — как <c>GiveActivePaidPlanAsync</c> у остальных вертикалей);
/// проверяемое поведение в помощниках не спрятано. Даты «сегодня» считаются по поясу Шерегеша (<c>Asia/Novokuznetsk</c>, UTC+7), а не по поясу
/// машины прогона.
/// </summary>
public abstract class Cycle37TestBase(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    protected static readonly TimeZoneInfo SheregeshTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Novokuznetsk");

    protected static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), SheregeshTz));

    /// <summary>Сегодня по Шерегешу + смещение в днях.</summary>
    protected static DateOnly InDays(int days) => LocalDate(DateTime.UtcNow).AddDays(days);

    /// <summary>UTC-момент локального времени Шерегеша.</summary>
    protected static DateTime LocalToUtc(DateOnly date, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified), SheregeshTz);

    /// <summary>Действительный ИНН физлица (12 цифр, обе контрольные цифры сходятся — проверено <c>InnValidator</c>).</summary>
    protected const string ValidPersonInn = "500100732259";

    protected const string PaymentDetailsText = "Сбербанк, получатель Иванов Иван Иванович, СБП +7 900 111-22-33, карта 2202 0000 1111 2222";
    protected const string PaymentPurposeText = "Аренда дома, бронь";

    protected sealed record StaysCtx(AuthResponseDto Owner, string OwnerToken, StaysCompanyManageDto Company)
    {
        public Guid Id => Company.Id;
        public string Slug => Company.Slug;
    }

    protected sealed record HouseCtx(StaysCtx Company, HouseManageDto House)
    {
        public Guid Id => House.Id;
        public string Slug => House.Slug;
    }

    protected sealed record GuestCtx(string Phone, string Name);

    /// <summary>
    /// Основа каталога («Дома») кешируется на 30 с (ARCHITECTURE_CYCLE37.md §37.11.2, A13; кеш не сбрасывается при публикации/блокировке — задержка до 30 с
    /// задокументирована). Тест, который меняет условия видимости и тут же читает каталог, сбрасывает кеш хоста сам: сам кеш — не предмет этих сценариев.
    /// </summary>
    protected void InvalidateCatalog(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? host = null)
    {
        using var scope = (host ?? Factory).Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Stays.StaysCatalogService>().InvalidateBase();
    }

    // ── компания ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Компания «Дома» с действующим тарифом, реквизитами и сведениями об исполнителе — компания, которая принимает брони.
    /// Любой из шагов можно отключить, чтобы проверить закрытый гейт.
    /// </summary>
    protected async Task<StaysCtx> CreateStaysCompanyAsync(
        string? slug = null, string? name = null, bool plan = true, bool paymentDetails = true, bool provider = true, int? prepayPercent = null,
        StayCancellationPolicy? policy = null, Func<StaysSettingsDto, StaysSettingsDto>? settings = null, AuthResponseDto? ownerAccount = null)
    {
        var registered = ownerAccount ?? await RegisterAsync();
        slug ??= Unique("dom-");
        var created = await CreateStaysCompanyForAsync(registered.Token, slug, name);
        var ctx = new StaysCtx(registered, created.Token, created.Company);
        if (plan) await GiveStaysPlanAsync(ctx.Id, StaysPlans.UnlimitedSeedId);
        if (paymentDetails) ctx = ctx with { Company = await PutPaymentDetailsAsync(ctx, PaymentDetailsText, PaymentPurposeText) };
        if (provider) ctx = ctx with { Company = await PutProviderAsync(ctx, StayProviderStatus.SelfEmployed, "Иванов Иван Иванович", ValidPersonInn, null, "Кемеровская обл., г. Новокузнецк, ул. Мира, 1") };
        if (prepayPercent is not null || policy is not null || settings is not null)
        {
            var current = ctx.Company.Settings!;
            if (prepayPercent is { } p) current = current with { PrepayPercent = p };
            if (policy is { } pol) current = current with { CancellationPolicy = pol };
            if (settings is not null) current = settings(current);
            ctx = ctx with { Company = await PutSettingsAsync(ctx, current) };
        }
        return ctx;
    }

    protected async Task<StaysCompanyCreatedDto> CreateStaysCompanyForAsync(string token, string slug, string? name = null, string? trialTermsVersion = null)
    {
        var r = await AuthedClient(token).PostJsonAsync("/api/stays/companies",
            new StaysCompanyCreateInput(name ?? $"Дома {slug}", slug, null, "+79001112233", CurrentOwnerTermsDto().Version, trialTermsVersion));
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaysCompanyCreatedDto>())!;
    }

    /// <summary>Подписка линейки «Дома» напрямую в БД (быстрый «платный тариф» для подготовки данных, не предмет проверки).</summary>
    protected async Task GiveStaysPlanAsync(Guid companyId, Guid planId, DateTime? paidUntil = null, bool isActive = true)
    {
        await WithDbAsync(async db =>
        {
            var accountId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BillingAccountId).FirstAsync();
            accountId.Should().NotBeNull("у компании «Дома» есть биллинг-аккаунт");
            var sub = await db.StaysSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == accountId);
            if (sub is null)
            {
                sub = new StaysSubscription { Id = Guid.NewGuid(), BillingAccountId = accountId!.Value };
                db.StaysSubscriptions.Add(sub);
            }
            sub.PlanConfigId = planId;
            sub.IsActive = isActive;
            sub.PaidUntil = paidUntil ?? DateTime.UtcNow.AddYears(1);
            sub.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        });
    }

    protected async Task<StaysCompanyManageDto> PutSettingsAsync(StaysCtx ctx, StaysSettingsDto settings, string? token = null)
    {
        var r = await AuthedClient(token ?? ctx.OwnerToken).PutJsonAsync($"/api/stays/companies/{ctx.Id}/settings", settings);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaysCompanyManageDto>())!;
    }

    protected async Task<StaysCompanyManageDto> PutPaymentDetailsAsync(StaysCtx ctx, string? details, string? purpose)
    {
        var r = await AuthedClient(ctx.OwnerToken).PutJsonAsync($"/api/stays/companies/{ctx.Id}/payment-details", new PaymentDetailsDto(details, purpose));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaysCompanyManageDto>())!;
    }

    protected async Task<StaysCompanyManageDto> PutProviderAsync(StaysCtx ctx, StayProviderStatus status, string name, string inn, string? ogrn, string claimsAddress)
    {
        var r = await AuthedClient(ctx.OwnerToken).PutJsonAsync($"/api/stays/companies/{ctx.Id}/provider", new ProviderInput(status, name, inn, ogrn, claimsAddress));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaysCompanyManageDto>())!;
    }

    protected async Task<StaysCompanyManageDto> GetCompanyAsync(StaysCtx ctx, string? token = null)
    {
        var r = await AuthedClient(token ?? ctx.OwnerToken).GetAsync($"/api/stays/companies/{ctx.Id}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaysCompanyManageDto>())!;
    }

    // ── персонал ────────────────────────────────────────────────────────────────

    protected sealed record StaffCtx(AuthResponseDto User, string Token, Guid MemberId);

    protected async Task<StaffCtx> AddStaffAsync(StaysCtx company, string position)
    {
        var user = await RegisterAsync();
        var r = await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/Companies/{company.Id}/members",
            new { phone = user.Phone, firstName = user.FirstName, lastName = user.LastName, role = "Master", bio = (string?)null, email = (string?)null, position });
        r.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await r.Content.ReadAsStringAsync());
        var member = await J(r);
        var token = (await LoginAsync(user.Phone, "Password123!")).Token;
        return new StaffCtx(user, token, member.GetProperty("id").GetGuid());
    }

    // ── дома ────────────────────────────────────────────────────────────────────

    /// <summary>Дом: создан, настроен, цена задана, вид объекта указан, опубликован под заверением (решение заказчика ЮР-2).</summary>
    protected async Task<HouseCtx> CreateHouseAsync(
        StaysCtx company, string? name = null, int capacity = 4, int price = 5000, int extraBedsMax = 0, int extraBedPrice = 0,
        bool dogsForbidden = false, bool hasCot = false, bool publish = true, string? address = "Шерегеш, ул. Лесная, 5")
    {
        var client = AuthedClient(company.OwnerToken);
        var created = await client.PostJsonAsync($"/api/stays/companies/{company.Id}/houses", new HouseCreateInput(name ?? Unique("Дом "), capacity));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var house = (await created.Content.ReadJsonAsync<HouseManageDto>())!;

        var setup = await client.PutJsonAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}/setup",
            new HouseSetupInput(house.Name, house.Slug, capacity, extraBedsMax > 0, extraBedsMax, extraBedPrice, dogsForbidden, hasCot));
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());

        var content = await client.PutJsonAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}/content",
            new HouseContentInput("Уютный дом у подъёмников", [], address, null, null, null));
        content.StatusCode.Should().Be(HttpStatusCode.OK, await content.Content.ReadAsStringAsync());

        var pricing = await client.PutJsonAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}/pricing", new HousePricingInput(HousePriceMode.Constant, price));
        pricing.StatusCode.Should().Be(HttpStatusCode.OK, await pricing.Content.ReadAsStringAsync());

        var registry = await client.PutJsonAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}/registry",
            new HouseRegistryInput(HouseObjectKind.Residential, null, null, null));
        registry.StatusCode.Should().Be(HttpStatusCode.OK, await registry.Content.ReadAsStringAsync());
        var current = (await registry.Content.ReadJsonAsync<HouseManageDto>())!;

        if (publish) current = await PublishHouseAsync(company, current);
        return new HouseCtx(company, current);
    }

    protected async Task<HouseManageDto> PublishHouseAsync(StaysCtx company, HouseManageDto house)
    {
        var r = await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}/publish",
            new HousePublishInput(new AttestationInput(true, house.RegistryNotice.Version)));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<HouseManageDto>())!;
    }

    protected async Task<HouseManageDto> GetHouseAsync(StaysCtx company, Guid houseId, string? token = null)
    {
        var r = await AuthedClient(token ?? company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/houses/{houseId}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<HouseManageDto>())!;
    }

    // ── расчёт и бронь гостя ────────────────────────────────────────────────────

    protected async Task<StayQuoteDto> QuoteAsync(
        Guid houseId, DateOnly checkIn, DateOnly checkOut, int adults = 2, int children = 0, int dogs = 0, bool needCot = false, HttpClient? client = null)
    {
        var r = await (client ?? AnonymousClient()).PostJsonAsync($"/api/stays/public/houses/{houseId}/quote",
            new StayQuoteInput(checkIn, checkOut, adults, children, dogs, needCot));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StayQuoteDto>())!;
    }

    protected CreateStayBookingInput Booking(
        DateOnly checkIn, DateOnly checkOut, int expectedTotal, string? phone = null, string name = "Пётр Гость", int adults = 2, int children = 0,
        int dogs = 0, bool needCot = false, string? arrival = null, string? comment = null, Guid? key = null) =>
        new(checkIn, checkOut, adults, children, dogs, needCot, name, phone ?? UniquePhone(), arrival, comment, false, expectedTotal, key ?? Guid.NewGuid(), null);

    protected Task<HttpResponseMessage> PostBookingAsync(Guid houseId, CreateStayBookingInput input, HttpClient? client = null) =>
        (client ?? AnonymousClient()).PostJsonAsync($"/api/stays/public/houses/{houseId}/bookings", input);

    /// <summary>Гость бронирует через quote → create (как форма): итог берётся из расчёта сервера.</summary>
    protected async Task<CreateStayBookingResponse> BookOkAsync(
        Guid houseId, DateOnly checkIn, DateOnly checkOut, string? phone = null, int adults = 2, int children = 0, int dogs = 0, bool needCot = false,
        string? comment = null, string? arrival = null, HttpClient? client = null, string name = "Пётр Гость")
    {
        var quote = await QuoteAsync(houseId, checkIn, checkOut, adults, children, dogs, needCot, client);
        quote.Ok.Should().BeTrue("расчёт для брони должен быть без проблем: " + string.Join("; ", quote.Problems.Select(p => p.Message)));
        var r = await PostBookingAsync(houseId, Booking(checkIn, checkOut, quote.TotalRub, phone, name, adults, children, dogs, needCot, arrival, comment), client);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<CreateStayBookingResponse>())!;
    }

    protected async Task<PublicStayBookingDto> GetPublicBookingAsync(string token, HttpClient? client = null)
    {
        var r = await (client ?? AnonymousClient()).GetAsync($"/api/stays/bookings/public/{token}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<PublicStayBookingDto>())!;
    }

    protected static MultipartFormDataContent FileContent(byte[] bytes, string contentType, string fileName)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        return content;
    }

    protected static byte[] SamplePdf() => Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    protected Task<HttpResponseMessage> AttachProofAsync(string token, HttpClient? client = null, byte[]? bytes = null, string contentType = "image/jpeg", int width = 60) =>
        (client ?? AnonymousClient()).PostAsync($"/api/stays/bookings/public/{token}/payment-proofs",
            FileContent(bytes ?? TestImages.SolidJpeg(width, 40), contentType, "check.jpg"));

    protected async Task<PublicStayBookingDto> AttachProofOkAsync(string token, HttpClient? client = null)
    {
        var r = await AttachProofAsync(token, client);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<PublicStayBookingDto>())!;
    }

    // ── кабинет: брони ──────────────────────────────────────────────────────────

    protected async Task<StaffStayBookingCardDto> StaffCardAsync(StaysCtx company, Guid bookingId, string? token = null)
    {
        var r = await AuthedClient(token ?? company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/bookings/{bookingId}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaffStayBookingCardDto>())!;
    }

    /// <summary>Id брони по публичному токену (в публичном DTO id нет — и не должно быть): через БД.</summary>
    protected Task<Guid> BookingIdAsync(string publicToken) =>
        WithDbAsync(db => db.StayBookings.AsNoTracking().Where(b => b.PublicToken == publicToken).Select(b => b.Id).SingleAsync());

    protected Task<HttpResponseMessage> StaffActionAsync(StaysCtx company, Guid bookingId, string action, int version, string? reason = null, string? token = null) =>
        AuthedClient(token ?? company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/bookings/{bookingId}/{action}",
            action == "confirm-payment" ? (object)new ExpectedVersionInput(version) : new ExpectedVersionReasonInput(version, reason));

    protected async Task<StaffStayBookingCardDto> StaffActionOkAsync(StaysCtx company, Guid bookingId, string action, int version, string? reason = null)
    {
        var r = await StaffActionAsync(company, bookingId, action, version, reason);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaffStayBookingCardDto>())!;
    }

    protected async Task<HouseBlockDto> BlockAsync(StaysCtx company, Guid houseId, DateOnly from, DateOnly to, HouseBlockKind kind = HouseBlockKind.Repair, string? comment = null, string? token = null)
    {
        var r = await PostBlockAsync(company, houseId, from, to, kind, comment, token);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<HouseBlockDto>())!;
    }

    protected Task<HttpResponseMessage> PostBlockAsync(StaysCtx company, Guid houseId, DateOnly from, DateOnly to, HouseBlockKind kind = HouseBlockKind.Repair, string? comment = null, string? token = null) =>
        AuthedClient(token ?? company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/blocks", new HouseBlockInput(houseId, from, to, kind, comment));

    protected async Task<HouseCalendarDto> CalendarAsync(Guid houseId, DateOnly? from = null, DateOnly? to = null, HttpClient? client = null)
    {
        var url = $"/api/stays/public/houses/{houseId}/calendar" + (from is null ? "" : $"?from={D(from.Value)}&to={D(to!.Value)}");
        var r = await (client ?? AnonymousClient()).GetAsync(url);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<HouseCalendarDto>())!;
    }

    protected async Task<StaysScheduleDto> ScheduleAsync(StaysCtx company, string? token = null, DateOnly? from = null, int? days = null)
    {
        var q = new List<string>();
        if (from is not null) q.Add($"from={D(from.Value)}");
        if (days is not null) q.Add($"days={days}");
        var r = await AuthedClient(token ?? company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/schedule" + (q.Count == 0 ? "" : "?" + string.Join("&", q)));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadJsonAsync<StaysScheduleDto>())!;
    }

    protected static string Rub(int amount) => amount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
}
