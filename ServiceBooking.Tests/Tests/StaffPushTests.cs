using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 9 (ARCHITECTURE_CYCLE9.md §105, task C12) — Web Push mastеру functional tests, none of
/// which existed before this pass: pass C was built outside the main pipeline and never reached QA.
/// Written against SPEC.md redaction 2 (US-116/US-117/US-123/US-124) and ARCHITECTURE_CYCLE9.md
/// §105.4-§105.8, not against the implementation. Queueing-only assertions ("сам себе не шлёт", "ровно
/// одна строка на устройство", "настройка гасит доставку, но не подписку", "смена владельца endpoint")
/// use the shared "Api" collection (<see cref="ApiTestBase"/>) — no real dispatcher tick needed, same
/// reasoning as <see cref="NotificationQueueingTests"/>. The response-classification/TTL scenarios
/// ("410 удаляет подписку", "429/5xx не удаляет") need the REAL <c>staff-push-dispatch</c> task
/// ticking, so those live in <see cref="StaffPushDispatchTests"/> below, against
/// <see cref="PushDispatchTestFactory"/> — same split <see cref="NotificationQueueingTests"/> /
/// <see cref="NotificationDispatchTests"/> already established for the WhatsApp/MAX side.
/// </summary>
public class StaffPushSubscriptionAndQueueingTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("PUSH-001")]
    public async Task Subscribe_SameEndpoint_DifferentUser_ReassignsTheRow_PreviousOwnerNoLongerReceivesIt()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var masterA = await AddMasterAsync(owner.Token, company.Id);
        var masterB = await AddMasterAsync(owner.Token, company.Id);

        await using var push = new PushEnabledFactory(ConnectionString);
        const string sharedEndpoint = "https://push.example.test/shared-computer-endpoint";
        var subscribeA = await PushAuthedClient(push, masterA.Token).PostAsJsonAsync("/api/push/subscriptions",
            new CreatePushSubscriptionInput(sharedEndpoint, new CreatePushSubscriptionKeysInput("p256dh-a", "auth-a"), "Chrome"));
        subscribeA.EnsureSuccessStatusCode();

        // §105.5 rubeж 1: a SECOND person subscribing the SAME endpoint (shared salon computer) must
        // REASSIGN the row to them, not 409 — a 409 here would leave the previous owner (masterA)
        // still wired to receive the new owner's (masterB's) client names.
        var subscribeB = await PushAuthedClient(push, masterB.Token).PostAsJsonAsync("/api/push/subscriptions",
            new CreatePushSubscriptionInput(sharedEndpoint, new CreatePushSubscriptionKeysInput("p256dh-b", "auth-b"), "Chrome"));
        subscribeB.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.PushSubscriptions.Where(s => s.Endpoint == sharedEndpoint).ToListAsync();

        rows.Should().ContainSingle("the same endpoint must never produce two rows");
        rows.Single().UserId.Should().Be(masterB.UserId, "the SECOND subscriber now owns the row");
        rows.Single().UserId.Should().NotBe(masterA.UserId);
    }

    [Fact, TestCase("PUSH-002")]
    public async Task DeleteCurrent_IsIdempotent_SecondCallStillReturns204()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        await using var push = new PushEnabledFactory(ConnectionString);
        const string endpoint = "https://push.example.test/logout-endpoint";
        await PushAuthedClient(push, master.Token).PostAsJsonAsync("/api/push/subscriptions",
            new CreatePushSubscriptionInput(endpoint, new CreatePushSubscriptionKeysInput("p256dh", "auth"), null));

        var first = await DeleteCurrentAsync(push, master.Token, endpoint);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var second = await DeleteCurrentAsync(push, master.Token, endpoint);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent, "§105.5 rubeж 2: logout must succeed even with no matching row left");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint)).Should().BeFalse();
    }

    [Fact, TestCase("PUSH-003")]
    public async Task BookingCreatedByClient_QueuesExactlyOneRow_PerSubscribedDevice()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        await using var push = new PushEnabledFactory(ConnectionString);
        // Three devices — "три устройства — три уведомления" (§105.6), by design.
        string[] endpoints =
        [
            "https://push.example.test/device-1", "https://push.example.test/device-2", "https://push.example.test/device-3",
        ];
        foreach (var endpoint in endpoints)
        {
            var response = await PushAuthedClient(push, master.Token).PostAsJsonAsync("/api/push/subscriptions",
                new CreatePushSubscriptionInput(endpoint, new CreatePushSubscriptionKeysInput("p256dh", "auth"), null));
            response.EnsureSuccessStatusCode();
        }
        // A second registration of the SAME first endpoint (e.g. page refresh / second tab) must not
        // add a fourth row — upsert by endpoint, not append.
        (await PushAuthedClient(push, master.Token).PostAsJsonAsync("/api/push/subscriptions",
            new CreatePushSubscriptionInput(endpoints[0], new CreatePushSubscriptionKeysInput("p256dh", "auth"), null))).EnsureSuccessStatusCode();

        var clientUser = await RegisterAsync();
        var response2 = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        response2.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await response2.Content.ReadJsonAsync<BookingDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.StaffPushNotifications.Where(n => n.BookingId == booking.Id).ToListAsync();

        rows.Should().HaveCount(3, "one row per subscribed device — never one row per event");
        rows.Select(r => r.SubscriptionId).Should().OnlyHaveUniqueItems();
        rows.Should().OnlyContain(r => r.Status == NotificationStatus.Pending && r.Type == NotificationType.StaffBookingCreated);
    }

    [Fact, TestCase("PUSH-004")]
    public async Task MasterBooksHimself_QueuesZeroRows()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(onlineBooking: false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        await using var push = new PushEnabledFactory(ConnectionString);
        await PushAuthedClient(push, master.Token).PostAsJsonAsync("/api/push/subscriptions",
            new CreatePushSubscriptionInput("https://push.example.test/self-device", new CreatePushSubscriptionKeysInput("p256dh", "auth"), null));

        // §105.6: "если запись создал сам мастер — ноль строк" — master manually records a walk-in,
        // AS the authenticated master, for that same master's own schedule.
        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, "Walk-in Client", "+79990001122", null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.StaffPushNotifications.Where(n => n.BookingId == booking.Id).ToListAsync();
        rows.Should().BeEmpty("a master recording their own booking must never notify themselves");
    }

    [Fact, TestCase("PUSH-005")]
    public async Task StaffPushEnabled_False_QueuesZeroRows_ButSubscriptionRowSurvives()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        await using var push = new PushEnabledFactory(ConnectionString);
        const string endpoint = "https://push.example.test/disabled-company-device";
        await PushAuthedClient(push, master.Token).PostAsJsonAsync("/api/push/subscriptions",
            new CreatePushSubscriptionInput(endpoint, new CreatePushSubscriptionKeysInput("p256dh", "auth"), null));

        var settingsResponse = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/staff-push-settings", new { staffPushEnabled = false });
        settingsResponse.EnsureSuccessStatusCode();

        var clientUser = await RegisterAsync();
        var response = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await response.Content.ReadJsonAsync<BookingDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.StaffPushNotifications.Where(n => n.BookingId == booking.Id).ToListAsync();
        rows.Should().BeEmpty("StaffPushEnabled=false must hold delivery at QUEUE time too");

        // §105.4/US-117: turning the COMPANY setting off must never touch the subscription row itself —
        // only re-enabling it should be needed to resume delivery, no re-subscribe.
        (await db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint)).Should().BeTrue(
            "disabling the company setting must not delete the master's device subscription");
    }

    private static HttpClient PushAuthedClient(PushEnabledFactory push, string token)
    {
        var client = push.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpResponseMessage> DeleteCurrentAsync(PushEnabledFactory push, string token, string endpoint)
    {
        var client = PushAuthedClient(push, token);
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions/current")
        {
            Content = JsonContent.Create(new DeleteCurrentPushSubscriptionInput(endpoint)),
        };
        return await client.SendAsync(request);
    }
}

/// <summary>
/// The response-classification/TTL half of task C12 — needs the REAL <c>staff-push-dispatch</c> task
/// ticking (<see cref="PushDispatchTestFactory"/>), same "behavior the shared Testing configuration
/// deliberately glues shut" situation <see cref="NotificationDispatchTests"/> exists for.
/// </summary>
public class StaffPushDispatchTests(TestDatabaseFixture fixture) : IClassFixture<TestDatabaseFixture>
{
    private readonly int _recorded = RecordTestClassOnConstruction(fixture, nameof(StaffPushDispatchTests));

    private static int RecordTestClassOnConstruction(TestDatabaseFixture fixture, string className)
    {
        fixture.RecordTestClass(className);
        return 0;
    }

    [Fact, TestCase("PUSH-006")]
    public async Task RealRunner_410Gone_DeletesSubscription_SameProcessing_NoRetry()
    {
        await using var factory = new PushDispatchTestFactory(fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (subscription, row) = await SeedPendingRowAsync(factory, db);
        factory.Sender.EnqueueOutcomeFor(subscription.Id, new WebPushSendOutcome.Gone());

        await WaitForAsync(() => factory.Sender.Calls.Any(c => c.SubscriptionId == subscription.Id), 20);
        await WaitForRowStatusAsync(factory, row.Id, NotificationStatus.Skipped, 20);

        await db.Entry(row).ReloadAsync();
        row.Status.Should().Be(NotificationStatus.Skipped);
        row.Reason.Should().Be(NotificationReason.PushSubscriptionGone);

        (await db.PushSubscriptions.AnyAsync(s => s.Id == subscription.Id)).Should().BeFalse(
            "410 Gone must delete the dead subscription in the SAME pass");
        factory.Sender.Calls.Count(c => c.SubscriptionId == subscription.Id).Should().Be(1, "no retry after 410");
    }

    [Fact, TestCase("PUSH-007")]
    public async Task RealRunner_429_DoesNotDeleteSubscription_StaysPendingForRetry()
    {
        await using var factory = new PushDispatchTestFactory(fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (subscription, row) = await SeedPendingRowAsync(factory, db);
        factory.Sender.EnqueueOutcomeFor(subscription.Id, new WebPushSendOutcome.Transient("simulated 429"));

        await WaitForAsync(() => factory.Sender.Calls.Any(c => c.SubscriptionId == subscription.Id), 20);
        // Give the (single) attempt time to be recorded — the row stays Pending with a future
        // NextAttemptAtUtc rather than transitioning to any terminal state.
        await Task.Delay(500);

        await db.Entry(row).ReloadAsync();
        row.Status.Should().Be(NotificationStatus.Pending, "429/5xx is a TRANSIENT failure — it must not fail the row outright");
        row.NextAttemptAtUtc.Should().NotBeNull();
        row.AttemptCount.Should().Be(1);

        (await db.PushSubscriptions.AnyAsync(s => s.Id == subscription.Id)).Should().BeTrue(
            "a transient failure must never delete the subscription — only 404/410 does");
    }

    [Fact, TestCase("PUSH-008")]
    public async Task RealRunner_MasterRemovedFromCompany_HoldsDeliveryEvenThoughAlreadyQueued()
    {
        await using var factory = new PushDispatchTestFactory(fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (subscription, row) = await SeedPendingRowAsync(factory, db);

        // §105.6/R4: remove the master's OWN membership row AFTER the row was already queued — rights
        // are re-checked at SEND time, not trusted from queue time.
        var membership = await db.CompanyMembers.FirstAsync(m => m.UserId == row.UserId && m.CompanyId == row.CompanyId);
        db.CompanyMembers.Remove(membership);
        await db.SaveChangesAsync();

        await WaitForRowStatusAsync(factory, row.Id, NotificationStatus.Skipped, 20);

        await db.Entry(row).ReloadAsync();
        row.Status.Should().Be(NotificationStatus.Skipped);
        row.Reason.Should().Be(NotificationReason.MasterNoLongerInCompany);
        factory.Sender.Calls.Should().NotContain(c => c.SubscriptionId == subscription.Id,
            "a master already removed from the company must never actually be sent to, even for an already-queued row");
    }

    // ── Seeding helpers ──────────────────────────────────────────────────────────────────────────

    private static async Task<(PushSubscription Subscription, StaffPushNotification Row)> SeedPendingRowAsync(
        PushDispatchTestFactory factory, AppDbContext db)
    {
        var userId = $"qa9-push-{Guid.NewGuid():N}";
        var companyId = Guid.NewGuid();
        var billingAccount = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = userId };
        var company = new ServiceBooking.Core.Entities.Company
        {
            Id = companyId, Name = "QA Push Co", Slug = $"qa-push-{Guid.NewGuid():N}"[..20], OwnerUserId = userId,
            BillingAccountId = billingAccount.Id, TimeZoneId = "Etc/UTC",
        };
        var user = new AppUser
        {
            Id = userId, UserName = $"{userId}@test.local", Email = $"{userId}@test.local",
            PhoneNumber = "79990000999", FirstName = "QA", LastName = "Master",
        };
        db.BillingAccounts.Add(billingAccount);
        db.Companies.Add(company);
        db.Users.Add(user);
        db.CompanyMembers.Add(new ServiceBooking.Core.Entities.CompanyMember
        {
            Id = Guid.NewGuid(), CompanyId = companyId, UserId = userId, Role = UserRole.Master,
        });

        var subscription = new PushSubscription
        {
            Id = Guid.NewGuid(), UserId = userId, Endpoint = $"https://push.example.test/{Guid.NewGuid():N}",
            DeviceLabel = "QA device",
        };
        subscription.P256dhCiphertext = ServiceBooking.API.Services.Notifications.SecretProtector.Encrypt(
            "p256dh-value", PushDispatchTestFactory.TestEncryptionKeyBase64, $"push-subscription:{subscription.Id}");
        subscription.AuthCiphertext = ServiceBooking.API.Services.Notifications.SecretProtector.Encrypt(
            "auth-value", PushDispatchTestFactory.TestEncryptionKeyBase64, $"push-subscription:{subscription.Id}");
        db.PushSubscriptions.Add(subscription);

        var row = new StaffPushNotification
        {
            Id = Guid.NewGuid(), UserId = userId, CompanyId = companyId, SubscriptionId = subscription.Id,
            Type = NotificationType.StaffBookingCreated, Payload = "{\"title\":\"Новая запись\"}",
            Status = NotificationStatus.Pending, ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            IdempotencyKey = $"StaffBookingCreated:{Guid.NewGuid()}:{userId}:{subscription.Id}",
        };
        db.StaffPushNotifications.Add(row);
        await db.SaveChangesAsync();

        return (subscription, row);
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(200);
        }
        predicate().Should().BeTrue($"condition did not become true within {timeoutSeconds}s");
    }

    private static async Task WaitForRowStatusAsync(PushDispatchTestFactory factory, Guid rowId, NotificationStatus expected, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var current = await db.StaffPushNotifications.AsNoTracking().FirstAsync(n => n.Id == rowId);
            if (current.Status == expected) return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"StaffPushNotification {rowId} did not reach status {expected} within {timeoutSeconds}s");
    }
}
