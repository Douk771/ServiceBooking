using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 43, US-43-02 (критерии 1, 2) и US-43-03 (критерий 6): мессенджеры вне тарифов — тариф не создаёт, не меняет и не удаляет их правила, форма подписки аккаунта не
/// создаёт, не продлевает и не завершает их строки и не отвечает 400/409 по ним ни в одной линейке.
/// </summary>
public class Cycle43TariffFormTests(TestDatabaseFixture fixture) : Cycle43TestBase(fixture)
{
    private object PlanBody(string name, IEnumerable<object>? options) => new
    {
        name, description = "Тариф теста цикла 43", highlights = new[] { "Пункт" }, pricePerMonth = 500, maxEmployees = 5, maxCompanies = 2,
        allowOnlineBooking = true, allowMailing = true, allowAnalytics = false, allowPublicListing = true, allowOnlinePayment = false,
        notifyDaysBefore = 7, isPublic = false, isActive = true, options,
    };

    private Task<List<PlanOptionRule>> MessengerRulesAsync(Guid planId) =>
        DbAsync(db => db.PlanOptionRules.AsNoTracking().Where(r => r.PlanConfigId == planId && (r.Option.Code == WhatsAppCode || r.Option.Code == MaxCode)).OrderBy(r => r.Option.Code).ToListAsync());

    // ── тарифы ───────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-30")]
    public async Task PlanSave_IgnoresMessengerRules_NeverCreatesChangesOrDeletesThem()
    {
        var admin = await AdminAsync();
        var waId = await OptionIdAsync("WhatsApp");
        var maxId = await OptionIdAsync("Max");

        // a new plan with messenger rules in the request: 201, no rules created
        var created = await admin.PostAsJsonAsync("/api/admin/plans", PlanBody(Unique("План-мессенджеры "), new object[]
        {
            new { optionId = waId, availability = "Extra", includedQuantity = (int?)null },
            new { optionId = maxId, availability = "Included", includedQuantity = 1 },
        }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var planId = (await J(created)).GetProperty("id").GetGuid();
        (await MessengerRulesAsync(planId)).Should().BeEmpty("a request from an old client is ignored without an error, nothing is written");

        // the rules that already exist (written by the previous model) stay as they are
        await DbAsync(async db =>
        {
            db.PlanOptionRules.Add(new PlanOptionRule { Id = Guid.NewGuid(), PlanConfigId = planId, OptionId = waId, Availability = OptionAvailability.Extra, IncludedQuantity = 2 });
            db.PlanOptionRules.Add(new PlanOptionRule { Id = Guid.NewGuid(), PlanConfigId = planId, OptionId = maxId, Availability = OptionAvailability.Included, IncludedQuantity = 1 });
            await db.SaveChangesAsync();
        });
        var before = await MessengerRulesAsync(planId);
        before.Should().HaveCount(2);

        foreach (var options in new object[]?[]
                 {
                     null, // the field is absent
                     Array.Empty<object>(), // an empty matrix does not delete messenger rules
                     new object[] { new { optionId = waId, availability = "Unavailable", includedQuantity = (int?)null }, new { optionId = maxId, availability = "Unavailable", includedQuantity = (int?)null } },
                 })
        {
            var update = await admin.PutAsJsonAsync($"/api/admin/plans/{planId}", PlanBody(Unique("План-мессенджеры "), options));
            update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
            var after = await MessengerRulesAsync(planId);
            after.Select(r => (r.OptionId, r.Availability, r.IncludedQuantity)).Should().BeEquivalentTo(before.Select(r => (r.OptionId, r.Availability, r.IncludedQuantity)),
                "a plan save never creates, changes or deletes the rules of the messengers");
        }
    }

    [Fact, TestCase("CY43-31")]
    public async Task PlansList_DoesNotOfferMessengerRules_AndCoverageCountsOnlyTariffDependentOptions()
    {
        var admin = await AdminAsync();
        var waId = await OptionIdAsync("WhatsApp");
        var maxId = await OptionIdAsync("Max");
        var planId = await DbAsync(async db => (await db.SubscriptionPlanConfigs.FirstAsync(p => p.IsActive && p.Line == CompanyKind.Services && !p.IsSystemTrial)).Id);
        await DbAsync(async db =>
        {
            foreach (var optionId in new[] { waId, maxId })
                if (!await db.PlanOptionRules.AnyAsync(r => r.PlanConfigId == planId && r.OptionId == optionId))
                    db.PlanOptionRules.Add(new PlanOptionRule { Id = Guid.NewGuid(), PlanConfigId = planId, OptionId = optionId, Availability = OptionAvailability.Extra });
            await db.SaveChangesAsync();
        });

        var plans = await J(await Expect(await admin.GetAsync("/api/admin/plans"), HttpStatusCode.OK));
        var catalog = (await J(await Expect(await admin.GetAsync("/api/admin/options"), HttpStatusCode.OK))).GetProperty("options").EnumerateArray().ToList();
        var dependentOptions = catalog.Count(o => o.GetProperty("code").GetString() is not (WhatsAppCode or MaxCode));
        var raw = plans.GetRawText();
        foreach (var plan in plans.GetProperty("plans").EnumerateArray())
        {
            plan.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("optionId").GetGuid()).Should().NotContain([waId, maxId], "no rule of a messenger in the answer");
            var coverage = plan.GetProperty("optionCoverage");
            coverage.GetProperty("total").GetInt32().Should().Be(dependentOptions, "the coverage counts only the options the tariff rules apply to");
            coverage.GetProperty("configured").GetInt32().Should().BeLessThanOrEqualTo(dependentOptions);
            if (dependentOptions == 0) coverage.GetProperty("text").GetString().Should().Be("Опций, зависящих от тарифа, нет");
        }
        raw.Should().NotContain("Недоступно на текущем тарифе");
    }

    // ── форма подписки ───────────────────────────────────────────────────────────────────

    private async Task<(HttpResponseMessage Response, Guid? LogId)> SaveFormAsync(HttpClient admin, Acc a, IEnumerable<object>? options, string? line = null, Guid? planId = null, DateOnly? paidUntil = null) =>
        (await admin.PutAsJsonAsync($"/api/admin/billing-accounts/{a.AccountId}/subscription", new
        {
            planId = planId ?? a.PlanId, isActive = true, paidUntil = paidUntil ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), options,
            reasonCode = "OperatorErrorCorrection", reasonDetails = "Проверка цикла 43", line,
        }), null);

    [Fact, TestCase("CY43-32")]
    public async Task SubscriptionForm_Services_MessengerLinesAreIgnored_NoRowCreatedExtendedOrEnded_NoMessage400()
    {
        await SetCatalogAsync("WhatsApp", 300);
        await SetCatalogAsync("Max", 490);
        var admin = await AdminAsync();
        var waId = await OptionIdAsync("WhatsApp");
        var maxId = await OptionIdAsync("Max");

        // an account with no messengers: lines with a date, without a date, with a quantity > 1 — all 200, nothing created
        var empty = await NewAccountAsync();
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3));
        foreach (var options in new object[][]
                 {
                     [new { optionId = maxId, quantity = 1, paidUntil = future }],
                     [new { optionId = maxId, quantity = 1, paidUntil = (DateOnly?)null }],
                     [new { optionId = waId, quantity = 1, paidUntil = (DateOnly?)null }, new { optionId = maxId, quantity = 2, paidUntil = (DateOnly?)null }],
                     [],
                 })
        {
            var (response, _) = await SaveFormAsync(admin, empty, options);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            (await TextAsync(response)).Should().NotContain("укажите дату окончания оплаты");
            (await RowAsync(empty.AccountId, "Max")).Should().BeNull("the form never creates a messenger row");
            (await RowAsync(empty.AccountId, "WhatsApp")).Should().BeNull();
        }

        // closed messenger: the form no longer answers 409 about it
        await SetCatalogAsync("Max", 490, open: false);
        var (closed, _) = await SaveFormAsync(admin, empty, [new { optionId = maxId, quantity = 1, paidUntil = (DateOnly?)null }]);
        closed.StatusCode.Should().Be(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
        await SetCatalogAsync("Max", 490);

        // an account with paid, trial and scheduled-to-end rows: the form neither extends, nor ends, nor redeems them
        var rich = await NewAccountAsync();
        var paidUntil = NoonIn(15);
        var trialUntil = NoonIn(6);
        var endsAt = NoonIn(9);
        await SeedRowAsync(rich.AccountId, "WhatsApp", paidUntil);
        await SeedRowAsync(rich.AccountId, "Max", trialUntil, trial: true);
        // after the accounts exist: the test helper lazily adds the Extra rules of the shared plan, the form must not add more
        var rulesBefore = await DbAsync(db => db.PlanOptionRules.CountAsync(r => r.Option.Code == WhatsAppCode || r.Option.Code == MaxCode));
        var waBefore = (await RowAsync(rich.AccountId, "WhatsApp"))!;
        var maxBefore = (await RowAsync(rich.AccountId, "Max"))!;
        var iteration = 0;
        foreach (var options in new object[][]
                 {
                     [], // the messengers are not listed: not ended
                     [new { optionId = waId, quantity = 1, paidUntil = (DateOnly?)null }, new { optionId = maxId, quantity = 1, paidUntil = (DateOnly?)null }],
                     [new { optionId = waId, quantity = 1, paidUntil = future }, new { optionId = maxId, quantity = 1, paidUntil = future }],
                 })
        {
            iteration++;
            var (response, _) = await SaveFormAsync(admin, rich, options);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var wa = (await RowAsync(rich.AccountId, "WhatsApp"))!;
            var max = (await RowAsync(rich.AccountId, "Max"))!;
            (wa.PaidUntilUtc, wa.EndsAtUtc, wa.GrantedByTrial, wa.Quantity).Should().Be((waBefore.PaidUntilUtc, null, false, 1), $"a paid row is not extended or ended by the form (variant {iteration})");
            (max.PaidUntilUtc, max.EndsAtUtc, max.GrantedByTrial, max.Quantity).Should().Be((maxBefore.PaidUntilUtc, null, true, 1), $"a trial row is not redeemed or ended by the form (variant {iteration})");
        }

        // a row scheduled to end stays scheduled
        await SeedRowAsync(rich.AccountId, "WhatsApp", paidUntil, endsAt: endsAt);
        var (scheduled, _) = await SaveFormAsync(admin, rich, []);
        scheduled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowAsync(rich.AccountId, "WhatsApp"))!.EndsAtUtc.Should().Be(endsAt);

        // the history of the subscription does not talk about messengers when the form is saved
        var history = await J(await Expect(await admin.GetAsync($"/api/admin/billing-accounts/{rich.AccountId}/subscription-history"), HttpStatusCode.OK));
        history.GetRawText().Should().NotContain("WhatsApp:").And.NotContain("MAX:");

        (await DbAsync(db => db.PlanOptionRules.CountAsync(r => r.Option.Code == WhatsAppCode || r.Option.Code == MaxCode))).Should().Be(rulesBefore, "the form does not touch plan rules either");
    }

    [Fact, TestCase("CY43-33")]
    public async Task SubscriptionForm_OtherLines_MessengerLinesAreIgnoredToo()
    {
        await SetCatalogAsync("Max", 490);
        var admin = await AdminAsync();
        var maxId = await OptionIdAsync("Max");
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2));
        foreach (var line in new[] { CompanyKind.Orders, CompanyKind.Stays, CompanyKind.Baths })
        {
            var planId = await DbAsync(db => db.SubscriptionPlanConfigs.Where(p => p.Line == line && p.IsActive && !p.IsSystemTrial)
                .OrderByDescending(p => p.PricePerMonth).Select(p => p.Id).FirstAsync());
            var a = await NewAccountAsync();
            await SeedRowAsync(a.AccountId, "Max", NoonIn(14));
            var before = (await RowAsync(a.AccountId, "Max"))!;
            foreach (var options in new object[][]
                     {
                         [new { optionId = maxId, quantity = 1, paidUntil = (DateOnly?)null }],
                         [new { optionId = maxId, quantity = 1, paidUntil = future }],
                         [],
                     })
            {
                var (response, _) = await SaveFormAsync(admin, a, options, line.ToString(), planId, future);
                response.StatusCode.Should().Be(HttpStatusCode.OK, $"{line}: {await response.Content.ReadAsStringAsync()}");
                var row = (await RowAsync(a.AccountId, "Max"))!;
                (row.PaidUntilUtc, row.EndsAtUtc, row.GrantedByTrial).Should().Be((before.PaidUntilUtc, null, false), $"{line}: the form leaves the messenger row alone");
            }

            // and an account of the line that has no row gets none
            var b = await NewAccountAsync();
            var (created, _) = await SaveFormAsync(admin, b, [new { optionId = maxId, quantity = 1, paidUntil = future }], line.ToString(), planId, future);
            created.StatusCode.Should().Be(HttpStatusCode.OK, $"{line}: {await created.Content.ReadAsStringAsync()}");
            (await RowAsync(b.AccountId, "Max")).Should().BeNull($"{line}: no row from the form");
        }
    }
}
