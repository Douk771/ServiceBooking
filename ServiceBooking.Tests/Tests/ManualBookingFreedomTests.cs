using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 10, Block A (US-120/US-121/US-122) — SPEC_CYCLE10_MASTER_BOOKING_HISTORY_PHOTO.md,
/// ARCHITECTURE_CYCLE10.md §103, §113.1/§113.2. Written against the SPEC and ARCHITECTURE documents,
/// independently of the frontend/backend implementation, per the cycle-10 QA brief.
///
/// This file exercises the SERVER contract only (GET /api/bookings/availability, GET /api/bookings/slots,
/// POST /api/bookings) — the "one screen, two entry points" merge itself is a frontend concern
/// (BookingModal.tsx) and is not re-tested here beyond confirming the server-side behavior both entry
/// points rely on: `staffMode` is derived purely from server-verified membership, and creating a booking
/// without `guestName` always attributes it to the authenticated caller (the regression fix for the
/// merged screen).
/// </summary>
public class ManualBookingFreedomTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── §6 SPEC acceptance scenario: staff books a client 3 months out, 07:30, on a day with no
    // schedule at all — both from POST /api/bookings directly (the shared endpoint both "Мои записи →
    // Записать клиента" and the public company page ultimately call) — in one pass, no workarounds. ──

    [Fact, TestCase("BK-074")]
    public async Task StaffManualBooking_ThreeMonthsOut_0730_NoScheduleAtAll_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        // Deliberately never call SetWorkingDayAsync — no WorkingHours row exists for this date at all.
        var farDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, farDate, new TimeOnly(7, 30),
                null, "Walk-in Guest", "+79997770001", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.Date.Should().Be(farDate);
        booking.StartTime.Should().Be(new TimeOnly(7, 30));
        booking.ClientId.Should().BeNull(); // genuine manual booking (guestName supplied)
    }

    [Fact, TestCase("BK-075")]
    public async Task Availability_StaffManualExtendedHours_ReturnsScheduleStateAndAllowsFarDayOffDate()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var workingDate = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, workingDate);
        // Explicit day off (a WorkingHours row exists but IsWorking = false) right after the working day.
        var dayOffDate = workingDate.AddDays(1);
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, dayOffDate, isWorking: false);
        var noScheduleDate = workingDate.AddDays(2); // no WorkingHours row at all

        var url = $"/api/bookings/availability?companyId={company.Id}&masterId={master.UserId}" +
            $"&serviceId={service.Id}&from={workingDate:yyyy-MM-dd}&to={noScheduleDate:yyyy-MM-dd}" +
            "&manual=true&extendedHours=true";
        var response = await AuthedClient(owner.Token).GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var availability = await response.Content.ReadJsonAsync<AvailabilityDto>();

        availability!.StaffMode.Should().BeTrue();
        var workingDay = availability.Days.Single(d => d.Date == workingDate);
        var dayOffDay = availability.Days.Single(d => d.Date == dayOffDate);
        var noScheduleDay = availability.Days.Single(d => d.Date == noScheduleDate);

        workingDay.ScheduleState.Should().Be(DayScheduleState.Working);
        dayOffDay.ScheduleState.Should().Be(DayScheduleState.DayOff);
        noScheduleDay.ScheduleState.Should().Be(DayScheduleState.NoSchedule);

        // The critical bit: a day off or without a schedule is NOT reported DayOff/unbookable to staff —
        // it must still be Available (or FullyBooked, never DayOff) because extendedHours opens the
        // whole day as a fallback window.
        dayOffDay.Status.Should().NotBe(DayAvailabilityStatus.DayOff);
        noScheduleDay.Status.Should().NotBe(DayAvailabilityStatus.DayOff);
    }

    [Fact, TestCase("BK-076")]
    public async Task Slots_StaffExtendedHours_OffersTimeOutsideTheDefaultWindow()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        // No working hours row at all for this date.

        var url = $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}" +
            $"&serviceId={service.Id}&date={date:yyyy-MM-dd}&manual=true&extendedHours=true";
        var response = await AuthedClient(owner.Token).GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = await response.Content.ReadFromJsonAsync<List<TimeSlotResult>>();

        // WholeDay fallback: slots must cover late hours the 09:00-21:00 default window wouldn't.
        slots!.Should().Contain(s => s.Start >= new TimeOnly(22, 0));
    }

    [Fact, TestCase("BK-077")]
    public async Task StaffManualBooking_IgnoresCompanyBookingHorizon()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        // Horizon defaults to 90 days — request a date well past it.
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var farDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(200);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, farDate, new TimeOnly(11, 0),
                null, "Far Guest", "+79997770002", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("BK-078")]
    public async Task Create_CrossCompanyBusyMaster_ReturnsConflictNotSilentFailure()
    {
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(ownerA.Token, companyA.Id);
        // Attach the same person as a master of company B too (co-op / multi-company master, Q9).
        var addToB = await AuthedClient(ownerB.Token).PostAsJsonAsync($"/api/companies/{companyB.Id}/members",
            new { phone = master.Phone, firstName = master.FirstName, lastName = master.LastName, role = "Master", bio = (string?)null, email = (string?)null });
        addToB.StatusCode.Should().Be(HttpStatusCode.OK);

        var serviceA = await CreateServiceAsync(ownerA.Token, companyA.Id, durationMinutes: 30);
        var serviceB = await CreateServiceAsync(ownerB.Token, companyB.Id, durationMinutes: 30);
        var date = NextWeekday();

        var first = await AuthedClient(ownerA.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyA.Id, serviceA.Id, master.UserId, date, new TimeOnly(12, 0),
                null, "First Guest", "+79997770003", null, null));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Same master, overlapping time, but a DIFFERENT company — must still conflict.
        var second = await AuthedClient(ownerB.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyB.Id, serviceB.Id, master.UserId, date, new TimeOnly(12, 15),
                null, "Second Guest", "+79997770004", null, null));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── QA-1 (§113.1): the not-staff matrix, three actors × the checks the brief calls out ──

    private async Task AssertNonStaffCannotUseStaffMode(HttpClient client, Guid companyId, string masterId, Guid serviceId, DateOnly dayOff)
    {
        var to = dayOff.AddDays(3);
        var url = $"/api/bookings/availability?companyId={companyId}&masterId={masterId}" +
            $"&serviceId={serviceId}&from={dayOff:yyyy-MM-dd}&to={to:yyyy-MM-dd}&manual=true&extendedHours=true";
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var availability = await response.Content.ReadJsonAsync<AvailabilityDto>();

        availability!.StaffMode.Should().BeFalse();
        availability.Days.Should().OnlyContain(d => d.ScheduleState == null);
        availability.Days.Single(d => d.Date == dayOff).Status.Should().Be(DayAvailabilityStatus.DayOff);

        // extendedHours=true from a non-staff caller must not widen GET /api/bookings/slots either.
        var slotsUrl = $"/api/bookings/slots?companyId={companyId}&masterId={masterId}" +
            $"&serviceId={serviceId}&date={dayOff:yyyy-MM-dd}&manual=true&extendedHours=true";
        var slotsResponse = await client.GetAsync(slotsUrl);
        slotsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = await slotsResponse.Content.ReadFromJsonAsync<List<TimeSlotResult>>();
        slots.Should().BeEmpty(); // no working hours on this date, and no staff fallback granted
    }

    [Fact, TestCase("BK-079")]
    public async Task QA1_AnonymousVisitor_NeverGetsStaffModeOrScheduleState()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var dayOff = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, dayOff, isWorking: false);

        await AssertNonStaffCannotUseStaffMode(AnonymousClient(), company.Id, master.UserId, service.Id, dayOff);
    }

    [Fact, TestCase("BK-080")]
    public async Task QA1_AuthenticatedClient_NeverGetsStaffModeOrScheduleState()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var dayOff = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, dayOff, isWorking: false);

        var client = await RegisterAsync();
        await AssertNonStaffCannotUseStaffMode(AuthedClient(client.Token), company.Id, master.UserId, service.Id, dayOff);
    }

    [Fact, TestCase("BK-081")]
    public async Task QA1_StaffOfAnotherCompany_NeverGetsStaffModeOrScheduleState()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var dayOff = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, dayOff, isWorking: false);

        var (foreignOwner, _) = await CreateOwnerWithCompanyAsync();
        await AssertNonStaffCannotUseStaffMode(AuthedClient(foreignOwner.Token), company.Id, master.UserId, service.Id, dayOff);
    }

    [Fact, TestCase("BK-082")]
    public async Task QA1_Availability_AnonymousStillHonorsBookingHorizon()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(95); // default horizon is 90 days
        var to = from.AddDays(2);

        var url = $"/api/bookings/availability?companyId={company.Id}&masterId={master.UserId}" +
            $"&serviceId={service.Id}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&manual=true&extendedHours=true";
        var response = await AnonymousClient().GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── QA-1 addendum called out explicitly in §113.1: history is 404 to the record's own client ──

    [Fact, TestCase("BK-083")]
    public async Task QA1_History_ClientOwningTheBookingGets404NotAuthorization()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var historyResponse = await AuthedClient(clientUser.Token).GetAsync($"/api/bookings/{booking!.Id}/history");

        historyResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── §113.2 / US-122: rights matrix, closed exactly as it is today, not narrowed and not widened ──

    [Fact, TestCase("BK-084")]
    public async Task Rights_MasterCanBookThemselves()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(13, 0),
                null, "Self Walk-in", "+79997770005", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("BK-085")]
    public async Task Rights_MasterCanBookAnotherMasterOfTheSameCompany()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var masterA = await AddMasterAsync(owner.Token, company.Id);
        var masterB = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, masterB.UserId, company.Id, date);

        var response = await AuthedClient(masterA.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, masterB.UserId, date, new TimeOnly(14, 0),
                null, "Colleague Guest", "+79997770006", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("BK-086")]
    public async Task Rights_OwnerCanBookAnyMaster()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(15, 0),
                null, "Owner-booked Guest", "+79997770007", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("BK-087")]
    public async Task Rights_StaffOfCompanyA_CannotBookMasterOfCompanyB()
    {
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var masterB = await AddMasterAsync(ownerB.Token, companyB.Id);
        var serviceA = await CreateServiceAsync(ownerA.Token, companyA.Id, durationMinutes: 30);
        var date = NextWeekday();

        var response = await AuthedClient(ownerA.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyA.Id, serviceA.Id, masterB.UserId, date, new TimeOnly(16, 0),
                null, "Cross-company Guest", "+79997770008", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("BK-088")]
    public async Task Rights_HiddenMaster_NotInPublicListButAvailableToStaffManualBooking()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        // Owner turns off "provides services" for the master.
        var members = await AuthedClient(owner.Token).GetFromJsonAsync<List<MemberDto>>($"/api/companies/{company.Id}/members");
        var memberRow = members!.Single(m => m.UserId == master.UserId);
        var hide = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{memberRow.Id}/provides-services",
            new { providesServices = false, confirm = true });
        hide.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Public listing no longer shows this master.
        var publicList = await AnonymousClient().GetFromJsonAsync<List<MasterPublicDto>>($"/api/companies/{company.Id}/masters");
        publicList!.Should().NotContain(m => m.UserId == master.UserId);

        // But the owner can still manually book them.
        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(17, 0),
                null, "Regular Guest", "+79997770009", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Regression from code review, closed by "intent from the entry point, not staffMode" ──
    // (ARCHITECTURE_CYCLE10.md §108.2). Server contract for it: a staff member booking WITHOUT
    // guestName gets a genuine client booking, not an ownerless walk-in — regardless of the fact
    // that they are staff of this company. This is exactly what the "book for myself" default (no
    // `company` prop -> defaults false; `company` prop -> defaults false too, per BookingModal.tsx)
    // relies on when it posts the request.

    [Fact, TestCase("BK-089")]
    public async Task Regression_StaffSelfBookingWithoutGuestName_GetsGenuineClientBooking()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        // The master books THEMSELVES as a client of the company's own public page — no guestName,
        // exactly the "bookForClient = false" branch BookingModal defaults to on a public company page.
        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.ClientId.Should().Be(master.UserId); // NOT null — a real, owned booking

        // Visible in "Мои визиты" (GET /api/bookings/client) for that same staff member as a client.
        var myVisitsResponse = await AuthedClient(master.Token).GetAsync("/api/bookings/client");
        var myVisits = await myVisitsResponse.Content.ReadJsonAsync<List<BookingDto>>();
        myVisits!.Should().Contain(b => b.Id == booking.Id);
    }

    [Fact, TestCase("BK-090")]
    public async Task Regression_StaffSelfBookingWithoutGuestName_PrepaymentAppliesPerCompanySetting()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(requirePrepayment: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 30), null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.PaymentStatus.Should().Be(PaymentStatus.Pending); // not NotRequired — prepayment applied
    }

    [Fact, TestCase("BK-091")]
    public async Task Regression_GenuineManualBooking_StillIgnoresPrepaymentRegardlessOfCompanySetting()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(requirePrepayment: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        // Same staff member, same company — but genuinely recording a THIRD PARTY (guestName supplied).
        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0),
                null, "Walk-in Third Party", "+79997770010", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await response.Content.ReadJsonAsync<BookingDto>();
        booking!.ClientId.Should().BeNull();
        booking.PaymentStatus.Should().Be(PaymentStatus.NotRequired); // staff manual booking: prepayment skipped
    }
}
