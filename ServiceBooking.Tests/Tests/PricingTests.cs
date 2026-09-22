using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 5 (SPEC.md US-71, US-72; API_CONTRACT_CYCLE5.md §39-40) — the public pricing catalog
/// surface: <c>GET /api/pricing</c> (anonymous) and <c>GET /api/admin/pricing/preview</c> (SuperAdmin).
/// Written from SPEC.md/API_CONTRACT_CYCLE5.md, independently of PricingController's/
/// PricingCatalogBuilder's own implementation, per this cycle's QA brief ("Вызов 2"). Only these two
/// endpoints exist in this slice of cycle 5 (see git log on cycle/07-pricing-model-rework) — everything
/// else in SPEC.md's Эпик 1/3 (billing account, /api/billing/subscription, admin CRUD of the catalog,
/// company transfer, US-64/65/66/67/68/69/70/73/74/76/77) has no implementation yet and is out of scope
/// for this test pass; it is flagged separately in the QA report, not tested here.
///
/// The catalog cache (<see cref="PricingCatalogCache"/>) is a scoped wrapper around a process-wide
/// <c>IMemoryCache</c> with a 60s TTL, and the whole "Api" xUnit collection shares one host/DB — so
/// every test that cares about exact catalog *content* invalidates the cache after writing rows and
/// before asserting, instead of relying on TTL expiry (which would make the suite slow and flaky).
/// </summary>
public class PricingTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── Seeding helpers (no admin write endpoint exists yet in this slice, ARCHITECTURE_CYCLE5.md §58
    //    B5-2 — so tests write catalog rows straight to the DB, the same convention ApiTestBase already
    //    uses for CreateTestPlanConfigAsync) ──────────────────────────────────────────────────────────

    private Task InvalidatePricingCacheAsync()
    {
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PricingCatalogCache>().Invalidate();
        return Task.CompletedTask;
    }

    private async Task SetPublicationEnabledAsync(bool? enabled)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.PlatformSettings.FindAsync(PricingCatalogCache.PublicEnabledSettingKey);
        if (enabled is null)
        {
            if (row is not null) db.PlatformSettings.Remove(row);
        }
        else if (row is null)
        {
            db.PlatformSettings.Add(new PlatformSetting
            {
                Key = PricingCatalogCache.PublicEnabledSettingKey,
                Value = enabled.Value ? "true" : "false",
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            row.Value = enabled.Value ? "true" : "false";
        }
        await db.SaveChangesAsync();
        await InvalidatePricingCacheAsync();
    }

    private async Task<Guid> CreatePlanAsync(
        string? name = null, decimal price = 990, bool isPublic = true, bool isActive = true,
        int sortOrder = 0, int? maxCompanies = 1, int? maxEmployees = 1, string? highlights = null,
        bool isSystemFree = false, string? description = "Описание тарифа")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var plan = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(),
            Name = name ?? Unique("Plan "),
            Description = description,
            PricePerMonth = price,
            MaxCompanies = maxCompanies,
            MaxEmployees = maxEmployees,
            IsPublic = isPublic,
            IsActive = isActive,
            SortOrder = sortOrder,
            Highlights = highlights,
            IsSystemFree = isSystemFree,
            CreatedAt = DateTime.UtcNow,
        };
        db.SubscriptionPlanConfigs.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private async Task<Guid> CreateOptionAsync(
        string? name = null, OptionKind kind = OptionKind.Quantity, decimal? price = 490,
        bool isPublic = true, bool isActive = true, int sortOrder = 0, string? unitName = "компания",
        string? unitPriceText = null, string? code = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var option = new SubscriptionOption
        {
            Id = Guid.NewGuid(),
            Code = code ?? Unique("option-"),
            Name = name ?? Unique("Option "),
            Kind = kind,
            PricePerMonth = price,
            UnitName = kind == OptionKind.Quantity ? unitName : null,
            UnitPriceText = unitPriceText,
            IsPublic = isPublic,
            IsActive = isActive,
            SortOrder = sortOrder,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.SubscriptionOptions.Add(option);
        await db.SaveChangesAsync();
        return option.Id;
    }

    // Removes ALL plan/option rows so a test can assert exact catalog contents without other tests'
    // (non-public, so normally invisible) rows or a previous PricingTests run's rows interfering with
    // count-based assertions. Safe: nothing else in the "Api" collection reads these tables by content
    // (grep confirms SubscriptionOption is only touched here and in PricingCatalogBuilder), and other
    // suites' plan configs are recreated on demand per-test via ApiTestBase, not shared by identity.
    private async Task ResetCatalogAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"SubscriptionOptions\"");
        // NOTE: AccountSubscription.PlanConfigId is nullable (SetNull on delete) — a plain
        // "NOT IN (SELECT PlanConfigId FROM AccountSubscriptions)" would evaluate to UNKNOWN (i.e.
        // delete nothing at all) for every row the moment a single NULL shows up in that subquery,
        // which happens routinely elsewhere in the suite. Filtering NULLs out of both sides avoids
        // that classic SQL NOT-IN-with-NULLs trap.
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"SubscriptionPlanConfigs\" WHERE \"Id\" NOT IN " +
            "(SELECT \"PlanConfigId\" FROM \"AccountSubscriptions\" WHERE \"PlanConfigId\" IS NOT NULL)");
        await InvalidatePricingCacheAsync();
    }

    private async Task<PublicPricingDto> GetPublicPricingOkAsync()
    {
        var response = await AnonymousClient().GetAsync("/api/pricing");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadJsonAsync<PublicPricingDto>())!;
    }

    // ── US-71 / §39: GET /api/pricing — publication switch ─────────────────────────────────────────

    [Fact, TestCase("PRC-001")]
    public async Task GetPublicPricing_SettingAbsent_Returns404NotFound()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(null); // absence convention (§48, same as ChannelPricePerMonthKey)
        await CreatePlanAsync(); // even with public data present, the switch gates it
        await InvalidatePricingCacheAsync();

        var response = await AnonymousClient().GetAsync("/api/pricing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "§39: absence of pricing.public-enabled must read as 'not published', not as an empty catalog");
    }

    [Fact, TestCase("PRC-002")]
    public async Task GetPublicPricing_SettingExplicitlyFalse_Returns404NotFound()
    {
        await ResetCatalogAsync();
        await CreatePlanAsync();
        await SetPublicationEnabledAsync(false);

        var response = await AnonymousClient().GetAsync("/api/pricing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("PRC-003")]
    public async Task GetPublicPricing_Returns404WithEmptyBody_NotAnEmptyArray()
    {
        // §48: "чтобы посторонний не мог понять, что каталог вообще существует" — must not leak an
        // empty pricing JSON array/object shape (e.g. {"plans":[],"options":[]}) that would let a
        // caller distinguish "disabled" from "enabled but empty". [ApiController]'s automatic
        // ProblemDetails wrapper for a bare NotFound() (the same shape every other 404 in this app
        // returns, see e.g. CompaniesTests.GetBySlug_UnknownSlug_ReturnsNotFound) is the framework's
        // standard 404 body across the whole project, not pricing-specific leakage — §38.2's "тело:
        // пусто" is read here as "no pricing-shaped payload", not literally zero bytes.
        await ResetCatalogAsync();
        await CreatePlanAsync(name: "Не должно просочиться");
        await SetPublicationEnabledAsync(null);

        var response = await AnonymousClient().GetAsync("/api/pricing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("plans");
        body.Should().NotContain("options");
        body.Should().NotContain("Не должно просочиться");
    }

    [Fact, TestCase("PRC-004")]
    public async Task GetPublicPricing_Enabled_ButNoPublicRows_Returns200WithEmptyArrays()
    {
        // Publication being ON and the catalog being empty are different states from publication being
        // OFF — §39 only documents 404 for the switch itself.
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);

        var response = await AnonymousClient().GetAsync("/api/pricing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadJsonAsync<PublicPricingDto>();
        dto.Should().NotBeNull();
        dto!.Plans.Should().BeEmpty();
        dto.Options.Should().BeEmpty();
    }

    // ── US-71 п.4/US-66: filtering ───────────────────────────────────────────────────────────────

    [Fact, TestCase("PRC-005")]
    public async Task GetPublicPricing_ExcludesNonPublicPlan()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync(name: "Публичный", isPublic: true);
        await CreatePlanAsync(name: "Индивидуальный", isPublic: false);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Plans.Should().ContainSingle().Which.Name.Should().Be("Публичный");
    }

    [Fact, TestCase("PRC-006")]
    public async Task GetPublicPricing_ExcludesInactivePlan()
    {
        // §48: снятый с продажи тариф (IsActive=false) исчезает из прайса (US-66).
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync(name: "Активный", isActive: true);
        await CreatePlanAsync(name: "Снят с продажи", isActive: false, isPublic: true);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Plans.Should().ContainSingle().Which.Name.Should().Be("Активный");
    }

    [Fact, TestCase("PRC-007")]
    public async Task GetPublicPricing_ExcludesOptionWithoutPrice()
    {
        // US-66: "Given опция без цены, Then она не предлагается ни одному владельцу и не показывается
        // на публичной странице" — absence-of-price convention carried over from cycle 4.
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreateOptionAsync(name: "С ценой", price: 490);
        await CreateOptionAsync(name: "Без цены", price: null);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Options.Should().ContainSingle().Which.Name.Should().Be("С ценой");
    }

    [Fact, TestCase("PRC-008")]
    public async Task GetPublicPricing_ExcludesNonPublicOption()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreateOptionAsync(name: "Публичная", isPublic: true);
        await CreateOptionAsync(name: "Непубличная", isPublic: false);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Options.Should().ContainSingle().Which.Name.Should().Be("Публичная");
    }

    [Fact, TestCase("PRC-009")]
    public async Task GetPublicPricing_ExcludesInactiveOption()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreateOptionAsync(name: "Активная", isActive: true);
        await CreateOptionAsync(name: "Деактивированная", isActive: false, isPublic: true);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Options.Should().ContainSingle().Which.Name.Should().Be("Активная");
    }

    // ── US-71 п.2/п.4: shape of a plan/option row ───────────────────────────────────────────────

    [Fact, TestCase("PRC-010")]
    public async Task GetPublicPricing_FreePlan_IsFreeTrue_WithBaseLimits()
    {
        // П4: бесплатный тариф публикуется обычной строкой с лимитами 1 компания / 1 сотрудник.
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync(name: "Бесплатный", price: 0, maxCompanies: 1, maxEmployees: 1, isSystemFree: true);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        var free = dto.Plans.Should().ContainSingle().Subject;
        free.IsFree.Should().BeTrue();
        free.PricePerMonth.Should().Be(0);
        free.IncludedCompanies.Should().Be(1);
        free.IncludedEmployees.Should().Be(1);
    }

    [Fact, TestCase("PRC-011")]
    public async Task GetPublicPricing_PaidPlan_IsFreeFalse()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync(name: "Базовый", price: 1490, isSystemFree: false);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Plans.Should().ContainSingle().Which.IsFree.Should().BeFalse();
    }

    [Fact, TestCase("PRC-012")]
    public async Task GetPublicPricing_NoIncludedLimit_IsNullMeaningUnlimited()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync(name: "Профи", maxCompanies: null, maxEmployees: null);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        var plan = dto.Plans.Should().ContainSingle().Subject;
        plan.IncludedCompanies.Should().BeNull();
        plan.IncludedEmployees.Should().BeNull();
    }

    [Fact, TestCase("PRC-013")]
    public async Task GetPublicPricing_PlanHighlights_SplitFromNewlineSeparatedField()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync(name: "Базовый", highlights: "Онлайн-запись 24/7\nОтчёты и аналитика\nДо 5 сотрудников");
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Plans.Should().ContainSingle().Which.Highlights.Should().Equal(
            "Онлайн-запись 24/7", "Отчёты и аналитика", "До 5 сотрудников");
    }

    [Fact, TestCase("PRC-014")]
    public async Task GetPublicPricing_QuantityOption_UnitPriceTextIsNotBare_ContainsUnit()
    {
        // US-71 п.3: "Формулировок вида «опция рассылок» без единицы на странице быть не должно" —
        // whether the admin authored the phrase or the server falls back to a generic one, the unit
        // must be present in the printed string.
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreateOptionAsync(name: "Дополнительная компания", kind: OptionKind.Quantity,
            price: 490, unitName: "компания", unitPriceText: null);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        var option = dto.Options.Should().ContainSingle().Subject;
        option.UnitPriceText.Should().NotBeNullOrWhiteSpace();
        option.UnitPriceText.Should().Contain("компания");
        option.UnitPriceText.Should().Contain("490");
    }

    [Fact, TestCase("PRC-015")]
    public async Task GetPublicPricing_QuantityOption_UsesAdminAuthoredUnitPriceTextVerbatim()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreateOptionAsync(name: "Рассылки в WhatsApp", kind: OptionKind.Quantity, price: 690,
            unitName: "номер", unitPriceText: "номер для рассылок — 690 ₽/мес");
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Options.Should().ContainSingle().Which.UnitPriceText.Should().Be("номер для рассылок — 690 ₽/мес");
    }

    [Fact, TestCase("PRC-016")]
    public async Task GetPublicPricing_ResponseHasCurrencyRub()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Currency.Should().Be("RUB");
    }

    [Fact, TestCase("PRC-017")]
    public async Task GetPublicPricing_ResponseHasNoticeAboutAdminOnlyActivation()
    {
        // US-71: "На странице явно написано, что подключение происходит через администратора платформы"
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Notice.Should().NotBeNullOrWhiteSpace();
        dto.Notice.Should().Contain("администратор");
    }

    [Fact, TestCase("PRC-018")]
    public async Task GetPublicPricing_LegalNoticeAbsent_UntilExplicitlySet()
    {
        // §39: "поле legalNotice добавляется после вычитки юристом; до тех пор поля нет" — SPEC §7
        // confirms the legal text hasn't been written yet in this cycle.
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.LegalNotice.Should().BeNull();
    }

    [Fact, TestCase("PRC-032")]
    public async Task GetPublicPricing_SameSortOrder_TieBreaksByPrice_AscendingNotByName()
    {
        // §39: "Порядок — по sortOrder, затем по цене." Names are picked so that alphabetical order
        // and price order disagree, so this only passes if the tie-break is really price-based.
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync(name: "Zeta (дешевле)", price: 100, sortOrder: 0);
        await CreatePlanAsync(name: "Alpha (дороже)", price: 500, sortOrder: 0);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Plans.Select(p => p.Name).Should().Equal("Zeta (дешевле)", "Alpha (дороже)");
    }

    [Fact, TestCase("PRC-033")]
    public async Task GetPublicPricing_SameSortOrder_OptionsTieBreakByPrice_AscendingNotByName()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreateOptionAsync(name: "Zeta option (дешевле)", price: 100, sortOrder: 0);
        await CreateOptionAsync(name: "Alpha option (дороже)", price: 500, sortOrder: 0);
        await InvalidatePricingCacheAsync();

        var dto = await GetPublicPricingOkAsync();

        dto.Options.Select(o => o.Name).Should().Equal("Zeta option (дешевле)", "Alpha option (дороже)");
    }

    // ── §39: internal fields must never leak on the public/preview surface ─────────────────────────

    [Fact, TestCase("PRC-019")]
    public async Task GetPublicPricing_RawJson_NeverExposesInternalFields()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync(name: "Базовый");
        await CreateOptionAsync(name: "Опция");
        await InvalidatePricingCacheAsync();

        var response = await AnonymousClient().GetAsync("/api/pricing");
        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().NotContainEquivalentOf("capabilityKey");
        raw.Should().NotContainEquivalentOf("isActive");
        raw.Should().NotContainEquivalentOf("isPublic");
        raw.Should().NotContainEquivalentOf("includedQuantity");
        raw.Should().NotContainEquivalentOf("photoRetention");
        raw.Should().NotContainEquivalentOf("\"code\"");
        raw.Should().NotContainEquivalentOf("maxQuantity");
    }

    // ── §6/§39: caching, ETag, Cache-Control ─────────────────────────────────────────────────────

    [Fact, TestCase("PRC-020")]
    public async Task GetPublicPricing_SetsETagAndCacheControlHeaders()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await InvalidatePricingCacheAsync();

        var response = await AnonymousClient().GetAsync("/api/pricing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact, TestCase("PRC-021")]
    public async Task GetPublicPricing_IfNoneMatchWithCurrentETag_Returns304WithEmptyBody()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync();
        await InvalidatePricingCacheAsync();

        var first = await AnonymousClient().GetAsync("/api/pricing");
        var etag = first.Headers.ETag!.ToString();

        var client = AnonymousClient();
        client.DefaultRequestHeaders.Add("If-None-Match", etag);
        var second = await client.GetAsync("/api/pricing");

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await second.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact, TestCase("PRC-022")]
    public async Task GetPublicPricing_IfNoneMatchWithStaleETag_Returns200WithFreshBody()
    {
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync(name: "До правки");
        await InvalidatePricingCacheAsync();
        var before = await AnonymousClient().GetAsync("/api/pricing");
        var staleEtag = before.Headers.ETag!.ToString();

        await CreatePlanAsync(name: "После правки");
        await InvalidatePricingCacheAsync(); // simulates the admin write endpoint's invalidation call

        var client = AnonymousClient();
        client.DefaultRequestHeaders.Add("If-None-Match", staleEtag);
        var after = await client.GetAsync("/api/pricing");

        after.StatusCode.Should().Be(HttpStatusCode.OK);
        after.Headers.ETag!.ToString().Should().NotBe(staleEtag);
    }

    [Fact, TestCase("PRC-023")]
    public async Task GetPublicPricing_Version_MatchesETag()
    {
        // §39: "version совпадает со значением внутри ETag"
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await InvalidatePricingCacheAsync();

        var response = await AnonymousClient().GetAsync("/api/pricing");
        var etag = response.Headers.ETag!.ToString().Trim('"').Replace("W/", "").Trim('"');
        var dto = await response.Content.ReadJsonAsync<PublicPricingDto>();

        etag.Should().Contain(dto!.Version);
    }

    [Fact, TestCase("PRC-024")]
    public async Task GetPublicPricing_WithinCacheWindow_DoesNotReflectDbChangeUntilInvalidated()
    {
        // US-72 п.2 promises "не позже, чем через минуту" via cache invalidation on write — this proves
        // the other side of that promise: within the 60s window, and WITHOUT an explicit invalidation,
        // stale data is served (i.e. the cache genuinely caches, rather than the endpoint hitting the
        // DB on every request only "by accident" always looking fresh).
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        var planId = await CreatePlanAsync(name: "Исходное имя");
        await InvalidatePricingCacheAsync();
        var first = await GetPublicPricingOkAsync();
        first.Plans.Should().ContainSingle().Which.Name.Should().Be("Исходное имя");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Targeted by Id, not FirstAsync(): ResetCatalogAsync() only removes plans that aren't
            // referenced by some other test's AccountSubscription, so leftover rows from earlier tests
            // in the full suite run can still be present here, and an unfiltered FirstAsync() would
            // non-deterministically pick one of those instead of the plan this test just created.
            var plan = await db.SubscriptionPlanConfigs.SingleAsync(p => p.Id == planId);
            plan.Name = "Новое имя (мимо кеша)";
            await db.SaveChangesAsync();
        }
        // Deliberately no InvalidatePricingCacheAsync() call here.

        var second = await GetPublicPricingOkAsync();
        second.Plans.Should().ContainSingle().Which.Name.Should().Be("Исходное имя",
            "цикл 5 полагается на 60-секундный кеш (§6/§48), пока цикл не истёк, обычный HTTP-запрос " +
            "не должен видеть только что записанное в БД изменение");
    }

    // ── US-72 / §40: GET /api/admin/pricing/preview ─────────────────────────────────────────────

    [Fact, TestCase("PRC-025")]
    public async Task AdminPreview_Anonymous_ReturnsUnauthorized()
    {
        var response = await AnonymousClient().GetAsync("/api/admin/pricing/preview");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("PRC-026")]
    public async Task AdminPreview_AsCompanyOwner_ReturnsForbidden()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var response = await AuthedClient(owner.Token).GetAsync("/api/admin/pricing/preview");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("PRC-027")]
    public async Task AdminPreview_AsPlainClient_ReturnsForbidden()
    {
        var user = await RegisterAsync();
        var response = await AuthedClient(user.Token).GetAsync("/api/admin/pricing/preview");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("PRC-028")]
    public async Task AdminPreview_AsSuperAdmin_ReturnsOk_EvenWhenPublicationDisabled()
    {
        // §40: "игнорирует рубильник публикации (всегда 200)" — this is the whole point of the
        // preview endpoint: showing a draft that isn't live yet.
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(null);
        await CreatePlanAsync(name: "Черновик тарифа");
        await InvalidatePricingCacheAsync();

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/pricing/preview");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadJsonAsync<PublicPricingDto>();
        dto!.Plans.Should().ContainSingle().Which.Name.Should().Be("Черновик тарифа");
    }

    [Fact, TestCase("PRC-029")]
    public async Task AdminPreview_AppliesSamePublicityFilters_AsThePublicEndpoint()
    {
        // §40: "фильтры публичности применяются те же — предпросмотр показывает ровно то, что увидит
        // посетитель".
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await CreatePlanAsync(name: "Публичный", isPublic: true);
        await CreatePlanAsync(name: "Индивидуальный", isPublic: false);
        await InvalidatePricingCacheAsync();

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/pricing/preview");
        var dto = await response.Content.ReadJsonAsync<PublicPricingDto>();

        dto!.Plans.Should().ContainSingle().Which.Name.Should().Be("Публичный");
    }

    [Fact, TestCase("PRC-030")]
    public async Task AdminPreview_DoesNotSetETagHeader()
    {
        // §40: "без кеша и ETag (администратор должен видеть правку мгновенно)".
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        await InvalidatePricingCacheAsync();

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/pricing/preview");

        response.Headers.ETag.Should().BeNull();
    }

    [Fact, TestCase("PRC-031")]
    public async Task AdminPreview_ReflectsAJustMadeChange_WithoutWaitingForPublicCacheToExpire()
    {
        // §40: "администратор должен видеть правку мгновенно" is the explicit reason the preview
        // endpoint exists as a separate route from GET /api/pricing rather than SuperAdmin just reusing
        // it. This scenario is the realistic admin workflow: edit a plan, immediately check the
        // preview screen — no one manually calls an internal cache-invalidation hook in between.
        await ResetCatalogAsync();
        await SetPublicationEnabledAsync(true);
        var planId = await CreatePlanAsync(name: "До правки");
        await InvalidatePricingCacheAsync();

        // Populates the shared 60s cache exactly like a visitor hitting the public homepage would.
        await GetPublicPricingOkAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Targeted by Id, not FirstAsync() — see the identical note in PRC-024 above: leftover
            // plan rows from earlier tests in a full-suite run make an unfiltered FirstAsync()
            // non-deterministic.
            var plan = await db.SubscriptionPlanConfigs.SingleAsync(p => p.Id == planId);
            plan.Name = "После правки";
            await db.SaveChangesAsync();
        }

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/pricing/preview");
        var dto = await response.Content.ReadJsonAsync<PublicPricingDto>();

        dto!.Plans.Should().ContainSingle().Which.Name.Should().Be("После правки",
            "§40 обещает мгновенное отражение правки в предпросмотре независимо от того, прогрет ли " +
            "уже публичный 60-секундный кеш; PricingController.GetPricingPreview делегирует в тот же " +
            "PricingCatalogCache.GetAsync, что и публичный эндпоинт, так что это переиспользование " +
            "кеша, а не отдельное 'всегда свежее' чтение");
    }
}
