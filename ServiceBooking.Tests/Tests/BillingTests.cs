using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 5 (SPEC.md US-65, US-70; ARCHITECTURE_CYCLE5.md §49, §54.5) — the owner's own subscription
/// screen and their plan/option request queue. Written from SPEC.md, independently of
/// BillingController's/AdminBillingController's own implementation, per this cycle's QA brief
/// ("Вызов 2"). Developers proposed these scenarios but did not write them (backend report) — this is
/// that missing coverage.
/// </summary>
public class BillingTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("BLL-001")]
    public async Task GetSubscription_TotalEqualsPlanPricePlusSumOfOptions()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync(); // paid "QA Full Access" plan
        Guid billingAccountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            billingAccountId = await db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync();
            var sub = await db.AccountSubscriptions.FirstAsync(s => s.BillingAccountId == billingAccountId);
            sub.PlanConfig = null; // avoid stale tracked nav confusing the next query
            var plan = await db.SubscriptionPlanConfigs.FindAsync(sub.PlanConfigId);
            plan!.PricePerMonth = 990m;

            var optionA = new SubscriptionOption
            {
                Id = Guid.NewGuid(), Code = Unique("opt-a-"), Name = "Доп. сотрудники",
                Kind = OptionKind.Quantity, PricePerMonth = 150m, UnitName = "сотрудник", IsActive = true,
            };
            var optionB = new SubscriptionOption
            {
                Id = Guid.NewGuid(), Code = Unique("opt-b-"), Name = "Доп. компания",
                Kind = OptionKind.Quantity, PricePerMonth = 400m, UnitName = "компания", IsActive = true,
            };
            db.SubscriptionOptions.AddRange(optionA, optionB);
            // §43.3/N14 fail-closed: a (plan, option) pair with no PlanOptionRule row resolves to
            // Unavailable and contributes 0 regardless of any AccountSubscriptionOption row/quantity —
            // both options need an explicit Extra rule on this plan to be purchasable/billable at all.
            db.PlanOptionRules.AddRange(
                new PlanOptionRule { Id = Guid.NewGuid(), PlanConfigId = sub.PlanConfigId!.Value, OptionId = optionA.Id, Availability = OptionAvailability.Extra },
                new PlanOptionRule { Id = Guid.NewGuid(), PlanConfigId = sub.PlanConfigId!.Value, OptionId = optionB.Id, Availability = OptionAvailability.Extra });
            db.AccountSubscriptionOptions.AddRange(
                new AccountSubscriptionOption { Id = Guid.NewGuid(), BillingAccountId = billingAccountId, OptionId = optionA.Id, Quantity = 3, ActivatedAtUtc = DateTime.UtcNow },
                new AccountSubscriptionOption { Id = Guid.NewGuid(), BillingAccountId = billingAccountId, OptionId = optionB.Id, Quantity = 1, ActivatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var dto = await (await AuthedClient(owner.Token).GetAsync("/api/billing/subscription"))
            .Content.ReadFromJsonAsync<OwnerSubscriptionDto>();

        dto.Should().NotBeNull();
        dto!.Plan.PricePerMonth.Should().Be(990m);
        dto.Options.Should().HaveCount(2);
        var expected = 990m + 3 * 150m + 1 * 400m;
        dto.TotalMonthlyPrice.Should().Be(expected, "SPEC §3.1/US-65: total = plan price + sum(option price × quantity)");
    }

    [Fact, TestCase("BLL-002")]
    public async Task AssignSubscription_WithRequestId_ClosesTheRequestAndAppearsInHistory()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var planId = await CreateTestPlanConfigAsync(allowOnlineBooking: true, maxCompanies: 10, maxEmployees: 10);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var plan = await db.SubscriptionPlanConfigs.FindAsync(planId);
            plan!.IsActive = true;
            await db.SaveChangesAsync();
        }

        var submit = await AuthedClient(owner.Token).PostAsJsonAsync("/api/billing/subscription/request",
            new SubscriptionRequestInputDto(planId, [], "Хочу перейти на новый тариф"));
        submit.EnsureSuccessStatusCode();

        Guid accountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            accountId = await db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync();
        }

        var before = await (await AuthedClient(owner.Token).GetAsync("/api/billing/subscription"))
            .Content.ReadFromJsonAsync<OwnerSubscriptionDto>();
        before!.PendingRequest.Should().NotBeNull();

        // The admin's queue must show the request's own composition BEFORE it's actioned — this is
        // what a real assignment form would pre-fill from (US-67 п.1's "approve → form pre-filled with
        // the request's composition" acceptance criterion).
        var queueBeforeJson = await (await AuthedClient(admin.Token).GetAsync("/api/admin/subscription-requests?pageSize=100"))
            .Content.ReadAsStringAsync();
        using (var queueBeforeDoc = JsonDocument.Parse(queueBeforeJson))
        {
            var queuedItem = queueBeforeDoc.RootElement.GetProperty("items").EnumerateArray()
                .Should().ContainSingle(item => item.GetProperty("billingAccountId").GetGuid() == accountId).Subject;
            queuedItem.GetProperty("desiredPlanName").GetString().Should().NotBeNullOrEmpty(
                "the queue item must name the desired plan so the assignment form can pre-fill it");
            queuedItem.GetProperty("comment").GetString().Should().Be("Хочу перейти на новый тариф");
        }

        var assign = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/billing-accounts/{accountId}/subscription",
            new { planId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)), options = new object[0], requestId = accountId });
        assign.EnsureSuccessStatusCode();

        var after = await (await AuthedClient(owner.Token).GetAsync("/api/billing/subscription"))
            .Content.ReadFromJsonAsync<OwnerSubscriptionDto>();
        after!.PendingRequest.Should().BeNull("assigning a subscription with the matching requestId must close the owner's pending request");

        var queue = await (await AuthedClient(admin.Token).GetAsync("/api/admin/subscription-requests"))
            .Content.ReadAsStringAsync();
        queue.Should().NotContain(accountId.ToString(), "a closed request must leave the admin queue");

        // ...and become visible in the account's subscription HISTORY, not just vanish from the queue.
        var historyJson = await (await AuthedClient(admin.Token).GetAsync($"/api/admin/billing-accounts/{accountId}/subscription-history"))
            .Content.ReadAsStringAsync();
        using var historyDoc = JsonDocument.Parse(historyJson);
        historyDoc.RootElement.GetProperty("items").EnumerateArray().Should().Contain(
            item => item.GetProperty("newPlanName").GetString() != null,
            "the approved assignment must leave a row in the account's subscription history");
    }

    [Fact, TestCase("BLL-002B")]
    public async Task RejectSubscriptionRequest_ReasonReachesOwner_AndClearsFromQueue()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var planId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.SubscriptionPlanConfigs.FindAsync(planId))!.IsActive = true;
            await db.SaveChangesAsync();
        }

        var submit = await AuthedClient(owner.Token).PostAsJsonAsync("/api/billing/subscription/request",
            new SubscriptionRequestInputDto(planId, [], "Хочу перейти на новый тариф"));
        submit.EnsureSuccessStatusCode();

        Guid accountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            accountId = await db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync();
        }

        var reject = await AuthedClient(admin.Token).PostAsJsonAsync(
            $"/api/admin/subscription-requests/{accountId}/reject",
            new { comment = "Тариф снят с продажи, выберите другой" });
        reject.EnsureSuccessStatusCode();

        // Gone from the admin queue...
        var queueJson = await (await AuthedClient(admin.Token).GetAsync("/api/admin/subscription-requests?pageSize=100"))
            .Content.ReadAsStringAsync();
        using (var queueDoc = JsonDocument.Parse(queueJson))
        {
            queueDoc.RootElement.GetProperty("items").EnumerateArray()
                .Should().NotContain(item => item.GetProperty("billingAccountId").GetGuid() == accountId,
                    "a rejected request must leave the admin queue");
        }

        // ...and the REASON is visible to the owner in their own cabinet (US-70, N10).
        var afterReject = await (await AuthedClient(owner.Token).GetAsync("/api/billing/subscription"))
            .Content.ReadFromJsonAsync<OwnerSubscriptionDto>();
        afterReject!.PendingRequest.Should().BeNull("a rejected request is no longer pending");
        afterReject.LastRejectedRequest.Should().NotBeNull("the owner must be told their request was rejected, not just see it silently vanish");
        afterReject.LastRejectedRequest!.Reason.Should().Be("Тариф снят с продажи, выберите другой");

        // A fresh request after rejection must go through cleanly (not blocked by leftover state).
        var resubmit = await AuthedClient(owner.Token).PostAsJsonAsync("/api/billing/subscription/request",
            new SubscriptionRequestInputDto(planId, [], "Пробую снова"));
        resubmit.EnsureSuccessStatusCode();
        var afterResubmit = await (await AuthedClient(owner.Token).GetAsync("/api/billing/subscription"))
            .Content.ReadFromJsonAsync<OwnerSubscriptionDto>();
        afterResubmit!.PendingRequest.Should().NotBeNull("resubmission after a rejection must succeed and create a fresh pending request");
    }

    [Fact, TestCase("BLL-003")]
    public async Task SubmitRequest_Twice_OverwritesRatherThanDuplicating()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var planA = await CreateTestPlanConfigAsync(allowOnlineBooking: true);
        var planB = await CreateTestPlanConfigAsync(allowOnlineBooking: true, allowAnalytics: true);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var id in new[] { planA, planB })
                (await db.SubscriptionPlanConfigs.FindAsync(id))!.IsActive = true;
            await db.SaveChangesAsync();
        }

        var first = await AuthedClient(owner.Token).PostAsJsonAsync("/api/billing/subscription/request",
            new SubscriptionRequestInputDto(planA, [], "Первая заявка"));
        first.EnsureSuccessStatusCode();

        var second = await AuthedClient(owner.Token).PostAsJsonAsync("/api/billing/subscription/request",
            new SubscriptionRequestInputDto(planB, [], "Вторая заявка — заменяет первую"));
        second.EnsureSuccessStatusCode();

        Guid accountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            accountId = await db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync();
        }

        var dto = await (await AuthedClient(owner.Token).GetAsync("/api/billing/subscription"))
            .Content.ReadFromJsonAsync<OwnerSubscriptionDto>();
        dto!.PendingRequest.Should().NotBeNull();
        dto.PendingRequest!.Comment.Should().Be("Вторая заявка — заменяет первую");
        dto.PendingRequest.DesiredPlanId.Should().Be(planB);

        // Exactly one pending request for this account in the admin queue — not two.
        var queueJson = await (await AuthedClient(admin.Token).GetAsync("/api/admin/subscription-requests?pageSize=100"))
            .Content.ReadAsStringAsync();
        using var queueDoc = JsonDocument.Parse(queueJson);
        var matches = queueDoc.RootElement.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("billingAccountId").GetGuid() == accountId);
        matches.Should().Be(1, "a resubmission must overwrite the pending request, not add a second one to the queue");
    }

    // ── US-74: this cycle must not release WhatsApp notifications ──────────────────────────────────

    [Fact, TestCase("BLL-004")]
    public async Task WhatsAppRemainsUnreleased_ProviderIsLogging_NoWhatsAppOptionOfferedByDefault()
    {
        // §54.3/US-74: the transport must stay the no-op "logging" adapter — no test in this suite (or
        // in production, until this changes deliberately) may cause a real GREEN-API network call.
        using (var scope = Factory.Services.CreateScope())
        {
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            config["Notifications:Provider"].Should().Be("logging",
                "US-74: an unrecognized/real provider value must never ship silently in this environment");
        }

        // A brand-new Free-plan owner, with no catalog rows seeded by this test, must not be offered
        // WhatsApp: "opция никому не предлагается" is the expected, deliberate state (SPEC US-74),
        // not something a stray seed/migration should quietly turn on.
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var dto = await (await AuthedClient(owner.Token).GetAsync("/api/billing/subscription"))
            .Content.ReadFromJsonAsync<OwnerSubscriptionDto>();

        dto.Should().NotBeNull();
        dto!.AvailableOptions.Should().NotContain(o => o.OptionId != Guid.Empty && o.Name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) && o.PricePerMonth != null,
            "US-74: WhatsApp must not appear as a purchasable, priced option by default");
    }
}
