using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 11 (SPEC.md US-11-04, US-11-11; ARCHITECTURE_CYCLE11.md §102.7/§102.10, Q7/Q11) — written
/// from SPEC.md's acceptance criteria independently of AdminController's/PricingCatalogCache's own
/// implementation, per this cycle's QA brief ("Вызов 2"). Covers the one piece of the cycle that is a
/// genuine runtime behaviour change: the public pricing switch and the notifications.whatsapp option are
/// now wired to whether TermsOwner (the channel-offer carrier document, §102.2) is a draft.
///
/// Uses its own <see cref="LegalDocumentsTestFactory"/> instance (own DB, own Legal:Root) so TermsOwner's
/// draft/published state can be flipped without touching the shared "Api" collection's fixed manifest,
/// on which every other pricing/billing test depends for a stable, always-published TermsOwner.
/// </summary>
public class LegalPricingGateTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;

    public LegalPricingGateTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        fixture.RecordTestClass(nameof(LegalPricingGateTests));
    }

    private LegalDocumentsTestFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new LegalDocumentsTestFactory(_fixture.ConnectionString);
        _ = _factory.Services;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Anon() => _factory.CreateClient();

    private HttpClient Authed(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // TestHostSettings' "legal" factoryTag pins this phone/password (ARCHITECTURE_CYCLE8.md §71.1) —
    // this is the same SuperAdmin LegalDocumentsTestFactory's own doc comment says no LEG- test in this
    // assembly has logged in as before; unique to this factory tag, so it never races another
    // collection's SuperAdmin seeding.
    // Logs in, then accepts whatever Privacy/TermsClient version is CURRENTLY live (this class's tests
    // bump those versions via WriteManifest to control TermsOwner independently of Privacy/TermsClient,
    // but every WriteManifest call still writes a fresh Material version for the two Global-gated
    // documents — see LegalDocumentsTestFactory.WriteManifest's own doc comment). Without this, every
    // admin-authenticated request after the first WriteManifest call in a test would 451 on the
    // Global consent gate before ever reaching the endpoint under test — a problem specific to this
    // SuperAdmin's ONE-TIME seeding at host boot (LegalDocumentsTestFactory's own doc comment), not
    // something any other LEG- test in this assembly needs to work around (they only ever check
    // Privacy/TermsClient's OWN gate, so a mismatch there is the point of the test, not a testing
    // obstacle). Mirrors LegalConsentVersionChangeTests' own accept-then-swap-token pattern.
    private async Task<string> LoginAsSuperAdminAsync()
    {
        var loginResponse = await Anon().PostAsJsonAsync("/api/auth/login",
            new LoginDto("+70000099999", "SuperAdmin123!"));
        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        return await AcceptCurrentLegalAsync(loginBody!.Token);
    }

    // Call again after every mid-test WriteManifest — each call writes a fresh Material version for
    // Privacy/TermsClient too (see WriteManifest's own doc comment), which re-arms the 451 gate for
    // whatever token the caller was holding.
    private async Task<string> AcceptCurrentLegalAsync(string token)
    {
        var docs = await (await Anon().GetAsync("/api/legal/documents")).Content.ReadFromJsonAsync<LegalManifestDto>();
        var privacy = docs!.Documents.First(d => d.Type == "Privacy").Version;
        var terms = docs.Documents.First(d => d.Type == "TermsClient").Version;

        var acceptResponse = await Authed(token).PostAsJsonAsync("/api/legal/accept",
            new AcceptLegalRequestDto([new AcceptLegalItemDto("Privacy", privacy), new AcceptLegalItemDto("TermsClient", terms)]));
        acceptResponse.EnsureSuccessStatusCode();
        var accepted = await acceptResponse.Content.ReadFromJsonAsync<AcceptLegalResponseDto>();
        return accepted!.Token;
    }

    private async Task SetPublicationEnabledDirectlyAsync(bool enabled)
    {
        // Bypasses the admin endpoint on purpose — simulates the DB row being flipped by something
        // other than PUT /api/admin/platform-settings (migration, manual restore of a dump), which is
        // exactly the scenario §102.7/Q7's "invariant on the read path" clause exists to cover.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.PlatformSettings.FindAsync("pricing.public-enabled");
        if (row is null)
        {
            db.PlatformSettings.Add(new PlatformSetting
            {
                Key = "pricing.public-enabled", Value = enabled ? "true" : "false", UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            row.Value = enabled ? "true" : "false";
        }
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreatePublicPlanAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var plan = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(), Name = "LEG-GATE-" + Guid.NewGuid().ToString("N")[..8],
            Description = "test", PricePerMonth = 500, MaxCompanies = 1, MaxEmployees = 1,
            IsPublic = true, IsActive = true, SortOrder = 0, CreatedAt = DateTime.UtcNow,
        };
        db.SubscriptionPlanConfigs.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    // `notifications.whatsapp` already exists as a row seeded by migration 20260922121140_SeedBillingCatalog
    // (§B9: seeded with no price and IsPublic=false, on purpose, so nothing sells it until an operator
    // both prices it AND — as of this cycle — publishes TermsOwner). Reusing/updating that row rather than
    // inserting a second one with the same Code (unique index IX_SubscriptionOptions_Code).
    private async Task<string> MakeWhatsAppOptionPubliclySellableAsync()
    {
        var name = "LEG-GATE-whatsapp-" + Guid.NewGuid().ToString("N")[..8];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var option = await db.SubscriptionOptions.SingleAsync(o => o.Code == "notifications.whatsapp");
        option.Name = name;
        option.PricePerMonth = 100;
        option.IsPublic = true;
        option.IsActive = true;
        option.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return name;
    }

    // ── US-11-04 AC2: PUT /api/admin/platform-settings rejects enabling the switch while the offer is a draft ──

    [Fact, TestCase("LEG-038")]
    public async Task PutPlatformSettings_EnablePricingWithDraftOffer_Returns409AndLeavesSwitchOff()
    {
        // This class shares one database across all five tests in the class (TestDatabaseFixture is
        // IClassFixture-scoped, not per-test — see LegalDocumentsTestFactory's own doc comment), and
        // xUnit's RandomTestCaseOrderer (AssemblyInfo.cs) means LEG-039/LEG-040 — which both flip
        // pricing.public-enabled to true — may run BEFORE this test. UpdatePlatformSettings only runs the
        // draft-offer gate when the switch is transitioning off->on (`!oldPricingPublicEnabled`); if a
        // previous test already left it on, this PUT becomes a same-value no-op that short-circuits the
        // gate and returns 200, not 409. Pin the precondition explicitly rather than assume run order.
        await SetPublicationEnabledDirectlyAsync(false);
        _factory.WriteManifest("v1-draft", isDraft: true, changeKind: "Material", termsOwnerIsDraft: true, termsOwnerVersion: "owner-1-draft");
        await Task.Delay(2500); // > Legal:ReloadSeconds (1s); wider margin than LegalConsentVersionChangeTests's 1200 — this file's hosts are freshly booted per test (not reused), so boot jitter under load eats into the margin
        var token = await LoginAsSuperAdminAsync();

        var response = await Authed(token).PutAsJsonAsync("/api/admin/platform-settings",
            new AdminPlatformSettingsDto(null, 30, PricingPublicEnabled: true));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "US-11-04 AC2: enabling the public price list while TermsOwner (the channel-offer carrier) is a draft must be rejected, not accepted silently");
        var body = await response.Content.ReadFromJsonAsync<PricingPublicationBlockedDto>();
        body!.Reason.Should().Be("OfferIsDraft");
        body.DocumentType.Should().Be("TermsOwner");

        var get = await Authed(token).GetFromJsonAsync<AdminPlatformSettingsDto>("/api/admin/platform-settings");
        get!.PricingPublicEnabled.Should().BeFalse("a rejected PUT must not move the switch");
    }

    // ── US-11-04 AC3: publishing the offer lets the switch turn on normally ──

    [Fact, TestCase("LEG-039")]
    public async Task PutPlatformSettings_EnablePricingWithPublishedOffer_Returns200AndCatalogBecomesVisible()
    {
        _factory.WriteManifest("v1-draft", isDraft: true, changeKind: "Material", termsOwnerIsDraft: false);
        await Task.Delay(2500); // > Legal:ReloadSeconds (1s); wider margin than LegalConsentVersionChangeTests's 1200 — this file's hosts are freshly booted per test (not reused), so boot jitter under load eats into the margin
        var token = await LoginAsSuperAdminAsync();
        await CreatePublicPlanAsync();

        var response = await Authed(token).PutAsJsonAsync("/api/admin/platform-settings",
            new AdminPlatformSettingsDto(null, 30, PricingPublicEnabled: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "with TermsOwner published, enabling the switch is an ordinary admin action");

        var pricing = await Anon().GetAsync("/api/pricing");
        pricing.StatusCode.Should().Be(HttpStatusCode.OK,
            "US-11-04 AC3: once the offer is published, the storefront cycle 7 built works exactly as before");
    }

    // ── Q7 second half: an invariant on the READ path, not just at the moment the switch is flipped ──

    [Fact, TestCase("LEG-040")]
    public async Task GetPublicPricing_SwitchOnAtDbLevelButOfferDraft_Returns404NotOk()
    {
        _factory.WriteManifest("v1-draft", isDraft: true, changeKind: "Material", termsOwnerIsDraft: true, termsOwnerVersion: "owner-1-draft");
        await Task.Delay(2500); // > Legal:ReloadSeconds (1s); wider margin than LegalConsentVersionChangeTests's 1200 — this file's hosts are freshly booted per test (not reused), so boot jitter under load eats into the margin
        await CreatePublicPlanAsync();
        await SetPublicationEnabledDirectlyAsync(true); // simulates a dump restore / manual DB edit

        var response = await Anon().GetAsync("/api/pricing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "ARCHITECTURE_CYCLE11.md §102.7 Q7: the switch being 'on' in the database must never be enough by itself — " +
            "the storefront must stay hidden for as long as TermsOwner is a draft, however the switch got flipped");
    }

    // ── US-11-11 / Q11: notifications.whatsapp is not publicly sellable while its required document is a draft ──

    [Fact, TestCase("LEG-041")]
    public async Task AdminPricingPreview_WhatsAppOption_HiddenWhileTermsOwnerDraft_VisibleOncePublished()
    {
        _factory.WriteManifest("v1-draft", isDraft: true, changeKind: "Material", termsOwnerIsDraft: true, termsOwnerVersion: "owner-1-draft");
        await Task.Delay(2500); // > Legal:ReloadSeconds (1s); wider margin than LegalConsentVersionChangeTests's 1200 — this file's hosts are freshly booted per test (not reused), so boot jitter under load eats into the margin
        var token = await LoginAsSuperAdminAsync();
        var optionName = await MakeWhatsAppOptionPubliclySellableAsync();

        var draftPreview = await Authed(token).GetFromJsonAsync<PublicPricingDto>("/api/admin/pricing/preview");
        draftPreview!.Options.Should().NotContain(o => o.Name == optionName,
            "US-11-11/Q11: an option gated on TermsOwner must not be offered for sale while that document is a draft");

        _factory.WriteManifest("v2-draft", isDraft: true, changeKind: "Material", termsOwnerIsDraft: false);
        await Task.Delay(2500); // > Legal:ReloadSeconds (1s); wider margin than LegalConsentVersionChangeTests's 1200 — this file's hosts are freshly booted per test (not reused), so boot jitter under load eats into the margin
        token = await AcceptCurrentLegalAsync(token);

        var publishedPreview = await Authed(token).GetFromJsonAsync<PublicPricingDto>("/api/admin/pricing/preview");
        publishedPreview!.Options.Should().Contain(o => o.Name == optionName,
            "once TermsOwner is published, the same option must reappear without any other admin action");
    }

    // ── US-11-07: the readiness endpoint reflects the actual draft/published state of the snapshot it reads ──

    [Fact, TestCase("LEG-042")]
    public async Task GetLegalReadiness_ReflectsCurrentDraftState_AndFlipsWhenOfferIsPublished()
    {
        _factory.WriteManifest("v1-draft", isDraft: true, changeKind: "Material", termsOwnerIsDraft: true, termsOwnerVersion: "owner-1-draft");
        await Task.Delay(2500); // > Legal:ReloadSeconds (1s); wider margin than LegalConsentVersionChangeTests's 1200 — this file's hosts are freshly booted per test (not reused), so boot jitter under load eats into the margin
        var token = await LoginAsSuperAdminAsync();

        var draft = await Authed(token).GetFromJsonAsync<LegalReadinessDto>("/api/admin/legal/readiness");
        draft!.Ready.Should().BeFalse("Privacy/TermsClient and TermsOwner are all drafts in this manifest");
        draft.Documents.Single(d => d.Type == "TermsOwner").IsDraft.Should().BeTrue();
        draft.Blockers.Should().Contain(b => b.Kind == "DraftDocuments");

        _factory.WriteManifest("v2-published", isDraft: false, changeKind: "Material", termsOwnerIsDraft: false);
        await Task.Delay(2500); // > Legal:ReloadSeconds (1s); wider margin than LegalConsentVersionChangeTests's 1200 — this file's hosts are freshly booted per test (not reused), so boot jitter under load eats into the margin
        token = await AcceptCurrentLegalAsync(token);

        var afterPublish = await Authed(token).GetFromJsonAsync<LegalReadinessDto>("/api/admin/legal/readiness");
        afterPublish!.Documents.Single(d => d.Type == "TermsOwner").IsDraft.Should().BeFalse(
            "US-11-07: the readiness report must read the live snapshot, not a cached/stale one");
    }
}
