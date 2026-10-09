using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 40 (ARCHITECTURE_CYCLE40.md §40.11, §40.11a): the part of the customer consent the server decides — the messenger mark of a booking (guest, staff, signed-in
/// customer) and the sender's name in the text of a salon message.
/// </summary>
public class Cycle40MessagingTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>A bound (once), paid, first number of the owner's account; Disconnected on purpose so that the real dispatcher of other tests never sends from it.</summary>
    private async Task SeedConnectedAssignedChannelAsync(string ownerUserId, Guid companyId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var billingAccountId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BillingAccountId).FirstAsync();
        db.NotificationChannels.Add(new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerUserId, BillingAccountId = billingAccountId, State = ChannelState.Disconnected,
            PhoneNumber = "79990009999", ProviderInstanceId = Unique("instance"),
            ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();
        await NotificationTestBase.EnsureWhatsAppPaidAsync(db, billingAccountId!.Value);
    }

    [Fact, TestCase("CY40-MSG-01")]
    public async Task GuestBooking_StoresTheMarkAndTheConsentVersion_AndASalonMessageNamesTheSender()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var guest = Factory.CreateClient();
        var created = await guest.PostAsJsonAsync("/api/bookings", new
        {
            companyId = company.Id, serviceId = service.Id, masterId = master.UserId, date, startTime = "09:00:00",
            guestName = "Гость", guestPhone = "+79990001111", notifyByMessenger = true,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var booking = (await created.Content.ReadJsonAsync<ServiceBooking.API.DTOs.Bookings.BookingDto>())!;
        booking.NotifyByMessenger.Should().BeTrue();
        booking.MessengerConsentAtUtc.Should().NotBeNull();
        booking.MessengerConsentByStaff.Should().BeFalse();

        var stored = await DbAsync(db => db.Bookings.AsNoTracking().FirstAsync(b => b.Id == booking.Id));
        stored.MessengerConsentVersion.Should().StartWith("fallback:", "no lawyer's text in the manifest yet — the fallback hash is the version");

        var queued = await DbAsync(db => db.OutboundNotifications.AsNoTracking()
            .Where(n => n.BookingId == booking.Id && n.Type == NotificationType.BookingConfirmed).ToListAsync());
        queued.Should().ContainSingle(n => n.Status == NotificationStatus.Pending);
        queued[0].Body.Should().StartWith($"{company.Name}:", "one number serves several companies — the first line names the sender");
    }

    [Fact, TestCase("CY40-MSG-02")]
    public async Task StaffBooking_WithoutTheMark_IsNotSentToAPhoneThatIsNoAccount_AndWithTheMark_IsStoredWithWhoTicked()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var staff = AuthedClient(owner.Token);

        var without = await staff.PostAsJsonAsync("/api/bookings", new
        {
            companyId = company.Id, serviceId = service.Id, masterId = master.UserId, date, startTime = "09:00:00", guestName = "Клиент", guestPhone = "+79990002222",
        });
        without.EnsureSuccessStatusCode();
        var plain = (await without.Content.ReadJsonAsync<ServiceBooking.API.DTOs.Bookings.BookingDto>())!;
        plain.NotifyByMessenger.Should().BeNull();
        (await DbAsync(db => db.OutboundNotifications.AsNoTracking().Where(n => n.BookingId == plain.Id && n.Type == NotificationType.BookingConfirmed).ToListAsync()))
            .Should().OnlyContain(n => n.Status == NotificationStatus.Skipped && n.Reason == NotificationReason.NoProviderDeliveryConsent);

        var withMark = await staff.PostAsJsonAsync("/api/bookings", new
        {
            companyId = company.Id, serviceId = service.Id, masterId = master.UserId, date, startTime = "10:00:00", guestName = "Клиент", guestPhone = "+79990003333",
            notifyByMessenger = true,
        });
        withMark.EnsureSuccessStatusCode();
        var marked = (await withMark.Content.ReadJsonAsync<ServiceBooking.API.DTOs.Bookings.BookingDto>())!;
        marked.NotifyByMessenger.Should().BeTrue();
        marked.MessengerConsentByStaff.Should().BeTrue("the staff's tick means 'the client agreed' and records who ticked it");
        (await DbAsync(db => db.Bookings.AsNoTracking().FirstAsync(b => b.Id == marked.Id))).MessengerConsentByUserId.Should().Be(owner.UserId);
    }

    [Fact, TestCase("CY40-MSG-03")]
    public async Task SignedInCustomerMark_WritesTheProviderDeliveryConsent_AndPreferencesShowIt()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var customer = await RegisterAsync();
        var customerClient = AuthedClient(customer.Token);
        (await customerClient.GetFromJsonAsync<NotificationPreferencesDto>("/api/notifications/preferences"))!.ProviderDeliveryConsent.Should().BeFalse();

        var created = await customerClient.PostAsJsonAsync("/api/bookings", new
        {
            companyId = company.Id, serviceId = service.Id, masterId = master.UserId, date, startTime = "09:00:00", notifyByMessenger = true,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        (await customerClient.GetFromJsonAsync<NotificationPreferencesDto>("/api/notifications/preferences"))!.ProviderDeliveryConsent.Should().BeTrue();
        (await DbAsync(db => db.ConsentRecords.AsNoTracking()
            .AnyAsync(c => c.UserId == customer.UserId && c.DocumentKey == "PdnConsent" && c.Purpose == ConsentPurpose.ProviderDelivery && c.RevokedAtUtc == null))).Should().BeTrue();
    }
}
