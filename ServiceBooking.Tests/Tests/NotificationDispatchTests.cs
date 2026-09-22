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
/// ARCHITECTURE_CYCLE8_PHASE2.md §91/§92.1: this class now gets its OWN database via
/// <see cref="TestDatabaseFixture"/> (<c>IClassFixture</c>, not phase 1's <c>ICollectionFixture</c>/
/// <c>[Collection("NotificationDispatch")]</c>) — it never shares a database with
/// <see cref="NotificationDispatchExtraTests"/> or any other class, so the "must never overlap a real
/// background tick against a shared database" concern the phase-1 collection existed for no longer
/// applies: there is no other test touching this class' rows to overlap with.
/// </summary>
public class NotificationDispatchTests(TestDatabaseFixture fixture) : IClassFixture<TestDatabaseFixture>
{
    // T9 review (M3): records the slot↔class pairing (see TestDatabaseFixture.RecordTestClass) — this class declares IClassFixture<TestDatabaseFixture> directly (not via ApiTestBase/NotificationTestBase), so it must call this itself.
    private readonly int _testClassRecorded = RecordTestClassOnConstruction(fixture, nameof(NotificationDispatchTests));

    private static int RecordTestClassOnConstruction(TestDatabaseFixture fixture, string className)
    {
        fixture.RecordTestClass(className);
        return 0;
    }

    private const string EncryptionKey = NotificationDispatchTestFactory.TestEncryptionKeyBase64;

    [Fact, TestCase("NTF-D01")]
    public async Task RealRunner_SendsQueuedMessages_AndRequestsAPauseBetweenThem()
    {
        await using var factory = new NotificationDispatchTestFactory(fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (ownerUserId, channel, company) = await SeedConnectedChannelAsync(factory, db);

        string[] ownPhones = ["79990000001", "79990000002"];
        var row1 = NewPendingNotification(company.Id, channel.Id, ownPhones[0]);
        var row2 = NewPendingNotification(company.Id, channel.Id, ownPhones[1]);
        db.OutboundNotifications.AddRange(row1, row2);
        await db.SaveChangesAsync();

        // Filtered to ownPhones, not asserted on Transport.Calls as a whole: this suite's real
        // dispatcher scans Pending rows PLATFORM-WIDE (ARCHITECTURE_CYCLE4.md §27.1) against the same
        // shared database every other functional test uses — a leftover Connected-channel row from an
        // unrelated, differently-isolated test (e.g. NotificationQueueingTests/NotificationChannelsTests,
        // which run in the "Api" collection with no ticking runner of their own to drain what they queue)
        // can still be Pending and due when THIS host's runner ticks. Same fix already applied in
        // NotificationDispatchExtraTests.cs (NTF-D03/NTF-D04) for the identical reason.
        Func<int> ownCallCount = () => factory.Transport.Calls.Count(c => ownPhones.Contains(c.CanonicalPhone));
        await WaitForAsync(factory, () => ownCallCount() >= 2, timeoutSeconds: 20);

        ownCallCount().Should().Be(2);
        factory.Transport.Calls.Where(c => ownPhones.Contains(c.CanonicalPhone))
            .Select(c => c.CanonicalPhone).Should().Equal(ownPhones);

        // Exactly one pause between two sends on the SAME channel (§26.1) — not zero, not two. This
        // channel is exclusive to this test (a freshly registered owner/company/channel), so — unlike
        // Transport.Calls above — Delay.Requested needs no filtering: nothing else could have paused on
        // THIS channel's group.
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
        await using var factory = new NotificationDispatchTestFactory(fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, channel, company) = await SeedConnectedChannelAsync(factory, db);

        string[] ownPhones = ["79990000003", "79990000004"];
        // Phone-scoped (SetOutcomeForPhone), NOT the global FIFO EnqueueOutcome — this suite's real
        // dispatcher scans Pending rows PLATFORM-WIDE (ARCHITECTURE_CYCLE4.md §27.1), so a
        // globally-queued outcome could be consumed by whichever channel group the runner happens to
        // process FIRST in the pass — own or an unrelated leftover row from another, differently-isolated
        // test (NotificationQueueingTests/NotificationChannelsTests run in the "Api" collection with no
        // ticking runner of their own to drain what they queue) — silently handing THIS test's row a
        // default Sent outcome instead and leaving row1 never actually exercising ChannelInvalid at all.
        factory.Transport.SetOutcomeForPhone(ownPhones[0], new SendOutcome.ChannelInvalid("simulated 401"));

        var row1 = NewPendingNotification(company.Id, channel.Id, ownPhones[0]);
        var row2 = NewPendingNotification(company.Id, channel.Id, ownPhones[1]);
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
        // was never even tried this pass. Filtered to ownPhones for the same platform-wide-scan reason
        // documented above.
        factory.Transport.Calls.Where(c => ownPhones.Contains(c.CanonicalPhone)).Should().ContainSingle();

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

    // CYCLE5-BREAKING (compile-only adaptation, see ApiTestBase.RegisterAsync's own note): reads the
    // live manifest through the same factory the registration call itself targets, so this stays correct
    // even if a future test class points its factory at a non-default Legal:Root.
    internal static RegisterLegalDto CurrentRegisterLegalDto(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        var snapshot = provider.Current!;
        return new RegisterLegalDto(
            snapshot.Get(LegalDocumentType.Privacy)!.Version, snapshot.Get(LegalDocumentType.TermsClient)!.Version);
    }

    private static async Task<(string OwnerUserId, NotificationChannel Channel, Company Company)> SeedConnectedChannelAsync(
        NotificationDispatchTestFactory factory, AppDbContext db)
    {
        var phone = UniquePhone();
        var registerResponse = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Test", "Owner", phone, "Password123!", null, CurrentRegisterLegalDto(factory)));
        registerResponse.EnsureSuccessStatusCode();
        var auth = (await registerResponse.Content.ReadFromJsonAsync<AuthResponseDto>())!;

        var plan = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(), Name = Unique("Plan"), AllowNotificationChannel = true, IsActive = true,
        };
        // Cycle 7 (ARCHITECTURE_CYCLE7.md §45.1): money is resolved through BillingAccountId now, not
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
            Id = Guid.NewGuid(), OwnerUserId = auth.UserId, BillingAccountId = billingAccount.Id, State = ChannelState.Connected,
            PhoneNumber = "79990000000",
            ProviderInstanceId = Unique("instance"),
            PaidFromUtc = DateTime.UtcNow.AddDays(-1), PaidUntilUtc = DateTime.UtcNow.AddDays(30),
            ConnectedAtUtc = DateTime.UtcNow.AddDays(-1),
        };
        // AAD binds ciphertext to the channel's own id (§24.1) — must be encrypted AFTER Id is assigned.
        channel.ProviderSecretCiphertext = SecretProtector.Encrypt("test-provider-token", EncryptionKey, channel.Id);

        var assignment = new ChannelCompanyAssignment
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = company.Id, BillingAccountId = billingAccount.Id, AssignedByUserId = auth.UserId,
        };

        db.SubscriptionPlanConfigs.Add(plan);
        db.BillingAccounts.Add(billingAccount);
        db.AccountSubscriptions.Add(subscription);
        db.Companies.Add(company);
        db.NotificationChannels.Add(channel);
        db.ChannelCompanyAssignments.Add(assignment);
        // Flakiness fix (this QA pass) — see NotificationTestBase.EnsureWhatsAppPlanRuleAsync's own doc
        // comment: without an explicit PlanOptionRule, SubscriptionResolver's fail-closed convention
        // (N13/N14) always resolves PaidNotificationNumbers to 0 for this plan, regardless of
        // EnsureWhatsAppPaidAsync below — deterministically blocking every row in this test with
        // NotOnPaidPlan before it ever reaches the transport, not an intermittent load issue.
        await ServiceBooking.Tests.Infrastructure.NotificationTestBase.EnsureWhatsAppPlanRuleAsync(db, plan.Id);
        await db.SaveChangesAsync();

        // Cycle 7, stage 3 (ARCHITECTURE_CYCLE7.md §47.1): funding comes from the account's paid
        // notifications.whatsapp quantity now, not the channel's own PaidUntilUtc.
        await ServiceBooking.Tests.Infrastructure.NotificationTestBase.EnsureWhatsAppPaidAsync(db, billingAccount.Id);

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
