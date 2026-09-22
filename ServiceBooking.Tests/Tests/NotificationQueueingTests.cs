using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 4 (SPEC.md §5.3/§6.3/§8, ARCHITECTURE_CYCLE4.md §25.3) — where a booking event becomes a
/// queued <see cref="OutboundNotification"/> row. Runs against the shared "Api" collection
/// (<see cref="CustomWebApplicationFactory"/>, no <c>ScheduledTaskRunner</c> ticking) — every assertion
/// here is about what gets QUEUED and with what status/reason, not about actual delivery (that's
/// <c>NotificationDispatchTests.cs</c>, ARCHITECTURE_CYCLE4.md §27).
/// </summary>
public class NotificationQueueingTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("NTF-Q001")]
    public async Task BookingCreated_QueuesConfirmedAndReminder_BothPending()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        var channel = await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        // CYCLE5-BREAKING (ARCHITECTURE_CYCLE5.md §52.3, T-24): the shipped `AccountsOnly` gate mode
        // blocks a registered recipient who never granted PdnConsent/ProviderDelivery — without this the
        // rows below would queue as Skipped/NoProviderDeliveryConsent, not Pending.
        await GrantProviderDeliveryConsentAsync(clientUser.Token);
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.OutboundNotifications.Where(n => n.BookingId == booking.Id).ToListAsync();
        rows.Should().Contain(r => r.Type == NotificationType.BookingConfirmed && r.Status == NotificationStatus.Pending);
        rows.Should().Contain(r => r.Type == NotificationType.Reminder && r.Status == NotificationStatus.Pending);
        rows.Should().OnlyContain(r => r.ChannelId == channel.Id);
    }

    // LGL-072-01 (SPEC.md §6.3 T-24, ARCHITECTURE_CYCLE5.md §52.3 "AccountsOnly"). The mirror of the test
    // above: a registered client who never granted ProviderDelivery consent must be BLOCKED, with the
    // dedicated reason — this is the gate the default mode exists to enforce.
    [Fact, TestCase("LGL-072-01")]
    public async Task BookingCreated_RegisteredClientWithoutProviderDeliveryConsent_RowsSkipped_WithReason()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync(); // deliberately no GrantProviderDeliveryConsentAsync call
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 30), null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, "declining a purely optional consent must never block the booking itself (US-67 п.4)");
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.OutboundNotifications.Where(n => n.BookingId == booking.Id).ToListAsync();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.Status == NotificationStatus.Skipped && r.Reason == NotificationReason.NoProviderDeliveryConsent);
    }

    [Fact, TestCase("NTF-Q002")]
    public async Task OptedOutClient_BookingCreated_RowsQueuedButSkipped_WithReason()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.NotificationOptOuts.Add(new NotificationOptOut
            {
                Id = Guid.NewGuid(), Phone = clientUser.Phone, Source = OptOutSource.Cabinet, UserId = clientUser.UserId,
            });
            await db.SaveChangesAsync();
        }

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, "the booking itself must succeed regardless of notification eligibility");
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await verifyDb.OutboundNotifications.Where(n => n.BookingId == booking.Id).ToListAsync();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.Status == NotificationStatus.Skipped && r.Reason == NotificationReason.RecipientOptedOut);
    }

    [Fact, TestCase("NTF-Q003")]
    public async Task Cancel_VisitLessThanThresholdAway_StillQueuesCancellationNotification()
    {
        // Regression this cycle fixed: cancellation/reschedule must NOT be subject to the "minutes before
        // visit" threshold that reminders are — only Reminder is gated by MinLeadMinutes (SPEC §6.3 п.2,
        // ARCHITECTURE_CYCLE4.md §25.3).
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id);
        await SetMinLeadMinutesAsync(company.Id, minLeadMinutes: 600, reminderLeadMinutes: 1440); // 10h threshold
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);

        // A visit only 90 minutes away — well under the 600-minute threshold. Date/StartTime are the
        // company's own LOCAL wall clock (US-30), so "soon" (a UTC instant) must be converted through the
        // company's own time zone, not assumed to be UTC — this company was NOT created with Moscow's
        // zone (AnyCityIdAsync picks whichever seeded city comes first, e.g. Barnaul/UTC+7).
        var (date, start) = LocalDateTimeIn(company.TimeZoneId, DateTime.UtcNow.AddMinutes(90));

        // Staff manual booking (Q7): bypasses the working-hours grid, which this test doesn't set up for
        // "today" — the only thing under test is the cancel-vs-threshold gate, not slot availability.
        var createResponse = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, start, null, "Walk-in", "+79990001234", null, null));
        createResponse.EnsureSuccessStatusCode();
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;

        var cancelResponse = await AuthedClient(owner.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/cancel", "salon cancelled");
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.OutboundNotifications.Where(n => n.BookingId == booking.Id).ToListAsync();
        rows.Should().Contain(r => r.Type == NotificationType.BookingCancelled && r.Status == NotificationStatus.Pending,
            "cancellation must be queued for delivery even though the visit is inside the reminder threshold");
    }

    [Fact, TestCase("NTF-Q004")]
    public async Task Reschedule_VisitLessThanThresholdAway_QueuesReschedule_ButNewReminderIsSkippedByThreshold()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id);
        await SetMinLeadMinutesAsync(company.Id, minLeadMinutes: 600, reminderLeadMinutes: 1440);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var farDate = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, farDate);

        var clientUser = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(clientUser.Token); // AccountsOnly gate (§52.3) — see NTF-Q001's own note
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, farDate, new TimeOnly(9, 0), null, null, null, null, null));
        createResponse.EnsureSuccessStatusCode();
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;

        var (newDate, newStart) = LocalDateTimeIn(company.TimeZoneId, DateTime.UtcNow.AddMinutes(90));
        var rescheduleResponse = await AuthedClient(owner.Token).PatchAsJsonAsync($"/api/bookings/{booking.Id}/reschedule",
            new RescheduleDto(newDate, newStart));
        rescheduleResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.OutboundNotifications.Where(n => n.BookingId == booking.Id).ToListAsync();
        rows.Should().Contain(r => r.Type == NotificationType.BookingRescheduled && r.Status == NotificationStatus.Pending,
            "the reschedule notice itself is not threshold-gated");
        rows.Where(r => r.Type == NotificationType.Reminder).OrderByDescending(r => r.Generation).First()
            .Should().Match<OutboundNotification>(r =>
                r.Status == NotificationStatus.Skipped && r.Reason == NotificationReason.BelowMinimumLeadTime,
                "the NEW reminder generation is correctly threshold-gated, asymmetric from the reschedule notice itself");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private static (DateOnly Date, TimeOnly Time) LocalDateTimeIn(string? timeZoneId, DateTime utcInstant)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId ?? "UTC");
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcInstant, tz);
        return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    protected async Task GiveNotificationCapablePlanAsync(string ownerUserId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig).FirstAsync(s => s.OwnerUserId == ownerUserId);
        sub.PlanConfig!.AllowNotificationChannel = true;
        await db.SaveChangesAsync();
    }

    private async Task<NotificationChannel> SeedConnectedAssignedChannelAsync(string ownerUserId, Guid companyId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // ARCHITECTURE_CYCLE4.md §27.1's isolation invariant: no functional test outside the
        // "NotificationDispatch" collection may leave a Connected, paid, assigned channel with Pending
        // rows behind in the shared "servicebooking_test" database — a REAL background dispatcher
        // (NotificationDispatchTestFactory) scans ALL Pending rows platform-wide regardless of which test
        // created them, so a Connected channel left here would be silently picked up (and its queue
        // drained) by an unrelated test's dispatcher run. NotificationGate.Evaluate (what THIS test class
        // actually exercises) deliberately does not look at ChannelState at all, so Disconnected serves
        // every assertion here identically to Connected while staying invisible to the real dispatcher,
        // which skips any non-Connected channel's rows outright (NotificationDispatchTask "channel is
        // null or not connected" branch).
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerUserId, State = ChannelState.Disconnected,
            PhoneNumber = "79990009999", ProviderInstanceId = Unique("instance"),
            PaidFromUtc = DateTime.UtcNow.AddDays(-1), PaidUntilUtc = DateTime.UtcNow.AddDays(30),
            ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1),
        };
        db.NotificationChannels.Add(channel);
        db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = companyId, AssignedByUserId = ownerUserId,
        });
        await db.SaveChangesAsync();
        return channel;
    }

    private async Task SetMinLeadMinutesAsync(Guid companyId, int minLeadMinutes, int reminderLeadMinutes)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CompanyNotificationSettings.Add(new CompanyNotificationSettings
        {
            CompanyId = companyId, MinLeadMinutes = minLeadMinutes, ReminderLeadMinutes = reminderLeadMinutes,
            EnabledTypeMask = CompanyNotificationSettings.DefaultEnabledTypeMask,
        });
        await db.SaveChangesAsync();
    }
}
