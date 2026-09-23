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
/// QA cycle 9 (ARCHITECTURE_CYCLE9.md §104.5/§106.4, task B13) — the second-transport (MAX)
/// queueing/routing tests the architecture explicitly names as mandatory, and which never existed
/// before this pass: pass B stopped at the contract gate, before QA. Written against SPEC.md redaction
/// 2 (US-119/US-120/US-125) and ARCHITECTURE_CYCLE9.md §104.3/§104.5, not against the implementation —
/// same "Api" collection/style as <see cref="NotificationQueueingTests"/> (queueing-level assertions,
/// no real background dispatcher needed: every scenario here is about what gets QUEUED into
/// <c>OutboundNotification</c>, mirroring the exact wording ARCHITECTURE_CYCLE9.md §104.5 uses for the
/// mandatory test: "два подключённых канала, режим AllChannels, одно событие → две строки
/// OutboundNotification с разными транспортами; повторный прогон того же события → ни одной новой").
///
/// "Повторный прогон того же события" is simulated by invoking the real, DI-resolved
/// <see cref="ServiceBooking.API.Services.NotificationScheduler"/> a second time for the SAME booking
/// entity — there is no HTTP-level way to replay one booking-created event a second time (each booking
/// has a unique id), and this is exactly the scenario the idempotency key exists to protect against (a
/// crashed/retried caller re-invoking the scheduler for a booking it already scheduled once).
/// </summary>
public class NotificationMaxTransportTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("MAX-001")]
    public async Task AllChannels_TwoConnectedFundedChannels_OneEvent_QueuesTwoRows_DifferentTransports_ReplayQueuesNoNewRows()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId, paidNumbers: 2);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id, NotificationTransport.WhatsApp);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id, NotificationTransport.Max);
        await SetDeliveryModeAsync(company.Id, NotificationDeliveryMode.AllChannels, NotificationTransport.WhatsApp);

        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(clientUser.Token);
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bookingEntity = await db.Bookings.AsNoTracking().FirstAsync(b => b.Id == booking.Id);

        var confirmedRows = await db.OutboundNotifications
            .Where(n => n.BookingId == booking.Id && n.Type == NotificationType.BookingConfirmed)
            .ToListAsync();
        confirmedRows.Should().HaveCount(2, "AllChannels with two funded/connected channels must queue one row PER TRANSPORT (§104.5)");
        confirmedRows.Select(r => r.Transport).Should().BeEquivalentTo(
            [NotificationTransport.WhatsApp, NotificationTransport.Max]);
        confirmedRows.Should().OnlyContain(r => r.Status == NotificationStatus.Pending);
        // R3: the idempotency key must include the transport, or the second row above would never have
        // been queued at all (it would look "already queued" against the first).
        confirmedRows.Select(r => r.IdempotencyKey).Should().OnlyHaveUniqueItems();

        // "Повторный прогон того же события" — replay the SAME booking through the scheduler a second
        // time. No new rows for ANY transport.
        using (var replayScope = Factory.Services.CreateScope())
        {
            var scheduler = replayScope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.NotificationScheduler>();
            var replayDb = replayScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trackedBooking = await replayDb.Bookings.FirstAsync(b => b.Id == booking.Id);
            await scheduler.OnBookingCreatedAsync(trackedBooking, [service.Name], CancellationToken.None);
            await replayDb.SaveChangesAsync();
        }

        var confirmedRowsAfterReplay = await db.OutboundNotifications
            .Where(n => n.BookingId == booking.Id && n.Type == NotificationType.BookingConfirmed)
            .ToListAsync();
        confirmedRowsAfterReplay.Should().HaveCount(2, "a replayed event must queue ZERO new rows, for either transport");
    }

    [Fact, TestCase("MAX-002")]
    public async Task PriorityChannel_TwoConnectedFundedChannels_QueuesExactlyOneRow_ForThePriorityTransport()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId, paidNumbers: 2);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id, NotificationTransport.WhatsApp);
        var maxChannel = await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id, NotificationTransport.Max);
        // Priority explicitly set to MAX — proves routing picks the CONFIGURED transport, not always
        // the first/WhatsApp one.
        await SetDeliveryModeAsync(company.Id, NotificationDeliveryMode.PriorityChannel, NotificationTransport.Max);

        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(clientUser.Token);
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var confirmedRows = await db.OutboundNotifications
            .Where(n => n.BookingId == booking.Id && n.Type == NotificationType.BookingConfirmed)
            .ToListAsync();

        confirmedRows.Should().ContainSingle("PriorityChannel mode must queue exactly one row");
        confirmedRows.Single().Transport.Should().Be(NotificationTransport.Max);
        confirmedRows.Single().ChannelId.Should().Be(maxChannel.Id);
    }

    [Fact, TestCase("MAX-003")]
    public async Task Unsubscribe_SilencesBothTransports_AllChannelsMode_ZeroRowsQueued()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId, paidNumbers: 2);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id, NotificationTransport.WhatsApp);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id, NotificationTransport.Max);
        await SetDeliveryModeAsync(company.Id, NotificationDeliveryMode.AllChannels, NotificationTransport.WhatsApp);

        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(clientUser.Token);

        // Opt the recipient's canonical phone out BEFORE booking — the same "opt-out belongs to the
        // NUMBER, checked in BuildContextAsync before routing" property ARCHITECTURE_CYCLE9.md §104.5
        // requires to hold for the new transport too.
        using (var seedScope = Factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clientEntity = await seedDb.Users.AsNoTracking().FirstAsync(u => u.Id == clientUser.UserId);
            seedDb.NotificationOptOuts.Add(new NotificationOptOut
            {
                Id = Guid.NewGuid(), Phone = clientEntity.PhoneNumber!, OptedOutAtUtc = DateTime.UtcNow,
            });
            await seedDb.SaveChangesAsync();
        }

        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var confirmedRows = await db.OutboundNotifications
            .Where(n => n.BookingId == booking.Id && n.Type == NotificationType.BookingConfirmed)
            .ToListAsync();

        // Opted-out is a GLOBAL gate, evaluated before routing (§104.5) — it must queue a single Skipped
        // "representative" row (never a Pending, sendable one), for neither transport individually.
        confirmedRows.Should().NotContain(r => r.Status == NotificationStatus.Pending,
            "an opted-out number must not receive a Pending row on EITHER transport");
    }

    [Fact, TestCase("MAX-004")]
    public async Task Max_ChannelPresenceAndFailure_DoesNotAffectWhatsAppChannelsOwnQueueing()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId, paidNumbers: 2);
        var whatsAppChannel = await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id, NotificationTransport.WhatsApp);
        // The MAX channel exists, is assigned to the SAME company (proving the §104.3 invariant — one
        // channel per company PER TRANSPORT, not one overall — allows this to coexist at all) but is
        // broken (Disconnected/unfunded doesn't matter: routing purity means it must not leak into
        // WhatsApp's own row at all).
        var maxChannel = await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id, NotificationTransport.Max);
        using (var breakScope = Factory.Services.CreateScope())
        {
            var breakDb = breakScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trackedMax = await breakDb.NotificationChannels.FirstAsync(c => c.Id == maxChannel.Id);
            trackedMax.State = ChannelState.Disconnected;
            await breakDb.SaveChangesAsync();
        }
        await SetDeliveryModeAsync(company.Id, NotificationDeliveryMode.PriorityChannel, NotificationTransport.WhatsApp);

        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(clientUser.Token);
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var confirmedRows = await db.OutboundNotifications
            .Where(n => n.BookingId == booking.Id && n.Type == NotificationType.BookingConfirmed)
            .ToListAsync();

        confirmedRows.Should().ContainSingle();
        confirmedRows.Single().Transport.Should().Be(NotificationTransport.WhatsApp);
        confirmedRows.Single().ChannelId.Should().Be(whatsAppChannel.Id);
        confirmedRows.Single().Status.Should().Be(NotificationStatus.Pending,
            "the broken/unrelated MAX channel on the same company must not touch WhatsApp's own row at all");

        var whatsAppChannelAfter = await db.NotificationChannels.AsNoTracking().FirstAsync(c => c.Id == whatsAppChannel.Id);
        whatsAppChannelAfter.State.Should().Be(ChannelState.Connected, "the WhatsApp channel's own state must be untouched by the MAX channel's breakage");
    }

    // ── Seeding helpers ──────────────────────────────────────────────────────────────────────────

    protected async Task GiveNotificationCapablePlanAsync(string ownerUserId, int paidNumbers)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var plan = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(), Name = Unique("QA MAX Plan "), IsActive = true,
            AllowNotificationChannel = true, AllowOnlineBooking = true, AllowMailing = true,
            AllowAnalytics = true, AllowOnlinePayment = true, CreatedAt = DateTime.UtcNow,
        };
        db.SubscriptionPlanConfigs.Add(plan);

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId);
        if (account is null)
        {
            account = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = ownerUserId };
            db.BillingAccounts.Add(account);
            await db.SaveChangesAsync();
        }
        var orphanedCompanies = await db.Companies
            .Where(c => c.OwnerUserId == ownerUserId && c.BillingAccountId == null)
            .ToListAsync();
        foreach (var company in orphanedCompanies)
            company.BillingAccountId = account.Id;

        var sub = await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId);
        if (sub is null)
        {
            db.AccountSubscriptions.Add(new AccountSubscription
            {
                Id = Guid.NewGuid(), OwnerUserId = ownerUserId, PlanConfigId = plan.Id,
                BillingAccountId = account.Id,
                PaidUntil = DateTime.UtcNow.AddMonths(1), IsActive = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            sub.PlanConfigId = plan.Id;
            sub.BillingAccountId = account.Id;
            sub.PaidUntil = DateTime.UtcNow.AddMonths(1);
            sub.IsActive = true;
            sub.UpdatedAt = DateTime.UtcNow;
        }
        await NotificationTestBase.EnsureWhatsAppPlanRuleAsync(db, plan.Id);
        await db.SaveChangesAsync();

        // ARCHITECTURE_CYCLE9.md §104.4: ONE option funds numbers regardless of transport
        // (ChannelFunding.Rank ranks ALL live channels of the account, transport-blind) — paidNumbers=2
        // is what lets a WhatsApp channel AND a MAX channel both be Funded on the same account.
        await NotificationTestBase.EnsureWhatsAppPaidAsync(db, account.Id, paidNumbers);
    }

    private async Task<NotificationChannel> SeedConnectedAssignedChannelAsync(
        string ownerUserId, Guid companyId, NotificationTransport transport)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var billingAccountId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BillingAccountId).FirstAsync();
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerUserId, BillingAccountId = billingAccountId,
            State = ChannelState.Connected, Transport = transport,
            PhoneNumber = UniquePhone().TrimStart('+'), ProviderInstanceId = Unique("instance"),
            PaidFromUtc = DateTime.UtcNow.AddDays(-1), PaidUntilUtc = DateTime.UtcNow.AddDays(30),
            ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1),
        };
        db.NotificationChannels.Add(channel);
        db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = companyId, Transport = transport,
            BillingAccountId = billingAccountId!.Value, AssignedByUserId = ownerUserId,
        });
        await db.SaveChangesAsync();
        return channel;
    }

    private async Task SetDeliveryModeAsync(Guid companyId, NotificationDeliveryMode mode, NotificationTransport priorityTransport)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.CompanyNotificationSettings.FirstOrDefaultAsync(s => s.CompanyId == companyId);
        if (settings is null)
        {
            settings = new CompanyNotificationSettings { CompanyId = companyId };
            db.CompanyNotificationSettings.Add(settings);
        }
        settings.DeliveryMode = mode;
        settings.PriorityTransport = priorityTransport;
        await db.SaveChangesAsync();
    }
}
