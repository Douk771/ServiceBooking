using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 7 (final pass before merge into `develop`) — <c>GET /api/admin/billing-accounts</c> had NO
/// functional coverage at all before this file, which is exactly how a real crash on migrated data
/// (an account with a paid option but no <see cref="AccountSubscription"/> row — the seed's own
/// "quantity option without a subscription" shape, see <see cref="AdminBillingController"/>'s BLK-2
/// fix) went unnoticed by 498 otherwise-green tests. Written from SPEC.md/ARCHITECTURE_CYCLE7.md §43.4
/// (five subscription-status states) and the QA brief's own root-cause description, independently of
/// the controller's implementation.
/// </summary>
public class AdminBillingAccountsTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── BLK-2: subless account must not crash a page that ALSO contains a subscribed account ────────

    [Fact, TestCase("ABA-001")]
    public async Task GetBillingAccounts_PageWithSubscribedAndSublessPaidOptionAccounts_DoesNotCrash()
    {
        var admin = await LoginAsSuperAdminAsync();

        // Account A: a normal paid plan (so `sub` is non-null for this row in the LEFT JOIN).
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync(); // "QA Full Access", active subscription

        // Account B: the migrated shape that crashed — a billing account with a subscribed, priced
        // option (AccountSubscriptionOption) but NO AccountSubscription row at all. This is exactly
        // what a pre-cycle-5 "paid number, no subscription" migration leaves behind (backend report).
        var (ownerB, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        Guid accountBId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            accountBId = await db.BillingAccounts.Where(a => a.OwnerUserId == ownerB.UserId).Select(a => a.Id).FirstAsync();
            (await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == accountBId))
                .Should().BeNull("this test's whole point is an account with NO subscription row");

            var option = new SubscriptionOption
            {
                Id = Guid.NewGuid(), Code = Unique("opt-subless-"), Name = "Доп. номер",
                Kind = OptionKind.Quantity, PricePerMonth = 300m, UnitName = "номер", IsActive = true,
            };
            db.SubscriptionOptions.Add(option);
            db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
            {
                Id = Guid.NewGuid(), BillingAccountId = accountBId, OptionId = option.Id,
                Quantity = 1, ActivatedAtUtc = DateTime.UtcNow,
            });

            // A PlanOptionRule for THIS SAME option, on ANY plan (here: account A's, already active on
            // the page) — this is what actually forces the crashing code path to execute. The faulty
            // line was `planRules.FirstOrDefault(r => r.OptionId == o.OptionId && r.PlanConfigId ==
            // sub!.PlanConfigId)`: `&&` short-circuits, so the null-dereferencing second operand is only
            // evaluated once at least one rule matches by OptionId first. Without a rule for this
            // option, `FirstOrDefault` would never touch `sub!` at all and the bug would go unnoticed
            // even by a test that otherwise looks like a faithful repro (verified by literally reverting
            // the fix locally and watching this exact setup fail to reproduce without this rule).
            var accountAPlanId = await db.AccountSubscriptions
                .Where(s => s.BillingAccountId != null)
                .Where(s => db.Companies.Any(c => c.Id == companyA.Id && c.BillingAccountId == s.BillingAccountId))
                .Select(s => s.PlanConfigId!.Value).FirstAsync();
            db.PlanOptionRules.Add(new PlanOptionRule
            {
                Id = Guid.NewGuid(), PlanConfigId = accountAPlanId, OptionId = option.Id, Availability = OptionAvailability.Extra,
            });
            await db.SaveChangesAsync();
        }

        // The crash (BLK-2) only reproduced when the SAME page contained at least one account WITH a
        // subscription — a page of only subless accounts never hit the faulty branch. Both accounts
        // are freshly created in this test, so both land on page 1 sorted by CreatedAtUtc (default).
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/billing-accounts?pageSize=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "a subless account sharing a page with a subscribed one must not 500");

        var page = await response.Content.ReadFromJsonAsync<ContractPagedResult<AdminBillingAccountListItemDto>>();
        page.Should().NotBeNull();
        var rowA = page!.Items.Should().ContainSingle(i => i.OwnerUserId == ownerA.UserId).Subject;
        var rowB = page.Items.Should().ContainSingle(i => i.OwnerUserId == ownerB.UserId).Subject;

        rowA.Status.Should().Be("Active");
        rowB.Status.Should().Be("Free", "no AccountSubscription row at all resolves to Free, same as SubscriptionStatusFor(null, now)");
        // The subless account's option has no PlanOptionRule (there's no plan to have one), so it's
        // fail-closed Unavailable and contributes 0 — the fixed code path, not a crash.
        rowB.TotalMonthlyPrice.Should().Be(0m);
    }

    // ── Status parity: the list's inline SQL CASE must agree with the card's SubscriptionStatusFor ──

    [Fact, TestCase("ABA-002")]
    public async Task GetBillingAccounts_ListStatus_MatchesCardStatus_ForAllFiveSubscriptionShapes()
    {
        // OwnerSubscriptionService.SubscriptionStatusFor is duplicated as an inline SQL CASE in
        // AdminBillingController.GetBillingAccounts (EF Core cannot translate a call to the shared
        // method) — nothing exercises the SQL branch except this test. Any accidental drift between the
        // two must fail here, not silently in production.
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var now = DateTime.UtcNow;

        var planId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.SubscriptionPlanConfigs.FindAsync(planId))!.IsActive = true;
            await db.SaveChangesAsync();
        }

        // 1) "без плана вовсе" — a billing account that never got an AccountSubscription row.
        var (ownerNoSub, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        // 2) "бесплатный" — an AccountSubscription row exists, but PlanConfigId is null.
        var (ownerFreeRow, freeCompany) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await CreateExplicitSubscriptionAsync(freeCompany.Id, planConfigId: null, paidUntil: null, isActive: true);

        // 3) "активный" — a real plan, paid in the future, IsActive true.
        var (ownerActive, activeCompany) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await CreateExplicitSubscriptionAsync(activeCompany.Id, planConfigId: planId, paidUntil: now.AddMonths(1), isActive: true);

        // 4) "истекший по дате" — PaidUntil in the past, IsActive still true.
        var (ownerExpiredByDate, expiredDateCompany) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await CreateExplicitSubscriptionAsync(expiredDateCompany.Id, planConfigId: planId, paidUntil: now.AddDays(-1), isActive: true);

        // 5) "истекший из-за снятой активности" — IsActive false, regardless of PaidUntil being in the future.
        var (ownerExpiredByFlag, expiredFlagCompany) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await CreateExplicitSubscriptionAsync(expiredFlagCompany.Id, planConfigId: planId, paidUntil: now.AddMonths(1), isActive: false);

        var owners = new[] { ownerNoSub, ownerFreeRow, ownerActive, ownerExpiredByDate, ownerExpiredByFlag };
        var expected = new[] { "Free", "Free", "Active", "Expired", "Expired" };

        var listJson = await (await adminClient.GetAsync("/api/admin/billing-accounts?pageSize=100"))
            .Content.ReadFromJsonAsync<ContractPagedResult<AdminBillingAccountListItemDto>>();

        for (var i = 0; i < owners.Length; i++)
        {
            var listRow = listJson!.Items.Should().ContainSingle(it => it.OwnerUserId == owners[i].UserId).Subject;
            listRow.Status.Should().Be(expected[i], $"list status mismatch for scenario #{i + 1}");

            var card = await (await adminClient.GetAsync($"/api/admin/billing-accounts/{listRow.Id}"))
                .Content.ReadFromJsonAsync<AdminBillingAccountDto>();
            card!.Status.Should().Be(expected[i], $"card status mismatch for scenario #{i + 1}");

            card.Status.Should().Be(listRow.Status,
                $"list (inline SQL CASE) and card (OwnerSubscriptionService.SubscriptionStatusFor) must never disagree — scenario #{i + 1}");
        }
    }

    // ── Search must never 500 on hostile input (schemathesis finding #1, cycle-07 QA pass) ──────────

    [Fact, TestCase("ABA-003")]
    public async Task GetBillingAccounts_SearchWithControlCharacters_DoesNotCrash()
    {
        var admin = await LoginAsSuperAdminAsync();
        await CreateOwnerWithCompanyAsync(); // at least one row to page through

        // NUL byte + other C0 control characters — Postgres' `text` rejects an embedded NUL outright,
        // which used to surface as an unhandled 500 instead of a normal (possibly empty) result.
        var hostileSearch = Uri.EscapeDataString("abc def");
        var response = await AuthedClient(admin.Token).GetAsync($"/api/admin/billing-accounts?search={hostileSearch}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "control characters in ?search= must be sanitized, not crash the endpoint");
    }

    // ── Helper: writes an AccountSubscription row with an EXACT shape (including PlanConfigId == null,
    // which GiveAccountPlanAsync/SetSubscriptionAsync never produce) — needed for the "бесплатный"
    // (has a row, no plan) vs "без плана вовсе" (no row) distinction. ─────────────────────────────────
    private async Task CreateExplicitSubscriptionAsync(Guid companyId, Guid? planConfigId, DateTime? paidUntil, bool isActive)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await db.Companies.FirstAsync(c => c.Id == companyId);
        var billingAccountId = company.BillingAccountId
            ?? throw new InvalidOperationException("Company must already have a billing account (CreateOwnerWithCompanyAsync provisions one).");

        db.AccountSubscriptions.Add(new AccountSubscription
        {
            Id = Guid.NewGuid(), OwnerUserId = company.OwnerUserId, BillingAccountId = billingAccountId,
            PlanConfigId = planConfigId, PaidUntil = paidUntil, IsActive = isActive,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
