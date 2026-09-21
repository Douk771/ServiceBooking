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
/// QA cycle 4 (SPEC.md §4, API_CONTRACT_CYCLE4.md §19-27, §33) — the channel lifecycle surface: request,
/// risk acceptance, connect/QR, company assignment, disconnect, replace-after-ban, owner change, and the
/// provider webhook. Written from SPEC.md/API_CONTRACT_CYCLE4.md, independently of
/// NotificationChannelsController's own implementation, per this cycle's QA brief.
/// Each test owns its own <see cref="NotificationTestFactory"/> instance (see that class's doc comment).
/// </summary>
public class NotificationChannelsTests(TestDatabaseFixture fixture) : NotificationTestBase(fixture)
{
    // ── GET /api/notification-channels, offer, request ──────────────────────────────────────────

    [Fact, TestCase("NTF-C001")]
    public async Task GetChannels_NonOwner_ReturnsForbidden()
    {
        var registered = await RegisterAsync(); // never created a company
        var response = await AuthedClient(registered.Token).GetAsync("/api/notification-channels");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty("§19.2: 403 body must be empty");
    }

    [Fact, TestCase("NTF-C002")]
    public async Task Offer_PriceNotSet_ReturnsUnavailable_NotZero()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(null); // superadmin never set a price — expected post-launch state (SPEC Q1)

        var response = await AuthedClient(owner.Token).GetAsync("/api/notification-channels/offer");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var offer = (await response.Content.ReadJsonAsync<ChannelOfferDto>())!;
        offer.Available.Should().BeFalse();
        offer.PricePerMonth.Should().BeNull("§21: unavailable must read as null, not a misleading 0");
    }

    [Fact, TestCase("NTF-C003")]
    public async Task Offer_PlanDoesNotAllowChannel_PlanAllowsFalse()
    {
        // A freshly-registered owner has no plan at all → SubscriptionResolver's baseline default,
        // AllowNotificationChannel = false (SPEC "ожидаемое состояние сразу после выката").
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await SetChannelPriceAsync(990);

        var response = await AuthedClient(owner.Token).GetAsync("/api/notification-channels/offer");
        var offer = (await response.Content.ReadJsonAsync<ChannelOfferDto>())!;
        offer.PlanAllows.Should().BeFalse();
    }

    [Fact, TestCase("NTF-C004")]
    public async Task CreateChannel_PlanDisallows_Returns402()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await SetChannelPriceAsync(990);

        var response = await AuthedClient(owner.Token).PostAsync("/api/notification-channels", null);
        response.StatusCode.Should().Be((HttpStatusCode)402);
        (await response.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace();
    }

    [Fact, TestCase("NTF-C005")]
    public async Task CreateChannel_PriceNotSet_Returns409()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(null);

        var response = await AuthedClient(owner.Token).PostAsync("/api/notification-channels", null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("NTF-C006")]
    public async Task CreateChannel_Allowed_ReturnsNotConnectedNotPaid()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(990);

        var response = await AuthedClient(owner.Token).PostAsync("/api/notification-channels", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var channel = (await response.Content.ReadJsonAsync<ChannelDto>())!;
        channel.State.Should().Be(ChannelState.NotConnected);
        channel.PaymentState.Should().Be(ChannelPaymentStatus.NotPaid);
        channel.RequestedAt.Should().NotBeNull();
    }

    // ── Accept-risk / connect gating ─────────────────────────────────────────────────────────────

    [Fact, TestCase("NTF-C007")]
    public async Task Connect_UnpaidChannel_Returns402()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(990);
        var created = await CreateChannelAsync(owner.Token);

        var response = await AuthedClient(owner.Token).PostAsync($"/api/notification-channels/{created.Id}/connect", null);
        response.StatusCode.Should().Be((HttpStatusCode)402);
    }

    [Fact, TestCase("NTF-C008")]
    public async Task Connect_RiskNotAccepted_Returns409()
    {
        var (owner, _, channel) = await CreateConnectedChannelAsync();
        // Force back to NotConnected + strip risk acceptance to isolate the risk gate from "already connected".
        await SetChannelStateAsync(channel.Id, ChannelState.NotConnected, riskAcceptedAtUtc: null);

        var response = await AuthedClient(owner.Token).PostAsync($"/api/notification-channels/{channel.Id}/connect", null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("условия");
    }

    [Fact, TestCase("NTF-C009")]
    public async Task AcceptRisk_StaleVersion_Returns400()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(990);
        var created = await CreateChannelAsync(owner.Token);

        var response = await AuthedClient(owner.Token)
            .PostAsJsonAsync($"/api/notification-channels/{created.Id}/accept-risk", new AcceptRiskDto("not-a-real-version"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Double-click / concurrent connect (priority scenario 4) ─────────────────────────────────

    [Fact, TestCase("NTF-C010")]
    public async Task Connect_DoubleClick_SecondConcurrentRequestGets409_NoSecondInstance()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(990);
        var created = await CreateChannelAsync(owner.Token);
        await AuthedClient(owner.Token).PostAsJsonAsync($"/api/notification-channels/{created.Id}/accept-risk",
            new AcceptRiskDto(NotificationRiskTextVersion()));
        // Mark paid directly (the owner-facing flow has no self-serve payment, SPEC §9.1 — an admin does it).
        await MarkPaidAsync(created.Id);

        var client1 = AuthedClient(owner.Token);
        var client2 = AuthedClient(owner.Token);

        var task1 = client1.PostAsync($"/api/notification-channels/{created.Id}/connect", null);
        var task2 = client2.PostAsync($"/api/notification-channels/{created.Id}/connect", null);
        var results = await Task.WhenAll(task1, task2);

        var statusCodes = results.Select(r => r.StatusCode).OrderBy(c => c).ToList();
        statusCodes.Should().Contain(HttpStatusCode.Accepted, "exactly one of the two concurrent requests must win");
        statusCodes.Should().Contain(HttpStatusCode.Conflict, "the loser must be told the number is already connecting, not silently overwritten (I3)");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = await db.NotificationChannels.AsNoTracking().FirstAsync(c => c.Id == created.Id);
        channel.ProviderInstanceId.Should().NotBeNull("the winner must have created exactly one instance");
    }

    // ── Company assignment ───────────────────────────────────────────────────────────────────────

    [Fact, TestCase("NTF-C011")]
    public async Task AssignCompany_SecondCompanyWithoutAck_Returns409()
    {
        var (owner, company1, channel) = await CreateConnectedChannelAsync();
        var company2 = await CreateCompanyAsync(owner.Token);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/notification-channels/{channel.Id}/companies",
            new AssignCompanyDto(company2.Id, WarningAcknowledged: false));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("NTF-C012")]
    public async Task AssignCompany_SecondCompanyWithAck_Succeeds()
    {
        var (owner, _, channel) = await CreateConnectedChannelAsync();
        var company2 = await CreateCompanyAsync(owner.Token);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/notification-channels/{channel.Id}/companies",
            new AssignCompanyDto(company2.Id, WarningAcknowledged: true));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await response.Content.ReadJsonAsync<ChannelDto>())!;
        dto.Companies.Should().HaveCount(2);
    }

    [Fact, TestCase("NTF-C013")]
    public async Task AssignCompany_AlreadyOnAnotherChannel_Returns409()
    {
        var (owner, _, channelA) = await CreateConnectedChannelAsync();
        var (_, companyB, _) = await CreateConnectedChannelAsync(); // different owner+channel+company

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/notification-channels/{channelA.Id}/companies",
            new AssignCompanyDto(companyB.Id, WarningAcknowledged: true));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "companyB belongs to a different owner");
    }

    [Fact, TestCase("NTF-C014")]
    public async Task UnassignCompany_CancelsOnlyThatCompanysPendingRows_LeavesSiblingCompanyAlone()
    {
        var (owner, company1, channel) = await CreateConnectedChannelAsync();
        var company2 = await CreateCompanyAsync(owner.Token);
        await AuthedClient(owner.Token).PostAsJsonAsync($"/api/notification-channels/{channel.Id}/companies",
            new AssignCompanyDto(company2.Id, WarningAcknowledged: true));

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OutboundNotifications.AddRange(
                NewPendingNotification(company1.Id, channel.Id),
                NewPendingNotification(company2.Id, channel.Id));
            await db.SaveChangesAsync();
        }

        var response = await AuthedClient(owner.Token).DeleteAsync($"/api/notification-channels/{channel.Id}/companies/{company1.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.OutboundNotifications.Where(n => n.ChannelId == channel.Id).ToListAsync();
            rows.Single(r => r.CompanyId == company1.Id).Status.Should().Be(NotificationStatus.Cancelled);
            rows.Single(r => r.CompanyId == company2.Id).Status.Should().Be(NotificationStatus.Pending, "sibling company's queue must be untouched");

            // ARCHITECTURE_CYCLE4.md §27.1's isolation invariant: this is the one test in this file that
            // deliberately leaves a Pending row on a channel still Connected at the assertion point above
            // — clean it up now so it can never be picked up by a LATER, unrelated
            // NotificationDispatchTestFactory run sharing the same "servicebooking_test" database (the
            // real dispatcher scans Pending rows platform-wide, not scoped to whichever test created them).
            var survivingRow = rows.Single(r => r.CompanyId == company2.Id);
            db.OutboundNotifications.Remove(survivingRow);
            await db.SaveChangesAsync();
        }
    }

    // ── Replace after ban (priority scenario 2) ──────────────────────────────────────────────────

    [Fact, TestCase("NTF-C015")]
    public async Task Replace_BlockedChannel_MovesPeriodAssignmentsAndPendingRows_NotExpired()
    {
        var (owner, company, channel) = await CreateConnectedChannelAsync(paidUntilUtc: DateTime.UtcNow.AddDays(20));
        var paidUntilBefore = channel.PaidUntilUtc;

        Guid pendingId, expiredId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.NotificationChannels.FirstAsync(c => c.Id == channel.Id);
            tracked.State = ChannelState.Blocked;

            var pending = NewPendingNotification(company.Id, channel.Id);
            var expired = NewPendingNotification(company.Id, channel.Id);
            expired.Status = NotificationStatus.Expired;
            db.OutboundNotifications.AddRange(pending, expired);
            pendingId = pending.Id;
            expiredId = expired.Id;
            await db.SaveChangesAsync();
        }

        var response = await AuthedClient(owner.Token).PostAsync($"/api/notification-channels/{channel.Id}/replace", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await response.Content.ReadJsonAsync<ReplaceChannelResponseDto>())!;
        result.CompaniesMoved.Should().Be(1);
        // CI-flake fix: `paidUntilBefore` is captured from an in-process DateTime (100ns ticks);
        // `result.PaidUntil` came back through Postgres (microsecond precision) and JSON — an exact
        // .Be(...) compares ticks bit-for-bit and is flaky depending on where the seed's random tick
        // landed. 1ms tolerance is generous for round-trip truncation, still tight enough to catch a real
        // "period got recalculated instead of carried through" bug.
        result.PaidUntil.Should().BeCloseTo(paidUntilBefore!.Value, TimeSpan.FromMilliseconds(1));

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var oldChannel = await db.NotificationChannels.AsNoTracking().FirstAsync(c => c.Id == channel.Id);
            oldChannel.State.Should().Be(ChannelState.Replaced);
            oldChannel.ReplacedByChannelId.Should().Be(result.NewChannelId);
            oldChannel.PaidUntilUtc.Should().BeNull("N6: the paid period must not double-count on the terminal row");

            var newChannel = await db.NotificationChannels.AsNoTracking().FirstAsync(c => c.Id == result.NewChannelId);
            newChannel.PaidUntilUtc.Should().Be(paidUntilBefore);

            var assignment = await db.ChannelCompanyAssignments.AsNoTracking().SingleAsync(a => a.CompanyId == company.Id);
            assignment.ChannelId.Should().Be(result.NewChannelId);

            var pendingRow = await db.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == pendingId);
            pendingRow.ChannelId.Should().Be(result.NewChannelId, "Pending rows migrate to the new channel");

            var expiredRow = await db.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == expiredId);
            expiredRow.ChannelId.Should().Be(channel.Id, "Expired rows must NOT be revived onto the new channel");
        }
    }

    [Fact, TestCase("NTF-C016")]
    public async Task Replace_ChannelNotBlocked_Returns409()
    {
        var (owner, _, channel) = await CreateConnectedChannelAsync(); // still Connected, not Blocked
        var response = await AuthedClient(owner.Token).PostAsync($"/api/notification-channels/{channel.Id}/replace", null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Owner change on a company (priority scenario 3) ──────────────────────────────────────────

    [Fact, TestCase("NTF-C017")]
    public async Task ChangeCompanyOwner_UnassignsChannel_CancelsPendingRows_OldOwnerStillSeesChannelInList()
    {
        var (owner, company, channel) = await CreateConnectedChannelAsync();
        Guid pendingId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = NewPendingNotification(company.Id, channel.Id);
            db.OutboundNotifications.Add(pending);
            pendingId = pending.Id;
            await db.SaveChangesAsync();
        }

        var newOwnerRegistered = await RegisterAsync();
        var admin = await LoginAsSuperAdminAsync();
        var changeResponse = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/companies/{company.Id}/owner", new { newOwnerUserId = newOwnerRegistered.UserId });
        changeResponse.EnsureSuccessStatusCode();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ChannelCompanyAssignments.AnyAsync(a => a.CompanyId == company.Id)).Should().BeFalse("assignment must be removed on owner change");
            var pendingRow = await db.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == pendingId);
            pendingRow.Status.Should().Be(NotificationStatus.Cancelled);
        }

        // Regression this cycle fixed: the PREVIOUS owner must still see (and own) the channel they paid
        // for, even though they may now own zero companies.
        var listResponse = await AuthedClient(owner.Token).GetAsync("/api/notification-channels");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, "a former owner must not be 403'd out of a channel they still own/pay for");
        var list = (await listResponse.Content.ReadJsonAsync<ChannelListDto>())!;
        list.Channels.Should().ContainSingle(c => c.Id == channel.Id);
    }

    // ── Disconnect ────────────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("NTF-C018")]
    public async Task Disconnect_KeepsPaidPeriod_CancelsPendingRows()
    {
        var (owner, company, channel) = await CreateConnectedChannelAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OutboundNotifications.Add(NewPendingNotification(company.Id, channel.Id));
            await db.SaveChangesAsync();
        }

        var response = await AuthedClient(owner.Token).DeleteAsync($"/api/notification-channels/{channel.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = Factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db2.NotificationChannels.AsNoTracking().FirstAsync(c => c.Id == channel.Id);
        reloaded.State.Should().Be(ChannelState.DisabledByOwner);
        reloaded.PaidUntilUtc.Should().NotBeNull("US-56 п. 2: paid period survives a voluntary disconnect");

        var rows = await db2.OutboundNotifications.Where(n => n.ChannelId == channel.Id).ToListAsync();
        rows.Should().OnlyContain(r => r.Status == NotificationStatus.Cancelled);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private async Task<ChannelDto> CreateChannelAsync(string ownerToken)
    {
        var response = await AuthedClient(ownerToken).PostAsync("/api/notification-channels", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadJsonAsync<ChannelDto>())!;
    }

    private async Task MarkPaidAsync(Guid channelId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = await db.NotificationChannels.FirstAsync(c => c.Id == channelId);
        channel.PaidFromUtc = DateTime.UtcNow.AddDays(-1);
        channel.PaidUntilUtc = DateTime.UtcNow.AddDays(30);
        await db.SaveChangesAsync();
    }

    private async Task SetChannelStateAsync(Guid channelId, ChannelState state, DateTime? riskAcceptedAtUtc)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = await db.NotificationChannels.FirstAsync(c => c.Id == channelId);
        channel.State = state;
        channel.RiskAcceptedAtUtc = riskAcceptedAtUtc;
        channel.ProviderInstanceId = null;
        channel.ProviderSecretCiphertext = null;
        await db.SaveChangesAsync();
    }

    private static string NotificationRiskTextVersion() =>
        // Mirrors NotificationRiskText.CurrentVersion (API_CONTRACT_CYCLE4.md §23) — read from the live
        // offer response instead of hardcoding it would be more robust, but every other call site in this
        // file already has a channel, not an offer; kept as a named constant so a version bump surfaces
        // here as a single, obvious compile-time-adjacent failure rather than scattered string literals.
        "2026-09-18-draft";

    private static OutboundNotification NewPendingNotification(Guid companyId, Guid channelId)
    {
        var nowUtc = DateTime.UtcNow;
        var id = Guid.NewGuid();
        return new OutboundNotification
        {
            Id = id, CompanyId = companyId, ChannelId = channelId, Type = NotificationType.BookingConfirmed,
            RecipientPhone = "79990001122", Body = "Test",
            DueAtUtc = nowUtc.AddMinutes(-1), VisitStartUtc = nowUtc.AddHours(2),
            Status = NotificationStatus.Pending,
            IdempotencyKey = $"test:{id}",
        };
    }
}
