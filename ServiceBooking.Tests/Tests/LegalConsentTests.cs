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
/// US-36/US-37 (SPEC.md §3.1/§3.3, ARCHITECTURE.md §18.2 T-B4/T-B5, API_CONTRACT.md §1-7). Written
/// against the shared "Api" collection factory, which loads the real (static, never-swapped)
/// App_Data/legal/legal.json — good enough for everything that does NOT need to change the document
/// version mid-test. Scenarios that need a version bump (material/editorial mismatch, live file
/// replacement) are in LegalConsentVersionChangeTests, against a dedicated factory.
/// </summary>
public class LegalConsentTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── GET /api/legal/documents — US-36 ────────────────────────────────────

    [Fact, TestCase("LEG-001")]
    public async Task GetDocuments_IsPublic_AndListsBothTypes()
    {
        var response = await AnonymousClient().GetAsync("/api/legal/documents");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadJsonAsync<LegalDocumentListDto>();
        body!.Documents.Should().HaveCount(2);
        body.Documents.Should().Contain(d => d.Type == "Privacy");
        body.Documents.Should().Contain(d => d.Type == "Terms");
        body.Documents.Should().OnlyContain(d => d.Version != "" && d.EffectiveFrom != default);
    }

    [Fact, TestCase("LEG-002")]
    public async Task GetDocument_ByType_IsPublic_AndContainsHtmlWithDraftMarker()
    {
        var response = await AnonymousClient().GetAsync("/api/legal/documents/privacy");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadJsonAsync<LegalDocumentDto>();
        body!.Type.Should().Be("Privacy");
        body.ContentHtml.Should().NotBeNullOrWhiteSpace();
        // US-36 п.3: the draft marker survives inside the text itself, not only in the isDraft flag —
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

    // ── POST /api/auth/register — US-37 п.1, BREAKING № 1 ───────────────────

    [Fact, TestCase("LEG-004")]
    public async Task Register_WithoutAcceptedLegal_ReturnsBadRequest()
    {
        var response = await RegisterRawAsync(UniquePhone(), "Password123!", acceptedLegal: false);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("Consent");
    }

    [Fact, TestCase("LEG-004b")]
    public async Task Register_AcceptedLegalOmittedFromJson_DefaultsToFalse_ReturnsBadRequest()
    {
        // The contract (API_CONTRACT.md §5.1) says a MISSING field must be treated the same as an
        // explicit false, not silently pass — sent as a raw JSON object with the field entirely absent,
        // bypassing the C# default parameter that RegisterRawAsync's typed call would otherwise apply.
        var client = AnonymousClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Т", lastName = "Т", phone = UniquePhone(), password = "Password123!"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("LEG-005")]
    public async Task Register_WithAcceptedLegal_RecordsBothConsentVersions()
    {
        var user = await RegisterAsync(acceptedLegal: true);

        var status = await (await AuthedClient(user.Token).GetAsync("/api/legal/consent-status"))
            .Content.ReadJsonAsync<ConsentStatusDto>();

        status!.RequiresAcceptance.Should().BeFalse();
        status.Documents.Should().HaveCount(2);
        status.Documents.Should().OnlyContain(d => d.AcceptedVersion == d.Version);
    }

    // ── GET /api/legal/consent-status — US-37 ────────────────────────────────

    [Fact, TestCase("LEG-006")]
    public async Task ConsentStatus_RequiresAuth()
    {
        var response = await AnonymousClient().GetAsync("/api/legal/consent-status");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/legal/accept — US-37 п.4 ───────────────────────────────────

    [Fact, TestCase("LEG-007")]
    public async Task Accept_WithStaleVersionInBody_ReturnsConflict()
    {
        var user = await RegisterAsync();
        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/legal/accept",
            new AcceptLegalDto("not-a-real-version", "also-not-real"));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("LEG-008")]
    public async Task Accept_WithMissingVersion_ReturnsBadRequest()
    {
        var user = await RegisterAsync();
        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/legal/accept",
            new AcceptLegalDto(null, null));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("LEG-009")]
    public async Task Accept_WithCurrentVersions_ReturnsFreshToken()
    {
        var user = await RegisterAsync();
        var docs = await (await AnonymousClient().GetAsync("/api/legal/documents"))
            .Content.ReadJsonAsync<LegalDocumentListDto>();
        var privacy = docs!.Documents.First(d => d.Type == "Privacy").Version;
        var terms = docs.Documents.First(d => d.Type == "Terms").Version;

        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/legal/accept",
            new AcceptLegalDto(privacy, terms));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadJsonAsync<AcceptLegalResponseDto>();
        body!.Token.Should().NotBeNullOrWhiteSpace();
    }

    // ── Guest booking captures consent version — US-37 п.3, API_CONTRACT.md §7.2 ────────────

    [Fact, TestCase("LEG-010")]
    public async Task GuestBooking_RecordsConsentVersionOnTheBooking()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var currentPrivacy = (await (await AnonymousClient().GetAsync("/api/legal/documents"))
            .Content.ReadJsonAsync<LegalDocumentListDto>())!.Documents.First(d => d.Type == "Privacy").Version;

        var response = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null,
            "Guest Consent", UniquePhone(), null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.ConsentPrivacyVersion.Should().Be(currentPrivacy);
        booking.ConsentTermsVersion.Should().NotBeNullOrWhiteSpace();
        booking.ConsentAcceptedAt.Should().NotBeNull();
    }

    [Fact, TestCase("LEG-011")]
    public async Task AuthenticatedClientBooking_DoesNotCarryConsentFieldsOnTheBooking()
    {
        // The client's consent lives in UserConsent (recorded once at registration), not repeated on
        // every booking they make — API_CONTRACT.md §7.2: "consent* только на гостевом пути".
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
    // existing row", both try to INSERT, and the second one dies on the (UserId, DocumentType) unique
    // constraint with an unhandled DbUpdateException → 500. The fix wraps the upsert in a transaction +
    // AdvisoryLock keyed on the user. This pins that firing the request twice in parallel never 500s.
    //
    // Second code review finding (this test, this cycle): RegisterAsync defaults to acceptedLegal:
    // true, so AuthController.Register already writes both UserConsent rows at registration time — a
    // parallel Accept afterwards only ever hits the UPDATE branch of UpsertConsentAsync, where a
    // concurrent UPDATE of the *same* row is trivially serialized by Postgres on its own, with or
    // without the AdvisoryLock. That never exercises the unique-index INSERT race the lock exists for,
    // so the test used to pass even with the transaction + AdvisoryLock removed. To force the real race,
    // this test deletes the two UserConsent rows straight from the database after registering — exactly
    // the state of a legacy user who signed up before consent tracking existed, i.e. the very case
    // /accept exists to handle — so both parallel calls read "no existing row" and race to INSERT.
    [Fact, TestCase("LEG-036")]
    public async Task Accept_CalledTwiceInParallelBySameUser_NeitherCallReturnsServerError()
    {
        var user = await RegisterAsync();
        var docs = await (await AnonymousClient().GetAsync("/api/legal/documents"))
            .Content.ReadJsonAsync<LegalDocumentListDto>();
        var privacy = docs!.Documents.First(d => d.Type == "Privacy").Version;
        var terms = docs.Documents.First(d => d.Type == "Terms").Version;

        // Simulate a legacy account with no consent rows yet, so the upcoming parallel Accept calls
        // both take the INSERT branch and actually race on the unique (UserId, DocumentType) index.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
            var rows = db.UserConsents.Where(c => c.UserId == user.UserId);
            db.UserConsents.RemoveRange(rows);
            await db.SaveChangesAsync();
        }

        var client1 = AuthedClient(user.Token);
        var client2 = AuthedClient(user.Token);

        var call1 = client1.PostAsJsonAsync("/api/legal/accept", new AcceptLegalDto(privacy, terms));
        var call2 = client2.PostAsJsonAsync("/api/legal/accept", new AcceptLegalDto(privacy, terms));
        var results = await Task.WhenAll(call1, call2);

        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
            "a concurrent double accept by the same user must never 500 — both calls describe the same, idempotent end state");

        // The upsert must not have produced two rows for the same (UserId, DocumentType) either.
        var status = await (await AuthedClient(user.Token).GetAsync("/api/legal/consent-status"))
            .Content.ReadJsonAsync<ConsentStatusDto>();
        status!.RequiresAcceptance.Should().BeFalse();
        status.Documents.Should().OnlyContain(d => d.AcceptedVersion == d.Version);
    }
}
