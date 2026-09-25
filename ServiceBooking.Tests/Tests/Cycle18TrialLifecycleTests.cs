using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Retention;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 18 ("Вызов 2", second pass) — the three incomplete architecture pieces flagged as bugs #3-#5
/// in TEST_CATALOG.md's "Найденные баги" section (mailing-window start hook §336, the trial-lifecycle
/// background task §337, and the trial phone registry's retention rule §343) are now implemented
/// (commits d2fff69, b37b566, a7d2979). The developer who wrote them did not add functional coverage —
/// this file closes that gap, written from ARCHITECTURE_CYCLE18.md §336/§337/§343 and SPEC.md decisions
/// Д5/Д16/Д18/Д19, independently of the implementation itself.
///
/// Deliberately NOT in <c>Cycle18TrialPlanTests.cs</c> (untouched by this pass — backend-developer is
/// actively working in it) and does not re-test anything already covered there (catalog protections,
/// activation, one-time-ness, admin surface, public pricing).
///
/// QA "Вызов 2" recheck (code-review delta on top of commit d43f701): CY18L-15 onward close two
/// review-flagged blockers this file's first 14 tests would NOT have caught (the trial mailing window's
/// notifications.whatsapp option row never actually reaching a real send, §333.3/B1/B2; the expiry phase
/// dating out a pre-existing PAID option row it never granted, §337.3) plus three coverage gaps (batch
/// keyset cursors beyond <c>BatchSize</c>=100, journal atomicity on a genuinely failed per-account
/// iteration, and the empty-string variant of the "no current key configured" retention guard).
/// </summary>
// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// Part 1 — §336 mailing-window start hook, scenarios 1-5. Needs a host with the channel/webhook/QR
// surface actually reachable (Notifications:Provider=logging, GreenApi instance creation enabled,
// WebhookToken configured) AND the trial subsystem's HMAC key configured — neither
// NotificationTestFactory nor CustomWebApplicationFactory alone gives both, so this file defines its
// own dedicated factory (same pattern as NotificationTestFactory/RateLimitTestFactory/etc., one nested
// factory per QA concern).
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

public class Cycle18TrialMailingWindowHookTests : IClassFixture<TestDatabaseFixture>, IAsyncDisposable
{
    private const string WebhookToken = "cy18-mailing-window-webhook-token";

    private readonly TestDatabaseFixture _fixture;
    private readonly ChannelHookFactory _factory;

    public Cycle18TrialMailingWindowHookTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _factory = new ChannelHookFactory(fixture.ConnectionString);
        _ = _factory.Services; // boot eagerly so Identity is populated before LoginAsSuperAdminAsync
        fixture.RecordTestClass(GetType().Name);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>Combines NotificationTestFactory's settings (channel/webhook/QR surface reachable) with
    /// CustomWebApplicationFactory's Trial:PhoneKeyHmac/PhoneKeyId test key (trial activation not
    /// fail-closed) — see both classes' own doc comments for why each individual setting exists.</summary>
    private sealed class ChannelHookFactory(string connectionString) : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        public TestHostIdentity Identity { get; private set; } = null!;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Reusing the "ntf" factoryTag is safe: it only selects a SuperAdmin phone/email pair and a
            // temp-directory slot derived from THIS class's own connection string/database name — never
            // shared with an actual NotificationTestFactory instance, which always lives in a different
            // test class with a different database.
            Identity = TestHostSettings.Apply(builder, "ntf", connectionString);

            builder.UseSetting("Notifications:Provider", "logging");
            builder.UseSetting("Notifications:EncryptionKey", NotificationDispatchTestFactory.TestEncryptionKeyBase64);
            builder.UseSetting("Notifications:WebhookToken", WebhookToken);
            builder.UseSetting("Notifications:GreenApi:ServerCountry", "Russia");
            builder.UseSetting("Notifications:GreenApi:InstanceCreationEnabled", "true");

            // Same test key CustomWebApplicationFactory uses (Cycle 18 QA finding, see that class) — Trial
            // activation fail-closes with TrialUniquenessCheckUnavailable without it.
            builder.UseSetting("Trial:PhoneKeyHmac", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
            builder.UseSetting("Trial:PhoneKeyId", "qa-test-key");
        }
    }

    // ── HTTP/DI helpers (mirrors NotificationTestBase/ApiTestBase's own, scoped to this factory) ────

    private HttpClient AnonymousClient() => _factory.CreateClient();

    private HttpClient AuthedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string Unique(string prefix) => _fixture.Data.Name(prefix);
    private string UniquePhone() => _fixture.Data.Phone();

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(db);
    }

    private async Task RunInDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
        await db.SaveChangesAsync();
    }

    private RegisterLegalDto CurrentRegisterLegalDto()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        var snapshot = provider.Current!;
        return new RegisterLegalDto(
            snapshot.Get(LegalDocumentType.Privacy)!.Version, snapshot.Get(LegalDocumentType.TermsClient)!.Version);
    }

    private OwnerTermsDto CurrentOwnerTermsDto()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        return new OwnerTermsDto(provider.Current!.Get(LegalDocumentType.TermsOwner)!.Version);
    }

    private async Task<AuthResponseDto> RegisterAsync()
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Test", "Owner", UniquePhone(), "Password123!", null, CurrentRegisterLegalDto()));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    private async Task<AuthResponseDto> LoginAsync(string phone)
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/login", new LoginDto(phone, "Password123!"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    private async Task<AuthResponseDto> LoginAsSuperAdminAsync()
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/login",
            new LoginDto(_factory.Identity.SuperAdminPhone, _factory.Identity.SuperAdminPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    private async Task<int> AnyCityIdAsync() =>
        await DbAsync(db => db.Cities.Where(c => c.IsActive).Select(c => c.Id).FirstAsync());

    /// <summary>Registers an owner, gives them exactly one company (needed to be recognized as a
    /// CompanyOwner at all — the trial/channel endpoints don't care about the company itself) and
    /// marks their phone verified — everything <c>/api/billing/trial</c> needs besides the plan.</summary>
    private async Task<(AuthResponseDto Owner, Guid AccountId)> CreateOwnerWithVerifiedPhoneAsync()
    {
        var registered = await RegisterAsync();
        var slug = Unique("company-");
        var createCompany = await AuthedClient(registered.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto($"Company {slug}", slug, null, null, null, null, await AnyCityIdAsync(), null, true,
                OwnerTerms: CurrentOwnerTermsDto()));
        createCompany.EnsureSuccessStatusCode();
        var owner = await LoginAsync(registered.Phone); // re-login: CompanyOwner role only lands after this

        await DbAsync(async db =>
        {
            var canonicalPhone = owner.Phone.TrimStart('+').Replace(" ", "");
            if (!await db.VerifiedPhones.AnyAsync(v => v.Phone == canonicalPhone))
            {
                db.VerifiedPhones.Add(new VerifiedPhone
                {
                    Id = Guid.NewGuid(), Phone = canonicalPhone, Method = PhoneVerificationMethod.MaxBot,
                    VerifiedAtUtc = DateTime.UtcNow, UserId = owner.UserId,
                });
                await db.SaveChangesAsync();
            }
            return true;
        });

        var accountId = await DbAsync(db => db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync());
        return (owner, accountId);
    }

    /// <summary>Idempotent across the whole shared class database — same reasoning as
    /// <c>Cycle18TrialPlanTests.CreateTrialPlanAsync</c>'s own doc comment (partial unique index on
    /// IsSystemTrial, one row for the whole class).</summary>
    private async Task<Guid> CreateTrialPlanAsync(int durationDays = 14, int mailingWindowDays = 7)
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
                name = "Trial Plan (QA hook)", description = "Пробный период",
                highlights = new[] { "Максимум возможностей" },
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
                name = Unique("Trial Plan Hook "), description = "Пробный период",
                highlights = new[] { "Максимум возможностей" },
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

        var settings = await client.PutAsJsonAsync("/api/admin/platform-settings",
            new { trialDurationDays = durationDays, trialMailingWindowDays = mailingWindowDays });
        settings.StatusCode.Should().Be(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        return planId;
    }

    private async Task<HttpResponseMessage> ActivateTrialAsync(string ownerToken) =>
        await AuthedClient(ownerToken).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = TrialTermsRegistry.CurrentVersion });

    /// <summary>Writes a <see cref="NotificationChannel"/> row directly — bypassing the paid-channel
    /// provisioning workflow (already covered by <c>NotificationChannelsTests.cs</c>), since this file's
    /// concern is the trial mailing-window side effect of a channel reaching <c>Connected</c>, not the
    /// provisioning workflow itself.</summary>
    private async Task<NotificationChannel> CreateChannelRowAsync(
        string ownerUserId, Guid billingAccountId, ChannelState state, string providerInstanceId)
    {
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerUserId, BillingAccountId = billingAccountId,
            Transport = NotificationTransport.WhatsApp, State = state,
            ProviderInstanceId = providerInstanceId,
            ProviderSecretCiphertext = "dummy-ciphertext-not-decrypted-in-this-test",
        };
        await DbAsync(async db =>
        {
            db.NotificationChannels.Add(channel);
            await db.SaveChangesAsync();
            return true;
        });
        return channel;
    }

    private static string StateInstanceWebhookBody(string providerInstanceId, string stateInstance) =>
        $$"""
        {
          "typeWebhook": "stateInstanceChanged",
          "instanceData": { "idInstance": "{{providerInstanceId}}" },
          "timestamp": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
          "stateInstance": "{{stateInstance}}"
        }
        """;

    private async Task<HttpResponseMessage> PostAuthorizedWebhookAsync(string providerInstanceId) =>
        await AnonymousClient().PostAsync($"/api/notifications/provider-webhook/{WebhookToken}",
            new StringContent(StateInstanceWebhookBody(providerInstanceId, "authorized"), Encoding.UTF8, "application/json"));

    private async Task<BillingAccount> ReloadAccountAsync(Guid accountId) =>
        await DbAsync(db => db.BillingAccounts.AsNoTracking().FirstAsync(a => a.Id == accountId));

    private async Task<AccountSubscription> ReloadSubscriptionAsync(Guid accountId) =>
        await DbAsync(db => db.AccountSubscriptions.AsNoTracking().FirstAsync(s => s.BillingAccountId == accountId));

    // ── Scenario 1 — start via the webhook path (NotificationsController) ──────────────────────────

    [Fact, TestCase("CY18L-01")]
    public async Task Webhook_ChannelBecomesConnected_StartsTrialMailingWindow()
    {
        await CreateTrialPlanAsync(durationDays: 14, mailingWindowDays: 7);
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var instanceId = Unique("instance-webhook-");
        await CreateChannelRowAsync(owner.UserId, accountId, ChannelState.NotConnected, instanceId);

        var webhook = await PostAuthorizedWebhookAsync(instanceId);
        webhook.StatusCode.Should().Be(HttpStatusCode.OK, "§32: the provider webhook always answers 200");

        var account = await ReloadAccountAsync(accountId);
        account.TrialChannelFirstAuthorizedAtUtc.Should().NotBeNull(
            "§336.1: the webhook path is one of the three places that must call the hook on becoming Connected");
        account.TrialMailingWindowEndsAtUtc.Should().NotBeNull();
        account.TrialMailingWindowEndsAtUtc.Should().BeCloseTo(
            account.TrialChannelFirstAuthorizedAtUtc!.Value.AddDays(7), TimeSpan.FromMinutes(2));

        var sub = await ReloadSubscriptionAsync(accountId);
        sub.MailingUntilUtc.Should().Be(account.TrialMailingWindowEndsAtUtc,
            "§336.3: AccountSubscription.MailingUntilUtc must mirror the account's own window end");
    }

    // ── Scenario 2 — start via the QR path (NotificationChannelsController) ────────────────────────

    [Fact, TestCase("CY18L-02")]
    public async Task QrPoll_ChannelBecomesConnected_StartsTrialMailingWindow()
    {
        await CreateTrialPlanAsync(durationDays: 14, mailingWindowDays: 7);
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var instanceId = Unique("instance-qr-");
        var channel = await CreateChannelRowAsync(owner.UserId, accountId, ChannelState.Connecting, instanceId);

        // Fake an authorized QR poll response without exercising the real GREEN-API provisioning stub
        // (already covered elsewhere) — seed the same in-memory cache slot GetQr reads.
        using (var scope = _factory.Services.CreateScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            cache.Set($"channel-qr:{channel.Id}", new QrSnapshot(null, Authorized: true, RefreshAfterSeconds: 3), TimeSpan.FromSeconds(30));
        }

        var qr = await AuthedClient(owner.Token).GetAsync($"/api/notification-channels/{channel.Id}/qr");
        qr.StatusCode.Should().Be(HttpStatusCode.OK, await qr.Content.ReadAsStringAsync());

        var account = await ReloadAccountAsync(accountId);
        account.TrialChannelFirstAuthorizedAtUtc.Should().NotBeNull(
            "§336.1: the QR path is one of the three places that must call the hook on becoming Connected");
        account.TrialMailingWindowEndsAtUtc.Should().NotBeNull();

        var sub = await ReloadSubscriptionAsync(accountId);
        sub.MailingUntilUtc.Should().Be(account.TrialMailingWindowEndsAtUtc);
    }

    // ── Scenario 3 — not restarted by disconnect/replace/re-authorize ──────────────────────────────

    [Fact, TestCase("CY18L-03")]
    public async Task ReAuthorization_AfterDisconnect_DoesNotMoveTheAlreadySetStamp()
    {
        await CreateTrialPlanAsync(durationDays: 14, mailingWindowDays: 7);
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var instanceId = Unique("instance-restart-");
        await CreateChannelRowAsync(owner.UserId, accountId, ChannelState.NotConnected, instanceId);
        (await PostAuthorizedWebhookAsync(instanceId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var firstStamp = (await ReloadAccountAsync(accountId)).TrialChannelFirstAuthorizedAtUtc;
        firstStamp.Should().NotBeNull();

        // Simulate "disconnected, then re-authorized" on the SAME channel — a provider "notAuthorized"
        // state (regression, since it had already been Connected) followed by "authorized" again.
        await DbAsync(async db =>
        {
            await db.NotificationChannels.Where(c => c.ProviderInstanceId == instanceId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.State, ChannelState.Disconnected));
            return true;
        });
        (await PostAuthorizedWebhookAsync(instanceId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var accountAfter = await ReloadAccountAsync(accountId);
        accountAfter.TrialChannelFirstAuthorizedAtUtc.Should().Be(firstStamp,
            "Д5: disconnect/replace/re-authorize must never move an already-set stamp");
    }

    // ── Scenario 4 — window end is capped at the trial's own end ───────────────────────────────────

    [Fact, TestCase("CY18L-04")]
    public async Task ChannelAuthorizedNearTrialEnd_WindowEndIsCappedAtTrialEnd_NotStartPlusM()
    {
        // Admin platform-settings validation (§339.1) enforces mailingWindowDays <= durationDays, so
        // the overshoot this test needs can't come from the SETTING itself — it comes from the channel
        // being authorized LATE in an already-running trial (US-18-08's own example: "канал привязан
        // за 2 дня до конца триала при M=7 → рассылки работают 2 дня"), simulated here by backdating
        // TrialStartedAtUtc so the trial is already 12 of its 14 days in by the time the channel
        // authorizes NOW — naive "now + 7 days" would overshoot TrialEndsAtUtc by 5 days.
        await CreateTrialPlanAsync(durationDays: 14, mailingWindowDays: 7);
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var now = DateTime.UtcNow;
        await RunInDbAsync(async db =>
        {
            var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
            account.TrialStartedAtUtc = now.AddDays(-12);
            account.TrialEndsAtUtc = now.AddDays(2);
        });

        var instanceId = Unique("instance-cap-");
        await CreateChannelRowAsync(owner.UserId, accountId, ChannelState.NotConnected, instanceId);
        (await PostAuthorizedWebhookAsync(instanceId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var account = await ReloadAccountAsync(accountId);
        account.TrialEndsAtUtc.Should().NotBeNull();
        account.TrialMailingWindowEndsAtUtc.Should().BeCloseTo(account.TrialEndsAtUtc!.Value, TimeSpan.FromMinutes(2),
            "§336.2: the ceiling is always the trial's own end, never start+windowDays when that overshoots it");
    }

    // ── Scenario 5 — no channel authorized yet: window not started, not "infinite" ──────────────────

    [Fact, TestCase("CY18L-05")]
    public async Task NoChannelEverAuthorized_WindowNeverStarts()
    {
        await CreateTrialPlanAsync(durationDays: 14, mailingWindowDays: 7);
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var account = await ReloadAccountAsync(accountId);
        account.TrialChannelFirstAuthorizedAtUtc.Should().BeNull(
            "§336.2: firstAuthorizedUtc == null means the window has not started at all");
        account.TrialMailingWindowEndsAtUtc.Should().BeNull(
            "US-18-07/§336.2: 'not started' must not be materialized as some far-future/'unlimited' date");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// Shared helpers for Parts 2 and 3 (trial-lifecycle task + retention rule) — both run against the plain
// CustomWebApplicationFactory (already carries the Trial:PhoneKeyHmac/PhoneKeyId test key), no
// channel/webhook surface needed.
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

public abstract class Cycle18LifecycleTestBase(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    protected async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(db);
    }

    protected async Task RunInDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
        await db.SaveChangesAsync();
    }

    /// <summary>Idempotent across this class' own shared database — same reasoning as
    /// <c>Cycle18TrialPlanTests.CreateTrialPlanAsync</c>.</summary>
    protected async Task<Guid> CreateTrialPlanAsync(int durationDays = 14, int mailingWindowDays = 7)
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
                name = "Trial Plan (QA lifecycle)", description = "Пробный период",
                highlights = new[] { "Максимум возможностей" },
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
                name = Unique("Trial Plan Lifecycle "), description = "Пробный период",
                highlights = new[] { "Максимум возможностей" },
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

        var settings = await client.PutAsJsonAsync("/api/admin/platform-settings",
            new { trialDurationDays = durationDays, trialMailingWindowDays = mailingWindowDays });
        settings.StatusCode.Should().Be(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        return planId;
    }

    protected async Task<(AuthResponseDto Owner, Guid AccountId)> CreateOwnerWithVerifiedPhoneAsync(bool attachPlan = false)
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: attachPlan);
        await MarkPhoneVerifiedAsync(owner.Phone, owner.UserId);
        var accountId = await DbAsync(db => db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync());
        return (owner, accountId);
    }

    protected async Task<HttpResponseMessage> ActivateTrialAsync(string ownerToken) =>
        await AuthedClient(ownerToken).PostAsJsonAsync("/api/billing/trial",
            new { termsVersion = TrialTermsRegistry.CurrentVersion });

    protected async Task<BillingAccount> ReloadAccountAsync(Guid accountId) =>
        await DbAsync(db => db.BillingAccounts.AsNoTracking().FirstAsync(a => a.Id == accountId));

    protected async Task<AccountSubscription> ReloadSubscriptionAsync(Guid accountId) =>
        await DbAsync(db => db.AccountSubscriptions.AsNoTracking().FirstAsync(s => s.BillingAccountId == accountId));

    protected async Task<ScheduledTaskOutcome> RunTrialLifecycleTaskAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "trial-lifecycle");
        return await task.ExecuteAsync(CancellationToken.None);
    }

    // ── Shared WhatsApp-option helpers (Дыра1/Дыра2 recheck) — used by both Cycle18TrialLifecycleTaskTests
    // (option-lifecycle scenarios) and Cycle18TrialMailingDeliveryTests (real-delivery scenarios) below.

    protected static async Task<Guid> GetOrCreateWhatsAppOptionIdAsync(AppDbContext db)
    {
        var existingId = await db.SubscriptionOptions
            .Where(o => o.Code == ServiceBooking.API.Services.SubscriptionResolver.WhatsAppOptionCode)
            .Select(o => o.Id).FirstOrDefaultAsync();
        if (existingId != Guid.Empty) return existingId;

        var option = new SubscriptionOption
        {
            Id = Guid.NewGuid(), Code = ServiceBooking.API.Services.SubscriptionResolver.WhatsAppOptionCode,
            Name = "Рассылки в WhatsApp", Kind = OptionKind.Quantity, UnitName = "номер", IsActive = true,
        };
        db.SubscriptionOptions.Add(option);
        await db.SaveChangesAsync();
        return option.Id;
    }

    /// <summary>Gives a trial plan an `Included` rule for the WhatsApp option — the exact precondition
    /// <c>TrialActivationService.GrantAsync</c>'s §333.3 materialization step requires (`rule is {
    /// Availability: OptionAvailability.Included }`). Not part of <c>CreateTrialPlanAsync</c> itself
    /// (used by every test in this file, including ones that don't care about option materialization at
    /// all) — added explicitly only where this matters, on the same shared plan row every test in a
    /// given class' database already reuses.</summary>
    protected async Task EnsureWhatsAppIncludedOnTrialPlanAsync(Guid trialPlanId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var optionId = await GetOrCreateWhatsAppOptionIdAsync(db);
        var rule = await db.PlanOptionRules.FirstOrDefaultAsync(r => r.PlanConfigId == trialPlanId && r.OptionId == optionId);
        if (rule is null)
        {
            db.PlanOptionRules.Add(new PlanOptionRule
            {
                Id = Guid.NewGuid(), PlanConfigId = trialPlanId, OptionId = optionId,
                Availability = OptionAvailability.Included, IncludedQuantity = 1,
            });
        }
        else
        {
            rule.Availability = OptionAvailability.Included;
            rule.IncludedQuantity = 1;
        }
        await db.SaveChangesAsync();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// Part 2 — §337 trial-lifecycle background task, scenarios 6-11.
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

public class Cycle18TrialLifecycleTaskTests(TestDatabaseFixture fixture) : Cycle18LifecycleTestBase(fixture)
{
    // ── Scenario 6 — "догон пропуска": one pass catches up BOTH an overdue window AND an expired trial ──

    [Fact, TestCase("CY18L-06")]
    public async Task OnePass_CatchesUpMissedWindowStart_WindowClose_AndExpiry_Together()
    {
        await CreateTrialPlanAsync(durationDays: 14, mailingWindowDays: 7);
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var now = DateTime.UtcNow;
        var missedAuthorization = now.AddDays(-19); // hook "lost" — never called StartIfDueAsync
        var trialEnd = now.AddDays(-6); // trial's own end date already passed

        await RunInDbAsync(async db =>
        {
            var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
            account.TrialStartedAtUtc = now.AddDays(-20);
            account.TrialEndsAtUtc = trialEnd;

            db.NotificationChannels.Add(new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = owner.UserId, BillingAccountId = accountId,
                Transport = NotificationTransport.WhatsApp, State = ChannelState.Connected,
                ProviderInstanceId = Unique("instance-catchup-"),
                ConnectedAtUtc = missedAuthorization,
            });

            var sub = await db.AccountSubscriptions.FirstAsync(s => s.BillingAccountId == accountId);
            sub.PaidUntil = trialEnd;
        });

        var outcome = await RunTrialLifecycleTaskAsync();
        outcome.Error.Should().BeNull();

        var account = await ReloadAccountAsync(accountId);
        account.TrialChannelFirstAuthorizedAtUtc.Should().BeCloseTo(missedAuthorization, TimeSpan.FromSeconds(2),
            "US-18-13: phase 1 self-heal must date the window from the REAL missed authorization, not from 'now'");
        account.TrialMailingWindowEndsAtUtc.Should().NotBeNull();
        account.TrialMailingWindowEndsAtUtc!.Value.Should().BeBefore(now,
            "the self-healed window (missedAuthorization + 7d) must already be in the past too, so phase 2 closes it in the SAME pass");
        account.TrialMailingClosureLoggedAtUtc.Should().NotBeNull(
            "US-18-13: one pass must catch up phase 2 (window close) as well as phase 1, not just the oldest overdue phase");
        account.TrialExpiredHandledAtUtc.Should().NotBeNull(
            "US-18-13: the SAME pass must also catch up phase 4 (expiry) — idempotency is by state, not by calendar");

        var sub = await ReloadSubscriptionAsync(accountId);
        sub.MailingUntilUtc.Should().BeNull("§337.3: expiry clears MailingUntilUtc");
        var freePlanId = await DbAsync(db => db.SubscriptionPlanConfigs.Where(p => p.IsSystemFree).Select(p => p.Id).FirstAsync());
        sub.PlanConfigId.Should().Be(freePlanId);
        sub.PaidUntil.Should().BeNull();
    }

    // ── Scenario 7 — fail-closed when no plan is flagged IsSystemFree ──────────────────────────────

    [Fact, TestCase("CY18L-07")]
    public async Task NoSystemFreePlanConfigured_ExpiryFailsClosed_AccountLeftUntouched()
    {
        await CreateTrialPlanAsync();
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await RunInDbAsync(async db =>
        {
            var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
            account.TrialEndsAtUtc = DateTime.UtcNow.AddDays(-1);
            var sub = await db.AccountSubscriptions.FirstAsync(s => s.BillingAccountId == accountId);
            sub.PaidUntil = DateTime.UtcNow.AddDays(-1);
        });

        var freePlanId = await DbAsync(db => db.SubscriptionPlanConfigs.Where(p => p.IsSystemFree).Select(p => p.Id).FirstAsync());
        try
        {
            await RunInDbAsync(async db =>
            {
                var freePlan = await db.SubscriptionPlanConfigs.FirstAsync(p => p.Id == freePlanId);
                freePlan.IsSystemFree = false; // temporarily un-flag: no plan is IsSystemFree now
            });

            var outcome = await RunTrialLifecycleTaskAsync();
            outcome.Error.Should().NotBeNullOrEmpty(
                "R5/US-18-11: fail-closed — no IsSystemFree plan means the task must surface an error, never silently do nothing");

            var account = await ReloadAccountAsync(accountId);
            account.TrialExpiredHandledAtUtc.Should().BeNull(
                "fail-closed: the account must be left completely untouched, not marked handled with nothing done");

            var sub = await ReloadSubscriptionAsync(accountId);
            sub.PlanConfigId.Should().NotBe(freePlanId, "no 'some other free-ish plan' invented to keep going");
        }
        finally
        {
            // Restore for every other [Fact] in this class sharing the same database.
            await RunInDbAsync(async db =>
            {
                var freePlan = await db.SubscriptionPlanConfigs.FirstAsync(p => p.Id == freePlanId);
                freePlan.IsSystemFree = true;
            });
        }
    }

    // ── Scenario 8 — warning thresholds read from the account's OWN snapshot, not the live setting ──

    [Fact, TestCase("CY18L-08")]
    public async Task Warnings_ReadThresholdsFromAccountSnapshot_NotLivePlatformSetting()
    {
        await CreateTrialPlanAsync();
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The live platform setting is validated to always match the current terms edition's promised
        // thresholds ("7,3,1", §338.4 п.6) — PUT /api/admin/platform-settings 400s on anything else, so
        // there is no way to exercise "admin changed the setting" through the HTTP surface itself. What
        // IS testable, and is the actual code-level guarantee Д19 asks for, is that the task reads
        // whatever is stamped on THIS account, not a hardcoded/live default — simulated here as an
        // account that was granted under thresholds different from the current default, exactly as if a
        // past terms edition had promised different numbers.
        const string snapshotThresholds = "10,5,2";
        var now = DateTime.UtcNow;
        await RunInDbAsync(async db =>
        {
            var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
            account.TrialWarningThresholdsDays = snapshotThresholds;
            account.TrialEndsAtUtc = now.AddDays(10); // daysLeft rounds up to 10 -> matches the "10" threshold
            account.TrialWarnedAtThresholdDays = null;
        });

        var outcome = await RunTrialLifecycleTaskAsync();
        outcome.Error.Should().BeNull();

        var account = await ReloadAccountAsync(accountId);
        account.TrialWarnedAtThresholdDays.Should().Be(10,
            "Д19: the applicable threshold must come from THIS account's own snapshot (10,5,2), not the " +
            "live platform default (7,3,1) — 10 could only ever be produced by reading the snapshot");
    }

    // ── Scenario 9 — §337.3 legal invariant: nothing irreversible happens on expiry ─────────────────

    [Fact, TestCase("CY18L-09")]
    public async Task Expiry_LeavesEveryCompanyAssetIntact_AndPublicPageReachable()
    {
        await CreateTrialPlanAsync();
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await MarkPhoneVerifiedAsync(owner.Phone, owner.UserId);
        var accountId = await DbAsync(db => db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync());
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var booking = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, "Walk-in", "+79990002222", null, null));
        booking.StatusCode.Should().Be(HttpStatusCode.Created, await booking.Content.ReadAsStringAsync());
        var bookingId = (await booking.Content.ReadJsonAsync<ServiceBooking.API.DTOs.Bookings.BookingDto>())!.Id;

        var photoId = Guid.NewGuid();
        await RunInDbAsync(async db =>
        {
            db.CompanyPhotos.Add(new CompanyPhoto
            {
                Id = photoId, CompanyId = company.Id, Url = "/uploads/companies/qa/photo.jpg",
                ThumbnailUrl = "/uploads/companies/qa/photo-thumb.jpg", ContentHash = Unique("hash-"),
            });
            var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
            account.TrialEndsAtUtc = DateTime.UtcNow.AddDays(-1);
            var sub = await db.AccountSubscriptions.FirstAsync(s => s.BillingAccountId == accountId);
            sub.PaidUntil = DateTime.UtcNow.AddDays(-1);
        });

        var outcome = await RunTrialLifecycleTaskAsync();
        outcome.Error.Should().BeNull();

        var reloadedAccount = await ReloadAccountAsync(accountId);
        reloadedAccount.TrialExpiredHandledAtUtc.Should().NotBeNull("the transition must actually have happened for this test to prove anything");

        var freePlanId = await DbAsync(db => db.SubscriptionPlanConfigs.Where(p => p.IsSystemFree).Select(p => p.Id).FirstAsync());
        var sub = await ReloadSubscriptionAsync(accountId);
        sub.PlanConfigId.Should().Be(freePlanId, "the ONLY thing §337.3 permits changing on the subscription is the plan/dates");
        sub.PaidUntil.Should().BeNull();
        sub.MailingUntilUtc.Should().BeNull();

        // §337.3's closed list, checked one by one.
        (await DbAsync(db => db.Companies.AsNoTracking().FirstAsync(c => c.Id == company.Id))).IsActive.Should().BeTrue(
            "§337.3: the company must not be deactivated");
        (await DbAsync(db => db.Companies.AsNoTracking().FirstAsync(c => c.Id == company.Id))).ShowInPublicListing.Should().BeTrue(
            "§337.3: the company must not be hidden from the public catalog");
        (await DbAsync(db => db.CompanyMembers.CountAsync(m => m.CompanyId == company.Id && m.UserId == master.UserId))).Should().Be(1,
            "§337.3: employees must not be removed for being over the new (Free) seat limit");
        (await DbAsync(db => db.Services.AnyAsync(s => s.Id == service.Id && s.IsActive))).Should().BeTrue(
            "§337.3: services must not be touched");
        (await DbAsync(db => db.WorkingHours.AnyAsync(w => w.MasterId == master.UserId && w.Date == date))).Should().BeTrue(
            "§337.3: schedule must not be touched");
        var bookingAfter = await DbAsync(db => db.Bookings.AsNoTracking().FirstAsync(b => b.Id == bookingId));
        bookingAfter.Status.Should().NotBe(BookingStatus.Cancelled, "§337.3: existing client bookings must not be cancelled");
        (await DbAsync(db => db.CompanyPhotos.AnyAsync(p => p.Id == photoId))).Should().BeTrue(
            "§337.3: photos must not be deleted");

        var publicPage = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        publicPage.StatusCode.Should().Be(HttpStatusCode.OK, "§337.3: the public company page must stay reachable after expiry");
    }

    // ── Scenario 10 — an account moved to a paid plan BEFORE the trial's own end is untouched ───────

    [Fact, TestCase("CY18L-10")]
    public async Task AccountMovedToPaidPlanEarly_ExpiryPhaseLeavesItAlone()
    {
        await CreateTrialPlanAsync();
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var paidPlanId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);
        var paidUntil = DateTime.UtcNow.AddDays(30);
        await RunInDbAsync(async db =>
        {
            // Trial* history columns are never cleared by an early upgrade (§332.3) — only the
            // subscription itself moves.
            var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
            account.TrialEndsAtUtc = DateTime.UtcNow.AddDays(-1); // trial's OWN end date has passed too

            var sub = await db.AccountSubscriptions.FirstAsync(s => s.BillingAccountId == accountId);
            sub.PlanConfigId = paidPlanId;
            sub.PaidUntil = paidUntil;
            sub.MailingUntilUtc = null;
        });

        var outcome = await RunTrialLifecycleTaskAsync();
        outcome.Error.Should().BeNull();

        var account = await ReloadAccountAsync(accountId);
        account.TrialExpiredHandledAtUtc.Should().NotBeNull(
            "the account must stop being re-selected by IX_BillingAccounts_TrialExpiry every pass forever");

        var sub = await ReloadSubscriptionAsync(accountId);
        sub.PlanConfigId.Should().Be(paidPlanId, "an admin/owner's own plan decision must never be overwritten by this task");
        sub.PaidUntil.Should().BeCloseTo(paidUntil, TimeSpan.FromSeconds(5));
    }

    // ── Scenario 11 — idempotency: a second pass over the same already-handled state is a no-op ─────

    [Fact, TestCase("CY18L-11")]
    public async Task SecondPass_OverAlreadyHandledState_WritesNoSecondLogRow_AndWarnsNoAgain()
    {
        await CreateTrialPlanAsync();
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var now = DateTime.UtcNow;
        await RunInDbAsync(async db =>
        {
            var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
            account.TrialStartedAtUtc = now.AddDays(-20);
            account.TrialEndsAtUtc = now.AddDays(-1);

            db.NotificationChannels.Add(new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = owner.UserId, BillingAccountId = accountId,
                Transport = NotificationTransport.WhatsApp, State = ChannelState.Connected,
                ProviderInstanceId = Unique("instance-idempotent-"),
                ConnectedAtUtc = now.AddDays(-19),
            });

            var sub = await db.AccountSubscriptions.FirstAsync(s => s.BillingAccountId == accountId);
            sub.PaidUntil = now.AddDays(-1);
        });

        var first = await RunTrialLifecycleTaskAsync();
        first.Error.Should().BeNull();
        var second = await RunTrialLifecycleTaskAsync();
        second.Error.Should().BeNull();

        var expiredLogRows = await DbAsync(db => db.SubscriptionChangeLogs
            .CountAsync(l => l.BillingAccountId == accountId && l.ChangeKind == SubscriptionChangeKind.TrialExpired));
        expiredLogRows.Should().Be(1, "idempotency by state: a second pass over an already-handled account must not double the journal");

        var mailingWindowLogRows = await DbAsync(db => db.SubscriptionChangeLogs
            .CountAsync(l => l.BillingAccountId == accountId && l.ChangeKind == SubscriptionChangeKind.TrialMailingWindow));
        mailingWindowLogRows.Should().Be(2, "exactly one 'opened' row and one 'closed' row — never doubled by the second pass");

        var account = await ReloadAccountAsync(accountId);
        account.TrialWarnedAtThresholdDays.Should().BeNull(
            "an already-expired trial (TrialExpiredHandledAtUtc set) must never additionally collect a warning");
    }

    // ── Scenario 15 — §337.3 extended: expiry dates out ONLY the trial-granted option row ───────────
    //
    // Review finding (QA "Вызов 2" recheck): CY18L-09's own §337.3 checklist enumerates company,
    // employee, service, schedule, booking and photo — it never checks AccountSubscriptionOptions at
    // all. The real bug this missed: before B1 (code review, cycle 18 late delta,
    // TrialLifecycleTask.ExpireTrialsAsync's `GrantedByTrial` filter), the expiry phase dated out EVERY
    // open AccountSubscriptionOption row for the account, including one an admin had assigned and PAID
    // for (AdminBillingController.AssignSubscription, EndsAtUtc == null) before the account ever went on
    // trial — an irreversible side effect §337.3 forbids outright. This test seeds exactly that
    // collision: a pre-existing, non-trial-granted paid option row for the SAME option code the trial
    // itself would also materialize.

    [Fact, TestCase("CY18L-15")]
    public async Task TrialActivation_DoesNotOverwriteAPreExistingNonTrialOptionRow()
    {
        var trialPlanId = await CreateTrialPlanAsync();
        await EnsureWhatsAppIncludedOnTrialPlanAsync(trialPlanId);
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();

        var optionId = await DbAsync(db => GetOrCreateWhatsAppOptionIdAsync(db));
        await RunInDbAsync(async db =>
        {
            db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
            {
                Id = Guid.NewGuid(), BillingAccountId = accountId, OptionId = optionId, Quantity = 3,
                PaidUntilUtc = null, EndsAtUtc = null, GrantedByTrial = false,
                ActivatedAtUtc = DateTime.UtcNow.AddDays(-30),
            });
            await Task.CompletedTask;
        });

        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await DbAsync(db => db.AccountSubscriptionOptions
            .Where(o => o.BillingAccountId == accountId && o.OptionId == optionId).ToListAsync());
        rows.Should().ContainSingle(
            "B1 (code review): the (BillingAccountId, OptionId) unique index means the trial can never " +
            "hold a SECOND row for the same option next to the admin's — it must reuse or leave the existing one");
        var row = rows.Single();
        row.GrantedByTrial.Should().BeFalse("the trial must never claim an admin-granted row as its own");
        row.Quantity.Should().Be(3, "the admin's own paid quantity must survive activation untouched");
        row.EndsAtUtc.Should().BeNull();
        row.PaidUntilUtc.Should().BeNull("only a row this service itself materialized ever gets its PaidUntilUtc touched");
    }

    [Fact, TestCase("CY18L-16")]
    public async Task Expiry_DatesOutOnlyTheTrialGrantedOptionRow_LeavesPreExistingPaidRowUntouched()
    {
        var trialPlanId = await CreateTrialPlanAsync();
        await EnsureWhatsAppIncludedOnTrialPlanAsync(trialPlanId);
        var (owner, accountId) = await CreateOwnerWithVerifiedPhoneAsync();

        var optionId = await DbAsync(db => GetOrCreateWhatsAppOptionIdAsync(db));
        var preExistingId = Guid.NewGuid();
        await RunInDbAsync(async db =>
        {
            db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
            {
                Id = preExistingId, BillingAccountId = accountId, OptionId = optionId, Quantity = 2,
                PaidUntilUtc = null, EndsAtUtc = null, GrantedByTrial = false,
                ActivatedAtUtc = DateTime.UtcNow.AddDays(-60),
            });
            await Task.CompletedTask;
        });

        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The trial itself must NOT have materialized a second row (see CY18L-15) — the only row on the
        // account for this option is still the pre-existing, non-trial one.
        var trialGrantedRowId = await DbAsync(db => db.AccountSubscriptionOptions
            .Where(o => o.BillingAccountId == accountId && o.OptionId == optionId && o.GrantedByTrial)
            .Select(o => (Guid?)o.Id).FirstOrDefaultAsync());
        trialGrantedRowId.Should().BeNull("B1: the trial never claims an existing admin-granted row as its own");

        await RunInDbAsync(async db =>
        {
            var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
            account.TrialEndsAtUtc = DateTime.UtcNow.AddDays(-1);
            var sub = await db.AccountSubscriptions.FirstAsync(s => s.BillingAccountId == accountId);
            sub.PaidUntil = DateTime.UtcNow.AddDays(-1);
        });

        var outcome = await RunTrialLifecycleTaskAsync();
        outcome.Error.Should().BeNull();

        var preExisting = await DbAsync(db => db.AccountSubscriptionOptions.AsNoTracking().FirstAsync(o => o.Id == preExistingId));
        preExisting.EndsAtUtc.Should().BeNull(
            "§337.3 extended: a paid option row the trial never granted must survive the trial→Free " +
            "transition untouched — B1's GrantedByTrial filter is exactly what protects it");
        preExisting.Quantity.Should().Be(2);
    }

    // ── Scenario 17 — keyset cursors process a backlog LARGER than BatchSize=100 in ONE pass ─────────
    //
    // Review finding: none of CY18L-06..16 ever exceeds BatchSize, so the keyset-cursor plumbing in the
    // self-heal-window-start and warn phases (the two phases of the four that actually need one — see
    // each phase's own doc comment for why the other two don't) was only ever checked by reading the
    // code. A backlog of 101+ rows that a buggy/missing cursor would either infinite-loop on or silently
    // truncate at the first page is the only way to prove it end-to-end.

    private async Task<Guid> CreateBareBillingAccountAsync(Guid trialPlanId, DateTime trialStartedAtUtc, DateTime trialEndsAtUtc, int windowDays)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var phone = UniquePhone();
        var user = new AppUser { FirstName = "Batch", LastName = "Owner", UserName = phone, PhoneNumber = phone };
        var created = await userManager.CreateAsync(user, "Password123!");
        created.Succeeded.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Description)));

        var account = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = user.Id,
            TrialStartedAtUtc = trialStartedAtUtc, TrialEndsAtUtc = trialEndsAtUtc, TrialDurationDays = (int)(trialEndsAtUtc - trialStartedAtUtc).TotalDays,
            TrialMailingWindowDays = windowDays, TrialWarningThresholdsDays = "7,3,1" };
        db.BillingAccounts.Add(account);
        db.AccountSubscriptions.Add(new AccountSubscription
        {
            Id = Guid.NewGuid(), OwnerUserId = user.Id, BillingAccountId = account.Id, PlanConfigId = trialPlanId,
            IsActive = true, PaidUntil = trialEndsAtUtc, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return account.Id;
    }

    [Fact, TestCase("CY18L-17")]
    public async Task SelfHealPhase_ProcessesAOneHundredAndFiveAccountBacklog_InOnePass()
    {
        var trialPlanId = await CreateTrialPlanAsync();
        const int count = 105; // > BatchSize (100)
        var now = DateTime.UtcNow;
        var accountIds = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var accountId = await CreateBareBillingAccountAsync(trialPlanId, now.AddDays(-1), now.AddDays(13), 7);
            await RunInDbAsync(async db =>
            {
                db.NotificationChannels.Add(new NotificationChannel
                {
                    Id = Guid.NewGuid(), OwnerUserId = await db.BillingAccounts.Where(a => a.Id == accountId).Select(a => a.OwnerUserId).FirstAsync(),
                    BillingAccountId = accountId, Transport = NotificationTransport.WhatsApp, State = ChannelState.Connected,
                    ProviderInstanceId = Unique("instance-batch-"), ConnectedAtUtc = now.AddMinutes(-1),
                });
            });
            accountIds.Add(accountId);
        }

        var outcome = await RunTrialLifecycleTaskAsync();
        outcome.Error.Should().BeNull();

        var stillUnhealed = await DbAsync(db => db.BillingAccounts
            .CountAsync(a => accountIds.Contains(a.Id) && a.TrialChannelFirstAuthorizedAtUtc == null));
        stillUnhealed.Should().Be(0,
            "a missing/broken keyset cursor would either infinite-loop this call or silently stop after the " +
            "first 100 — every one of the 105 seeded accounts must be healed by the END of a SINGLE pass");
    }

    [Fact, TestCase("CY18L-18")]
    public async Task WarnPhase_ProcessesAOneHundredAndFiveAccountBacklog_InOnePass()
    {
        var trialPlanId = await CreateTrialPlanAsync();
        const int count = 105; // > BatchSize (100)
        var now = DateTime.UtcNow;
        var accountIds = new List<Guid>();
        for (var i = 0; i < count; i++)
            accountIds.Add(await CreateBareBillingAccountAsync(trialPlanId, now.AddDays(-7), now.AddDays(7), 7));

        var outcome = await RunTrialLifecycleTaskAsync();
        outcome.Error.Should().BeNull();

        var stillUnwarned = await DbAsync(db => db.BillingAccounts
            .CountAsync(a => accountIds.Contains(a.Id) && a.TrialWarnedAtThresholdDays == null));
        stillUnwarned.Should().Be(0,
            "same keyset-cursor risk as the self-heal phase (CY18L-17) — a missing `a.Id > cursor` clause " +
            "would re-select the SAME first page of not-yet-warned accounts forever, or a batched save would " +
            "silently drop everything past the first 100");
    }

    // ── Scenario 19 — journal atomicity: a genuinely failed iteration leaves no false journal row ────
    //
    // Coordinator-supplied recipe (commit 7bb1158's own author): SubscriptionChangeLog carries no FK on
    // OwnerUserId (confirmed against AppDbContextModelSnapshot — only BillingAccountId/CompanyId are real
    // FKs there), so a "bad OwnerUserId" can never actually reach the database in the first place — any
    // value that would violate a real constraint is, by construction, impossible to have gotten INTO the
    // database to begin with. The genuine, constraint-agnostic way to make exactly ONE iteration's
    // per-account SaveChangesAsync fail without touching the other accounts in the same batch: delete
    // that ONE account's AccountSubscription row out from under EF via a SEPARATE connection/DbContext,
    // timed via the real <see cref="Microsoft.EntityFrameworkCore.DbContext.SavingChanges"/> event so it
    // happens exactly between the row being loaded (top of ExpireTrialsAsync's batch) and that account's
    // own SaveChangesAsync call — modeling a real concurrent actor (an admin/owner action, a cascade from
    // elsewhere) deleting the subscription out from under the task. EF Core always checks the affected-row
    // count on UPDATE regardless of whether a concurrency token is configured — zero rows affected because
    // the row is gone throws a genuine <see cref="DbUpdateConcurrencyException"/>, not a mocked one.
    [Fact, TestCase("CY18L-19")]
    public async Task FailedIteration_InExpirePhase_LeavesNoFalseJournalRow_AndDoesNotBlockTheRestOfTheBatch()
    {
        await CreateTrialPlanAsync();
        var now = DateTime.UtcNow;

        var (ownerA, accountA) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(ownerA.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (ownerB, accountB) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(ownerB.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (ownerC, accountC) = await CreateOwnerWithVerifiedPhoneAsync();
        (await ActivateTrialAsync(ownerC.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var accountId in new[] { accountA, accountB, accountC })
        {
            await RunInDbAsync(async db =>
            {
                var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
                account.TrialEndsAtUtc = now.AddDays(-1);
                var sub = await db.AccountSubscriptions.FirstAsync(s => s.BillingAccountId == accountId);
                sub.PaidUntil = now.AddDays(-1);
            });
        }

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var poisoned = false;
        db.SavingChanges += (_, _) =>
        {
            if (poisoned) return;
            var isAccountBExpiring = db.ChangeTracker.Entries<SubscriptionChangeLog>().Any(e =>
                e.State == EntityState.Added && e.Entity.BillingAccountId == accountB &&
                e.Entity.ChangeKind == SubscriptionChangeKind.TrialExpired);
            if (!isAccountBExpiring) return;
            poisoned = true;
            // A genuinely concurrent actor, via its OWN connection/DbContext — deletes account B's
            // subscription row an instant before this SAME row's UPDATE (already queued in `db`'s change
            // tracker above) is sent to the database.
            using var raceScope = Factory.Services.CreateScope();
            var raceDb = raceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            raceDb.Database.ExecuteSqlRaw("DELETE FROM \"AccountSubscriptions\" WHERE \"BillingAccountId\" = {0}", accountB);
        };

        var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "trial-lifecycle");
        var outcome = await task.ExecuteAsync(CancellationToken.None);

        poisoned.Should().BeTrue("the race must actually have fired for this test to prove anything");
        outcome.Error.Should().BeNull("a single poisoned account is isolated per-account (N4) — it must never surface as a phase-level Error");

        // Account B: no false evidence, and never falsely marked "done".
        var accountBAfter = await DbAsync(d => d.BillingAccounts.AsNoTracking().FirstAsync(a => a.Id == accountB));
        accountBAfter.TrialExpiredHandledAtUtc.Should().BeNull(
            "the failed iteration's own DiscardFailedIterationChanges must have detached this, and the failed " +
            "per-account SaveChangesAsync must never have committed it");
        var accountBLogRows = await DbAsync(d => d.SubscriptionChangeLogs
            .CountAsync(l => l.BillingAccountId == accountB && l.ChangeKind == SubscriptionChangeKind.TrialExpired));
        accountBLogRows.Should().Be(0,
            "Д18/Т1: the append-only journal must NEVER assert a transition that never actually committed — " +
            "a false row here would be worse than the missed account itself");

        // Accounts A and C: unaffected by B's poisoned iteration, transitioned normally in the SAME pass.
        var freePlanId = await DbAsync(d => d.SubscriptionPlanConfigs.Where(p => p.IsSystemFree).Select(p => p.Id).FirstAsync());
        foreach (var accountId in new[] { accountA, accountC })
        {
            var account = await DbAsync(d => d.BillingAccounts.AsNoTracking().FirstAsync(a => a.Id == accountId));
            account.TrialExpiredHandledAtUtc.Should().NotBeNull($"account {accountId} was never poisoned and must transition normally");
            var sub = await DbAsync(d => d.AccountSubscriptions.AsNoTracking().FirstAsync(s => s.BillingAccountId == accountId));
            sub.PlanConfigId.Should().Be(freePlanId);
            var logRows = await DbAsync(d => d.SubscriptionChangeLogs
                .CountAsync(l => l.BillingAccountId == accountId && l.ChangeKind == SubscriptionChangeKind.TrialExpired));
            logRows.Should().Be(1, $"account {accountId}'s own journal entry must be written exactly once, undisturbed by B's failure");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// Part 3 — §343 TrialPhoneRegistration retention rule, scenarios 12-14.
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

public class Cycle18TrialPhoneRetentionTests(TestDatabaseFixture fixture) : Cycle18LifecycleTestBase(fixture)
{
    private const string CurrentKeyId = "qa-test-key"; // matches CustomWebApplicationFactory's Trial:PhoneKeyId

    private async Task<Guid> InsertRegistrationAsync(DateTime registeredAtUtc, string keyId)
    {
        var id = Guid.NewGuid();
        await RunInDbAsync(async db =>
        {
            db.TrialPhoneRegistrations.Add(new TrialPhoneRegistration
            {
                Id = id, PhoneKeyHash = Guid.NewGuid().ToString("N"), RegisteredAtUtc = registeredAtUtc, KeyId = keyId,
            });
            await Task.CompletedTask;
        });
        return id;
    }

    private async Task<RetentionOutcome> RunRuleAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? factoryOverride = null)
    {
        using var scope = (factoryOverride ?? Factory).Services.CreateScope();
        var rule = scope.ServiceProvider.GetServices<IRetentionRule>().Single(r => r.Name == "trial-phone-registration");
        var periods = scope.ServiceProvider.GetRequiredService<IOptions<RetentionPeriods>>().Value;
        var ctx = new RetentionContext(DateTime.UtcNow, periods, BatchSize: 500, DryRun: false);
        return await rule.ApplyAsync(ctx, CancellationToken.None);
    }

    private async Task<bool> RowExistsAsync(Guid id) => await DbAsync(db => db.TrialPhoneRegistrations.AnyAsync(r => r.Id == id));

    // ── Scenario 12 — destroyed by age (from RegisteredAtUtc, i.e. the date of GRANT — Д16) ─────────

    [Fact, TestCase("CY18L-12")]
    public async Task Rule_DestroysRowsOlderThanRetentionPeriod_KeepsFresherOnes()
    {
        var now = DateTime.UtcNow;
        var oldId = await InsertRegistrationAsync(now.AddDays(-1096), CurrentKeyId); // past the 1095-day default
        var freshId = await InsertRegistrationAsync(now.AddDays(-10), CurrentKeyId);

        await RunRuleAsync();

        (await RowExistsAsync(oldId)).Should().BeFalse("Д16: a row older than the 3-year cutoff FROM THE DATE OF GRANT must be destroyed");
        (await RowExistsAsync(freshId)).Should().BeTrue("a fresh row must survive untouched");
    }

    // ── Scenario 13 — destroyed by key rotation (К3), regardless of age ─────────────────────────────

    [Fact, TestCase("CY18L-13")]
    public async Task Rule_DestroysRowsUnderARotatedAwayKey_EvenWhenFresh()
    {
        var now = DateTime.UtcNow;
        var rotatedAwayId = await InsertRegistrationAsync(now.AddDays(-1), "old-rotated-key");
        var currentKeyId = await InsertRegistrationAsync(now.AddDays(-1), CurrentKeyId);

        await RunRuleAsync();

        (await RowExistsAsync(rotatedAwayId)).Should().BeFalse(
            "К3: a row computed under a key that is no longer the platform's current key can never be compared against anything again");
        (await RowExistsAsync(currentKeyId)).Should().BeTrue("a fresh row under the CURRENT key must survive");
    }

    // ── Scenario 14 — no current key configured at all must NOT read as "every row is stale" ────────

    [Fact, TestCase("CY18L-14")]
    public async Task Rule_NoCurrentKeyConfigured_DoesNotMassDeleteTheRegistry()
    {
        var now = DateTime.UtcNow;
        var oldId = await InsertRegistrationAsync(now.AddDays(-1096), "some-key");
        var freshUnderAnyKeyId = await InsertRegistrationAsync(now.AddDays(-10), "some-other-key");

        // appsettings.json ships a non-empty default ("k1") for Trial:PhoneKeyId, so
        // UseSetting(key, null) merely falls back to it instead of producing a real null — PostConfigure
        // is what actually forces TrialOptions.PhoneKeyId to null regardless of configuration precedence,
        // simulating "this environment never set Trial:PhoneKeyId at all".
        await using var noKeyFactory = Factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services => services.PostConfigure<TrialOptions>(o => o.PhoneKeyId = null)));

        await RunRuleAsync(noKeyFactory);

        (await RowExistsAsync(oldId)).Should().BeFalse("the AGE criterion is independent of the key and must still apply");
        (await RowExistsAsync(freshUnderAnyKeyId)).Should().BeTrue(
            "protection from catastrophe: 'no current key configured' must fall back to age-only, never treat every " +
            "row's KeyId as stale and wipe the whole uniqueness registry outright");
    }

    // ── Scenario 20 — the EMPTY-STRING variant of CY18L-14 ────────────────────────────────────────────
    //
    // Review finding: N1's own fix (commit 1739932, `TrialPhoneRegistrationRule.cs`) switched the
    // "current key configured" check from `!= null` to `string.IsNullOrWhiteSpace` specifically because
    // `Trial:PhoneKeyId = ""` (appsettings.json's own shipped default before an environment sets a real
    // one) is a real, reachable configuration state distinct from an outright missing key — CY18L-14
    // only ever exercises the null case (forced via PostConfigure, since UseSetting(key, null) merely
    // falls back to the JSON default). The developer was explicitly asked not to add this case to this
    // file himself (coordinator instruction) — it is QA's own to add.
    [Fact, TestCase("CY18L-20")]
    public async Task Rule_EmptyStringKeyConfigured_DoesNotMassDeleteTheRegistry()
    {
        var now = DateTime.UtcNow;
        var oldId = await InsertRegistrationAsync(now.AddDays(-1096), "some-key");
        var freshUnderAnyKeyId = await InsertRegistrationAsync(now.AddDays(-10), "some-other-key");

        await using var emptyKeyFactory = Factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services => services.PostConfigure<TrialOptions>(o => o.PhoneKeyId = "")));

        await RunRuleAsync(emptyKeyFactory);

        (await RowExistsAsync(oldId)).Should().BeFalse("the AGE criterion is independent of the key and must still apply");
        (await RowExistsAsync(freshUnderAnyKeyId)).Should().BeTrue(
            "N1: an EMPTY string (appsettings.json's own shipped default) must fall back to age-only exactly " +
            "like an outright null — a bare `!= null` check would read \"\" as \"configured\" and treat every " +
            "row's KeyId as not matching it, mass-deleting the whole uniqueness registry");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════════════════════════
// Part 4 — Дыра1 (QA "Вызов 2" recheck, review-flagged BLOCKER): §333.3's option-row materialization
// must actually reach a real send, not just create a row. Reuses this class' own Factory/ConnectionString
// (via Cycle18LifecycleTestBase) for the trial setup, then spins up a SECOND, dedicated host bound to
// the SAME database purely to tick the REAL NotificationDispatchTask and observe an actual send — the
// same "second, dedicated host against the same database" pattern ApiTestBase's own doc comment names
// (RateLimitTestFactory), applied here for the identical reason NotificationDispatchTests.cs itself
// exists: proving delivery requires the real background runner actually ticking.
// ═══════════════════════════════════════════════════════════════════════════════════════════════════

public class Cycle18TrialMailingDeliveryTests(TestDatabaseFixture fixture) : Cycle18LifecycleTestBase(fixture)
{
    /// <summary>A dedicated host bound to THIS class' own database (via <see cref="ApiTestBase"/>'s
    /// protected <c>ConnectionString</c>) solely to tick the real <c>NotificationDispatchTask</c> — the
    /// shared <c>Factory</c> this class inherits never ticks any scheduled task
    /// (ARCHITECTURE_CYCLE4.md §27.1's own convention, same reasoning as
    /// <c>NotificationDispatchTestFactory</c>/<c>RateLimitTestFactory</c>). Reuses the "dispatch"
    /// factoryTag (safe: it only selects a SuperAdmin phone/email pair and a temp-directory slot derived
    /// from THIS class' own connection string/database name — never shared with an actual
    /// <c>NotificationDispatchTestFactory</c> instance, which always lives in a different test class
    /// with a different database).</summary>
    private sealed class TrialDispatchFactory(string connectionString) : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        public RecordingTransport Transport { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestHostSettings.Apply(builder, "dispatch", connectionString);

            // Same test key CustomWebApplicationFactory/Cycle18LifecycleTestBase's own Factory uses —
            // Trial activation itself already happened through THAT factory; this host only needs to
            // boot without fail-closing on the trial subsystem's own startup checks.
            builder.UseSetting("Trial:PhoneKeyHmac", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
            builder.UseSetting("Trial:PhoneKeyId", "qa-test-key");

            builder.UseSetting("Notifications:Provider", "logging");
            builder.UseSetting("Notifications:EncryptionKey", NotificationDispatchTestFactory.TestEncryptionKeyBase64);

            // §27.1 — the runner ticks only under THIS host, fast enough for a real-clock test to observe
            // a pass within seconds (NotificationDispatchTestFactory's own convention).
            builder.UseSetting("ScheduledTasks:Enabled", "true");
            builder.UseSetting("ScheduledTasks:TickSeconds", "1");
            builder.UseSetting("ScheduledTasks:photo-retention-cleanup:Enabled", "false");
            builder.UseSetting("ScheduledTasks:notification-dispatch:Enabled", "true");
            builder.UseSetting("ScheduledTasks:notification-dispatch:PeriodSeconds", "1");
            builder.UseSetting("ScheduledTasks:notification-dispatch:MaxRunMinutes", "1");
            builder.UseSetting("ScheduledTasks:channel-health:Enabled", "false");

            builder.ConfigureServices(services => services.AddSingleton<INotificationTransport>(Transport));
        }
    }

    /// <summary>Writes a Connected, funded-by-trial channel directly (provisioning itself is
    /// <c>NotificationChannelsTests.cs</c>'s concern, same reasoning <c>Cycle18TrialMailingWindowHookTests</c>
    /// gives for its own direct-row channels) and opens the mailing window via the SAME shared
    /// <c>TrialMailingWindowStarter.StartIfDueAsync</c> the real hook calls (exercising the hook itself
    /// end-to-end is Part 1's job, CY18L-01..05) — this class' own concern starts one layer further down
    /// the chain: does an open window / materialized option row actually let a message through.</summary>
    private async Task SeedConnectedTrialChannelAsync(string ownerUserId, Guid accountId, Guid companyId)
    {
        await RunInDbAsync(async db =>
        {
            var channel = new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = ownerUserId, BillingAccountId = accountId,
                Transport = NotificationTransport.WhatsApp, State = ChannelState.Connected,
                PhoneNumber = UniquePhone().TrimStart('+'), ProviderInstanceId = Unique("instance-delivery-"),
                ConnectedAtUtc = DateTime.UtcNow,
            };
            channel.ProviderSecretCiphertext = SecretProtector.Encrypt(
                "test-provider-token", NotificationDispatchTestFactory.TestEncryptionKeyBase64, channel.Id);
            db.NotificationChannels.Add(channel);
            db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
            {
                Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = companyId,
                BillingAccountId = accountId, AssignedByUserId = ownerUserId,
            });

            var account = await db.BillingAccounts.FirstAsync(a => a.Id == accountId);
            await TrialMailingWindowStarter.StartIfDueAsync(db, account, DateTime.UtcNow, CancellationToken.None);
        });
    }

    private static async Task WaitForAsync(Func<Task<bool>> predicate, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(200);
        }
        (await predicate()).Should().BeTrue($"condition did not become true within {timeoutSeconds}s");
    }

    // ── Scenario 21 — the bridge actually works: a trial account's queued confirmation reaches SendAsync ──
    //
    // Review finding, the headline blocker (Дыра1): §333.3's AccountSubscriptionOption materialization
    // (commit 9c2d060) closes the DATA gap, but nothing in this file's first 20 tests ever proved the
    // full chain reaches an actual transport call — SubscriptionResolver.IsOptionCurrentlyPaid reads
    // ONLY this materialized row (no fallback to PlanOptionRule.IncludedQuantity — SubscriptionResolver.cs
    // ~L236-240) and NotificationGate.cs blocks outright at PaidNotificationNumbers == 0. Exactly the gap
    // between "row exists" and "message sent" that TrialLegalNotices.cs's own promised text ("Бесплатные
    // рассылки на пробном периоде заканчиваются {0}") depends on being closed for real.
    [Fact, TestCase("CY18L-21")]
    public async Task TrialAccountWithAuthorizedChannel_NewBookingConfirmation_ActuallyReachesSendAsync()
    {
        var trialPlanId = await CreateTrialPlanAsync(durationDays: 14, mailingWindowDays: 7);
        await EnsureWhatsAppIncludedOnTrialPlanAsync(trialPlanId);
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await MarkPhoneVerifiedAsync(owner.Phone, owner.UserId);
        var accountId = await DbAsync(db => db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync());
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await SeedConnectedTrialChannelAsync(owner.UserId, accountId, company.Id);

        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(clientUser.Token);
        var booking = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, "Walk-in", clientUser.Phone, null, null));
        booking.StatusCode.Should().Be(HttpStatusCode.Created, await booking.Content.ReadAsStringAsync());
        var bookingId = (await booking.Content.ReadJsonAsync<ServiceBooking.API.DTOs.Bookings.BookingDto>())!.Id;

        var queued = await DbAsync(db => db.OutboundNotifications
            .FirstAsync(n => n.BookingId == bookingId && n.Type == NotificationType.BookingConfirmed));
        queued.Status.Should().Be(NotificationStatus.Pending,
            "the row must actually be QUEUED, not Skipped/NotOnPaidPlan — that IS the bug this test targets");

        var canonicalClientPhone = clientUser.Phone.TrimStart('+').Replace(" ", "");
        await using var dispatchFactory = new TrialDispatchFactory(ConnectionString);
        _ = dispatchFactory.Services; // boot eagerly

        await WaitForAsync(async () =>
        {
            await using var scope = dispatchFactory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == queued.Id);
            return row.Status == NotificationStatus.Sent;
        }, timeoutSeconds: 20);

        dispatchFactory.Transport.Calls.Should().Contain(c => c.CanonicalPhone == canonicalClientPhone,
            "the message must reach the REAL transport, not just sit Pending in the queue — this is the " +
            "exact gap between §333.3's option row and an actual send that Дыра1 flagged");
    }

    // ── Scenario 22 — the mirror: once the option's own funding has ended, the SAME flow is blocked
    // again, synchronously, before ever reaching the transport ─────────────────────────────────────────
    [Fact, TestCase("CY18L-22")]
    public async Task TrialAccountAfterMailingWindowCloses_NewBookingConfirmation_NeverReachesSendAsync()
    {
        var trialPlanId = await CreateTrialPlanAsync(durationDays: 14, mailingWindowDays: 7);
        await EnsureWhatsAppIncludedOnTrialPlanAsync(trialPlanId);
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        await MarkPhoneVerifiedAsync(owner.Phone, owner.UserId);
        var accountId = await DbAsync(db => db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).Select(a => a.Id).FirstAsync());
        (await ActivateTrialAsync(owner.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await SeedConnectedTrialChannelAsync(owner.UserId, accountId, company.Id);

        // Simulate the mailing window having already closed (§336.3) — the option row's own PaidUntilUtc
        // (kept in sync with the window's own end date by TrialMailingWindowStarter, already covered end
        // to end by Part 1) is backdated directly, the same end state a real elapsed window leaves behind.
        var optionId = await DbAsync(db => GetOrCreateWhatsAppOptionIdAsync(db));
        await RunInDbAsync(async db =>
        {
            var option = await db.AccountSubscriptionOptions.FirstAsync(o => o.BillingAccountId == accountId && o.OptionId == optionId);
            option.PaidUntilUtc = DateTime.UtcNow.AddDays(-1);
        });

        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(clientUser.Token);
        var booking = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, "Walk-in", clientUser.Phone, null, null));
        booking.StatusCode.Should().Be(HttpStatusCode.Created, await booking.Content.ReadAsStringAsync());
        var bookingId = (await booking.Content.ReadJsonAsync<ServiceBooking.API.DTOs.Bookings.BookingDto>())!.Id;

        var queued = await DbAsync(db => db.OutboundNotifications
            .FirstAsync(n => n.BookingId == bookingId && n.Type == NotificationType.BookingConfirmed));
        queued.Status.Should().Be(NotificationStatus.Skipped,
            "§336.3: once the mailing window's own funding has ended, the gate must block again — the row " +
            "must never reach Pending, let alone an actual send");
        queued.Reason.Should().Be(NotificationReason.NotOnPaidPlan);
    }
}
