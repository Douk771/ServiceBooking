using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 22, package P4 (SPEC.md Р2/Р6, US-22-08-bis; ARCHITECTURE_CYCLE22.md §379–§381) — every place
/// that used to read the channel's own <c>PaidUntilUtc</c> column (never written since cycle 7, nulled on
/// replace) now reads the channel's actual FUNDING: the account's <c>notifications.whatsapp</c> option
/// (ranked over the account's live channels), with "paid until" = the option's own PaidUntilUtc, else the
/// subscription's PaidUntil — exactly what <c>ChannelFundingReader</c> computes for the owner's own list.
///
/// These are deliberately BEHAVIOR-CHANGE tests: written first, they fail on the pre-P4 code wherever it
/// read the stale column (admin counters/list, company settings paidUntil, idle computation, replace
/// response). Five funding scenarios, one owner/account each:
/// <list type="bullet">
/// <item><c>FarFuture</c> — option paid through +60 days: funded, not expiring.</item>
/// <item><c>Expiring</c> — option paid through +3 days: funded, counts in ExpiringIn7Days.</item>
/// <item><c>Requested</c> — channel requested, no option row bought yet: not funded; paid-until falls
/// back to the subscription's own period (reader semantics).</item>
/// <item><c>Expired</c> — option paid through −2 days: not funded; paid-until is that past date.</item>
/// <item><c>NoSubscription</c> — no AccountSubscription at all: not funded, no paid-until.</item>
/// </list>
/// </summary>
public class Cycle22ChannelFundingTests(TestDatabaseFixture fixture) : NotificationTestBase(fixture)
{
    public enum Funding { FarFuture, Expiring, Requested, Expired, NoSubscription }

    private sealed record Seeded(
        AuthResponseDto Owner, CompanyDto Company, Guid AccountId, Guid ChannelId, DateTime? ExpectedPaidUntil, bool Funded);

    private static readonly TimeSpan DbPrecision = TimeSpan.FromMilliseconds(1);

    // ── CY22-02: admin channel summary, list, suspend log ──────────────────────────────────────────

    [Fact, TestCase("CY22-02")]
    public async Task AdminSummary_ExpiringAndPendingCounters_ComeFromFunding()
    {
        var adminClient = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var before = await GetSummaryAsync(adminClient);

        // All five channels carry RequestedAtUtc (every channel is born from an owner's request).
        await SeedAsync(Funding.FarFuture);
        await SeedAsync(Funding.Expiring);
        await SeedAsync(Funding.Requested);
        await SeedAsync(Funding.Expired);
        await SeedAsync(Funding.NoSubscription);

        var after = await GetSummaryAsync(adminClient);

        // Only the funded channel whose paid-until falls within [now, now+7d] is "expiring".
        (after.ExpiringIn7Days - before.ExpiringIn7Days).Should().Be(1,
            "the Expiring scenario is funded through +3 days; FarFuture is +60, the rest are unfunded");
        // PendingRequests = requested AND not funded (and not a Replaced terminal row): Requested,
        // Expired, NoSubscription — the two funded requests are no longer "pending".
        (after.PendingRequests - before.PendingRequests).Should().Be(3,
            "a request is pending while the channel is not funded — the two funded channels are not pending");
    }

    [Fact, TestCase("CY22-02")]
    public async Task AdminChannelList_PaidUntilAndPaymentState_ComeFromFunding()
    {
        var adminClient = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var seeded = new Dictionary<Funding, Seeded>();
        foreach (var f in Enum.GetValues<Funding>()) seeded[f] = await SeedAsync(f);

        var page = await GetJsonAsync<PagedResult<AdminChannelDto>>(adminClient, "/api/admin/notification-channels?pageSize=100");
        foreach (var (funding, s) in seeded)
        {
            var row = page.Items.Should().ContainSingle(c => c.Id == s.ChannelId, funding.ToString()).Subject;
            AssertPaidUntil(row.PaidUntil, s.ExpectedPaidUntil, funding);
            row.PaymentState.Should().Be(s.Funded ? ChannelPaymentStatus.Paid : ChannelPaymentStatus.NotPaid, funding.ToString());
        }

        // The computed ?paymentState= filter uses the same rule.
        var paid = await GetJsonAsync<PagedResult<AdminChannelDto>>(adminClient, "/api/admin/notification-channels?paymentState=Paid&pageSize=100");
        paid.Items.Select(c => c.Id).Should().Contain([seeded[Funding.FarFuture].ChannelId, seeded[Funding.Expiring].ChannelId])
            .And.NotContain([seeded[Funding.Requested].ChannelId, seeded[Funding.Expired].ChannelId, seeded[Funding.NoSubscription].ChannelId]);
    }

    [Fact, TestCase("CY22-02")]
    public async Task AdminSuspend_PaymentLogRecordsFundingPaidUntil()
    {
        var adminClient = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var s = await SeedAsync(Funding.FarFuture);

        var response = await adminClient.PostAsJsonAsync($"/api/admin/notification-channels/{s.ChannelId}/suspend", new { comment = "cy22" });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.ChannelPaymentLogs.AsNoTracking().SingleAsync(l => l.ChannelId == s.ChannelId);
        AssertPaidUntil(log.OldPaidUntil, s.ExpectedPaidUntil, Funding.FarFuture);
        AssertPaidUntil(log.NewPaidUntil, s.ExpectedPaidUntil, Funding.FarFuture);

        // Suspended by the admin wins over funding in the admin list.
        var page = await GetJsonAsync<PagedResult<AdminChannelDto>>(adminClient, "/api/admin/notification-channels?pageSize=100");
        page.Items.Single(c => c.Id == s.ChannelId).PaymentState.Should().Be(ChannelPaymentStatus.Suspended);
    }

    // ── CY22-03: company notification settings / summary ──────────────────────────────────────────

    [Fact, TestCase("CY22-03")]
    public async Task CompanySettings_PaymentStateAndPaidUntil_ComeFromFunding()
    {
        foreach (var funding in Enum.GetValues<Funding>())
        {
            var s = await SeedAsync(funding);
            var client = AuthedClient(s.Owner.Token);

            var settings = await GetJsonAsync<NotificationSettingsDto>(client, $"/api/companies/{s.Company.Id}/notification-settings");
            settings.Channel.Assigned.Should().BeTrue(funding.ToString());
            settings.Channel.ChannelId.Should().Be(s.ChannelId);
            settings.Channel.PaymentState.Should().Be(s.Funded ? ChannelPaymentStatus.Paid : ChannelPaymentStatus.NotPaid, funding.ToString());
            AssertPaidUntil(settings.Channel.PaidUntil, s.ExpectedPaidUntil, funding);

            var summary = await GetJsonAsync<NotificationSummaryDto>(client, $"/api/companies/{s.Company.Id}/notifications/summary");
            AssertPaidUntil(summary.ChannelPaidUntil, s.ExpectedPaidUntil, funding);
        }
    }

    [Fact, TestCase("CY22-03")]
    public async Task CompanySettings_NeedsReconnectStateText_NamesFundingPaidUntil()
    {
        var s = await SeedAsync(Funding.Expiring, ChannelState.NeedsReconnect);
        var settings = await GetJsonAsync<NotificationSettingsDto>(
            AuthedClient(s.Owner.Token), $"/api/companies/{s.Company.Id}/notification-settings");

        settings.Channel.StateText.Should().Contain($"оплаченный период до {s.ExpectedPaidUntil!.Value:dd.MM}");
    }

    // ── CY22-04: channel idle computation (ChannelHealthTask → ChannelIdleCalculator) ─────────────

    [Fact, TestCase("CY22-04")]
    public async Task ChannelHealthPass_IdleFollowsFunding()
    {
        // Every channel starts "idle since yesterday" with an ACTIVE assigned company, so the only thing
        // that can end the idle period is the channel being funded (and not suspended).
        var idleSince = DateTime.UtcNow.AddDays(-1);
        var seeded = new Dictionary<Funding, Seeded>();
        foreach (var f in Enum.GetValues<Funding>()) seeded[f] = await SeedAsync(f, idleSinceUtc: idleSince);
        var suspended = await SeedAsync(Funding.FarFuture, idleSinceUtc: idleSince, suspendedByAdmin: true);
        var inactiveCompany = await SeedAsync(Funding.FarFuture, idleSinceUtc: idleSince, companyActive: false);

        await RunChannelHealthPassAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        async Task<DateTime?> IdleOf(Guid id) =>
            (await db.NotificationChannels.AsNoTracking().FirstAsync(c => c.Id == id)).IdleSinceUtc;

        foreach (var (funding, s) in seeded)
        {
            var idle = await IdleOf(s.ChannelId);
            if (s.Funded)
                idle.Should().BeNull($"{funding}: funded with an active company — idle ends");
            else
                idle.Should().BeCloseTo(idleSince, DbPrecision, $"{funding}: not funded — idle continues from its original start");
        }
        (await IdleOf(suspended.ChannelId)).Should().NotBeNull("suspended by the admin — funding does not end idle");
        (await IdleOf(inactiveCompany.ChannelId)).Should().NotBeNull("funded, but no active company — still idle");
    }

    /// <summary>Review of cycle 22 (debt C22-5): the reader's WhatsApp-option filter runs on the "now" the
    /// caller hands it — ChannelHealthTask hands its <see cref="ServiceBooking.API.Services.Notifications.INotificationClock"/>
    /// instant — not on the wall clock. A <see cref="FakeClock"/> moved past the option's EndsAtUtc drops
    /// the option: paid-until falls back to the subscription's period. The funding STATE (what the idle
    /// computation reads) comes from plan resolution, which still runs on the wall clock and so stays
    /// Funded here — the part of C22-5 that remains open; this assertion flips when the resolver takes
    /// "now" too.</summary>
    [Fact, TestCase("CY22-04b")]
    public async Task FundingReader_OptionFilter_UsesCallersClock()
    {
        var s = await SeedAsync(Funding.FarFuture);
        var clock = new FakeClock();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var option = await db.AccountSubscriptionOptions.Include(o => o.Option)
            .SingleAsync(o => o.BillingAccountId == s.AccountId && o.Option.Code == ServiceBooking.API.Services.SubscriptionResolver.WhatsAppOptionCode);
        option.EndsAtUtc = clock.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        var subscriptionPaidUntil = await db.AccountSubscriptions.AsNoTracking()
            .Where(x => x.BillingAccountId == s.AccountId).Select(x => x.PaidUntil).SingleAsync();
        subscriptionPaidUntil.Should().NotBeNull();
        subscriptionPaidUntil!.Value.Should().NotBeCloseTo(s.ExpectedPaidUntil!.Value, TimeSpan.FromDays(1),
            "the two paid-until sources must be distinguishable for this test to mean anything");

        var reader = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Notifications.ChannelFundingReader>();
        var channel = await db.NotificationChannels.AsNoTracking().SingleAsync(c => c.Id == s.ChannelId);

        // Clock before EndsAtUtc (and the default, wall clock): the option is live and supplies paid-until.
        var before = (await reader.LoadAsync([channel], nowUtc: clock.UtcNow))[channel.Id];
        before.PaidUntil.Should().BeCloseTo(s.ExpectedPaidUntil!.Value, DbPrecision);
        (await reader.LoadAsync([channel]))[channel.Id].PaidUntil.Should().BeCloseTo(s.ExpectedPaidUntil!.Value, DbPrecision);

        // Clock moved past EndsAtUtc: the option is filtered out by the CALLER's clock.
        clock.Advance(TimeSpan.FromDays(2));
        var after = (await reader.LoadAsync([channel], nowUtc: clock.UtcNow))[channel.Id];
        after.PaidUntil.Should().BeCloseTo(subscriptionPaidUntil.Value, DbPrecision,
            "an option ended by the caller's clock no longer supplies paid-until");
        after.State.Should().Be(ServiceBooking.API.Services.Billing.ChannelFundingState.Funded,
            "C22-5 remainder: plan resolution (paid numbers → funding state) still reads the wall clock");
    }

    // ── CY22-05: replacing a blocked channel ──────────────────────────────────────────────────────

    [Theory, TestCase("CY22-05")]
    [InlineData(Funding.FarFuture)]
    [InlineData(Funding.Requested)]
    public async Task Replace_ResponsePaidUntil_ComesFromFunding(Funding funding)
    {
        var s = await SeedAsync(funding, ChannelState.Blocked);

        var response = await AuthedClient(s.Owner.Token).PostAsync($"/api/notification-channels/{s.ChannelId}/replace", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await response.Content.ReadJsonAsync<ReplaceChannelResponseDto>())!;
        AssertPaidUntil(result.PaidUntil, s.ExpectedPaidUntil, funding);

        // The owner's own list (already funding-based since cycle 7) agrees for the new channel.
        var list = await GetJsonAsync<ChannelListDto>(AuthedClient(s.Owner.Token), "/api/notification-channels");
        AssertPaidUntil(list.Channels.Single(c => c.Id == result.NewChannelId).PaidUntil, s.ExpectedPaidUntil, funding);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private static void AssertPaidUntil(DateTime? actual, DateTime? expected, Funding funding)
    {
        if (expected is null)
            actual.Should().BeNull(funding.ToString());
        else
            actual.Should().BeCloseTo(expected.Value, DbPrecision, funding.ToString());
    }

    private async Task<T> GetJsonAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return (await response.Content.ReadJsonAsync<T>())!;
    }

    private Task<AdminChannelSummaryDto> GetSummaryAsync(HttpClient adminClient) =>
        GetJsonAsync<AdminChannelSummaryDto>(adminClient, "/api/admin/notification-channels/summary");

    private async Task RunChannelHealthPassAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "channel-health");
        await task.ExecuteAsync(CancellationToken.None);
    }

    /// <summary>One owner + company + billing account, funded per <paramref name="funding"/>, with one
    /// requested channel assigned to the company. Seeded straight into the database — States that are
    /// not polled by ChannelHealthTask (NotConnected/NeedsReconnect/Blocked) and no provider instance,
    /// so a health pass only recomputes idle.</summary>
    private async Task<Seeded> SeedAsync(
        Funding funding, ChannelState state = ChannelState.NotConnected, DateTime? idleSinceUtc = null,
        bool suspendedByAdmin = false, bool companyActive = true)
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        if (funding != Funding.NoSubscription) await GiveNotificationCapablePlanAsync(owner.UserId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var trackedCompany = await db.Companies.FirstAsync(c => c.Id == company.Id);
        if (trackedCompany.BillingAccountId is null)
        {
            var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == owner.UserId)
                ?? db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = owner.UserId }).Entity;
            trackedCompany.BillingAccountId = account.Id;
            await db.SaveChangesAsync();
        }
        var accountId = trackedCompany.BillingAccountId!.Value;
        trackedCompany.IsActive = companyActive;

        var nowUtc = DateTime.UtcNow;
        DateTime? expectedPaidUntil = null;
        var funded = false;
        switch (funding)
        {
            case Funding.FarFuture:
            case Funding.Expiring:
            case Funding.Expired:
                await EnsureWhatsAppPaidAsync(db, accountId);
                var option = await db.AccountSubscriptionOptions.Include(o => o.Option)
                    .SingleAsync(o => o.BillingAccountId == accountId && o.Option.Code == ServiceBooking.API.Services.SubscriptionResolver.WhatsAppOptionCode);
                option.PaidUntilUtc = funding switch
                {
                    Funding.FarFuture => nowUtc.AddDays(60),
                    Funding.Expiring => nowUtc.AddDays(3),
                    _ => nowUtc.AddDays(-2),
                };
                expectedPaidUntil = option.PaidUntilUtc;
                funded = funding != Funding.Expired;
                break;
            case Funding.Requested:
                // Nothing bought yet: the reader falls back to the subscription's own paid period.
                expectedPaidUntil = await db.AccountSubscriptions.Where(s => s.BillingAccountId == accountId)
                    .Select(s => s.PaidUntil).SingleAsync();
                break;
            case Funding.NoSubscription:
                db.AccountSubscriptions.RemoveRange(await db.AccountSubscriptions
                    .Where(s => s.BillingAccountId == accountId || s.OwnerUserId == owner.UserId).ToListAsync());
                break;
        }

        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = owner.UserId, BillingAccountId = accountId, State = state,
            RequestedAtUtc = nowUtc.AddHours(-1), IdleSinceUtc = idleSinceUtc, IsSuspendedByAdmin = suspendedByAdmin,
            RiskAcceptedAtUtc = nowUtc.AddHours(-1), CreatedAt = nowUtc,
        };
        db.NotificationChannels.Add(channel);
        db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = company.Id, BillingAccountId = accountId,
            AssignedByUserId = owner.UserId,
        });
        await db.SaveChangesAsync();

        return new Seeded(owner, company, accountId, channel.Id, expectedPaidUntil, funded);
    }
}
