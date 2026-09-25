using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 15, Block D (US-15-07/08/09, T-D3/T-D4) — "устаревший тариф" (IsActive:true +
/// IsPublic:false) is expressed with the EXISTING pair of flags, no new status field
/// (SPEC_CYCLE15_BOOKING_CARD_TARIFF.md §0-bis П3, ARCHITECTURE_CYCLE15.md §255). Written against SPEC_CYCLE15_BOOKING_CARD_TARIFF.md §5/API_CONTRACT_CYCLE15.md
/// §288, not against the implementation. PlansTab.tsx's own defect (constants sent on every save) is a
/// frontend concern covered by PlansTab.test.tsx — these tests exercise the SERVER'S contract for the
/// same endpoint directly over HTTP, independent of the admin UI.
/// </summary>
public class Cycle15PlansTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // A full, valid PUT /api/admin/plans/{id} body built from the plan's OWN current values —
    // mirrors what a correctly-fixed admin form sends: everything the operator can see, but isPublic/
    // sortOrder ONLY when the caller explicitly wants to touch them (the T-D... contract under test).
    private static object FullPlanBody(AdminPlanDto plan, decimal? pricePerMonth = null, bool? isPublic = null, int? sortOrder = null, bool? isActive = null) => new
    {
        name = plan.Name,
        description = plan.Description,
        highlights = plan.Highlights,
        pricePerMonth = pricePerMonth ?? plan.PricePerMonth,
        maxEmployees = plan.MaxEmployees,
        maxCompanies = plan.MaxCompanies,
        allowOnlineBooking = plan.AllowOnlineBooking,
        allowMailing = plan.AllowMailing,
        allowAnalytics = plan.AllowAnalytics,
        allowPublicListing = plan.AllowPublicListing,
        allowOnlinePayment = plan.AllowOnlinePayment,
        photoQuotaMb = plan.PhotoQuotaMb,
        photoRetention = plan.PhotoRetention.ToString(),
        notifyDaysBefore = plan.NotifyDaysBefore,
        isActive = isActive ?? plan.IsActive,
        isPublic = (object?)isPublic,
        sortOrder = (object?)sortOrder,
    };

    private async Task<AdminPlanDto> CreatePlanAsync(string token, bool isPublic = true, int sortOrder = 3, decimal price = 990)
    {
        var body = new
        {
            name = Unique("Тариф "),
            description = "Описание",
            highlights = new[] { "Пункт 1" },
            pricePerMonth = price,
            maxEmployees = 5,
            maxCompanies = 1,
            allowOnlineBooking = true,
            allowMailing = false,
            allowAnalytics = false,
            allowPublicListing = true,
            allowOnlinePayment = false,
            photoQuotaMb = 100,
            photoRetention = "SixMonths",
            notifyDaysBefore = 7,
            isActive = true,
            isPublic,
            sortOrder,
        };
        var response = await AuthedClient(token).PostAsJsonAsync("/api/admin/plans", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadJsonAsync<AdminPlanDto>())!;
    }

    [Fact, TestCase("CY15-D3-01")]
    public async Task HidingFromStorefront_DisappearsFromPricingButStaysInAdminList_WithHiddenLabelState()
    {
        var admin = await LoginAsSuperAdminAsync();
        var plan = await CreatePlanAsync(admin.Token, isPublic: true, sortOrder: 3);
        await EnablePublicPricingAsync();

        var pricingBefore = await (await AnonymousClient().GetAsync("/api/pricing")).Content.ReadJsonAsync<PublicPricingDto>();
        pricingBefore!.Plans.Should().Contain(p => p.Id == plan.Id);

        var update = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{plan.Id}", FullPlanBody(plan, isPublic: false));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await update.Content.ReadJsonAsync<AdminPlanDto>())!;
        updated.IsPublic.Should().BeFalse();
        updated.IsActive.Should().BeTrue("устаревший тариф остаётся isActive:true — только витрина скрыта");

        // Gone from the public storefront...
        var pricingAfter = await (await AnonymousClient().GetAsync("/api/pricing")).Content.ReadJsonAsync<PublicPricingDto>();
        pricingAfter!.Plans.Should().NotContain(p => p.Id == plan.Id);

        // ...but still present (and assignable, isActive still true) in the admin list.
        var adminList = await (await AuthedClient(admin.Token).GetAsync("/api/admin/plans")).Content.ReadJsonAsync<AdminPlansListDto>();
        var stillListed = adminList!.Plans.Should().ContainSingle(p => p.Id == plan.Id).Subject;
        stillListed.IsPublic.Should().BeFalse();
        stillListed.IsActive.Should().BeTrue("скрыт от витрины — не значит скрыт от администратора");
    }

    [Fact, TestCase("CY15-D3-02")]
    public async Task CompanyOnHiddenPlan_KeepsLimitsOptionsAndOnlineBooking_Unchanged()
    {
        var admin = await LoginAsSuperAdminAsync();
        var plan = await CreatePlanAsync(admin.Token, isPublic: true);

        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await SetSubscriptionAsync(company.Id, plan.Id);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        // Hide the plan from the storefront AFTER the company is already on it.
        (await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{plan.Id}", FullPlanBody(plan, isPublic: false)))
            .EnsureSuccessStatusCode();

        // The subscription's own view is untouched: plan name unchanged...
        var subscription = await (await AuthedClient(owner.Token).GetAsync("/api/billing/subscription")).Content.ReadJsonAsync<OwnerSubscriptionDto>();
        subscription!.Plan.Name.Should().Be(plan.Name);

        // ...and online booking still actually works end-to-end for this company (US-15-09), not just
        // a flag on a DTO: AllowOnlineBooking is copied from the plan itself, never gated by IsPublic
        // (R8/§255.3 — the only reader of IsPublic is PricingCatalogBuilder).
        var client = await RegisterAsync();
        var booking = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        booking.StatusCode.Should().Be(HttpStatusCode.Created, "a hidden-from-storefront plan must not block online booking for existing subscribers");
    }

    [Fact, TestCase("CY15-D3-03")]
    public async Task SuperAdmin_ExtendsSubscription_OnHiddenPlan_SucceedsWithoutForcedPlanChange()
    {
        var admin = await LoginAsSuperAdminAsync();
        var plan = await CreatePlanAsync(admin.Token, isPublic: true);
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await SetSubscriptionAsync(company.Id, plan.Id, paidUntil: DateTime.UtcNow.AddDays(5));
        (await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{plan.Id}", FullPlanBody(plan, isPublic: false)))
            .EnsureSuccessStatusCode();

        Guid accountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            accountId = await db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync();
        }

        var newPaidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2));
        var extend = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/billing-accounts/{accountId}/subscription", new
        {
            planId = plan.Id,
            isActive = true,
            paidUntil = newPaidUntil,
            options = Array.Empty<object>(),
        });
        extend.StatusCode.Should().Be(HttpStatusCode.OK, "extending a subscriber already on a hidden plan must not be blocked by the plan's own visibility");
    }

    [Fact, TestCase("CY15-D4-01")]
    public async Task SystemFreePlan_OnStorefront_CannotBeHidden_Returns409()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // 20260922121140_SeedBillingCatalog seeds exactly one system-free plan into every fresh DB.
        var systemFree = await db.SubscriptionPlanConfigs.FirstAsync(p => p.IsSystemFree);
        scope.Dispose();

        var admin = await LoginAsSuperAdminAsync();
        var current = await (await AuthedClient(admin.Token).GetAsync("/api/admin/plans")).Content.ReadJsonAsync<AdminPlansListDto>();
        var dto = current!.Plans.Single(p => p.Id == systemFree.Id);

        // The shipped seed ships isPublic:false already (FixSeedBillingCatalogCapabilityKeys) — put it
        // ON the storefront first so this test exercises the true->false TRANSITION §255.4 guards.
        if (!dto.IsPublic)
        {
            var putOn = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{systemFree.Id}", FullPlanBody(dto, isPublic: true));
            putOn.StatusCode.Should().Be(HttpStatusCode.OK);
            dto = (await putOn.Content.ReadJsonAsync<AdminPlanDto>())!;
        }

        var hide = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{systemFree.Id}", FullPlanBody(dto, isPublic: false));
        hide.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await hide.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace();

        // Restore state for any other test relying on the seeded system-free plan being public.
        var reread = await (await AuthedClient(admin.Token).GetAsync("/api/admin/plans")).Content.ReadJsonAsync<AdminPlansListDto>();
        reread!.Plans.Single(p => p.Id == systemFree.Id).IsPublic.Should().BeTrue("the 409 must not have partially applied");
    }

    [Fact, TestCase("CY15-D4-02")]
    public async Task OrdinaryPlan_HidingReturns200_AndDisappearsFromPricing()
    {
        var admin = await LoginAsSuperAdminAsync();
        var plan = await CreatePlanAsync(admin.Token, isPublic: true);
        await EnablePublicPricingAsync();

        var hide = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{plan.Id}", FullPlanBody(plan, isPublic: false));
        hide.StatusCode.Should().Be(HttpStatusCode.OK);

        var pricing = await (await AnonymousClient().GetAsync("/api/pricing")).Content.ReadJsonAsync<PublicPricingDto>();
        pricing!.Plans.Should().NotContain(p => p.Id == plan.Id);
    }

    [Fact, TestCase("CY15-D7-01")]
    public async Task Saving_OnlyPrice_DoesNotResetIsPublicOrSortOrder()
    {
        // US-15-07's own acceptance criterion, at the HTTP boundary: a save that legitimately omits
        // isPublic/sortOrder (the operator only touched price) must not reset either to the DTO's own
        // hardcoded defaults.
        var admin = await LoginAsSuperAdminAsync();
        var plan = await CreatePlanAsync(admin.Token, isPublic: false, sortOrder: 3);

        var response = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{plan.Id}",
            FullPlanBody(plan, pricePerMonth: 1234));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadJsonAsync<AdminPlanDto>())!;
        updated.PricePerMonth.Should().Be(1234);
        updated.IsPublic.Should().BeFalse("isPublic was not sent — must remain untouched, not reset to true");
        updated.SortOrder.Should().Be(3, "sortOrder was not sent — must remain untouched, not reset to 0");
    }

    [Fact, TestCase("CY15-D7-02")]
    public async Task Saving_DeactivatedPlan_DoesNotReactivateItself()
    {
        var admin = await LoginAsSuperAdminAsync();
        var plan = await CreatePlanAsync(admin.Token, isPublic: false);
        (await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{plan.Id}", FullPlanBody(plan, isActive: false)))
            .EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reread = await db.SubscriptionPlanConfigs.AsNoTracking().FirstAsync(p => p.Id == plan.Id);
        reread.IsActive.Should().BeFalse();
        scope.Dispose();

        // Re-editing (e.g. changing the description) and saving again must NOT silently reactivate it —
        // activation is a distinct, explicit action.
        var adminList = await (await AuthedClient(admin.Token).GetAsync("/api/admin/plans")).Content.ReadJsonAsync<AdminPlansListDto>();
        var deactivated = adminList!.Plans.Single(p => p.Id == plan.Id);
        var resave = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{plan.Id}", FullPlanBody(deactivated));
        var resaved = (await resave.Content.ReadJsonAsync<AdminPlanDto>())!;
        resaved.IsActive.Should().BeFalse("saving a deactivated plan must not implicitly reactivate it");
    }

    private async Task EnablePublicPricingAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.PlatformSettings.FindAsync("pricing.public-enabled");
        if (row is null)
        {
            db.PlatformSettings.Add(new PlatformSetting { Key = "pricing.public-enabled", Value = "true", UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            row.Value = "true";
        }
        await db.SaveChangesAsync();
    }
}
