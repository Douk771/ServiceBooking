using System.Net;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39, контрактная проверка: реальные ответы услуг-слотов и напоминания сверяются с машиночитаемой схемой <c>contracts/cycle39/openapi.json</c>
/// (API_CONTRACT_CYCLE39.md, ARCHITECTURE_CYCLE39.md §39.15). Только ФОРМА ответа: имя поля, обязательность, формат, nullable. Валидатор строгий (лишнее поле — нарушение), кроме
/// DTO, которые контракт цикла 39 объявляет частичными (<c>additionalProperties: true</c>, изменённые маршруты цикла 37). Расхождения собираются списком по всем маршрутам.
/// </summary>
public class Cycle39ContractTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private static readonly OpenApiContract C39 = OpenApiContract.Load("cycle39");

    /// <summary>
    /// Поля, которые цикл 42 ДОПИСАЛ в кабинет услуг «Домов» (<c>capacity</c>, <c>contentWarnings</c> в ServiceManageDto, <c>warnings</c> в ServiceItemDto). Схема cycle39 заморожена
    /// (контракт выпущенного цикла не правится), а схема cycle42 описывает setup/items/content «Домов» частично и не покрывает остальные маршруты кабинета услуг, поэтому
    /// здесь отдельно допускается ровно этот набор «поле не описано схемой». Форма этих полей проверяется по схеме cycle42 в Cycle42ContractTests.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex Cycle42AdditiveField =
        new(@"^\$(\[\d+\])?\.(capacity|contentWarnings|warnings): property is not described by the schema$");

    private sealed class Violations
    {
        public readonly List<string> Items = [];

        public async Task Check(string method, string path, HttpResponseMessage response, int status)
        {
            var text = await response.Content.ReadAsStringAsync();
            ((int)response.StatusCode).Should().Be(status, $"{method} {path}: {text}");
            if (status == 204) return;
            JsonElement body;
            try { body = JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException) { Items.Add($"{method} {path} -> {status}: тело не JSON: {text}"); return; }
            Items.AddRange(C39.Collect(method, path, status, body).Where(e => !Cycle42AdditiveField.IsMatch(e)).Select(e => $"{method} {path} -> {status}: {e}"));
        }
    }

    // ── гость ────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-130")]
    public async Task PublicServiceRoutes_MatchContract()
    {
        var v = new Violations();
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var house = await CreateHouseAsync(company, price: 3000);
        var svc = await CreateServiceAsync(company, prepay: 30, policy: StayServiceCancellationPolicy.PreparationCosts);
        var broom = await AddItemAsync(company, svc.Id, "Веник", 300, 5);
        var anon = AnonymousClient();
        var date = InDays(9);

        await v.Check("GET", "/api/stays/public/companies/{slug}", await anon.GetAsync($"/api/stays/public/companies/{company.Slug}"), 200);
        await v.Check("GET", "/api/stays/public/companies/{slug}/houses/{houseSlug}", await anon.GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}"), 200);
        await v.Check("GET", "/api/stays/public/companies/{slug}/services/{serviceSlug}", await anon.GetAsync($"/api/stays/public/companies/{company.Slug}/services/{svc.Slug}"), 200);
        await v.Check("GET", "/api/stays/public/services/{serviceId}/availability", await anon.GetAsync($"/api/stays/public/services/{svc.Id}/availability"), 200);
        await v.Check("GET", "/api/stays/public/services/{serviceId}/availability",
            await anon.GetAsync($"/api/stays/public/services/{svc.Id}/availability?from={D(date)}&days=7&houseId={house.Id}&checkIn={D(date)}&checkOut={D(date.AddDays(3))}"), 200);
        await v.Check("GET", "/api/stays/public/services/{serviceId}/starts", await anon.GetAsync($"/api/stays/public/services/{svc.Id}/starts?date={D(date)}"), 200);
        await v.Check("GET", "/api/stays/public/services/{serviceId}/starts", await anon.GetAsync($"/api/stays/public/services/{svc.Id}/starts?date={D(InDays(-5))}"), 200);
        await v.Check("POST", "/api/stays/public/services/{serviceId}/quote",
            await anon.PostJsonAsync($"/api/stays/public/services/{svc.Id}/quote", new PublicServiceQuoteInput(date, 720, 3, [new ItemSelectionInput(broom.Id, 2)], null, null, null)), 200);
        await v.Check("POST", "/api/stays/public/services/{serviceId}/quote",
            await anon.PostJsonAsync($"/api/stays/public/services/{svc.Id}/quote", new PublicServiceQuoteInput(date, 450, 1, [], null, null, null)), 200); // с проблемами

        // 400/404 строкой — не JSON, проверяем только код
        (await anon.PostJsonAsync($"/api/stays/public/services/{svc.Id}/quote", new PublicServiceQuoteInput(null, 720, 3, [], null, null, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        v.Items.Should().BeEmpty("публичные ответы услуг должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    [Fact, TestCase("CY39-131")]
    public async Task OrderRoutes_MatchContract_IncludingJson409()
    {
        var v = new Violations();
        var company = await CreateStaysCompanyAsync();
        var svc = await CreateServiceAsync(company, prepay: 30);
        var date = InDays(9);
        var anon = AnonymousClient();
        var quote = await QuoteServiceAsync(svc.Id, date, 720, 2);

        // настройка выключена → 409 ServiceOrdersDisabled
        await v.Check("POST", "/api/stays/public/services/{serviceId}/orders", await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, quote.TotalRub, UniquePhone())), 409);
        await EnableOrdersWithoutStayAsync(company);

        var input = OrderInput(date, 720, 2, quote.TotalRub, UniquePhone());
        await v.Check("POST", "/api/stays/public/services/{serviceId}/orders", await PostOrderAsync(svc.Id, input), 201);
        await v.Check("POST", "/api/stays/public/services/{serviceId}/orders", await PostOrderAsync(svc.Id, input), 200); // повтор: тот же ключ
        var created = (await (await PostOrderAsync(svc.Id, input)).Content.ReadJsonAsync<CreateServiceOrderResponse>())!;
        await v.Check("POST", "/api/stays/public/services/{serviceId}/orders", await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, quote.TotalRub, UniquePhone())), 409); // SlotTaken
        await v.Check("POST", "/api/stays/public/services/{serviceId}/orders", await PostOrderAsync(svc.Id, OrderInput(date, 1080, 2, quote.TotalRub + 1, UniquePhone())), 409); // PriceChanged с расчётом

        await v.Check("GET", "/api/stays/service-orders/public/{token}", await anon.GetAsync($"/api/stays/service-orders/public/{created.Token}"), 200);
        await v.Check("POST", "/api/stays/service-orders/public/{token}/payment-proofs", await AttachOrderProofAsync(created.Token), 201);
        var proofId = (await GetOrderAsync(created.Token)).PaymentProofs.First().Id;
        (await anon.GetAsync($"/api/stays/service-orders/public/{created.Token}/payment-proofs/{proofId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        await v.Check("GET", "/api/stays/service-orders/public/{token}", await anon.GetAsync($"/api/stays/service-orders/public/{created.Token}"), 200); // ждёт проверки оплаты
        await v.Check("POST", "/api/stays/service-orders/public/{token}/cancel", await CancelOrderAsync(created.Token), 200);
        await v.Check("POST", "/api/stays/service-orders/public/{token}/cancel", await CancelOrderAsync(created.Token), 409);
        await v.Check("POST", "/api/stays/service-orders/public/{token}/payment-proofs", await AttachOrderProofAsync(created.Token), 409);
        // заказ без предоплаты и «Расходы на подготовку» (CostsOnlyUpTo) — отдельные формы поля refund
        var free = await CreateServiceAsync(company, "Чан", prepay: null);
        var freeOrder = await OrderOkAsync(free.Id, date, 720, 2);
        await v.Check("GET", "/api/stays/service-orders/public/{token}", await anon.GetAsync($"/api/stays/service-orders/public/{freeOrder.Token}"), 200);
        // push-подписка
        var sub = new PushSubscriptionInput($"https://push.example.test/cy39-contract/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null);
        var held = await OrderOkAsync(svc.Id, date, 1200, 2);
        var subscribed = await anon.PostJsonAsync($"/api/stays/service-orders/public/{held.Token}/push-subscription", sub);
        new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict }.Should().Contain(subscribed.StatusCode, "в тестовом хосте web-push выключен — 409 допустим, форма 204 проверяется отдельно");

        v.Items.Should().BeEmpty("ответы заказа должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    [Fact, TestCase("CY39-132")]
    public async Task BookingSessionRoutes_MatchContract()
    {
        var v = new Violations();
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        var house = await CreateHouseAsync(company, price: 3000);
        var svc = await CreateServiceAsync(company);
        var anon = AnonymousClient();
        var ci = InDays(12);
        var co = ci.AddDays(3);
        var d = ci.AddDays(1);

        var quote = await QuoteAsync(house.Id, ci, co);
        var sel = new StayServiceSelectionInput(svc.Id, d, 1080, 2, []);
        await v.Check("POST", "/api/stays/public/houses/{houseId}/quote",
            await anon.PostJsonAsync($"/api/stays/public/houses/{house.Id}/quote", new StayQuoteInput(ci, co, 2, 0, 0, false, [sel])), 200);
        var createdWith = await PostBookingAsync(house.Id, Booking(ci, co, quote.TotalRub + 4000) with { Services = [sel] });
        await v.Check("POST", "/api/stays/public/houses/{houseId}/bookings", createdWith, 201);
        var withToken = (await createdWith.Content.ReadJsonAsync<CreateStayBookingResponse>())!.Token;
        await v.Check("GET", "/api/stays/bookings/public/{token}", await anon.GetAsync($"/api/stays/bookings/public/{withToken}"), 200);
        // занятое время → 409 StayRefusalDto с serviceIndex
        await v.Check("POST", "/api/stays/public/houses/{houseId}/bookings",
            await PostBookingAsync(house.Id, Booking(ci.AddDays(10), co.AddDays(10), quote.TotalRub + 4000) with { Services = [sel with { BusinessDate = ci.AddDays(11) }, sel with { BusinessDate = ci.AddDays(11) }] }), 409);

        var plain = await BookOkAsync(house.Id, ci.AddDays(20), co.AddDays(20));
        var avail = await anon.GetAsync($"/api/stays/bookings/public/{plain.Token}/services");
        await v.Check("GET", "/api/stays/bookings/public/{token}/services", avail, 200);
        var day = ci.AddDays(21);
        await v.Check("GET", "/api/stays/bookings/public/{token}/services/{serviceId}/starts", await anon.GetAsync($"/api/stays/bookings/public/{plain.Token}/services/{svc.Id}/starts?date={D(day)}"), 200);
        await v.Check("POST", "/api/stays/bookings/public/{token}/services/{serviceId}/quote",
            await anon.PostJsonAsync($"/api/stays/bookings/public/{plain.Token}/services/{svc.Id}/quote", new PublicServiceQuoteInput(day, 720, 2, [], null, null, null)), 200);
        var key = Guid.NewGuid();
        await v.Check("POST", "/api/stays/bookings/public/{token}/sessions", await AddSessionAsync(plain.Token, SessionInput(svc.Id, day, 720, 2, 4000, key: key)), 201);
        await v.Check("POST", "/api/stays/bookings/public/{token}/sessions", await AddSessionAsync(plain.Token, SessionInput(svc.Id, day, 720, 2, 4000, key: key)), 200);
        await v.Check("POST", "/api/stays/bookings/public/{token}/sessions", await AddSessionAsync(plain.Token, SessionInput(svc.Id, day, 720, 2, 4000)), 409);
        var sessionId = (await GetPublicBookingAsync(plain.Token)).Sessions!.Single().Id;
        await v.Check("POST", "/api/stays/bookings/public/{token}/sessions/{sessionId}/cancel", await anon.PostJsonAsync($"/api/stays/bookings/public/{plain.Token}/sessions/{sessionId}/cancel", new { }), 200);
        await v.Check("POST", "/api/stays/bookings/public/{token}/sessions/{sessionId}/cancel", await anon.PostJsonAsync($"/api/stays/bookings/public/{plain.Token}/sessions/{sessionId}/cancel", new { }), 409);

        v.Items.Should().BeEmpty("ответы сеансов в брони должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    // ── кабинет ──────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-133")]
    public async Task CabinetServiceRoutes_MatchContract()
    {
        var v = new Violations();
        var company = await CreateStaysCompanyAsync();
        var c = AuthedClient(company.OwnerToken);
        var b = $"/api/stays/companies/{company.Id}";
        await EnableOrdersWithoutStayAsync(company);

        await v.Check("GET", "/api/stays/companies/{companyId}", await c.GetAsync(b), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/settings", await c.PutJsonAsync(b + "/settings", (await GetCompanyAsync(company)).Settings!), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/services", await c.GetAsync(b + "/services"), 200);
        var created = await c.PostJsonAsync(b + "/services", new ServiceCreateInput("Баня"));
        await v.Check("POST", "/api/stays/companies/{companyId}/services", created, 201);
        var svc = (await created.Content.ReadJsonAsync<ServiceManageDto>())!;
        var sp = $"{b}/services/{svc.Id}";
        await v.Check("GET", "/api/stays/companies/{companyId}/services/{serviceId}", await c.GetAsync(sp), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/setup",
            await c.PutJsonAsync(sp + "/setup", new ServiceSetupInput("Баня", Unique("svc-").ToLowerInvariant(), 2, 6, 60, 30, true, 60, 30, StayServiceCancellationPolicy.PreparationCosts, 12, true)), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/content", await c.PutJsonAsync(sp + "/content", new ServiceContentInput("Описание бани")), 200);
        var photo = await c.PostAsync(sp + "/photos", FileContent(TestImages.SolidJpeg(300, 200), "image/jpeg", "p.jpg"));
        await v.Check("POST", "/api/stays/companies/{companyId}/services/{serviceId}/photos", photo, 201);
        var photoDto = (await photo.Content.ReadJsonAsync<ServicePhotoDto>())!;
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/photos/order", await c.PutJsonAsync(sp + "/photos/order", new IdsOrderInput([photoDto.Id])), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/services/{serviceId}/weekly-schedule", await c.GetAsync(sp + "/weekly-schedule"), 200);
        var days = Enumerable.Range(1, 7).Select(d => new WeeklyDayInput(d, d == 6 ? [] : [new ServiceWindowInput(480, 1560)])).ToList();
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/weekly-schedule", await c.PutJsonAsync(sp + "/weekly-schedule", new WeeklyScheduleInput(days)), 200);
        var rule = await c.PostJsonAsync(sp + "/price-rules", new PriceRuleInput(127, 6, 30, 2000));
        await v.Check("POST", "/api/stays/companies/{companyId}/services/{serviceId}/price-rules", rule, 201);
        var ruleId = (await rule.Content.ReadJsonAsync<PriceRulesDto>())!.Rules.Single().Id;
        await v.Check("POST", "/api/stays/companies/{companyId}/services/{serviceId}/price-rules", await c.PostJsonAsync(sp + "/price-rules", new PriceRuleInput(1, 8, 10, 100)), 409);
        await v.Check("GET", "/api/stays/companies/{companyId}/services/{serviceId}/price-rules", await c.GetAsync(sp + "/price-rules"), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/price-rules/{ruleId}", await c.PutJsonAsync($"{sp}/price-rules/{ruleId}", new PriceRuleInput(127, 6, 30, 2500)), 200);
        var item = await c.PostJsonAsync(sp + "/items", new ServiceItemInput("Веник", 300, 5, true));
        await v.Check("POST", "/api/stays/companies/{companyId}/services/{serviceId}/items", item, 201);
        var itemDto = (await item.Content.ReadJsonAsync<ServiceItemDto>())!;
        await v.Check("GET", "/api/stays/companies/{companyId}/services/{serviceId}/items", await c.GetAsync(sp + "/items"), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/items/{itemId}", await c.PutJsonAsync($"{sp}/items/{itemDto.Id}", new ServiceItemInput("Веник", 350, 5, true)), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/items/order", await c.PutJsonAsync(sp + "/items/order", new IdsOrderInput([itemDto.Id])), 200);
        var month = InDays(10);
        await v.Check("GET", "/api/stays/companies/{companyId}/services/{serviceId}/date-overrides", await c.GetAsync($"{sp}/date-overrides?month={month:yyyy-MM}"), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/date-overrides/{date}", await c.PutJsonAsync($"{sp}/date-overrides/{D(month)}", new DateOverrideInput(false, [new ServiceWindowInput(600, 900)], "Праздник")), 200);
        await v.Check("DELETE", "/api/stays/companies/{companyId}/services/{serviceId}/date-overrides/{date}", await c.DeleteAsync($"{sp}/date-overrides/{D(month)}"), 200);
        await v.Check("POST", "/api/stays/companies/{companyId}/services/{serviceId}/publish", await c.PostJsonAsync(sp + "/publish", new EmptyInput()), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/services/{serviceId}/starts", await c.GetAsync($"{sp}/starts?date={D(InDays(9))}"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/services/{serviceId}/availability", await c.GetAsync($"{sp}/availability?from={D(InDays(9))}&days=5"), 200);
        await v.Check("POST", "/api/stays/companies/{companyId}/services/{serviceId}/unpublish", await c.PostJsonAsync(sp + "/unpublish", new EmptyInput()), 200);
        await v.Check("POST", "/api/stays/companies/{companyId}/services/{serviceId}/publish", await c.PostJsonAsync(sp + "/publish", new EmptyInput()), 200);
        var second = (await (await c.PostJsonAsync(b + "/services", new ServiceCreateInput("Чан"))).Content.ReadJsonAsync<ServiceManageDto>())!;
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/order", await c.PutJsonAsync(b + "/services/order", new IdsOrderInput([second.Id, svc.Id])), 200);
        // 409 ServiceHasSessions и удаление
        await OrderOkAsync(svc.Id, InDays(12), 720, 2);
        await v.Check("DELETE", "/api/stays/companies/{companyId}/services/{serviceId}", await c.DeleteAsync(sp), 409);
        await v.Check("POST", "/api/stays/companies/{companyId}/services/{serviceId}/archive", await c.PostJsonAsync(sp + "/archive", new EmptyInput()), 200);
        await v.Check("POST", "/api/stays/companies/{companyId}/services/{serviceId}/publish", await c.PostJsonAsync(sp + "/publish", new EmptyInput()), 409);
        await v.Check("DELETE", "/api/stays/companies/{companyId}/services/{serviceId}/photos/{photoId}", await c.DeleteAsync($"{sp}/photos/{photoDto.Id}"), 204);
        await v.Check("DELETE", "/api/stays/companies/{companyId}/services/{serviceId}/items/{itemId}", await c.DeleteAsync($"{sp}/items/{itemDto.Id}"), 204);
        await v.Check("DELETE", "/api/stays/companies/{companyId}/services/{serviceId}", await c.DeleteAsync($"{b}/services/{second.Id}"), 204);

        v.Items.Should().BeEmpty("ответы кабинета услуг должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    [Fact, TestCase("CY39-134")]
    public async Task CabinetSessionRoutes_ServiceDay_BoardSchedule_MatchContract()
    {
        var v = new Violations();
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        var house = await CreateHouseAsync(company, price: 3000);
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: 30);
        var broom = await AddItemAsync(company, svc.Id, "Веник", 300, 5);
        var c = AuthedClient(company.OwnerToken);
        var b = $"/api/stays/companies/{company.Id}";
        var date = InDays(10);

        var order = await OrderOkAsync(svc.Id, date, 720, 2, items: [new ItemSelectionInput(broom.Id, 1)]);
        var sessionId = await SessionIdOfOrderAsync(order.Token);
        await v.Check("GET", "/api/stays/companies/{companyId}/service-day", await c.GetAsync($"{b}/service-day?date={D(date)}"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/service-day", await c.GetAsync($"{b}/service-day"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/service-sessions", await c.GetAsync($"{b}/service-sessions?status=Held&status=Confirmed"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/service-sessions", await c.GetAsync($"{b}/service-sessions?from={D(date)}&to={D(date)}"), 200);
        await v.Check("POST", "/api/stays/companies/{companyId}/service-sessions/quote",
            await c.PostJsonAsync(b + "/service-sessions/quote", new StaffServiceQuoteInput(svc.Id, date, 1080, 2, [], null)), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/service-sessions/{sessionId}", await c.GetAsync($"{b}/service-sessions/{sessionId}"), 200);
        await AttachOrderProofAsync(order.Token);
        var card = await SessionCardAsync(company, sessionId);
        await v.Check("GET", "/api/stays/companies/{companyId}/service-sessions/{sessionId}", await c.GetAsync($"{b}/service-sessions/{sessionId}"), 200);
        var proofId = card.PaymentProofs.First().Id;
        (await c.GetAsync($"{b}/service-sessions/{sessionId}/payment-proofs/{proofId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        await v.Check("POST", "/api/stays/companies/{companyId}/service-sessions/{sessionId}/confirm-payment", await SessionActionAsync(company, sessionId, "confirm-payment", card.Version + 9), 409);
        await v.Check("POST", "/api/stays/companies/{companyId}/service-sessions/{sessionId}/confirm-payment", await SessionActionAsync(company, sessionId, "confirm-payment", card.Version), 200);
        var confirmed = await SessionCardAsync(company, sessionId);
        await v.Check("POST", "/api/stays/companies/{companyId}/service-sessions/{sessionId}/cancel", await SessionActionAsync(company, sessionId, "cancel", confirmed.Version, "Авария"), 200);

        var order2 = await OrderOkAsync(svc.Id, date, 1080, 2);
        await AttachOrderProofAsync(order2.Token);
        var s2 = await SessionIdOfOrderAsync(order2.Token);
        await v.Check("POST", "/api/stays/companies/{companyId}/service-sessions/{sessionId}/reject-payment", await SessionActionAsync(company, s2, "reject-payment", (await SessionCardAsync(company, s2)).Version, "Не пришло"), 200);

        // ручной сеанс
        await v.Check("POST", "/api/stays/companies/{companyId}/service-sessions",
            await c.PostJsonAsync(b + "/service-sessions", new ManualServiceOrderInput(svc.Id, date, 840, 2, [], "Свои", null, null, StayServiceRequestBasis.Phone, Guid.NewGuid())), 201);
        await v.Check("POST", "/api/stays/companies/{companyId}/service-sessions",
            await c.PostJsonAsync(b + "/service-sessions", new ManualServiceOrderInput(svc.Id, date, 840, 2, [], "Свои", null, null, StayServiceRequestBasis.Phone, Guid.NewGuid())), 409);

        // сеанс в брони: добавление персоналом, шахматка, график, карточка брони
        var booked = await BookOkAsync(house.Id, date.AddDays(1), date.AddDays(3));
        var bookingId = await BookingIdAsync(booked.Token);
        await v.Check("POST", "/api/stays/companies/{companyId}/bookings/{bookingId}/sessions",
            await c.PostJsonAsync($"{b}/bookings/{bookingId}/sessions", new StaffAddSessionInput(svc.Id, date.AddDays(2), 720, 2, [], StayServiceRequestBasis.InPerson, Guid.NewGuid())), 201);
        await v.Check("POST", "/api/stays/companies/{companyId}/bookings/{bookingId}/sessions",
            await c.PostJsonAsync($"{b}/bookings/{bookingId}/sessions", new StaffAddSessionInput(svc.Id, date.AddDays(2), 720, 2, [], StayServiceRequestBasis.InPerson, Guid.NewGuid())), 409);
        await v.Check("GET", "/api/stays/companies/{companyId}/bookings/{bookingId}", await c.GetAsync($"{b}/bookings/{bookingId}"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/board", await c.GetAsync($"{b}/board?from={D(date)}&days=7"), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/schedule", await c.GetAsync($"{b}/schedule?from={D(date)}&days=7"), 200);
        await v.Check("GET", "/api/stays/bookings/public/{token}", await AnonymousClient().GetAsync($"/api/stays/bookings/public/{booked.Token}"), 200);

        v.Items.Should().BeEmpty("ответы сеансов кабинета должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    [Fact, TestCase("CY39-135")]
    public async Task ArrivalReminderRoutes_MatchContract()
    {
        var v = new Violations();
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var c = AuthedClient(company.OwnerToken);
        var u = $"/api/stays/companies/{company.Id}/arrival-reminder";
        await v.Check("GET", "/api/stays/companies/{companyId}/arrival-reminder", await c.GetAsync(u), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/arrival-reminder",
            await c.PutJsonAsync(u, new ArrivalReminderInput("19:30", "Ждём вас в «{Дом}»! Задать вопрос — пишите", true, false, "v1", "v1")), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/arrival-reminder", await c.GetAsync(u), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/arrival-reminder",
            await c.PutJsonAsync(u, new ArrivalReminderInput("19:30", "Код 1234", true, false, "v1", "v1")), 409);
        await v.Check("PUT", "/api/stays/companies/{companyId}/arrival-reminder",
            await c.PutJsonAsync(u, new ArrivalReminderInput("19:30", "Код 1234", true, true, "v1", "v1")), 200);
        await v.Check("POST", "/api/stays/companies/{companyId}/arrival-reminder/preview",
            await c.PostJsonAsync(u + "/preview", new ArrivalReminderPreviewInput("{ИмяГостя}\nЖдём в «{Дом}» — 1234", true, null)), 200);
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12));
        await v.Check("POST", "/api/stays/companies/{companyId}/arrival-reminder/preview",
            await c.PostJsonAsync(u + "/preview", new ArrivalReminderPreviewInput(null, false, await BookingIdAsync(booked.Token))), 200);
        await v.Check("GET", "/api/stays/companies/{companyId}/arrival-reminder/history", await c.GetAsync(u + "/history"), 200);
        (await c.PutJsonAsync(u, new ArrivalReminderInput("07:00", null, false, false, "v1", null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        v.Items.Should().BeEmpty("ответы напоминания должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }
}
