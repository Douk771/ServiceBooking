using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.API.Services.Signals;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 48 (CY48-QA-*): functional scenarios written from SPEC.md US-48-04 and US-48-08 and LEGAL_REVIEW_CYCLE48.md, not from the code.
/// Expected texts are literals from the SPEC/legal review; the forbidden phrases are re-typed here rather than read from contracts/cycle48.
/// </summary>
public class Cycle48StorefrontQaTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // The previous seed, byte for byte (SPEC.md §2 «Сеть» «Записи»).
    private const string LegacyNetworkDescription = "Для сети от 4 филиалов. Стоимость от 3 900 ₽ в месяц, зависит от числа филиалов.";

    private static readonly Regex[] Forbidden =
    [
        new("от\\s?\\d[\\d\\s\\u00A0]*₽", RegexOptions.IgnoreCase),
        new("после заявки", RegexOptions.IgnoreCase),
        new("свяжемся с вами", RegexOptions.IgnoreCase),
        new("(?<!не )зависит от числа (филиалов|магазинов)", RegexOptions.IgnoreCase),
    ];

    private async Task ApplyCatalogAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TariffCatalogSeeder>().ApplyAsync();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.PlatformSettings.FindAsync("pricing.public-enabled");
        if (row is null) db.PlatformSettings.Add(new PlatformSetting { Key = "pricing.public-enabled", Value = "true", UpdatedAt = DateTime.UtcNow });
        else row.Value = "true";
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<PricingCatalogCache>().Invalidate();
    }

    private async Task DbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private void InvalidateCache()
    {
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PricingCatalogCache>().Invalidate();
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        var response = await AnonymousClient().GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static JsonElement Plan(JsonElement body, string name) =>
        body.GetProperty("plans").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);

    private static IEnumerable<string> PublicTexts(JsonElement body)
    {
        yield return body.GetProperty("notice").GetString() ?? "";
        foreach (var p in body.GetProperty("plans").EnumerateArray())
        {
            yield return p.GetProperty("description").GetString() ?? "";
            foreach (var h in p.GetProperty("highlights").EnumerateArray()) yield return h.GetString() ?? "";
        }

        if (body.TryGetProperty("messengerAddonsNote", out var note) && note.ValueKind == JsonValueKind.Object)
            yield return note.GetProperty("taxNote").GetString() ?? "";
    }

    [Fact, TestCase("CY48-QA-1")]
    public async Task ZapisPricing_NetworkIsFixed3900_AndNoPublicTextHasFloatingPriceOrIndividualTerms()
    {
        await ApplyCatalogAsync();
        var body = await GetJsonAsync("/api/pricing");

        var network = Plan(body, "Сеть");
        network.GetProperty("pricePerMonth").GetDecimal().Should().Be(3900m);
        network.GetProperty("description").GetString().Should()
            .Be("Для сети салонов: филиалы и сотрудники без ограничения по количеству. 3 900 ₽ в месяц независимо от числа филиалов.");
        network.GetProperty("highlights").EnumerateArray().Select(h => h.GetString()).Should().Contain("Цена не зависит от числа филиалов");
        network.GetProperty("includedEmployees").ValueKind.Should().Be(JsonValueKind.Null, "limits of «Сеть» are not changed: unlimited");

        foreach (var text in PublicTexts(body))
            foreach (var rx in Forbidden)
                rx.IsMatch(text).Should().BeFalse($"«{text}» must not match /{rx}/ (SPEC.md US-48-04, п. 6.13.11 оферты)");
    }

    [Fact, TestCase("CY48-QA-2")]
    public async Task OrdersPricing_ChainIsFixed2990_AndPublicTextsAreClean()
    {
        await ApplyCatalogAsync();
        var body = await GetJsonAsync("/api/pricing/orders");

        var chain = Plan(body, "Сеть магазинов");
        chain.GetProperty("pricePerMonth").GetDecimal().Should().Be(2990m);
        chain.GetProperty("description").GetString().Should().Contain("2 990 ₽ в месяц независимо от числа магазинов");
        foreach (var text in PublicTexts(body))
            foreach (var rx in Forbidden)
                rx.IsMatch(text).Should().BeFalse($"«{text}» must not match /{rx}/");
    }

    [Fact, TestCase("CY48-QA-3")]
    public async Task Notice_IsShortAndNeutral_PointsToPaymentInfo_AndDoesNotPromiseCards()
    {
        // LEGAL_REVIEW_CYCLE48.md «Поправка 31.10.2026 (2)»: the detailed path (request → invoice → payment) is only on /payment-info.
        await ApplyCatalogAsync();
        foreach (var url in new[] { "/api/pricing", "/api/pricing/orders" })
        {
            var notice = (await GetJsonAsync(url)).GetProperty("notice").GetString()!;
            notice.Should().Be("Подключить платный тариф можно в кабинете после регистрации. Как оплатить и как вернуть деньги — на странице «Оплата и возврат».");
            notice.Should().NotContain("заявку").And.NotContain("счёт").And.NotContain("банковским переводом");
            notice.Should().NotContain("{{").And.NotContain("}}");
            notice.Should().NotMatchRegex("(?i)картой|свяжемся");
        }
    }

    [Fact, TestCase("CY48-QA-4")]
    public async Task Seeder_AlignsUntouchedLegacyNetworkText_ButNeverOverwritesAnAdminsEdit()
    {
        await ApplyCatalogAsync();

        // Existing production database: «Сеть» still carries the previous seed -> the apply moves it to the new text.
        await DbAsync(async db =>
        {
            var plan = await db.SubscriptionPlanConfigs.SingleAsync(p => p.Line == CompanyKind.Services && p.Name == "Сеть");
            plan.Description = LegacyNetworkDescription;
            await db.SaveChangesAsync();
        });
        await ApplyCatalogAsync();
        var aligned = await DbAsync(db => db.SubscriptionPlanConfigs.AsNoTracking().SingleAsync(p => p.Line == CompanyKind.Services && p.Name == "Сеть"));
        aligned.Description.Should().Contain("независимо от числа филиалов");
        aligned.PricePerMonth.Should().Be(3900m);

        // The admin edited the text by hand -> it stays, and the plan says so instead of silently overwriting.
        const string manual = "Текст, который владелец платформы поправил руками";
        await DbAsync(async db =>
        {
            var plan = await db.SubscriptionPlanConfigs.SingleAsync(p => p.Line == CompanyKind.Services && p.Name == "Сеть");
            plan.Description = manual;
            await db.SaveChangesAsync();
        });
        await ApplyCatalogAsync();
        var kept = await DbAsync(db => db.SubscriptionPlanConfigs.AsNoTracking().SingleAsync(p => p.Line == CompanyKind.Services && p.Name == "Сеть"));
        kept.Description.Should().Be(manual);
        kept.PricePerMonth.Should().Be(3900m, "no price changes in this cycle");

        // Put the new text back: the tests of this class share one database.
        await DbAsync(async db =>
        {
            var plan = await db.SubscriptionPlanConfigs.SingleAsync(p => p.Line == CompanyKind.Services && p.Name == "Сеть");
            plan.Description = aligned.Description;
            await db.SaveChangesAsync();
        });
        InvalidateCache();
    }

    [Fact, TestCase("CY48-QA-5")]
    public async Task Seeder_ApplyTwice_IsIdempotent_NoDuplicateTariffs()
    {
        await ApplyCatalogAsync();
        var first = await DbAsync(db => db.SubscriptionPlanConfigs.CountAsync());
        await ApplyCatalogAsync();
        await ApplyCatalogAsync();
        (await DbAsync(db => db.SubscriptionPlanConfigs.CountAsync())).Should().Be(first);
    }

    private sealed class RecordingSignals(bool fail) : IGlitchTipSignalService
    {
        public List<string> Messages { get; } = [];

        public Task<bool> SendAsync(string message, CancellationToken ct)
        {
            lock (Messages) Messages.Add(message);
            if (fail) throw new InvalidOperationException("GlitchTip is down");
            return Task.FromResult(true);
        }
    }

    private async Task<(HttpResponseMessage Response, string Body)> SubmitRequestWithAsync(RecordingSignals signals)
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var planId = await CreateTestPlanConfigAsync(allowOnlineBooking: true, maxCompanies: 10, maxEmployees: 10);
        await DbAsync(async db =>
        {
            (await db.SubscriptionPlanConfigs.FindAsync(planId))!.IsActive = true;
            await db.SaveChangesAsync();
        });

        await using var factory = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IGlitchTipSignalService>();
            s.AddSingleton<IGlitchTipSignalService>(signals);
        }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.Token);
        var response = await client.PostAsJsonAsync("/api/billing/subscription/request",
            new SubscriptionRequestInputDto(planId, [], "Хочу перейти на новый тариф"));
        var body = await response.Content.ReadAsStringAsync();
        for (var i = 0; i < 50 && signals.Messages.Count == 0; i++) await Task.Delay(100);
        return (response, body);
    }

    [Fact, TestCase("CY48-QA-6")]
    public async Task NewSubscriptionRequest_SendsExactlyOneOperatorSignal_WithoutPersonalData()
    {
        var signals = new RecordingSignals(fail: false);
        var (response, body) = await SubmitRequestWithAsync(signals);

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        await Task.Delay(300);
        signals.Messages.Should().HaveCount(1);
        signals.Messages[0].Should().StartWith("Новая заявка на подписку").And.Contain("счёт выставить до");
        signals.Messages[0].Should().NotContain("@").And.NotContain("+7").And.NotContain("Хочу перейти", "the free-text comment never goes into a signal");
    }

    [Fact, TestCase("CY48-QA-7")]
    public async Task FailingSignalChannel_DoesNotBreakTheRequest()
    {
        var signals = new RecordingSignals(fail: true);
        var (response, body) = await SubmitRequestWithAsync(signals);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a broken signal channel must not change the answer (US-48-08)");
        signals.Messages.Should().HaveCount(1, "the signal was attempted");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        doc.RootElement.EnumerateObject().Should().NotBeEmpty("the answer has the same body as without a failing channel");
    }

    [Fact, TestCase("CY48-QA-8")]
    public async Task ReplacingAPendingRequest_SendsOneSignalPerSubmission()
    {
        var signals = new RecordingSignals(fail: false);
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var planId = await CreateTestPlanConfigAsync(allowOnlineBooking: true, maxCompanies: 10, maxEmployees: 10);
        await DbAsync(async db =>
        {
            (await db.SubscriptionPlanConfigs.FindAsync(planId))!.IsActive = true;
            await db.SaveChangesAsync();
        });
        await using var factory = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IGlitchTipSignalService>();
            s.AddSingleton<IGlitchTipSignalService>(signals);
        }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.Token);
        var input = new SubscriptionRequestInputDto(planId, [], null);

        (await client.PostAsJsonAsync("/api/billing/subscription/request", input)).EnsureSuccessStatusCode();
        for (var i = 0; i < 50 && signals.Messages.Count < 1; i++) await Task.Delay(100);
        (await client.PostAsJsonAsync("/api/billing/subscription/request", input)).EnsureSuccessStatusCode(); // replaces the pending one (BLL: «заменяет первую»)
        for (var i = 0; i < 50 && signals.Messages.Count < 2; i++) await Task.Delay(100);
        await Task.Delay(300);

        signals.Messages.Should().HaveCount(2, "the operator must see the new request again: its text may differ and the invoice deadline restarts");
    }
}
