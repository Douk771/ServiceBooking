using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// CY48-B5 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.15) and the functional half of CY48-B3: after the tariff seeder and with the public
/// storefront on, the real answers of GET /api/pricing, GET /api/pricing/orders and GET /api/billing/subscription conform to
/// contracts/cycle48/openapi.json; the tariff texts pass the «tariffTexts» patterns of contracts/cycle48/forbidden-phrases.json;
/// the messenger rows carry the short tax note, not the storefront notice.
/// </summary>
public class Cycle48StorefrontContractTests(TestDatabaseFixture fixture) : Cycle43TestBase(fixture)
{
    private static readonly OpenApiContract C48 = OpenApiContract.Load("cycle48");

    private const string TaxNote = "Без НДС: оператор применяет налог на профессиональный доход";

    private async Task PrepareAsync()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TariffCatalogSeeder>().ApplyAsync();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PlatformSettings.FindAsync("pricing.public-enabled");
            if (row is null) db.PlatformSettings.Add(new ServiceBooking.Core.Entities.PlatformSetting { Key = "pricing.public-enabled", Value = "true", UpdatedAt = DateTime.UtcNow });
            else row.Value = "true";
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<PricingCatalogCache>().Invalidate();
        }

        // The messenger rows (and their note) are shown only for an open, priced catalog.
        await SetCatalogAsync("WhatsApp", 300);
        await SetCatalogAsync("Max", 490);
        using var again = Factory.Services.CreateScope();
        again.ServiceProvider.GetRequiredService<PricingCatalogCache>().Invalidate();
    }

    private async Task<JsonElement> PublicAsync(string url)
    {
        var response = await AnonymousClient().GetAsync(url);
        return await J(await Expect(response, System.Net.HttpStatusCode.OK));
    }

    private static List<Regex> TariffTextPatterns()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "contracts", "cycle48", "forbidden-phrases.json");
            if (!File.Exists(path)) continue;
            var doc = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            return doc.GetProperty("patterns").EnumerateArray()
                .Where(p => p.GetProperty("scopes").EnumerateArray().Any(s => s.GetString() == "tariffTexts"))
                .Select(p => new Regex(p.GetProperty("regex").GetString()!, p.GetProperty("ignoreCase").GetBoolean() ? RegexOptions.IgnoreCase : RegexOptions.None))
                .ToList();
        }

        throw new FileNotFoundException("contracts/cycle48/forbidden-phrases.json not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> TariffTexts(JsonElement body)
    {
        foreach (var p in body.GetProperty("plans").EnumerateArray())
        {
            yield return p.GetProperty("description").GetString() ?? "";
            foreach (var h in p.GetProperty("highlights").EnumerateArray()) yield return h.GetString() ?? "";
        }
    }

    [Fact, TestCase("CY48-B5")]
    public async Task PublicPricing_ConformsToTheCycle48Contract_AndTariffTextsPassTheForbiddenPatterns()
    {
        await PrepareAsync();
        var patterns = TariffTextPatterns();
        patterns.Should().NotBeEmpty("the tariffTexts scope has patterns");

        foreach (var url in new[] { "/api/pricing", "/api/pricing/orders" })
        {
            var body = await PublicAsync(url);
            C48.AssertResponse("get", url, 200, body);
            var texts = TariffTexts(body).ToList();
            texts.Should().NotBeEmpty();
            foreach (var text in texts)
                foreach (var rx in patterns)
                    rx.IsMatch(text).Should().BeFalse($"{url}: «{text}» must not match /{rx}/ (forbidden-phrases.json, tariffTexts)");
        }
    }

    [Fact, TestCase("CY48-B5")]
    public async Task OwnerSubscription_ConformsToTheCycle48Contract()
    {
        await PrepareAsync();
        var account = await NewAccountAsync();

        var body = await OwnerSubscriptionAsync(account);

        C48.AssertResponse("get", "/api/billing/subscription", 200, body);
    }

    [Fact, TestCase("CY48-B3")]
    public async Task MessengerRows_CarryTheTaxNote_NotTheStorefrontNotice()
    {
        await PrepareAsync();
        var account = await NewAccountAsync();

        var zapis = await PublicAsync("/api/pricing");
        var orders = await PublicAsync("/api/pricing/orders");
        var subscription = await OwnerSubscriptionAsync(account);

        foreach (var (name, body) in new[] { ("/api/pricing", zapis), ("/api/billing/subscription", subscription) })
        {
            body.TryGetProperty("messengerAddonsNote", out var note).Should().BeTrue($"{name} has the messenger note");
            note.ValueKind.Should().Be(JsonValueKind.Object, name);
            note.GetProperty("taxNote").GetString().Should().Be(TaxNote, name);
        }

        var notice = zapis.GetProperty("notice").GetString()!;
        notice.Should().NotBe(TaxNote).And.Contain("«Оплата и возврат»", "the storefront notice is the short pointer to /payment-info (LEGAL «Поправка 31.10.2026 (2)»)");
        zapis.GetProperty("messengerAddonsNote").GetProperty("taxNote").GetString().Should().NotBe(notice, "taxNote is not the notice (LEGAL §2.6)");
        subscription.GetProperty("messengerAddonsNote").GetProperty("taxNote").GetString().Should().NotBe(notice);
        orders.GetProperty("notice").GetString().Should().NotBe(TaxNote);
    }
}
