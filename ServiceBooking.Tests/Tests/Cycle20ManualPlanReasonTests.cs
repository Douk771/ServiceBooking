using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 20 (SPEC_CYCLE20_LEGAL_CLOSURE.md US-20-02, C15-8/C17-4; ARCHITECTURE_CYCLE20.md §403) — the
/// closed list of reasons a SuperAdmin may cite when hand-assigning a hidden tariff, exercised end to end
/// through <c>PUT /api/admin/billing-accounts/{id}/subscription</c>. ManualPlanAssignmentPolicy's own
/// pure logic already has unit coverage (ManualPlanAssignmentPolicyTests.cs); this file is the missing
/// functional coverage of the actual endpoint (rejection, persistence, and the journal), written from
/// SPEC.md's acceptance criteria, independently of AdminBillingController's own implementation
/// ("Вызов 2").
/// </summary>
public class Cycle20ManualPlanReasonTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private async Task<Guid> GetAccountIdAsync(string ownerUserId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.BillingAccounts.Where(a => a.OwnerUserId == ownerUserId).Select(a => a.Id).FirstAsync();
    }

    private async Task<Guid?> GetCurrentPlanIdAsync(Guid accountId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AccountSubscriptions.Where(s => s.BillingAccountId == accountId).Select(s => s.PlanConfigId).FirstOrDefaultAsync();
    }

    // ── CY20-RSN-01: assigning a DIFFERENT hidden plan with no reason -> 400, nothing changes ───────

    [Fact, TestCase("CY20-RSN-01")]
    public async Task AssignDifferentHiddenPlan_NoReason_Returns400_PlanUnchanged()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync(); // already on a hidden test plan (IsPublic=false)
        var accountId = await GetAccountIdAsync(owner.UserId);
        var currentPlanId = await GetCurrentPlanIdAsync(accountId);
        var newHiddenPlanId = await CreateTestPlanConfigAsync(); // also IsPublic=false by default, and different

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/billing-accounts/{accountId}/subscription",
            new { planId = newHiddenPlanId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), options = new object[0] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stillCurrent = await GetCurrentPlanIdAsync(accountId);
        stillCurrent.Should().Be(currentPlanId, "a rejected manual assignment must write nothing");
    }

    // ── CY20-RSN-02: TrialReissue reason is refused here — it has its own dedicated endpoint ────────

    [Fact, TestCase("CY20-RSN-02")]
    public async Task AssignDifferentHiddenPlan_TrialReissueReason_IsRejected()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var accountId = await GetAccountIdAsync(owner.UserId);
        var newHiddenPlanId = await CreateTestPlanConfigAsync();

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/billing-accounts/{accountId}/subscription",
            new
            {
                planId = newHiddenPlanId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), options = new object[0],
                reasonCode = "TrialReissue", reasonDetails = "пробуем через общую ручку",
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "P6/§403.2: TrialReissue is only accepted through the dedicated trial-regrant endpoint, never this one");
    }

    // ── CY20-RSN-03: OperatorErrorCorrection without reasonDetails -> 400 ────────────────────────────

    [Fact, TestCase("CY20-RSN-03")]
    public async Task AssignDifferentHiddenPlan_OperatorErrorCorrectionWithoutDetails_Returns400()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var accountId = await GetAccountIdAsync(owner.UserId);
        var newHiddenPlanId = await CreateTestPlanConfigAsync();

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/billing-accounts/{accountId}/subscription",
            new
            {
                planId = newHiddenPlanId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), options = new object[0],
                reasonCode = "OperatorErrorCorrection", reasonDetails = (string?)null,
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Т20-01: reasonDetails describing the mistake is mandatory for OperatorErrorCorrection");
    }

    // ── CY20-RSN-04: valid reason -> succeeds, and the reason is visible in the subscription history ──

    [Fact, TestCase("CY20-RSN-04")]
    public async Task AssignDifferentHiddenPlan_ValidReason_Succeeds_AndAppearsInHistory()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var accountId = await GetAccountIdAsync(owner.UserId);
        var newHiddenPlanId = await CreateTestPlanConfigAsync();

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/billing-accounts/{accountId}/subscription",
            new
            {
                planId = newHiddenPlanId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), options = new object[0],
                reasonCode = "OperatorErrorCorrection", reasonDetails = "Оператор ошибочно назначил не тот тариф ранее",
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await AuthedClient(admin.Token).GetAsync($"/api/admin/billing-accounts/{accountId}/subscription-history");
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await history.Content.ReadFromJsonAsync<JsonElement>();
        var latest = body.GetProperty("items").EnumerateArray().First();
        latest.GetProperty("reasonCode").GetString().Should().Be("OperatorErrorCorrection");
        latest.GetProperty("reasonDetails").GetString().Should().Be("Оператор ошибочно назначил не тот тариф ранее");
        latest.GetProperty("reasonTitle").GetString().Should().NotBeNullOrEmpty();
    }

    // ── CY20-RSN-05: extending the SAME plan (no change of plan) never requires a reason ────────────

    [Fact, TestCase("CY20-RSN-05")]
    public async Task ExtendingTheSamePlan_NeverRequiresAReason()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var accountId = await GetAccountIdAsync(owner.UserId);
        var currentPlanId = await GetCurrentPlanIdAsync(accountId);

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/billing-accounts/{accountId}/subscription",
            new { planId = currentPlanId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)), options = new object[0] });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "П3: extending the SAME plan the account already has is not \"handing out a hidden tariff\" and needs no reason");
    }
}
