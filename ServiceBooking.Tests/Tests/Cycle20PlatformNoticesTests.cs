using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

using ServiceBooking.Core.Enums;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 20 (SPEC_CYCLE20_LEGAL_CLOSURE.md US-20-03, C18-8(б); ARCHITECTURE_CYCLE20.md §404) — the
/// new platform-notice mechanism end to end: SuperAdmin publishes, the owner sees it in their cabinet and
/// acknowledges it, acknowledgement is recorded server-side and is idempotent. Written from SPEC.md's
/// acceptance criteria, independently of AdminNoticesController's/LegalController's own implementation
/// ("Вызов 2") — PlatformNoticeRules/PlatformNoticeTexts' pure logic already has unit coverage
/// (PlatformNoticeRulesTests.cs et al.), so this file exercises the HTTP wiring and cross-cutting
/// behavior those unit tests cannot: audience matching against a real caller, persistence, idempotency.
/// </summary>
public class Cycle20PlatformNoticesTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── CY20-N-01: publish an AllOwners notice, owner sees+acknowledges it, ack is recorded once ──

    [Fact, TestCase("CY20-N-01")]
    public async Task Publish_AllOwners_OwnerSeesInPending_AcknowledgesOnce_CountsUpdate()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync();

        var publish = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/notices", new
        {
            kind = "Other",
            audience = new { type = "AllOwners", planIds = (Guid[]?)null, billingAccountId = (Guid?)null },
            effectiveFrom = (DateOnly?)null,
            title = "Плановые работы",
            body = "В ночь на воскресенье возможны короткие перерывы в работе сервиса.",
        });
        publish.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await publish.Content.ReadFromJsonAsync<JsonElement>();
        var noticeId = created.GetProperty("id").GetGuid();
        created.GetProperty("acknowledgedCount").GetInt32().Should().Be(0);
        created.GetProperty("audienceCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        var pendingBefore = await AuthedClient(owner.Token).GetAsync("/api/legal/notices?scope=pending");
        pendingBefore.StatusCode.Should().Be(HttpStatusCode.OK);
        var pendingBody = await pendingBefore.Content.ReadFromJsonAsync<JsonElement>();
        pendingBody.GetProperty("items").EnumerateArray().Should().Contain(
            i => i.GetProperty("id").GetGuid() == noticeId);

        var ack1 = await AuthedClient(owner.Token).PostAsync($"/api/legal/notices/{noticeId}/acknowledge", null);
        ack1.StatusCode.Should().Be(HttpStatusCode.OK);
        var ack2 = await AuthedClient(owner.Token).PostAsync($"/api/legal/notices/{noticeId}/acknowledge", null);
        ack2.StatusCode.Should().Be(HttpStatusCode.OK, "a repeat acknowledge must not fail");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ackCount = await db.PlatformNoticeAcknowledgements.CountAsync(a => a.NoticeId == noticeId && a.UserId == owner.UserId);
            ackCount.Should().Be(1, "a second click must not create a duplicate acknowledgement row");
        }

        var pendingAfter = await AuthedClient(owner.Token).GetAsync("/api/legal/notices?scope=pending");
        var pendingAfterBody = await pendingAfter.Content.ReadFromJsonAsync<JsonElement>();
        pendingAfterBody.GetProperty("items").EnumerateArray().Should().NotContain(
            i => i.GetProperty("id").GetGuid() == noticeId, "an acknowledged notice must leave the pending scope");

        var allAfter = await AuthedClient(owner.Token).GetAsync("/api/legal/notices?scope=all");
        var allAfterBody = await allAfter.Content.ReadFromJsonAsync<JsonElement>();
        allAfterBody.GetProperty("items").EnumerateArray().Should().Contain(
            i => i.GetProperty("id").GetGuid() == noticeId, "acknowledged notices must remain visible under scope=all");

        var adminList = await AuthedClient(admin.Token).GetAsync("/api/admin/notices");
        var adminBody = await adminList.Content.ReadFromJsonAsync<JsonElement>();
        var adminItem = adminBody.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == noticeId);
        adminItem.GetProperty("acknowledgedCount").GetInt32().Should().Be(1);
    }

    // ── CY20-N-02: PriceChange effectiveFrom too soon -> 400, nothing persisted ──────────────────

    [Fact, TestCase("CY20-N-02")]
    public async Task PublishPriceChange_EffectiveFromTooSoon_Returns400_WritesNothing()
    {
        var admin = await LoginAsSuperAdminAsync();
        var planId = await CreateTestPlanConfigAsync();

        int before;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            before = await db.PlatformNotices.CountAsync();
        }

        var response = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/notices", new
        {
            kind = "PriceChange",
            audience = new { type = "AllOwners", planIds = (Guid[]?)null, billingAccountId = (Guid?)null },
            effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            priceChange = new { planId, oldPricePerMonth = 990m, newPricePerMonth = 1490m },
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "SPEC US-20-03: effectiveFrom for a price change must be at least 30 days out");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PlatformNotices.CountAsync()).Should().Be(before, "a rejected publish must write nothing");
        }
    }

    // ── CY20-N-03: PriceChange with a valid date publishes; preview writes nothing ───────────────

    [Fact, TestCase("CY20-N-03")]
    public async Task PublishPriceChange_ValidDate_Succeeds_PreviewNeverPersists()
    {
        var admin = await LoginAsSuperAdminAsync();
        var planId = await CreateTestPlanConfigAsync();
        var validEffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(31));

        var body = new
        {
            kind = "PriceChange",
            audience = new { type = "AllOwners", planIds = (Guid[]?)null, billingAccountId = (Guid?)null },
            effectiveFrom = validEffectiveFrom,
            priceChange = new { planId, oldPricePerMonth = 990m, newPricePerMonth = 1490m },
        };

        int before;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            before = await db.PlatformNotices.CountAsync();
        }

        var preview = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/notices/preview", body);
        preview.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PlatformNotices.CountAsync()).Should().Be(before, "preview must never write a row");
        }

        var publish = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/notices", body);
        publish.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await publish.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("effectiveFrom").GetDateTime().Should().Be(validEffectiveFrom.ToDateTime(TimeOnly.MinValue));
    }

    // ── CY20-N-04: revoke requires a reason, then hides the notice from the owner's cabinet ──────

    [Fact, TestCase("CY20-N-04")]
    public async Task Revoke_RequiresReason_ThenNoticeDisappearsFromOwnerCabinet()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync();

        var publish = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/notices", new
        {
            kind = "Other",
            audience = new { type = "AllOwners", planIds = (Guid[]?)null, billingAccountId = (Guid?)null },
            effectiveFrom = (DateOnly?)null,
            title = "Тестовое уведомление",
            body = "Текст уведомления для теста отзыва.",
        });
        publish.EnsureSuccessStatusCode();
        var noticeId = (await publish.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var revokeNoReason = await AuthedClient(admin.Token).PostAsJsonAsync($"/api/admin/notices/{noticeId}/revoke", new { reason = "" });
        revokeNoReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var revoke = await AuthedClient(admin.Token).PostAsJsonAsync($"/api/admin/notices/{noticeId}/revoke", new { reason = "Опубликовано по ошибке" });
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        var revokeAgain = await AuthedClient(admin.Token).PostAsJsonAsync($"/api/admin/notices/{noticeId}/revoke", new { reason = "Повторный отзыв" });
        revokeAgain.StatusCode.Should().Be(HttpStatusCode.Conflict, "an already-revoked notice cannot be revoked twice");

        var pending = await AuthedClient(owner.Token).GetAsync("/api/legal/notices?scope=pending");
        var pendingBody = await pending.Content.ReadFromJsonAsync<JsonElement>();
        pendingBody.GetProperty("items").EnumerateArray().Should().NotContain(
            i => i.GetProperty("id").GetGuid() == noticeId, "a revoked notice must not appear as pending to the owner");
    }

    // ── CY20-N-05: a BillingAccount-scoped notice is invisible/unacknowledgeable to a different owner ─

    [Fact, TestCase("CY20-N-05")]
    public async Task BillingAccountScopedNotice_IsNotVisibleToADifferentOwner()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (targetOwner, _) = await CreateOwnerWithCompanyAsync();
        var (otherOwner, _) = await CreateOwnerWithCompanyAsync();

        Guid targetAccountId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            targetAccountId = await db.BillingAccounts.Where(a => a.OwnerUserId == targetOwner.UserId).Select(a => a.Id).FirstAsync();
        }

        var publish = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/notices", new
        {
            kind = "Other",
            audience = new { type = "BillingAccount", planIds = (Guid[]?)null, billingAccountId = targetAccountId },
            effectiveFrom = (DateOnly?)null,
            title = "Лично для вас",
            body = "Текст, адресованный только одному аккаунту.",
        });
        publish.EnsureSuccessStatusCode();
        var noticeId = (await publish.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var targetPending = await AuthedClient(targetOwner.Token).GetAsync("/api/legal/notices?scope=pending");
        var targetBody = await targetPending.Content.ReadFromJsonAsync<JsonElement>();
        targetBody.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("id").GetGuid() == noticeId);

        var otherPending = await AuthedClient(otherOwner.Token).GetAsync("/api/legal/notices?scope=pending");
        var otherBody = await otherPending.Content.ReadFromJsonAsync<JsonElement>();
        otherBody.GetProperty("items").EnumerateArray().Should().NotContain(i => i.GetProperty("id").GetGuid() == noticeId);

        var otherAck = await AuthedClient(otherOwner.Token).PostAsync($"/api/legal/notices/{noticeId}/acknowledge", null);
        otherAck.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a non-addressee must get the same 404 as a nonexistent notice — never reveal existence");
    }

    // ── CY20-N-06: a non-SuperAdmin cannot publish or revoke ─────────────────────────────────────

    [Fact, TestCase("CY20-N-06")]
    public async Task NonSuperAdmin_CannotPublishOrRevoke()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();

        var publish = await AuthedClient(owner.Token).PostAsJsonAsync("/api/admin/notices", new
        {
            kind = "Other",
            audience = new { type = "AllOwners", planIds = (Guid[]?)null, billingAccountId = (Guid?)null },
            effectiveFrom = (DateOnly?)null,
            title = "x",
            body = "y",
        });
        publish.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── CY20-N-07: PhotoRemoved cannot be published manually through the admin endpoint ──────────

    [Fact, TestCase("CY20-N-07")]
    public async Task PhotoRemoved_CannotBePublishedManually()
    {
        var admin = await LoginAsSuperAdminAsync();

        var publish = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/notices", new
        {
            kind = "PhotoRemoved",
            audience = new { type = "AllOwners", planIds = (Guid[]?)null, billingAccountId = (Guid?)null },
            effectiveFrom = (DateOnly?)null,
            title = "x",
            body = "y",
        });
        publish.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "§434.5 row 6: PhotoRemoved is only ever created by the system (photo-removal flow), never manually");
    }

    // ── CY20-N-08: review finding 2 (blocking) — a Free-plan owner (no AccountSubscription row at
    // all) addressed by an OwnersOnPlans notice that lists the system-Free plan must be able to open
    // the notice's attachment, not just see/acknowledge it. Written independently from SPEC/the review
    // finding's own wording, against the real seeded system-Free plan (SeedBillingCatalog always seeds
    // exactly one — ADM-051's own note), not a hand-rolled one, so this exercises the exact lookup path
    // (LoadCallerFactsAsync's needsFreePlan branch) the finding was about.

    [Fact, TestCase("CY20-N-08")]
    public async Task OwnersOnPlansNotice_ListingTheFreePlan_FreePlanOwnerCanAlsoOpenTheAttachment()
    {
        var admin = await LoginAsSuperAdminAsync();
        // attachPlan: false -> genuinely no AccountSubscription row, i.e. the implicit system-Free plan.
        var (freeOwner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        Guid systemFreePlanId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            systemFreePlanId = await db.SubscriptionPlanConfigs.Where(p => p.IsSystemFree && p.Line == CompanyKind.Services).Select(p => p.Id).FirstAsync();
        }

        var publish = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/notices", new
        {
            kind = "Other",
            audience = new { type = "OwnersOnPlans", planIds = new[] { systemFreePlanId }, billingAccountId = (Guid?)null },
            effectiveFrom = (DateOnly?)null,
            title = "Новая редакция документа",
            body = "Текст уведомления с вложением новой редакции.",
            attachment = new { title = "Соглашение с компанией, редакция от 01.11.2026", html = "<p>Полный текст новой редакции.</p>" },
        });
        publish.EnsureSuccessStatusCode();
        var created = await publish.Content.ReadFromJsonAsync<JsonElement>();
        var noticeId = created.GetProperty("id").GetGuid();
        created.GetProperty("attachment").GetProperty("title").GetString().Should().NotBeNullOrEmpty();

        // The free owner must see it addressed to them...
        var pending = await AuthedClient(freeOwner.Token).GetAsync("/api/legal/notices?scope=pending");
        var pendingBody = await pending.Content.ReadFromJsonAsync<JsonElement>();
        pendingBody.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("id").GetGuid() == noticeId);

        // ...and, per review finding 2, must also be able to open the attachment itself, not 404.
        var attachment = await AuthedClient(freeOwner.Token).GetAsync($"/api/legal/notices/{noticeId}/attachment");
        attachment.StatusCode.Should().Be(HttpStatusCode.OK,
            "review finding 2 (blocking): a system-Free owner addressed by an OwnersOnPlans notice must be able to read the attachment, same as the notice itself");
        var html = await attachment.Content.ReadAsStringAsync();
        html.Should().Contain("Полный текст новой редакции.");
    }
}
