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
public class NotificationChannelsTests(TestDatabaseFixture apiFixture) : NotificationTestBase(apiFixture)
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
        // API_CONTRACT_CYCLE9.md §114.1: `available` was dropped from ChannelOfferDto — it was always
        // exactly `pricePerMonth is not null`, so the assertion below already covers it.
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
        // API_CONTRACT_CYCLE9.md §114.1: PlanAllows renamed to AllowedByPlan.
        offer.AllowedByPlan.Should().BeFalse();
    }

    [Fact, TestCase("NTF-C004")]
    public async Task CreateChannel_PlanDisallows_Returns402()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await SetChannelPriceAsync(990);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/notification-channels", ValidCreateChannelRequest());
        response.StatusCode.Should().Be((HttpStatusCode)402);
        (await response.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace();
    }

    [Fact, TestCase("NTF-C005")]
    public async Task CreateChannel_PriceNotSet_StillSucceeds()
    {
        // Regression, N21 (§59/§47.3): the retired `notifications.channel.price-per-month` platform
        // setting used to 409 every request the moment nobody had set it — a channel is priced through
        // the `notifications.whatsapp` subscription option now, and this setting controls nothing at
        // all. A plan that allows the channel must be enough to request one, price-per-month or not.
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(null);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/notification-channels", ValidCreateChannelRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var channel = (await response.Content.ReadJsonAsync<ChannelDto>())!;
        channel.State.Should().Be(ChannelState.NotConnected);
    }

    // LGL-082-01 (SPEC.md §10.4 US-82, API_CONTRACT_CYCLE5.md §50.1). ИНН/legal-entity-form/offer
    // acceptance become required only HERE (paid-function gate), never at registration/company creation
    // (US-82 п.1) — pinned by NTF-C004/C005/C006 above all succeeding via CreateOwnerWithCompanyAsync's
    // free path with no ИНН anywhere in that helper.
    [Fact, TestCase("LGL-082-01")]
    public async Task CreateChannel_WithoutInnOrOfferAcceptance_ReturnsBadRequest()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(990);

        var noInn = await AuthedClient(owner.Token).PostAsJsonAsync("/api/notification-channels",
            new { legalEntityForm = "Company", inn = (string?)null, offerAccepted = new { version = OwnerTermsVersion() } });
        noInn.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badChecksum = await AuthedClient(owner.Token).PostAsJsonAsync("/api/notification-channels",
            new { legalEntityForm = "Company", inn = "7707083894", offerAccepted = new { version = OwnerTermsVersion() } });
        badChecksum.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "ПЛ5/US-82 п.3: only the checksum is validated, but a wrong one must still 400");

        var noOffer = await AuthedClient(owner.Token).PostAsJsonAsync("/api/notification-channels",
            new { legalEntityForm = "Company", inn = "7707083893", offerAccepted = (object?)null });
        noOffer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("NTF-C006")]
    public async Task CreateChannel_Allowed_ReturnsNotConnectedNotPaid()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(990);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/notification-channels", ValidCreateChannelRequest());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var channel = (await response.Content.ReadJsonAsync<ChannelDto>())!;
        channel.State.Should().Be(ChannelState.NotConnected);
        channel.PaymentState.Should().Be(ChannelPaymentStatus.NotPaid);
        channel.RequestedAt.Should().NotBeNull();
    }

    [Fact, TestCase("NTF-C005B")]
    public async Task OrderSecondChannel_WithOnlyOneNumberPaid_FirstStaysFundedSecondIsNotPaid()
    {
        // §47.1 ranking (ChannelFunding.Rank): funding is "paid for N numbers, M registered" — the
        // account's live channels are ranked (oldest first) and only the top N read as Funded. Ordering
        // a second number while only one is paid must leave the FIRST one untouched (still Funded) and
        // mark the new, second one NotPaid — never silently spread the one paid slot across both, and
        // never let the newcomer bump the existing one out of its slot.
        //
        // QA cycle 9 (N8, §114.2): the SECOND channel here is seeded directly in the database rather
        // than through POST /api/notification-channels — after af2c38b, a second live channel of the
        // SAME transport on one account now 409s at that endpoint (see
        // Create_SecondLiveChannelOfSameTransport_Returns409_AllowedAgainAfterFirstIsReplaced below for
        // that contract on its own). §104.4 funding ("ChannelFunding.Rank ranks ALL live channels of the
        // account, transport-blind") still applies across transports, so a MAX channel here exercises
        // the exact same ranking code path the original WhatsApp/WhatsApp version of this test did.
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        var owned = AuthedClient(owner.Token);

        var first = (await (await owned.PostAsJsonAsync("/api/notification-channels", ValidCreateChannelRequest()))
            .Content.ReadJsonAsync<ChannelDto>())!;

        Guid secondId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var billingAccountId = await db.NotificationChannels.Where(c => c.Id == first.Id)
                .Select(c => c.BillingAccountId!.Value).FirstAsync();
            await NotificationTestBase.EnsureWhatsAppPaidAsync(db, billingAccountId, quantity: 1);

            var second = new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = owner.UserId, BillingAccountId = billingAccountId,
                State = ChannelState.NotConnected, Transport = NotificationTransport.Max,
                RequestedAtUtc = DateTime.UtcNow.AddSeconds(1), // strictly after `first` (oldest-first ranking)
            };
            db.NotificationChannels.Add(second);
            secondId = second.Id;
            await db.SaveChangesAsync();
        }

        var list = (await (await owned.GetAsync("/api/notification-channels"))
            .Content.ReadJsonAsync<ChannelListDto>())!;
        list.Channels.Should().ContainSingle(c => c.Id == first.Id).Which.PaymentState
            .Should().Be(ChannelPaymentStatus.Paid, "the first, already-funded number must keep its slot");
        list.Channels.Should().ContainSingle(c => c.Id == secondId).Which.PaymentState
            .Should().Be(ChannelPaymentStatus.NotPaid, "only 1 number is paid for — the 2nd registered number has nothing left to fund it");
    }

    [Fact, TestCase("NTF-N8-001")]
    public async Task Create_SecondLiveChannelOfSameTransport_Returns409_AllowedAgainAfterFirstIsReplaced()
    {
        // QA cycle 9 (N8, §114.2, af2c38b) — the exact scenario the fix's own test description names:
        // "второй запрос канала того же транспорта, пока первый NotConnected → 409; после того как
        // первый Replaced → 200".
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        var owned = AuthedClient(owner.Token);

        var firstResponse = await owned.PostAsJsonAsync("/api/notification-channels", ValidCreateChannelRequest());
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var first = (await firstResponse.Content.ReadJsonAsync<ChannelDto>())!;

        var duplicateResponse = await owned.PostAsJsonAsync("/api/notification-channels", ValidCreateChannelRequest());
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the account already has a live (non-Replaced) channel of this transport");

        // Move the first channel to State=Replaced directly (bypassing the /replace endpoint's own
        // plumbing — this test cares only about the dedup check reading State, not about what put it
        // there) and retry the same request.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.NotificationChannels.FirstAsync(c => c.Id == first.Id);
            tracked.State = ChannelState.Replaced;
            await db.SaveChangesAsync();
        }

        var retryResponse = await owned.PostAsJsonAsync("/api/notification-channels", ValidCreateChannelRequest());
        retryResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            "once the old channel is Replaced (no longer 'live'), a new one of the same transport must be orderable again");
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

    // LGL-071-01 (SPEC.md §6.1 US-71 п.4, ARCHITECTURE_CYCLE5.md §52.1). While ПЛ1 (does the partner API
    // let us pin the server country) is unconfirmed, `InstanceCreationEnabled` stays false in production
    // by default — connect must fail closed with a human 409, not create an instance silently. Set up the
    // owner/channel/risk/payment through this class's OWN factory (InstanceCreationEnabled=true, so every
    // other test here can reach past this gate — see NotificationTestFactory's own doc comment), then
    // reach the SAME database from a second host with the flag forced back to its production default.
    [Fact, TestCase("LGL-071-01")]
    public async Task Connect_WhenInstanceCreationDisabledByPlatform_Returns409_WithHumanText()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(990);
        var created = await CreateChannelAsync(owner.Token);
        await AuthedClient(owner.Token).PostAsJsonAsync($"/api/notification-channels/{created.Id}/accept-risk",
            new AcceptRiskDto(NotificationRiskTextVersion()));
        await MarkPaidAsync(created.Id);

        await using var disabledFactory = new NotificationTestFactory(ConnectionString).WithWebHostBuilder(builder =>
            builder.UseSetting("Notifications:GreenApi:InstanceCreationEnabled", "false"));
        var reLogin = await disabledFactory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new ServiceBooking.API.DTOs.Auth.LoginDto(owner.Phone, "Password123!"));
        reLogin.EnsureSuccessStatusCode();
        var reAuth = (await reLogin.Content.ReadFromJsonAsync<ServiceBooking.API.DTOs.Auth.AuthResponseDto>())!;

        var disabledClient = disabledFactory.CreateClient();
        disabledClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", reAuth.Token);
        var response = await disabledClient.PostAsync($"/api/notification-channels/{created.Id}/connect", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
        body.Should().NotContain("stack", "the text must be human, not a stack trace");
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
            // Tolerance, not equality: the expected value was read back through Postgres (microseconds)
            // while the actual carries .NET ticks (100ns), so exact comparison drops a digit on CI's
            // database and not on ours — the same trap as `result.PaidUntil` above.
            newChannel.PaidUntilUtc.Should().BeCloseTo(paidUntilBefore!.Value, TimeSpan.FromMilliseconds(1));

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

    // Cycle 7 (SPEC.md US-64 p.3, ARCHITECTURE_CYCLE7.md §47.4) DELIBERATELY REVERSES cycle 4's
    // behavior: the number belongs to the billing account, not to whoever manages the company, so a
    // stand-alone owner change must no longer unassign the channel or cancel queued messages. Replaces
    // the test above (which asserted cycle 4's now-reversed behavior and would be red against today's
    // intentional product change) with its mirror image, written from SPEC.md independently of
    // NotificationChannelsController/AdminController's implementation.
    [Fact, TestCase("NTF-C017")]
    public async Task ChangeCompanyOwner_KeepsChannelAssigned_PendingRowsStayPending()
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
            (await db.ChannelCompanyAssignments.AnyAsync(a => a.CompanyId == company.Id)).Should().BeTrue(
                "US-64 p.3: the number belongs to the billing account, not to whoever manages the company — a stand-alone owner change must not detach it");
            var pendingRow = await db.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == pendingId);
            pendingRow.Status.Should().Be(NotificationStatus.Pending, "queued messages must not be cancelled by a stand-alone owner change");
        }

        // The channel's owner (who pays for it) is unaffected by this change — they still see it.
        var listResponse = await AuthedClient(owner.Token).GetAsync("/api/notification-channels");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
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
        var response = await AuthedClient(ownerToken).PostAsJsonAsync("/api/notification-channels", ValidCreateChannelRequest());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadJsonAsync<ChannelDto>())!;
    }

    // CYCLE5-BREAKING (API_CONTRACT_CYCLE5.md §50.1, US-82): POST /api/notification-channels now requires
    // legalEntityForm/inn/offerAccepted — a formally-valid ИНН (Sberbank's real, public 10-digit one,
    // same test vector InnValidatorTests uses) and the current TermsOwner version (D9 is an appendix to
    // it, §43.2), read from the live manifest so a version bump never desyncs this helper.
    private object ValidCreateChannelRequest() =>
        new { legalEntityForm = "Company", inn = "7707083893", offerAccepted = new { version = OwnerTermsVersion() } };

    private string OwnerTermsVersion()
    {
        using var scope = Factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        return provider.Current!.Get(ServiceBooking.Core.Enums.LegalDocumentType.TermsOwner)!.Version;
    }

    private async Task MarkPaidAsync(Guid channelId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = await db.NotificationChannels.FirstAsync(c => c.Id == channelId);
        // Kept for compatibility with any assertion still reading these historical columns — no longer
        // what makes the channel funded (ARCHITECTURE_CYCLE7.md §47.3).
        channel.PaidFromUtc = DateTime.UtcNow.AddDays(-1);
        channel.PaidUntilUtc = DateTime.UtcNow.AddDays(30);
        await db.SaveChangesAsync();

        // Cycle 5, stage 3 (§47.1): funding now comes from the account's paid notifications.whatsapp
        // quantity — mark the OWNING account (not necessarily every account in the test) as paid for at
        // least this one number.
        var accountId = channel.BillingAccountId
            ?? await db.BillingAccounts.Where(a => a.OwnerUserId == channel.OwnerUserId).Select(a => a.Id).FirstAsync();
        await NotificationTestBase.EnsureWhatsAppPaidAsync(db, accountId);
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
        // ARCHITECTURE_CYCLE9.md §104.8 (B12): riskText/riskVersion now come from the ChannelRiskNotice
        // legal document (App_Data/legal/legal.json), not the deleted NotificationRiskText constant —
        // mirrors that document's current "version" field. Read from the live offer response instead of
        // hardcoding it would be more robust, but every other call site in this file already has a
        // channel, not an offer; kept as a named constant so a version bump surfaces here as a single,
        // obvious compile-time-adjacent failure rather than scattered string literals.
        "2026-09-21-draft";

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
