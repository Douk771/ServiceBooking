using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class MailingTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private record SendMailResponse(int RecipientCount, string Message);

    private record MailLogRecord(Guid Id, Guid CompanyId, string Subject, string Message, string SentById, int RecipientCount, DateTime SentAt);

    [Fact, TestCase("MAIL-001")]
    public async Task SendMail_WithNoBookings_ReturnsZeroRecipientsAndOk()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/mail",
            new SendMailDto("Hello", "No customers yet"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendMailResponse>();
        body!.RecipientCount.Should().Be(0);
    }

    [Fact, TestCase("MAIL-002")]
    public async Task SendMail_AsUnrelatedUser_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PostAsJsonAsync($"/api/companies/{company.Id}/mail",
            new SendMailDto("Hi", "Body"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("MAIL-003")]
    public async Task SendMail_AsSuperAdmin_Succeeds()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();

        var response = await AuthedClient(admin.Token).PostAsJsonAsync($"/api/companies/{company.Id}/mail",
            new SendMailDto("Admin blast", "Body"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("MAIL-004")]
    public async Task SendMail_CountsDistinctRegisteredClients_ExcludingGuestBookings()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        // Same registered client books twice (different slots) — should count as ONE distinct recipient.
        // Mailing targets clients that have an email on file (you can't email without one).
        var clientUser = await RegisterAsync(email: UniqueEmail("mailclient"));
        var booking1 = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        booking1.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking2 = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null));
        booking2.StatusCode.Should().Be(HttpStatusCode.Created);

        // A manual/guest booking (GuestName set) made by the authenticated owner has ClientId == null
        // (see BookingsController.Create: isManualBooking sets ClientId to null even though the caller
        // is authenticated) — it must NOT count as a recipient.
        var guestBooking = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(14, 0), null, "Walk-in Guest", "+79990000000", null, null));
        guestBooking.StatusCode.Should().Be(HttpStatusCode.Created);
        var guestBookingDto = await guestBooking.Content.ReadJsonAsync<BookingDto>();
        guestBookingDto!.ClientId.Should().BeNull();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/mail",
            new SendMailDto("Promo", "Come back!"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendMailResponse>();
        body!.RecipientCount.Should().Be(1);
    }

    [Fact, TestCase("MAIL-005")]
    public async Task GetHistory_AsUnrelatedUser_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).GetAsync($"/api/companies/{company.Id}/mail");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("MAIL-006")]
    public async Task GetHistory_AfterSendingTwoMails_ReturnsThemNewestFirst()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var first = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/mail",
            new SendMailDto("First blast", "Body 1"));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(50);

        var second = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/mail",
            new SendMailDto("Second blast", "Body 2"));
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/mail");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = await response.Content.ReadFromJsonAsync<List<MailLogRecord>>();
        logs.Should().HaveCount(2);
        logs![0].Subject.Should().Be("Second blast");
        logs[1].Subject.Should().Be("First blast");
    }

    [Fact, TestCase("MAIL-007")]
    public async Task SendMail_WhenTariffDoesNotIncludeMailing_ReturnsPaymentRequired()
    {
        // onlineBooking: false leaves the company without a linked plan config (Free defaults),
        // so AllowMailing resolves to false — mirrors the same "untouched company" fallback used
        // by the online-booking gate in BookingsController.Create.
        var (owner, company) = await CreateOwnerWithCompanyAsync(onlineBooking: false);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/mail",
            new SendMailDto("Hello", "Body"));

        response.StatusCode.Should().Be((HttpStatusCode)402);
    }

    [Fact, TestCase("MAIL-008")]
    public async Task SendMail_WhenTariffIncludesMailing_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(onlineBooking: false);
        var configId = await CreateTestPlanConfigAsync(allowMailing: true);
        await SetSubscriptionAsync(company.Id, configId);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/mail",
            new SendMailDto("Hello", "Body"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("MAIL-009")]
    public async Task SendMail_CountsMultipleDistinctRegisteredClients()
    {
        // Complements MAIL-004 (which proves one client's repeat bookings collapse to 1): here three
        // different registered clients must each be counted once → RecipientCount == 3.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 60);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var hours = new[] { 9, 11, 13 };
        foreach (var h in hours)
        {
            var client = await RegisterAsync(email: UniqueEmail("mailclient"));
            (await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
                new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(h, 0), null, null, null, null, null)))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/mail",
            new SendMailDto("Promo", "Come back!"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<SendMailResponse>();
        body!.RecipientCount.Should().Be(3);
    }
}
