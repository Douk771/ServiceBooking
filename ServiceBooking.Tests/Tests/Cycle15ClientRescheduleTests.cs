using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 15, Block E (US-15-10/11/12/13, T-E1..T-E8) — a client reschedules their own booking
/// through PATCH /api/bookings/{id}/reschedule, one route with server-computed authority
/// (ARCHITECTURE_CYCLE15.md §257.1-§257.8, API_CONTRACT_CYCLE15.md §287). Written against SPEC.md §5
/// (US-15-10..13) and §0-bis П4/П5, not against the implementation. Includes two scenarios the backend
/// implementer flagged as easy to miss in review: the symmetric 403->404 replacement on
/// GET /api/bookings/slots?excludeBookingId= (not just PATCH .../reschedule), and that a SECOND
/// reschedule of the same booking queues a NEW StaffPushNotification row instead of colliding into the
/// first one's idempotency key (StaffPushScheduler.BuildRescheduledIdempotencyKey).
/// </summary>
public class Cycle15ClientRescheduleTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private async Task<(ServiceBooking.API.DTOs.Auth.AuthResponseDto Owner, ServiceBooking.API.DTOs.Auth.AuthResponseDto Master,
        CompanyDto Company, ServiceDto Service, DateOnly Date, ServiceBooking.API.DTOs.Auth.AuthResponseDto Client, BookingDto Booking)>
        SetUpConfirmedClientBookingAsync(int? clientRescheduleMinHours = null)
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        if (clientRescheduleMinHours is not null)
        {
            (await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
                new { clientRescheduleMinHours })).EnsureSuccessStatusCode();
        }

        var client = await RegisterAsync();
        var createResponse = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;

        return (owner, master, company, service, date, client, booking);
    }

    // ── T-E1: happy path ────────────────────────────────────────────────────────

    [Fact, TestCase("CY15-E1-01")]
    public async Task ClientReschedulesOwnConfirmedBooking_Returns204_DateAndTimeChange()
    {
        var (_, _, _, _, date, client, booking) = await SetUpConfirmedClientBookingAsync();
        var newDate = date; // same working day, different slot
        var newTime = new TimeOnly(11, 0);

        var response = await AuthedClient(client.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule", new RescheduleDto(newDate, newTime));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var fetched = await (await AuthedClient(client.Token).GetAsync("/api/bookings/client")).Content.ReadJsonAsync<List<BookingDto>>();
        var moved = fetched!.Single(b => b.Id == booking.Id);
        moved.Date.Should().Be(newDate);
        moved.StartTime.Should().Be(newTime);
        moved.EndTime.Should().Be(newTime.AddMinutes(30));
    }

    // ── T-E2: outside the master's schedule -> 409, booking untouched ──────────

    [Fact, TestCase("CY15-E2-01")]
    public async Task ClientRequestsTimeOutsideMastersSchedule_Returns409_BookingUnchanged()
    {
        var (_, _, _, _, date, client, booking) = await SetUpConfirmedClientBookingAsync();

        // Working day is 09:00-18:00 (SetWorkingDayAsync default) — 21:00 is outside it.
        var response = await AuthedClient(client.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule", new RescheduleDto(date, new TimeOnly(21, 0)));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var fetched = await (await AuthedClient(client.Token).GetAsync("/api/bookings/client")).Content.ReadJsonAsync<List<BookingDto>>();
        var unchanged = fetched!.Single(b => b.Id == booking.Id);
        unchanged.Date.Should().Be(date);
        unchanged.StartTime.Should().Be(new TimeOnly(10, 0));
    }

    // ── T-E3: less than the window remains before the CURRENT visit -> 400 ─────

    [Fact, TestCase("CY15-E3-01")]
    public async Task CurrentVisitLessThanWindowAway_Returns400()
    {
        var (owner, master, company, _, date, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);

        // Arrange: move the EXISTING booking to 30 minutes from now (bypassing the API — this is setup,
        // not the behavior under test) so the current-visit end of the window rule is the one that
        // fires. Company default time zone is used the same way NotificationTiming does.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tz = TimeZoneInfo.FindSystemTimeZoneById(company.TimeZoneId);
            var soonLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddMinutes(30), tz);
            var row = await db.Bookings.FirstAsync(b => b.Id == booking.Id);
            row.Date = DateOnly.FromDateTime(soonLocal);
            row.StartTime = TimeOnly.FromDateTime(soonLocal);
            row.EndTime = row.StartTime.AddMinutes(30);
            await db.SaveChangesAsync();
        }

        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)));
        var response = await AuthedClient(client.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule",
            new RescheduleDto(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), new TimeOnly(12, 0)));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("2");
    }

    // ── T-E4: the NEW time is too close -> 400 (the other end of the window) ───

    [Fact, TestCase("CY15-E4-01")]
    public async Task RequestedNewTimeLessThanWindowAway_Returns400()
    {
        var (owner, master, company, _, date, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);
        // The CURRENT booking is safely far away (NextWeekday, §257.4's first end is satisfied); the new
        // time requested is 30 minutes from now — that end alone must trigger the same 400.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tz = TimeZoneInfo.FindSystemTimeZoneById(company.TimeZoneId);
        var soonLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddMinutes(30), tz);
        scope.Dispose();

        var response = await AuthedClient(client.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule",
            new RescheduleDto(DateOnly.FromDateTime(soonLocal), TimeOnly.FromDateTime(soonLocal)));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── T-E5: symmetric 404 — stranger gets an empty-body 404, on BOTH endpoints ─

    [Fact, TestCase("CY15-E5-01")]
    public async Task StrangerCallingReschedule_Gets404WithEmptyBody()
    {
        var (_, _, _, _, _, _, booking) = await SetUpConfirmedClientBookingAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule", new RescheduleDto(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), new TimeOnly(10, 0)));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty("must not confirm someone else's booking id is real");
    }

    [Fact, TestCase("CY15-E5-02")]
    public async Task StrangerCallingSlotsWithExcludeBookingId_Gets404WithEmptyBody_SymmetricWithReschedule()
    {
        // Backend review finding: the SAME 403->404 replacement applies to
        // GET /api/bookings/slots?excludeBookingId=, not only to the PATCH endpoint.
        var (_, _, company, _, date, _, booking) = await SetUpConfirmedClientBookingAsync();
        var master = booking.MasterId;
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master}&date={date:yyyy-MM-dd}&excludeBookingId={booking.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty(
            "a caller who is neither staff of this booking nor its own client must get the SAME bare 404 as a booking that doesn't exist");
    }

    [Fact, TestCase("CY15-E5-03")]
    public async Task ClientOwner_CanCallSlotsWithOwnExcludeBookingId_GetsRegularGrid()
    {
        var (_, _, company, _, date, client, booking) = await SetUpConfirmedClientBookingAsync();

        var response = await AuthedClient(client.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={booking.MasterId}&date={date:yyyy-MM-dd}&excludeBookingId={booking.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── T-E6: gates — AllowSelfBooking off -> 403, plan without online booking -> 402 ─

    [Fact, TestCase("CY15-E6-01")]
    public async Task CompanyWithSelfBookingDisabled_ClientCannotReschedule_Returns403()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var client = await RegisterAsync();
        var created = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await created.Content.ReadJsonAsync<BookingDto>())!;

        // Turn AllowSelfBooking off AFTER the booking exists (staff can still create manually, but a
        // client can no longer touch it through self-service anymore).
        (await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { allowSelfBooking = false })).EnsureSuccessStatusCode();

        var response = await AuthedClient(client.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule", new RescheduleDto(date, new TimeOnly(11, 0)));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CY15-E6-02")]
    public async Task PlanWithoutOnlineBooking_ClientCannotReschedule_Returns402()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(onlineBooking: false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        // The booking itself has to be created by STAFF (a restricted plan blocks the client's own
        // online creation too) — a walk-in that the client later tries to self-reschedule.
        var client = await RegisterAsync();
        // GuestName present + caller is staff of this company => isStaffManualBooking (bypasses the
        // AllowOnlineBooking gate at creation time, same as a real walk-in recorded by the owner).
        var created = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, "Walk-in Guest", "+79991234567", null, null));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await created.Content.ReadJsonAsync<BookingDto>())!;

        // Link the booking to the client so ClientOwner authority applies — done via a normal staff
        // "book for a known client" flow isn't exercised here; instead we directly attach ClientId in
        // the DB (arrangement only, mirrors ApiTestBase's own direct-DB helpers) since CreateBookingDto
        // has no staff-assigns-existing-client field in this slice.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Bookings.FirstAsync(b => b.Id == booking.Id);
            row.ClientId = client.UserId;
            await db.SaveChangesAsync();
        }

        var response = await AuthedClient(client.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule", new RescheduleDto(date, new TimeOnly(11, 0)));
        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
    }

    // ── T-E7: journal shows Rescheduled with actor Client and the before/after pair ─

    [Fact, TestCase("CY15-E7-01")]
    public async Task AfterClientReschedule_HistoryShowsRescheduledEvent_WithClientActor()
    {
        var (owner, _, _, _, date, client, booking) = await SetUpConfirmedClientBookingAsync();
        var newTime = new TimeOnly(11, 0);

        (await AuthedClient(client.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule", new RescheduleDto(date, newTime))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var historyResponse = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking.Id}/history");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = (await historyResponse.Content.ReadJsonAsync<BookingHistoryDto>())!;

        var rescheduled = history.Events.Should().ContainSingle(e => e.Kind == BookingEventKind.Rescheduled).Subject;
        rescheduled.Actor.Kind.Should().Be(BookingActorKind.Client, "US-15-12 — the master must see WHO moved it, not a generic system event");
        rescheduled.Reschedule.Should().NotBeNull();
        rescheduled.Reschedule!.FromStartTime.Should().Be(new TimeOnly(10, 0));
        rescheduled.Reschedule.ToStartTime.Should().Be(newTime);
    }

    // ── T-E8: manual/extendedHours are ignored on the client's own excludeBookingId path ─

    [Fact, TestCase("CY15-E8-01")]
    public async Task ClientOwner_SlotsWithManualAndExtendedHours_StillGetsRegularGrid_NotAnyTime()
    {
        var (owner, master, company, _, date, client, booking) = await SetUpConfirmedClientBookingAsync();

        // Regular grid only (09:00-18:00), NOT "any time" — request manual=true&extendedHours=true and
        // confirm a time clearly outside the working day (22:00) does NOT come back in the list.
        var response = await AuthedClient(client.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={booking.MasterId}&date={date:yyyy-MM-dd}" +
            $"&excludeBookingId={booking.Id}&manual=true&extendedHours=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = await response.Content.ReadJsonAsync<List<ServiceBooking.API.Services.TimeSlotResult>>();
        slots.Should().NotBeNull();
        slots!.Should().OnlyContain(s => s.Start >= new TimeOnly(9, 0) && s.Start < new TimeOnly(18, 0),
            "manual/extendedHours must be silently ignored for a client caller — no fallback to WholeDay/DefaultWindow");
    }

    // ── Backend review finding: a SECOND reschedule must queue a NEW push row, not collide ─

    [Fact, TestCase("CY15-E-PUSH-01")]
    public async Task ClientReschedulesTwice_QueuesTwoDistinctStaffPushRows_NotOne()
    {
        var (_, master, company, _, date, client, booking) = await SetUpConfirmedClientBookingAsync();

        await using var push = new PushEnabledFactory(ConnectionString);
        var subscribe = await PushAuthedClient(push, master.Token).PostAsJsonAsync("/api/push/subscriptions",
            new CreatePushSubscriptionInput("https://push.example.test/cy15-reschedule-device",
                new CreatePushSubscriptionKeysInput("p256dh", "auth"), null));
        subscribe.EnsureSuccessStatusCode();

        var first = await AuthedClient(client.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule", new RescheduleDto(date, new TimeOnly(11, 0)));
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await AuthedClient(client.Token).PatchJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule", new RescheduleDto(date, new TimeOnly(12, 0)));
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.StaffPushNotifications
            .Where(n => n.BookingId == booking.Id && n.Type == NotificationType.StaffBookingRescheduled)
            .ToListAsync();

        rows.Should().HaveCount(2,
            "each reschedule must queue its own row — a second reschedule colliding into the first one's " +
            "idempotency key would silently vanish and the master would never learn of the second move");
        rows.Select(r => r.IdempotencyKey).Should().OnlyHaveUniqueItems();
    }

    private static HttpClient PushAuthedClient(PushEnabledFactory push, string token)
    {
        var client = push.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
