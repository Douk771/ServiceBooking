using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 19 — SPEC_CYCLE19_TARIFF_LIMITS_GEOCODER.md US-19-01…US-19-05, ARCHITECTURE_CYCLE19.md §382-§408. Written from SPEC_CYCLE19_TARIFF_LIMITS_GEOCODER.md's
/// acceptance criteria, independently of AdminController/AdminBillingController/BillingController's own
/// implementation. Covers: "employees"/"companies" retired from the sellable option catalog everywhere
/// they could be selected, bought or shown, and the account limit formula (tariff field + grandfathered
/// employee bonus only, no purchased extra-* contribution).
/// </summary>
public class Cycle19TariffLimitsTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── US-19-01: option-capabilities / options catalog ─────────────────────────────────────────────

    [Fact, TestCase("CY19-01")]
    public async Task OptionCapabilities_DoesNotList_EmployeesOrCompanies()
    {
        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/option-capabilities");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CapabilitiesEnvelope>(JsonHelpers.Options);
        body!.Capabilities.Should().NotContain(c => c.Key == "employees");
        body.Capabilities.Should().NotContain(c => c.Key == "companies");
    }

    [Theory, TestCase("CY19-02")]
    [InlineData("employees")]
    [InlineData("companies")]
    public async Task CreateOption_WithLimitCapability_Returns400_WithRussianText(string capabilityKey)
    {
        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).PostJsonAsync("/api/admin/options", new
        {
            code = Unique("extra-limit-"), name = "Доп. что-то", kind = "Quantity",
            capabilityKey, unitName = "шт",
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().MatchRegex("[а-яА-Я]", "the error text must be in Russian per SPEC US-19-01");
    }

    [Fact, TestCase("CY19-03")]
    public async Task UpdateOption_ToLimitCapability_Returns400()
    {
        var admin = await LoginAsSuperAdminAsync();
        var create = await AuthedClient(admin.Token).PostJsonAsync("/api/admin/options", new
        {
            code = Unique("addon-"), name = "Доп. номер", kind = "Quantity", unitName = "номер",
        });
        create.EnsureSuccessStatusCode();
        var option = (await create.Content.ReadJsonAsync<AdminOptionDto>())!;

        var update = await AuthedClient(admin.Token).PutJsonAsync($"/api/admin/options/{option.Id}", new
        {
            code = option.Code, name = option.Name, kind = "Quantity", unitName = "номер",
            capabilityKey = "employees",
        });
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY19-04")]
    public async Task UpdateRetiredOption_Returns409_BeforeAnyOtherValidation()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (retiredId, code) = await GetRetiredOptionAsync("extra-employees");

        var update = await AuthedClient(admin.Token).PutJsonAsync($"/api/admin/options/{retiredId}", new
        {
            code, name = "Изменённое имя", kind = "Quantity", unitName = "чел.",
        });
        update.StatusCode.Should().Be(HttpStatusCode.Conflict, "ARCHITECTURE_CYCLE19.md §403: 409 on a retired option comes before other checks");
    }

    [Fact, TestCase("CY19-05")]
    public async Task GetOptions_DoesNotList_RetiredExtraOptions()
    {
        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/options");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OptionsEnvelope>(JsonHelpers.Options);
        body!.Options.Should().NotContain(o => o.Code == "extra-employees");
        body.Options.Should().NotContain(o => o.Code == "extra-companies");
    }

    // ── US-19-01: PUT /api/admin/plans/{id} with an extra-* rule in the body ───────────────────────

    [Fact, TestCase("CY19-06")]
    public async Task UpdatePlan_WithRetiredOptionRuleInBody_DoesNotFail_AndDoesNotResurrectTheRule()
    {
        var admin = await LoginAsSuperAdminAsync();
        var plan = await CreatePlanAsync(admin.Token);
        var (retiredId, _) = await GetRetiredOptionAsync("extra-employees");

        var body = FullPlanBody(plan, options: new List<object>
        {
            new { optionId = retiredId, availability = "Extra", includedQuantity = 5 },
        });
        var response = await AuthedClient(admin.Token).PutJsonAsync($"/api/admin/plans/{plan.Id}", body);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, "an old cached frontend sending extra-* in the matrix must never 500");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = await db.PlanOptionRules.FirstOrDefaultAsync(r => r.PlanConfigId == plan.Id && r.OptionId == retiredId);
        rule.Should().BeNull("a retired limit option must never end up with a live rule again, whatever the response code");
    }

    // ── US-19-01: public pricing never shows extra-* even if forced active/public/priced ───────────

    [Fact, TestCase("CY19-07")]
    public async Task PublicPricing_NeverShows_RetiredOption_EvenIfForcedActivePublicPriced()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var retired = await db.SubscriptionOptions.FirstAsync(o => o.Code == "extra-employees");
            retired.IsActive = true;
            retired.IsPublic = true;
            retired.PricePerMonth = 199m;
            await db.SaveChangesAsync();
        }

        await EnablePublicPricingAsync();
        var pricing = await (await AnonymousClient().GetAsync("/api/pricing")).Content.ReadJsonAsync<PublicPricingDto>();
        pricing!.Options.Should().NotContain(o => o.Name.Contains("Дополнительны"));
    }

    // ── US-19-02: assigning a subscription with extra-* in the body ────────────────────────────────

    [Fact, TestCase("CY19-08")]
    public async Task AssignSubscription_WithRetiredOptionInBody_RejectedWithoutSavingAnything()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var plan = await CreatePlanAsync(admin.Token);
        var (retiredId, _) = await GetRetiredOptionAsync("extra-companies");

        Guid accountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            accountId = await db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync();
        }

        var response = await AuthedClient(admin.Token).PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", new
        {
            planId = plan.Id, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            options = new[] { new { optionId = retiredId, quantity = 1, paidUntil = (DateOnly?)null } },
        });
        ((int)response.StatusCode).Should().BeInRange(400, 499, "a retired-limit option in the request must be rejected, not 500 and not silently dropped");

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db2.AccountSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == accountId);
        sub.Should().BeNull("nothing must be saved when the request is rejected");
    }

    // ── US-19-02: overflow check uses tariff + grandfathered bonus, not purchases ──────────────────

    [Fact, TestCase("CY19-09")]
    public async Task AssignSubscription_OverflowCheck_UsesTariffPlusBonus_NoConfirm_NoOverflow()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var plan = await CreatePlanAsync(admin.Token, maxEmployees: 5);

        Guid accountId = await SeedAccountWithEmployeesAndBonusAsync(owner.UserId, company.Id, seatsUsed: 7, bonus: 2);

        var response = await AuthedClient(admin.Token).PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", new
        {
            planId = plan.Id, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            options = Array.Empty<object>(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "5 (tariff) + 2 (bonus) = 7, exactly equal to usage — must not need confirmLimitOverflow");
    }

    [Fact, TestCase("CY19-10")]
    public async Task AssignSubscription_OverflowCheck_UsesTariffPlusBonus_Over_Returns409WithoutConfirm()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var plan = await CreatePlanAsync(admin.Token, maxEmployees: 5);

        Guid accountId = await SeedAccountWithEmployeesAndBonusAsync(owner.UserId, company.Id, seatsUsed: 8, bonus: 2);

        var response = await AuthedClient(admin.Token).PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", new
        {
            planId = plan.Id, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            options = Array.Empty<object>(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "5 (tariff) + 2 (bonus) = 7 < 8 used — must 409 without confirmLimitOverflow");

        var confirmed = await AuthedClient(admin.Token).PutJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", new
        {
            planId = plan.Id, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            options = Array.Empty<object>(), confirmLimitOverflow = true,
        });
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, "confirmLimitOverflow must still let the operator proceed");
    }

    // ── US-19-03: canAddEmployee/402 still uses the (new) formula end to end ───────────────────────

    [Fact, TestCase("CY19-11")]
    public async Task CanAddEmployee_And_402_ReflectTariffPlusBonusFormula()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var plan = await CreatePlanAsync(admin.Token, maxEmployees: 1);
        await SetSubscriptionAsync(company.Id, plan.Id);

        // Grant a +1 bonus directly (the migration path is covered at the unit level;
        // this proves the bonus is actually READ by the seat-limit gate, end to end).
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = await db.BillingAccounts.FirstAsync(a => a.OwnerUserId == owner.UserId);
            account.GrandfatheredEmployeeBonus = 1;
            await db.SaveChangesAsync();
        }

        var first = await AddMasterAsync(owner.Token, company.Id);
        first.Should().NotBeNull("tariff (1) + bonus (1) = 2, first member (the owner) plus one master must fit");

        var candidate = await RegisterAsync();
        var second = await AuthedClient(owner.Token).PostJsonAsync($"/api/companies/{company.Id}/members", new
        {
            phone = candidate.Phone, firstName = candidate.FirstName, lastName = candidate.LastName,
            role = "Master", bio = (string?)null, email = (string?)null,
        });
        second.StatusCode.Should().Be((HttpStatusCode)402, "one more than tariff+bonus must still 402");

        var companyDto = (await (await AuthedClient(owner.Token).GetAsync("/api/companies/my")).Content.ReadJsonAsync<List<CompanyDto>>())!
            .Single(c => c.Id == company.Id);
        companyDto.AccountSeatsLimit.Should().Be(2);
        companyDto.CanAddEmployee.Should().BeFalse();
    }

    // ── US-19-04: owner-facing subscription request with extra-* ───────────────────────────────────

    [Fact, TestCase("CY19-12")]
    public async Task SubmitRequest_WithRetiredOption_RejectedWithoutSavingAnything()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var (retiredId, _) = await GetRetiredOptionAsync("extra-employees");

        var response = await AuthedClient(owner.Token).PostJsonAsync("/api/billing/subscription/request", new
        {
            options = new[] { new { optionId = retiredId, quantity = 1 } },
        });
        ((int)response.StatusCode).Should().BeInRange(400, 499);

        var subscription = await (await AuthedClient(owner.Token).GetAsync("/api/billing/subscription")).Content.ReadJsonAsync<OwnerSubscriptionDto>();
        subscription!.PendingRequest.Should().BeNull("a rejected request must not create a pending request");
    }

    [Fact, TestCase("CY19-13")]
    public async Task OwnerSubscription_AvailableOptions_NeverIncludesRetiredExtraOptions()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var subscription = await (await AuthedClient(owner.Token).GetAsync("/api/billing/subscription")).Content.ReadJsonAsync<OwnerSubscriptionDto>();
        subscription!.AvailableOptions.Should().NotContain(o => o.Name.Contains("Дополнительны"));
        subscription.Options.Should().NotContain(o => o.Name.Contains("Дополнительны"));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private record CapabilityItem(string Key, string Kind, string Name);
    private record CapabilitiesEnvelope(List<CapabilityItem> Capabilities);
    private record OptionsEnvelope(List<AdminOptionDto> Options);

    private static object FullPlanBody(AdminPlanDto plan, List<object>? options = null) => new
    {
        name = plan.Name, description = plan.Description, highlights = plan.Highlights,
        pricePerMonth = plan.PricePerMonth, maxEmployees = plan.MaxEmployees, maxCompanies = plan.MaxCompanies,
        allowOnlineBooking = plan.AllowOnlineBooking, allowMailing = plan.AllowMailing,
        allowAnalytics = plan.AllowAnalytics, allowPublicListing = plan.AllowPublicListing,
        allowOnlinePayment = plan.AllowOnlinePayment, photoQuotaMb = plan.PhotoQuotaMb,
        photoRetention = plan.PhotoRetention.ToString(), notifyDaysBefore = plan.NotifyDaysBefore,
        isActive = plan.IsActive, isPublic = (object?)plan.IsPublic, sortOrder = (object?)plan.SortOrder,
        options = (object?)options,
    };

    private async Task<AdminPlanDto> CreatePlanAsync(string token, int? maxEmployees = 5, int? maxCompanies = 1, decimal price = 990)
    {
        var body = new
        {
            name = Unique("Тариф "), description = "Описание", highlights = new[] { "Пункт 1" },
            pricePerMonth = price, maxEmployees, maxCompanies,
            allowOnlineBooking = true, allowMailing = false, allowAnalytics = false,
            allowPublicListing = true, allowOnlinePayment = false,
            photoQuotaMb = 100, photoRetention = "SixMonths", notifyDaysBefore = 7,
            isActive = true, isPublic = true, sortOrder = 3,
        };
        var response = await AuthedClient(token).PostAsJsonAsync("/api/admin/plans", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadJsonAsync<AdminPlanDto>())!;
    }

    /// <summary>Cycle 19's own seed (20260922121140_SeedBillingCatalog) ships exactly two retired
    /// limit options, extra-employees/extra-companies, IsActive=false since the cycle-19 migration —
    /// still present as data (§383.2), never deleted.</summary>
    private async Task<(Guid Id, string Code)> GetRetiredOptionAsync(string code)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var option = await db.SubscriptionOptions.FirstAsync(o => o.Code == code);
        return (option.Id, option.Code);
    }

    private async Task EnablePublicPricingAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.PlatformSettings.FindAsync("pricing.public-enabled");
        if (row is null)
            db.PlatformSettings.Add(new PlatformSetting { Key = "pricing.public-enabled", Value = "true", UpdatedAt = DateTime.UtcNow });
        else
            row.Value = "true";
        await db.SaveChangesAsync();
    }

    /// <summary>Attaches the company to a fresh billing account (paid, unlimited-ish plan) with
    /// <paramref name="seatsUsed"/> company members total and a
    /// <see cref="BillingAccount.GrandfatheredEmployeeBonus"/> of <paramref name="bonus"/>.</summary>
    private async Task<Guid> SeedAccountWithEmployeesAndBonusAsync(string ownerUserId, Guid companyId, int seatsUsed, int bonus)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.BillingAccounts.FirstAsync(a => a.OwnerUserId == ownerUserId);
        account.GrandfatheredEmployeeBonus = bonus;

        // seatsUsed - 1 additional members beyond the owner (who already counts as a seat via
        // CompanyMembers created at company-creation time in the shared test helper).
        var existing = await db.CompanyMembers.CountAsync(m => m.CompanyId == companyId);
        for (var i = existing; i < seatsUsed; i++)
        {
            var userId = Guid.NewGuid().ToString();
            var email = Unique("seat-") + "@test.local";
            db.Users.Add(new AppUser
            {
                Id = userId, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(),
                FirstName = "Seat", LastName = i.ToString(), EmailConfirmed = true,
            });
            db.CompanyMembers.Add(new CompanyMember
            {
                Id = Guid.NewGuid(), CompanyId = companyId, UserId = userId, Role = UserRole.Master, CommissionPercent = 0,
            });
        }
        await db.SaveChangesAsync();
        return account.Id;
    }
}
