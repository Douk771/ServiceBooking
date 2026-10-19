using System.Net;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 43, US-43-02 (SPEC.md): суммы и статусы строк мессенджера одинаковы в списке аккаунтов, карточке аккаунта, профиле и «Подписке» владельца всех четырёх линеек,
/// не зависят от тарифа, и нигде нет «Недоступно на текущем тарифе». Ожидаемые числа и тексты — из SPEC/контракта (§43.28.1), посчитаны в тесте независимо от сервера.
/// Тариф «Записей» стоит 1000 ₽, WhatsApp — 300 ₽/мес, MAX — 490 ₽/мес.
/// </summary>
public class Cycle43MatrixTests(TestDatabaseFixture fixture) : Cycle43TestBase(fixture)
{
    private const decimal PlanPrice = 1000m;
    private static readonly string?[] Lines = [null, "Services", "Orders", "Stays", "Baths"];

    private sealed record Want(string Status, string Text, decimal Monthly, bool Counts);

    private static decimal PriceOf(string transport) => transport == "Max" ? 490m : 300m;

    private async Task PrepareCatalogAsync()
    {
        await SetCatalogAsync("WhatsApp", 300);
        await SetCatalogAsync("Max", 490);
    }

    /// <summary>
    /// Сверяет одну строку мессенджера во всех местах, где она видна, и итоги. <paramref name="other"/> — вклад второго мессенджера в сумму (по умолчанию его нет).
    /// </summary>
    private async Task AssertEverywhereAsync(Acc a, string transport, Want want, decimal planPrice = PlanPrice, decimal other = 0m, string scenario = "")
    {
        var admin = await AdminAsync();
        var raw = new List<string>();
        var cardResponse = await Expect(await admin.GetAsync($"/api/admin/billing-accounts/{a.AccountId}"), HttpStatusCode.OK);
        var cardText = await cardResponse.Content.ReadAsStringAsync();
        raw.Add(cardText);
        var card = JsonDocument.Parse(cardText).RootElement;
        var expectedTotal = planPrice + (want.Counts ? want.Monthly : 0m) + other;

        using (new AssertionScope($"{transport}, {scenario}"))
        {
            var line = Line(card, transport);
            line.GetProperty("status").GetString().Should().Be(want.Status, "card status");
            line.GetProperty("statusText").GetString().Should().Be(want.Text, "card text");
            line.GetProperty("monthlyPrice").GetDecimal().Should().Be(want.Monthly, "card line sum");
            line.GetProperty("countsInTotal").GetBoolean().Should().Be(want.Counts, "card countsInTotal");
            card.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(expectedTotal, "card total = tariff + options + messengers");
            card.GetProperty("messengersMonthlyTotal").GetDecimal().Should().Be((want.Counts ? want.Monthly : 0m) + other, "card messengers total");
            card.GetProperty("options").EnumerateArray().Select(o => o.ToString()).Should().NotContain(s => s.Contains("notifications.") || s.Contains("WhatsApp") || s.Contains("MAX"),
                "messengers moved out of options[] into messengers[]");

            (await ListTotalAsync(a, admin)).Should().Be(expectedTotal, "accounts list total");
            (await ProfileTotalAsync(a)).Should().Be(expectedTotal, "profile total");

            foreach (var lineName in Lines)
            {
                var subText = await (await Expect(await AuthedClient(a.Owner.Token).GetAsync("/api/billing/subscription" + (lineName is null ? "" : $"?line={lineName}")), HttpStatusCode.OK)).Content.ReadAsStringAsync();
                raw.Add(subText);
                var sub = JsonDocument.Parse(subText).RootElement;
                var owner = OwnerLine(sub, transport);
                owner.GetProperty("status").GetString().Should().Be(want.Status, $"owner status, line {lineName ?? "default"}");
                owner.GetProperty("statusText").GetString().Should().Be(want.Text, $"owner text, line {lineName ?? "default"}");
                owner.GetProperty("monthlyPrice").GetDecimal().Should().Be(want.Monthly, $"owner sum, line {lineName ?? "default"}");
                sub.GetProperty("messengers").GetProperty("monthlyTotal").GetDecimal().Should().Be((want.Counts ? want.Monthly : 0m) + other, $"owner messengers total, line {lineName ?? "default"}");
                sub.GetProperty("options").EnumerateArray().Select(o => o.ToString()).Should().NotContain(s => s.Contains("notifications.") || s.Contains("WhatsApp") || s.Contains("MAX"),
                    $"messengers are not in options[] of line {lineName ?? "default"}");
                if (lineName is null or "Services")
                    sub.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(planPrice, "the line total is the tariff plus its own options only (cycle7 invariant)");
            }

            raw.Should().NotContain(s => s.Contains("Недоступно на текущем тарифе"), "the phrase never appears for messengers");
        }
    }

    private static Want TrialNoDate => new("Trial", "Пробный период", 0m, true);

    // ── пробная строка ───────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-20")]
    public async Task TrialRows_ShowAsTrial_ZeroRubles_InTheTotalAsZero_Everywhere()
    {
        await PrepareCatalogAsync();
        foreach (var transport in new[] { "WhatsApp", "Max" })
        {
            // the trial window of the account runs; the row has no own date
            var a = await NewAccountAsync();
            await DbAsync(async db => { (await db.BillingAccounts.FirstAsync(x => x.Id == a.AccountId)).TrialEndsAtUtc = DateTime.UtcNow.AddDays(10); await db.SaveChangesAsync(); });
            await SeedRowAsync(a.AccountId, transport, null, trial: true);
            await AssertEverywhereAsync(a, transport, TrialNoDate, scenario: "trial without own date");

            // the row has its own date
            var until = NoonIn(9);
            var b = await NewAccountAsync();
            await SeedRowAsync(b.AccountId, transport, until, trial: true);
            await AssertEverywhereAsync(b, transport, new Want("Trial", $"Пробный период до {D(until)}", 0m, true), scenario: "trial with own date");
        }
    }

    // ── оплаченная строка ────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-21")]
    public async Task PaidRows_ShowPaidUntil_AndCostThePriceOfTheOption_Everywhere()
    {
        await PrepareCatalogAsync();
        foreach (var transport in new[] { "WhatsApp", "Max" })
        {
            var until = NoonIn(25);
            var a = await NewAccountAsync();
            await SeedRowAsync(a.AccountId, transport, until);
            await AssertEverywhereAsync(a, transport, new Want("Paid", $"Оплачено до {D(until)}", PriceOf(transport), true), scenario: "paid with own date");

            // the paid period is the same for the account of a different tariff price: the tariff does not enter into the messenger sum
            var cheap = await NewAccountAsync(planPrice: 0m);
            await SeedRowAsync(cheap.AccountId, transport, until);
            await AssertEverywhereAsync(cheap, transport, new Want("Paid", $"Оплачено до {D(until)}", PriceOf(transport), true), planPrice: 0m, scenario: "paid, free tariff");
        }
    }

    [Fact, TestCase("CY43-22")]
    public async Task ExpiredRows_SayPaymentEnded_AndStayOutOfTheTotal_Everywhere()
    {
        await PrepareCatalogAsync();
        foreach (var transport in new[] { "WhatsApp", "Max" })
        {
            var ended = NoonIn(-4);
            var a = await NewAccountAsync();
            await SeedRowAsync(a.AccountId, transport, ended);
            await AssertEverywhereAsync(a, transport, new Want("Expired", $"Оплата закончилась {D(ended)}", 0m, false), scenario: "paid period ended");

            // an ended trial row says so in its own words
            var endedTrial = NoonIn(-2);
            var b = await NewAccountAsync();
            await SeedRowAsync(b.AccountId, transport, endedTrial, trial: true);
            await AssertEverywhereAsync(b, transport, new Want("Expired", $"Пробный период закончился {D(endedTrial)}", 0m, false), scenario: "trial ended");
        }
    }

    [Fact, TestCase("CY43-23")]
    public async Task LegacyRowsWithoutOwnDate_LiveByTheServicesSubscription_AndAreShownHonestly()
    {
        await PrepareCatalogAsync();
        foreach (var transport in new[] { "WhatsApp", "Max" })
        {
            var good = await NewAccountAsync(subscriptionDays: 20);
            await SeedRowAsync(good.AccountId, transport, null);
            var goodUntil = (await DbAsync(db => db.AccountSubscriptions.AsNoTracking().FirstAsync(s => s.BillingAccountId == good.AccountId))).PaidUntil!.Value;
            await AssertEverywhereAsync(good, transport, new Want("Paid", $"Оплачено до {D(goodUntil)}", PriceOf(transport), true), scenario: "no own date, subscription valid");

            var lapsed = await NewAccountAsync();
            await SeedRowAsync(lapsed.AccountId, transport, null);
            await DbAsync(async db => { (await db.AccountSubscriptions.FirstAsync(s => s.BillingAccountId == lapsed.AccountId)).PaidUntil = NoonIn(-6); await db.SaveChangesAsync(); });
            await AssertEverywhereAsync(lapsed, transport, new Want("Expired", $"Оплата закончилась {D(NoonIn(-6))}", 0m, false), scenario: "no own date, subscription ended");
        }
    }

    [Fact, TestCase("CY43-24")]
    public async Task EndingRows_SayWhenTheyEnd_AndStillCountInTheTotal()
    {
        await PrepareCatalogAsync();
        foreach (var transport in new[] { "WhatsApp", "Max" })
        {
            var a = await NewAccountAsync();
            var paidUntil = NoonIn(40);
            var endsAt = NoonIn(12);
            await SeedRowAsync(a.AccountId, transport, paidUntil, endsAt: endsAt);
            await AssertEverywhereAsync(a, transport, new Want("Ending", $"Отключается {D(endsAt)}", PriceOf(transport), true), scenario: "disable scheduled");
        }
    }

    [Fact, TestCase("CY43-25")]
    public async Task NotConnectedAndPendingPayment_AreZeroAndOutOfTheTotal_Everywhere()
    {
        await PrepareCatalogAsync();
        foreach (var transport in new[] { "WhatsApp", "Max" })
        {
            var none = await NewAccountAsync();
            await AssertEverywhereAsync(none, transport, new Want("NotConnected", "Не подключён", 0m, false), scenario: "no row");

            var pending = await NewAccountAsync();
            await RequestChannelAsync(pending, transport);
            await AssertEverywhereAsync(pending, transport, new Want("PendingPayment", "Ожидает подтверждения оплаты", 0m, false), scenario: "owner asked to pay");
        }
    }

    [Fact, TestCase("CY43-26")]
    public async Task BothMessengersTogether_AreSummedOnceEach_AndTheOtherLineIsUntouched()
    {
        await PrepareCatalogAsync();
        var a = await NewAccountAsync();
        var waUntil = NoonIn(30);
        await SeedRowAsync(a.AccountId, "WhatsApp", waUntil);
        await SeedRowAsync(a.AccountId, "Max", NoonIn(8), trial: true);

        await AssertEverywhereAsync(a, "WhatsApp", new Want("Paid", $"Оплачено до {D(waUntil)}", 300m, true), other: 0m, scenario: "WhatsApp paid + MAX trial");
        // MAX trial is 0 in the total, so the total above is tariff + 300 for both views
        var card = await CardAsync(a.AccountId);
        card.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1300m);
        Line(card, "Max").GetProperty("statusText").GetString().Should().Be($"Пробный период до {D(NoonIn(8))}");

        // buying out the trial (paid) adds the price once
        await SeedRowAsync(a.AccountId, "Max", NoonIn(31));
        var card2 = await CardAsync(a.AccountId);
        card2.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1000m + 300m + 490m);
        (await ListTotalAsync(a)).Should().Be(1790m);
        (await ProfileTotalAsync(a)).Should().Be(1790m);
        (await OwnerSubscriptionAsync(a)).GetProperty("messengers").GetProperty("monthlyTotal").GetDecimal().Should().Be(790m);
        (await OwnerSubscriptionAsync(a)).GetProperty("messengers").GetProperty("monthlyTotalText").GetString().Should().Be("Мессенджеры: 790 ₽/мес");
    }

    [Fact, TestCase("CY43-27")]
    public async Task TariffRulesForMessengers_ChangeNothing_SumsAndStatusesAreTheSameOnAnyTariff()
    {
        await PrepareCatalogAsync();
        var until = NoonIn(22);
        var variants = new (string Name, Func<AppDbContextProxy, Task> Arrange)[]
        {
            ("rule Unavailable for both", db => db.SetRulesAsync(OptionAvailability.Unavailable)),
            ("rule Included for both", db => db.SetRulesAsync(OptionAvailability.Included, included: 1)),
            ("rule Extra for both", db => db.SetRulesAsync(OptionAvailability.Extra)),
            ("no rule for either", db => db.DeleteRulesAsync()),
        };
        foreach (var (name, arrange) in variants)
        foreach (var transport in new[] { "WhatsApp", "Max" })
        {
            var a = await NewAccountAsync(planPrice: 2000m);
            await SeedRowAsync(a.AccountId, transport, until);
            await DbAsync(async db => await arrange(new AppDbContextProxy(db, a.PlanId)));
            await AssertEverywhereAsync(a, transport, new Want("Paid", $"Оплачено до {D(until)}", PriceOf(transport), true), planPrice: 2000m, scenario: name);
        }
    }

    [Fact, TestCase("CY43-28")]
    public async Task AccountWithoutServicesTariff_PaidMessengerIsShownWithPriceAndCountedInTheTotal()
    {
        await PrepareCatalogAsync();
        foreach (var transport in new[] { "WhatsApp", "Max" })
        {
            var a = await NewAccountWithoutServicesPlanAsync();
            var until = NoonIn(18);
            await SeedRowAsync(a.AccountId, transport, until);
            // the owner of such an account only has "Заказы" / "Дома" / "Бани": the card and the list still show the price and the sum
            var admin = await AdminAsync();
            var card = await CardAsync(a.AccountId, admin);
            var line = Line(card, transport);
            line.GetProperty("status").GetString().Should().Be("Paid");
            line.GetProperty("statusText").GetString().Should().Be($"Оплачено до {D(until)}");
            line.GetProperty("monthlyPrice").GetDecimal().Should().Be(PriceOf(transport));
            line.GetProperty("priceText").GetString().Should().Contain(((int)PriceOf(transport)).ToString());
            card.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(PriceOf(transport), "no tariff, one messenger: the total is the messenger");
            (await ListTotalAsync(a, admin)).Should().Be(PriceOf(transport));
            (await ProfileTotalAsync(a)).Should().Be(PriceOf(transport));
            foreach (var lineName in Lines)
            {
                var sub = await OwnerSubscriptionAsync(a, lineName);
                OwnerLine(sub, transport).GetProperty("statusText").GetString().Should().Be($"Оплачено до {D(until)}", $"line {lineName ?? "default"}");
                OwnerLine(sub, transport).GetProperty("monthlyPrice").GetDecimal().Should().Be(PriceOf(transport));
            }
        }
    }

    [Fact, TestCase("CY43-29")]
    public async Task PriceOfTheCatalog_IsTheOnlyPriceOfAPaidRow_NoMatterWhoPaidWhen()
    {
        await PrepareCatalogAsync();
        var a = await NewAccountAsync();
        await SeedRowAsync(a.AccountId, "Max", NoonIn(30));
        (await CardAsync(a.AccountId)).GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1490m);

        // a new price applies to the sums of already paid rows from now on (the periods themselves stay)
        await SetCatalogAsync("Max", 520);
        var card = await CardAsync(a.AccountId);
        card.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1520m);
        Line(card, "Max").GetProperty("paidUntil").GetDateTime().Date.Should().Be(NoonIn(30).Date, "the paid period is not recalculated");
        (await ProfileTotalAsync(a)).Should().Be(1520m);

        // a free (0 ₽) price: a paid row is in the total as zero, the status is still "Оплачено"
        await SetCatalogAsync("Max", 0);
        var free = await CardAsync(a.AccountId);
        Line(free, "Max").GetProperty("status").GetString().Should().Be("Paid");
        free.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1000m);

        // no price: the row is still paid, it costs nothing in the total
        await SetCatalogAsync("Max", null);
        var noPrice = await CardAsync(a.AccountId);
        Line(noPrice, "Max").GetProperty("status").GetString().Should().Be("Paid");
        Line(noPrice, "Max").GetProperty("priceText").GetString().Should().Be("Цена не задана");
        noPrice.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1000m);
        await SetCatalogAsync("Max", 490);
    }

    /// <summary>Узкая обёртка над контекстом для подготовки правил тарифа в тесте CY43-27: правила мессенджеров пишутся прямо в базу (так их оставила прежняя модель).</summary>
    private sealed class AppDbContextProxy(ServiceBooking.Infrastructure.Data.AppDbContext db, Guid planId)
    {
        public async Task SetRulesAsync(OptionAvailability availability, int? included = null)
        {
            foreach (var code in new[] { WhatsAppCode, MaxCode })
            {
                var optionId = await db.SubscriptionOptions.Where(o => o.Code == code).Select(o => o.Id).FirstAsync();
                var rule = await db.PlanOptionRules.FirstOrDefaultAsync(r => r.PlanConfigId == planId && r.OptionId == optionId);
                if (rule is null) db.PlanOptionRules.Add(new PlanOptionRule { Id = Guid.NewGuid(), PlanConfigId = planId, OptionId = optionId, Availability = availability, IncludedQuantity = included });
                else { rule.Availability = availability; rule.IncludedQuantity = included; }
            }
            await db.SaveChangesAsync();
        }

        public async Task DeleteRulesAsync()
        {
            var ids = await db.SubscriptionOptions.Where(o => o.Code == WhatsAppCode || o.Code == MaxCode).Select(o => o.Id).ToListAsync();
            db.PlanOptionRules.RemoveRange(db.PlanOptionRules.Where(r => r.PlanConfigId == planId && ids.Contains(r.OptionId)));
            await db.SaveChangesAsync();
        }
    }
}
