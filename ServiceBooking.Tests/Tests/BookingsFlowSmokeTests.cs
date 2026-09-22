using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Companies;
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

        // T-B16: GetOccupied is no longer anonymous — the master viewing their own occupancy is always
        // allowed.
        var response = await AuthedClient(master.Token).GetAsync(
            $"/api/bookings/occupied?masterId={master.UserId}&date={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ranges = (await response.Content.ReadFromJsonAsync<List<OccupiedRangeDto>>())!;
        ranges.Should().Contain(r => r.Start == keep!.StartTime && r.End == keep.EndTime);
        ranges.Should().NotContain(r => r.Start == new TimeOnly(12, 0));
    }

    [Fact, TestCase("BK-044")]
    public async Task GetOccupied_Anonymous_ReturnsUnauthorized()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var date = NextWeekday();

        var response = await AnonymousClient().GetAsync($"/api/bookings/occupied?masterId={master.UserId}&date={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("BK-045")]
    public async Task GetOccupied_UnrelatedAuthenticatedUser_ReturnsForbidden()
    {
        // masterId isn't secret (GET /api/companies/{id}/masters lists it publicly), so this closes
        // audit E3/Q9: knowing the id alone must no longer be enough to see occupancy.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var stranger = await RegisterAsync();
        var date = NextWeekday();

        var response = await AuthedClient(stranger.Token).GetAsync($"/api/bookings/occupied?masterId={master.UserId}&date={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("BK-046")]
    public async Task GetOccupied_MasterInTwoCompanies_ReturnsIntervalsFromBoth()
    {
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var masterShared = await AddMasterAsync(ownerA.Token, companyA.Id);
        var serviceA = await CreateServiceAsync(ownerA.Token, companyA.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(ownerA.Token, masterShared.UserId, companyA.Id, date);

        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var addToB = await AuthedClient(ownerB.Token).PostAsJsonAsync($"/api/companies/{companyB.Id}/members",
            new { phone = masterShared.Phone, firstName = masterShared.FirstName, lastName = masterShared.LastName,
                  role = "Master", bio = (string?)null, email = (string?)null });
        addToB.StatusCode.Should().Be(HttpStatusCode.OK);
        var serviceB = await CreateServiceAsync(ownerB.Token, companyB.Id, durationMinutes: 60);
        await SetWorkingDayAsync(ownerB.Token, masterShared.UserId, companyB.Id, date);

        var clientUser = await RegisterAsync();
        (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyA.Id, serviceA.Id, masterShared.UserId, date, new TimeOnly(9, 0), null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyB.Id, serviceB.Id, masterShared.UserId, date, new TimeOnly(14, 0), null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await AuthedClient(masterShared.Token).GetAsync(
            $"/api/bookings/occupied?masterId={masterShared.UserId}&date={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ranges = await response.Content.ReadFromJsonAsync<List<OccupiedRangeDto>>();
        ranges.Should().Contain(r => r.Start == new TimeOnly(9, 0));
        ranges.Should().Contain(r => r.Start == new TimeOnly(14, 0));
    }

    [Fact, TestCase("BK-047")]
    public async Task GetOccupied_ByOwnerOfCompanyWhereMasterWorks_ReturnsOk()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var date = NextWeekday();

        var response = await AuthedClient(owner.Token).GetAsync($"/api/bookings/occupied?masterId={master.UserId}&date={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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

    [Fact, TestCase("BK-050")]
    public async Task Create_AuthenticatedClientSelfBooking_WhenCompanyDisallowsSelfBooking_ReturnsForbidden()
    {
        // The toggle used to be checked only on the guest branch, so a logged-in client booking for
        // themselves ignored it entirely when calling the API directly. The UI hides the button, which
        // is exactly why this went unnoticed — same shape as the guestName bypass (A1).
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("BK-051")]
    public async Task Create_StaffManualBooking_IsNotBlockedByDisabledSelfBooking()
    {
        // The flip side: the toggle governs the public storefront, not the staff's own tool. A salon
        // that turned online booking off still records walk-ins.
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, "Walk-in Client", "+79990001133", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("BK-049")]
    public async Task GuestBooking_WithPrepaymentAndPayingTariff_IsPending()
    {
        // A guest booking is an online self-booking like any other, so the prepayment gate applies to
        // it too. Before the isStaffManualBooking fix this case fell through to NotRequired: "manual"
        // used to mean "the body carries a guestName", which every genuine guest sends — so the guest
        // path silently skipped prepayment. See API_CONTRACT.md §2.3.
        var (owner, company) = await CreateOwnerWithCompanyAsync(requirePrepayment: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var createResponse = await AnonymousClient().PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, "Guest Name", "+79990001122", null, null));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();
        booking!.PaymentStatus.Should().Be(PaymentStatus.Pending);
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
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}");

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
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}");

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
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}");

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
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}");

        var slots = (await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!;
        slots.Should().Contain(s => s.Start == new TimeOnly(11, 0));
        slots.Should().NotContain(s => s.Start == new TimeOnly(11, 30));
        slots.Should().NotContain(s => s.End > new TimeOnly(12, 0));
    }

    // ── GET /api/bookings/slots?manual=true ──────────────────────────────────
    //
    // Regression coverage: ManualBookingModal.tsx (staff booking a client in on the master's behalf)
    // used this same endpoint as guest self-booking, so a master who hadn't set a schedule for a future
    // date yet couldn't be booked manually either — even though staff creating the booking should be
    // trusted to pick any free time. The `manual` flag opens up a full-day range when no WorkingHours
    // row exists, but only for an authenticated caller; guest self-booking never sends it.

    // US-66 (ARCHITECTURE_CYCLE6.md §46, API_CONTRACT_CYCLE6.md §41.1): a date with no WorkingHours row
    // no longer opens up the full 00:00-24:00 range by default — it falls back to the configured
    // default working window (09:00-21:00, appsettings.json Slots:DefaultWindow). The whole-day range is
    // still reachable, but only via the explicit extendedHours=true (see the test right below).
    [Fact, TestCase("BK-024")]
    public async Task GetSlots_WithManualFlag_ReturnsDefaultWorkingWindow_WhenNoWorkingHoursSetForThatDate()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        // Deliberately not calling SetWorkingDayAsync — no WorkingHours row exists for this date.

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}&manual=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = (await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!;
        // Default window is 09:00-21:00 — nothing before it and nothing after it.
        slots.Should().Contain(s => s.Start == new TimeOnly(9, 0));
        // 60-minute service inside a 09:00-21:00 window: the last slot that fits starts at 20:00.
        slots.Should().Contain(s => s.Start == new TimeOnly(20, 0));
        slots.Should().NotContain(s => s.Start < new TimeOnly(9, 0));
        slots.Should().NotContain(s => s.Start > new TimeOnly(20, 0));
        slots.Should().NotContain(s => s.End > new TimeOnly(21, 0));
        // The old whole-day behavior must be gone by default, not just "also present".
        slots.Should().NotContain(s => s.Start == new TimeOnly(0, 0));
        slots.Should().NotContain(s => s.Start == new TimeOnly(22, 30));
    }

    // The previous, pre-US-66 behavior (whole day, 00:00-24:00) is still available, but only when the
    // caller explicitly asks for it via extendedHours=true alongside manual=true (ARCHITECTURE_CYCLE6.md
    // §46.3/§46.4) — this is the "show remaining hours" escape hatch, not the default any more.
    [Fact, TestCase("BK-057")]
    public async Task GetSlots_WithManualFlagAndExtendedHours_ReturnsFullDayRange_WhenNoWorkingHoursSetForThatDate()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        // Deliberately not calling SetWorkingDayAsync — no WorkingHours row exists for this date.

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}&manual=true&extendedHours=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = (await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!;
        slots.Should().Contain(s => s.Start == new TimeOnly(0, 0));
        // 60-minute service: the last slot that fits before midnight starts at 22:30 (ends 23:30) — a
        // 23:00 start would need to end exactly at 24:00, which TimeOnly can't represent, so it's
        // correctly excluded rather than throwing.
        slots.Should().Contain(s => s.Start == new TimeOnly(22, 30));
        slots.Should().NotContain(s => s.Start == new TimeOnly(23, 0));
        slots.Should().NotContain(s => s.End > new TimeOnly(23, 59, 59));
    }

    [Fact, TestCase("BK-025")]
    public async Task GetSlots_WithManualFlag_StillExcludesAlreadyBookedTimes()
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

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}&manual=true");

        var slots = (await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!;
        slots.Should().NotContain(s => s.Start == new TimeOnly(11, 0));
        slots.Should().Contain(s => s.Start == new TimeOnly(9, 0));
    }

    [Fact, TestCase("BK-026")]
    public async Task GetSlots_WithManualFlag_ButAnonymousCaller_StillReturnsEmptyList()
    {
        // `manual` is client-supplied, so it must only take effect for an authenticated caller — a
        // guest can't bypass the working-hours gate by just appending the query param themselves.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();

        var response = await AnonymousClient().GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}&manual=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>();
        slots.Should().BeEmpty();
    }

    // ── companyId scoping (US-04 п. 5 / A4, US-13 п. 6 / A1) ─────────────────

    [Fact, TestCase("BK-042")]
    public async Task GetSlots_ManualFlag_FromAuthenticatedNonMember_ScheduleStillApplies()
    {
        // Closes A1: only an authenticated caller who actually works in THIS company may bypass the
        // schedule via manual=true. A logged-in stranger gets the ordinary schedule-gated grid.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        // Deliberately not calling SetWorkingDayAsync — no WorkingHours row exists for this date.
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}&manual=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>();
        slots.Should().BeEmpty();
    }

    [Fact, TestCase("BK-043")]
    public async Task GetSlots_MasterInTwoCompanies_ScheduleFromOtherCompanyDoesNotLeakIn()
    {
        // Closes A4: a moonlighting master's WorkingHours row set in company A must not surface on
        // company B's slot grid.
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var masterShared = await AddMasterAsync(ownerA.Token, companyA.Id);
        var serviceA = await CreateServiceAsync(ownerA.Token, companyA.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(ownerA.Token, masterShared.UserId, companyA.Id, date);

        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var addToB = await AuthedClient(ownerB.Token).PostAsJsonAsync($"/api/companies/{companyB.Id}/members",
            new { phone = masterShared.Phone, firstName = masterShared.FirstName, lastName = masterShared.LastName,
                  role = "Master", bio = (string?)null, email = (string?)null });
        addToB.StatusCode.Should().Be(HttpStatusCode.OK);
        var serviceB = await CreateServiceAsync(ownerB.Token, companyB.Id, durationMinutes: 60);
        // Deliberately no WorkingHours row for masterShared in companyB.

        var slotsA = await AnonymousClient().GetAsync(
            $"/api/bookings/slots?companyId={companyA.Id}&masterId={masterShared.UserId}&serviceId={serviceA.Id}&date={date:yyyy-MM-dd}");
        var slotsB = await AnonymousClient().GetAsync(
            $"/api/bookings/slots?companyId={companyB.Id}&masterId={masterShared.UserId}&serviceId={serviceB.Id}&date={date:yyyy-MM-dd}");

        (await slotsA.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!.Should().NotBeEmpty();
        (await slotsB.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!.Should().BeEmpty();
    }

    // ── GET /api/bookings/slots?excludeBookingId=... (reschedule grid, R2/R3) ──
    //
    // API_CONTRACT_CYCLE6.md §41.1 / ARCHITECTURE_CYCLE6.md §46.3. Regression coverage for the
    // reschedule screen's own slot grid: it must not block the booking's own current interval against
    // itself (R2), and it must keep working when the service was deactivated or the master was removed
    // from the company (R3), since none of that should make an existing booking un-reschedulable. Also
    // locks down the deliberately-ordered permission checks (CanManageBookingAsync before the
    // companyId/masterId pair match) that close the 403-vs-400 oracle.

    [Fact, TestCase("BK-068")]
    public async Task GetSlots_ExcludeBookingId_ByUnrelatedCaller_ReturnsForbidden_EvenWithWrongCompanyAndMaster()
    {
        // The oracle this closes: if the pair-match ran before CanManage, a caller without any right to
        // manage the booking could distinguish "this booking belongs to company X / master Y" (400) from
        // "it doesn't" (403) by observing which status they get back, while holding nothing but a public
        // booking id. Passing deliberately WRONG companyId/masterId here and still getting 403 (not 400)
        // proves CanManage is evaluated first, regardless of what the caller supplies.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();

        var stranger = await RegisterAsync();
        var response = await AuthedClient(stranger.Token).GetAsync(
            $"/api/bookings/slots?companyId={Guid.NewGuid()}&masterId=some-other-master&serviceId={service.Id}" +
            $"&date={date:yyyy-MM-dd}&excludeBookingId={booking!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("BK-069")]
    public async Task GetSlots_ExcludeBookingId_UnknownBookingId_ReturnsNotFound()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}" +
            $"&date={date:yyyy-MM-dd}&excludeBookingId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("BK-070")]
    public async Task GetSlots_ExcludeBookingId_MasterIdDoesNotMatchTheBooking_ReturnsBadRequest()
    {
        // Caller CAN manage the booking (they're the company owner) but supplies a masterId that
        // doesn't match the booking's own MasterId — 400, not 403/404 (every object exists, only the
        // combination is wrong, same convention as the non-exclude branch just below in this file).
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var otherMaster = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={otherMaster.UserId}&serviceId={service.Id}" +
            $"&date={date:yyyy-MM-dd}&excludeBookingId={booking!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("BK-071")]
    public async Task GetSlots_ExcludeBookingId_ByOwner_OffersTimeOverlappingTheBookingsOwnInterval()
    {
        // The main test for R2: without excludeBookingId, a booking's own interval always shows up as
        // occupied (BK-011). With it, the grid must offer a slot that overlaps the booking's OWN current
        // interval — proving the booking excludes itself from its own occupancy, not merely that some
        // other slot survived.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();

        var withoutExclude = await AuthedClient(owner.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}&date={date:yyyy-MM-dd}");
        (await withoutExclude.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!
            .Should().NotContain(s => s.Start == new TimeOnly(11, 0));

        var withExclude = await AuthedClient(owner.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}" +
            $"&date={date:yyyy-MM-dd}&excludeBookingId={booking!.Id}");

        withExclude.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = (await withExclude.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!;
        slots.Should().Contain(s => s.Start == new TimeOnly(11, 0));
    }

    [Fact, TestCase("BK-072")]
    public async Task GetSlots_ExcludeBookingId_ServiceWasDeactivatedSinceBooking_StillOffersSlots()
    {
        // R3: a service soft-deleted (IsActive = false) after the booking was made must not make the
        // booking un-reschedulable — the exclude path takes duration straight from the booking's own
        // stored BookingServices/Service and skips the "service is active" re-validation entirely.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();

        var deactivate = await AuthedClient(owner.Token).DeleteAsync($"/api/services/{service.Id}");
        deactivate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}" +
            $"&date={date:yyyy-MM-dd}&excludeBookingId={booking!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = (await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!;
        // Some slot must be offered at all (the whole working day isn't blocked), and specifically the
        // booking's own 60-minute interval, now excluded from occupancy, must be among them.
        slots.Should().Contain(s => s.Start == new TimeOnly(11, 0));
        slots.Should().Contain(s => s.Start == new TimeOnly(9, 0));
    }

    [Fact, TestCase("BK-073")]
    public async Task GetSlots_ExcludeBookingId_MasterLeftTheCompanySinceBooking_StillOffersSlots()
    {
        // Last review's finding: a master removed from CompanyMembers (fired) after the booking was made
        // still has future bookings the owner must be able to reschedule. The exclude path deliberately
        // skips the "master works in this company" membership check.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();

        var members = await (await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/members"))
            .Content.ReadFromJsonAsync<List<MemberDto>>();
        var memberId = members!.Single(m => m.UserId == master.UserId).Id;
        var removeResponse = await AuthedClient(owner.Token).DeleteAsync($"/api/companies/{company.Id}/members/{memberId}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={service.Id}" +
            $"&date={date:yyyy-MM-dd}&excludeBookingId={booking!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = (await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>())!;
        slots.Should().Contain(s => s.Start == new TimeOnly(11, 0));
        slots.Should().Contain(s => s.Start == new TimeOnly(9, 0));
    }

    // ── POST /api/bookings — целостность создания (US-13, US-05, T-B14) ─────

    [Fact, TestCase("BK-027")]
    public async Task Create_AuthenticatedCaller_UnknownCompanyId_ReturnsNotFound()
    {
        // Previously an authenticated caller's Create fell straight through the (guest-only) company
        // check and hit a broken FK — 500. Now the company existence check runs for everyone first.
        var clientUser = await RegisterAsync();

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(Guid.NewGuid(), Guid.NewGuid(), "some-master-id", NextWeekday(), new TimeOnly(10, 0),
                null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("BK-028")]
    public async Task Create_OwnerWithoutSubscription_UnknownCompanyId_ReturnsNotFound_NotPaymentRequired()
    {
        // The company-existence check (404) must run BEFORE the tariff gate (402) — otherwise a caller
        // who happens to be an unsubscribed owner would see a misleading 402 for a typo'd companyId.
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(Guid.NewGuid(), Guid.NewGuid(), "some-master-id", NextWeekday(), new TimeOnly(10, 0),
                null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("BK-032")]
    public async Task Create_ClientWithGuestName_CompanyDisallowsSelfBooking_ReturnsForbidden()
    {
        // Closes A1: a logged-in client who supplies guestName but does NOT actually work at this
        // company goes through the exact same gates as a guest — including AllowSelfBooking.
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0),
                null, "Someone Else", "+79990001122", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("BK-033")]
    public async Task Create_ClientWithGuestName_FreePlanCompany_ReturnsPaymentRequired()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true, onlineBooking: false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0),
                null, "Someone Else", "+79990001122", null, null));

        response.StatusCode.Should().Be((HttpStatusCode)402);
    }

    [Fact, TestCase("BK-035")]
    public async Task Create_ServiceFromAnotherCompany_ReturnsBadRequest_NoBookingCreated()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var (otherOwner, otherCompany) = await CreateOwnerWithCompanyAsync();
        var foreignService = await CreateServiceAsync(otherOwner.Token, otherCompany.Id);

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, foreignService.Id, master.UserId, date, new TimeOnly(11, 0),
                null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("BK-036")]
    public async Task Create_MasterNotAMemberOfThisCompany_ReturnsBadRequest()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var (otherOwner, otherCompany) = await CreateOwnerWithCompanyAsync();
        var strangerMaster = await AddMasterAsync(otherOwner.Token, otherCompany.Id);
        var date = NextWeekday();

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, strangerMaster.UserId, date, new TimeOnly(11, 0),
                null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("BK-037")]
    public async Task Create_ClientBookingOnNonWorkingDay_ReturnsConflict()
    {
        // No WorkingHours row for this date — the client goes through SlotCalculator with
        // allowWithoutSchedule: false, same as GET /api/bookings/slots would (empty grid).
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0),
                null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("BK-038")]
    public async Task Create_ClientBookingInsideABreak_ReturnsConflict()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();

        var dto = new UpsertWorkingHoursDto(master.UserId, company.Id, date, true,
            new TimeOnly(9, 0), new TimeOnly(18, 0), [new UpsertBreakDto(new TimeOnly(13, 0), new TimeOnly(14, 0))]);
        (await AuthedClient(owner.Token).PutAsJsonAsync("/api/workinghours", dto)).StatusCode.Should().Be(HttpStatusCode.OK);

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(13, 0),
                null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("BK-039")]
    public async Task Create_DateInThePast_ReturnsConflict()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, pastDate, isWorking: true);

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, pastDate, new TimeOnly(11, 0),
                null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("BK-040")]
    public async Task Create_StartTimeNotOnTheGrid_ReturnsConflict()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 7),
                null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("BK-041")]
    public async Task Reschedule_ToAPastDate_ReturnsConflict()
    {
        // Reformulated per ARCHITECTURE.md §14.1: Reschedule is staff-only and, by decision Q7, follows
        // the relaxed staff rule (not in the past + no overlap) rather than full WorkingHours validation.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0),
                null, null, null, null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;

        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var response = await AuthedClient(master.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/reschedule",
            new RescheduleDto(pastDate, new TimeOnly(9, 0)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("BK-048")]
    public async Task Create_ClientWithGuestName_MissingNameOrPhone_ReturnsBadRequest()
    {
        // isGuestPath applies the same gates a real guest gets — including the name/phone requirement —
        // to a logged-in client who supplies SOME guest details but not both.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0),
                null, "Someone Else", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/bookings/client?status= (US-07, T-B15) ──────────────────────

    [Fact, TestCase("BK-029")]
    public async Task GetClientBookings_StatusUpcoming_ReturnsOnlyFutureConfirmedOrPending()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var futureDate = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, futureDate);

        var clientUser = await RegisterAsync();
        var futureBooking = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, futureDate, new TimeOnly(9, 0), null, null, null, null, null));
        futureBooking.StatusCode.Should().Be(HttpStatusCode.Created);
        var future = (await futureBooking.Content.ReadJsonAsync<BookingDto>())!;

        // Create no longer accepts a past date at all (T-B14), so a past booking has to be planted
        // directly in the DB — a row in this shape can still exist from before that fix went live.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
            db.Bookings.Add(new ServiceBooking.Core.Entities.Booking
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, ServiceId = service.Id, MasterId = master.UserId,
                ClientId = clientUser.UserId, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
                StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0),
                Status = BookingStatus.Confirmed, PaymentStatus = PaymentStatus.NotRequired, Price = service.Price
            });
            await db.SaveChangesAsync();
        }

        var response = await AuthedClient(clientUser.Token).GetAsync("/api/bookings/client?status=upcoming");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bookings = await response.Content.ReadJsonAsync<List<BookingDto>>();
        bookings.Should().ContainSingle(b => b.Id == future.Id);
        bookings.Should().OnlyContain(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Pending);
    }

    [Fact, TestCase("BK-030")]
    public async Task GetClientBookings_StatusCompletedOrCancelled_FiltersAsBefore()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var create1 = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking1 = (await create1.Content.ReadJsonAsync<BookingDto>())!;
        (await AuthedClient(master.Token).PatchAsync($"/api/bookings/{booking1.Id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var create2 = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null));
        var booking2 = (await create2.Content.ReadJsonAsync<BookingDto>())!;
        (await AuthedClient(clientUser.Token).PatchAsJsonAsync($"/api/bookings/{booking2.Id}/cancel", (string?)null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var completedResponse = await AuthedClient(clientUser.Token).GetAsync("/api/bookings/client?status=Completed");
        var completed = await completedResponse.Content.ReadJsonAsync<List<BookingDto>>();
        completed.Should().ContainSingle(b => b.Id == booking1.Id);

        var cancelledResponse = await AuthedClient(clientUser.Token).GetAsync("/api/bookings/client?status=Cancelled");
        var cancelled = await cancelledResponse.Content.ReadJsonAsync<List<BookingDto>>();
        cancelled.Should().ContainSingle(b => b.Id == booking2.Id);
    }

    [Fact, TestCase("BK-031")]
    public async Task GetClientBookings_UnknownStatus_ReturnsBadRequest()
    {
        var clientUser = await RegisterAsync();

        var response = await AuthedClient(clientUser.Token).GetAsync("/api/bookings/client?status=garbage");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── US-06 + Q12: cancellationReason / price / companySlug ────────────────

    [Fact, TestCase("BK-053")]
    public async Task Cancel_WithReason_IsVisibleOnTheBookingAfterwards()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30, price: 1800);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var create = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = (await create.Content.ReadJsonAsync<BookingDto>())!;

        // Every BookingDto now carries these three fields, regardless of status.
        booking.Price.Should().Be(1800);
        booking.CompanySlug.Should().Be(company.Slug);
        booking.CancellationReason.Should().BeNull();

        var reason = "Мастер заболел";
        var cancel = await AuthedClient(master.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", reason);
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reGet = await AuthedClient(clientUser.Token).GetAsync($"/api/bookings/{booking.Id}");
        var reDto = await reGet.Content.ReadJsonAsync<BookingDto>();
        reDto!.CancellationReason.Should().Be(reason);
        reDto.Status.Should().Be(BookingStatus.Cancelled);
    }

    [Fact, TestCase("BK-054")]
    public async Task Cancel_ReasonLongerThan300Characters_ReturnsBadRequest()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var create = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = (await create.Content.ReadJsonAsync<BookingDto>())!;

        var tooLong = new string('x', 301);
        var response = await AuthedClient(clientUser.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", tooLong);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("BK-055")]
    public async Task Cancel_WithoutReason_LeavesCancellationReasonNull()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var create = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = (await create.Content.ReadJsonAsync<BookingDto>())!;

        var cancel = await AuthedClient(clientUser.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", (string?)null);
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reGet = await AuthedClient(clientUser.Token).GetAsync($"/api/bookings/{booking.Id}");
        (await reGet.Content.ReadJsonAsync<BookingDto>())!.CancellationReason.Should().BeNull();
    }

    [Fact, TestCase("BK-056")]
    public async Task GetMyBookings_RouteNoLongerExists()
    {
        // US-22, BREAKING № 2: superseded by GET /api/bookings/client.
        var clientUser = await RegisterAsync();

        var response = await AuthedClient(clientUser.Token).GetAsync("/api/bookings/my");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── US-65/Q5: booking horizon (ARCHITECTURE_CYCLE6.md §45.7) ───────────────────

    [Fact, TestCase("BK-058")]
    public async Task ClientBookingBeyondHorizon_ReturnsBadRequest()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var horizonUpdate = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}", new { bookingHorizonDays = 1 });
        horizonUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var farDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, farDate);

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, farDate, new TimeOnly(10, 0), null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "US-65/Q5: an online self-booking client may not book further ahead than the company's horizon");
    }

    [Fact, TestCase("BK-059")]
    public async Task StaffManualBookingBeyondHorizon_Succeeds()
    {
        // Q5/§45.7 p.2: the horizon governs the public storefront only — staff recording a walk-in or a
        // regular must always be able to book ahead of it.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var horizonUpdate = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}", new { bookingHorizonDays = 1 });
        horizonUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var farDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, farDate);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, farDate, new TimeOnly(10, 0),
                null, "Walk-in guest", "+79990001234", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
