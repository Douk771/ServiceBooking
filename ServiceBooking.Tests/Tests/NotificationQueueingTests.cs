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

    [Fact, TestCase("NTF-Q005")]
    public async Task BookingCreated_WithMultipleServices_RendersCommaJoinedServiceList_NoEmptySegmentsOrUndefined()
    {
        // US-67 (SPEC.md's acceptance criterion, ARCHITECTURE_CYCLE6.md §47.2): the service placeholder
        // in the (unsent, since Provider=logging in Testing) notification body must be a clean
        // comma-joined list for 1, 2 and 5 services — no "undefined", no empty segments, no trailing
        // "/leading comma.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var services = new List<ServiceBooking.API.DTOs.Services.ServiceDto>();
        for (var i = 0; i < 5; i++)
            services.Add(await CreateServiceAsync(owner.Token, company.Id, name: $"Услуга{i}"));
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, services[0].Id, master.UserId, date, new TimeOnly(9, 0),
                null, null, null, null, null, ServiceIds: services.Select(s => s.Id).ToList()));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var confirmed = await db.OutboundNotifications
            .FirstAsync(n => n.BookingId == booking.Id && n.Type == NotificationType.BookingConfirmed);

        foreach (var s in services)
            confirmed.Body.Should().Contain(s.Name);
        confirmed.Body.Should().NotContain("undefined");
        confirmed.Body.Should().NotContain(", ,", "no empty segment between service names");
        confirmed.Body.Should().NotContain(",,");
        // The five names, in order, joined by ", " — this is the exact §47.2 rendering rule.
        confirmed.Body.Should().Contain(string.Join(", ", services.Select(s => s.Name)));
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
        // company's own time zone — NOT assumed to be UTC. Pinned to a fixed UTC-offset zone here
        // (rather than trusting whichever city AnyCityIdAsync happens to pick, e.g. Barnaul/UTC+7)
        // because BookingsController.IsBookableMoment compares Date/StartTime against DateTime.UtcNow
        // directly, with no timezone conversion of its own (a known, pre-existing limitation —
        // ARCHITECTURE_CYCLE6.md §45.5, "Server lives in UTC" — not something this test is meant to
        // exercise). With a non-UTC company zone, a run landing near local midnight can shift Date to
        // "tomorrow" relative to the server's own UTC "today" and get a spurious 409 from that gate —
        // flaky depending only on wall-clock time at test-run time, not on any behavior under test here.
        // Pinning the company to UTC makes the local and "gate" frames of reference identical always.
        await SetCompanyTimeZoneAsync(company.Id, TestTimeZoneId);
        var (date, start) = LocalDateTimeIn(TestTimeZoneId, DateTime.UtcNow.AddMinutes(90));

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
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, farDate, new TimeOnly(9, 0), null, null, null, null, null));
        createResponse.EnsureSuccessStatusCode();
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;

        // Same UTC-pinning as NTF-Q003 above, and for the same reason: IsBookableMoment compares against
        // DateTime.UtcNow with no timezone conversion, so a non-UTC company zone makes this test's
        // pass/fail depend on what time of day (UTC) it happens to run.
        await SetCompanyTimeZoneAsync(company.Id, TestTimeZoneId);
        var (newDate, newStart) = LocalDateTimeIn(TestTimeZoneId, DateTime.UtcNow.AddMinutes(90));
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

    // Used by NTF-Q003/NTF-Q004 to remove wall-clock-time flakiness — see the comments at each call site.
    private const string TestTimeZoneId = "Etc/UTC";

    private async Task SetCompanyTimeZoneAsync(Guid companyId, string timeZoneId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await db.Companies.FirstAsync(c => c.Id == companyId);
        company.TimeZoneId = timeZoneId;
        await db.SaveChangesAsync();
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
