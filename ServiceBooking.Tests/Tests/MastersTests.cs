using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class MastersTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
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
        var entries = await response.Content.ReadFromJsonAsync<List<MasterClientDto>>();
        var entry = entries.Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which;
        entry.TotalVisits.Should().Be(2);
        entry.Name.Should().Be($"{clientUser.FirstName} {clientUser.LastName}");
        entry.GuestPhone.Should().BeNull();

        // Contact info is always shown to a master who serves the client (cycle A decision Q10 — the
        // "hidden until 24h after visit" rule is removed entirely, see MC-012 below). Phone is canonical
        // (digits only) — US-26.
        entry.Phone.Should().Be("79995551234");
        entry.Email.Should().Be(clientUser.Email);

        entry.BookingSummaries.Should().HaveCount(2);
        entry.BookingSummaries.Should().OnlyContain(b => b.ServiceName == service.Name && b.Status == "Confirmed" && b.Date == date);
    }

    [Fact, TestCase("MC-003")]
    public async Task GetClients_ForGuestBooking_ReturnsGroupedByGuestPhone()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        // A guest phone with enough entropy to be collision-free across a run, kept in an already-
        // canonical (digits-only, 11-digit, leading 7) shape so the assertion below doesn't need to
        // account for US-26 normalization separately. (Guid hex digits can include a-f, which isn't a
        // digit — so the random suffix is taken from a decimal, not hex, representation.)
        var guestPhone = "7999" + Math.Abs(Guid.NewGuid().GetHashCode()).ToString().PadLeft(7, '0')[..7];
        var guestBooking = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(15, 0), null, "Walk-in Guest", guestPhone, "guest@test.local", null));
        guestBooking.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<List<MasterClientDto>>();
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
        var entries = await response.Content.ReadFromJsonAsync<List<MasterClientDto>>();
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
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var beforeResponse = await masterClient.GetAsync($"/api/masters/clients?companyId={company.Id}");
        var beforeEntry = (await beforeResponse.Content.ReadFromJsonAsync<List<MasterClientDto>>())!
            .Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which;
        beforeEntry.Notes.Should().Contain(n => n.Note == noteText);
        beforeEntry.BookingSummaries.Should().OnlyContain(b => b.Status == "Confirmed");

        (await masterClient.PatchAsync($"/api/bookings/{bookingId}/noshow", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterResponse = await masterClient.GetAsync($"/api/masters/clients?companyId={company.Id}");
        var afterEntry = (await afterResponse.Content.ReadFromJsonAsync<List<MasterClientDto>>())!
            .Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which;
        afterEntry.Notes.Should().Contain(n => n.Note == noteText);
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
        // US-07: creation now returns 201 + the full note object, not 200 + { id }.
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await addResponse.Content.ReadJsonAsync<ClientNoteDto>())!;
        created.Note.Should().Be(noteText);
        created.AuthorId.Should().Be(master.UserId);
        created.CanDelete.Should().BeTrue();
        created.Photos.Should().BeEmpty();

        var listResponse = await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");
        var entries = await listResponse.Content.ReadFromJsonAsync<List<MasterClientDto>>();
        var entry = entries.Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which;
        entry.Notes.Should().Contain(n => n.Note == noteText && n.Id == created.Id);
    }

    [Fact, TestCase("MC-013")]
    public async Task AddNote_WithBookingId_LinksTheVisitAndIsReturnedInTheNote()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var bookingResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = (await bookingResponse.Content.ReadJsonAsync<BookingDto>())!;

        var addResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, clientUser.UserId, null, Unique("Note from panel "), booking.Id));

        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await addResponse.Content.ReadJsonAsync<ClientNoteDto>())!;
        created.BookingId.Should().Be(booking.Id);
        created.BookingDate.Should().Be(date);
        created.BookingServiceName.Should().Be(service.Name);
    }

    [Fact, TestCase("MC-014")]
    public async Task AddNote_WithBookingFromAnotherCompany_ReturnsBadRequest()
    {
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var masterA = await AddMasterAsync(ownerA.Token, companyA.Id);
        var serviceA = await CreateServiceAsync(ownerA.Token, companyA.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(ownerA.Token, masterA.UserId, companyA.Id, date);
        var clientUser = await RegisterAsync();
        var bookingResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyA.Id, serviceA.Id, masterA.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = (await bookingResponse.Content.ReadJsonAsync<BookingDto>())!;

        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var masterB = await AddMasterAsync(ownerB.Token, companyB.Id);

        var response = await AuthedClient(masterB.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(companyB.Id, clientUser.UserId, null, Unique("Note "), booking.Id));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("MC-015")]
    public async Task AddNote_EmptyText_ReturnsBadRequest()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990003333", "   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("MC-018")]
    public async Task AddNote_WithNeitherClientIdNorGuestPhone_ReturnsBadRequest()
    {
        // A note with neither identifier attaches to nothing GetClients could ever group it under
        // (MastersController.cs:159-163) — it would be written and then be permanently invisible.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, null, Unique("Orphaned note ")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("MC-019")]
    public async Task AddNote_WithClientIdOfUserWithNoBookingsAtThisCompany_ReturnsBadRequest()
    {
        // ClientId is caller-supplied and otherwise unchecked; without the "has a booking here" guard
        // (MastersController.cs:165-175) any staff member could attach a note to an arbitrary AppUser id
        // who has never interacted with this company at all.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var stranger = await RegisterAsync();

        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, stranger.UserId, null, Unique("Note about a stranger ")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // And it must not have been silently written and left invisible either.
        var entries = await (await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}"))
            .Content.ReadFromJsonAsync<List<MasterClientDto>>();
        entries.Should().NotContain(e => e.ClientId == stranger.UserId);
    }

    // ── DELETE /api/masters/clients/notes/{id} ───────────────────────────────

    [Fact, TestCase("MC-005")]
    public async Task DeleteNote_AsOwningMaster_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var addResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990001111", Unique("Note ")));
        var note = (await addResponse.Content.ReadJsonAsync<ClientNoteDto>())!;

        var deleteResponse = await AuthedClient(master.Token).DeleteAsync($"/api/masters/clients/notes/{note.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("MC-006")]
    public async Task DeleteNote_AsColleagueWhoIsNotAuthorOrOwner_ReturnsForbidden()
    {
        // API_CONTRACT.md §2.2 / decision Q16: a colleague who works in the SAME company but is neither
        // the author nor the CompanyOwner gets 403 (visible, testable) rather than 404 — the previous
        // cycle's "scope the lookup by MasterId" trick, which incidentally produced 404 for this case,
        // is gone now that the rule is explicit.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var masterA = await AddMasterAsync(owner.Token, company.Id);
        var masterB = await AddMasterAsync(owner.Token, company.Id);

        var addResponse = await AuthedClient(masterA.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990002222", Unique("Note ")));
        var note = (await addResponse.Content.ReadJsonAsync<ClientNoteDto>())!;

        var response = await AuthedClient(masterB.Token).DeleteAsync($"/api/masters/clients/notes/{note.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("MC-016")]
    public async Task DeleteNote_AsCompanyOwner_DeletingAMastersNote_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var addResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990004444", Unique("Note ")));
        var note = (await addResponse.Content.ReadJsonAsync<ClientNoteDto>())!;

        var response = await AuthedClient(owner.Token).DeleteAsync($"/api/masters/clients/notes/{note.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("MC-017")]
    public async Task DeleteNote_FromAnotherCompany_ReturnsNotFound()
    {
        // Cross-tenant isolation (ARCHITECTURE.md §21.4): staff of a DIFFERENT company gets 404, not
        // 403 — the existence of someone else's note is never confirmed.
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var masterA = await AddMasterAsync(ownerA.Token, companyA.Id);
        var addResponse = await AuthedClient(masterA.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(companyA.Id, null, "+79990005555", Unique("Note ")));
        var note = (await addResponse.Content.ReadJsonAsync<ClientNoteDto>())!;

        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var masterB = await AddMasterAsync(ownerB.Token, companyB.Id);

        var response = await AuthedClient(masterB.Token).DeleteAsync($"/api/masters/clients/notes/{note.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("MC-009")]
    public async Task Notes_AreSharedAcrossMastersWithinTheSameCompany()
    {
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
            new AddNoteRequest(company.Id, clientUser.UserId, null, sharedNote))).StatusCode.Should().Be(HttpStatusCode.Created);

        var bEntries = await (await AuthedClient(masterB.Token).GetAsync($"/api/masters/clients?companyId={company.Id}"))
            .Content.ReadFromJsonAsync<List<MasterClientDto>>();
        bEntries.Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which.Notes.Should().Contain(n => n.Note == sharedNote);
    }

    [Fact, TestCase("MC-010")]
    public async Task Notes_AreNotSharedAcrossDifferentCompanies()
    {
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
            new AddNoteRequest(companyA.Id, clientUser.UserId, null, noteInA))).StatusCode.Should().Be(HttpStatusCode.Created);

        var bEntries = await (await AuthedClient(masterB.Token).GetAsync($"/api/masters/clients?companyId={companyB.Id}"))
            .Content.ReadFromJsonAsync<List<MasterClientDto>>();
        bEntries.Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which.Notes.Should().NotContain(n => n.Note == noteInA);
    }

    [Fact, TestCase("MC-011")]
    public async Task AddNote_ByNonMemberOfThatCompany_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990009988", "should be rejected"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("MC-012")]
    public async Task GetClients_VisitMoreThan24HoursAgo_StillShowsContact()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var bookingResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = (await bookingResponse.Content.ReadJsonAsync<BookingDto>())!;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
            var row = await db.Bookings.FindAsync(booking.Id);
            row!.Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
            await db.SaveChangesAsync();
        }

        var response = await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");
        var entries = await response.Content.ReadFromJsonAsync<List<MasterClientDto>>();

        var entry = entries.Should().ContainSingle(e => e.ClientId == clientUser.UserId).Which;
        entry.Phone.Should().Be(clientUser.Phone);
        entry.Email.Should().Be(clientUser.Email);
    }
}
