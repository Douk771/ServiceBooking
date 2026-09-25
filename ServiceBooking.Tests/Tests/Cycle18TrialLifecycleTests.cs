using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
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
/// </summary>
public class Cycle18TrialLifecycleTests
{
}

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
}
