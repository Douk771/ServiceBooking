using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// US-67 (SPEC.md, ARCHITECTURE_CYCLE6.md §47) — several services in one visit. This is the riskiest
/// story of the cycle (it changes the shape of a booking, which reports/reviews/notifications/the
/// widget/admin all read) and had NO functional test coverage before this file — see the QA cycle 6
/// task list. Unit tests already cover the pure aggregation rules
/// (BookingServiceSelectionTests, if present) — these are end-to-end, through the real HTTP pipeline
/// and a real BookingServices table.
/// </summary>
public class MultiServiceBookingTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("BK-060")]
    public async Task CreateBooking_WithMultipleServices_SumsDurationAndPrice_AndListsServicesInOrder()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var haircut = await CreateServiceAsync(owner.Token, company.Id, name: "Стрижка", durationMinutes: 30, price: 1000);
        var coloring = await CreateServiceAsync(owner.Token, company.Id, name: "Окрашивание", durationMinutes: 90, price: 3000);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, haircut.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null,
                ServiceIds: [haircut.Id, coloring.Id]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        booking.TotalDurationMinutes.Should().Be(120, "US-67: duration is the SUM of every selected service");
        booking.Price.Should().Be(4000, "US-67: price is the SUM of every selected service");
        booking.EndTime.Should().Be(new TimeOnly(11, 0));
        booking.Services.Should().HaveCount(2);
        booking.Services![0].ServiceId.Should().Be(haircut.Id, "order of services must follow serviceIds, not creation order");
        booking.Services[1].ServiceId.Should().Be(coloring.Id);
        booking.ServiceId.Should().Be(haircut.Id, "Booking.ServiceId legacy field must be serviceIds[0]");
    }

    [Fact, TestCase("BK-061")]
    public async Task CreateBooking_WithSixServices_IsRejected_FiveIsAccepted()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var services = new List<ServiceDto>();
        for (var i = 0; i < 6; i++)
            services.Add(await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 15, price: 100));
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var tooMany = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, services[0].Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null,
                ServiceIds: services.Select(s => s.Id).ToList()));
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest, "US-67: maximum 5 services per visit (SPEC.md Q3)");

        var exactlyFive = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, services[0].Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null,
                ServiceIds: services.Take(5).Select(s => s.Id).ToList()));
        exactlyFive.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("BK-062")]
    public async Task CreateBooking_MasterCannotPerformOneOfTheServices_ReturnsBadRequestNamingIt()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var canDo = await CreateServiceAsync(owner.Token, company.Id, name: "Стрижка");
        var cannotDo = await CreateServiceAsync(owner.Token, company.Id, name: "Маникюр");
        // Assigning ANY MasterService row for `cannotDo` (to a second, unrelated master) switches the
        // fallback off for that service — without this, "no assignments exist" makes everyone able to
        // perform it (MasterCapability.cs), and the scenario wouldn't be reachable.
        var otherMaster = await AddMasterAsync(owner.Token, company.Id);
        var memberOther = await AuthedClient(owner.Token).GetFromJsonAsync<List<ServiceBooking.API.DTOs.Companies.MemberDto>>(
            $"/api/companies/{company.Id}/members");
        var otherMemberId = memberOther!.Single(m => m.UserId == otherMaster.UserId).Id;
        (await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}/members/{otherMemberId}/services",
            new List<Guid> { cannotDo.Id })).EnsureSuccessStatusCode();

        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, canDo.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null,
                ServiceIds: [canDo.Id, cannotDo.Id]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Маникюр", "the message must name the unsupported service, not just say the slot list is empty");
    }

    [Fact, TestCase("BK-063")]
    public async Task Slots_And_Availability_AgreeWithCreate_OnSummedDuration()
    {
        // A 60-minute working window (09:00-10:00) with two 30-minute services (60 min total) fits
        // EXACTLY one start time — 09:00. A single-service view of this same window would offer both
        // 09:00 and 09:30 as valid 30-minute starts; the multi-service total must collapse that to one.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var svcA = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30, price: 500);
        var svcB = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30, price: 500);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date,
            start: new TimeOnly(9, 0), end: new TimeOnly(10, 0));

        var slotsResponse = await AnonymousClient().GetAsync(
            $"/api/bookings/slots?companyId={company.Id}&masterId={master.UserId}&serviceId={svcA.Id}&serviceIds={svcA.Id}&serviceIds={svcB.Id}&date={date:yyyy-MM-dd}");
        slotsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = await slotsResponse.Content.ReadFromJsonAsync<List<TimeSlotResult>>();

        // Only ONE slot (09:00) can fit a 60-minute visit in a 90-minute window — 09:30 would overrun.
        slots.Should().ContainSingle();
        slots![0].Start.Should().Be(new TimeOnly(9, 0));

        var from = date; var to = date;
        var availabilityResponse = await AnonymousClient().GetAsync(
            $"/api/bookings/availability?companyId={company.Id}&masterId={master.UserId}&serviceId={svcA.Id}" +
            $"&serviceIds={svcA.Id}&serviceIds={svcB.Id}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        availabilityResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var availability = await availabilityResponse.Content.ReadJsonAsync<AvailabilityDto>();
        availability!.TotalDurationMinutes.Should().Be(60);
        availability.Days.Should().ContainSingle(d => d.Date == date && d.Status == DayAvailabilityStatus.Available);

        // Booking the ONLY available slot succeeds; booking the tail (09:30) that /slots correctly
        // excluded is independently rejected by Create too, so the three endpoints can never disagree.
        var clientUser = await RegisterAsync();
        var bookOk = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, svcA.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null,
                ServiceIds: [svcA.Id, svcB.Id]));
        bookOk.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("BK-064")]
    public async Task Reschedule_MultiServiceBooking_UsesSummedDuration_NotFirstServiceDuration()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var svcA = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30, price: 500);
        var svcB = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 45, price: 700);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, svcA.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null,
                ServiceIds: [svcA.Id, svcB.Id]));
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;
        booking.TotalDurationMinutes.Should().Be(75);

        var rescheduleResponse = await AuthedClient(master.Token).PatchAsJsonAsync(
            $"/api/bookings/{booking.Id}/reschedule", new RescheduleDto(date, new TimeOnly(14, 0)));
        rescheduleResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterResponse = await AuthedClient(master.Token).GetAsync($"/api/bookings/{booking.Id}");
        var after = (await afterResponse.Content.ReadJsonAsync<BookingDto>())!;
        after.StartTime.Should().Be(new TimeOnly(14, 0));
        after.EndTime.Should().Be(new TimeOnly(15, 15), "75 minutes (30+45), NOT booking.Service.DurationMinutes (30)");
        after.TotalDurationMinutes.Should().Be(75);
    }

    [Fact, TestCase("BK-065")]
    public async Task LegacyPreCycleBooking_OpensReschedulesAndCancels_LikeAnyOther()
    {
        // Simulates a booking created BEFORE the AddBookingServices migration, exactly as the migration's
        // own backfill leaves it: one BookingServices row, Position 0 (ARCHITECTURE_CYCLE6.md §47.3) —
        // written directly to the database, bypassing POST /api/bookings entirely, since every booking
        // created through the API today already goes through the new multi-service code path.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, name: "Стрижка (legacy)", durationMinutes: 40, price: 900);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var bookingId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Bookings.Add(new Booking
            {
                Id = bookingId, CompanyId = company.Id, ServiceId = service.Id, MasterId = master.UserId,
                ClientId = clientUser.UserId, Date = date, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 40),
                Status = BookingStatus.Confirmed, PaymentStatus = PaymentStatus.NotRequired, Price = 900,
            });
            db.BookingServices.Add(new BookingService
            {
                Id = Guid.NewGuid(), BookingId = bookingId, ServiceId = service.Id, Position = 0,
                NameSnapshot = service.Name, DurationMinutes = 40, Price = 900,
            });
            await db.SaveChangesAsync();
        }

        // Opens, and the single legacy service is reported correctly.
        var getResponse = await AuthedClient(clientUser.Token).GetAsync($"/api/bookings/{bookingId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var booking = (await getResponse.Content.ReadJsonAsync<BookingDto>())!;
        booking.TotalDurationMinutes.Should().Be(40);
        booking.Services.Should().ContainSingle(s => s.ServiceId == service.Id);

        // Reschedules using the (single-row) summed duration, exactly like any post-cycle booking.
        var rescheduleResponse = await AuthedClient(master.Token).PatchAsJsonAsync(
            $"/api/bookings/{bookingId}/reschedule", new RescheduleDto(date, new TimeOnly(11, 0)));
        rescheduleResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterReschedule = (await (await AuthedClient(master.Token).GetAsync($"/api/bookings/{bookingId}"))
            .Content.ReadJsonAsync<BookingDto>())!;
        afterReschedule.EndTime.Should().Be(new TimeOnly(11, 40));

        // Cancels cleanly.
        var cancelResponse = await AuthedClient(clientUser.Token).PatchAsJsonAsync(
            $"/api/bookings/{bookingId}/cancel", "no longer needed");
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterCancel = (await (await AuthedClient(master.Token).GetAsync($"/api/bookings/{bookingId}"))
            .Content.ReadJsonAsync<BookingDto>())!;
        afterCancel.Status.Should().Be(BookingStatus.Cancelled);
    }

    [Fact, TestCase("BK-066")]
    public async Task LegacyPreCycleBooking_AppearsCorrectlyInStats()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 1200);
        var date = NextWeekday();
        var clientUser = await RegisterAsync();

        var bookingId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Bookings.Add(new Booking
            {
                Id = bookingId, CompanyId = company.Id, ServiceId = service.Id, MasterId = master.UserId,
                ClientId = clientUser.UserId, Date = date, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 30),
                Status = BookingStatus.Completed, PaymentStatus = PaymentStatus.NotRequired, Price = 1200,
            });
            db.BookingServices.Add(new BookingService
            {
                Id = Guid.NewGuid(), BookingId = bookingId, ServiceId = service.Id, Position = 0,
                NameSnapshot = service.Name, DurationMinutes = 30, Price = 1200,
            });
            await db.SaveChangesAsync();
        }

        var from = Uri.EscapeDataString(date.ToDateTime(TimeOnly.MinValue).ToString("o"));
        var to = Uri.EscapeDataString(date.ToDateTime(TimeOnly.MinValue).ToString("o"));
        var statsResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/stats?from={from}&to={to}");
        statsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var stats = (await statsResponse.Content.ReadFromJsonAsync<MultiServiceStatsResult>())!;

        stats.TotalRevenue.Should().Be(1200);
        stats.PopularServices.Should().ContainSingle(p => p.ServiceId == service.Id && p.Count == 1);
    }

    [Fact, TestCase("BK-067")]
    public async Task ThreeServiceVisit_CountsThreeInPopularServices_ButRevenueOnlyOnce()
    {
        // US-67 (ARCHITECTURE_CYCLE6.md §44.2 p.4): the "top services" breakdown is a per-line-item
        // count (a 3-service visit contributes 3), while totalRevenue is computed from Booking.Price and
        // must equal exactly what the client saw at checkout — never Σ(BookingServices.Price) × visits.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var s1 = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 20, price: 300);
        var s2 = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 20, price: 400);
        var s3 = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 20, price: 500);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();

        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, s1.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null,
                ServiceIds: [s1.Id, s2.Id, s3.Id]));
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;
        booking.Price.Should().Be(1200);

        (await AuthedClient(master.Token).PatchAsync($"/api/bookings/{booking.Id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var from = Uri.EscapeDataString(date.ToDateTime(TimeOnly.MinValue).ToString("o"));
        var to = Uri.EscapeDataString(date.ToDateTime(TimeOnly.MinValue).ToString("o"));
        var statsResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/stats?from={from}&to={to}");
        var stats = (await statsResponse.Content.ReadFromJsonAsync<MultiServiceStatsResult>())!;

        stats.TotalRevenue.Should().Be(1200, "the visit is counted ONCE, matching what the client saw, not Σ line items");
        stats.BookingsCount.Should().Be(1);
        stats.CompletedCount.Should().Be(1);
        stats.PopularServices.Should().HaveCount(3, "one line per service in the visit");
        stats.PopularServices.Select(p => p.ServiceId).Should().BeEquivalentTo([s1.Id, s2.Id, s3.Id]);
        stats.PopularServices.Should().OnlyContain(p => p.Count == 1);
    }

    private record PopularServiceItem(Guid ServiceId, string ServiceName, int Count);
    private record MultiServiceStatsResult(decimal TotalRevenue, int BookingsCount, int CompletedCount, int CancelledCount,
        int NewClientsCount, List<PopularServiceItem> PopularServices);
}
