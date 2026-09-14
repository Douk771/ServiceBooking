using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>US-38 (SPEC.md §3.4) and US-39 (SPEC.md §3.5) — subject rights: export and self-deletion.</summary>
public class DataRightsTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── GET /api/profile/export — US-38 ──────────────────────────────────────

    [Fact, TestCase("LEG-020")]
    public async Task Export_Anonymous_ReturnsUnauthorized()
    {
        var response = await AnonymousClient().GetAsync("/api/profile/export");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("LEG-021")]
    public async Task Export_ContainsOnlyOwnBookings_NotOtherClients()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientA = await RegisterAsync();
        var clientB = await RegisterAsync();

        var bookingA = await AuthedClient(clientA.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        bookingA.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookingAId = (await bookingA.Content.ReadJsonAsync<BookingDto>())!.Id;

        var bookingB = await AuthedClient(clientB.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null));
        bookingB.StatusCode.Should().Be(HttpStatusCode.Created);

        var exportA = await (await AuthedClient(clientA.Token).GetAsync("/api/profile/export"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var bookingIds = exportA.GetProperty("bookings").EnumerateArray()
            .Select(b => b.GetProperty("id").GetString()).ToList();
        bookingIds.Should().Contain(bookingAId.ToString());
        bookingIds.Should().HaveCount(1, "the export must not leak another client's bookings");
    }

    [Fact, TestCase("LEG-022")]
    public async Task Export_NeverIncludesNoteTextOrPhotoBytes_OrForeignIdsAndHashes()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var bookingResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        bookingResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        const string secretNoteText = "SECRET-JUDGEMENT-ABOUT-THE-CLIENT-9f31";
        var noteResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, clientUser.UserId, null, secretNoteText, null));
        noteResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var raw = await (await AuthedClient(clientUser.Token).GetAsync("/api/profile/export")).Content.ReadAsStringAsync();

        raw.Should().NotContain(secretNoteText, "note text is a staff member's judgment, not the subject's own data (API_CONTRACT.md §8)");
        raw.Should().NotContain(company.Id.ToString(), "no foreign entity ids should appear in the export");
        raw.Should().NotContain("PasswordHash");
        raw.Should().NotContain("SecurityStamp");
        raw.Should().NotContain("ContentHash");

        var exported = JsonSerializer.Deserialize<JsonElement>(raw);
        exported.GetProperty("notesAboutMe").EnumerateArray().Should().ContainSingle();
        exported.GetProperty("notesAboutMe")[0].TryGetProperty("photoCount", out _).Should().BeTrue();
        // Only the fact/metadata is present — there is no field carrying note text or photo bytes at all.
        exported.GetProperty("notesAboutMe")[0].EnumerateObject().Select(p => p.Name)
            .Should().NotContain(new[] { "note", "text", "content" });
    }

    [Fact, TestCase("LEG-023")]
    public async Task Export_FourthCallInADay_ReturnsTooManyRequests()
    {
        // The default factory raises RateLimits:data-export to 10000/min in Testing (appsettings.Testing.json)
        // so the other 344 pre-existing functional tests aren't limited by it — that means the CONTRACT
        // limit (3/day, API_CONTRACT.md §12) has to be exercised against its own, tightly-configured host.
        await using var factory = new RateLimitTestFactory(dataExportPermitLimit: 3);
        var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Т", lastName = "Т", phone = "+79990001122", password = "Password123!", acceptedLegal = true
        });
        register.EnsureSuccessStatusCode();
        var user = (await register.Content.ReadFromJsonAsync<ServiceBooking.API.DTOs.Auth.AuthResponseDto>())!;

        var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.Token);

        for (var i = 0; i < 3; i++)
            (await authed.GetAsync("/api/profile/export")).StatusCode.Should().Be(HttpStatusCode.OK);

        var fourth = await authed.GetAsync("/api/profile/export");
        fourth.StatusCode.Should().Be((HttpStatusCode)429);
        (await fourth.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace();
    }

    // ── POST /api/profile/delete-account — US-39 ─────────────────────────────

    [Fact, TestCase("LEG-030")]
    public async Task DeleteAccount_WithoutPassword_ReturnsBadRequest()
    {
        var user = await RegisterAsync();
        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "" });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("LEG-031")]
    public async Task DeleteAccount_WrongPassword_ReturnsBadRequest()
    {
        var user = await RegisterAsync();
        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "TotallyWrongPassword1" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("LEG-032")]
    public async Task DeleteAccount_Success_LoginFails_OldTokenRejected_VisitAnonymized_PhoneReusable()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var phone = UniquePhone();
        var clientUser = await RegisterAsync(phone: phone);
        var oldToken = clientUser.Token;

        var bookingResponse = await AuthedClient(oldToken).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(15, 0), null, null, null, null, null));
        bookingResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookingId = (await bookingResponse.Content.ReadJsonAsync<BookingDto>())!.Id;

        var deleteResponse = await AuthedClient(oldToken).PostAsJsonAsync("/api/profile/delete-account",
            new { currentPassword = "Password123!" });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Login no longer works.
        var loginResponse = await LoginRawAsync(phone, "Password123!");
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The old token, still structurally valid, is rejected immediately (SecurityStamp rotation).
        var staleTokenResponse = await AuthedClient(oldToken).GetAsync("/api/profile");
        staleTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The visit is anonymized, not deleted, and flagged.
        var bookingAfter = await (await AuthedClient(owner.Token).GetAsync($"/api/bookings/{bookingId}"))
            .Content.ReadJsonAsync<BookingDto>();
        bookingAfter!.ClientDeleted.Should().BeTrue();
        bookingAfter.ClientId.Should().BeNull();
        bookingAfter.Date.Should().Be(date);
        bookingAfter.Price.Should().BePositive();

        // The phone number is free again.
        var reRegistered = await RegisterRawAsync(phone, "AnotherPassword1");
        reRegistered.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("LEG-033")]
    public async Task DeleteAccount_NotesWrittenByThemAboutOthers_SurviveTheirDeletion()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        // GET /api/masters/clients scopes bookings to "MasterId == caller" (a master's OWN client list) —
        // notes are shared company-wide regardless of author, but to observe them via this endpoint
        // after `master` is gone, the booking (and hence the client-list entry) has to belong to
        // someone who can still call it: the owner, working the same visit here.
        await SetWorkingDayAsync(owner.Token, owner.UserId, company.Id, date);

        var otherClient = await RegisterAsync();
        var otherBooking = await AuthedClient(otherClient.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, owner.UserId, date, new TimeOnly(16, 0), null, null, null, null, null));
        otherBooking.StatusCode.Should().Be(HttpStatusCode.Created);

        const string noteAboutOther = "note-about-the-other-client-survives";
        var noteResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, otherClient.UserId, null, noteAboutOther, null));
        noteResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // The master themself is deletable too — they wrote the note above, and it must survive.
        var deleteResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/profile/delete-account",
            new { currentPassword = "Password123!" });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var clientsAfter = await (await AuthedClient(owner.Token).GetAsync($"/api/masters/clients?companyId={company.Id}"))
            .Content.ReadJsonAsync<PagedResult<MasterClientDto>>();
        var entry = clientsAfter!.Items.Should().ContainSingle(c => c.ClientId == otherClient.UserId).Subject;
        entry.Notes.Should().Contain(n => n.Note == noteAboutOther);
    }

    [Fact, TestCase("LEG-034")]
    public async Task DeleteAccount_AsCompanyOwner_ReturnsConflict()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/profile/delete-account",
            new { currentPassword = "Password123!" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Symmetry between export and delete on a guest-then-registered phone (code review fix) ──

    // Code review finding: export and delete-account matched guest-path bookings differently, so a
    // visit made as a GUEST before this phone ever registered could be shown by one endpoint and missed
    // by the other (or vice versa) — a subject could be told "here is all your data" by export while
    // delete-account silently left a guest booking un-anonymized, or export could omit a booking that
    // delete-account then anonymizes without ever having disclosed it. Both must key off the same
    // canonical GuestPhone match, and this pins that they agree on the SAME booking.
    [Fact, TestCase("LEG-035")]
    public async Task GuestBookingThenRegistration_ExportAndDeleteAgreeOnTheSameBooking()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var phone = UniquePhone();

        // Step 1: a GUEST booking is made on this phone, before any account exists on it.
        var guestBookingResponse = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(14, 0), null,
            "Guest Before Signup", phone, null, null));
        guestBookingResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var guestBookingId = (await guestBookingResponse.Content.ReadJsonAsync<BookingDto>())!.Id;

        // Step 2: the same phone registers a real account afterward.
        var user = await RegisterAsync(phone: phone);

        // Step 3: export must show the pre-existing guest visit.
        var exported = await (await AuthedClient(user.Token).GetAsync("/api/profile/export"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var exportedBookingIds = exported.GetProperty("bookings").EnumerateArray()
            .Select(b => b.GetProperty("id").GetString()).ToList();
        exportedBookingIds.Should().Contain(guestBookingId.ToString(),
            "a guest visit recorded on this phone before registration is this subject's own data too");

        // Step 4: delete-account must anonymize that exact same booking, not just bookings created
        // after registration.
        var deleteResponse = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/delete-account",
            new { currentPassword = "Password123!" });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var bookingAfter = await (await AuthedClient(owner.Token).GetAsync($"/api/bookings/{guestBookingId}"))
            .Content.ReadJsonAsync<BookingDto>();
        bookingAfter!.ClientDeleted.Should().BeTrue("the pre-registration guest visit must be anonymized too — export already disclosed it as this subject's data");
        bookingAfter.ClientId.Should().BeNull();
    }
}
