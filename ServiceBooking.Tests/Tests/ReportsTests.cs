using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class ReportsTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("RPT-001")]
    public async Task GetMastersReport_CompletedBookingInRange_ComputesEarningsSplitByCommission()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id, commissionPercent: 20);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 1000);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await CreateBookingAsync(clientUser.Token, company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0));
        await CompleteBookingAsync(master.Token, booking.Id);

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadJsonAsync<List<MasterReportDto>>();

        var entry = report.Should().ContainSingle(r => r.MasterId == master.UserId).Subject;
        entry.CommissionPercent.Should().Be(20);
        entry.BookingsCount.Should().Be(1);
        entry.TotalAmount.Should().Be(1000);
        entry.MasterEarnings.Should().Be(200); // 1000 * 20 / 100, rounded to 2 decimals
        entry.CompanyEarnings.Should().Be(800); // TotalAmount - MasterEarnings
    }

    [Fact, TestCase("RPT-002")]
    public async Task GetMastersReport_BookingOutsideDateRange_IsExcluded()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id, commissionPercent: 10);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 500);

        var inRangeDate = NextWeekday();
        var outOfRangeDate = inRangeDate.AddDays(30);
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, inRangeDate);
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, outOfRangeDate);

        var client1 = await RegisterAsync();
        var inRangeBooking = await CreateBookingAsync(client1.Token, company.Id, service.Id, master.UserId, inRangeDate, new TimeOnly(9, 0));
        await CompleteBookingAsync(master.Token, inRangeBooking.Id);

        var client2 = await RegisterAsync();
        var outOfRangeBooking = await CreateBookingAsync(client2.Token, company.Id, service.Id, master.UserId, outOfRangeDate, new TimeOnly(9, 0));
        await CompleteBookingAsync(master.Token, outOfRangeBooking.Id);

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={inRangeDate:yyyy-MM-dd}&to={inRangeDate:yyyy-MM-dd}");
        var report = await response.Content.ReadJsonAsync<List<MasterReportDto>>();

        var entry = report.Should().ContainSingle(r => r.MasterId == master.UserId).Subject;
        entry.BookingsCount.Should().Be(1);
        entry.TotalAmount.Should().Be(500);
    }

    [Fact, TestCase("RPT-003")]
    public async Task GetMastersReport_NonCompletedBookings_AreExcluded()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id, commissionPercent: 15);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 300);
        var date = NextWeekday();
        var date2 = date.AddDays(1);
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date2);

        // Left as Confirmed — never completed.
        var confirmedClient = await RegisterAsync();
        await CreateBookingAsync(confirmedClient.Token, company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0));

        // Cancelled by the client.
        var cancelledClient = await RegisterAsync();
        var cancelledBooking = await CreateBookingAsync(cancelledClient.Token, company.Id, service.Id, master.UserId, date2, new TimeOnly(9, 0));
        var cancelResponse = await AuthedClient(cancelledClient.Token).PatchAsJsonAsync($"/api/bookings/{cancelledBooking.Id}/cancel", "changed my mind");
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date2:yyyy-MM-dd}");
        var report = await response.Content.ReadJsonAsync<List<MasterReportDto>>();

        // Neither a Confirmed nor a Cancelled booking counts as Completed, so the master has zero
        // qualifying bookings and should not appear in the grouped result at all.
        report.Should().NotContain(r => r.MasterId == master.UserId);
    }

    [Fact, TestCase("RPT-004")]
    public async Task GetMastersReport_CompanyOwnerQueryingUnownedCompany_ReturnsForbidden()
    {
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var (ownerB, _) = await CreateOwnerWithCompanyAsync();
        var date = NextWeekday();

        var response = await AuthedClient(ownerB.Token).GetAsync(
            $"/api/reports/masters?companyId={companyA.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("RPT-005")]
    public async Task GetMastersReport_AsPlainClient_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var client = await RegisterAsync(); // only has the "Client" role
        var date = NextWeekday();

        var response = await AuthedClient(client.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("RPT-006")]
    public async Task GetMastersReport_AsMaster_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id); // "Master" role, not CompanyOwner/SuperAdmin
        var date = NextWeekday();

        var response = await AuthedClient(master.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("RPT-007")]
    public async Task GetMastersReport_Anonymous_ReturnsUnauthorized()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var date = NextWeekday();

        var response = await AnonymousClient().GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("RPT-008")]
    public async Task GetMastersReport_AsSuperAdmin_CanFetchReportForCompanyTheyDoNotOwn()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id, commissionPercent: 25);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 400);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await CreateBookingAsync(clientUser.Token, company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0));
        await CompleteBookingAsync(master.Token, booking.Id);

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadJsonAsync<List<MasterReportDto>>();
        report.Should().ContainSingle(r => r.MasterId == master.UserId && r.BookingsCount == 1);
    }

    [Fact, TestCase("RPT-009")]
    public async Task GetMastersReport_WhenTariffDoesNotIncludeAnalytics_ReturnsPaymentRequired()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(onlineBooking: false);
        var date = NextWeekday();

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be((HttpStatusCode)402);
    }

    [Fact, TestCase("RPT-010")]
    public async Task GetMastersReport_WhenTariffIncludesAnalytics_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(onlineBooking: false);
        var configId = await CreateTestPlanConfigAsync(allowAnalytics: true);
        await SetSubscriptionAsync(company.Id, configId);
        var date = NextWeekday();

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("RPT-011")]
    public async Task GetMastersReport_ReflectsPriceAtBookingTime_NotCurrentServicePrice()
    {
        // Regression test: revenue/commission used to be computed by joining to the service's current
        // price, so raising or lowering a price retroactively changed the earnings of already-completed,
        // already-paid bookings. Booking.Price is now a snapshot taken at creation time.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id, commissionPercent: 10);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 1000);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await CreateBookingAsync(clientUser.Token, company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0));
        await CompleteBookingAsync(master.Token, booking.Id);

        // The owner raises the service's price after the booking was made and completed.
        var updateResponse = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/services/{service.Id}",
            new CreateServiceDto(company.Id, "Updated Name", null, 60, 5000));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
        var report = await response.Content.ReadJsonAsync<List<MasterReportDto>>();

        var entry = report.Should().ContainSingle(r => r.MasterId == master.UserId).Subject;
        entry.TotalAmount.Should().Be(1000); // the price at the time the booking was made, not 5000
        entry.MasterEarnings.Should().Be(100);
    }

    [Fact, TestCase("RPT-013")]
    public async Task GetMastersReport_KeepsCommission_AfterMasterIsRemovedFromCompany()
    {
        // Regression test for the same class of bug as RPT-011, one step further: commission used to be
        // read from the master's CURRENT membership row, so removing them from the company rewrote
        // closed periods — a master who earned 40% showed 0%, and their share silently moved to the
        // company. Booking.CommissionPercent is now a snapshot taken at creation, like Booking.Price.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id, commissionPercent: 40);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 1000);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await CreateBookingAsync(clientUser.Token, company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0));
        await CompleteBookingAsync(master.Token, booking.Id);

        // The master leaves the company after the period is over.
        var members = await (await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/members"))
            .Content.ReadFromJsonAsync<List<MemberDto>>();
        var member = members!.Single(m => m.UserId == master.UserId);
        var removal = await AuthedClient(owner.Token).DeleteAsync($"/api/companies/{company.Id}/members/{member.Id}");
        removal.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
        var report = await response.Content.ReadJsonAsync<List<MasterReportDto>>();

        var entry = report.Should().ContainSingle(r => r.MasterId == master.UserId).Subject;
        entry.TotalAmount.Should().Be(1000);
        entry.MasterEarnings.Should().Be(400);   // not 0 — the rate that applied when the visit happened
        entry.CompanyEarnings.Should().Be(600);  // not the full 1000
    }

    [Fact, TestCase("RPT-012")]
    public async Task GetMastersReport_ForMoonlightingMaster_UsesThisCompanysOwnCommission()
    {
        // US-15 (B1): commission is per-membership. A master moonlighting at two companies must be
        // reported using company B's own commission rate, not whatever rate company A set.
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(ownerA.Token, companyA.Id, commissionPercent: 50);

        var addToB = await AuthedClient(ownerB.Token).PostAsJsonAsync($"/api/companies/{companyB.Id}/members",
            new { phone = master.Phone, firstName = master.FirstName, lastName = master.LastName,
                  role = "Master", bio = (string?)null, email = (string?)null });
        addToB.StatusCode.Should().Be(HttpStatusCode.OK);
        var memberInB = (await addToB.Content.ReadJsonAsync<MemberDto>())!;
        await AuthedClient(ownerB.Token).PutAsJsonAsync(
            $"/api/companies/{companyB.Id}/members/{memberInB.Id}/commission", new { commissionPercent = 15 });

        var serviceB = await CreateServiceAsync(ownerB.Token, companyB.Id, price: 1000);
        var date = NextWeekday();
        await SetWorkingDayAsync(ownerB.Token, master.UserId, companyB.Id, date);

        var clientUser = await RegisterAsync();
        var booking = await CreateBookingAsync(clientUser.Token, companyB.Id, serviceB.Id, master.UserId, date, new TimeOnly(9, 0));
        await CompleteBookingAsync(master.Token, booking.Id);

        var response = await AuthedClient(ownerB.Token).GetAsync(
            $"/api/reports/masters?companyId={companyB.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
        var report = await response.Content.ReadJsonAsync<List<MasterReportDto>>();

        var entry = report.Should().ContainSingle(r => r.MasterId == master.UserId).Subject;
        entry.CommissionPercent.Should().Be(15);
        entry.MasterEarnings.Should().Be(150);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────

    private async Task<BookingDto> CreateBookingAsync(
        string clientToken, Guid companyId, Guid serviceId, string masterId, DateOnly date, TimeOnly startTime)
    {
        var response = await AuthedClient(clientToken).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyId, serviceId, masterId, date, startTime, null, null, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadJsonAsync<BookingDto>())!;
    }

    private async Task CompleteBookingAsync(string masterToken, Guid bookingId)
    {
        var response = await AuthedClient(masterToken).PatchAsync($"/api/bookings/{bookingId}/complete", null);
        response.EnsureSuccessStatusCode();
    }
}
