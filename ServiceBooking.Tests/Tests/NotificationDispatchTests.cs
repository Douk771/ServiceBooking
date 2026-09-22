using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// ARCHITECTURE_CYCLE4.md §27 — sequential end-to-end scenarios exercising the REAL
/// <c>ScheduledTaskRunner</c> ticking <c>NotificationDispatchTask</c> against
/// <see cref="NotificationDispatchTestFactory"/>. NOTE for code-reviewer/QA: by this repository's own
/// stated unit-vs-functional boundary (a test that boots a host and touches a real database is
/// functional, regardless of intent or folder), every test in this class IS a functional test — it is
/// here, not in ServiceBooking.UnitTests, specifically because it needs the real background runner
/// (advisory lock, budget, partial-pass handling) actually ticking, which only exists once a host is
/// booted. This is the explicit, customer-approved exception ARCHITECTURE_CYCLE4.md §27 documents (T4-B14),
/// modeled directly on cycle 3's <c>RateLimitingTests</c>/<c>RateLimitTestFactory</c> pair — the same
/// "behavior the shared Testing configuration deliberately glues shut" situation, solved the same way.
/// Kept deliberately small (two scenarios): the point is proving the wiring end-to-end, not exhaustively
/// re-testing gate/timing/classification rules already covered by unit tests elsewhere.
///
/// <see cref="DisableParallelization"/> is essential, not incidental: this collection's tests run a REAL
/// background tick against the shared "servicebooking_test" database, same as every other functional
/// test — but unlike them, this one deliberately turns a normally-off background service back on, so it
/// must never overlap with itself.
/// </summary>
[Collection("NotificationDispatch")]
public class NotificationDispatchTests
{
    private const string EncryptionKey = NotificationDispatchTestFactory.TestEncryptionKeyBase64;

    [Fact, TestCase("NTF-D01")]
    public async Task RealRunner_SendsQueuedMessages_AndRequestsAPauseBetweenThem()
    {
        await using var factory = new NotificationDispatchTestFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (ownerUserId, channel, company) = await SeedConnectedChannelAsync(factory, db);

        var row1 = NewPendingNotification(company.Id, channel.Id, "79990000001");
        var row2 = NewPendingNotification(company.Id, channel.Id, "79990000002");
        db.OutboundNotifications.AddRange(row1, row2);
        await db.SaveChangesAsync();

        await WaitForAsync(factory, () =>
            factory.Transport.Calls.Count >= 2, timeoutSeconds: 20);

        factory.Transport.Calls.Should().HaveCount(2);
        factory.Transport.Calls.Select(c => c.CanonicalPhone).Should().Equal("79990000001", "79990000002");

        // Exactly one pause between two sends on the SAME channel (§26.1) — not zero, not two.
        factory.Delay.Requested.Should().ContainSingle();
        factory.Delay.Requested.Single().TotalMilliseconds.Should().BeInRange(5000, 15000);

        await db.Entry(row1).ReloadAsync();
        await db.Entry(row2).ReloadAsync();
        row1.Status.Should().Be(NotificationStatus.Sent);
        row2.Status.Should().Be(NotificationStatus.Sent);
    }

    [Fact, TestCase("NTF-D02")]
    public async Task RealRunner_ChannelInvalid_StopsTheRestOfThatChannelsGroup_RowsStayPending()
    {
        await using var factory = new NotificationDispatchTestFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, channel, company) = await SeedConnectedChannelAsync(factory, db);

        factory.Transport.EnqueueOutcome(new SendOutcome.ChannelInvalid("simulated 401"));

        var row1 = NewPendingNotification(company.Id, channel.Id, "79990000003");
        var row2 = NewPendingNotification(company.Id, channel.Id, "79990000004");
        db.OutboundNotifications.AddRange(row1, row2);
        await db.SaveChangesAsync();

        // The channel transitioning away from Connected is the observable signal that the pass reacted
        // to the ChannelInvalid outcome and stopped — the rows themselves never change Status for this
        // outcome (US-28 p.9), so polling Status would never resolve.
        await WaitForAsync(factory, async () =>
        {
            await using var pollScope = factory.Services.CreateAsyncScope();
            var pollDb = pollScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var current = await pollDb.NotificationChannels.AsNoTracking().FirstAsync(c => c.Id == channel.Id);
            return current.State != ChannelState.Connected;
        }, timeoutSeconds: 20);

        // Only the FIRST row was ever attempted — the group stopped immediately (§26.4), the second row
        // was never even tried this pass.
        factory.Transport.Calls.Should().ContainSingle();

        await db.Entry(row1).ReloadAsync();
        await db.Entry(row2).ReloadAsync();
        row1.Status.Should().Be(NotificationStatus.Pending);
        row2.Status.Should().Be(NotificationStatus.Pending);

        var channelAfter = await db.NotificationChannels.AsNoTracking().FirstAsync(c => c.Id == channel.Id);
        channelAfter.State.Should().Be(ChannelState.Disconnected);

        var events = await db.ChannelStateEvents.Where(e => e.ChannelId == channel.Id).ToListAsync();
        events.Should().ContainSingle(e => e.ToState == ChannelState.Disconnected);
    }

    // ── Seeding helpers ──────────────────────────────────────────────────────────────────────────

    private static async Task<(string OwnerUserId, NotificationChannel Channel, Company Company)> SeedConnectedChannelAsync(
        NotificationDispatchTestFactory factory, AppDbContext db)
    {
        var phone = UniquePhone();
        var registerResponse = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Test", "Owner", phone, "Password123!", null, true));
        registerResponse.EnsureSuccessStatusCode();
        var auth = (await registerResponse.Content.ReadFromJsonAsync<AuthResponseDto>())!;

        var plan = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(), Name = Unique("Plan"), AllowNotificationChannel = true, IsActive = true,
        };
        // Cycle 5 (ARCHITECTURE_CYCLE5.md §45.1): money is resolved through BillingAccountId now, not
        // OwnerUserId directly — this raw-row seeding helper must wire one up itself.
        var billingAccount = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = auth.UserId };
        var subscription = new AccountSubscription
        {
            Id = Guid.NewGuid(), OwnerUserId = auth.UserId, PlanConfigId = plan.Id,
            BillingAccountId = billingAccount.Id, IsActive = true,
            PaidUntil = DateTime.UtcNow.AddDays(30),
        };
        var company = new Company
        {
            Id = Guid.NewGuid(), Name = Unique("Co"), Slug = Unique("co"), OwnerUserId = auth.UserId,
            BillingAccountId = billingAccount.Id,
            TimeZoneId = "Europe/Moscow",
        };
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = auth.UserId, State = ChannelState.Connected,
            PhoneNumber = "79990000000",
            ProviderInstanceId = Unique("instance"),
            PaidFromUtc = DateTime.UtcNow.AddDays(-1), PaidUntilUtc = DateTime.UtcNow.AddDays(30),
            ConnectedAtUtc = DateTime.UtcNow.AddDays(-1),
        };
        // AAD binds ciphertext to the channel's own id (§24.1) — must be encrypted AFTER Id is assigned.
        channel.ProviderSecretCiphertext = SecretProtector.Encrypt("test-provider-token", EncryptionKey, channel.Id);

        var assignment = new ChannelCompanyAssignment
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = company.Id, AssignedByUserId = auth.UserId,
        };

        db.SubscriptionPlanConfigs.Add(plan);
        db.BillingAccounts.Add(billingAccount);
        db.AccountSubscriptions.Add(subscription);
        db.Companies.Add(company);
        db.NotificationChannels.Add(channel);
        db.ChannelCompanyAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return (auth.UserId, channel, company);
    }

    private static OutboundNotification NewPendingNotification(Guid companyId, Guid channelId, string recipientPhone)
    {
        var nowUtc = DateTime.UtcNow;
        var id = Guid.NewGuid();
        return new OutboundNotification
        {
            Id = id, CompanyId = companyId, ChannelId = channelId, Type = NotificationType.BookingConfirmed,
            RecipientPhone = recipientPhone, Body = "Test notification body",
            DueAtUtc = nowUtc.AddMinutes(-1), VisitStartUtc = nowUtc.AddHours(2),
            Status = NotificationStatus.Pending,
            IdempotencyKey = $"test:{id}",
        };
    }

    private static async Task WaitForAsync(NotificationDispatchTestFactory factory, Func<bool> predicate, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(200);
        }
        predicate().Should().BeTrue($"condition did not become true within {timeoutSeconds}s");
    }

    private static async Task WaitForAsync(NotificationDispatchTestFactory factory, Func<Task<bool>> predicate, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(200);
        }
        (await predicate()).Should().BeTrue($"condition did not become true within {timeoutSeconds}s");
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 20, prefix.Length + 12)];

    private static string UniquePhone()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsDigit).Take(10).ToArray();
        var suffix = new string(digits).PadRight(10, '0');
        return $"+79{suffix[..9]}";
    }
}

/// <summary>§27.1: not parallel with itself — a real background tick against the shared functional-test
/// database must never race another test in the same collection. Plate cost: a few extra seconds of
/// total suite time. Radius even if this ever overlapped another collection: empty, by construction — no
/// OTHER functional test gives any company a paid, assigned, Connected channel, so this collection's
/// runner never finds a Pending row belonging to anyone else (ARCHITECTURE_CYCLE4.md §27.1).</summary>
[CollectionDefinition("NotificationDispatch", DisableParallelization = true)]
public class NotificationDispatchCollection;
