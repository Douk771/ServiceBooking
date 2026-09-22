using System.Collections.Concurrent;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
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
/// QA cycle 4 — the three dispatcher/idle scenarios the code reviewer named explicitly as missing
/// coverage that let real defects through: queue priority by visit time, a budget-interrupted pass that
/// doesn't duplicate sends, and channel idle warn-then-delete. Same real-runner infrastructure as
/// <c>NotificationDispatchTests.cs</c> (ARCHITECTURE_CYCLE4.md §27) — this class now gets its own,
/// disjoint database (ARCHITECTURE_CYCLE8_PHASE2.md §91), so it never overlaps another test ticking the
/// same background runner against the same rows.
/// </summary>
public class NotificationDispatchExtraTests(TestDatabaseFixture fixture) : IClassFixture<TestDatabaseFixture>
{
    // T9 review (M3): records the slot↔class pairing (see TestDatabaseFixture.RecordTestClass) — this class declares IClassFixture<TestDatabaseFixture> directly (not via ApiTestBase/NotificationTestBase), so it must call this itself.
    private readonly int _testClassRecorded = RecordTestClassOnConstruction(fixture, nameof(NotificationDispatchExtraTests));

    private static int RecordTestClassOnConstruction(TestDatabaseFixture fixture, string className)
    {
        fixture.RecordTestClass(className);
        return 0;
    }

    private const string EncryptionKey = NotificationDispatchTestFactory.TestEncryptionKeyBase64;

    [Fact, TestCase("NTF-D03")]
    public async Task Priority_ByVisitTime_ClosestVisitSentFirst_RegardlessOfQueueOrder()
    {
        await using var factory = new NotificationDispatchTestFactory(fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, channel, company) = await SeedConnectedChannelAsync(factory, db);

        // Queued in the OPPOSITE order from how they must be sent: the far-future visit is inserted
        // first, the near one second — priority must come from VisitStartUtc, not insertion order.
        var far = NewPendingNotification(company.Id, channel.Id, "79990000101", visitStartUtc: DateTime.UtcNow.AddDays(60));
        var near = NewPendingNotification(company.Id, channel.Id, "79990000102", visitStartUtc: DateTime.UtcNow.AddHours(3));
        db.OutboundNotifications.AddRange(far, near);
        await db.SaveChangesAsync();

        // Filters by these two specific phone numbers rather than asserting on Transport.Calls as a
        // whole — this suite's real dispatcher scans Pending rows platform-wide (ARCHITECTURE_CYCLE4.md
        // §27.1), so it must stay correct even if an unrelated leftover row (from a differently-isolated
        // test elsewhere in the shared "servicebooking_test" database) is also in flight.
        await WaitForAsync(() => KnownCalls(factory).Count(c => c is "79990000101" or "79990000102") >= 2, timeoutSeconds: 20);

        var allCalls = KnownCalls(factory);
        var knownOrder = allCalls.Where(c => c is "79990000101" or "79990000102").ToList();
        knownOrder.Should().Equal(new[] { "79990000102", "79990000101" },
            "the near-future visit must be dispatched before the far-future one, even though it was queued second");
    }

    [Fact, TestCase("NTF-D04")]
    public async Task Budget_InterruptedPass_DoesNotDuplicateAlreadySentRows_RemainderStaysPending_SummaryIsHonest()
    {
        // BudgetSeconds binds to `int` (whole seconds only, ArgumentException on "0.9"), so the budget
        // itself can only be 1s here — the PER-SEND delay is tuned instead so exactly 2 of 3 same-channel
        // sends fit: the loop-top budget check (NotificationDispatchTask, §26.1) happens BEFORE each
        // send, at elapsed times ~0ms/550ms/1100ms for rows 1/2/3 — the first two checks are under the
        // 1000ms budget, the third is not, so row 3 never starts this pass.
        const int perSendDelayMs = 550;
        var slowTransport = new SlowRecordingTransport(perSendDelayMs);
        // WithWebHostBuilder layers this slow transport IN ADDITION to (registered after, so it wins
        // over) the base factory's instant RecordingTransport, which is otherwise too fast for any
        // BudgetSeconds this test can wait for in real time to ever interrupt a pass. `baseFactory` stays
        // an UNSTARTED handle — it exists only because `WithWebHostBuilder` has to be called on some
        // instance, and `SeedConnectedChannelAsync` needs a `NotificationDispatchTestFactory`-typed
        // parameter for its signature. Nothing may ever touch `baseFactory.Services`/`.Server`/
        // `.CreateClient()`: doing so lazily builds AND STARTS a second, independent host pointed at the
        // very same database, with its OWN ScheduledTaskRunner ticking notification-dispatch on the
        // ORIGINAL (fast) RecordingTransport — racing `webFactory`'s slow one on the same Pending rows
        // and defeating the budget-cutoff this test exists to prove (this used to happen via
        // `SeedConnectedChannelAsync`'s legal-document lookup, which read `factory.Services` — fixed by
        // passing `legalFactory: webFactory` explicitly below). `webFactory` (the one `WithWebHostBuilder`
        // returns) is the ACTUAL running host — Services/CreateClient must come from it, always.
        await using var baseFactory = new NotificationDispatchTestFactory(fixture.ConnectionString, budgetSecondsOverride: 1);
        await using var webFactory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<INotificationTransport>(slowTransport)));

        using var scope = webFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, channel, company) = await SeedConnectedChannelAsync(baseFactory, db, client: webFactory.CreateClient(), legalFactory: webFactory);

        string[] ownPhones = ["79990000201", "79990000202", "79990000203"];
        var row1 = NewPendingNotification(company.Id, channel.Id, ownPhones[0]);
        var row2 = NewPendingNotification(company.Id, channel.Id, ownPhones[1]);
        var row3 = NewPendingNotification(company.Id, channel.Id, ownPhones[2]);
        db.OutboundNotifications.AddRange(row1, row2, row3);
        await db.SaveChangesAsync();

        // CI-flake fix: `webFactory`'s background runner scans Pending rows PLATFORM-WIDE
        // (ARCHITECTURE_CYCLE4.md §27.1), same as every other test in this file — `slowTransport`, being
        // registered once for the whole host, records EVERY send that host makes, not just this test's
        // own 3 rows. A run against the shared "servicebooking_test" database can have other channels
        // still Connected with their own Pending rows left by other tests' factories (each with its own
        // background loop that doesn't necessarily stop the instant its own test method returns) — those
        // sends land in `slowTransport.Calls` too. The original `slowTransport.Calls.Count.Should().Be(2)`
        // counted ALL of them, so it could read anywhere from fewer than 2 (another host's dispatcher won
        // the race and sent OUR rows through ITS OWN transport first) to more than 2 (an unrelated row
        // got sent through THIS host in the same pass) — neither has anything to do with whether the
        // budget correctly stopped THIS test's own group after 2 sends. Filtered to `ownPhones`, exactly
        // like Priority_ByVisitTime above does for the same reason, the assertion is back to testing the
        // actual invariant instead of a shared, unfiltered call log.
        Func<int> ownCallCount = () => slowTransport.Calls.Count(c => ownPhones.Contains(c.CanonicalPhone));

        // First pass: exactly 2 of the 3 same-channel rows fit in the ~900ms budget at 500ms/send. The
        // budget cutoff is deterministic within a single pass: two full 550ms sends already sum to
        // 1100ms, past the 1000ms budget, so row 3's loop-top check (or the antiban pause right after row
        // 2) is guaranteed to already be past the deadline by the time this pass could even consider it.
        await WaitForAsync(() => ownCallCount() >= 2, timeoutSeconds: 20);
        ownCallCount().Should().Be(2, "the budget must stop the group after the 2nd send, before the 3rd starts");

        await using (var pollScope = webFactory.Services.CreateAsyncScope())
        {
            var pollDb = pollScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await pollDb.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == row3.Id)).Status
                .Should().Be(NotificationStatus.Pending, "the row the budget cut off must stay Pending, not be lost or marked anything else");
        }

        // Second pass (fresh budget): must finish the remainder, WITHOUT re-sending rows 1/2.
        await WaitForAsync(() => ownCallCount() >= 3, timeoutSeconds: 20);
        var ownCalls = slowTransport.Calls.Where(c => ownPhones.Contains(c.CanonicalPhone)).ToList();
        ownCalls.Select(c => c.CanonicalPhone).Should()
            .OnlyHaveUniqueItems("no row may ever be sent twice across passes, even one interrupted by budget");
        ownCalls.Select(c => c.CanonicalPhone).Should().Contain(ownPhones);
    }

    [Fact, TestCase("NTF-D05")]
    public async Task Idle_WarnsBeforeDeleting_ThenDeletesInstance_KeepsAssignmentPeriodAndPendingQueue()
    {
        await using var factory = new NotificationDispatchTestFactory(fixture.ConnectionString, channelHealthEnabled: true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await LoginAsSuperAdminAsync(factory);
        var setResponse = await AuthedClient(factory, admin.Token)
            .PutAsJsonAsync("/api/admin/platform-settings", new { channelPricePerMonth = (decimal?)null, channelIdleDays = 3 });
        setResponse.EnsureSuccessStatusCode();

        var (ownerUserId, channel, company) = await SeedConnectedChannelAsync(factory, db);
        // "Простой" = zero ACTIVE assigned companies — deactivate the only assigned company.
        var trackedCompany = await db.Companies.FirstAsync(c => c.Id == company.Id);
        trackedCompany.IsActive = false;
        // DueAtUtc is set far in the future (relative to the SAME FakeClock this test advances) so the
        // ordinary notification-dispatch task — which this factory ALSO runs, not just channel-health —
        // never selects this row as a candidate (its WHERE clause is DueAtUtc <= now) regardless of the
        // channel staying Connected right up until the idle deletion. Without this, the row would simply
        // get sent normally within the first second, defeating the point of the assertion below.
        var pendingRow = NewPendingNotification(company.Id, channel.Id, "79990000301",
            visitStartUtc: factory.Clock.UtcNow.AddDays(20));
        pendingRow.DueAtUtc = factory.Clock.UtcNow.AddDays(10);
        db.OutboundNotifications.Add(pendingRow);
        var paidUntilBefore = channel.PaidUntilUtc;
        await db.SaveChangesAsync();

        // Pass 1: establishes IdleSinceUtc = now (FakeClock's current instant).
        await WaitForAsync(async () =>
        {
            await using var s = factory.Services.CreateAsyncScope();
            var d = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await d.NotificationChannels.AsNoTracking().FirstAsync(x => x.Id == channel.Id);
            return c.IdleSinceUtc is not null;
        }, timeoutSeconds: 20);

        // Advance 2 of 3 idle days: warning must fire, deletion must NOT have happened yet.
        factory.Clock.Advance(TimeSpan.FromDays(2));
        await WaitForAsync(async () =>
        {
            await using var s = factory.Services.CreateAsyncScope();
            var d = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await d.NotificationChannels.AsNoTracking().FirstAsync(x => x.Id == channel.Id);
            return c.IdleWarningSentAtUtc is not null;
        }, timeoutSeconds: 20);

        await using (var s = factory.Services.CreateAsyncScope())
        {
            var d = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await d.NotificationChannels.AsNoTracking().FirstAsync(x => x.Id == channel.Id);
            c.ProviderInstanceId.Should().NotBeNull("warned, but not yet past the full idle threshold — instance must still be alive");
            c.State.Should().Be(ChannelState.Connected);
        }

        // Advance past the full 3-day threshold: deletion must now happen.
        factory.Clock.Advance(TimeSpan.FromDays(2));
        await WaitForAsync(async () =>
        {
            await using var s = factory.Services.CreateAsyncScope();
            var d = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await d.NotificationChannels.AsNoTracking().FirstAsync(x => x.Id == channel.Id);
            return c.ProviderInstanceId is null;
        }, timeoutSeconds: 20);

        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var finalChannel = await finalDb.NotificationChannels.AsNoTracking().FirstAsync(x => x.Id == channel.Id);
        finalChannel.State.Should().Be(ChannelState.NeedsReconnect);
        // CI-flake fix: Postgres' timestamp column is microsecond-precision, .NET DateTime is 100ns-tick
        // precision — a value written, then read back through a real round trip (unlike this test's other
        // in-memory `paidUntilBefore` capture), can lose its 7th significant digit. Exact .Be(...) compares
        // ticks bit-for-bit and is flaky depending on what random sub-microsecond tick the seed happened to
        // land on; a 1ms tolerance is generous for round-trip truncation and still tight enough to catch a
        // real bug (e.g. the period being recalculated instead of carried through unchanged).
        finalChannel.PaidUntilUtc.Should().BeCloseTo(paidUntilBefore!.Value, TimeSpan.FromMilliseconds(1),
            "the paid period must survive an idle deletion");

        var assignmentStillThere = await finalDb.ChannelCompanyAssignments.AsNoTracking()
            .AnyAsync(a => a.ChannelId == channel.Id && a.CompanyId == company.Id);
        assignmentStillThere.Should().BeTrue("company assignment must survive an idle deletion");

        var reloadedRow = await finalDb.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == pendingRow.Id);
        reloadedRow.Status.Should().Be(NotificationStatus.Pending, "queued rows must stay Pending through an idle deletion, not be cancelled");
    }

    // ── Seeding / auth helpers (mirrors NotificationDispatchTests.cs) ──────────────────────────────

    private static async Task<(string OwnerUserId, NotificationChannel Channel, Company Company)> SeedConnectedChannelAsync(
        NotificationDispatchTestFactory factory, AppDbContext db, HttpClient? client = null,
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? legalFactory = null)
    {
        // `legalFactory` defaults to `factory`, but a caller running two factories against the same
        // database (Budget_InterruptedPass below) MUST pass the one that is actually meant to run —
        // touching ANY WebApplicationFactory's `.Services`/`.Server` for the first time lazily builds
        // and STARTS its host, including its own ScheduledTaskRunner. `factory.Services` here used to be
        // read unconditionally via NotificationDispatchTests.CurrentRegisterLegalDto(factory), which for
        // that test silently started a SECOND, independent notification-dispatch loop (on the fast,
        // un-overridden RecordingTransport) racing the real one under test on the very same Pending rows
        // — an intermittent flake, not a real product bug (see the test below for the full story).
        var phone = UniquePhone();
        var registerResponse = await (client ?? factory.CreateClient()).PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Test", "Owner", phone, "Password123!", null,
                NotificationDispatchTests.CurrentRegisterLegalDto(legalFactory ?? factory)));
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
            TimeZoneId = "Europe/Moscow", IsActive = true,
        };
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = auth.UserId, BillingAccountId = billingAccount.Id, State = ChannelState.Connected,
            PhoneNumber = "79990000000",
            ProviderInstanceId = Unique("instance"),
            PaidFromUtc = DateTime.UtcNow.AddDays(-1), PaidUntilUtc = DateTime.UtcNow.AddDays(30),
            ConnectedAtUtc = DateTime.UtcNow.AddDays(-1),
        };
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
        // EnsureWhatsAppPaidAsync below — deterministically blocking every row in this file's tests with
        // NotOnPaidPlan before it ever reaches the transport, not an intermittent load issue.
        await ServiceBooking.Tests.Infrastructure.NotificationTestBase.EnsureWhatsAppPlanRuleAsync(db, plan.Id);
        await db.SaveChangesAsync();

        // Cycle 7, stage 3 (ARCHITECTURE_CYCLE7.md §47.1): funding comes from the account's paid
        // notifications.whatsapp quantity now, not the channel's own PaidUntilUtc.
        await ServiceBooking.Tests.Infrastructure.NotificationTestBase.EnsureWhatsAppPaidAsync(db, billingAccount.Id);

        return (auth.UserId, channel, company);
    }

    private static OutboundNotification NewPendingNotification(Guid companyId, Guid channelId, string recipientPhone, DateTime? visitStartUtc = null)
    {
        var nowUtc = DateTime.UtcNow;
        var id = Guid.NewGuid();
        return new OutboundNotification
        {
            Id = id, CompanyId = companyId, ChannelId = channelId, Type = NotificationType.BookingConfirmed,
            RecipientPhone = recipientPhone, Body = "Test notification body",
            DueAtUtc = nowUtc.AddMinutes(-1), VisitStartUtc = visitStartUtc ?? nowUtc.AddHours(2),
            Status = NotificationStatus.Pending,
            IdempotencyKey = $"test:{id}",
        };
    }

    private static async Task<AuthResponseDto> LoginAsSuperAdminAsync(NotificationDispatchTestFactory factory)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginDto(factory.Identity.SuperAdminPhone, factory.Identity.SuperAdminPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    private static HttpClient AuthedClient(NotificationDispatchTestFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 20, prefix.Length + 12)];

    private static string UniquePhone()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsDigit).Take(10).ToArray();
        var suffix = new string(digits).PadRight(10, '0');
        return $"+79{suffix[..9]}";
    }

    private static IReadOnlyList<string> KnownCalls(NotificationDispatchTestFactory factory) =>
        factory.Transport.Calls.Select(c => c.CanonicalPhone).ToList();

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

    private static async Task WaitForAsync(Func<Task<bool>> predicate, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(200);
        }
        (await predicate()).Should().BeTrue($"condition did not become true within {timeoutSeconds}s");
    }
}

/// <summary>Like <see cref="RecordingTransport"/> but with a real, configurable per-call delay — needed
/// ONLY to make <c>Notifications:Dispatch:BudgetSeconds</c> exhaustible within a real-clock test
/// (NTF-D04): the base <see cref="RecordingTransport"/> completes instantly, so no real budget this test
/// can afford to wait for would ever actually interrupt a pass.</summary>
public sealed class SlowRecordingTransport(int perSendDelayMs) : INotificationTransport
{
    private readonly ConcurrentQueue<RecordedSend> _calls = new();
    public IReadOnlyList<RecordedSend> Calls => _calls.ToArray();

    public async Task<SendOutcome> SendAsync(ChannelCredentials credentials, string canonicalPhone, string text, CancellationToken ct)
    {
        await Task.Delay(perSendDelayMs, CancellationToken.None);
        _calls.Enqueue(new RecordedSend(credentials, canonicalPhone, text, DateTime.UtcNow));
        return new SendOutcome.Sent($"slow-test-{Guid.NewGuid():N}");
    }
}
