using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 20 (SPEC_CYCLE20_LEGAL_CLOSURE.md US-20-07, LG6; ARCHITECTURE_CYCLE20.md §407) — the core
/// acceptance criterion CompanyTransferTests.cs's own cycle-20 diff never added a positive test for: a
/// company must not land in a subscriber's account together with an owner who has no relation to that
/// account, unless the transfer explicitly names a new owner from it. Written from SPEC.md's acceptance
/// criteria, independently of CompanyTransferController's/CompanyTransferService's own implementation
/// ("Вызов 2").
/// </summary>
public class Cycle20CompanyTransferLg6Tests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── CY20-LG6-01: transfer WITHOUT a new owner, to an account unrelated to the current owner -> rejected ──

    [Fact, TestCase("CY20-LG6-01")]
    public async Task TransferWithoutOwnerChange_ToUnrelatedAccount_IsRejected()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (sourceOwner, company) = await CreateOwnerWithCompanyAsync();
        var (targetOwner, _) = await CreateOwnerWithCompanyAsync();

        Guid targetAccountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            targetAccountId = await db.BillingAccounts.Where(a => a.OwnerUserId == targetOwner.UserId).Select(a => a.Id).FirstAsync();
        }

        var response = await AuthedClient(admin.Token).PostAsJsonAsync(
            $"/api/admin/companies/{company.Id}/transfer",
            new CompanyTransferInput(targetAccountId, null, ConfirmRightsTransfer: true));

        ((int)response.StatusCode).Should().Be(409,
            "LG6: the company's current owner (sourceOwner) has no relation to targetOwner's account — a transfer with no new owner must be refused");

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db2.Companies.FindAsync(company.Id);
        reloaded!.BillingAccountId.Should().NotBe(targetAccountId, "a rejected transfer must not move the company");
        reloaded.OwnerUserId.Should().Be(sourceOwner.UserId);
    }

    // ── CY20-LG6-02: the preview endpoint reports the same block, with ownerChangeRequired: true ──────

    [Fact, TestCase("CY20-LG6-02")]
    public async Task Preview_WithoutOwnerChange_ToUnrelatedAccount_ReportsOwnerChangeRequired()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var (targetOwner, _) = await CreateOwnerWithCompanyAsync();

        Guid targetAccountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            targetAccountId = await db.BillingAccounts.Where(a => a.OwnerUserId == targetOwner.UserId).Select(a => a.Id).FirstAsync();
        }

        var preview = await AuthedClient(admin.Token).GetAsync(
            $"/api/admin/companies/{company.Id}/transfer/preview?targetBillingAccountId={targetAccountId}");
        preview.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("canTransfer").GetBoolean().Should().BeFalse();
        body.GetProperty("ownerChangeRequired").GetBoolean().Should().BeTrue();
        body.GetProperty("blockReason").GetString().Should().NotBeNullOrEmpty();
    }

    // ── CY20-LG6-03: same transfer WITH a new owner from the target account -> succeeds (unblocked) ────

    [Fact, TestCase("CY20-LG6-03")]
    public async Task TransferWithNewOwnerFromTargetAccount_Succeeds_EvenWhenCurrentOwnerIsUnrelated()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var (targetOwner, _) = await CreateOwnerWithCompanyAsync();

        Guid targetAccountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            targetAccountId = await db.BillingAccounts.Where(a => a.OwnerUserId == targetOwner.UserId).Select(a => a.Id).FirstAsync();
        }

        var response = await AuthedClient(admin.Token).PostAsJsonAsync(
            $"/api/admin/companies/{company.Id}/transfer",
            new CompanyTransferInput(targetAccountId, targetOwner.UserId, ConfirmRightsTransfer: true));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "naming a new owner who IS linked to the target account (its holder) satisfies LG6 directly");

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db2.Companies.FindAsync(company.Id);
        reloaded!.BillingAccountId.Should().Be(targetAccountId);
        reloaded.OwnerUserId.Should().Be(targetOwner.UserId);
    }

    // ── CY20-LG6-04: confirmRightsTransfer missing/false -> 400, checked before anything else ─────────

    [Fact, TestCase("CY20-LG6-04")]
    public async Task Transfer_WithoutConfirmRightsTransfer_Returns400_EvenWithAValidNewOwner()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var (targetOwner, _) = await CreateOwnerWithCompanyAsync();

        Guid targetAccountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            targetAccountId = await db.BillingAccounts.Where(a => a.OwnerUserId == targetOwner.UserId).Select(a => a.Id).FirstAsync();
        }

        var response = await AuthedClient(admin.Token).PostAsJsonAsync(
            $"/api/admin/companies/{company.Id}/transfer",
            new CompanyTransferInput(targetAccountId, targetOwner.UserId, ConfirmRightsTransfer: false));
        ((int)response.StatusCode).Should().Be(400,
            "Т20-09: confirmRightsTransfer is checked FIRST, before any owner-linkage/limit computation");
    }
}
