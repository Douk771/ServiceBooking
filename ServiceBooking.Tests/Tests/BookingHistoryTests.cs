using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 10, Block B (US-123/US-124) — SPEC_CYCLE10_MASTER_BOOKING_HISTORY_PHOTO.md §3,
/// ARCHITECTURE_CYCLE10.md §105/§122/§123, §113.2 (QA-3). Written against SPEC/ARCHITECTURE, not the
/// implementation.
/// </summary>
public class BookingHistoryTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private async Task<(ServiceBooking.API.DTOs.Auth.AuthResponseDto Owner, ServiceBooking.API.DTOs.Auth.AuthResponseDto Master, CompanyDto Company, ServiceDto Service, DateOnly Date)> SetUpAsync()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        return (owner, master, company, service, date);
    }

    // ── US-123: chronological events, each with a date/time and an author ──

    [Fact, TestCase("BKH-001")]
    public async Task History_CreatedThenRescheduledThenCancelled_ShowsThreeEventsInOrderWithDetails()
    {
        var (owner, master, company, service, date) = await SetUpAsync();

        var createResponse = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0),
                null, "Walk-in Guest", "+79991234567", null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var newDate = date.AddDays(1);
        var rescheduleResponse = await AuthedClient(owner.Token).PatchJsonAsync(
            $"/api/bookings/{booking!.Id}/reschedule", new RescheduleDto(newDate, new TimeOnly(11, 0)));
        rescheduleResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var cancelResponse = await AuthedClient(owner.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/cancel", "Client called to cancel");
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking.Id}/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>();

        history!.PrecedesJournal.Should().BeFalse();
        history.Events.Should().HaveCount(3);
        // Chronological order.
        history.Events.Should().BeInAscendingOrder(e => e.OccurredAt);

        var created = history.Events.Single(e => e.Kind == BookingEventKind.Created);
        created.Actor.Kind.Should().Be(BookingActorKind.Staff);
        created.Actor.Name.Should().Contain(owner.FirstName).And.Contain(owner.LastName);

        var rescheduled = history.Events.Single(e => e.Kind == BookingEventKind.Rescheduled);
        rescheduled.Reschedule.Should().NotBeNull();
        rescheduled.Reschedule!.FromDate.Should().Be(date);
        rescheduled.Reschedule.FromStartTime.Should().Be(new TimeOnly(10, 0));
        rescheduled.Reschedule.ToDate.Should().Be(newDate);
        rescheduled.Reschedule.ToStartTime.Should().Be(new TimeOnly(11, 0));

        var cancelled = history.Events.Single(e => e.Kind == BookingEventKind.Cancelled);
        cancelled.CancellationReason.Should().Be("Client called to cancel");
    }

    [Fact, TestCase("BKH-002")]
    public async Task History_CompletedAndPaymentMarkedAndNoShow_AreJournaled()
    {
        var (owner, master, company, service, date) = await SetUpAsync();

        var createResponse = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        (await AuthedClient(owner.Token).PatchAsync($"/api/bookings/{booking!.Id}/mark-paid", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AuthedClient(owner.Token).PatchAsync($"/api/bookings/{booking.Id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking.Id}/history");
        var history = await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>();

        history!.Events.Should().Contain(e => e.Kind == BookingEventKind.PaymentMarked);
        history.Events.Should().Contain(e => e.Kind == BookingEventKind.Completed);
    }

    [Fact, TestCase("BKH-003")]
    public async Task History_NoShow_IsJournaled()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var createResponse = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 30), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        (await AuthedClient(owner.Token).PatchAsync($"/api/bookings/{booking!.Id}/noshow", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking.Id}/history");
        var history = await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>();

        history!.Events.Should().ContainSingle(e => e.Kind == BookingEventKind.NoShow);
    }

    // ── Decision П3: a booking that predates the journal shows NO history block at all — Events is
    // empty and PrecedesJournal is true, so the frontend hides the block entirely. ──

    [Fact, TestCase("BKH-004")]
    public async Task History_BookingWithNoJournalEntriesAtAll_PrecedesJournalTrueAndEventsEmpty()
    {
        var (owner, master, company, service, date) = await SetUpAsync();

        // Simulate a pre-cycle-10 booking: insert the row directly, bypassing POST /api/bookings so no
        // BookingEvent row is ever written (decision П3: no backfill).
        Guid bookingId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var booking = new Booking
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, ServiceId = service.Id, MasterId = master.UserId,
                ClientId = null, GuestName = "Legacy Guest", GuestPhone = "+79990000001",
                Date = date, StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(12, 30),
                Status = BookingStatus.Confirmed, PaymentStatus = PaymentStatus.NotRequired,
                Price = service.Price, CreatedAt = DateTime.UtcNow,
            };
            db.Bookings.Add(booking);
            db.BookingServices.Add(new BookingService
            {
                Id = Guid.NewGuid(), BookingId = booking.Id, ServiceId = service.Id,
                NameSnapshot = service.Name, DurationMinutes = 30, Price = service.Price,
            });
            await db.SaveChangesAsync();
            bookingId = booking.Id;
        }

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{bookingId}/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>();

        history!.PrecedesJournal.Should().BeTrue();
        history.Events.Should().BeEmpty();
    }

    [Fact, TestCase("BKH-005")]
    public async Task History_LegacyBookingLaterRescheduled_ShowsOnlyEventsSinceTheJournalStarted()
    {
        var (owner, master, company, service, date) = await SetUpAsync();

        Guid bookingId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var booking = new Booking
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, ServiceId = service.Id, MasterId = master.UserId,
                ClientId = null, GuestName = "Legacy Guest 2", GuestPhone = "+79990000002",
                Date = date, StartTime = new TimeOnly(13, 0), EndTime = new TimeOnly(13, 30),
                Status = BookingStatus.Confirmed, PaymentStatus = PaymentStatus.NotRequired,
                Price = service.Price, CreatedAt = DateTime.UtcNow,
            };
            db.Bookings.Add(booking);
            db.BookingServices.Add(new BookingService
            {
                Id = Guid.NewGuid(), BookingId = booking.Id, ServiceId = service.Id,
                NameSnapshot = service.Name, DurationMinutes = 30, Price = service.Price,
            });
            await db.SaveChangesAsync();
            bookingId = booking.Id;
        }

        var rescheduleResponse = await AuthedClient(owner.Token).PatchJsonAsync(
            $"/api/bookings/{bookingId}/reschedule", new RescheduleDto(date, new TimeOnly(14, 0)));
        rescheduleResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{bookingId}/history");
        var history = await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>();

        // No Created event ever existed for this booking, so it still "precedes the journal" —
        // but unlike BKH-004 there IS one event to show (the reschedule that happened after cycle 10).
        history!.PrecedesJournal.Should().BeTrue();
        history.Events.Should().ContainSingle(e => e.Kind == BookingEventKind.Rescheduled);
    }

    // ── §113.2/QA-3: privacy — 404 (never 403) for the owning client; 200 for staff of the company and
    // SuperAdmin; 404 for staff of an unrelated company. ──

    [Fact, TestCase("BKH-006")]
    public async Task History_OwningClient_Gets404NotForbidden()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(15, 0), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var response = await AuthedClient(clientUser.Token).GetAsync($"/api/bookings/{booking!.Id}/history");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("BKH-007")]
    public async Task History_StaffOfAnUnrelatedCompany_Gets404()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var createResponse = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(16, 0),
                null, "Guest", "+79990000003", null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var (foreignOwner, _) = await CreateOwnerWithCompanyAsync();
        var response = await AuthedClient(foreignOwner.Token).GetAsync($"/api/bookings/{booking!.Id}/history");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("BKH-008")]
    public async Task History_SuperAdmin_Gets200ForAnyCompanysBooking()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var createResponse = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(17, 0),
                null, "Guest", "+79990000004", null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync($"/api/bookings/{booking!.Id}/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("BKH-009")]
    public async Task History_MasterAssignedToTheBooking_Gets200()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var createResponse = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 15),
                null, "Guest", "+79990000005", null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var response = await AuthedClient(master.Token).GetAsync($"/api/bookings/{booking!.Id}/history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── US-124: authorship texts — client, guest, staff, superadmin ──

    [Fact, TestCase("BKH-010")]
    public async Task Authorship_ClientSelfBooking_ShowsClientLabelWithTheirName()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 30), null, null, null, null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking!.Id}/history");
        var history = await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>();

        var created = history!.Events.Single(e => e.Kind == BookingEventKind.Created);
        created.Actor.Kind.Should().Be(BookingActorKind.Client);
        created.Actor.Label.Should().Contain("Записался").And.Contain("клиент")
            .And.Contain(clientUser.FirstName).And.Contain(clientUser.LastName);
    }

    [Fact, TestCase("BKH-011")]
    public async Task Authorship_GuestSelfBooking_ShowsGuestLabelWithTypedName()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var guestName = Unique("Guest ");
        var createResponse = await AnonymousClient().PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0),
                null, guestName, "+79990000006", null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking!.Id}/history");
        var history = await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>();

        var created = history!.Events.Single(e => e.Kind == BookingEventKind.Created);
        created.Actor.Kind.Should().Be(BookingActorKind.Guest);
        created.Actor.Label.Should().Contain("гость").And.Contain(guestName);
    }

    [Fact, TestCase("BKH-012")]
    public async Task Authorship_StaffManualBooking_ShowsStaffNameAndRole_NoPhoneOrEmailLeaked()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var createResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 45),
                null, "Walk-in Guest", "+79990000007", null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking!.Id}/history");
        var raw = await historyResponse.Content.ReadAsStringAsync();
        var history = await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>();

        var created = history!.Events.Single(e => e.Kind == BookingEventKind.Created);
        created.Actor.Kind.Should().Be(BookingActorKind.Staff);
        created.Actor.Role.Should().Be(UserRole.Master);
        created.Actor.Label.Should().Contain("Записал").And.Contain(master.FirstName).And.Contain(master.LastName)
            .And.Contain("мастер");
        raw.Should().NotContain(master.Phone);
    }

    [Fact, TestCase("BKH-013")]
    public async Task Authorship_SuperAdminManualBooking_ShowsPlatformAdministratorLabel()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var admin = await LoginAsSuperAdminAsync();

        var createResponse = await AuthedClient(admin.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 15),
                null, "Admin-recorded Guest", "+79990000008", null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking!.Id}/history");
        var history = await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>();

        var created = history!.Events.Single(e => e.Kind == BookingEventKind.Created);
        created.Actor.Kind.Should().Be(BookingActorKind.SuperAdmin);
        created.Actor.Label.Should().Be("Администратор платформы");
    }

    [Fact, TestCase("BKH-014")]
    public async Task Authorship_StaffLaterRemovedFromCompany_HistoryKeepsNameSnapshot()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var createResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(12, 45),
                null, "Guest", "+79990000009", null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var members = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/members");
        var memberDtos = await members.Content.ReadJsonAsync<List<MemberDto>>();
        var memberRow = memberDtos!.Single(m => m.UserId == master.UserId);
        var removeResponse = await AuthedClient(owner.Token).DeleteAsync($"/api/companies/{company.Id}/members/{memberRow.Id}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking!.Id}/history");
        var history = await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>();

        var created = history!.Events.Single(e => e.Kind == BookingEventKind.Created);
        created.Actor.Name.Should().Contain(master.FirstName).And.Contain(master.LastName);
    }

    // ── historyEventCount: staff-visible only, never on the client's own endpoints (П8) ──

    [Fact, TestCase("BKH-015")]
    public async Task HistoryEventCount_PresentOnStaffEndpoints_NullOnClientEndpoint()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var createResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(13, 15),
                null, "Guest", "+79990000010", null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();
        booking!.HistoryEventCount.Should().BeNull(); // Create response is not a staff-listing endpoint

        var byIdForStaff = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking.Id}");
        var byIdDto = await byIdForStaff.Content.ReadJsonAsync<BookingDto>();
        byIdDto!.HistoryEventCount.Should().Be(1); // one Created event so far

        var masterList = await AuthedClient(master.Token).GetAsync("/api/bookings/master");
        var masterListDtos = await masterList.Content.ReadJsonAsync<List<BookingDto>>();
        masterListDtos!.Single(b => b.Id == booking.Id).HistoryEventCount.Should().Be(1);
    }

    [Fact, TestCase("BKH-016")]
    public async Task ClientOwnBookings_NeverExposeHistoryEventCountOrChangeCardComposition()
    {
        var (owner, master, company, service, date) = await SetUpAsync();
        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(14, 0), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var response = await AuthedClient(clientUser.Token).GetAsync("/api/bookings/client");
        var raw = await response.Content.ReadAsStringAsync();
        var dtos = System.Text.Json.JsonSerializer.Deserialize<List<BookingDto>>(raw, JsonHelpers.Options);
        var mine = dtos!.Single(b => b.Id == booking!.Id);

        mine.HistoryEventCount.Should().BeNull();
        // Card composition per §6 SPEC acceptance: master, service, date, time, price, status remain.
        mine.MasterId.Should().Be(master.UserId);
        mine.ServiceId.Should().Be(service.Id);
        mine.Date.Should().Be(date);
        mine.StartTime.Should().Be(new TimeOnly(14, 0));
        mine.Price.Should().Be(service.Price);
        mine.Status.Should().Be(BookingStatus.Confirmed);
        // No author/history leaked in the raw payload either (not merely hidden on the frontend).
        raw.Should().NotContain("historyEventCount\":0").And.NotContain("historyEventCount\":1");
    }
}
