using System.Net;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 37, контрактная проверка: реальные ответы вертикали «Дома» сверяются с машиночитаемой схемой <c>contracts/cycle37/openapi.json</c>
/// (API_CONTRACT_CYCLE37.md, ARCHITECTURE_CYCLE37.md §37.0). Только ФОРМА ответа: имя поля, обязательность, формат даты, nullable; поведение
/// проверяют остальные CY37-*. Валидатор строгий — лишнее поле тоже нарушение. Расхождения собираются списком по всем маршрутам, а не
/// падением на первом.
/// </summary>
public class Cycle37ContractTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    private static readonly OpenApiContract C37 = OpenApiContract.Load("cycle37");

    private sealed class Violations
    {
        public readonly List<string> Items = [];

        public async Task Check(string method, string path, HttpResponseMessage response, int status)
        {
            var text = await response.Content.ReadAsStringAsync();
            ((int)response.StatusCode).Should().Be(status, $"{method} {path}: {text}");
            JsonElement body;
            try { body = JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException) { Items.Add($"{method} {path} -> {status}: тело не JSON: {text}"); return; }
            Items.AddRange(C37.Collect(method, path, status, body).Select(e => $"{method} {path} -> {status}: {e}"));
        }
    }

    [Fact, TestCase("CY37-01")]
    public async Task PublicRoutes_MatchContract()
    {
        var v = new Violations();
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, capacity: 4, price: 4000, extraBedsMax: 2, extraBedPrice: 700, hasCot: true);
        var ci = InDays(10);
        var co = InDays(13);

        await v.Check("GET", "/api/stays/public/amenities", await AnonymousClient().GetAsync("/api/stays/public/amenities"), 200);
        await v.Check("GET", "/api/stays/public/catalog", await AnonymousClient().GetAsync("/api/stays/public/catalog"), 200);
        await v.Check("GET", "/api/stays/public/catalog",
            await AnonymousClient().GetAsync($"/api/stays/public/catalog?checkIn={D(ci)}&checkOut={D(co)}&guests=2"), 200);
        await v.Check("GET", "/api/stays/public/companies/{slug}", await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}"), 200);
        await v.Check("GET", "/api/stays/public/companies/{slug}",
            await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}?checkIn={D(ci)}&checkOut={D(co)}&guests=3"), 200);
        await v.Check("GET", "/api/stays/public/companies/{slug}/houses/{houseSlug}",
            await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}"), 200);
        await v.Check("GET", "/api/stays/public/houses/{houseId}/calendar", await AnonymousClient().GetAsync($"/api/stays/public/houses/{house.Id}/calendar"), 200);
        await v.Check("POST", "/api/stays/public/houses/{houseId}/quote",
            await AnonymousClient().PostJsonAsync($"/api/stays/public/houses/{house.Id}/quote", new StayQuoteInput(ci, co, 4, 1, 0, true)), 200);
        // расчёт с проблемами (суммы нули, списки пусты) — тоже по схеме
        await v.Check("POST", "/api/stays/public/houses/{houseId}/quote",
            await AnonymousClient().PostJsonAsync($"/api/stays/public/houses/{house.Id}/quote", new StayQuoteInput(ci, co, 20, 0, 3, false)), 200);

        v.Items.Should().BeEmpty("публичные ответы должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    [Fact, TestCase("CY37-02")]
    public async Task GuestBookingRoutes_MatchContract()
    {
        var v = new Violations();
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 3000);
        var ci = InDays(20);
        var co = InDays(22);

        var quote = await QuoteAsync(house.Id, ci, co);
        var input = Booking(ci, co, quote.TotalRub);
        await v.Check("POST", "/api/stays/public/houses/{houseId}/bookings", await PostBookingAsync(house.Id, input), 201);
        await v.Check("POST", "/api/stays/public/houses/{houseId}/bookings", await PostBookingAsync(house.Id, input), 200); // повтор: тот же ключ
        var created = (await (await PostBookingAsync(house.Id, input)).Content.ReadJsonAsync<CreateStayBookingResponse>())!;

        await v.Check("GET", "/api/stays/bookings/public/{token}", await AnonymousClient().GetAsync($"/api/stays/bookings/public/{created.Token}"), 200);
        await v.Check("POST", "/api/stays/bookings/public/{token}/payment-proofs", await AttachProofAsync(created.Token), 201);
        await v.Check("POST", "/api/stays/bookings/public/{token}/payment-proofs", await AttachProofAsync(created.Token, bytes: SamplePdf(), contentType: "application/pdf"), 201);
        var afterProofs = await GetPublicBookingAsync(created.Token);
        afterProofs.Status.Should().Be(StayBookingStatus.AwaitingPaymentCheck);

        // файл подтверждения — не JSON, проверяем только код и заголовки (тело — байты)
        var proofId = afterProofs.PaymentProofs.First().Id;
        var file = await AnonymousClient().GetAsync($"/api/stays/bookings/public/{created.Token}/payment-proofs/{proofId}");
        file.StatusCode.Should().Be(HttpStatusCode.OK);

        await v.Check("POST", "/api/stays/bookings/public/{token}/cancel", await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{created.Token}/cancel", new { }), 200);

        // «Мои брони» (вошедший гость): бронь аккаунта
        var guest = await RegisterAsync();
        var q2 = await QuoteAsync(house.Id, InDays(30), InDays(32));
        var mine = await PostBookingAsync(house.Id, Booking(InDays(30), InDays(32), q2.TotalRub), AuthedClient(guest.Token));
        mine.StatusCode.Should().Be(HttpStatusCode.Created, await mine.Content.ReadAsStringAsync());
        await v.Check("GET", "/api/stays/bookings/my", await AuthedClient(guest.Token).GetAsync("/api/stays/bookings/my"), 200);

        v.Items.Should().BeEmpty("ответы брони гостя должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    [Fact, TestCase("CY37-03")]
    public async Task CabinetRoutes_MatchContract()
    {
        var v = new Violations();
        var owner = await RegisterAsync();
        var slug = Unique("dom-c-");
        var create = await AuthedClient(owner.Token).PostJsonAsync("/api/stays/companies",
            new StaysCompanyCreateInput($"Дома {slug}", slug, "Описание", "+79001112233", CurrentOwnerTermsDto().Version, null));
        await v.Check("POST", "/api/stays/companies", create, 201);
        var created = (await create.Content.ReadJsonAsync<StaysCompanyCreatedDto>())!;
        var company = new StaysCtx(owner, created.Token, created.Company);
        await GiveStaysPlanAsync(company.Id, StaysPlans.UnlimitedSeedId);
        var c = AuthedClient(company.OwnerToken);

        await v.Check("GET", "/api/stays/companies/my", await c.GetAsync("/api/stays/companies/my"), 200);
        await v.Check("GET", "/api/stays/slug-check", await c.GetAsync($"/api/stays/slug-check?name=Мои дома"), 200);
        await v.Check("GET", "/api/stays/slug-check", await c.GetAsync($"/api/stays/slug-check?slug={slug}"), 200);
        await v.Check("GET", "/api/stays/trial", await c.GetAsync("/api/stays/trial"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}", await c.GetAsync($"/api/stays/companies/{company.Id}"), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/settings",
            await c.PutJsonAsync($"/api/stays/companies/{company.Id}/settings", company.Company.Settings!), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/payment-details",
            await c.PutJsonAsync($"/api/stays/companies/{company.Id}/payment-details", new PaymentDetailsDto(PaymentDetailsText, PaymentPurposeText)), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/provider",
            await c.PutJsonAsync($"/api/stays/companies/{company.Id}/provider", new ProviderInput(StayProviderStatus.SelfEmployed, "Иванов Иван", ValidPersonInn, null, "г. Новокузнецк")), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}", await c.GetAsync($"/api/stays/companies/{company.Id}"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/notification-settings", await c.GetAsync($"/api/stays/companies/{company.Id}/notification-settings"), 200);
        (await c.GetAsync($"/api/stays/companies/{company.Id}/qr")).StatusCode.Should().Be(HttpStatusCode.OK);

        // дома
        var created1 = await c.PostJsonAsync($"/api/stays/companies/{company.Id}/houses", new HouseCreateInput("Дом контракта", 4));
        await v.Check("POST", "/api/stays/companies/{companyId}/houses", created1, 201);
        var house = (await created1.Content.ReadJsonAsync<HouseManageDto>())!;
        var hp = $"/api/stays/companies/{company.Id}/houses/{house.Id}";
        await v.Check("GET", "/api/stays/companies/{companyId}/houses", await c.GetAsync($"/api/stays/companies/{company.Id}/houses"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/houses/{houseId}", await c.GetAsync(hp), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/houses/{houseId}/setup",
            await c.PutJsonAsync(hp + "/setup", new HouseSetupInput(house.Name, house.Slug, 4, true, 2, 500, false, true)), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/houses/{houseId}/content",
            await c.PutJsonAsync(hp + "/content", new HouseContentInput("Описание", ["Wifi", "Kitchen"], "Шерегеш, Лесная 1", null, null, "Код ключницы 1234")), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/houses/{houseId}/pricing",
            await c.PutJsonAsync(hp + "/pricing", new HousePricingInput(HousePriceMode.ByDates, 4000)), 200);
        var period = await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(InDays(1), InDays(60), 4500));
        await v.Check("POST", "/api/stays/companies/{companyId}/houses/{houseId}/price-periods", period, 201);
        var periodDto = (await period.Content.ReadJsonAsync<PricePeriodDto>())!;
        await v.Check("GET", "/api/stays/companies/{companyId}/houses/{houseId}/price-periods", await c.GetAsync(hp + "/price-periods"), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/houses/{houseId}/price-periods/{periodId}",
            await c.PutJsonAsync($"{hp}/price-periods/{periodDto.Id}", new PricePeriodInput(InDays(1), InDays(61), 4600)), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/houses/{houseId}/registry",
            await c.PutJsonAsync(hp + "/registry", new HouseRegistryInput(HouseObjectKind.GuestHouse, "ABC-12345", "https://example.test/r/1", null)), 200);
        var fresh = await GetHouseAsync(company, house.Id);
        await v.Check("POST", "/api/stays/companies/{companyId}/houses/{houseId}/publish",
            await c.PostJsonAsync(hp + "/publish", new HousePublishInput(new AttestationInput(true, fresh.RegistryNotice.Version))), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/houses/{houseId}", await c.GetAsync(hp), 200);
        (await c.GetAsync(hp + "/qr")).StatusCode.Should().Be(HttpStatusCode.OK);

        // шахматка, блокировка, брони, график
        var block = await c.PostJsonAsync($"/api/stays/companies/{company.Id}/blocks", new HouseBlockInput(house.Id, InDays(40), InDays(42), HouseBlockKind.Repair, "Ремонт крыши"));
        await v.Check("POST", "/api/stays/companies/{companyId}/blocks", block, 201);
        var blockDto = (await block.Content.ReadJsonAsync<HouseBlockDto>())!;
        await v.Check("PUT", "/api/stays/companies/{companyId}/blocks/{blockId}",
            await c.PutJsonAsync($"/api/stays/companies/{company.Id}/blocks/{blockDto.Id}", new HouseBlockInput(house.Id, InDays(40), InDays(43), HouseBlockKind.Other, null)), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/board", await c.GetAsync($"/api/stays/companies/{company.Id}/board"), 200);

        var bk = await BookOkAsync(house.Id, InDays(5), InDays(7));
        var bookingId = await BookingIdAsync(bk.Token);
        await AttachProofOkAsync(bk.Token);
        await v.Check("GET", "/api/stays/companies/{companyId}/board", await c.GetAsync($"/api/stays/companies/{company.Id}/board"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/bookings", await c.GetAsync($"/api/stays/companies/{company.Id}/bookings"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/bookings",
            await c.GetAsync($"/api/stays/companies/{company.Id}/bookings?status=AwaitingPaymentCheck&status=Confirmed&status=Held"), 200);
        var card = await StaffCardAsync(company, bookingId);
        await v.Check("GET", "/api/stays/companies/{companyId}/bookings/{bookingId}", await c.GetAsync($"/api/stays/companies/{company.Id}/bookings/{bookingId}"), 200);
        await v.Check("POST", "/api/stays/companies/{companyId}/bookings/{bookingId}/confirm-payment",
            await StaffActionAsync(company, bookingId, "confirm-payment", card.Version), 200);

        var qm = await c.PostJsonAsync($"/api/stays/companies/{company.Id}/bookings/quote", new StaffStayQuoteInput(house.Id, InDays(8), InDays(9), 2, 0, 0, false));
        await v.Check("POST", "/api/stays/companies/{companyId}/bookings/quote", qm, 200);
        await v.Check("POST", "/api/stays/companies/{companyId}/bookings",
            await c.PostJsonAsync($"/api/stays/companies/{company.Id}/bookings", new ManualStayBookingInput(house.Id, InDays(8), InDays(9), 2, 0, 0, false, "Звонок", null, false, 9000, "по телефону")), 201);
        await v.Check("GET", "/api/stays/companies/{companyId}/schedule", await c.GetAsync($"/api/stays/companies/{company.Id}/schedule"), 200);

        v.Items.Should().BeEmpty("ответы кабинета должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    [Fact, TestCase("CY37-04")]
    public async Task JsonConflicts_MatchContract()
    {
        var v = new Violations();
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 3000);
        var c = AuthedClient(company.OwnerToken);
        var ci = InDays(15);
        var co = InDays(17);

        var first = await BookOkAsync(house.Id, ci, co);
        var bookingId = await BookingIdAsync(first.Token);

        // StayRefusalDto: даты заняты
        var q = await QuoteAsync(house.Id, ci, co);
        await v.Check("POST", "/api/stays/public/houses/{houseId}/bookings", await PostBookingAsync(house.Id, Booking(ci, co, q.TotalRub)), 409);
        // StayRefusalDto с вложенным расчётом: цена изменилась
        var q2 = await QuoteAsync(house.Id, InDays(25), InDays(27));
        await v.Check("POST", "/api/stays/public/houses/{houseId}/bookings", await PostBookingAsync(house.Id, Booking(InDays(25), InDays(27), q2.TotalRub + 1)), 409);

        // StayStaffConflictDto: устаревшая версия
        await v.Check("POST", "/api/stays/companies/{companyId}/bookings/{bookingId}/cancel",
            await StaffActionAsync(company, bookingId, "cancel", version: 99, reason: "причина"), 409);
        // StayGuestConflictDto: отмена уже отменённой
        await v.Check("POST", "/api/stays/bookings/public/{token}/cancel", await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{first.Token}/cancel", new { }), 200);
        await v.Check("POST", "/api/stays/bookings/public/{token}/cancel", await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{first.Token}/cancel", new { }), 409);
        await v.Check("POST", "/api/stays/bookings/public/{token}/payment-proofs", await AttachProofAsync(first.Token), 409);

        // StaysConflictDto: пересечение периодов цен и блокировка поверх брони
        var hp = $"/api/stays/companies/{company.Id}/houses/{house.Id}";
        (await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(InDays(1), InDays(30), 3000))).StatusCode.Should().Be(HttpStatusCode.Created);
        await v.Check("PUT", "/api/stays/companies/{companyId}/houses/{houseId}/pricing",
            await c.PutJsonAsync(hp + "/pricing", new HousePricingInput(HousePriceMode.ByDates, null)), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/houses/{houseId}/pricing",
            await c.PutJsonAsync(hp + "/pricing", new HousePricingInput(HousePriceMode.Constant, null)), 200);
        await v.Check("POST", "/api/stays/companies/{companyId}/houses/{houseId}/price-periods",
            await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(InDays(10), InDays(40), 3100)), 409);
        var held = await BookOkAsync(house.Id, InDays(3), InDays(5));
        _ = held;
        await v.Check("POST", "/api/stays/companies/{companyId}/blocks", await PostBlockAsync(company, house.Id, InDays(4), InDays(6)), 409);
        await v.Check("PUT", "/api/stays/companies/{companyId}/slug",
            await c.PutJsonAsync($"/api/stays/companies/{company.Id}/slug", new SlugInput("admin")), 409);

        v.Items.Should().BeEmpty("409 с JSON-телом должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }
}
