using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.API.DTOs.WorkingHours;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Shared base for all functional test classes. Provides a real HTTP client wired to the
/// in-memory ServiceBooking.API host (see CustomWebApplicationFactory) plus helpers that
/// perform the common multi-step setup (register → create company → add master → seed
/// services/working hours/subscription) that almost every scenario needs.
///
/// All helpers use globally-unique emails/slugs (via <see cref="Unique"/>) so tests can run
/// against this class' own database without cleaning up after themselves.
///
/// ARCHITECTURE_CYCLE8_PHASE2.md §91/§92.1: one <see cref="TestDatabaseFixture"/> instance per test
/// class (<c>IClassFixture</c>, not the phase-1 <c>ICollectionFixture</c>/<c>[Collection("Api")]</c>) —
/// every subclass gets its own, disjoint database, cloned from the run's shared template.
/// </summary>
public abstract class ApiTestBase : IClassFixture<TestDatabaseFixture>
{
    protected readonly TestDatabaseFixture Fixture;
    protected readonly CustomWebApplicationFactory Factory;

    protected ApiTestBase(TestDatabaseFixture fixture)
    {
        Fixture = fixture;
        Factory = fixture.Factory;
        ConnectionString = fixture.ConnectionString;
        // T9 M3: records the slot↔class pairing this fixture's own doc comment promised — see
        // TestDatabaseFixture.RecordTestClass.
        fixture.RecordTestClass(GetType().Name);
    }

    /// <summary>This class' database connection string — for the rare subclass that needs to build a
    /// second, dedicated host against the same database (e.g. <see cref="RateLimitTestFactory"/>).</summary>
    protected readonly string ConnectionString;

    /// <summary>Anonymous (unauthenticated) client — for guest/public endpoint scenarios.</summary>
    protected HttpClient AnonymousClient() => Factory.CreateClient();

    /// <summary>Client with a Bearer token attached — for authenticated scenarios.</summary>
    protected HttpClient AuthedClient(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Produces a collision-free string for emails/slugs/codes — delegates to this class' own
    /// <see cref="TestData"/> (ARCHITECTURE_CYCLE8_PHASE2.md §94, Q12): no longer static, since uniqueness
    /// is now scoped to (and guaranteed by) this test class' own database, not the whole process.</summary>
    protected string Unique(string prefix) => Fixture.Data.Name(prefix);

    protected string UniqueEmail(string prefix) => Fixture.Data.Email(prefix);

    /// <summary>A unique, valid-looking phone number for this test class (accounts are keyed by phone).</summary>
    protected string UniquePhone() => Fixture.Data.Phone();

    // ── Identity ─────────────────────────────────────────────────────────────

    // CYCLE5-BREAKING (compile-only adaptation — ARCHITECTURE_CYCLE5.md §46.2, API_CONTRACT_CYCLE5.md
    // §40.1): `acceptedLegal: bool` is gone from RegisterDto, replaced by a `Legal` object carrying the
    // versions actually being accepted, read here from the live manifest. `acceptedLegal` is KEPT as this
    // helper's own parameter name/meaning ("build a request that will pass the legal gate, or one that
    // won't") so every existing call site in this suite keeps compiling unchanged; whether the RESULTING
    // behavior (and status code) still matches each test's assertions is exactly the kind of judgment
    // call this cycle's backend implementer left to QA (see the cycle report) — not touched here.
    protected async Task<AuthResponseDto> RegisterAsync(
        string? phone = null, string password = "Password123!", string firstName = "Test", string lastName = "User",
        string? email = null, bool acceptedLegal = true)
    {
        phone ??= UniquePhone();
        var client = AnonymousClient();
        var legal = acceptedLegal ? CurrentRegisterLegalDto() : null;
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto(firstName, lastName, phone, password, email, legal));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    /// <summary>
    /// TD-03 (ARCHITECTURE_CYCLE16.md §245): помечает номер аккаунта подтверждённым, вставляя строку в
    /// <c>VerifiedPhones</c> — источник истины гейта. Нужен тестам, которые проверяют СШИВАНИЕ гостевых
    /// сущностей с аккаунтом: с цикла 16 оно работает только при подтверждённом номере.
    ///
    /// ⚠️ Это НЕ ослабление проверок. Критерий приёмки TD-03 гласит: для аккаунта с подтверждённым
    /// номером поведение остаётся дословно прежним — именно это такие тесты и должны проверять. Случай
    /// НЕподтверждённого номера покрыт отдельно в <c>GuestDataGateCycle16Tests</c>.
    ///
    /// Пишем строку напрямую, а не гоняем сценарий подтверждения через бота MAX: тому нужна отдельная
    /// фабрика (<c>PhoneVerificationEnabledFactory</c>) и обмен вебхуками, что к предмету этих тестов
    /// отношения не имеет. Канонизация номера повторяет ту, что применяет продукт.
    /// </summary>
    protected async Task MarkPhoneVerifiedAsync(string phone, string? userId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var canonicalPhone = phone.TrimStart('+').Replace(" ", "");

        if (!await db.VerifiedPhones.AnyAsync(v => v.Phone == canonicalPhone))
        {
            db.VerifiedPhones.Add(new VerifiedPhone
            {
                Id = Guid.NewGuid(),
                Phone = canonicalPhone,
                Method = PhoneVerificationMethod.MaxBot,
                VerifiedAtUtc = DateTime.UtcNow,
                UserId = userId,
            });
            await db.SaveChangesAsync();
        }
    }

    protected async Task<HttpResponseMessage> RegisterRawAsync(
        string phone, string password, string firstName = "Test", string lastName = "User",
        string? email = null, bool acceptedLegal = true)
    {
        var client = AnonymousClient();
        var legal = acceptedLegal ? CurrentRegisterLegalDto() : null;
        return await client.PostAsJsonAsync("/api/auth/register", new RegisterDto(firstName, lastName, phone, password, email, legal));
    }

    /// <summary>The Legal object a registration call needs to pass the gate right now — read from the
    /// live manifest via DI, not hardcoded, so a version bump in App_Data/legal never desyncs this
    /// helper from what the server actually expects (same reasoning as <see cref="AnyCityIdAsync"/>).</summary>
    protected RegisterLegalDto CurrentRegisterLegalDto()
    {
        using var scope = Factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        var snapshot = provider.Current!;
        return new RegisterLegalDto(
            snapshot.Get(LegalDocumentType.Privacy)!.Version,
            snapshot.Get(LegalDocumentType.TermsClient)!.Version);
    }

    protected async Task<AuthResponseDto> LoginAsync(string phone, string password)
    {
        var client = AnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginDto(phone, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    protected async Task<HttpResponseMessage> LoginRawAsync(string phone, string password)
    {
        var client = AnonymousClient();
        return await client.PostAsJsonAsync("/api/auth/login", new LoginDto(phone, password));
    }

    /// <summary>Logs in as the SuperAdmin seeded at startup for this host's factoryTag (§71.1).</summary>
    protected Task<AuthResponseDto> LoginAsSuperAdminAsync() =>
        LoginAsync(Factory.Identity.SuperAdminPhone, Factory.Identity.SuperAdminPassword);

    // ── Companies ────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a fresh Client user and turns them into a CompanyOwner by creating a company.
    /// Re-logs in afterward: JWTs bake in roles at issue time, and the initial registration token
    /// only ever has the "Client" role — creating a company grants "CompanyOwner" server-side, but
    /// that stale token would still lack the claim, breaking any role-claim-gated endpoint
    /// (e.g. ReportsController/AdminController's [Authorize(Roles = "...")]).
    /// </summary>
    /// <param name="onlineBooking">
    /// When true (default), the owner account gets a fully-featured plan so online self-booking works —
    /// this keeps the many tests that create authenticated bookings green. Pass false to give a
    /// "restricted" plan (online booking off, other paid features off) that still allows staff and extra
    /// companies, so setup like <see cref="AddMasterAsync"/> keeps working while the online-booking gate
    /// stays closed.
    /// </param>
    /// <param name="attachPlan">
    /// When false, the owner is left with NO subscription at all (true Free: 1 employee, 1 company) —
    /// used by tests that assert the employee/branch limits of the plan-less baseline.
    /// </param>
    protected async Task<(AuthResponseDto Owner, CompanyDto Company)> CreateOwnerWithCompanyAsync(
        bool allowSelfBooking = true, bool requirePrepayment = false, bool onlineBooking = true, bool attachPlan = true)
    {
        var registered = await RegisterAsync();
        // The first company always creates: a brand-new owner has 0 companies vs. the Free limit of 1.
        var company = await CreateCompanyAsync(registered.Token, allowSelfBooking: allowSelfBooking);
        var owner = await LoginAsync(registered.Phone, "Password123!");
        if (requirePrepayment)
        {
            var client = AuthedClient(owner.Token);
            var r = await client.PutAsJsonAsync($"/api/companies/{company.Id}", new
            {
                allowSelfBooking = (bool?)null,
                requirePrepayment = true
            });
            r.EnsureSuccessStatusCode();
        }
        if (attachPlan)
            await GiveAccountPlanAsync(owner.UserId, onlineBooking ? FullAccessPlanName : RestrictedPlanName, onlineBooking);
        return (owner, company);
    }

    private const string FullAccessPlanName = "QA Full Access";
    private const string RestrictedPlanName = "QA Restricted (no online booking)";

    /// <summary>
    /// Writes an active account-level subscription (linked to a shared test tariff) straight to the DB
    /// for the given owner — fast, and without the SuperAdmin login the HTTP <see cref="SetSubscriptionAsync"/>
    /// path needs. The full-access variant unlocks every feature with unlimited employees/companies; the
    /// restricted variant turns online booking (and other paid features) off but keeps limits unlimited.
    /// </summary>
    protected async Task GiveActivePaidPlanAsync(string ownerUserId) =>
        await GiveAccountPlanAsync(ownerUserId, FullAccessPlanName, allFeatures: true);

    private async Task GiveAccountPlanAsync(string ownerUserId, string planName, bool allFeatures)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var planConfig = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.Name == planName);
        if (planConfig is null)
        {
            planConfig = new SubscriptionPlanConfig
            {
                Id = Guid.NewGuid(), Name = planName,
                // Non-zero: cycle-07 QA finding #1 made PricePerMonth == 0 && IsActive a meaningful
                // "eligible to become the system free plan" signal on AdminController.SetSystemFree —
                // this fixture plan must never accidentally qualify.
                PricePerMonth = 1, MaxEmployees = null, MaxCompanies = null,
                AllowOnlineBooking = allFeatures, AllowMailing = allFeatures,
                AllowAnalytics = allFeatures, AllowOnlinePayment = allFeatures,
                IsActive = true, CreatedAt = DateTime.UtcNow,
            };
            db.SubscriptionPlanConfigs.Add(planConfig);
        }

        var accountId = await EnsureBillingAccountAsync(db, ownerUserId);

        var sub = await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId);
        if (sub is null)
        {
            db.AccountSubscriptions.Add(new AccountSubscription
            {
                Id = Guid.NewGuid(), OwnerUserId = ownerUserId, PlanConfigId = planConfig.Id,
                BillingAccountId = accountId,
                PaidUntil = DateTime.UtcNow.AddMonths(1), IsActive = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            sub.PlanConfigId = planConfig.Id;
            sub.BillingAccountId = accountId;
            sub.PaidUntil = DateTime.UtcNow.AddMonths(1);
            sub.IsActive = true;
            sub.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Cycle 7 (ARCHITECTURE_CYCLE7.md §45.1) — test-harness mirror of
    /// <c>BillingAccountProvisioner.EnsureAccountAsync</c>: money now reads through
    /// <see cref="BillingAccount"/>, so a helper that writes an <see cref="AccountSubscription"/> row
    /// directly (bypassing the real HTTP endpoints, which is the whole point of these arrange-only
    /// helpers) must also make sure the owner has an account, and that any of their already-created
    /// companies point at it — otherwise the row it writes is invisible to
    /// <c>SubscriptionResolver.GetEffectivePlanAsync</c>.
    /// </summary>
    private static async Task<Guid> EnsureBillingAccountAsync(AppDbContext db, string ownerUserId)
    {
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
        if (orphanedCompanies.Count > 0)
            await db.SaveChangesAsync();

        return account.Id;
    }

    /// <summary>
    /// Creates a standalone <see cref="SubscriptionPlanConfig"/> row directly in the database, for tests
    /// that need a specific, narrow feature set (e.g. asserting a gate rejects when a flag is off).
    /// </summary>
    protected async Task<Guid> CreateTestPlanConfigAsync(
        bool allowOnlineBooking = false, bool allowMailing = false,
        bool allowAnalytics = false, bool allowPublicListing = true, bool allowOnlinePayment = false,
        int? maxEmployees = null, int? maxCompanies = null,
        int? photoQuotaMb = 100, PhotoRetention photoRetention = PhotoRetention.SixMonths)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(), Name = Unique("Test Plan "),
            // Non-zero for the same reason as GiveAccountPlanAsync above.
            PricePerMonth = 1, MaxEmployees = maxEmployees, MaxCompanies = maxCompanies,
            AllowOnlineBooking = allowOnlineBooking, AllowMailing = allowMailing,
            AllowAnalytics = allowAnalytics, AllowPublicListing = allowPublicListing, AllowOnlinePayment = allowOnlinePayment,
            PhotoQuotaMb = photoQuotaMb, PhotoRetention = photoRetention,
            IsActive = true, CreatedAt = DateTime.UtcNow,
        };
        db.SubscriptionPlanConfigs.Add(config);
        await db.SaveChangesAsync();
        return config.Id;
    }

    // CYCLE5-BREAKING (compile-only adaptation, see RegisterAsync's own note — ARCHITECTURE_CYCLE5.md
    // §42.1, API_CONTRACT_CYCLE5.md §42.1 BREAKING № 3): CreateCompanyDto needs an `ownerTerms` object
    // now, and the response is an envelope `{ company, token }`, not a bare CompanyDto — unwrapped here
    // so every one of this helper's ~50 existing call sites keeps compiling AND keeps getting a real
    // CompanyDto back unchanged.
    protected async Task<CompanyDto> CreateCompanyAsync(string ownerToken, string? name = null, string? slug = null, bool allowSelfBooking = true)
    {
        slug ??= Unique("company-");
        name ??= $"Company {slug}";
        var client = AuthedClient(ownerToken);
        var response = await client.PostAsJsonAsync("/api/companies",
            new CreateCompanyDto(name, slug, null, null, null, null, await AnyCityIdAsync(), null, allowSelfBooking,
                OwnerTerms: CurrentOwnerTermsDto()));
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<CreateCompanyResponseDto>();
        return envelope!.Company;
    }

    /// <summary>The OwnerTerms object a company-creation call needs to pass the owner gate right now —
    /// read from the live manifest, same reasoning as CurrentRegisterLegalDto.</summary>
    protected OwnerTermsDto CurrentOwnerTermsDto()
    {
        using var scope = Factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        var snapshot = provider.Current!;
        return new OwnerTermsDto(snapshot.Get(LegalDocumentType.TermsOwner)!.Version);
    }

    // Cycle 4 (API_CONTRACT_CYCLE4.md §31.2): CreateCompanyDto.CityId is required. Tests that don't care
    // which city just need ANY valid, active one — seeded by the SeedCities migration, so this never
    // needs its own setup. Cached per test class instance since it's the same answer every call.
    private int? _anyCityId;
    protected async Task<int> AnyCityIdAsync()
    {
        if (_anyCityId is not null) return _anyCityId.Value;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _anyCityId = await db.Cities.Where(c => c.IsActive).Select(c => c.Id).FirstAsync();
        return _anyCityId.Value;
    }

    /// <summary>
    /// Registers a fresh Client user and attaches them to the company as a Master (or CompanyOwner).
    /// Re-logs in afterward: JWTs bake in roles at issue time, and the initial registration token
    /// only ever has the "Client" role, so it would be stale for anything requiring the new role.
    /// Commission is set by the OWNER (via the members/{memberId}/commission endpoint), not by the
    /// master themselves — a master can no longer set their own commission through /api/profile.
    /// </summary>
    protected async Task<AuthResponseDto> AddMasterAsync(string ownerToken, Guid companyId, string role = "Master", decimal commissionPercent = 0)
    {
        var registered = await RegisterAsync();
        var client = AuthedClient(ownerToken);
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/members",
            new { phone = registered.Phone, firstName = registered.FirstName, lastName = registered.LastName, role, bio = (string?)null, email = (string?)null });
        response.EnsureSuccessStatusCode();

        var master = await LoginAsync(registered.Phone, "Password123!");

        if (commissionPercent > 0)
        {
            var memberDto = (await response.Content.ReadJsonAsync<MemberDto>())!;
            await AuthedClient(ownerToken).PutAsJsonAsync(
                $"/api/companies/{companyId}/members/{memberDto.Id}/commission", new { commissionPercent });
        }

        return master;
    }

    // ── Services & schedule ──────────────────────────────────────────────────

    protected async Task<ServiceDto> CreateServiceAsync(string token, Guid companyId, string? name = null, int durationMinutes = 60, decimal price = 1000)
    {
        var client = AuthedClient(token);
        var response = await client.PostAsJsonAsync("/api/services",
            new CreateServiceDto(companyId, name ?? Unique("Service "), null, durationMinutes, price));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServiceDto>())!;
    }

    /// <summary>Marks the given master as working the whole day (09:00–18:00, no breaks) on the given date.</summary>
    protected async Task<WorkingHoursDto> SetWorkingDayAsync(
        string actingToken, string masterId, Guid companyId, DateOnly date,
        TimeOnly? start = null, TimeOnly? end = null, bool isWorking = true)
    {
        var client = AuthedClient(actingToken);
        var response = await client.PutAsJsonAsync("/api/workinghours", new UpsertWorkingHoursDto(
            masterId, companyId, date, isWorking,
            start ?? new TimeOnly(9, 0), end ?? new TimeOnly(18, 0), []));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkingHoursDto>())!;
    }

    // ── Subscriptions (unlocks paid-plan-gated features like guest/online booking) ─────

    /// <summary>
    /// Sets the subscription for a company's owner ACCOUNT straight in the database. Takes a companyId
    /// for convenience and resolves the owner internally. Defaults to linking the shared full-access
    /// test plan config so callers that only care about "paid vs free" don't need to create their own.
    ///
    /// Used to go through the real admin HTTP endpoint (<c>PUT /api/admin/owners/{ownerUserId}/subscription</c>),
    /// which exercised that endpoint's own validation as a side effect. That endpoint is now contractually
    /// retired (contracts/cycle7/openapi.yaml, redaction 2.1 — see <c>AdminController.UpdateSubscription</c>, answers
    /// 410 Gone) and its replacement (<c>PUT /admin/billing-accounts/{accountId}/subscription</c>) doesn't
    /// exist yet (cycle-07 backend report), so this helper — which exists purely to arrange test
    /// preconditions, not to exercise that endpoint's behavior — writes the row directly, the same way
    /// <see cref="GiveActivePaidPlanAsync"/> already does. Tests that actually need to exercise an
    /// admin subscription-assignment endpoint's own behavior should do so explicitly against whichever
    /// endpoint currently implements it, not through this helper.
    /// </summary>
    protected async Task SetSubscriptionAsync(Guid companyId, Guid? planConfigId = null, DateTime? paidUntil = null, bool isActive = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerUserId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.OwnerUserId).FirstAsync();

        if (planConfigId is null)
        {
            var fullAccess = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.Name == FullAccessPlanName);
            if (fullAccess is null)
            {
                fullAccess = new SubscriptionPlanConfig
                {
                    Id = Guid.NewGuid(), Name = FullAccessPlanName,
                    // Non-zero for the same reason as GiveAccountPlanAsync above.
                    PricePerMonth = 1, MaxEmployees = null, MaxCompanies = null,
                    AllowOnlineBooking = true, AllowMailing = true, AllowAnalytics = true, AllowOnlinePayment = true,
                    IsActive = true, CreatedAt = DateTime.UtcNow,
                };
                db.SubscriptionPlanConfigs.Add(fullAccess);
                await db.SaveChangesAsync();
            }
            planConfigId = fullAccess.Id;
        }

        var accountId = await EnsureBillingAccountAsync(db, ownerUserId);

        var sub = await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId);
        var effectivePaidUntil = paidUntil ?? DateTime.UtcNow.AddMonths(1);
        if (sub is null)
        {
            db.AccountSubscriptions.Add(new AccountSubscription
            {
                Id = Guid.NewGuid(), OwnerUserId = ownerUserId, PlanConfigId = planConfigId,
                BillingAccountId = accountId,
                PaidUntil = effectivePaidUntil, IsActive = isActive,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            sub.PlanConfigId = planConfigId;
            sub.BillingAccountId = accountId;
            sub.PaidUntil = effectivePaidUntil;
            sub.IsActive = isActive;
            sub.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates an Identity account directly (bypassing POST /api/auth/register and its
    /// PhoneNormalizer.TryNormalizeRussian gate — ARCHITECTURE_CYCLE6.md §48.1/§48.4), for tests that
    /// need to simulate an account that predates the cycle 6 Russian-only restriction (or was created by
    /// an admin) with a non-Russian phone number. US-61/Q4 only blocks NEW data; existing accounts with
    /// a foreign number must keep logging in and being found by search.
    /// </summary>
    protected async Task CreateRawAccountAsync(string canonicalPhone, string password, string role = "Client")
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var user = new AppUser
        {
            FirstName = "Foreign", LastName = "Account", UserName = canonicalPhone, PhoneNumber = canonicalPhone,
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, role);
    }

    protected static DateOnly NextWeekday(DayOfWeek? avoid = null)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        while (avoid.HasValue && date.DayOfWeek == avoid.Value)
            date = date.AddDays(1);
        return date;
    }

    // ── Cycle 5 consent helpers (ARCHITECTURE_CYCLE5.md §44, §45, §41) ─────────────────────────

    /// <summary>The current version of an interface text (GET /api/legal/texts/{key}) — read from the
    /// live manifest via DI, same reasoning as <see cref="CurrentRegisterLegalDto"/>.</summary>
    protected string CurrentTextVersion(string key)
    {
        using var scope = Factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        return scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>()
            .Current!.GetText(key)!.Version;
    }

    /// <summary>US-76/T5-B7: records staff-confirmed photo consent for one client of one company — the
    /// precondition <c>POST /api/client-notes/{id}/photos</c> now enforces (API_CONTRACT_CYCLE5.md
    /// §44.3). <paramref name="clientKey"/> is either a registered client's userId or
    /// <c>phone:&lt;canonical&gt;</c> for a guest (ClientKey's own format).</summary>
    protected async Task GrantPhotoConsentAsync(string staffToken, Guid companyId, string clientKey)
    {
        var response = await AuthedClient(staffToken).PostAsJsonAsync(
            $"/api/companies/{companyId}/clients/{clientKey}/photo-consent",
            new { textVersion = CurrentTextVersion("PhotoConsent"), confirmed = true });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>US-77/T5-B6: records staff-confirmed health-data consent for one client of one company —
    /// the precondition <c>PUT .../health-note</c> enforces (API_CONTRACT_CYCLE5.md §45.2).</summary>
    protected async Task GrantHealthConsentAsync(string staffToken, Guid companyId, string clientKey)
    {
        var response = await AuthedClient(staffToken).PostAsJsonAsync(
            $"/api/companies/{companyId}/clients/{clientKey}/health-consent",
            new { textVersion = CurrentTextVersion("HealthDataConsent"), confirmed = true });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>US-68/T5-B3: grants the registered account holder's own PdnConsent/ProviderDelivery
    /// purpose — required under the shipped <c>AccountsOnly</c> gate mode (ARCHITECTURE_CYCLE5.md §52.3)
    /// for a queued notification to actually reach <c>Pending</c> instead of being blocked with
    /// <c>NoProviderDeliveryConsent</c>. A guest never needs this (the gate treats "no account" as
    /// automatically satisfied) — only call this for a client who registered via RegisterAsync.</summary>
    protected async Task GrantProviderDeliveryConsentAsync(string clientToken)
    {
        using var scope = Factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        var pdnVersion = provider.Current!.Get(LegalDocumentType.PdnConsent)!.Version;

        var response = await AuthedClient(clientToken).PostAsJsonAsync("/api/profile/consents",
            new { documentKey = "PdnConsent", version = pdnVersion, purposes = new[] { "ProviderDelivery" } });
        response.EnsureSuccessStatusCode();
    }
}
