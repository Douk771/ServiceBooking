using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// US-64/US-65/US-67 (SPEC.md §4.1-4.4, ARCHITECTURE_CYCLE5.md §43-46, API_CONTRACT_CYCLE5.md §39-40) —
/// the five-document manifest, the two-call registration split (Privacy/TermsClient block; PdnConsent
/// does not), and the guest-booking path that carries no consent at all. Written from SPEC.md/
/// API_CONTRACT_CYCLE5.md directly, not from the controller implementation.
///
/// QA CYCLE5 rewrite: this file used to assert a two-document ("Privacy"/"Terms") model with a single
/// acceptedLegal boolean and a registration that BLOCKED without any consent at all. Both premises are
/// gone this cycle: there are five document types now (LegalDocumentType), and — per SPEC §59.2/
/// ARCHITECTURE_CYCLE5.md §46.2 — registration blocks ONLY on Privacy/TermsClient; PdnConsent is a
/// separate, non-blocking call. The assertions below are rewritten against that model.
/// </summary>
public class LegalConsentTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── GET /api/legal/documents — US-64 ────────────────────────────────────

    [Fact, TestCase("LEG-001")]
    public async Task GetDocuments_IsPublic_AndListsAllFiveTypes()
    {
        var response = await AnonymousClient().GetAsync("/api/legal/documents");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadJsonAsync<LegalManifestDto>();
        body!.Documents.Should().HaveCount(5);
        body.Documents.Select(d => d.Type).Should().BeEquivalentTo(
            ["Privacy", "TermsClient", "TermsOwner", "PdnConsent", "ChannelRiskNotice"]);
        body.Documents.Should().OnlyContain(d => d.Version != "" && d.EffectiveFrom != default);
        body.UiTexts.Should().HaveCount(6);
    }

    [Fact, TestCase("LEG-001b")]
    public async Task GetDocuments_PdnConsent_CarriesPurposesFromTheManifest()
    {
        // API_CONTRACT_CYCLE5.md §39.1: the frontend builds the registration/consents form's purpose
        // list from HERE, never from a hardcoded array — this pins that the field is actually populated.
        var body = await (await AnonymousClient().GetAsync("/api/legal/documents")).Content.ReadJsonAsync<LegalManifestDto>();
        var pdnConsent = body!.Documents.Single(d => d.Type == "PdnConsent");
        pdnConsent.Purposes.Should().NotBeNullOrEmpty();
        pdnConsent.Purposes.Should().Contain(p => p.Key == "ProviderDelivery");
        pdnConsent.Gate.Should().Be("None", "PdnConsent never blocks anything by itself (US-67 п.5, §59.2)");
    }

    [Fact, TestCase("LEG-002")]
    public async Task GetDocument_ByType_IsPublic_AndContainsHtmlWithDraftMarker()
    {
        var response = await AnonymousClient().GetAsync("/api/legal/documents/privacy");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadJsonAsync<LegalDocumentDto>();
        body!.Type.Should().Be("Privacy");
        body.ContentHtml.Should().NotBeNullOrWhiteSpace();
        // US-79 п.2: the draft marker survives inside the text itself, not only in the isDraft flag —
        // the seeded fixture document (ServiceBooking.API/App_Data/legal/legal.json) is currently a draft.
        if (body.IsDraft)
            body.ContentHtml.Should().Contain("Черновая редакция", "the draft banner must survive copy/print of the page, not only live in the isDraft flag");
    }

    [Fact, TestCase("LEG-003")]
    public async Task GetDocument_UnknownType_ReturnsNotFound()
    {
        var response = await AnonymousClient().GetAsync("/api/legal/documents/nonsense");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/auth/register — US-65, BREAKING № 2 ────────────────────────

    [Fact, TestCase("LEG-004")]
    public async Task Register_WithoutLegal_ReturnsBadRequest()
    {
        var response = await RegisterRawAsync(UniquePhone(), "Password123!", acceptedLegal: false);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("Consent");
    }

    [Fact, TestCase("LEG-004b")]
    public async Task Register_LegalOmittedFromJson_ReturnsBadRequest()
    {
        // The contract (API_CONTRACT_CYCLE5.md §40.1/§40.3) says a MISSING `legal` object must be
        // treated as incomplete, not silently pass — sent as a raw JSON object with the field entirely
        // absent, bypassing the C# default parameter RegisterRawAsync's typed call would otherwise apply.
        var client = AnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Т", lastName = "Т", phone = UniquePhone(), password = "Password123!"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // 🔴 SPEC §59.2 / ARCHITECTURE_CYCLE5.md §46.2, §55.1 R1: the legal opinion overrides SPEC's literal
    // wording here — registration blocks ONLY on Privacy/TermsClient. There must be NO test asserting
    // that registration fails without PdnConsent; the opposite must hold instead.
    [Fact, TestCase("LEG-005")]
    public async Task Register_WithPrivacyAndTermsClientOnly_Succeeds_WithoutAnyPdnConsent()
    {
        var user = await RegisterAsync(); // ApiTestBase.RegisterAsync never touches PdnConsent at all

        var status = await (await AuthedClient(user.Token).GetAsync("/api/legal/consent-status"))
            .Content.ReadJsonAsync<ConsentStatusDto>();
        status!.RequiresAcceptance.Should().BeFalse();
        // Every gate-bearing document (Global AND OwnerScope) shows up here — only PdnConsent/
        // ChannelRiskNotice (gate: None) are deliberately absent (§39.4). TermsOwner is included even
        // for a non-owner (AcceptedVersion is simply null for it — the "missing claim ≠ stale claim"
        // distinction the controller's own В1 code-review note describes).
        status.Documents.Select(d => d.Type).Should().BeEquivalentTo(["Privacy", "TermsClient", "TermsOwner"]);
        status.Documents.Single(d => d.Type == "TermsOwner").AcceptedVersion.Should().BeNull("this account never created a company");
        status.Documents.Where(d => d.Type != "TermsOwner").Should().OnlyContain(d => d.AcceptedVersion == d.CurrentVersion);

        var consents = await (await AuthedClient(user.Token).GetAsync("/api/profile/consents"))
            .Content.ReadJsonAsync<ConsentsDto>();
        consents!.Granted.Should().BeEmpty("registration must not be able to grant PdnConsent by shape — §46.2");
    }

    [Fact, TestCase("LEG-005b")]
    public async Task Register_ThenBookAndAccessEverything_NeverNeedingPdnConsent()
    {
        // The service itself must not degrade for someone who registers and never touches
        // POST /api/profile/consents — SPEC §4.4 п.4 ("отказ от необязательного не закрывает доступ").
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(14, 0), null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        (await AuthedClient(clientUser.Token).GetAsync("/api/profile")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /api/legal/consent-status — US-64/US-65 ──────────────────────────

    [Fact, TestCase("LEG-006")]
    public async Task ConsentStatus_RequiresAuth()
    {
        var response = await AnonymousClient().GetAsync("/api/legal/consent-status");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/legal/accept — US-65 п.6 ───────────────────────────────────

    [Fact, TestCase("LEG-007")]
    public async Task Accept_WithStaleVersionInBody_ReturnsConflict()
    {
        var user = await RegisterAsync();
        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/legal/accept",
            new AcceptLegalRequestDto([new AcceptLegalItemDto("Privacy", "not-a-real-version"), new AcceptLegalItemDto("TermsClient", "also-not-real")]));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("LEG-008")]
    public async Task Accept_WithEmptyList_ReturnsBadRequest()
    {
        var user = await RegisterAsync();
        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/legal/accept",
            new AcceptLegalRequestDto([]));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("LEG-008b")]
    public async Task Accept_WithGateNoneDocument_ReturnsBadRequest()
    {
        // API_CONTRACT_CYCLE5.md §39.5: PdnConsent (gate: None) is accepted through a DIFFERENT endpoint
        // (POST /api/profile/consents) — this one must refuse it, not silently record it.
        var user = await RegisterAsync();
        var docs = await (await AnonymousClient().GetAsync("/api/legal/documents")).Content.ReadJsonAsync<LegalManifestDto>();
        var pdnVersion = docs!.Documents.First(d => d.Type == "PdnConsent").Version;

        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/legal/accept",
            new AcceptLegalRequestDto([new AcceptLegalItemDto("PdnConsent", pdnVersion)]));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("LEG-009")]
    public async Task Accept_WithCurrentVersions_ReturnsFreshToken()
    {
        var user = await RegisterAsync();
        var docs = await (await AnonymousClient().GetAsync("/api/legal/documents"))
            .Content.ReadJsonAsync<LegalManifestDto>();
        var privacy = docs!.Documents.First(d => d.Type == "Privacy").Version;
        var terms = docs.Documents.First(d => d.Type == "TermsClient").Version;

        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/legal/accept",
            new AcceptLegalRequestDto([new AcceptLegalItemDto("Privacy", privacy), new AcceptLegalItemDto("TermsClient", terms)]));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadJsonAsync<AcceptLegalResponseDto>();
        body!.Token.Should().NotBeNullOrWhiteSpace();
    }

    // ── Guest booking captures NO consent at all — US-65 п.4, §54 row 5 ──────

    [Fact, TestCase("LEG-010")]
    public async Task GuestBooking_RecordsNoticeVersionOnTheBooking_ButRequiresNoConsentDocument()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var response = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null,
            "Guest Consent", UniquePhone(), null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "US-67 п.6/§55.1 R7: the guest path is договорное basis — no consent document is required to book");
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.BookingNoticeVersion.Should().NotBeNullOrWhiteSpace("§46.2/§46.3: the server fills the ст.18 notice version it showed, from its own snapshot");
    }

    [Fact, TestCase("LEG-011")]
    public async Task AuthenticatedClientBooking_DoesNotCarryConsentFieldsOnTheBooking()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(12, 0), null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.ConsentPrivacyVersion.Should().BeNull();
        booking.ConsentTermsVersion.Should().BeNull();
        booking.ConsentAcceptedAt.Should().BeNull();
    }

    [Fact, TestCase("LEG-012")]
    public async Task ManualStaffBooking_DoesNotCarryConsentFieldsOnTheBooking()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(13, 0), null,
            "Walk-in Client", UniquePhone(), null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.ConsentPrivacyVersion.Should().BeNull();
        booking.ConsentTermsVersion.Should().BeNull();
    }

    // ── Concurrency: two simultaneous accepts from the same user — code review fix ──────────

    // Code review finding: POST /api/legal/accept upserts by (UserId, DocumentType) without any lock —
    // two concurrent calls from the same user (e.g. a double-click, or two tabs) could both read "no
    // existing row", both try to INSERT, and the second one dies on a race with an unhandled exception →
    // 500. CYCLE5: the (UserId, DocumentType) UNIQUE INDEX is gone — US-66 п.1 turned ConsentRecord into
    // an append-only journal, so the race this test used to force (deleting the rows to simulate a
    // pre-journal legacy account) no longer applies; two concurrent Accept calls simply insert two rows
    // and that is the CORRECT, not a degraded, outcome (US-66 п.7: "два последовательных акцепта дают
    // две строки"). What must still hold is: neither call ever 500s, and the resulting consent-status is
    // consistent (both now say "accepted, current version").
    [Fact, TestCase("LEG-036")]
    public async Task Accept_CalledTwiceInParallelBySameUser_NeitherCallReturnsServerError()
    {
        var user = await RegisterAsync();
        var docs = await (await AnonymousClient().GetAsync("/api/legal/documents"))
            .Content.ReadJsonAsync<LegalManifestDto>();
        var privacy = docs!.Documents.First(d => d.Type == "Privacy").Version;
        var terms = docs.Documents.First(d => d.Type == "TermsClient").Version;

        var client1 = AuthedClient(user.Token);
        var client2 = AuthedClient(user.Token);

        var call1 = client1.PostAsJsonAsync("/api/legal/accept", new AcceptLegalRequestDto([new AcceptLegalItemDto("Privacy", privacy), new AcceptLegalItemDto("TermsClient", terms)]));
        var call2 = client2.PostAsJsonAsync("/api/legal/accept", new AcceptLegalRequestDto([new AcceptLegalItemDto("Privacy", privacy), new AcceptLegalItemDto("TermsClient", terms)]));
        var results = await Task.WhenAll(call1, call2);

        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
            "a concurrent double accept by the same user must never 500 — both calls describe the same, idempotent end state");

        var status = await (await AuthedClient(user.Token).GetAsync("/api/legal/consent-status"))
            .Content.ReadJsonAsync<ConsentStatusDto>();
        status!.RequiresAcceptance.Should().BeFalse();
        // TermsOwner is reported too (§39.4: every gate-bearing document, not just the ones this call
        // just accepted) — this account never created a company, so it legitimately has no claim for it.
        status.Documents.Where(d => d.Type != "TermsOwner").Should().OnlyContain(d => d.AcceptedVersion == d.CurrentVersion);

        // ConsentLedger.GrantAsync's own 5-second idempotency window (ARCHITECTURE_CYCLE5.md §45.4)
        // deliberately collapses a genuine double-click/parallel-retry into ONE journal row, not two —
        // that window is exactly what makes "never 500s" achievable without also duplicating the record.
        // A second, later acceptance of the SAME version (outside the window) is what legitimately
        // produces a second row (US-66 п.7); this test only pins "no crash, no duplicate from a race".
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
        var rows = await db.ConsentRecords.Where(c => c.UserId == user.UserId && c.DocumentKey == "Privacy").CountAsync();
        rows.Should().Be(1, "two concurrent accepts of the SAME version within the idempotency window must collapse to one journal row, not duplicate");
    }
}
