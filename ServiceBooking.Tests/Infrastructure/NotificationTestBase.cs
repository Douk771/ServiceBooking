using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// QA cycle 4 — shared setup for functional tests against <see cref="NotificationTestFactory"/>. Mirrors
/// the handful of <see cref="ApiTestBase"/> helpers this cycle's tests actually need; not DERIVED from
/// <see cref="ApiTestBase"/> because that class is hard-wired to <see cref="CustomWebApplicationFactory"/>,
/// and this cycle's channel/webhook/unsubscribe surface needs a host with different
/// <c>Notifications:*</c> settings (see <see cref="NotificationTestFactory"/>'s own doc comment).
///
/// ARCHITECTURE_CYCLE8_PHASE2.md §91/§92.1: one <see cref="TestDatabaseFixture"/> instance per test
/// class (<c>IClassFixture</c>, not phase 1's <c>ICollectionFixture</c>/<c>[Collection("Api")]</c>) — the
/// fixture guarantees this class' own database exists and is migrated before <see cref="NotificationTestFactory"/>
/// boots against it. This class still boots its OWN <see cref="NotificationTestFactory"/> per instance
/// (different <c>Notifications:*</c> settings), never <c>fixture.Factory</c> — the fixture is used only
/// for its connection string, class slot and <see cref="TestData"/> generator.
/// </summary>
public abstract class NotificationTestBase : IClassFixture<TestDatabaseFixture>, IAsyncDisposable
{
    protected readonly TestDatabaseFixture Fixture;

    /// <summary>This class' database connection string — for the rare subclass that needs to build a
    /// second, dedicated host against the same database.</summary>
    protected readonly string ConnectionString;

    protected readonly NotificationTestFactory Factory;

    protected NotificationTestBase(TestDatabaseFixture fixture)
    {
        Fixture = fixture;
        ConnectionString = fixture.ConnectionString;
        Factory = Boot(new NotificationTestFactory(fixture.ConnectionString));
        // T9 M3: records the slot↔class pairing this fixture's own doc comment promised — see
        // TestDatabaseFixture.RecordTestClass.
        fixture.RecordTestClass(GetType().Name);
    }

    /// <summary>Boots the host eagerly (rather than lazily on first <see cref="WebApplicationFactory{TEntryPoint}.CreateClient"/>)
    /// so <see cref="NotificationTestFactory.Identity"/> — populated inside <c>ConfigureWebHost</c> — is
    /// always available by the time <see cref="LoginAsSuperAdminAsync"/> reads it.</summary>
    private static NotificationTestFactory Boot(NotificationTestFactory factory)
    {
        _ = factory.Services;
        return factory;
    }

    protected HttpClient AnonymousClient() => Factory.CreateClient();

    protected HttpClient AuthedClient(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Delegates to this class' own <see cref="TestData"/> — see <see cref="ApiTestBase.Unique"/>'s
    /// own note on why this is no longer static (§94, Q12).</summary>
    protected string Unique(string prefix) => Fixture.Data.Name(prefix);

    protected string UniquePhone() => Fixture.Data.Phone();

    // CYCLE5-BREAKING (compile-only adaptation, see ApiTestBase.RegisterAsync's own note): Legal object
    // read from the live manifest instead of a bool.
    protected async Task<AuthResponseDto> RegisterAsync(string? phone = null)
    {
        phone ??= UniquePhone();
        using var scope = Factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        var snapshot = provider.Current!;
        var legal = new RegisterLegalDto(
            snapshot.Get(LegalDocumentType.Privacy)!.Version, snapshot.Get(LegalDocumentType.TermsClient)!.Version);

        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Test", "Owner", phone, "Password123!", null, legal));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    protected async Task<AuthResponseDto> LoginAsync(string phone, string password = "Password123!")
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/login", new LoginDto(phone, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    protected Task<AuthResponseDto> LoginAsSuperAdminAsync() =>
        LoginAsync(Factory.Identity.SuperAdminPhone, Factory.Identity.SuperAdminPassword);

    private int? _anyCityId;
    protected async Task<int> AnyCityIdAsync()
    {
        if (_anyCityId is not null) return _anyCityId.Value;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _anyCityId = await db.Cities.Where(c => c.IsActive).Select(c => c.Id).FirstAsync();
        return _anyCityId.Value;
    }

    protected async Task<int> BarnaulCityIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Cities.Where(c => c.Name == "Барнаул").Select(c => c.Id).FirstAsync();
    }

    protected async Task<int> NovosibirskCityIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Cities.Where(c => c.Name == "Новосибирск").Select(c => c.Id).FirstAsync();
    }

    // CYCLE5-BREAKING (compile-only adaptation, see ApiTestBase.CreateCompanyAsync's own note):
    // OwnerTerms is now required, and the response is an envelope, not a bare CompanyDto.
    protected async Task<CompanyDto> CreateCompanyAsync(string ownerToken, int? cityId = null, string? slug = null)
    {
        slug ??= Unique("company-");
        var client = AuthedClient(ownerToken);
        using var scope = Factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        var ownerTermsVersion = provider.Current!.Get(LegalDocumentType.TermsOwner)!.Version;
        var response = await client.PostAsJsonAsync("/api/companies",
            new CreateCompanyDto($"Company {slug}", slug, null, null, null, null, cityId ?? await AnyCityIdAsync(), null, true,
                OwnerTerms: new OwnerTermsDto(ownerTermsVersion)));
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<CreateCompanyResponseDto>();
        return envelope!.Company;
    }

    /// <summary>Registers an owner and gives them a first company (an owner always needs ≥1 company to
    /// pass <c>IsAnyCompanyOwnerAsync</c> on the channel endpoints).</summary>
    protected async Task<(AuthResponseDto Owner, CompanyDto Company)> CreateOwnerWithCompanyAsync()
    {
        var registered = await RegisterAsync();
        var company = await CreateCompanyAsync(registered.Token);
        var owner = await LoginAsync(registered.Phone);
        return (owner, company);
    }

    /// <summary>Creates a plan config with <c>AllowNotificationChannel = true</c> and attaches it to the
    /// given owner's account subscription directly via the database — fast, and avoids a second
    /// SuperAdmin-login HTTP round trip per test (mirrors <see cref="ApiTestBase.GiveActivePaidPlanAsync"/>'s
    /// own DB-write shortcut for the same reason).</summary>
    protected async Task GiveNotificationCapablePlanAsync(string ownerUserId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var plan = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(), Name = Unique("QA Notif Plan "), IsActive = true,
            AllowNotificationChannel = true, AllowOnlineBooking = true, AllowMailing = true,
            AllowAnalytics = true, AllowOnlinePayment = true, CreatedAt = DateTime.UtcNow,
        };
        db.SubscriptionPlanConfigs.Add(plan);

        // Cycle 7 (ARCHITECTURE_CYCLE7.md §45.1): money is read through BillingAccountId now, so a
        // helper that writes the AccountSubscription row directly must also make sure the owner has an
        // account (and that any of their companies already created point at it) — mirrors
        // ApiTestBase.EnsureBillingAccountAsync.
        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId);
        if (account is null)
        {
            account = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = ownerUserId };
            db.BillingAccounts.Add(account);
            await db.SaveChangesAsync();
        }
        var orphanedCompanies = await db.Companies
            .Where(c => c.OwnerUserId == ownerUserId && c.BillingAccountId == null)
            .ToListAsync();
        foreach (var company in orphanedCompanies)
            company.BillingAccountId = account.Id;

        var sub = await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId);
        if (sub is null)
        {
            db.AccountSubscriptions.Add(new AccountSubscription
            {
                Id = Guid.NewGuid(), OwnerUserId = ownerUserId, PlanConfigId = plan.Id,
                BillingAccountId = account.Id,
                PaidUntil = DateTime.UtcNow.AddMonths(1), IsActive = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            sub.PlanConfigId = plan.Id;
            sub.BillingAccountId = account.Id;
            sub.PaidUntil = DateTime.UtcNow.AddMonths(1);
            sub.IsActive = true;
            sub.UpdatedAt = DateTime.UtcNow;
        }
        // Regression fix (this QA pass): SubscriptionResolver.IsOptionCurrentlyPaid is fail-closed on a
        // missing PlanOptionRule (N13/N14) — a plan with AllowNotificationChannel=true but no explicit
        // rule for the WhatsApp option still resolves PaidNotificationNumbers to 0 for EVERY account on
        // it, no matter how many numbers EnsureWhatsAppPaidAsync (below, or by a caller) marks paid. That
        // silently broke every "funded channel" scenario this helper feeds — Connect_RiskNotAccepted_
        // Returns409, Connect_DoubleClick_SecondConcurrentRequestGets409_NoSecondInstance, and anything
        // else built on CreateConnectedChannelAsync/MarkPaidAsync — with a 402 "channel not paid" instead
        // of the behavior actually under test, deterministically (this plan is freshly created per call,
        // so no other test's data could ever have supplied the missing rule by accident).
        await EnsureWhatsAppPlanRuleAsync(db, plan.Id);
        await db.SaveChangesAsync();

        // Deliberately does NOT also fund notifications.whatsapp (EnsureWhatsAppPaidAsync) — this helper
        // only grants the PLAN's own AllowNotificationChannel/"may buy the option" flag (§47.2's first
        // gate check) plus the PLAN-level rule that makes the option purchasable at all (added just
        // above — a prerequisite for that gate, not the gate itself). "Is the option actually paid" is
        // the orthogonal §47.1 axis several tests (Connect_UnpaidChannel_Returns402,
        // CreateChannel_Allowed_ReturnsNotConnectedNotPaid) exercise as a DISTINCT, unfunded state —
        // callers that need a funded channel call EnsureWhatsAppPaidAsync themselves (see
        // CreateConnectedChannelAsync below).
    }

    private static async Task<Guid> GetOrCreateWhatsAppOptionIdAsync(AppDbContext db)
    {
        var existingId = await db.SubscriptionOptions
            .Where(o => o.Code == ServiceBooking.API.Services.SubscriptionResolver.WhatsAppOptionCode)
            .Select(o => o.Id)
            .FirstOrDefaultAsync();
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

    /// <summary>
    /// Shared fix (this QA pass) for the fail-closed PlanOptionRule gap (N13/N14, see
    /// <see cref="GiveNotificationCapablePlanAsync"/>'s own comment) — every raw-row plan-seeding helper
    /// across the notification test suite (this class, <c>NotificationDispatchTests</c>,
    /// <c>NotificationDispatchExtraTests</c>) hit the identical bug: a freshly-created plan with
    /// <c>AllowNotificationChannel = true</c> but no explicit rule for the WhatsApp option always
    /// resolves <c>PaidNotificationNumbers = 0</c>, no matter how many numbers
    /// <see cref="EnsureWhatsAppPaidAsync"/> marks paid on the account — deterministically breaking
    /// every scenario that depends on a genuinely funded/Connected channel (dispatch actually sending,
    /// Connect actually succeeding), not merely flaking under load as the cycle-07 backend report
    /// assumed. Idempotent: safe to call on a plan that already has a rule (only adds one if missing).
    /// </summary>
    public static async Task EnsureWhatsAppPlanRuleAsync(AppDbContext db, Guid planConfigId)
    {
        var optionId = await GetOrCreateWhatsAppOptionIdAsync(db);
        var existingRule = await db.PlanOptionRules
            .FirstOrDefaultAsync(r => r.PlanConfigId == planConfigId && r.OptionId == optionId);
        if (existingRule is null)
        {
            db.PlanOptionRules.Add(new PlanOptionRule
            {
                Id = Guid.NewGuid(), PlanConfigId = planConfigId, OptionId = optionId,
                Availability = OptionAvailability.Extra,
            });
        }
        else if (existingRule.Availability == OptionAvailability.Unavailable)
        {
            existingRule.Availability = OptionAvailability.Extra;
        }
    }

    /// <summary>
    /// Cycle 7, stage 3 (ARCHITECTURE_CYCLE7.md §47.1) — funding is no longer read off the channel's
    /// own PaidUntilUtc; it comes from the account's paid <c>notifications.whatsapp</c> quantity
    /// (<see cref="ServiceBooking.API.Services.SubscriptionResolver.WhatsAppOptionCode"/>). Every raw-row
    /// seeding helper that wants a channel to actually be <c>Funded</c> must call this too — creates the
    /// catalog row on first use (idempotent per test database) and one <c>AccountSubscriptionOption</c>
    /// row for the account.
    /// </summary>
    public static async Task EnsureWhatsAppPaidAsync(AppDbContext db, Guid billingAccountId, int quantity = 1)
    {
        var option = await db.SubscriptionOptions.FirstOrDefaultAsync(
            o => o.Code == ServiceBooking.API.Services.SubscriptionResolver.WhatsAppOptionCode);
        if (option is null)
        {
            option = new SubscriptionOption
            {
                Id = Guid.NewGuid(),
                Code = ServiceBooking.API.Services.SubscriptionResolver.WhatsAppOptionCode,
                Name = "Рассылки в WhatsApp",
                Kind = OptionKind.Quantity,
                UnitName = "номер",
                IsActive = true,
            };
            db.SubscriptionOptions.Add(option);
            await db.SaveChangesAsync();
        }

        var existing = await db.AccountSubscriptionOptions.FirstOrDefaultAsync(
            o => o.BillingAccountId == billingAccountId && o.OptionId == option.Id);
        if (existing is null)
        {
            db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
            {
                Id = Guid.NewGuid(), BillingAccountId = billingAccountId, OptionId = option.Id, Quantity = quantity,
            });
        }
        else
        {
            existing.Quantity = quantity;
            existing.EndsAtUtc = null;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Sets the platform's channel price/idle-days directly via the database — equivalent to a
    /// SuperAdmin <c>PUT /api/admin/platform-settings</c> call but without an extra login round trip. Tests
    /// that specifically exercise the admin endpoint itself call it over HTTP instead.</summary>
    protected async Task SetChannelPriceAsync(decimal? pricePerMonth, int idleDays = 3)
    {
        var admin = await LoginAsSuperAdminAsync();
        var client = AuthedClient(admin.Token);
        var response = await client.PutAsJsonAsync("/api/admin/platform-settings",
            new { channelPricePerMonth = pricePerMonth, channelIdleDays = idleDays });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Full happy-path setup for a channel that's ready to receive queued notifications: owner
    /// with a notification-capable plan, a paid+assigned+Connected channel written directly to the
    /// database (bypassing the QR flow, which this factory's <c>NoopChannelProvisioning</c> stub would
    /// otherwise require polling through — direct seeding is the same shortcut
    /// <c>NotificationDispatchTests.SeedConnectedChannelAsync</c> already takes for the same reason).</summary>
    protected async Task<(AuthResponseDto Owner, CompanyDto Company, NotificationChannel Channel)> CreateConnectedChannelAsync(
        DateTime? paidUntilUtc = null)
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var billingAccountId = await db.Companies.Where(c => c.Id == company.Id).Select(c => c.BillingAccountId!.Value).FirstAsync();

        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = owner.UserId, BillingAccountId = billingAccountId, State = ChannelState.Connected,
            PhoneNumber = UniquePhone().TrimStart('+'),
            ProviderInstanceId = Unique("instance"),
            PaidFromUtc = DateTime.UtcNow.AddDays(-1), PaidUntilUtc = paidUntilUtc ?? DateTime.UtcNow.AddDays(30),
            ConnectedAtUtc = DateTime.UtcNow.AddDays(-1),
            RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1),
        };
        channel.ProviderSecretCiphertext = SecretProtector.Encrypt("test-provider-token", NotificationTestFactory.TestEncryptionKeyBase64, channel.Id);
        db.NotificationChannels.Add(channel);

        db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = company.Id, BillingAccountId = billingAccountId, AssignedByUserId = owner.UserId,
        });
        await db.SaveChangesAsync();

        // This helper's whole point is "ready to receive queued notifications" — §47.1 funding is part
        // of that readiness, unlike GiveNotificationCapablePlanAsync above (plan-level allowance only).
        await EnsureWhatsAppPaidAsync(db, billingAccountId);

        return (owner, company, channel);
    }

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}
