using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.WorkingHours;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Validates the shared ApiTestBase helper pipeline (register → company → master → service →
/// working hours → booking) end to end before it's relied on by the full per-controller suites.
/// </summary>
public class BookingsFlowSmokeTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("BK-001")]
    public async Task AuthenticatedClient_CanBookAnAvailableSlot()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60, price: 1500);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var client = AuthedClient(clientUser.Token);

        var response = await client.PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), "First visit", null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.Status.Should().Be(BookingStatus.Confirmed);
        booking.ClientId.Should().Be(clientUser.UserId);
        booking.StartTime.Should().Be(new TimeOnly(10, 0));
        booking.EndTime.Should().Be(new TimeOnly(11, 0));
    }

    [Fact, TestCase("BK-002")]
    public async Task DoubleBookingSameSlot_ReturnsConflict()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30, price: 500);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var firstClient = await RegisterAsync();
        var firstResponse = await AuthedClient(firstClient.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(14, 0), null, null, null, null, null));
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var secondClient = await RegisterAsync();
        var secondResponse = await AuthedClient(secondClient.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(14, 0), null, null, null, null, null));

        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("BK-013")]
    public async Task ConcurrentBookingsForTheSameSlot_OnlyOneSucceeds()
    {
        // Regression test for a TOCTOU race: the free-slot check and the insert used to run outside
        // any transaction/lock, so two requests arriving at the same time could both pass the check
        // and both create a booking for the same master/slot. Firing the requests via Task.WhenAll
        // (rather than sequentially, as BK-002 does) is what actually exercises that race.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30, price: 500);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clients = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RegisterAsync()));

        var dto = new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(15, 0), null, null, null, null, null);
        var responses = await Task.WhenAll(clients.Select(c => AuthedClient(c.Token).PostAsJsonAsync("/api/bookings", dto)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(4);
    }

    [Fact, TestCase("BK-003")]
    public async Task GuestBooking_OnFreePlanCompany_ReturnsPaymentRequired()
    {
        // onlineBooking: false → the company stays on the default Free plan, which blocks online booking.
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true, onlineBooking: false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var response = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, "Guest Name", "+79990001122", null, null));

        response.StatusCode.Should().Be((HttpStatusCode)402);
    }

    [Fact, TestCase("BK-004")]
    public async Task AuthenticatedClientSelfBooking_OnFreePlanCompany_ReturnsPaymentRequired()
    {
        // The Free plan blocks online booking for authenticated clients too — not just guests.
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true, onlineBooking: false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null));

        response.StatusCode.Should().Be((HttpStatusCode)402);
    }

    [Fact, TestCase("BK-005")]
    public async Task StaffManualBooking_OnFreePlanCompany_Succeeds()
    {
        // Free permits the one booking type that isn't online self-service: a master recording a
        // walk-in manually (authenticated caller supplying guest details).
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true, onlineBooking: false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, "Walk-in Client", "+79990001122", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.ClientId.Should().BeNull();
        booking.ClientName.Should().Be("Walk-in Client");
    }

    [Fact, TestCase("BK-006")]
    public async Task GuestBooking_OnPaidPlanCompany_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        await SetSubscriptionAsync(company.Id);

        var response = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, "Guest Name", "+79990001122", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.ClientId.Should().BeNull();
        booking.ClientName.Should().Be("Guest Name");
    }

    [Fact, TestCase("BK-007")]
    public async Task GuestBooking_WhenCompanyDisallowsSelfBooking_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        await SetSubscriptionAsync(company.Id);

        var response = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, "Guest Name", "+79990001122", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("BK-008")]
    public async Task Master_CanCompleteAndThenCannotCancelOrRescheduleFurther()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var masterClient = AuthedClient(master.Token);
        var completeResponse = await masterClient.PatchAsync($"/api/bookings/{booking!.Id}/complete", null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rescheduleResponse = await masterClient.PatchAsJsonAsync($"/api/bookings/{booking.Id}/reschedule",
            new RescheduleDto(date.AddDays(1), new TimeOnly(10, 0)));
        rescheduleResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("BK-009")]
    public async Task UnrelatedMaster_CannotCompleteSomeoneElsesBooking()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var otherMaster = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var otherMasterClient = AuthedClient(otherMaster.Token);
        var response = await otherMasterClient.PatchAsync($"/api/bookings/{booking!.Id}/complete", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("BK-014")]
    public async Task CompanyOwner_CanCancelABookingTheyDoNotPersonallyServiceAsMaster()
    {
        // Regression test: cancel used to check only ClientId/MasterId/SuperAdmin, missing the
        // CompanyOwner branch that complete/noshow/mark-paid/reschedule all already had — an owner who
        // wasn't personally the assigned master couldn't cancel a booking in their own company.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var response = await AuthedClient(owner.Token).PatchAsJsonAsync($"/api/bookings/{booking!.Id}/cancel", "Owner cancelled");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Cancellation ───────────────────────────────────────────────────────────

    [Fact, TestCase("BK-015")]
    public async Task Client_CancelsOwnBooking_AndTheSlotBecomesBookableAgain()
    {
        // Cancelling must actually free the slot: the conflict check filters out Cancelled bookings,
        // so another client booking the exact same time right after a cancellation must succeed.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var firstClient = await RegisterAsync();
        var createResponse = await AuthedClient(firstClient.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var cancelResponse = await AuthedClient(firstClient.Token).PatchAsJsonAsync(
            $"/api/bookings/{booking!.Id}/cancel", "Передумал");
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var secondClient = await RegisterAsync();
        var rebookResponse = await AuthedClient(secondClient.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        rebookResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("BK-016")]
    public async Task UnrelatedClient_CannotCancelSomeoneElsesBooking()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var stranger = await RegisterAsync();
        var response = await AuthedClient(stranger.Token).PatchAsJsonAsync($"/api/bookings/{booking!.Id}/cancel", "hijack");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/bookings/occupied ─────────────────────────────────────────────

    [Fact, TestCase("BK-017")]
    public async Task GetOccupied_IncludesActiveBooking_AndExcludesCancelledOne()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var keep = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();
        var toCancel = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(12, 0), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();
        await AuthedClient(clientUser.Token).PatchAsJsonAsync($"/api/bookings/{toCancel!.Id}/cancel", (string?)null);

        var response = await AnonymousClient().GetAsync(
            $"/api/bookings/occupied?masterId={master.UserId}&date={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ranges = (await response.Content.ReadFromJsonAsync<List<OccupiedRangeDto>>())!;
        ranges.Should().Contain(r => r.Start == keep!.StartTime && r.End == keep.EndTime);
        ranges.Should().NotContain(r => r.Start == new TimeOnly(12, 0));
    }

    // ── Reschedule conflicts ───────────────────────────────────────────────────

    [Fact, TestCase("BK-018")]
    public async Task Reschedule_ToOccupiedSlotConflicts_ThenToFreeSlotSucceeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var blocker = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();
        blocker.Should().NotBeNull();
        var movable = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(12, 0), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();

        var masterClient = AuthedClient(master.Token);
        var conflictResponse = await masterClient.PatchAsJsonAsync($"/api/bookings/{movable!.Id}/reschedule",
            new RescheduleDto(date, new TimeOnly(9, 30))); // overlaps the 9:00-10:00 blocker
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var okResponse = await masterClient.PatchAsJsonAsync($"/api/bookings/{movable.Id}/reschedule",
            new RescheduleDto(date, new TimeOnly(15, 0)));
        okResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updated = await (await masterClient.GetAsync($"/api/bookings/{movable.Id}")).Content.ReadJsonAsync<BookingDto>();
        updated!.StartTime.Should().Be(new TimeOnly(15, 0));
        updated.EndTime.Should().Be(new TimeOnly(16, 0)); // recomputed from the service duration
    }

    // ── Prepayment (owner toggle × tariff AllowOnlinePayment) ──────────────────

    [Fact, TestCase("BK-019")]
    public async Task SelfBooking_WithPrepaymentAndPayingTariff_IsPending_ThenMarkPaidMakesItPaid()
    {
        // Full end-to-end for the payment status lifecycle: RequirePrepayment + a tariff with
        // AllowOnlinePayment → the client's self-booking starts as Pending; the master then confirms
        // payment manually via mark-paid, flipping it to Paid.
        var (owner, company) = await CreateOwnerWithCompanyAsync(requirePrepayment: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();
        booking!.PaymentStatus.Should().Be(PaymentStatus.Pending);

        var markPaidResponse = await AuthedClient(master.Token).PatchAsync($"/api/bookings/{booking.Id}/mark-paid", null);
        markPaidResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await (await AuthedClient(clientUser.Token).GetAsync($"/api/bookings/{booking.Id}")).Content.ReadJsonAsync<BookingDto>();
        after!.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Fact, TestCase("BK-020")]
    public async Task SelfBooking_WithPrepaymentButTariffWithoutOnlinePayment_StaysNotRequired()
    {
        // The owner turned RequirePrepayment on, but their tariff lacks AllowOnlinePayment — the
        // combined gate must NOT put the booking into Pending (mirrors CompanyDto.PrepaymentEnabled).
        var (owner, company) = await CreateOwnerWithCompanyAsync(requirePrepayment: true, attachPlan: false);
        var configId = await CreateTestPlanConfigAsync(allowOnlineBooking: true, allowOnlinePayment: false);
        await SetSubscriptionAsync(company.Id, configId);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();
        booking!.PaymentStatus.Should().Be(PaymentStatus.NotRequired);
    }

    [Fact, TestCase("BK-021")]
    public async Task StaffManualBooking_IgnoresPrepayment_EvenWhenFullyEnabled()
    {
        // Prepayment only applies to online self-bookings: a walk-in recorded by staff is NotRequired
        // even when both the owner toggle and the tariff flag are on.
        var (owner, company) = await CreateOwnerWithCompanyAsync(requirePrepayment: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var createResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, "Walk-in Client", "+79990001122", null, null));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();
        booking!.PaymentStatus.Should().Be(PaymentStatus.NotRequired);
    }

    // ── GET /api/bookings/{id} permissions ─────────────────────────────────────

    [Fact, TestCase("BK-022")]
    public async Task GetById_CompanyOwnerCanView_ButUnrelatedClientCannot()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();

        // The company owner isn't the assigned master or the client, but manages the company.
        (await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking!.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stranger = await RegisterAsync();
        (await AuthedClient(stranger.Token).GetAsync($"/api/bookings/{booking.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/bookings/slots ────────────────────────────────────────────────
    //
    // Regression coverage for a bug where the staff "Записать клиента" screen (ManualBookingModal.tsx)
    // built its own hardcoded time grid and only filtered out already-booked ranges, never checking the
    // master's working hours or breaks — so a break the master had just set up still showed as bookable
    // there. The fix was to make that screen consume this same /api/bookings/slots endpoint that the
    // customer-facing booking flow already used, so it inherits SlotService's break/working-hours
    // filtering "for free". These tests lock down that the shared slot-generation endpoint itself
    // actually excludes breaks, unstaffed days, and already-booked ranges, since nothing exercised it
    // directly before.

    [Fact, TestCase("BK-010")]
    public async Task GetSlots_ExcludesTimesOverlappingABreak_ButKeepsSlotsBeforeAndAfterIt()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();

        var dto = new UpsertWorkingHoursDto(master.UserId, company.Id, date, true,
            new TimeOnly(9, 0), new TimeOnly(18, 0), [new UpsertBreakDto(new TimeOnly(13, 0), new TimeOnly(14, 0))]);
        (await AuthedClient(owner.Token).PutAsJsonAsync("/api/workinghours", dto)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await AnonymousClient().GetAsync(
            $"/api/bookings/slots?masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = (await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!;

        // 60-minute slots starting at 12:00 or 14:00 don't actually overlap the 13:00-14:00 break.
        slots.Should().Contain(s => s.Start == new TimeOnly(12, 0));
        slots.Should().Contain(s => s.Start == new TimeOnly(14, 0));

        // Every slot starting from 12:30 through 13:30 overlaps the break in some way and must be gone.
        slots.Should().NotContain(s => s.Start == new TimeOnly(12, 30));
        slots.Should().NotContain(s => s.Start == new TimeOnly(13, 0));
        slots.Should().NotContain(s => s.Start == new TimeOnly(13, 30));
    }

    [Fact, TestCase("BK-011")]
    public async Task GetSlots_ExcludesTimesOverlappingAnExistingBooking()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var bookingResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null));
        bookingResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await AnonymousClient().GetAsync(
            $"/api/bookings/slots?masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}");

        var slots = (await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!;
        slots.Should().NotContain(s => s.Start == new TimeOnly(11, 0));
        slots.Should().Contain(s => s.Start == new TimeOnly(10, 0));
        slots.Should().Contain(s => s.Start == new TimeOnly(12, 0));
    }

    [Fact, TestCase("BK-012")]
    public async Task GetSlots_ReturnsEmptyList_WhenMasterHasNoWorkingHoursForThatDate()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        // Deliberately not calling SetWorkingDayAsync — no WorkingHours row exists for this date.

        var response = await AnonymousClient().GetAsync(
            $"/api/bookings/slots?masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>();
        slots.Should().BeEmpty();
    }

    [Fact, TestCase("BK-023")]
    public async Task GetSlots_DoesNotOfferSlotThatWouldRunPastEndOfWorkingDay()
    {
        // Working day 09:00-12:00, service 60 min. The last valid start is 11:00 (ends 12:00);
        // an 11:30 start would end at 12:30, past the end of the day, so it must not be offered.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();

        var dto = new UpsertWorkingHoursDto(master.UserId, company.Id, date, true,
            new TimeOnly(9, 0), new TimeOnly(12, 0), []);
        (await AuthedClient(owner.Token).PutAsJsonAsync("/api/workinghours", dto)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await AnonymousClient().GetAsync(
            $"/api/bookings/slots?masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}");

        var slots = (await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!;
        slots.Should().Contain(s => s.Start == new TimeOnly(11, 0));
        slots.Should().NotContain(s => s.Start == new TimeOnly(11, 30));
        slots.Should().NotContain(s => s.End > new TimeOnly(12, 0));
    }
}
