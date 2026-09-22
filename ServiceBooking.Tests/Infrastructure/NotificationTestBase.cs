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
/// It IS, however, in the same <c>[Collection("Api")]</c> as <see cref="ApiTestBase"/> — found missing
/// by the coordinator's own review of this QA pass, and load-bearing for two separate reasons, not one:
/// (1) <see cref="TestDatabaseFixture"/> wipes "servicebooking_test" exactly once, the first time ANY
/// test in the "Api" collection runs — a class outside every collection never triggers that wipe, so
/// rows accumulate silently across repeated `dotnet test` invocations (confirmed: a webhook test using a
/// literal, non-unique <c>ProviderMessageId</c> picked up a SEVEN-deep pile of same-named rows from prior
/// runs and updated the wrong one — see NotificationWebhookUnsubscribeTests.cs's own fix for the second,
/// independent line of defense against that); (2) xUnit only guarantees a test class doesn't run
/// concurrently with anything ELSE touching the same collection's fixture — a class outside every
/// collection is free to run in xUnit's default parallel bucket WHILE "Api"'s <c>TestDatabaseFixture</c>
/// is still mid-<c>EnsureDeletedAsync</c>+migrate, racing this factory's own independent Program.cs
/// migrate call against the SAME physical database (the exact "index/column already exists" class of
/// failure backend-developer reported before this cycle). The fixture parameter itself is intentionally
/// unused beyond establishing that dependency — this class still boots its OWN <see cref="NotificationTestFactory"/>
/// per instance (different <c>Notifications:*</c> settings), never <c>fixture.Factory</c>.
/// </summary>
[Collection("Api")]
public abstract class NotificationTestBase(TestDatabaseFixture fixture) : IAsyncDisposable
{
    // Referenced only to document/enforce the collection dependency above (see the class doc comment) —
    // deliberately never used to obtain a factory or connection string; every method below talks to its
    // own NotificationTestFactory instead.
    private readonly TestDatabaseFixture _collectionFixture = fixture;

    protected readonly NotificationTestFactory Factory = new();

    protected HttpClient AnonymousClient() => Factory.CreateClient();

    protected HttpClient AuthedClient(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 20, prefix.Length + 12)];

    protected static string UniquePhone()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsDigit).Take(10).ToArray();
        var suffix = new string(digits).PadRight(10, '0');
        return $"+79{suffix[..9]}";
    }

    protected async Task<AuthResponseDto> RegisterAsync(string? phone = null)
    {
        phone ??= UniquePhone();
        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Test", "Owner", phone, "Password123!", null, true));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    protected async Task<AuthResponseDto> LoginAsync(string phone, string password = "Password123!")
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/auth/login", new LoginDto(phone, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    protected Task<AuthResponseDto> LoginAsSuperAdminAsync() => LoginAsync("+70000000001", "SuperAdmin123!");

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

    protected async Task<CompanyDto> CreateCompanyAsync(string ownerToken, int? cityId = null, string? slug = null)
    {
        slug ??= Unique("company-");
        var client = AuthedClient(ownerToken);
        var response = await client.PostAsJsonAsync("/api/companies",
            new CreateCompanyDto($"Company {slug}", slug, null, null, null, null, cityId ?? await AnyCityIdAsync(), null, true));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompanyDto>())!;
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

        // Cycle 5 (ARCHITECTURE_CYCLE5.md §45.1): money is read through BillingAccountId now, so a
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
        await db.SaveChangesAsync();

        // Deliberately does NOT also fund notifications.whatsapp (EnsureWhatsAppPaidAsync) — this helper
        // only grants the PLAN's own AllowNotificationChannel/"may buy the option" flag (§47.2's first
        // gate check). "Is the option actually paid" is the orthogonal §47.1 axis several tests
        // (Connect_UnpaidChannel_Returns402, CreateChannel_Allowed_ReturnsNotConnectedNotPaid) exercise
        // as a DISTINCT, unfunded state — callers that need a funded channel call EnsureWhatsAppPaidAsync
        // themselves (see CreateConnectedChannelAsync below).
    }

    /// <summary>
    /// Cycle 5, stage 3 (ARCHITECTURE_CYCLE5.md §47.1) — funding is no longer read off the channel's
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
