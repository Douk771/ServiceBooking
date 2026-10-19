using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Контрактная проверка цикла 43 (CY43-80): реальные ответы новых и изменённых маршрутов опций мессенджеров сверяются со схемой <c>contracts/cycle43/openapi.json</c>
/// (API_CONTRACT_CYCLE43.md, ARCHITECTURE_CYCLE43.md). Только ФОРМА: имя поля, обязательность, формат, nullable; тела осознанных 400/409 — голая строка, только код и текст.
/// Расхождения собираются списком по всем маршрутам.
/// </summary>
public class Cycle43ContractTests(TestDatabaseFixture fixture) : NotificationTestBase(fixture)
{
    private static readonly OpenApiContract C43 = OpenApiContract.Load("cycle43");

    private sealed class Violations
    {
        public readonly List<string> Items = [];

        /// <summary>JSON-ответ: код + форма по схеме.</summary>
        public async Task Check(string method, string path, HttpResponseMessage response, int status)
        {
            var text = await response.Content.ReadAsStringAsync();
            ((int)response.StatusCode).Should().Be(status, $"{method} {path}: {text}");
            JsonElement body;
            try { body = JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException) { Items.Add($"{method} {path} -> {status}: тело не JSON: {text}"); return; }
            Items.AddRange(C43.Collect(method, path, status, body).Select(e => $"{method} {path} -> {status}: {e}"));
        }

        /// <summary>Осознанный 400/409: голая строка по-русски (не JSON), либо пустое тело для 401/403/404.</summary>
        public async Task CheckText(string method, string path, HttpResponseMessage response, int status, bool empty = false)
        {
            var text = await response.Content.ReadAsStringAsync();
            ((int)response.StatusCode).Should().Be(status, $"{method} {path}: {text}");
            if (empty) { if (text.Length > 0) Items.Add($"{method} {path} -> {status}: ожидалось пустое тело, получено: {text}"); return; }
            if (string.IsNullOrWhiteSpace(text)) Items.Add($"{method} {path} -> {status}: пустое тело вместо строки");
            else if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('"')) Items.Add($"{method} {path} -> {status}: ожидалась голая строка, получено: {text}");
        }
    }

    [Fact, TestCase("CY43-80")]
    public async Task MessengerRoutes_MatchContract()
    {
        var v = new Violations();
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        Guid accountId;
        using (var scope = Factory.Services.CreateScope())
            accountId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BillingAccounts
                .Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync();
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var ownerClient = AuthedClient(owner.Token);

        // ── экран цены ───────────────────────────────────────────────────────────
        await v.Check("GET", "/api/admin/messenger-options", await admin.GetAsync("/api/admin/messenger-options"), 200);
        await v.CheckText("GET", "/api/admin/messenger-options", await ownerClient.GetAsync("/api/admin/messenger-options"), 403, empty: true);
        await v.CheckText("GET", "/api/admin/messenger-options", await Factory.CreateClient().GetAsync("/api/admin/messenger-options"), 401, empty: true);

        var preview = "/api/admin/messenger-options/{transport}/price/preview";
        await v.Check("POST", preview, await admin.PostAsJsonAsync("/api/admin/messenger-options/Max/price/preview", new { pricePerMonth = 490, isActive = (bool?)null }), 200);
        await v.Check("POST", preview, await admin.PostAsJsonAsync("/api/admin/messenger-options/Max/price/preview", new { pricePerMonth = (decimal?)null, isActive = false }), 200);
        await v.CheckText("POST", preview, await admin.PostAsJsonAsync("/api/admin/messenger-options/Max/price/preview", new { pricePerMonth = -1, isActive = (bool?)null }), 400);
        await v.CheckText("POST", preview, await admin.PostAsJsonAsync("/api/admin/messenger-options/Max/price/preview", new { pricePerMonth = 10.123, isActive = (bool?)null }), 400);
        await v.CheckText("POST", preview, await admin.PostAsJsonAsync("/api/admin/messenger-options/Telegram/price/preview", new { pricePerMonth = 490 }), 404, empty: true);

        var put = "/api/admin/messenger-options/{transport}/price";
        var current = await (await admin.GetAsync("/api/admin/messenger-options")).Content.ReadFromJsonAsync<JsonElement>();
        var max = current.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("transport").GetString() == "Max");
        var curPrice = max.GetProperty("pricePerMonth").ValueKind == JsonValueKind.Null ? (decimal?)null : max.GetProperty("pricePerMonth").GetDecimal();
        var curActive = max.GetProperty("isActive").GetBoolean();
        await v.CheckText("PUT", put, await admin.PutAsJsonAsync("/api/admin/messenger-options/Max/price",
            new { pricePerMonth = 490, isActive = true, comment = (string?)null, expectedPricePerMonth = curPrice, expectedIsActive = curActive }), 400);
        await v.Check("PUT", put, await admin.PutAsJsonAsync("/api/admin/messenger-options/Max/price",
            new { pricePerMonth = 490, isActive = true, comment = "QA контракт цикла 43", expectedPricePerMonth = curPrice, expectedIsActive = curActive }), 200);
        await v.CheckText("PUT", put, await admin.PutAsJsonAsync("/api/admin/messenger-options/Max/price",
            new { pricePerMonth = 600, isActive = true, comment = "гонка", expectedPricePerMonth = curPrice == 111m ? 112m : 111m, expectedIsActive = true }), 409);

        // ── аккаунт: расчёт, подключение, продление, отключение ─────────────────
        var acc = $"/api/admin/billing-accounts/{accountId}";
        var quote = "/api/admin/billing-accounts/{id}/messengers/{transport}/quote";
        await v.Check("GET", quote, await admin.GetAsync($"{acc}/messengers/Max/quote?months=3"), 200);
        await v.CheckText("GET", quote, await admin.GetAsync($"{acc}/messengers/Max/quote?months=25"), 400);
        await v.CheckText("GET", quote, await admin.GetAsync($"/api/admin/billing-accounts/{Guid.NewGuid()}/messengers/Max/quote?months=1"), 404, empty: true);

        var extend = "/api/admin/billing-accounts/{id}/messengers/{transport}/extend";
        await v.CheckText("POST", extend, await admin.PostAsJsonAsync($"{acc}/messengers/Max/extend", new { months = 0, comment = "x", expectedPricePerMonth = 490 }), 400);
        await v.CheckText("POST", extend, await admin.PostAsJsonAsync($"{acc}/messengers/Max/extend", new { months = 1, comment = "x", expectedPricePerMonth = 111 }), 409);
        await v.Check("POST", extend, await admin.PostAsJsonAsync($"{acc}/messengers/Max/extend", new { months = 3, comment = "Счёт 17", expectedPricePerMonth = 490 }), 200);
        await v.Check("POST", extend, await admin.PostAsJsonAsync($"{acc}/messengers/Max/extend", new { months = 1, comment = (string?)null, expectedPricePerMonth = 490 }), 200); // повтор — ещё одно продление

        await v.Check("GET", "/api/admin/billing-accounts/{id}", await admin.GetAsync(acc), 200);
        await v.CheckText("GET", "/api/admin/billing-accounts/{id}", await admin.GetAsync($"/api/admin/billing-accounts/{Guid.NewGuid()}"), 404, empty: true);

        var disable = "/api/admin/billing-accounts/{id}/messengers/{transport}/disable";
        await v.CheckText("POST", disable, await admin.PostAsJsonAsync($"{acc}/messengers/Max/disable", new { when = "PeriodEnd", comment = (string?)null }), 400);
        await v.Check("POST", disable, await admin.PostAsJsonAsync($"{acc}/messengers/Max/disable", new { when = "PeriodEnd", comment = "Просьба владельца" }), 200);
        await v.CheckText("POST", disable, await admin.PostAsJsonAsync($"{acc}/messengers/Max/disable", new { when = "PeriodEnd", comment = "ещё раз" }), 409);
        await v.Check("POST", disable, await admin.PostAsJsonAsync($"{acc}/messengers/Max/disable", new { when = "Now", comment = "сейчас" }), 200);
        await v.CheckText("POST", disable, await admin.PostAsJsonAsync($"{acc}/messengers/WhatsApp/disable", new { when = "Now", comment = "нечего" }), 409);

        // ── владелец ─────────────────────────────────────────────────────────────
        foreach (var line in new[] { null, "Services", "Orders", "Stays", "Baths" })
            await v.Check("GET", "/api/billing/subscription", await ownerClient.GetAsync("/api/billing/subscription" + (line is null ? "" : $"?line={line}")), 200);

        v.Items.Should().BeEmpty("ответы цикла 43 должны совпадать со схемой:\n" + string.Join("\n", v.Items));
    }

    [Fact, TestCase("CY43-80b")]
    public async Task ConfirmPayment_MatchesContract()
    {
        var v = new Violations();
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
var state = (await (await admin.GetAsync("/api/admin/messenger-options")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single(i => i.GetProperty("transport").GetString() == "Max");
        if (state.GetProperty("pricePerMonth").ValueKind == JsonValueKind.Null || state.GetProperty("pricePerMonth").GetDecimal() != 490m || !state.GetProperty("isActive").GetBoolean())
            (await admin.PutAsJsonAsync("/api/admin/messenger-options/Max/price",
                new { pricePerMonth = 490, isActive = true, comment = "QA CY43-80b",
                      expectedPricePerMonth = state.GetProperty("pricePerMonth").ValueKind == JsonValueKind.Null ? (decimal?)null : state.GetProperty("pricePerMonth").GetDecimal(),
                      expectedIsActive = state.GetProperty("isActive").GetBoolean() })).EnsureSuccessStatusCode();
        var termsVersion = Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>()
            .Current!.Get(LegalDocumentType.TermsOwner)!.Version;
        var created = await AuthedClient(owner.Token).PostAsJsonAsync("/api/notification-channels",
            new { legalEntityForm = "Company", inn = "7707083893", offerAccepted = new { version = termsVersion }, transport = "Max" });
        created.EnsureSuccessStatusCode();
        var channelId = (await created.Content.ReadJsonAsync<ChannelDto>())!.Id;
        var path = "/api/admin/notification-channels/{id}/confirm-payment";
        var url = $"/api/admin/notification-channels/{channelId}/confirm-payment";

        await v.CheckText("POST", path, await admin.PostAsJsonAsync(url, new { months = 13 }), 400);
        await v.CheckText("POST", path, await admin.PostAsJsonAsync(url, new { months = 1, expectedPricePerMonth = 111 }), 409);
        await v.Check("POST", path, await admin.PostAsJsonAsync(url, new { months = 1, comment = "Счёт 17", expectedPricePerMonth = 490 }), 200);
        await v.CheckText("POST", path, await admin.PostAsJsonAsync($"/api/admin/notification-channels/{Guid.NewGuid()}/confirm-payment", new { months = 1 }), 404, empty: true);

        v.Items.Should().BeEmpty("confirm-payment должен совпадать со схемой:\n" + string.Join("\n", v.Items));
    }
}
