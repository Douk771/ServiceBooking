using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 28, pass A — US-28-01 (the "Записи" tariff grid with a trial). CY28-01…CY28-04 + the Q28-1 regression.
/// Written from SPEC.md (US-28-01), ARCHITECTURE_CYCLE28.md §573 and the customer's decisions Q28-1/Q28-3/Q28-4, not from the seeder's code.
/// </summary>
public class Cycle28TariffsTests(TestDatabaseFixture fixture) : Cycle28ShowcaseTestBase(fixture)
{
    private static readonly string[] GridNames = ["Студия", "Салон", "Сеть", "Пробный период"];

    private async Task<TariffCatalogReport> ApplyAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TariffCatalogSeeder>().ApplyAsync();
    }

    private Task<List<SubscriptionPlanConfig>> GridPlansAsync() =>
        DbAsync(db => db.SubscriptionPlanConfigs.AsNoTracking().Where(p => GridNames.Contains(p.Name)).ToListAsync());

    [Fact, TestCase("CY28-01")]
    public async Task Apply_OnCatalogWithoutGrid_CreatesFourRealTariffsWithRulesForEveryOption()
    {
        var report = await ApplyAsync();
        report.LockBusy.Should().BeFalse();

        var plans = await GridPlansAsync();
        plans.Select(p => p.Name).Should().BeEquivalentTo(GridNames, "US-28-01: the grid is Студия, Салон, Сеть and the trial; no duplicates");

        var byName = plans.ToDictionary(p => p.Name);
        byName["Студия"].PricePerMonth.Should().Be(790);
        byName["Студия"].MaxEmployees.Should().Be(5);
        byName["Студия"].MaxCompanies.Should().Be(1);
        byName["Салон"].PricePerMonth.Should().Be(1890);
        byName["Салон"].MaxEmployees.Should().Be(15);
        byName["Салон"].MaxCompanies.Should().Be(3, "SPEC: до 3 филиалов; в продукте филиал = компания");
        byName["Сеть"].PricePerMonth.Should().Be(3900);
        byName["Сеть"].MaxEmployees.Should().BeNull("«без лимита сотрудников»");
        byName["Пробный период"].PricePerMonth.Should().Be(0);
        byName["Пробный период"].MaxEmployees.Should().Be(15, "пробный — как «Салон»: 15 сотрудников");
        byName["Пробный период"].MaxCompanies.Should().Be(3);

        // Q28-3, variant A: photo quota and retention.
        byName["Студия"].PhotoQuotaMb.Should().Be(1000);
        byName["Салон"].PhotoQuotaMb.Should().Be(3000);
        byName["Сеть"].PhotoQuotaMb.Should().Be(10000);
        byName["Студия"].PhotoRetention.Should().Be(PhotoRetention.SixMonths);
        byName["Салон"].PhotoRetention.Should().Be(PhotoRetention.TwelveMonths);

        plans.Should().OnlyContain(p => p.IsActive && p.IsPublic, "the grid tariffs and the trial are public (cycle 18: a non-public trial is 'TrialNotOffered')");
        plans.Should().OnlyContain(p => p.Line == CompanyKind.Services);
        plans.Where(p => p.IsSystemTrial).Select(p => p.Name).Should().Equal("Пробный период");

        // Cycle-18 rule: a rule is set for EVERY option of the catalog — and mailings are unavailable.
        await DbAsync(async db =>
        {
            var options = await db.SubscriptionOptions.WhereNotRetired().Select(o => o.Id).ToListAsync();
            options.Should().NotBeEmpty();
            foreach (var plan in plans)
            {
                var rules = await db.PlanOptionRules.Where(r => r.PlanConfigId == plan.Id).ToListAsync();
                rules.Select(r => r.OptionId).Should().BeEquivalentTo(options, $"plan {plan.Name}: one explicit rule per catalog option");
                rules.Should().OnlyContain(r => r.Availability == OptionAvailability.Unavailable, "the mailing option must never be offered");
            }
        });
    }

    [Fact, TestCase("CY28-03")]
    public async Task Pricing_AfterApply_ShowsPaidGridAndTrial_WithoutMailingOptionOrShowcaseTariff()
    {
        await ApplyAsync();
        await EnsureShowcasePlanAsync(); // the hidden tariff exists on a machine with a showcase; it must never be listed
        await DbAsync(async db =>
        {
            var row = await db.PlatformSettings.FindAsync("pricing.public-enabled");
            if (row is null) db.PlatformSettings.Add(new PlatformSetting { Key = "pricing.public-enabled", Value = "true", UpdatedAt = DateTime.UtcNow });
            else row.Value = "true";
            await db.SaveChangesAsync();
        });
        using (var scope = Factory.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<PricingCatalogCache>().Invalidate();

        var response = await AnonymousClient().GetAsync("/api/pricing");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plans = doc.RootElement.GetProperty("plans").EnumerateArray().ToList();
        var names = plans.Select(p => p.GetProperty("name").GetString()).ToList();

        names.Should().Contain(["Студия", "Салон", "Сеть", "Пробный период"]);
        names.Should().NotContain(n => n!.Contains("Витрина"), "the service tariff of the showcase is never public");
        plans.Single(p => p.GetProperty("name").GetString() == "Студия").GetProperty("pricePerMonth").GetDecimal().Should().Be(790);
        plans.Single(p => p.GetProperty("name").GetString() == "Салон").GetProperty("pricePerMonth").GetDecimal().Should().Be(1890);
        plans.Single(p => p.GetProperty("name").GetString() == "Сеть").GetProperty("pricePerMonth").GetDecimal().Should().Be(3900);
        plans.Single(p => p.GetProperty("name").GetString() == "Пробный период").GetProperty("isTrial").GetBoolean().Should().BeTrue();

        doc.RootElement.GetProperty("options").GetArrayLength().Should().Be(0,
            "US-28-01: the mailing option (WhatsApp/MAX) is never shown on the price page, it has no price");
    }

    [Fact, TestCase("CY28-04")]
    public async Task Trial_AfterApply_OwnerWithoutSubscription_CanActivate_AndGetsSalonLikeLimits()
    {
        await ApplyAsync();

        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await MarkPhoneVerifiedAsync(owner.Phone, owner.UserId);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = TrialTermsRegistry.CurrentVersion });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "US-28-01: after the tariffs are filled, «Попробовать» works, no 'пробный период сейчас не предоставляется'. Body: " + body);

        var sub = await DbAsync(db => db.AccountSubscriptions.Include(s => s.PlanConfig).FirstAsync(s => s.OwnerUserId == owner.UserId));
        sub.PlanConfig!.IsSystemTrial.Should().BeTrue();
        sub.PlanConfig.MaxEmployees.Should().Be(15);
        sub.PaidUntil.Should().NotBeNull();
        sub.PaidUntil!.Value.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(5), "14 days");

        // The mailing option must not appear in «Ваша подписка» as an offer.
        var view = await AuthedClient(owner.Token).GetAsync("/api/billing/subscription");
        view.StatusCode.Should().Be(HttpStatusCode.OK);
        (await view.Content.ReadAsStringAsync()).Should().NotContain("Рассылки в WhatsApp — 1 номер");
    }

    [Fact, TestCase("CY28-04B")]
    public async Task TrialTerms_NewEditionWithoutMailingPromise_AndNoTermsVersionCollision()
    {
        // Q28-4: the trial terms edition 2026-09-30 says mailings are NOT part of the trial; the old edition is no longer current.
        TrialTermsRegistry.CurrentVersion.Should().Be("2026-09-30");

        await ApplyAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await MarkPhoneVerifiedAsync(owner.Phone, owner.UserId);

        var stale = await AuthedClient(owner.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion = "2026-09-26" });
        stale.StatusCode.Should().NotBe(HttpStatusCode.OK, "acknowledging the old edition (which promised mailings) must not activate the trial");

        var state = await AuthedClient(owner.Token).GetAsync("/api/billing/trial");
        state.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await state.Content.ReadAsStringAsync();
        text.Should().NotContain("{3}", "the mailing-window placeholder is gone from the terms");
        text.Should().Contain("Рассылки клиентам в мессенджеры в пробный период не входят");
    }

    // ── Q28-1 regression: the free tariff «Старт» ───────────────────────────────────────────────────

    [Fact, TestCase("CY28-Q1-01")]
    public async Task FreeOwner_WithoutSubscription_CanBookOnline_AddSecondEmployee_ThirdIsRefused()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var master = await AddMasterAsync(owner.Token, company.Id); // 2nd seat (the owner is the 1st)
        master.UserId.Should().NotBeNullOrEmpty();

        var third = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = UniquePhone(), firstName = "Третий", lastName = "Сотрудник", role = "Master", bio = (string?)null, email = (string?)null });
        third.StatusCode.Should().Be((HttpStatusCode)402, "Q28-1: «Старт» — 2 сотрудника");
        (await third.Content.ReadAsStringAsync()).Should().Contain("2");

        // Online booking on the free level works now (was 402 «Online booking requires a paid subscription»).
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var booking = await AnonymousClient().PostAsJsonAsync("/api/bookings", new
        {
            companyId = company.Id, serviceId = service.Id, masterId = master.UserId,
            date = date.ToString("yyyy-MM-dd"), startTime = "10:00:00", guestName = "Гость", guestPhone = UniquePhone(),
        });
        booking.StatusCode.Should().Be(HttpStatusCode.Created, await booking.Content.ReadAsStringAsync());
    }
}

/// <summary>CY28-01B — <c>ops tariffs plan</c> only shows: nothing is written (own database, so no other test can touch the catalog in between).</summary>
public class Cycle28TariffsPlanOnlyTests(TestDatabaseFixture fixture) : Cycle28ShowcaseTestBase(fixture)
{
    [Fact, TestCase("CY28-01B")]
    public async Task Plan_ChangesNothing_AndNamesWhatWouldBeCreated()
    {
        var before = await DbAsync(async db => (
            Plans: await db.SubscriptionPlanConfigs.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.Name, p.PricePerMonth, p.MaxEmployees, p.AllowOnlineBooking }).ToListAsync(),
            Rules: await db.PlanOptionRules.CountAsync()));

        var (exit, output) = await RunOpsAsync("tariffs", "plan");

        exit.Should().Be(0);
        foreach (var name in new[] { "Студия", "Салон", "Сеть", "Пробный период" })
            output.Should().Contain(name);
        output.Should().Contain("только показать");

        var after = await DbAsync(async db => (
            Plans: await db.SubscriptionPlanConfigs.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.Name, p.PricePerMonth, p.MaxEmployees, p.AllowOnlineBooking }).ToListAsync(),
            Rules: await db.PlanOptionRules.CountAsync()));
        after.Plans.Should().BeEquivalentTo(before.Plans, "plan must not write anything, including the free-tariff alignment");
        after.Rules.Should().Be(before.Rules);
    }

    [Fact, TestCase("CY28-01C")]
    public async Task Apply_AsCommand_PrintsCreatedTariffs_AndExitsZero_ThenSecondRunSaysAlreadyThere()
    {
        var (exit, output) = await RunOpsAsync("tariffs", "apply");
        exit.Should().Be(0);
        output.Should().Contain("создан: Студия");

        var (exit2, output2) = await RunOpsAsync("tariffs", "apply");
        exit2.Should().Be(0);
        output2.Should().NotContain("создан: ", "the second run creates nothing");
        output2.Should().Contain("уже есть, не трогаю: Салон");
    }
}

/// <summary>CY28-02 — own database: the test edits grid tariffs like an administrator would, which must not leak into tests that assert the grid values.</summary>
public class Cycle28TariffsAdminEditsTests(TestDatabaseFixture fixture) : Cycle28ShowcaseTestBase(fixture)
{
    private async Task<TariffCatalogReport> ApplyAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TariffCatalogSeeder>().ApplyAsync();
    }

    private Task<List<SubscriptionPlanConfig>> GridPlansAsync() =>
        DbAsync(db => db.SubscriptionPlanConfigs.AsNoTracking().Where(p => GridNames.Contains(p.Name)).ToListAsync());

    private static readonly string[] GridNames = ["Студия", "Салон", "Сеть", "Пробный период"];

    [Fact, TestCase("CY28-02")]
    public async Task Apply_Twice_NoDuplicates_AndAdminEditsSurvive()
    {
        await ApplyAsync();
        var studio = (await GridPlansAsync()).Single(p => p.Name == "Студия");

        // The administrator changes the price and the seats in the admin panel.
        await DbAsync(async db =>
        {
            var row = await db.SubscriptionPlanConfigs.FirstAsync(p => p.Id == studio.Id);
            row.PricePerMonth = 990;
            row.MaxEmployees = 7;
            await db.SaveChangesAsync();
        });

        var second = await ApplyAsync();
        second.PlansCreated.Should().Be(0, "US-28-01: repeated run creates nothing");
        second.Lines.Should().Contain(l => l.Contains("Студия") && l.Contains("990") && l.Contains("790"),
            "the report names the divergence: admin 990, grid 790 — left as is");

        var plans = await GridPlansAsync();
        plans.Should().HaveCount(4, "no duplicates");
        var after = plans.Single(p => p.Name == "Студия");
        after.PricePerMonth.Should().Be(990, "US-28-01: the admin's numbers are not overwritten");
        after.MaxEmployees.Should().Be(7);
    }

}

/// <summary>CY28-02B — own database: the administrator's own «студия» row exists BEFORE the first apply.</summary>
public class Cycle28TariffsSameNameTests(TestDatabaseFixture fixture) : Cycle28ShowcaseTestBase(fixture)
{
    private async Task<TariffCatalogReport> ApplyAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TariffCatalogSeeder>().ApplyAsync();
    }

    private Task<List<SubscriptionPlanConfig>> GridPlansAsync() =>
        DbAsync(db => db.SubscriptionPlanConfigs.AsNoTracking().Where(p => GridNames.Contains(p.Name)).ToListAsync());

    private static readonly string[] GridNames = ["Студия", "Салон", "Сеть", "Пробный период"];

    [Fact, TestCase("CY28-02B")]
    public async Task Apply_WhenAdminAlreadyHasTariffWithSameNameInOtherCase_DoesNotCreateSecondOne()
    {
        await DbAsync(async db =>
        {
            db.SubscriptionPlanConfigs.Add(new SubscriptionPlanConfig
            {
                Id = Guid.NewGuid(), Name = "СтуДИЯ", PricePerMonth = 1000, MaxEmployees = 3, MaxCompanies = 1,
                IsActive = true, IsPublic = true, Line = CompanyKind.Services, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        await ApplyAsync();

        var count = await DbAsync(db => db.SubscriptionPlanConfigs.CountAsync(p => p.Line == CompanyKind.Services && p.Name.ToLower() == "студия"));
        count.Should().Be(1, "the seeder matches by name ignoring case and never creates a twin of the admin's tariff");
    }

}
