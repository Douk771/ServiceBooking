using System.Net;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Контрактная проверка цикла 42: реальные ответы «Бань» и изменённых маршрутов «Домов» сверяются со схемой <c>contracts/cycle42/openapi.json</c>
/// (API_CONTRACT_CYCLE42.md, ARCHITECTURE_CYCLE42.md §42.15.1 BE-42-7). Только ФОРМА ответа: имя поля, обязательность, формат, nullable; валидатор строгий (лишнее поле — нарушение),
/// кроме DTO, которые контракт объявляет частичными (<c>additionalProperties: true</c>). Расхождения собираются списком по всем маршрутам.
/// Отдельно — закрытая форма расписания банщика: ровно перечисленные поля, без телефонов, сумм и реквизитов.
/// </summary>
public class Cycle42ContractTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private static readonly OpenApiContract C42 = OpenApiContract.Load("cycle42");

    /// <summary>
    /// Известное расхождение контракта и реализации (долг C42-1 в CURRENT_STATE.md): <c>BathsConflictDto</c> описан как {code, message}, а сервер переиспользует
    /// <c>StaysConflictDto</c> и пишет ещё <c>conflictingPeriod</c> и <c>conflicts</c> (у slug-check они всегда null). Контракт архитектора здесь не правится — расхождение
    /// допущено явно и узко (только эти два поля внутри <c>$.conflict</c>), а не молча; остальные нарушения формы по-прежнему валят тест.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex KnownConflictDtoExtras =
        new(@"^\$\.conflict\.(conflictingPeriod|conflicts): property is not described by the schema$");

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
            Items.AddRange(C42.Collect(method, path, status, body).Where(e => !KnownConflictDtoExtras.IsMatch(e)).Select(e => $"{method} {path} -> {status}: {e}"));
        }
    }

    // ── гость ────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY42-130")]
    public async Task PublicRoutes_MatchContract()
    {
        var v = new Violations();
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c);
        InvalidateBathsCatalog();
        var anon = AnonymousClient();
        var date = InDays(9);

        await v.Check("GET", "/api/baths/catalog", await anon.GetAsync("/api/baths/catalog"), 200);
        await v.Check("GET", "/api/baths/catalog", await anon.GetAsync($"/api/baths/catalog?cityId={c.CityId}&date={D(date)}"), 200);
        await v.Check("GET", "/api/baths/catalog/cities", await anon.GetAsync("/api/baths/catalog/cities"), 200);
        await v.Check("GET", "/api/baths/public/companies/{slug}", await anon.GetAsync($"/api/baths/public/companies/{c.Slug}"), 200);
        await v.Check("GET", "/api/baths/public/companies/{slug}/services/{serviceSlug}", await anon.GetAsync($"/api/baths/public/companies/{c.Slug}/services/{res.Slug}"), 200);
        await v.Check("GET", "/api/baths/public/services/{serviceId}/availability", await anon.GetAsync($"/api/baths/public/services/{res.Id}/availability?from={D(date)}&days=7"), 200);
        await v.Check("GET", "/api/baths/public/services/{serviceId}/starts", await anon.GetAsync($"/api/baths/public/services/{res.Id}/starts?date={D(date)}"), 200);
        await v.Check("POST", "/api/baths/public/services/{serviceId}/quote", await BathQuoteAsync(res.Id, date, 720, 2), 200);

        var phone = UniquePhone();
        var key = Guid.NewGuid();
        var body = BathOrderBody(date, 720, 2, 3, 2 * HourPrice, phone, key);
        var created = await anon.PostJsonAsync($"/api/baths/public/services/{res.Id}/orders", body);
        await v.Check("POST", "/api/baths/public/services/{serviceId}/orders", created, 201);
        await v.Check("POST", "/api/baths/public/services/{serviceId}/orders", await anon.PostJsonAsync($"/api/baths/public/services/{res.Id}/orders", body), 200); // повтор: тот же ключ
        await v.Check("POST", "/api/baths/public/services/{serviceId}/orders",
            await anon.PostJsonAsync($"/api/baths/public/services/{res.Id}/orders", BathOrderBody(date, 720, 2, 3, 2 * HourPrice, UniquePhone())), 409); // SlotTaken
        var token = (await J(created)).GetProperty("token").GetString()!;

        await v.Check("GET", "/api/baths/service-orders/public/{token}", await anon.GetAsync($"/api/baths/service-orders/public/{token}"), 200);
        await v.Check("POST", "/api/baths/service-orders/public/{token}/payment-proofs",
            await anon.PostAsync($"/api/baths/service-orders/public/{token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "check.jpg")), 201);
        await v.Check("GET", "/api/baths/service-orders/public/{token}", await anon.GetAsync($"/api/baths/service-orders/public/{token}"), 200); // ждёт проверки оплаты
        var subscribed = await anon.PostJsonAsync($"/api/baths/service-orders/public/{token}/push-subscription",
            new PushSubscriptionInput($"https://push.example.test/cy42-contract/{Guid.NewGuid():N}", new PushKeysInput("k1", "k2"), null));
        new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict }.Should().Contain(subscribed.StatusCode, "в тестовом хосте web-push выключен — 409 (строкой) допустим");
        await v.Check("POST", "/api/baths/service-orders/public/{token}/cancel", await anon.PostJsonAsync($"/api/baths/service-orders/public/{token}/cancel", new { }), 200);
        await v.Check("POST", "/api/baths/service-orders/public/{token}/cancel", await anon.PostJsonAsync($"/api/baths/service-orders/public/{token}/cancel", new { }), 409);

        var mine = await AuthedClient(c.Owner.Token).GetAsync("/api/baths/service-orders/my");
        await v.Check("GET", "/api/baths/service-orders/my", mine, 200);

        v.Items.Should().BeEmpty("публичные ответы «Бань» должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    // ── кабинет ──────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY42-131")]
    public async Task CabinetCompanyRoutes_MatchContract()
    {
        var v = new Violations();
        var c = await CreateBathAsync();
        var client = AuthedClient(c.Token);
        var b = $"/api/baths/companies/{c.CompanyId}";

        await v.Check("GET", "/api/baths/companies/my", await AuthedClient(c.Owner.Token).GetAsync("/api/baths/companies/my"), 200);
        await v.Check("GET", "/api/baths/slug-check", await client.GetAsync($"/api/baths/slug-check?slug={Unique("fr-").ToLowerInvariant()}"), 200);
        await v.Check("GET", "/api/baths/slug-check", await client.GetAsync("/api/baths/slug-check?slug=login"), 200);
        await v.Check("GET", "/api/baths/trial", await AuthedClient(c.Owner.Token).GetAsync("/api/baths/trial"), 200);
        var company = await client.GetAsync(b);
        await v.Check("GET", "/api/baths/companies/{companyId}", company, 200);
        var settings = (await J(company)).GetProperty("settings").Clone();
        await v.Check("PUT", "/api/baths/companies/{companyId}/settings", await client.PutJsonAsync(b + "/settings", settings), 200);
        await v.Check("PUT", "/api/baths/companies/{companyId}/payment-details", await client.PutJsonAsync(b + "/payment-details", new PaymentDetailsDto(PaymentDetailsText, "Бронь бани")), 200);
        await v.Check("PUT", "/api/baths/companies/{companyId}/provider",
            await client.PutJsonAsync(b + "/provider", new ProviderInput(StayProviderStatus.SelfEmployed, "Иванов Иван Иванович", ValidPersonInn, null, "г. Новокузнецк, ул. Мира, 1")), 200);
        await v.Check("PUT", "/api/baths/companies/{companyId}/slug", await client.PutJsonAsync(b + "/slug", new SlugInput(Unique("ns-").ToLowerInvariant())), 200);
        var notificationSettings = await client.GetAsync(b + "/notification-settings");
        await v.Check("GET", "/api/baths/companies/{companyId}/notification-settings", notificationSettings, 200);
        var notificationJson = await J(notificationSettings);
        foreach (var field in new[] { "messengerAvailable", "messagingActive", "deliveryChoiceVisible", "priorityWarning" })
            notificationJson.TryGetProperty(field, out _).Should().BeTrue($"ответ notification-settings «Бань» содержит поле цикла 40 {field}");
        await v.Check("PUT", "/api/baths/companies/{companyId}/notification-settings",
            await client.PutJsonAsync(b + "/notification-settings", new StaysNotificationSettingsInput(false, false, true, false, null, null)), 200);
        await v.Check("GET", "/api/baths/companies/{companyId}/revision", await client.GetAsync(b + "/revision"), 200);
        await v.Check("GET", "/api/baths/companies/{companyId}/schedule", await client.GetAsync($"{b}/schedule?from={D(InDays(1))}&days=3"), 200);
        await v.Check("GET", "/api/companies/kinds-summary", await AuthedClient(c.Owner.Token).GetAsync("/api/companies/kinds-summary"), 200);

        v.Items.Should().BeEmpty("ответы кабинета компании «Бани» должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    [Fact, TestCase("CY42-132")]
    public async Task CabinetResourceAndSessionRoutes_MatchContract()
    {
        var v = new Violations();
        var c = await CreateBathAsync();
        var client = AuthedClient(c.Token);
        var b = $"/api/baths/companies/{c.CompanyId}";
        var date = InDays(9);

        await v.Check("GET", "/api/baths/companies/{companyId}/services", await client.GetAsync(b + "/services"), 200);
        var created = await client.PostJsonAsync(b + "/services", new ServiceCreateInput("Русская баня"));
        await v.Check("POST", "/api/baths/companies/{companyId}/services", created, 201);
        var svc = (await created.Content.ReadJsonAsync<ServiceManageDto>())!;
        var sp = $"{b}/services/{svc.Id}";
        await v.Check("GET", "/api/baths/companies/{companyId}/services/{serviceId}", await client.GetAsync(sp), 200);
        await v.Check("PUT", "/api/baths/companies/{companyId}/services/{serviceId}/setup",
            await client.PutJsonAsync(sp + "/setup", new ServiceSetupInput("Русская баня", Unique("res-").ToLowerInvariant(), 1, 6, 30, 30, false, 0, 30, StayServiceCancellationPolicy.NoDeductions, 12, false, Capacity: 6)), 200);
        await v.Check("PUT", "/api/baths/companies/{companyId}/services/{serviceId}/content", await client.PutJsonAsync(sp + "/content", new ServiceContentInput("Парная на дровах, веники в наличии")), 200);
        var photo = await client.PostAsync(sp + "/photos", FileContent(TestImages.SolidJpeg(300, 200), "image/jpeg", "p.jpg"));
        await v.Check("POST", "/api/baths/companies/{companyId}/services/{serviceId}/photos", photo, 201);
        var photoDto = (await photo.Content.ReadJsonAsync<ServicePhotoDto>())!;
        await v.Check("PUT", "/api/baths/companies/{companyId}/services/{serviceId}/photos/order", await client.PutJsonAsync(sp + "/photos/order", new IdsOrderInput([photoDto.Id])), 200);
        await v.Check("GET", "/api/baths/companies/{companyId}/services/{serviceId}/weekly-schedule", await client.GetAsync(sp + "/weekly-schedule"), 200);
        var days = Enumerable.Range(1, 7).Select(d => new WeeklyDayInput(d, [new ServiceWindowInput(WideWindow.Start, WideWindow.End)])).ToList();
        await v.Check("PUT", "/api/baths/companies/{companyId}/services/{serviceId}/weekly-schedule", await client.PutJsonAsync(sp + "/weekly-schedule", new WeeklyScheduleInput(days)), 200);
        var rule = await client.PostJsonAsync(sp + "/price-rules", new PriceRuleInput(127, 6, 30, HourPrice));
        await v.Check("POST", "/api/baths/companies/{companyId}/services/{serviceId}/price-rules", rule, 201);
        var ruleId = (await rule.Content.ReadJsonAsync<PriceRulesDto>())!.Rules.Single().Id;
        await v.Check("GET", "/api/baths/companies/{companyId}/services/{serviceId}/price-rules", await client.GetAsync(sp + "/price-rules"), 200);
        await v.Check("PUT", "/api/baths/companies/{companyId}/services/{serviceId}/price-rules/{ruleId}", await client.PutJsonAsync($"{sp}/price-rules/{ruleId}", new PriceRuleInput(127, 6, 30, HourPrice + 100)), 200);
        var item = await client.PostJsonAsync(sp + "/items", new ServiceItemInput("Веник", 300, 5, true));
        await v.Check("POST", "/api/baths/companies/{companyId}/services/{serviceId}/items", item, 201);
        var itemDto = (await item.Content.ReadJsonAsync<ServiceItemDto>())!;
        await v.Check("GET", "/api/baths/companies/{companyId}/services/{serviceId}/items", await client.GetAsync(sp + "/items"), 200);
        await v.Check("PUT", "/api/baths/companies/{companyId}/services/{serviceId}/items/{itemId}", await client.PutJsonAsync($"{sp}/items/{itemDto.Id}", new ServiceItemInput("Веник", 350, 5, true)), 200);
        await v.Check("PUT", "/api/baths/companies/{companyId}/services/{serviceId}/items/order", await client.PutJsonAsync(sp + "/items/order", new IdsOrderInput([itemDto.Id])), 200);
        await v.Check("GET", "/api/baths/companies/{companyId}/services/{serviceId}/date-overrides", await client.GetAsync($"{sp}/date-overrides?month={date:yyyy-MM}"), 200);
        await v.Check("PUT", "/api/baths/companies/{companyId}/services/{serviceId}/date-overrides/{date}",
            await client.PutJsonAsync($"{sp}/date-overrides/{D(date)}", new DateOverrideInput(false, [new ServiceWindowInput(600, 900)], "Праздник")), 200);
        await v.Check("DELETE", "/api/baths/companies/{companyId}/services/{serviceId}/date-overrides/{date}", await client.DeleteAsync($"{sp}/date-overrides/{D(date)}"), 200);
        await v.Check("POST", "/api/baths/companies/{companyId}/services/{serviceId}/publish", await client.PostJsonAsync(sp + "/publish", new { }), 200);
        await v.Check("GET", "/api/baths/companies/{companyId}/services/{serviceId}/starts", await client.GetAsync($"{sp}/starts?date={D(date)}"), 200);
        await v.Check("GET", "/api/baths/companies/{companyId}/services/{serviceId}/availability", await client.GetAsync($"{sp}/availability?from={D(date)}&days=5"), 200);
        await v.Check("POST", "/api/baths/companies/{companyId}/services/{serviceId}/unpublish", await client.PostJsonAsync(sp + "/unpublish", new { }), 200);
        await v.Check("POST", "/api/baths/companies/{companyId}/services/{serviceId}/publish", await client.PostJsonAsync(sp + "/publish", new { }), 200);
        var second = (await (await client.PostJsonAsync(b + "/services", new ServiceCreateInput("Чан"))).Content.ReadJsonAsync<ServiceManageDto>())!;
        await v.Check("PUT", "/api/baths/companies/{companyId}/services/order", await client.PutJsonAsync(b + "/services/order", new IdsOrderInput([second.Id, svc.Id])), 200);

        // сеансы и «День услуг»
        var quoteBody = new { serviceId = svc.Id, businessDate = date, startMinute = 720, hours = 2, items = Array.Empty<object>() };
        await v.Check("POST", "/api/baths/companies/{companyId}/service-sessions/quote", await client.PostJsonAsync(b + "/service-sessions/quote", quoteBody), 200);
        var manual = await client.PostJsonAsync(b + "/service-sessions", new
        {
            serviceId = svc.Id, businessDate = date, startMinute = 720, hours = 2, items = Array.Empty<object>(), guestName = "Пётр", guestPhone = (string?)null, comment = (string?)null,
            requestBasis = "Phone", idempotencyKey = Guid.NewGuid(), guestsCount = 3
        });
        await v.Check("POST", "/api/baths/companies/{companyId}/service-sessions", manual, 201);
        var sessionId = (await J(manual)).GetProperty("id").GetGuid();
        await v.Check("GET", "/api/baths/companies/{companyId}/service-sessions", await client.GetAsync(b + "/service-sessions"), 200);
        await v.Check("GET", "/api/baths/companies/{companyId}/service-sessions/{sessionId}", await client.GetAsync($"{b}/service-sessions/{sessionId}"), 200);
        await v.Check("GET", "/api/baths/companies/{companyId}/service-day", await client.GetAsync($"{b}/service-day?date={D(date)}"), 200);
        var version = (await J(await client.GetAsync($"{b}/service-sessions/{sessionId}"))).GetProperty("version").GetInt32();
        await v.Check("POST", "/api/baths/companies/{companyId}/service-sessions/{sessionId}/cancel",
            await client.PostJsonAsync($"{b}/service-sessions/{sessionId}/cancel", new { expectedVersion = version, reason = "Тест контракта" }), 200);

        // после публикации и сеансов — удаление и архив
        await v.Check("DELETE", "/api/baths/companies/{companyId}/services/{serviceId}/photos/{photoId}", await client.DeleteAsync($"{sp}/photos/{photoDto.Id}"), 204);
        await v.Check("DELETE", "/api/baths/companies/{companyId}/services/{serviceId}/items/{itemId}", await client.DeleteAsync($"{sp}/items/{itemDto.Id}"), 204);
        await v.Check("DELETE", "/api/baths/companies/{companyId}/services/{serviceId}/price-rules/{ruleId}", await client.DeleteAsync($"{sp}/price-rules/{ruleId}"), 200);
        await v.Check("POST", "/api/baths/companies/{companyId}/services/{serviceId}/archive", await client.PostJsonAsync($"{b}/services/{second.Id}/archive", new { }), 200);

        v.Items.Should().BeEmpty("ответы кабинета ресурсов и сеансов «Бань» должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    // ── изменённые маршруты «Домов» ──────────────────────────────────────────────

    [Fact, TestCase("CY42-133")]
    public async Task ChangedStaysServiceRoutes_MatchCycle42Contract()
    {
        var v = new Violations();
        var company = await CreateStaysCompanyAsync();
        var c = AuthedClient(company.OwnerToken);
        var sp = $"/api/stays/companies/{company.Id}/services";
        var svc = (await (await c.PostJsonAsync(sp, new ServiceCreateInput("Баня"))).Content.ReadJsonAsync<ServiceManageDto>())!;
        var one = $"{sp}/{svc.Id}";

        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/setup",
            await c.PutJsonAsync(one + "/setup", new ServiceSetupInput("Баня", Unique("svc-").ToLowerInvariant(), 2, 6, 60, 30, true, 60, 30, StayServiceCancellationPolicy.PreparationCosts, 12, true)), 200);
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/content", await c.PutJsonAsync(one + "/content", new ServiceContentInput("Описание бани")), 200);
        var item = await c.PostJsonAsync(one + "/items", new ServiceItemInput("Веник", 300, 5, true));
        await v.Check("POST", "/api/stays/companies/{companyId}/services/{serviceId}/items", item, 201);
        var itemDto = (await item.Content.ReadJsonAsync<ServiceItemDto>())!;
        await v.Check("PUT", "/api/stays/companies/{companyId}/services/{serviceId}/items/{itemId}", await c.PutJsonAsync($"{one}/items/{itemDto.Id}", new ServiceItemInput("Веник", 350, 5, true)), 200);

        v.Items.Should().BeEmpty("изменённые маршруты услуг «Домов» должны совпадать со схемой cycle42:\n" + string.Join("\n", v.Items));
    }

    // ── закрытая форма расписания банщика ────────────────────────────────────────

    [Fact, TestCase("CY42-134")]
    public async Task Schedule_IsClosedShape_MatchesContract_AndLeaksNoPhonesMoneyOrRequisites()
    {
        var v = new Violations();
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c);
        var date = InDays(5);
        var phone = UniquePhone();
        var token = await BookBathAsync(res.Id, date, 720, 2, 3, phone);
        _ = token;

        var r = await AuthedClient(c.Token).GetAsync($"/api/baths/companies/{c.CompanyId}/schedule?from={D(date)}&days=2");
        await v.Check("GET", "/api/baths/companies/{companyId}/schedule", r, 200);
        v.Items.Should().BeEmpty("расписание должно совпадать со схемой BathScheduleDto:\n" + string.Join("\n", v.Items));

        var raw = await r.Content.ReadAsStringAsync();
        raw.Should().NotContain(phone.TrimStart('+')).And.NotContainEquivalentOf("phone").And.NotContainEquivalentOf("price").And.NotContainEquivalentOf("total").And.NotContain(PaymentDetailsText);
        var json = JsonDocument.Parse(raw).RootElement;
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("today", "days");
        var day = json.GetProperty("days").EnumerateArray().First();
        day.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("date", "label", "sessions");
    }
}
