using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 18 ("Вызов 2") — written from SPEC.md (US-18-01…US-18-14, decisions Д1-Д21) independently
/// of the backend/frontend implementation, per this cycle's QA brief. Covers block A (catalog
/// protections), B (admin settings validation), C (one-time-ness / antifraud), D (activation), and the
/// public/admin pricing surface (US-18-12) plus the admin billing-account trial view (US-18-14).
///
/// Deliberately NOT covered here (see the QA report instead of a flaky/impossible test): US-18-08's
/// mailing-window start hook and US-18-11/US-18-13's background expiration task. Both are missing from
/// the codebase entirely (no production code writes <c>TrialChannelFirstAuthorizedAtUtc</c>, and no
/// scheduled task materializes trial expiry) — confirmed by exhaustive grep, not by a guess. There is no
/// code path a functional test could invoke to exercise either.
/// </summary>
public class Cycle18TrialPlanTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Idempotent across the whole test class — all [Fact]s in this class share ONE database
    /// (ARCHITECTURE_CYCLE8_PHASE2.md §91/§92.1, IClassFixture), and the partial unique index on
    /// IsSystemTrial means at most one row in that shared database may ever carry the flag. Reuses
    /// the existing trial plan (restoring it to isActive/isPublic/price==0) instead of trying — and
    /// failing with a 409 — to create a second one from an unrelated test.
    /// </summary>
    private async Task<Guid> CreateTrialPlanAsync(int? durationDays = 14, int? mailingWindowDays = 7)
    {
        var admin = await LoginAsSuperAdminAsync();
        var client = AuthedClient(admin.Token);

        var existingId = await DbAsync(db => db.SubscriptionPlanConfigs
            .Where(p => p.IsSystemTrial).Select(p => (Guid?)p.Id).FirstOrDefaultAsync());

        Guid planId;
        if (existingId is { } id)
        {
            planId = id;
            var restore = await client.PutAsJsonAsync($"/api/admin/plans/{planId}", new
            {
                name = "Trial Plan (QA)", description = "Пробный период",
                highlights = new[] { "Максимум возможностей на 14 дней" },
                pricePerMonth = 0m, maxEmployees = 25, maxCompanies = 5,
                allowOnlineBooking = true, allowMailing = true, allowAnalytics = true,
                allowPublicListing = true, allowOnlinePayment = false,
                photoQuotaMb = 1000, isPublic = true, isActive = true, sortOrder = 1,
            });
            restore.StatusCode.Should().Be(HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
        }
        else
        {
            var create = await client.PostAsJsonAsync("/api/admin/plans", new
            {
                name = Unique("Trial Plan "), description = "Пробный период",
                highlights = new[] { "Максимум возможностей на 14 дней" },
                pricePerMonth = 0m, maxEmployees = 25, maxCompanies = 5,
                allowOnlineBooking = true, allowMailing = true, allowAnalytics = true,
                allowPublicListing = true, allowOnlinePayment = false,
                photoQuotaMb = 1000, isPublic = true, isActive = true, sortOrder = 1,
            });
            create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
            var plan = await create.Content.ReadJsonAsync<AdminPlanDto>();
            planId = plan!.Id;

            var flag = await client.PutAsJsonAsync($"/api/admin/plans/{planId}/system-trial", new { isSystemTrial = true });
            flag.StatusCode.Should().Be(HttpStatusCode.OK, await flag.Content.ReadAsStringAsync());
        }

        await SetTrialSettingsAsync(client, durationDays, mailingWindowDays);
        return planId;
    }

    private static async Task SetTrialSettingsAsync(HttpClient adminClient, int? durationDays, int? mailingWindowDays)
    {
        var response = await adminClient.PutAsJsonAsync("/api/admin/platform-settings", new
        {
            trialDurationDays = durationDays,
            trialMailingWindowDays = mailingWindowDays,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<(AuthResponseLike Owner, Guid AccountId)> CreateOwnerWithVerifiedPhoneAsync()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await MarkPhoneVerifiedAsync(owner.Phone, owner.UserId);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accountId = await db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync();
        return (new AuthResponseLike(owner.Token, owner.UserId, owner.Phone), accountId);
    }

    // Thin adapter so this file doesn't need to know AuthResponseDto's exact shape beyond these 3 fields.
    private readonly record struct AuthResponseLike(string Token, string UserId, string Phone);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(db);
    }

    // ── Block A — catalog protections (US-18-01, US-18-02) ──────────────────────────────────────

    [Fact, TestCase("CY18-A01")]
    public async Task SetSystemTrial_OnPlanWithPositivePrice_Returns400()
    {
        var admin = await LoginAsSuperAdminAsync();
        var client = AuthedClient(admin.Token);
        var create = await client.PostAsJsonAsync("/api/admin/plans", new
        {
            name = Unique("Paid "), pricePerMonth = 500m, maxEmployees = 5, maxCompanies = 1,
            isActive = true,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var plan = await create.Content.ReadJsonAsync<AdminPlanDto>();

        var flag = await client.PutAsJsonAsync($"/api/admin/plans/{plan!.Id}/system-trial", new { isSystemTrial = true });
        flag.StatusCode.Should().Be(HttpStatusCode.BadRequest, "US-18-01: триал не оплачивается, PricePerMonth обязан быть 0");
    }

    [Fact, TestCase("CY18-A02")]
    public async Task SetSystemTrial_SecondPlan_Returns409_PartialUniqueIndexHolds()
    {
        await CreateTrialPlanAsync();
        var admin = await LoginAsSuperAdminAsync();
        var client = AuthedClient(admin.Token);

        var create = await client.PostAsJsonAsync("/api/admin/plans", new
        {
            name = Unique("SecondTrial "), pricePerMonth = 0m, maxEmployees = 5, maxCompanies = 1, isActive = true,
        });
        var plan2 = await create.Content.ReadJsonAsync<AdminPlanDto>();

        var flag = await client.PutAsJsonAsync($"/api/admin/plans/{plan2!.Id}/system-trial", new { isSystemTrial = true });
        flag.StatusCode.Should().Be(HttpStatusCode.Conflict, "US-18-02: ровно один тариф может быть триалом");
    }

    [Fact, TestCase("CY18-A03")]
    public async Task DeleteTrialPlan_Returns409()
    {
        var trialId = await CreateTrialPlanAsync();
        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).DeleteAsync($"/api/admin/plans/{trialId}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "US-18-02: тариф пробного периода нельзя удалить");
    }

    [Fact, TestCase("CY18-A04")]
    public async Task DeactivateTrialPlan_WithLiveSubscriber_Returns409()
    {
        var trialId = await CreateTrialPlanAsync();
        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        var activate = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = await CurrentTrialTermsVersionAsync() });
        activate.StatusCode.Should().Be(HttpStatusCode.OK, await activate.Content.ReadAsStringAsync());

        var admin = await LoginAsSuperAdminAsync();
        var update = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{trialId}", new
        {
            name = "Trial (renamed)", pricePerMonth = 0m, maxEmployees = 25, maxCompanies = 5, isActive = false,
        });
        update.StatusCode.Should().Be(HttpStatusCode.Conflict, "US-18-01: деактивация роняет живых подписчиков в Free мгновенно и молча");
    }

    /// <summary>
    /// R4 (SPEC.md §5) — the found-but-not-fixed hole: "Триал не считается кандидатом" на приём
    /// флага системного бесплатного тарифа. <c>ValidateSystemFreeAsync</c> (AdminController) checks
    /// only <c>PricePerMonth == 0</c> and "no other IsSystemFree plan exists" — it never checks
    /// <c>IsSystemTrial</c>. This test documents the concrete, reachable violation: a superadmin can
    /// mark the Trial plan itself as the system free plan, producing one row with BOTH flags true,
    /// which US-18-02's own last bullet says must never happen ("флаг триала пытаются поставить на
    /// тариф ... системный бесплатный — отказ; один тариф не может быть одновременно системным
    /// бесплатным и триалом" — and by symmetry the reverse direction).
    /// </summary>
    [Fact, TestCase("CY18-A05")]
    public async Task SetSystemFree_OnTrialPlan_MustBeRejected()
    {
        var trialId = await CreateTrialPlanAsync();
        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{trialId}/system-free", new { isSystemFree = true });

        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "BUG (backend): SetSystemFree/ValidateSystemFreeAsync never checks plan.IsSystemTrial — a plan can end up " +
            "IsSystemTrial == true AND IsSystemFree == true simultaneously, which US-18-02's last bullet forbids.");
    }

    // ── Block B — platform settings validation (US-18-03) ───────────────────────────────────────

    [Theory, TestCase("CY18-B01")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public async Task PlatformSettings_InvalidTrialDurationDays_Returns400(int badValue)
    {
        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).PutAsJsonAsync("/api/admin/platform-settings",
            new { trialDurationDays = badValue });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY18-B02")]
    public async Task PlatformSettings_MailingWindowLongerThanDuration_Returns400()
    {
        var admin = await LoginAsSuperAdminAsync();
        var client = AuthedClient(admin.Token);
        await SetTrialSettingsAsync(client, 7, null);
        var response = await client.PutAsJsonAsync("/api/admin/platform-settings",
            new { trialMailingWindowDays = 30 });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "US-18-03: окно рассылок больше длительности триала — отказ, а не молчаливое усечение");
    }

    [Fact, TestCase("CY18-B03")]
    public async Task PlatformSettings_GetAfterPut_ReflectsTrialFields()
    {
        var admin = await LoginAsSuperAdminAsync();
        var client = AuthedClient(admin.Token);
        await SetTrialSettingsAsync(client, 21, 5);

        var response = await client.GetAsync("/api/admin/platform-settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("21").And.Contain("5");
    }

    // ── Block C/D — activation, one-time-ness, antifraud (US-18-04…07) ──────────────────────────

    private async Task<string> CurrentTrialTermsVersionAsync() =>
        await DbAsync(async db =>
        {
            // TrialStateReader is the same code path GetTrial uses — call it via HTTP instead, cheaper:
            return await Task.FromResult(ServiceBooking.API.Services.Billing.TrialTermsRegistry.CurrentVersion);
        });

    [Fact, TestCase("CY18-C01")]
    public async Task ActivateTrial_HappyPath_SetsPlanAndDatesAndWritesChangeLog()
    {
        await CreateTrialPlanAsync(durationDays: 14, mailingWindowDays: 7);
        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();

        var response = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = await CurrentTrialTermsVersionAsync() });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var sub = await DbAsync(db => db.AccountSubscriptions.Include(s => s.PlanConfig)
            .FirstAsync(s => s.BillingAccountId == accountId));
        sub.PlanConfig!.IsSystemTrial.Should().BeTrue();
        sub.PaidUntil.Should().NotBeNull();
        sub.PaidUntil!.Value.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(2));

        var log = await DbAsync(db => db.SubscriptionChangeLogs
            .Where(l => l.BillingAccountId == accountId).OrderByDescending(l => l.ChangedAt).FirstAsync());
        log.ChangeKind.Should().Be(SubscriptionChangeKind.TrialGranted);

        var account = await DbAsync(db => db.BillingAccounts.FirstAsync(a => a.Id == accountId));
        account.TrialStartedAtUtc.Should().NotBeNull();
        account.TrialEndsAtUtc.Should().NotBeNull();
    }

    [Fact, TestCase("CY18-C02")]
    public async Task ActivateTrial_SecondTimeWhileStillActive_Returns409TrialAlreadyActive()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, _) = await CreateOwnerWithVerifiedPhoneAsync();
        var termsVersion = await CurrentTrialTermsVersionAsync();

        var first = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var refusal = await second.Content.ReadFromJsonAsync<TrialRefusalDto>();
        refusal!.Code.Should().Be("TrialAlreadyActive");
    }

    [Fact, TestCase("CY18-C02B")]
    public async Task ActivateTrial_AfterOwnTrialExpired_Returns409TrialAlreadyUsed()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        var termsVersion = await CurrentTrialTermsVersionAsync();
        var first = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        await ExpireTrialAsync(accountId);

        var second = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "US-18-04: «даже давно истёкший» триал по-прежнему одноразовый");
        var refusal = await second.Content.ReadFromJsonAsync<TrialRefusalDto>();
        refusal!.Code.Should().Be("TrialAlreadyUsed");
    }

    /// <summary>Simulates the trial's own end date having passed — writes directly, since this cycle has
    /// no background task that would do it itself (see the QA report: US-18-11/13 gap).</summary>
    private async Task ExpireTrialAsync(Guid accountId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.AccountSubscriptions.FirstAsync(s => s.BillingAccountId == accountId);
        sub.PaidUntil = DateTime.UtcNow.AddDays(-1);
        var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
        account.TrialEndsAtUtc = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
    }

    /// <summary>The shared class host stays on <c>PhoneVerification:Provider = "stub"</c> (the Testing
    /// default, §0.5's "невыпущенность"), under which <c>MaxBotVerificationAdapter.Enabled</c> is false.
    /// With no verified phone, that puts this case in R7's "cannot verify at all right now" branch —
    /// PhoneVerificationUnavailable, not PhoneNotVerified — mirroring
    /// <c>ChangePhoneGateOutcome.SubsystemDisabled</c>'s own condition
    /// (<c>GuestBookingGateDecision.Evaluate</c>): the honest refusal fires only when verification is
    /// actually needed AND unreachable, never merely because the subsystem happens to be off.</summary>
    [Fact, TestCase("CY18-C03")]
    public async Task ActivateTrial_WithoutVerifiedPhone_SubsystemDisabled_Returns409PhoneVerificationUnavailable()
    {
        await CreateTrialPlanAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false); // phone never verified

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = await CurrentTrialTermsVersionAsync() });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var refusal = await response.Content.ReadFromJsonAsync<TrialRefusalDto>();
        refusal!.Code.Should().Be("PhoneVerificationUnavailable");
    }

    /// <summary>Same unverified-owner setup as above, but against a secondary host with
    /// <c>PhoneVerification:Provider = "max-bot"</c> (<see cref="PhoneVerificationEnabledFactory"/>,
    /// same class database, same fixed JWT signing key from <c>TestHostSettings.Apply</c> so the token
    /// minted by the shared host is still accepted). With the subsystem reachable, R7's branch 3 applies:
    /// the ordinary PhoneNotVerified prompt, not the "subsystem unavailable" refusal.</summary>
    [Fact, TestCase("CY18-C03b")]
    public async Task ActivateTrial_WithoutVerifiedPhone_SubsystemEnabled_Returns409PhoneNotVerified()
    {
        await CreateTrialPlanAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false); // phone never verified

        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        var response = await client.PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = await CurrentTrialTermsVersionAsync() });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var refusal = await response.Content.ReadFromJsonAsync<TrialRefusalDto>();
        refusal!.Code.Should().Be("PhoneNotVerified");
    }

    [Fact, TestCase("CY18-C04")]
    public async Task ActivateTrial_SamePhoneDifferentAccount_Returns409TrialPhoneAlreadyUsed_NoOracle()
    {
        await CreateTrialPlanAsync();
        var phone = UniquePhone();

        var (ownerA, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await MarkPhoneVerifiedAsync(phone, ownerA.UserId);
        // simulate "ownerA's phone" directly on ownerA's VerifiedPhones row (helper keys by phone+user)
        var termsVersion = await CurrentTrialTermsVersionAsync();
        var firstActivate = await AuthedClient(ownerA.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion });
        firstActivate.StatusCode.Should().Be(HttpStatusCode.OK, await firstActivate.Content.ReadAsStringAsync());

        // Simulates "ownerA deleted their account" — VerifiedPhones holds only current state and the
        // row disappears with the account (§4.27, VerifiedPhone.cs), freeing the number for a NEW
        // account. The TrialPhoneRegistrations row from ownerA's activation must survive this and still
        // block ownerB — that is the entire point of Д6's separate append-only registry.
        await DbAsync(async db =>
        {
            var rows = await db.VerifiedPhones.Where(v => v.UserId == ownerA.UserId).ToListAsync();
            db.VerifiedPhones.RemoveRange(rows);
            await db.SaveChangesAsync();
            return true;
        });

        // ownerB: a brand-new account, re-verified on the SAME number — the "deleted account,
        // re-registered on the same number" shape from Д6/US-18-05.
        var (ownerB, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await MarkPhoneVerifiedAsync(phone, ownerB.UserId);

        var second = await AuthedClient(ownerB.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var refusal = await second.Content.ReadFromJsonAsync<TrialRefusalDto>();
        refusal!.Code.Should().Be("TrialPhoneAlreadyUsed");
        refusal.Message.Should().NotContainAny(["ownerA", ownerA.UserId, "уже зарегистрирован аккаунт"],
            "§4.27 🔒: отказ не должен быть оракулом чужого аккаунта");
    }

    [Fact, TestCase("CY18-C05")]
    public async Task ActivateTrial_ConcurrentDoubleActivation_OnlyOneSucceeds()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        var termsVersion = await CurrentTrialTermsVersionAsync();

        var client1 = AuthedClient(ownerLike.Token);
        var client2 = AuthedClient(ownerLike.Token);
        var task1 = client1.PostAsJsonAsync("/api/billing/trial", new { termsVersion });
        var task2 = client2.PostAsJsonAsync("/api/billing/trial", new { termsVersion });
        var results = await Task.WhenAll(task1, task2);

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "US-18-04: гонка двух активаций — победитель ровно один");
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var grantCount = await DbAsync(db => db.TrialGrants.CountAsync(g => g.BillingAccountId == accountId));
        grantCount.Should().Be(1, "ни второй строки TrialGrants, ни второй записи в журнале появиться не должно");
    }

    [Fact, TestCase("CY18-C06")]
    public async Task ActivateTrial_AlreadyOnPaidPlan_Returns409AlreadyOnPaidPlan()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, _) = await CreateOwnerWithVerifiedPhoneAsync();
        await GiveActivePaidPlanAsync(ownerLike.UserId);

        var response = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = await CurrentTrialTermsVersionAsync() });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var refusal = await response.Content.ReadFromJsonAsync<TrialRefusalDto>();
        refusal!.Code.Should().Be("AlreadyOnPaidPlan");
    }

    [Fact, TestCase("CY18-C07")]
    public async Task ActivateTrial_PlanNotPublic_Returns409TrialNotOffered()
    {
        var trialId = await CreateTrialPlanAsync();
        var admin = await LoginAsSuperAdminAsync();
        var off = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/plans/{trialId}", new
        {
            name = "Trial", pricePerMonth = 0m, maxEmployees = 25, maxCompanies = 5, isActive = true, isPublic = false,
        });
        off.StatusCode.Should().Be(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());

        var (ownerLike, _) = await CreateOwnerWithVerifiedPhoneAsync();
        var response = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = await CurrentTrialTermsVersionAsync() });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var refusal = await response.Content.ReadFromJsonAsync<TrialRefusalDto>();
        refusal!.Code.Should().Be("TrialNotOffered", "US-18-01: isPublic:false останавливает новые выдачи");
    }

    [Fact, TestCase("CY18-C08")]
    public async Task ActivateTrial_WrongTermsVersion_Returns409TrialTermsVersionMismatch()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, _) = await CreateOwnerWithVerifiedPhoneAsync();
        var response = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = "not-a-real-version" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var refusal = await response.Content.ReadFromJsonAsync<TrialRefusalDto>();
        refusal!.Code.Should().Be("TrialTermsVersionMismatch");
    }

    [Fact, TestCase("CY18-C09")]
    public async Task ActivateTrial_EmptyTermsVersion_Returns409_NotServerError()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, _) = await CreateOwnerWithVerifiedPhoneAsync();
        var response = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion = "" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "пустая строка версии условий — отказ, а не 500/сырое исключение");
    }

    // ── Superadmin grant/regrant (Д1, Д1-бис) ────────────────────────────────────────────────────

    [Fact, TestCase("CY18-D01")]
    public async Task SuperAdminGrant_UsesSameOnceOnlyCheckAsOwnerPath()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        var admin = await LoginAsSuperAdminAsync();

        var grant1 = await AuthedClient(admin.Token).PostAsync($"/api/admin/billing-accounts/{accountId}/trial", null);
        grant1.StatusCode.Should().Be(HttpStatusCode.OK, await grant1.Content.ReadAsStringAsync());

        var grant2 = await AuthedClient(admin.Token).PostAsync($"/api/admin/billing-accounts/{accountId}/trial", null);
        grant2.StatusCode.Should().Be(HttpStatusCode.Conflict, "Д1: суперадмин обязан пройти ту же проверку одноразовости");
    }

    [Fact, TestCase("CY18-D02")]
    public async Task SuperAdminRegrant_EmptyReason_Returns409()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        var admin = await LoginAsSuperAdminAsync();
        await AuthedClient(admin.Token).PostAsync($"/api/admin/billing-accounts/{accountId}/trial", null);
        await ExpireTrialAsync(accountId); // Д1-бис's real scenario: an already-used/ended trial, not a live one

        var regrant = await AuthedClient(admin.Token).PostAsJsonAsync(
            $"/api/admin/billing-accounts/{accountId}/trial/regrant", new { reason = "" });
        // Note: AdminBillingController.RegrantTrial 400s on an empty reason before ever calling
        // TrialActivationService (whose own "TrialRegrantReasonRequired" 409 check is therefore dead
        // code on this path) — a minor internal inconsistency, not a SPEC violation (SPEC only requires
        // "пустая строка — отказ", no particular status code).
        regrant.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Д1-бис: пустая причина — отказ");
    }

    [Fact, TestCase("CY18-D03")]
    public async Task SuperAdminRegrant_WithReason_BypassesAlreadyUsed_AndLogsReason()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        await adminClient.PostAsync($"/api/admin/billing-accounts/{accountId}/trial", null);
        await ExpireTrialAsync(accountId); // "выдан ошибочно и его нужно перевыдать" only makes sense once the first grant is no longer live

        const string reason = "Ошибочно выдан и сразу отозван вручную, восстанавливаем по просьбе клиента";
        var regrant = await adminClient.PostAsJsonAsync(
            $"/api/admin/billing-accounts/{accountId}/trial/regrant", new { reason });
        regrant.StatusCode.Should().Be(HttpStatusCode.OK, await regrant.Content.ReadAsStringAsync());

        var grants = await DbAsync(db => db.TrialGrants.Where(g => g.BillingAccountId == accountId).ToListAsync());
        grants.Should().HaveCount(2);
        var regrantRow = grants.Single(g => g.Source == TrialGrantSource.SuperAdminOverride);
        regrantRow.Reason.Should().Be(reason, "Д1-бис: причина обязана попасть в журнал");
        regrantRow.GrantedByUserId.Should().Be(admin.UserId);
    }

    [Fact, TestCase("CY18-D04")]
    public async Task OwnerActivation_CannotBypassUsedMark_OnlySuperAdminOverrideCan()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        var admin = await LoginAsSuperAdminAsync();
        await AuthedClient(admin.Token).PostAsync($"/api/admin/billing-accounts/{accountId}/trial", null);

        // The owner never got a "shown terms" moment for the admin-granted trial, but that must not
        // let a subsequent OWNER POST re-grant it — the ordinary path is never bypassable (Д1-бис).
        var ownerAttempt = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = await CurrentTrialTermsVersionAsync() });
        ownerAttempt.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var refusal = await ownerAttempt.Content.ReadFromJsonAsync<TrialRefusalDto>();
        refusal!.Code.Should().Be("TrialAlreadyActive");
    }

    // ── US-18-12 — public pricing / preview ─────────────────────────────────────────────────────

    private static async Task SetPricingPublicEnabledAsync(HttpClient adminClient, bool enabled)
    {
        var response = await adminClient.PutAsJsonAsync("/api/admin/platform-settings", new { pricingPublicEnabled = enabled });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact, TestCase("CY18-E01")]
    public async Task PublicPricing_TrialPlan_IsMarkedIsTrial_InResponse()
    {
        await CreateTrialPlanAsync();
        var admin = await LoginAsSuperAdminAsync();
        await SetPricingPublicEnabledAsync(AuthedClient(admin.Token), enabled: true);

        var response = await AnonymousClient().GetAsync("/api/pricing");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "public pricing must be reachable in the test environment");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"isTrial\":true",
            "BUG (backend/contract): PublicPlanDto has no IsTrial property (ToPlanDto never sets one) — " +
            "the frontend's PlanCard.tsx reads plan.isTrial for the trial badge (US-18-01/§370) and will " +
            "always get undefined. GET /api/pricing must expose which plan is the trial the same way it " +
            "exposes IsFree.");
    }

    [Fact, TestCase("CY18-E02")]
    public async Task AdminPricingPreview_ShowsTrial_EvenWhenPublicShowcaseSwitchIsOff()
    {
        var trialId = await CreateTrialPlanAsync();
        var admin = await LoginAsSuperAdminAsync();
        var client = AuthedClient(admin.Token);
        await SetPricingPublicEnabledAsync(client, enabled: false);

        var publicResponse = await AnonymousClient().GetAsync("/api/pricing");
        publicResponse.StatusCode.Should().Be(HttpStatusCode.NotFound, "рубильник выключен — публично ничего не видно");

        var preview = await client.GetAsync("/api/admin/pricing/preview");
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await preview.Content.ReadAsStringAsync();
        body.Should().Contain(trialId.ToString(), "US-18-12: предпросмотр обязан показывать Триал даже когда витрина заблокирована");
    }

    // ── US-18-14 — admin billing-account trial visibility ──────────────────────────────────────

    [Fact, TestCase("CY18-F01")]
    public async Task AdminBillingAccounts_FilterByTrialActive_ReturnsOnlyActiveTrialAccounts()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        await AuthedClient((await LoginAsSuperAdminAsync()).Token)
            .PostAsync($"/api/admin/billing-accounts/{accountId}/trial", null);

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/billing-accounts?trial=active&pageSize=200");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(ownerLike.UserId);
    }

    [Fact, TestCase("CY18-F02")]
    public async Task AdminBillingAccounts_InvalidTrialFilterValue_Returns400()
    {
        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/billing-accounts?trial=bogus");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY18-F03")]
    public async Task AdminBillingAccountDetail_ShowsTrialGrantHistory()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        var termsVersion = await CurrentTrialTermsVersionAsync();
        await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion });

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync($"/api/admin/billing-accounts/{accountId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"trial\"", "US-18-14: карточка биллинг-аккаунта обязана показывать состояние триала");
        body.Should().Contain("OwnerSelfService");
    }

    // ── Delete-account preview — trial registry notice (Д20) ───────────────────────────────────

    [Fact, TestCase("CY18-G01")]
    public async Task DeleteAccountPreview_AfterTrial_ShowsRegistryNotice()
    {
        await CreateTrialPlanAsync();
        var (ownerLike, _) = await CreateOwnerWithVerifiedPhoneAsync();
        var termsVersion = await CurrentTrialTermsVersionAsync();
        await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion });

        var response = await AuthedClient(ownerLike.Token).GetAsync("/api/profile/delete-account/preview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AccountDeletionPreviewDto>();
        dto!.TrialRegistryNotice.Should().NotBeNullOrWhiteSpace("Д20: TrialRegistryNoticeOnAccountDeletion обязан появиться");
    }

    [Fact, TestCase("CY18-G02")]
    public async Task DeleteAccountPreview_NeverHadTrial_NoRegistryNotice()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var response = await AuthedClient(owner.Token).GetAsync("/api/profile/delete-account/preview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AccountDeletionPreviewDto>();
        dto!.TrialRegistryNotice.Should().BeNull();
    }

    // ── Re-check after code-reviewer fixes (efc716b, 255213a, 7bc88fd, 36669b4, 8a943a7) ─────────
    // §365 API_CONTRACT_CYCLE18.md: GET/POST /api/billing/subscription must carry the SAME TrialStateDto
    // as GET /api/billing/trial (owner cabinet's "Ваша подписка" card had nothing to read from before
    // efc716b), and SubscriptionUsageDto must carry over-limit fields (Д3, "деградация = заморозка").

    [Fact, TestCase("CY18-H01")]
    public async Task OwnerSubscription_WhileTrialActive_TrialFieldMatchesGetTrialEndpoint()
    {
        await CreateTrialPlanAsync(durationDays: 14, mailingWindowDays: 7);
        var (ownerLike, _) = await CreateOwnerWithVerifiedPhoneAsync();
        var termsVersion = await CurrentTrialTermsVersionAsync();
        var activate = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial", new { termsVersion });
        activate.StatusCode.Should().Be(HttpStatusCode.OK, await activate.Content.ReadAsStringAsync());

        var client = AuthedClient(ownerLike.Token);
        var subscriptionResponse = await client.GetAsync("/api/billing/subscription");
        subscriptionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var subscriptionDto = await subscriptionResponse.Content.ReadFromJsonAsync<OwnerSubscriptionDto>();
        subscriptionDto!.Trial.Should().NotBeNull(
            "§365: GET /api/billing/subscription обязан нести то же поле trial, что и GET /api/billing/trial");
        subscriptionDto.Trial!.State.Should().Be("Active");

        var trialResponse = await client.GetAsync("/api/billing/trial");
        trialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var trialDto = await trialResponse.Content.ReadFromJsonAsync<TrialStateDto>();

        subscriptionDto.Trial!.EndsAt.Should().Be(trialDto!.EndsAt, "оба эндпоинта не имеют права разойтись");
        subscriptionDto.Trial!.DaysLeft.Should().Be(trialDto.DaysLeft);
    }

    [Fact, TestCase("CY18-H02")]
    public async Task OwnerSubscription_NeverHadTrial_TrialFieldExplainsUnavailability_NotNull()
    {
        // Corrected after first run: OwnerSubscriptionDto.Trial mirrors GET /api/billing/trial exactly,
        // and that endpoint is documented to never answer null just because a trial isn't available —
        // it always explains WHY not (US-18-06/US-18-12: "либо почему нет — и это решает сервер").
        // CreateTrialPlanAsync restores the class-shared trial plan to isActive/isPublic/price==0 —
        // needed here because earlier tests in this class deliberately leave it unpublished/inactive,
        // and this test cares about the "no verified phone" refusal specifically, not "no trial offered".
        await CreateTrialPlanAsync();
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var response = await AuthedClient(owner.Token).GetAsync("/api/billing/subscription");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<OwnerSubscriptionDto>();
        dto!.Trial.Should().NotBeNull("§365: поле должно быть непустым всегда, когда GET /api/billing/trial непусто (а он непуст всегда)");
        dto.Trial!.State.Should().Be("Unavailable");
        dto.Trial!.RefusalCode.Should().Be("PhoneVerificationUnavailable",
            "стенд остаётся на Provider=stub (§0.5) — без подтверждённого номера и без работающей подсистемы это R7's честный отказ, а не PhoneNotVerified");
    }

    [Fact, TestCase("CY18-H03")]
    public async Task OwnerSubscription_UsageOverPlanLimits_ReturnsOverLimitFieldsNotZero()
    {
        // Д3 "деградация = заморозка": владелец уже создал компанию, затем оказался на тарифе с лимитом
        // компаний ниже уже созданного количества (ровно то, что происходит после истечения триала).
        // Проверяется напрямую через БД — не через сам переход триал→Free, которого в коде нет (US-18-11
        // gap, см. отчёт), а через сам факт: usage > limit ⇒ overLimit* заполнены, ничего не падает.
        //
        // Code-review finding (cycle 18 recheck, §365 Т4) — overLimit* only ever describes "fell back to
        // the system Free tariff and is over ITS limits", never "usage exceeds whatever paid plan is
        // currently assigned". So this scenario must subscribe the account to the actual system Free
        // plan config (IsSystemFree == true), not to a fresh unrelated paid plan — a paid plan an admin
        // tightens is a different, legitimate state that must report 0/0/null instead (covered by a
        // dedicated OwnerSubscriptionService.BuildOverLimit unit test).
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var freePlan = await db.SubscriptionPlanConfigs.FirstAsync(p => p.IsSystemFree);
            freePlan.MaxCompanies = 0;
            var account = await db.BillingAccounts.FirstAsync(a => a.OwnerUserId == owner.UserId);
            db.AccountSubscriptions.Add(new AccountSubscription
            {
                Id = Guid.NewGuid(), OwnerUserId = owner.UserId, BillingAccountId = account.Id,
                PlanConfigId = freePlan.Id, IsActive = true, PaidUntil = null,
            });
            await db.SaveChangesAsync();
        }

        var response = await AuthedClient(owner.Token).GetAsync("/api/billing/subscription");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var dto = await response.Content.ReadFromJsonAsync<OwnerSubscriptionDto>();
        dto!.Usage.OverLimitCompanies.Should().BeGreaterThan(0, "владелец уже создал 1 компанию, лимит нового тарифа — 0");
        dto.Usage.OverLimitText.Should().NotBeNullOrWhiteSpace("Д3: владелец обязан увидеть, на сколько он над лимитом");
    }

    [Fact, TestCase("CY18-H04")]
    public async Task AdminBillingAccountCard_NeverHadTrial_ReturnsTrialObjectWithStateNever_NotNull()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var accountId = await DbAsync(db => db.BillingAccounts
            .Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync());

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync($"/api/admin/billing-accounts/{accountId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var trial = doc.RootElement.GetProperty("trial");
        trial.ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Null,
            "code-review finding #2: trial НЕ nullable — админский grant-триал-кнопкой экран рендерится только при state==\"Never\"");
        trial.GetProperty("state").GetString().Should().Be("Never");
    }

    // ── Backend late-delta recheck (Б2/Б1, code review) ─────────────────────────────────────────
    // §333.3's materialization and §337.1 phase 4's GrantedByTrial narrowing are two halves of the same
    // fix (the review explicitly required them "одним куском") — covered together here rather than
    // split across files, since QA's Cycle18TrialLifecycleTests.cs predates this fix entirely.

    [Fact, TestCase("CY18-B2-01")]
    public async Task ActivateTrial_WithIncludedWhatsAppRule_MaterializesAccountSubscriptionOption()
    {
        var trialId = await CreateTrialPlanAsync();
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);

        var whatsappOptionId = await DbAsync(db => db.SubscriptionOptions
            .Where(o => o.Code == SubscriptionResolver.WhatsAppOptionCode).Select(o => o.Id).FirstAsync());

        // Trial plan's own matrix must say Included for §333.3 to materialize anything.
        var setRule = await adminClient.PutAsJsonAsync($"/api/admin/plans/{trialId}", new
        {
            name = "Trial Plan (QA)", pricePerMonth = 0m, maxEmployees = 25, maxCompanies = 5, isActive = true,
            options = new[] { new { optionId = whatsappOptionId, availability = "Included", includedQuantity = 3 } },
        });
        setRule.StatusCode.Should().Be(HttpStatusCode.OK, await setRule.Content.ReadAsStringAsync());

        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        var activate = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = await CurrentTrialTermsVersionAsync() });
        activate.StatusCode.Should().Be(HttpStatusCode.OK, await activate.Content.ReadAsStringAsync());

        var option = await DbAsync(db => db.AccountSubscriptionOptions
            .Where(o => o.BillingAccountId == accountId && o.OptionId == whatsappOptionId).SingleOrDefaultAsync());
        option.Should().NotBeNull("§333.3: R3 требует материализованную строку опции, иначе рассылки на триале физически не идут");
        option!.Quantity.Should().Be(3, "Quantity = PlanOptionRule.IncludedQuantity");
        option.GrantedByTrial.Should().BeTrue();
        option.EndsAtUtc.Should().BeNull();
    }

    [Fact, TestCase("CY18-B1-01")]
    public async Task ActivateTrial_AccountHadStaleAdminPaidWhatsAppOption_LeavesItUntouched_AndSurvivesLifecycleExpiry()
    {
        var trialId = await CreateTrialPlanAsync();
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);

        var whatsappOptionId = await DbAsync(db => db.SubscriptionOptions
            .Where(o => o.Code == SubscriptionResolver.WhatsAppOptionCode).Select(o => o.Id).FirstAsync());
        await adminClient.PutAsJsonAsync($"/api/admin/plans/{trialId}", new
        {
            name = "Trial Plan (QA)", pricePerMonth = 0m, maxEmployees = 25, maxCompanies = 5, isActive = true,
            options = new[] { new { optionId = whatsappOptionId, availability = "Included", includedQuantity = 1 } },
        });

        var (ownerLike, accountId) = await CreateOwnerWithVerifiedPhoneAsync();

        // Simulates the finding's exact precondition: an admin-assigned PAID whatsapp option row,
        // EndsAtUtc == null, from a paid subscription whose own paid period has since lapsed — created
        // directly against the DB rather than through AssignSubscription (whose own "не в прошлом"
        // validation would reject the already-lapsed PaidUntil this scenario needs).
        var adminOptionId = Guid.NewGuid();
        await DbAsync(async db =>
        {
            db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
            {
                Id = adminOptionId, BillingAccountId = accountId, OptionId = whatsappOptionId,
                Quantity = 7, PaidUntilUtc = null, EndsAtUtc = null,
                ActivatedAtUtc = DateTime.UtcNow.AddDays(-100), ActivatedByUserId = admin.UserId,
                GrantedByTrial = false,
            });
            await db.SaveChangesAsync();
            return true;
        });

        var activate = await AuthedClient(ownerLike.Token).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = await CurrentTrialTermsVersionAsync() });
        activate.StatusCode.Should().Be(HttpStatusCode.OK, await activate.Content.ReadAsStringAsync());

        var afterActivation = await DbAsync(db => db.AccountSubscriptionOptions.SingleAsync(o => o.Id == adminOptionId));
        afterActivation.Quantity.Should().Be(7, "B1: строка не от триала — активация триала не имеет права её трогать");
        afterActivation.GrantedByTrial.Should().BeFalse();
        afterActivation.EndsAtUtc.Should().BeNull();

        // Force the trial past its own end date and run the background task's expiry phase directly.
        await DbAsync(async db =>
        {
            var account = await db.BillingAccounts.SingleAsync(a => a.Id == accountId);
            account.TrialEndsAtUtc = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
            return true;
        });

        using (var scope = Factory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "trial-lifecycle");
            await task.ExecuteAsync(CancellationToken.None);
        }

        var afterExpiry = await DbAsync(db => db.AccountSubscriptionOptions.SingleAsync(o => o.Id == adminOptionId));
        afterExpiry.EndsAtUtc.Should().BeNull(
            "B1: сама находка — до фикса ЛЮБАЯ строка с EndsAtUtc == null на аккаунте (в т.ч. не от триала) " +
            "гасилась фазой 4 безвозвратно; GrantedByTrial должен сузить выборку до строк самого триала");
        afterExpiry.Quantity.Should().Be(7);
    }
}
