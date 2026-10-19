using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Общие помощники функциональных тестов цикла 43 (опции мессенджеров: цена, отвязка от тарифа, подключение отдельно). Только подготовка данных и разбор ответов по
/// API_CONTRACT_CYCLE43.md; проверяемое поведение в помощниках не спрятано. Ожидаемые тексты и суммы в тестах пишутся буквами по SPEC.md/контракту, а не берутся из кода сервера.
/// Класс = своя база и свой хост (NotificationTestBase), поэтому цена, переключатели и строки опций не пересекаются с другими классами.
/// </summary>
public abstract class Cycle43TestBase(TestDatabaseFixture fixture) : NotificationTestBase(fixture)
{
    protected const string WhatsAppCode = "notifications.whatsapp";
    protected const string MaxCode = "notifications.max";

    protected sealed record Acc(AuthResponseDto Owner, CompanyDto Company, Guid AccountId, Guid PlanId);

    protected static string CodeOf(string transport) => transport == "Max" ? MaxCode : WhatsAppCode;

    /// <summary>Московская дата, как в тексте сервера: dd.MM.yyyy (UTC+3).</summary>
    protected static string D(DateTime utc) => utc.ToUniversalTime().AddHours(3).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Середина дня UTC через <paramref name="days"/> дней: сдвиг на московское время не меняет календарную дату.</summary>
    protected static DateTime NoonIn(int days) => DateTime.UtcNow.Date.AddDays(days).AddHours(9);

    protected async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected async Task DbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected static async Task<JsonElement> J(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>());

    protected async Task<HttpClient> AdminAsync() => AuthedClient((await LoginAsSuperAdminAsync()).Token);

    protected async Task<string> TextAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    /// <summary>Ответ — ровно ожидаемый код; тело печатается в сообщении, если код другой.</summary>
    protected static async Task<HttpResponseMessage> Expect(HttpResponseMessage response, HttpStatusCode code)
    {
        response.StatusCode.Should().Be(code, await response.Content.ReadAsStringAsync());
        return response;
    }

    // ── каталог: цена, активность, переключатель ─────────────────────────────────────────

    /// <summary>
    /// Ставит цену и активность опции мессенджера штатным путём админа (PUT …/price) — если текущие значения другие, иначе ничего не делает
    /// (сервер на «ничего не меняется» отвечает 400). Переключатель «открыто» — через настройки платформы.
    /// </summary>
    protected async Task SetCatalogAsync(string transport, decimal? price, bool active = true, bool open = true)
    {
        var admin = await AdminAsync();
        await SetOpenAsync(admin, transport, open);
        var items = (await J(await Expect(await admin.GetAsync("/api/admin/messenger-options"), HttpStatusCode.OK))).GetProperty("items");
        var current = items.EnumerateArray().Single(i => i.GetProperty("transport").GetString() == transport);
        decimal? curPrice = current.GetProperty("pricePerMonth").ValueKind == JsonValueKind.Null ? null : current.GetProperty("pricePerMonth").GetDecimal();
        var curActive = current.GetProperty("isActive").GetBoolean();
        if (curPrice == price && curActive == active) return;
        await Expect(await admin.PutAsJsonAsync($"/api/admin/messenger-options/{transport}/price",
            new { pricePerMonth = price, isActive = active, comment = "подготовка теста", expectedPricePerMonth = curPrice, expectedIsActive = curActive }), HttpStatusCode.OK);
    }

    protected async Task SetOpenAsync(HttpClient admin, string transport, bool open) =>
        await SetPlatformFieldAsync(admin, transport == "Max" ? "maxOptionOpen" : "whatsAppOptionOpen", open);

    protected async Task SetPlatformFieldAsync(HttpClient admin, string field, object value)
    {
        var current = await J(await Expect(await admin.GetAsync("/api/admin/platform-settings"), HttpStatusCode.OK));
        var body = new Dictionary<string, object?>
        {
            ["channelIdleDays"] = current.GetProperty("channelIdleDays").GetInt32(),
            ["pricingPublicEnabled"] = current.GetProperty("pricingPublicEnabled").GetBoolean(), // null in the other fields = "do not change"
        };
        body[field] = value;
        await Expect(await admin.PutAsJsonAsync("/api/admin/platform-settings", body), HttpStatusCode.OK);
    }

    protected async Task<Guid> OptionIdAsync(string transport) =>
        await DbAsync(db => db.SubscriptionOptions.Where(o => o.Code == CodeOf(transport)).Select(o => o.Id).FirstAsync());

    // ── аккаунты и строки опций ──────────────────────────────────────────────────────────

    /// <summary>
    /// Владелец «Записей» с компанией и платным тарифом на месяц вперёд (цена тарифа <paramref name="planPrice"/>, чтобы итог отличался от сумм мессенджеров).
    /// Строк мессенджеров нет.
    /// </summary>
    protected async Task<Acc> NewAccountAsync(decimal planPrice = 1000m, int subscriptionDays = 30)
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        return await DbAsync(async db =>
        {
            var account = await db.BillingAccounts.FirstAsync(a => a.OwnerUserId == owner.UserId);
            var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig).FirstAsync(s => s.BillingAccountId == account.Id);
            sub.PlanConfig!.PricePerMonth = planPrice;
            sub.PaidUntil = DateTime.UtcNow.AddDays(subscriptionDays);
            await db.SaveChangesAsync();
            return new Acc(owner, company, account.Id, sub.PlanConfigId!.Value);
        });
    }

    /// <summary>Владелец без тарифа «Записей» (нет AccountSubscription), только аккаунт — для кейса «только Бани/Заказы» (И-4).</summary>
    protected async Task<Acc> NewAccountWithoutServicesPlanAsync()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        return await DbAsync(async db =>
        {
            var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == owner.UserId);
            if (account is null)
            {
                account = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = owner.UserId };
                db.BillingAccounts.Add(account);
                await db.SaveChangesAsync();
            }
            var companyRow = await db.Companies.FirstAsync(c => c.Id == company.Id);
            companyRow.BillingAccountId = account.Id;
            companyRow.Kind = CompanyKind.Baths;
            db.AccountSubscriptions.RemoveRange(db.AccountSubscriptions.Where(s => s.BillingAccountId == account.Id || s.OwnerUserId == owner.UserId));
            await db.SaveChangesAsync();
            return new Acc(owner, company, account.Id, Guid.Empty);
        });
    }

    /// <summary>Строка опции мессенджера у аккаунта напрямую в базе (подготовка состояния, которого штатным путём не получить: «пробная», «истёкшая»).</summary>
    protected async Task SeedRowAsync(Guid accountId, string transport, DateTime? paidUntil, bool trial = false, DateTime? endsAt = null, DateTime? activatedAt = null)
    {
        await DbAsync(async db =>
        {
            var optionId = await db.SubscriptionOptions.Where(o => o.Code == CodeOf(transport)).Select(o => o.Id).FirstAsync();
            var row = await db.AccountSubscriptionOptions.FirstOrDefaultAsync(r => r.BillingAccountId == accountId && r.OptionId == optionId);
            if (row is null)
            {
                row = new AccountSubscriptionOption { Id = Guid.NewGuid(), BillingAccountId = accountId, OptionId = optionId, Quantity = 1 };
                db.AccountSubscriptionOptions.Add(row);
            }
            row.PaidUntilUtc = paidUntil;
            row.GrantedByTrial = trial;
            row.EndsAtUtc = endsAt;
            row.ActivatedAtUtc = activatedAt ?? DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        });
    }

    protected Task<AccountSubscriptionOption?> RowAsync(Guid accountId, string transport) =>
        DbAsync(db => db.AccountSubscriptionOptions.AsNoTracking().FirstOrDefaultAsync(r => r.BillingAccountId == accountId && r.Option.Code == CodeOf(transport)));

    protected async Task<JsonElement> CardAsync(Guid accountId, HttpClient? admin = null)
    {
        admin ??= await AdminAsync();
        return await J(await Expect(await admin.GetAsync($"/api/admin/billing-accounts/{accountId}"), HttpStatusCode.OK));
    }

    protected static JsonElement Line(JsonElement card, string transport) =>
        card.GetProperty("messengers").EnumerateArray().Single(m => m.GetProperty("transport").GetString() == transport);

    protected static JsonElement OwnerLine(JsonElement subscription, string transport) =>
        subscription.GetProperty("messengers").GetProperty("lines").EnumerateArray().Single(m => m.GetProperty("transport").GetString() == transport);

    protected async Task<JsonElement> OwnerSubscriptionAsync(Acc a, string? line = null) =>
        await J(await Expect(await AuthedClient(a.Owner.Token).GetAsync("/api/billing/subscription" + (line is null ? "" : $"?line={line}")), HttpStatusCode.OK));

    protected async Task<decimal> ListTotalAsync(Acc a, HttpClient? admin = null)
    {
        admin ??= await AdminAsync();
        var page = await J(await Expect(await admin.GetAsync($"/api/admin/billing-accounts?search={Uri.EscapeDataString(a.Owner.Phone)}&pageSize=50"), HttpStatusCode.OK));
        return page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == a.AccountId).GetProperty("totalMonthlyPrice").GetDecimal();
    }

    protected async Task<decimal> ProfileTotalAsync(Acc a) =>
        (await J(await Expect(await AuthedClient(a.Owner.Token).GetAsync("/api/profile"), HttpStatusCode.OK))).GetProperty("plan").GetProperty("totalMonthlyPrice").GetDecimal();

    protected string CurrentOfferVersion() =>
        Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>()
            .Current!.Get(LegalDocumentType.TermsOwner)!.Version;

    /// <summary>Номер в «заявке на оплату» (владелец прошёл мастер без оплаты): создаётся штатным POST канала, как в мастере.</summary>
    protected async Task<Guid> RequestChannelAsync(Acc a, string transport, bool paymentRequest = true)
    {
        var created = await AuthedClient(a.Owner.Token).PostAsJsonAsync("/api/notification-channels",
            new { legalEntityForm = "Company", inn = "7707083893", offerAccepted = new { version = CurrentOfferVersion() }, transport, paymentRequest });
        created.IsSuccessStatusCode.Should().BeTrue(await created.Content.ReadAsStringAsync());
        return (await J(created)).GetProperty("id").GetGuid();
    }

    protected async Task<JsonElement> OverviewTransportAsync(Acc a, string transport)
    {
        var overview = await J(await Expect(await AuthedClient(a.Owner.Token).GetAsync("/api/notification-channels/overview"), HttpStatusCode.OK));
        return overview.GetProperty("transports").EnumerateArray().Single(t => t.GetProperty("transport").GetString() == transport);
    }

    protected Task<HttpResponseMessage> ExtendAsync(HttpClient admin, Guid accountId, string transport, int months, decimal expectedPrice, string? comment = "Счёт 17") =>
        admin.PostAsJsonAsync($"/api/admin/billing-accounts/{accountId}/messengers/{transport}/extend", new { months, comment, expectedPricePerMonth = expectedPrice });

    protected Task<HttpResponseMessage> DisableAsync(HttpClient admin, Guid accountId, string transport, string when, string? comment = "Просьба владельца") =>
        admin.PostAsJsonAsync($"/api/admin/billing-accounts/{accountId}/messengers/{transport}/disable", new { when, comment });
}
