using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class MastersTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private record BookingSummaryEntry(DateOnly Date, string ServiceName, string Status);

    private record MasterClientEntry(
        string? ClientId, string? GuestPhone, string Name, string? Phone, string? Email,
        DateOnly LastVisitDate, int TotalVisits, List<string> Notes, List<BookingSummaryEntry> BookingSummaries);

    private record AddNoteResponse(Guid Id);

    // ── GET /api/masters/clients ──────────────────────────────────────────────

    [Fact, TestCase("MC-001")]
    public async Task GetClients_CallerNotCompanyMember_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("MC-002")]
    public async Task GetClients_ForMemberMaster_ReturnsRegisteredClientGroupedWithVisitCountAndContactInfo()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync(phone: "+79995551234", email: UniqueEmail("mc2client"));
        var booking1 = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        booking1.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking2 = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null));
        booking2.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<List<MasterClientEntry>>();
        var entry = entries.Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which;
        entry.TotalVisits.Should().Be(2);
        entry.Name.Should().Be($"{clientUser.FirstName} {clientUser.LastName}");
        entry.GuestPhone.Should().BeNull();

        // The booking's end datetime is in the future (well within the 24h contact-visibility
        // window from MastersController.GetClients: `lastVisitEnd >= DateTime.UtcNow.AddHours(-24)`),
        // so contact info must be shown.
        entry.Phone.Should().Be(clientUser.Phone);
        entry.Email.Should().Be(clientUser.Email);

        // Regression guard: MastersController.GetClients previously omitted `bookingSummaries` from
        // its response entirely, even though MasterClientsPage.tsx's expanded "visit history" panel
        // unconditionally called `.length`/`.map` on it — clicking any client crashed the whole page
        // (React unmounts on an uncaught render error) because the field was `undefined`.
        entry.BookingSummaries.Should().HaveCount(2);
        entry.BookingSummaries.Should().OnlyContain(b => b.ServiceName == service.Name && b.Status == "Confirmed" && b.Date == date);

        // NOTE: the "hidden after 24h" branch of this same cutoff check is intentionally not covered
        // here. It requires a booking whose Date+EndTime lie more than 24 hours in the past, but the
        // public booking-creation flow only accepts future dates (see BookingsController.Create — no
        // explicit past-date rejection, but NextWeekday()/normal flows never produce one, and there is
        // no endpoint to backdate a booking's Date/EndTime after creation). Fabricating that state would
        // require reaching into the database directly, which this HTTP-level test suite deliberately
        // avoids, so that branch is left unexercised rather than tested via a fragile workaround.
    }

    [Fact, TestCase("MC-003")]
    public async Task GetClients_ForGuestBooking_ReturnsGroupedByGuestPhone()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var guestPhone = "+7999" + Guid.NewGuid().ToString("N")[..7];
        var guestBooking = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(15, 0), null, "Walk-in Guest", guestPhone, "guest@test.local", null));
        guestBooking.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<List<MasterClientEntry>>();
        var entry = entries.Should().ContainSingle(e => e.GuestPhone == guestPhone).Which;
        entry.ClientId.Should().BeNull();
        entry.Name.Should().Be("Walk-in Guest");
        entry.TotalVisits.Should().Be(1);
        entry.Phone.Should().Be(guestPhone);
        entry.Email.Should().Be("guest@test.local");
    }

    [Fact, TestCase("MC-007")]
    public async Task GetClients_BookingSummaries_ReflectStatusTransitionsAcrossActiveAndFinalizedBookings()
    {
        // Mirrors the MyBookingsPage "client history" panel, which now shows the client's full visit
        // history (including active, non-finalized bookings) with per-booking status badges. That relies
        // on bookingSummaries reflecting each booking's CURRENT status, not just "Confirmed" at creation.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var masterClient = AuthedClient(master.Token);
        var clientHttp = AuthedClient(clientUser.Token);

        var completedBooking = await clientHttp.PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        completedBooking.StatusCode.Should().Be(HttpStatusCode.Created);
        var completedId = (await completedBooking.Content.ReadJsonAsync<BookingDto>())!.Id;

        var cancelledBooking = await clientHttp.PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null));
        cancelledBooking.StatusCode.Should().Be(HttpStatusCode.Created);
        var cancelledId = (await cancelledBooking.Content.ReadJsonAsync<BookingDto>())!.Id;

        var activeBooking = await clientHttp.PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(13, 0), null, null, null, null, null));
        activeBooking.StatusCode.Should().Be(HttpStatusCode.Created);

        (await masterClient.PatchAsync($"/api/bookings/{completedId}/complete", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await masterClient.PatchAsync($"/api/bookings/{cancelledId}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await masterClient.GetAsync($"/api/masters/clients?companyId={company.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<List<MasterClientEntry>>();
        var entry = entries.Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which;

        entry.TotalVisits.Should().Be(3);
        entry.BookingSummaries.Should().HaveCount(3);
        entry.BookingSummaries.Should().Contain(b => b.Status == "Completed");
        entry.BookingSummaries.Should().Contain(b => b.Status == "Cancelled");
        entry.BookingSummaries.Should().Contain(b => b.Status == "Confirmed");
    }

    [Fact, TestCase("MC-008")]
    public async Task GetClients_NoteAddedWhileBookingActive_RemainsVisibleAfterBookingIsFinalized()
    {
        // Validates the exact workflow the new MyBookingsPage "client history" panel supports: a note
        // left about a client is not tied to one specific booking, so it must still show up (alongside
        // the growing visit history) regardless of what happens to the booking that was open when the
        // note was added.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var masterClient = AuthedClient(master.Token);

        var clientUser = await RegisterAsync();
        var booking = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        booking.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookingId = (await booking.Content.ReadJsonAsync<BookingDto>())!.Id;

        var noteText = Unique("Left mid-appointment ");
        var addResponse = await masterClient.PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, clientUser.UserId, null, noteText));
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Confirm the note is already visible while the booking is still active (Confirmed) — this is
        // the "history for active bookings too" half of the requirement.
        var beforeResponse = await masterClient.GetAsync($"/api/masters/clients?companyId={company.Id}");
        var beforeEntry = (await beforeResponse.Content.ReadFromJsonAsync<List<MasterClientEntry>>())!
            .Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which;
        beforeEntry.Notes.Should().Contain(noteText);
        beforeEntry.BookingSummaries.Should().OnlyContain(b => b.Status == "Confirmed");

        (await masterClient.PatchAsync($"/api/bookings/{bookingId}/noshow", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterResponse = await masterClient.GetAsync($"/api/masters/clients?companyId={company.Id}");
        var afterEntry = (await afterResponse.Content.ReadFromJsonAsync<List<MasterClientEntry>>())!
            .Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which;
        afterEntry.Notes.Should().Contain(noteText);
        afterEntry.BookingSummaries.Should().ContainSingle(b => b.Status == "NoShow");
    }

    // ── POST /api/masters/clients/notes ──────────────────────────────────────

    [Fact, TestCase("MC-004")]
    public async Task AddNote_ThenAppearsInMastersOwnClientsList()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        booking.StatusCode.Should().Be(HttpStatusCode.Created);

        var noteText = Unique("VIP customer ");
        var addResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, clientUser.UserId, null, noteText));
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");
        var entries = await listResponse.Content.ReadFromJsonAsync<List<MasterClientEntry>>();
        var entry = entries.Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which;
        entry.Notes.Should().Contain(noteText);
    }

    // ── DELETE /api/masters/clients/notes/{id} ───────────────────────────────

    [Fact, TestCase("MC-005")]
    public async Task DeleteNote_AsOwningMaster_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var addResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990001111", Unique("Note ")));
        var note = (await addResponse.Content.ReadFromJsonAsync<AddNoteResponse>())!;

        var deleteResponse = await AuthedClient(master.Token).DeleteAsync($"/api/masters/clients/notes/{note.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("MC-006")]
    public async Task DeleteNote_AsDifferentMaster_ReturnsNotFound()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var masterA = await AddMasterAsync(owner.Token, company.Id);
        var masterB = await AddMasterAsync(owner.Token, company.Id);

        var addResponse = await AuthedClient(masterA.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990002222", Unique("Note ")));
        var note = (await addResponse.Content.ReadFromJsonAsync<AddNoteResponse>())!;

        // MastersController.DeleteNote scopes its lookup by `n.MasterId == userId`, so a different
        // master's attempt doesn't find the row under their own id — resulting in 404, not 403.
        var response = await AuthedClient(masterB.Token).DeleteAsync($"/api/masters/clients/notes/{note.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("MC-009")]
    public async Task Notes_AreSharedAcrossMastersWithinTheSameCompany()
    {
        // Client history is shared within a company: a note master A writes about a client must be
        // visible to master B when that same client shows up in B's clients list (e.g. a first booking
        // to a new master) — so colleagues see prior context rather than starting blind.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var masterA = await AddMasterAsync(owner.Token, company.Id);
        var masterB = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, masterA.UserId, company.Id, date);
        await SetWorkingDayAsync(owner.Token, masterB.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, masterA.UserId, date, new TimeOnly(9, 0), null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, masterB.UserId, date, new TimeOnly(11, 0), null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var sharedNote = Unique("Allergic to product X ");
        (await AuthedClient(masterA.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, clientUser.UserId, null, sharedNote))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Master B, listing the same client in the same company, sees master A's note.
        var bEntries = await (await AuthedClient(masterB.Token).GetAsync($"/api/masters/clients?companyId={company.Id}"))
            .Content.ReadFromJsonAsync<List<MasterClientEntry>>();
        bEntries.Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which.Notes.Should().Contain(sharedNote);
    }

    [Fact, TestCase("MC-010")]
    public async Task Notes_AreNotSharedAcrossDifferentCompanies()
    {
        // Sharing is scoped to a single company. The same client books in BOTH companies (so they're
        // listed in each), but a note filed in company A must NOT surface when the client is listed in
        // company B — separate businesses keep separate client histories.
        var date = NextWeekday();
        var clientUser = await RegisterAsync();

        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var masterA = await AddMasterAsync(ownerA.Token, companyA.Id);
        var serviceA = await CreateServiceAsync(ownerA.Token, companyA.Id);
        await SetWorkingDayAsync(ownerA.Token, masterA.UserId, companyA.Id, date);
        (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyA.Id, serviceA.Id, masterA.UserId, date, new TimeOnly(9, 0), null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var masterB = await AddMasterAsync(ownerB.Token, companyB.Id);
        var serviceB = await CreateServiceAsync(ownerB.Token, companyB.Id);
        await SetWorkingDayAsync(ownerB.Token, masterB.UserId, companyB.Id, date);
        (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyB.Id, serviceB.Id, masterB.UserId, date, new TimeOnly(9, 0), null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var noteInA = Unique("Note filed in company A ");
        (await AuthedClient(masterA.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(companyA.Id, clientUser.UserId, null, noteInA))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Master B lists the same client (who did book in B) — the company-A note must not appear.
        var bEntries = await (await AuthedClient(masterB.Token).GetAsync($"/api/masters/clients?companyId={companyB.Id}"))
            .Content.ReadFromJsonAsync<List<MasterClientEntry>>();
        bEntries.Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which.Notes.Should().NotContain(noteInA);
    }

    [Fact, TestCase("MC-011")]
    public async Task AddNote_ByNonMemberOfThatCompany_ReturnsForbidden()
    {
        // Only a member of the company may file a note into its shared client history.
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990009988", "should be rejected"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
