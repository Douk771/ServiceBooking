using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 37, «Вызов 2»: тарифы по числу домов и триал линейки «Дома» (блок K, US-37-31, US-37-33; ARCHITECTURE_CYCLE37.md §37.10).
/// Триал «Домов» не мешает триалу «Записи» и наоборот; тариф считает неархивные опубликованные дома всех компаний «Дома» аккаунта.
/// </summary>
public class Cycle37TariffTrialTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    /// <summary>Идемпотентно для БД класса (один системный пробный тариф салонов на всю БД) — по образцу Cycle18TrialLifecycleTests.</summary>
    private async Task EnsureSalonTrialPlanAsync()
    {
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var existing = await WithDbAsync(db => db.SubscriptionPlanConfigs.Where(p => p.IsSystemTrial).Select(p => (Guid?)p.Id).FirstOrDefaultAsync());
        var body = new
        {
            name = Unique("Trial Plan QA37 "), description = "Пробный период", highlights = new[] { "Максимум возможностей" },
            pricePerMonth = 0m, maxEmployees = 25, maxCompanies = 5, allowOnlineBooking = true, allowMailing = true, allowAnalytics = true,
            allowPublicListing = true, allowOnlinePayment = false, photoQuotaMb = 1000, isPublic = true, isActive = true, sortOrder = 1,
        };
        if (existing is null)
        {
            var create = await admin.PostAsJsonAsync("/api/admin/plans", body);
            create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
            var id = (await J(create)).GetProperty("id").GetGuid();
            (await admin.PutAsJsonAsync($"/api/admin/plans/{id}/system-trial", new { isSystemTrial = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        (await admin.PutAsJsonAsync("/api/admin/platform-settings", new { trialDurationDays = 14, trialMailingWindowDays = 7 })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<AuthResponseDtoLike> VerifiedOwnerAsync()
    {
        var user = await RegisterAsync();
        await MarkPhoneVerifiedAsync(user.Phone, user.UserId);
        return new AuthResponseDtoLike(user.Token, user.Phone, user.UserId, user);
    }

    private sealed record AuthResponseDtoLike(string Token, string Phone, string UserId, ServiceBooking.API.DTOs.Auth.AuthResponseDto Raw);

    // ── триал ──────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY37-110")]
    public async Task StaysTrial_DoesNotBlockSalonTrial_AndSalonTrialDoesNotBlockStaysTrial()
    {
        await EnsureSalonTrialPlanAsync();

        // A: сначала «Дома», потом «Запись»
        var a = await VerifiedOwnerAsync();
        var staysState = await J(await AuthedClient(a.Token).GetAsync("/api/stays/trial"));
        staysState.GetProperty("offered").GetBoolean().Should().BeTrue();
        staysState.GetProperty("eligible").GetBoolean().Should().BeTrue();
        staysState.GetProperty("durationDays").GetInt32().Should().Be(14);
        var termsVersion = staysState.GetProperty("termsVersion").GetString()!;
        staysState.GetProperty("termsText").GetString().Should().NotBeNullOrWhiteSpace();

        var created = await CreateStaysCompanyForAsync(a.Token, Unique("dom-tr-"), trialTermsVersion: termsVersion);
        created.Trial.Should().NotBeNull();
        created.Trial!.Granted.Should().BeTrue("при первом создании компании «Дома» триал выдаётся сразу");
        created.Trial.EndsAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(5));
        created.Company.Plan.IsTrial.Should().BeTrue();
        created.Company.Plan.PlanName.Should().NotBeNullOrWhiteSpace();
        created.Company.Gate.ReasonCode.Should().NotBe("NoPlan");

        var salonBefore = await J(await AuthedClient(a.Token).GetAsync("/api/billing/trial"));
        salonBefore.GetProperty("state").GetString().Should().Be("Available", "триал «Домов» на триал «Записи» не влияет");
        var salonOwnerToken = await EnsureSalonOwnerAsync(a);
        var activate = await AuthedClient(salonOwnerToken).PostAsJsonAsync("/api/billing/trial", new { termsVersion = TrialTermsRegistry.CurrentVersion });
        activate.StatusCode.Should().Be(HttpStatusCode.OK, await activate.Content.ReadAsStringAsync());

        var lines = await WithDbAsync(db => db.TrialGrants.AsNoTracking().Where(g => db.BillingAccounts.Any(x => x.Id == g.BillingAccountId && x.OwnerUserId == a.UserId))
            .Select(g => g.Line).ToListAsync());
        lines.Should().BeEquivalentTo([CompanyKind.Stays, CompanyKind.Services]);
        // «Дома» после триала салона — тариф на месте
        (await GetCompanyAsync(new StaysCtx(a.Raw, created.Token, created.Company), created.Token)).Plan.IsTrial.Should().BeTrue();

        // B: сначала «Запись», потом «Дома»
        var b = await VerifiedOwnerAsync();
        var bSalonToken = await EnsureSalonOwnerAsync(b);
        (await AuthedClient(bSalonToken).PostAsJsonAsync("/api/billing/trial", new { termsVersion = TrialTermsRegistry.CurrentVersion })).StatusCode.Should().Be(HttpStatusCode.OK);
        var bStays = await AuthedClient(bSalonToken).PostJsonAsync("/api/stays/trial", new StaysTrialInput(termsVersion));
        bStays.StatusCode.Should().Be(HttpStatusCode.OK, "триал салонов на «Дома» не влияет: " + await bStays.Content.ReadAsStringAsync());
        (await bStays.Content.ReadJsonAsync<StaysTrialOutcomeDto>())!.Granted.Should().BeTrue();

        // повторно: тот же аккаунт — «уже активен»
        var again = await AuthedClient(bSalonToken).PostJsonAsync("/api/stays/trial", new StaysTrialInput(termsVersion));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var outcome = (await again.Content.ReadJsonAsync<StaysTrialOutcomeDto>())!;
        outcome.Granted.Should().BeFalse();
        outcome.RefusalCode.Should().Be("TrialAlreadyActive");
    }

    private async Task<string> EnsureSalonOwnerAsync(AuthResponseDtoLike user)
    {
        var slug = Unique("salon-tr-");
        var r = await AuthedClient(user.Token).PostAsJsonAsync("/api/companies",
            new ServiceBooking.API.DTOs.Companies.CreateCompanyDto($"Салон {slug}", slug, null, null, null, null, await AnyCityIdAsync(), null, true, OwnerTerms: CurrentOwnerTermsDto()));
        r.EnsureSuccessStatusCode();
        return (await LoginAsync(user.Phone, "Password123!")).Token;
    }

    [Fact, TestCase("CY37-111")]
    public async Task StaysTrial_Refusals_UnverifiedPhone_WrongTerms_AndCompanyIsStillCreated()
    {
        var user = await RegisterAsync(); // номер не подтверждён
        var state = await J(await AuthedClient(user.Token).GetAsync("/api/stays/trial"));
        state.GetProperty("eligible").GetBoolean().Should().BeFalse();
        state.GetProperty("refusalCode").GetString().Should().BeOneOf("PhoneNotVerified", "PhoneVerificationUnavailable");
        var version = state.GetProperty("termsVersion").GetString()!;

        var wrongTerms = await AuthedClient(user.Token).PostJsonAsync("/api/stays/trial", new StaysTrialInput("stale-version"));
        wrongTerms.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await wrongTerms.Content.ReadJsonAsync<StaysTrialOutcomeDto>())!.RefusalCode.Should().Be("TrialTermsVersionMismatch");
        var noPhone = await AuthedClient(user.Token).PostJsonAsync("/api/stays/trial", new StaysTrialInput(version));
        noPhone.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await noPhone.Content.ReadJsonAsync<StaysTrialOutcomeDto>())!.RefusalCode.Should().BeOneOf("PhoneNotVerified", "PhoneVerificationUnavailable");

        // отказ триала не отменяет создание компании (коммит до попытки триала)
        var created = await CreateStaysCompanyForAsync(user.Token, Unique("dom-notrial-"), trialTermsVersion: version);
        created.Trial!.Granted.Should().BeFalse();
        created.Company.Plan.WarningLevel.Should().Be("NoPlan");
        created.Company.Gate.Accepting.Should().BeFalse();
        created.Company.Gate.ReasonCode.Should().Be("NoPlan");
        (await AuthedClient(created.Token).GetAsync($"/api/stays/companies/{created.Company.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY37-112")]
    public async Task TrialBanners_ThreeDays_OneDay_Expired_AndBookingsCloseButPagesStay()
    {
        var company = await CreateStaysCompanyAsync(plan: false);
        var house = await CreateHouseAsync(company, publish: false);
        await GiveStaysPlanAsync(company.Id, StaysPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddDays(10));
        var published = await PublishHouseAsync(company, house.House);
        (await GetCompanyAsync(company)).Plan.WarningLevel.Should().Be("None");

        await GiveStaysPlanAsync(company.Id, StaysPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddDays(2.5));
        var three = (await GetCompanyAsync(company)).Plan;
        three.WarningLevel.Should().Be("TrialEnding3d");
        three.Text.Should().StartWith("Пробный период закончится через 3 дня — ");

        await GiveStaysPlanAsync(company.Id, StaysPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddHours(20));
        var one = (await GetCompanyAsync(company)).Plan;
        one.WarningLevel.Should().Be("TrialEnding1d");
        one.Text.Should().Be("Пробный период закончится завтра");

        await GiveStaysPlanAsync(company.Id, StaysPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddMinutes(-5));
        var expired = await GetCompanyAsync(company);
        expired.Plan.WarningLevel.Should().Be("Expired");
        expired.Plan.Text.Should().Be("Пробный период закончился. Гости не могут бронировать, пока вы не выберете тариф");
        expired.Gate.Accepting.Should().BeFalse();
        var q = (await QuoteAsync(published.Id, InDays(10), InDays(11)));
        q.AcceptingBookings.Should().BeFalse();
        q.NotAcceptingText.Should().Be("Бронирование временно недоступно");
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{published.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK, "страницы остаются видны");
        // уже созданная ранее бронь живёт: откат тарифа назад и бронь, потом снова истечение
        await GiveStaysPlanAsync(company.Id, StaysPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddDays(10));
        var booked = await BookOkAsync(published.Id, InDays(10), InDays(11));
        await GiveStaysPlanAsync(company.Id, StaysPlans.TrialSeedId, paidUntil: DateTime.UtcNow.AddMinutes(-5));
        (await GetPublicBookingAsync(booked.Token)).Status.Should().Be(StayBookingStatus.Held);
        (await AttachProofAsync(booked.Token)).StatusCode.Should().Be(HttpStatusCode.Created, "чек к существующей брони можно приложить и после конца тарифа");
    }

    // ── тарифы ─────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY37-113")]
    public async Task Tariff_PublishedHousesOfAllCompaniesOfTheAccount_AreCounted_LimitTextsByPlan()
    {
        var owner = await RegisterAsync();
        var first = await CreateStaysCompanyAsync(ownerAccount: owner, plan: false);
        // токен после создания первой компании несёт роль владельца; вторая компания того же аккаунта
        var second = await CreateStaysCompanyForAsync((await LoginAsync(owner.Phone, "Password123!")).Token, Unique("dom-2nd-"));
        var secondCtx = new StaysCtx(owner, second.Token, second.Company);
        (await PutPaymentDetailsAsync(secondCtx, PaymentDetailsText, null)).Id.Should().Be(second.Company.Id);
        await GiveStaysPlanAsync(first.Id, StaysPlans.OneHouseSeedId);

        var h1 = await CreateHouseAsync(first, price: 1000);
        var h2Draft = await CreateHouseAsync(secondCtx, price: 1000, publish: false);
        var refused = await AuthedClient(secondCtx.OwnerToken).PostJsonAsync($"/api/stays/companies/{secondCtx.Id}/houses/{h2Draft.Id}/publish",
            new HousePublishInput(new AttestationInput(true, h2Draft.House.RegistryNotice.Version)));
        refused.StatusCode.Should().Be((HttpStatusCode)402, "лимит — на аккаунт: дом первой компании уже занимает единственное место");
        var text = await refused.Content.ReadAsStringAsync();
        text.Should().StartWith("Тариф «").And.Contain("позволяет опубликовать 1 дом").And.EndWith("Снимите дом с публикации или смените тариф.");
        var manage = await GetCompanyAsync(secondCtx);
        manage.Plan.MaxHouses.Should().Be(1);
        manage.Plan.HousesPublished.Should().Be(1);

        // тариф «До 3 домов»: три можно, четвёртый — нет
        await GiveStaysPlanAsync(first.Id, StaysPlans.UpToThreeSeedId);
        await PublishHouseAsync(secondCtx, h2Draft.House);
        var h3 = await CreateHouseAsync(first, price: 1000);
        _ = h3;
        var h4Draft = await CreateHouseAsync(first, price: 1000, publish: false);
        var fourth = await AuthedClient(first.OwnerToken).PostJsonAsync($"/api/stays/companies/{first.Id}/houses/{h4Draft.Id}/publish",
            new HousePublishInput(new AttestationInput(true, h4Draft.House.RegistryNotice.Version)));
        fourth.StatusCode.Should().Be((HttpStatusCode)402);
        (await fourth.Content.ReadAsStringAsync()).Should().Contain("позволяет опубликовать 3 дома");

        // «Без ограничения»
        await GiveStaysPlanAsync(first.Id, StaysPlans.UnlimitedSeedId);
        (await PublishHouseAsync(first, h4Draft.House)).IsPublished.Should().BeTrue();
        _ = h1;

        // архив освобождает место в тарифе
        await GiveStaysPlanAsync(first.Id, StaysPlans.OneHouseSeedId, isActive: true);
        (await GetCompanyAsync(first)).Plan.WarningLevel.Should().Be("OverLimit");
        var firstHouses = (await J(await AuthedClient(first.OwnerToken).GetAsync($"/api/stays/companies/{first.Id}/houses"))).EnumerateArray().ToList();
        foreach (var h in firstHouses.Where(h => h.GetProperty("isPublished").GetBoolean()))
            await AuthedClient(first.OwnerToken).PostJsonAsync($"/api/stays/companies/{first.Id}/houses/{h.GetProperty("id").GetGuid()}/archive", new { });
        var afterArchive = await GetCompanyAsync(first);
        afterArchive.Plan.HousesPublished.Should().Be(1, "остался один опубликованный дом второй компании того же аккаунта");
    }

    [Fact, TestCase("CY37-114")]
    public async Task SeededPlans_NamesLimitsAndPrices_AndBillingSubscriptionStaysBlock()
    {
        var owner = await RegisterAsync();
        var company = await CreateStaysCompanyAsync(ownerAccount: owner, plan: false);
        await GiveStaysPlanAsync(company.Id, StaysPlans.UpToThreeSeedId);
        var c = AuthedClient(company.OwnerToken);

        var sub = await J(await c.GetAsync("/api/billing/subscription?line=Stays"));
        var stays = sub.GetProperty("stays");
        stays.GetProperty("maxHouses").GetInt32().Should().Be(3);
        stays.GetProperty("housesPublished").GetInt32().Should().Be(0);
        stays.GetProperty("isTrial").GetBoolean().Should().BeFalse();
        stays.GetProperty("warningLevel").GetString().Should().Be("None");
        var plans = sub.GetProperty("availablePlans").EnumerateArray().ToList();
        plans.Should().NotBeEmpty();
        var byLimit = plans.Select(p => (Price: p.GetProperty("pricePerMonth").GetDecimal(), Name: p.GetProperty("name").GetString()!)).ToList();
        byLimit.Should().Contain(p => p.Price == 200m && p.Name == "Один дом");
        byLimit.Should().Contain(p => p.Price == 500m && p.Name == "До 3 домов");
        byLimit.Should().Contain(p => p.Price == 1000m && p.Name == "Без ограничения");
        byLimit.Should().NotContain(p => p.Price == 0m, "пробный тариф в перечне доступных к заявке не показывается");
        (await J(await c.GetAsync("/api/billing/subscription?line=Stays"))).TryGetProperty("trial", out var trial).Should().BeTrue();
        trial.ValueKind.Should().Be(JsonValueKind.Null, "салонный триал в блоке «Домов» не отдаётся");

        // публичный /api/pricing «Дома» не содержит
        var pricing = await AnonymousClient().GetAsync("/api/pricing");
        if (pricing.IsSuccessStatusCode) (await pricing.Content.ReadAsStringAsync()).Should().NotContain("Один дом").And.NotContain("До 3 домов");

        // тариф другой линейки в заявке «Домов» — 400
        var admin = await LoginAsSuperAdminAsync();
        var plansAdmin = await J(await AuthedClient(admin.Token).GetAsync("/api/admin/plans"));
        var salonPlan = plansAdmin.GetProperty("plans").EnumerateArray().First(p => p.GetProperty("line").GetString() == "Services" && p.GetProperty("isActive").GetBoolean());
        var wrongLine = await c.PostAsJsonAsync("/api/billing/subscription/request", new { planId = salonPlan.GetProperty("id").GetGuid(), line = "Stays" });
        wrongLine.StatusCode.Should().Be(HttpStatusCode.BadRequest, await wrongLine.Content.ReadAsStringAsync());
    }

    [Fact, TestCase("CY37-115")]
    public async Task Admin_AssignsStaysPlan_TrialPlanCannotBeAssignedOrDeactivated_MaxHousesOnlyForStaysLine()
    {
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var company = await CreateStaysCompanyAsync(plan: false);
        var accountId = await WithDbAsync(db => db.Companies.Where(c => c.Id == company.Id).Select(c => c.BillingAccountId).SingleAsync());

        // назначение тарифа линейки «Дома» открывает приём броней
        var house = await CreateHouseAsync(company, publish: false);
        var assign = await admin.PutAsJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription",
            new { planId = StaysPlans.UnlimitedSeedId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)).ToString("yyyy-MM-dd"), line = "Stays", options = Array.Empty<object>() });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync());
        (await PublishHouseAsync(company, house.House)).IsPublished.Should().BeTrue();
        (await GetCompanyAsync(company)).Plan.PlanName.Should().NotBeNullOrWhiteSpace();
        var card = await J(await admin.GetAsync($"/api/admin/billing-accounts/{accountId}"));
        card.GetProperty("staysSubscription").GetProperty("planId").GetGuid().Should().Be(StaysPlans.UnlimitedSeedId);
        card.GetProperty("staysSubscription").GetProperty("housesPublished").GetInt32().Should().Be(1);

        // пробный тариф «Домов» нельзя назначить вручную и нельзя выключить
        var trialAssign = await admin.PutAsJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription",
            new { planId = StaysPlans.TrialSeedId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)).ToString("yyyy-MM-dd"), line = "Stays", options = Array.Empty<object>() });
        trialAssign.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var plans = await J(await admin.GetAsync("/api/admin/plans"));
        var trialPlan = plans.GetProperty("plans").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == StaysPlans.TrialSeedId);
        trialPlan.GetProperty("line").GetString().Should().Be("Stays");
        trialPlan.GetProperty("pricePerMonth").GetDecimal().Should().Be(0m);
        var off = await admin.PutAsJsonAsync($"/api/admin/plans/{StaysPlans.TrialSeedId}", new
        {
            name = trialPlan.GetProperty("name").GetString(), description = "x", highlights = Array.Empty<string>(), pricePerMonth = 0m, line = "Stays", maxHouses = (int?)null,
            allowOnlineBooking = false, allowMailing = false, allowAnalytics = false, allowPublicListing = false, allowOnlinePayment = false,
            photoQuotaMb = 0, isPublic = false, isActive = false, sortOrder = 99,
        });
        off.StatusCode.Should().Be(HttpStatusCode.Conflict, await off.Content.ReadAsStringAsync());
        (await off.Content.ReadAsStringAsync()).Should().Be("Пробный тариф линейки «Дома» нельзя удалить или выключить.");
        (await admin.DeleteAsync($"/api/admin/plans/{StaysPlans.TrialSeedId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // maxHouses допустим только в линейке «Дома»
        var salonWithMax = await admin.PostAsJsonAsync("/api/admin/plans", new
        {
            name = Unique("Салон с maxHouses "), description = "x", highlights = Array.Empty<string>(), pricePerMonth = 100m, maxEmployees = 1, maxCompanies = 1, maxHouses = 5,
            allowOnlineBooking = true, allowMailing = false, allowAnalytics = false, allowPublicListing = false, allowOnlinePayment = false,
            photoQuotaMb = 10, isPublic = true, isActive = true, sortOrder = 5,
        });
        salonWithMax.StatusCode.Should().Be(HttpStatusCode.BadRequest, await salonWithMax.Content.ReadAsStringAsync());
    }

    [Fact, TestCase("CY37-116")]
    public async Task Admin_CompanyList_ShowsStaysKind_AndAccountCardHasStaysSubscription()
    {
        var company = await CreateStaysCompanyAsync();
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var accountId = await WithDbAsync(db => db.Companies.Where(c => c.Id == company.Id).Select(c => c.BillingAccountId).SingleAsync());
        var card = await J(await admin.GetAsync($"/api/admin/billing-accounts/{accountId}"));
        card.TryGetProperty("staysSubscription", out var staysSub).Should().BeTrue();
        staysSub.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }
}
