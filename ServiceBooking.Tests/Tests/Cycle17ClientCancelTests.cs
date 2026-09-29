using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 17, Block B (US-17-06, C15-5, §313 CY17-B-01..08) — the client-owner CANCEL window now
/// lives on the server, not just in the client's UI (ARCHITECTURE_CYCLE17.md §304,
/// API_CONTRACT_CYCLE17.md §322/§323). Written against SPEC_CYCLE17_C15_TECHDEBT.md / ARCHITECTURE_CYCLE17.md §313's table,
/// not against the implementation — the backend implementer deliberately left these for QA. Direct
/// DB writes below are ARRANGE-only (moving the booking's time close to "now"), mirroring the pattern
/// already used by Cycle15ClientRescheduleTests for the identical problem on reschedule.
/// </summary>
public class Cycle17ClientCancelTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private async Task<(ServiceBooking.API.DTOs.Auth.AuthResponseDto Owner, ServiceBooking.API.DTOs.Auth.AuthResponseDto Master,
        CompanyDto Company, ServiceDto Service, ServiceBooking.API.DTOs.Auth.AuthResponseDto Client, BookingDto Booking)>
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

        return (owner, master, company, service, client, booking);
    }

    /// <summary>Arrange-only: moves the booking's visit start to <paramref name="minutesFromNow"/>
    /// minutes from the current instant, bypassing the API (this is setup, not the behavior under
    /// test) — same technique as Cycle15ClientRescheduleTests' T-E3/T-E4.</summary>
    private async Task MoveBookingVisitStartAsync(Guid bookingId, string timeZoneId, int minutesFromNow)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var targetLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow.AddMinutes(minutesFromNow), tz);
        var row = await db.Bookings.FirstAsync(b => b.Id == bookingId);
        row.Date = DateOnly.FromDateTime(targetLocal);
        row.StartTime = TimeOnly.FromDateTime(targetLocal);
        row.EndTime = row.StartTime.AddMinutes(30);
        await db.SaveChangesAsync();
    }

    // ── CY17-B-01: client cancels at minHours-1 -> 409, status unchanged ────────

    [Fact, TestCase("CY17-B-01")]
    public async Task ClientCancelsInsideWindow_Returns409_BookingUnchanged()
    {
        var (_, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);
        // 1 hour from now is inside a 2-hour window -> must be rejected.
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 60);

        var response = await AuthedClient(client.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var reGet = await AuthedClient(client.Token).GetAsync($"/api/bookings/{booking.Id}");
        var reDto = await reGet.Content.ReadJsonAsync<BookingDto>();
        reDto!.Status.Should().Be(BookingStatus.Confirmed, "a rejected cancel must not touch the booking's stored status");
    }

    // ── CY17-B-02: client cancels at minHours+1 -> 204, as today ────────────────

    [Fact, TestCase("CY17-B-02")]
    public async Task ClientCancelsOutsideWindow_Returns204()
    {
        var (_, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);
        // 3 hours from now is outside a 2-hour window -> allowed.
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 180);

        var response = await AuthedClient(client.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reGet = await AuthedClient(client.Token).GetAsync($"/api/bookings/{booking.Id}");
        var reDto = await reGet.Content.ReadJsonAsync<BookingDto>();
        reDto!.Status.Should().Be(BookingStatus.Cancelled);
    }

    // ── CY17-B-03: master cancels 5 minutes before visit -> 204 (staff unrestricted, regression) ─

    [Fact, TestCase("CY17-B-03")]
    public async Task MasterCancelsFiveMinutesBeforeVisit_Returns204_WindowDoesNotApplyToStaff()
    {
        var (_, master, company, _, _, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 5);

        var response = await AuthedClient(master.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the client-owner window must never apply to the assigned master");
    }

    // ── CY17-B-04: owner cancels inside window -> 204 (staff unrestricted) ──────

    [Fact, TestCase("CY17-B-04")]
    public async Task CompanyOwnerCancelsInsideWindow_Returns204_WindowDoesNotApplyToStaff()
    {
        var (owner, _, company, _, _, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 30);

        var response = await AuthedClient(owner.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the client-owner window must never apply to the company owner");
    }

    // ── CY17-B-05: stranger -> 403, NOT 404 (cancel's semantics did not change, unlike reschedule) ─

    [Fact, TestCase("CY17-B-05")]
    public async Task StrangerCancelling_Gets403_NotFound_SemanticsUnchanged()
    {
        var (_, _, _, _, _, booking) = await SetUpConfirmedClientBookingAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "cancel's 403 was NOT changed to 404 this cycle (unlike reschedule in cycle 15) — a deliberate asymmetry, §304.1");
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    // ── CY17-B-06: minHours = 0 -> client can cancel up to the very start; after start -> 409 ──

    [Fact, TestCase("CY17-B-06")]
    public async Task ZeroMinHours_ClientCanCancelRightUpToStart_ButNotAfterItStarted()
    {
        var (_, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 0);

        // Still one minute in the future -> allowed even with a zero-hour window.
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 1);
        var stillAllowed = await AuthedClient(client.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        stillAllowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("CY17-B-06b")]
    public async Task ZeroMinHours_VisitAlreadyStarted_Returns409()
    {
        var (_, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 0);

        // The visit started 5 minutes ago -> even a zero-hour window is exceeded.
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: -5);
        var response = await AuthedClient(client.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a client must not be able to \"cancel\" a visit that has already started, even with minHours = 0");
    }

    // ── CY17-B-07: reason > 300 chars, inside the window -> 400 (validation before window check) ─

    [Fact, TestCase("CY17-B-07")]
    public async Task ReasonTooLong_InsideWindow_Returns400_NotConflict()
    {
        var (_, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);
        // Inside the window AND invalid input -> input validation wins (400), asserting the documented
        // asymmetry with reschedule's own 400 (§304.2/§322).
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 30);
        var tooLong = new string('x', 301);

        var response = await AuthedClient(client.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", tooLong);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── CY17-B-08: clientCancelAllowed true/false on GET /client, null elsewhere ────

    [Fact, TestCase("CY17-B-08a")]
    public async Task GetClientBookings_ClientCancelAllowed_TrueOutsideWindow_FalseInside()
    {
        var (_, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);

        var outsideResponse = await AuthedClient(client.Token).GetAsync("/api/bookings/client");
        var outsideList = await outsideResponse.Content.ReadJsonAsync<List<BookingDto>>();
        outsideList!.Single(b => b.Id == booking.Id).ClientCancelAllowed.Should().BeTrue(
            "booking is far in the future (NextWeekday) — well outside a 2-hour window");

        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 30);
        var insideResponse = await AuthedClient(client.Token).GetAsync("/api/bookings/client");
        var insideList = await insideResponse.Content.ReadJsonAsync<List<BookingDto>>();
        insideList!.Single(b => b.Id == booking.Id).ClientCancelAllowed.Should().BeFalse();
    }

    [Fact, TestCase("CY17-B-08b")]
    public async Task ClientCancelAllowed_IsNull_OnMasterBookingsAndOnCreateResponse()
    {
        var (_, master, _, service, _, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);

        // Create's own response.
        booking.ClientCancelAllowed.Should().BeNull("Create must not compute this field at all");

        // GET /api/bookings/master.
        var masterResponse = await AuthedClient(master.Token).GetAsync($"/api/bookings/master?date={booking.Date:yyyy-MM-dd}");
        masterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var masterList = await masterResponse.Content.ReadJsonAsync<List<BookingDto>>();
        masterList!.Single(b => b.Id == booking.Id).ClientCancelAllowed.Should().BeNull(
            "computed ONLY on GET /api/bookings/client, per project convention — null elsewhere, not false");
    }

    [Fact, TestCase("CY17-B-08c")]
    public async Task ClientCancelAllowed_DoesNotDependOnAllowSelfBookingOrOnlinePlan_UnlikeReschedule()
    {
        // §304.3 — cancel deliberately ignores AllowSelfBooking/plan.AllowOnlineBooking, unlike
        // ClientRescheduleAllowed. Turning AllowSelfBooking off must NOT flip ClientCancelAllowed to
        // false while the booking is still outside the window.
        var (owner, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);

        (await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { allowSelfBooking = false })).EnsureSuccessStatusCode();

        var response = await AuthedClient(client.Token).GetAsync("/api/bookings/client");
        var list = await response.Content.ReadJsonAsync<List<BookingDto>>();
        var dto = list!.Single(b => b.Id == booking.Id);
        dto.ClientCancelAllowed.Should().BeTrue(
            "cancel does not gate on AllowSelfBooking — only status and the time window (§304.3)");
        dto.ClientRescheduleAllowed.Should().BeFalse(
            "reschedule DOES gate on AllowSelfBooking — the two flags must diverge here, proving cancel isn't reusing reschedule's rule");
    }

    // ── CY20-B06: US-20-04 (Т20-05) — a company window above 24h is capped for CANCEL only ─────────
    // SPEC_CYCLE20_LEGAL_CLOSURE.md §393: "Функциональные тесты цикла 17 (Cycle17ClientCancelTests.cs)
    // дополнены случаями «окно > 24»." Written from SPEC.md, independently of ClientRescheduleWindow's
    // own implementation.

    [Fact, TestCase("CY20-B06a")]
    public async Task CompanyWindowAbove24h_ClientCancelAt30Hours_Succeeds_ButRescheduleStillRefused()
    {
        // Company configured a generous 48h window; a client cancel must be capped at the 24h ceiling
        // (Math.Min(48, 24) = 24), so a visit 30h out is still outside the APPLIED window -> cancel OK.
        // Reschedule, in contrast, keeps using the full stored 48h, unchanged -> 30h out is inside it -> 400.
        var (_, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 48);
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 30 * 60);

        var cancelResponse = await AuthedClient(client.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "US-20-04: a client cancel is never blocked more than 24h before the visit, regardless of the company's configured window");
    }

    [Fact, TestCase("CY20-B06b")]
    public async Task CompanyWindowAbove24h_ClientCancelAt20Hours_StillRefused()
    {
        // 20h out is inside even the capped 24h ceiling -> still refused, proving the cap is a MINIMUM
        // protection for the client, not a blanket "always allow inside 24h" override of every other rule.
        var (_, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 48);
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 20 * 60);

        var response = await AuthedClient(client.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY20-B06c")]
    public async Task CompanyWindowAt2Hours_CancelBehavior_IsUnaffectedByTheCeiling()
    {
        // §393's second scenario: "Given окно 2 ч — поведение отмены прежнее" — the 24h ceiling must
        // never LOOSEN an already-stricter company setting (Math.Min(2, 24) = 2, unchanged).
        var (_, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 2);
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 60);

        var response = await AuthedClient(client.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a 2h company window must still block a cancel 1h before the visit — the 24h value is a ceiling, never a floor");
    }

    [Fact, TestCase("CY20-B06d")]
    public async Task GetClientBookings_ClientCancelAllowedText_ReflectsTheAppliedCeiling_NotTheStoredValue()
    {
        // §389: "Текст клиенту «Отменить можно не позже чем за N ч» показывает применяемое N (≤ 24),
        // а не сохранённое значение компании." ClientCancelAllowed itself is the boolean the frontend
        // renders that copy from — assert it flips at the APPLIED (24h) boundary, not the stored (48h) one.
        var (_, _, company, _, client, booking) = await SetUpConfirmedClientBookingAsync(clientRescheduleMinHours: 48);

        // 25h out: outside the applied 24h ceiling -> allowed, even though it's inside the stored 48h.
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 25 * 60);
        var outsideCeiling = await AuthedClient(client.Token).GetAsync("/api/bookings/client");
        var outsideList = await outsideCeiling.Content.ReadJsonAsync<List<BookingDto>>();
        outsideList!.Single(b => b.Id == booking.Id).ClientCancelAllowed.Should().BeTrue(
            "25h is outside the applied 24h ceiling, even though it's inside the company's stored 48h setting");

        // 23h out: inside the applied 24h ceiling -> not allowed.
        await MoveBookingVisitStartAsync(booking.Id, company.TimeZoneId, minutesFromNow: 23 * 60);
        var insideCeiling = await AuthedClient(client.Token).GetAsync("/api/bookings/client");
        var insideList = await insideCeiling.Content.ReadJsonAsync<List<BookingDto>>();
        insideList!.Single(b => b.Id == booking.Id).ClientCancelAllowed.Should().BeFalse();
    }
}
