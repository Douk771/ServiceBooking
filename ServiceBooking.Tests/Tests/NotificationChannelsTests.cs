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
    public async Task Offer_NoTariffFlag_AllowedByPlanIsAlwaysTrue()
    {
        // Cycle 40 (ARCHITECTURE_CYCLE40.md §40.3.4, A2): the tariff flag is gone, so even a freshly-registered owner with no plan at
        // all gets allowedByPlan = true (the field stays in the contract for the old screens). Was: false for such an owner.
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await SetChannelPriceAsync(990);

        var response = await AuthedClient(owner.Token).GetAsync("/api/notification-channels/offer");
        var offer = (await response.Content.ReadJsonAsync<ChannelOfferDto>())!;
        // API_CONTRACT_CYCLE9.md §114.1: PlanAllows renamed to AllowedByPlan.
        offer.AllowedByPlan.Should().BeTrue();
    }

    [Fact, TestCase("NTF-C004")]
    public async Task CreateChannel_NoTariffFlag_NeverAnswers402OnTariff()
    {
        // Cycle 40 (ARCHITECTURE_CYCLE40.md §40.3.4): the 402 «Подключение канала недоступно на вашем тарифе» is removed — an owner without any
        // plan is no longer refused by the tariff (the gate of the request is the option's availability, §40.7.1, added with the wizard).
        // Was: 402.
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await SetChannelPriceAsync(990);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/notification-channels", ValidCreateChannelRequest());
        response.StatusCode.Should().NotBe((HttpStatusCode)402);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
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
        var acceptRisk = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/notification-channels/{created.Id}/accept-risk",
            new AcceptRiskDto(NotificationRiskTextVersion()));
        acceptRisk.EnsureSuccessStatusCode();
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
        var acceptRisk = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/notification-channels/{created.Id}/accept-risk",
            new AcceptRiskDto(NotificationRiskTextVersion()));
        acceptRisk.EnsureSuccessStatusCode();
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
        body.Should().Contain("платформ",
            "the 409 must be specifically the InstanceCreationEnabled=false gate, not e.g. the unrelated " +
            "\"risk not accepted\" gate — a wrong-reason 409 would make this test pass without proving anything");
    }

    // ── Company assignment ───────────────────────────────────────────────────────────────────────

    // Cycle 40 (ARCHITECTURE_CYCLE40.md §40.28.5, BE-40-2): INTENTIONAL change of expectations. A number works for every company of its
    // billing account, so the assignment routes are legacy: POST answers 410 and changes nothing, DELETE answers 204 and changes nothing
    // (the old tests expected a 409 confirmation / 201 / a cancelled queue).

    [Fact, TestCase("NTF-C011")]
    public async Task AssignCompany_IsGone_410_AndChangesNothing_WhateverTheBody()
    {
        var (owner, _, channel) = await CreateConnectedChannelAsync();
        var company2 = await CreateCompanyAsync(owner.Token);

        foreach (var acknowledged in new[] { false, true })
        {
            var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/notification-channels/{channel.Id}/companies",
                new AssignCompanyDto(company2.Id, WarningAcknowledged: acknowledged));
            response.StatusCode.Should().Be(HttpStatusCode.Gone);
            (await response.Content.ReadAsStringAsync()).Should().Be("Назначать компании больше не нужно: номер работает для всех ваших компаний");
        }

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ChannelCompanyAssignments.AnyAsync(a => a.CompanyId == company2.Id)).Should().BeFalse("the call must not create an assignment");
    }

    [Fact, TestCase("NTF-C012")]
    public async Task AssignCompany_ForeignChannel_Is404_NotTheGone()
    {
        var (owner, _, _) = await CreateConnectedChannelAsync();
        var (_, _, foreignChannel) = await CreateConnectedChannelAsync();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/notification-channels/{foreignChannel.Id}/companies",
            new AssignCompanyDto(Guid.NewGuid(), WarningAcknowledged: true));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign channel is indistinguishable from a missing one (API_CONTRACT_CYCLE4.md §19.2)");
    }

    [Fact, TestCase("NTF-C013")]
    public async Task ChannelDto_Companies_AreAllCompaniesOfTheAccount_WithoutAnyAssignment()
    {
        var (owner, company1, channel) = await CreateConnectedChannelAsync();
        var company2 = await CreateCompanyAsync(owner.Token);

        var dto = (await (await AuthedClient(owner.Token).GetAsync($"/api/notification-channels/{channel.Id}")).Content.ReadJsonAsync<ChannelDto>())!;

        dto.Companies.Select(c => c.CompanyId).Should().BeEquivalentTo([company1.Id, company2.Id]);
    }

    [Fact, TestCase("NTF-C014")]
    public async Task UnassignCompany_IsNoOp_204_QueueUntouched()
    {
        var (owner, company1, channel) = await CreateConnectedChannelAsync();
        Guid rowId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = NewPendingNotification(company1.Id, channel.Id);
            db.OutboundNotifications.Add(row);
            await db.SaveChangesAsync();
            rowId = row.Id;
        }

        var response = await AuthedClient(owner.Token).DeleteAsync($"/api/notification-channels/{channel.Id}/companies/{company1.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.OutboundNotifications.FirstAsync(n => n.Id == rowId);
            row.Status.Should().Be(NotificationStatus.Pending, "the route changes no data any more");
            // ARCHITECTURE_CYCLE4.md §27.1's isolation invariant: a Pending row on a Connected channel must not outlive this test (the real
            // dispatcher scans Pending rows platform-wide).
            db.OutboundNotifications.Remove(row);
            await db.SaveChangesAsync();
        }
    }

    // ── Replace after ban (priority scenario 2) ──────────────────────────────────────────────────

    [Fact, TestCase("NTF-C015")]
    public async Task Replace_BlockedChannel_KeepsPeriod_MovesPendingRows_NotExpired()
    {
        var (owner, company, channel) = await CreateConnectedChannelAsync();

        Guid pendingId, expiredId;
        DateTime? paidUntilBefore;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Cycle 22 (§379, Р2): the paid period belongs to the account — here its WhatsApp option has
            // no own PaidUntilUtc, so it rides the subscription's period (ChannelFundingReader).
            paidUntilBefore = await db.AccountSubscriptions.Where(s => s.BillingAccountId == channel.BillingAccountId)
                .Select(s => s.PaidUntil).SingleAsync();
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

            // Cycle 40 (§40.4, BE-40-2): assignments are no longer moved (nor read) — the new number serves the whole account.
            var assignment = await db.ChannelCompanyAssignments.AsNoTracking().SingleAsync(a => a.CompanyId == company.Id);
            assignment.ChannelId.Should().Be(channel.Id, "the legacy assignment row is left alone");

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
        // ARCHITECTURE_CYCLE20.md §407.2/§437.3 (US-20-07, LG6, cycle 20) — PUT .../owner now requires
        // the new owner to already be linked to the company's billing account; make them a member of
        // this same company first (orthogonal to what this test actually exercises — the channel
        // assignment staying put across an owner change).
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CompanyMembers.Add(new CompanyMember
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, UserId = newOwnerRegistered.UserId, Role = UserRole.Master,
            });
            await db.SaveChangesAsync();
        }

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
        // US-56 п. 2: the paid period survives a voluntary disconnect — it is the account's funding
        // (cycle 22, §379), which a disconnect does not touch.
        var list = (await (await AuthedClient(owner.Token).GetAsync("/api/notification-channels")).Content.ReadJsonAsync<ChannelListDto>())!;
        list.Channels.Single(c => c.Id == channel.Id).PaidUntil.Should().NotBeNull("US-56 п. 2: paid period survives a voluntary disconnect");

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
        var channel = await db.NotificationChannels.AsNoTracking().FirstAsync(c => c.Id == channelId);

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

    private string NotificationRiskTextVersion()
    {
        // ARCHITECTURE_CYCLE9.md §104.8 (B12): riskText/riskVersion now come from the ChannelRiskNotice
        // legal document (App_Data/legal/legal.json), not the deleted NotificationRiskText constant.
        // Read live from LegalDocumentProvider (same pattern as NotificationTestBase.CreateCompanyAsync)
        // instead of hardcoding a version string, so a legal.json bump can't silently desync this file
        // from the running manifest.
        using var scope = Factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>();
        return provider.Current!.Get(LegalDocumentType.ChannelRiskNotice)!.Version;
    }

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
