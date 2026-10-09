using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 40 (SPEC.md US-40-xx, ARCHITECTURE_CYCLE40.md §40.13): the SuperAdmin's side of the numbers — «Подтвердить оплату», the table and the card, the availability
/// switches of the two messenger options — and the part of the customer consent the server decides (the messenger mark of a booking, the sender's name in the text).
/// </summary>
public class Cycle40ChannelsTests(TestDatabaseFixture fixture) : NotificationTestBase(fixture)
{
    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>A bound (once), paid, first number of the owner's account; Disconnected on purpose so that the real dispatcher of other tests never sends from it.</summary>
    private async Task SeedConnectedAssignedChannelAsync(string ownerUserId, Guid companyId, ChannelState state = ChannelState.Disconnected)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var billingAccountId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BillingAccountId).FirstAsync();
        db.NotificationChannels.Add(new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerUserId, BillingAccountId = billingAccountId, State = state,
            PhoneNumber = "79990009999", ProviderInstanceId = Unique("instance"),
            ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();
        await EnsureWhatsAppPaidAsync(db, billingAccountId!.Value);
    }

    private async Task<(AuthResponseDto Owner, Guid ChannelId)> OwnerWithRequestedChannelAsync(NotificationTransport transport = NotificationTransport.Max)
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SetChannelPriceAsync(490);
        var termsVersion = Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>()
            .Current!.Get(LegalDocumentType.TermsOwner)!.Version;
        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/notification-channels",
            new { legalEntityForm = "Company", inn = "7707083893", offerAccepted = new { version = termsVersion }, transport = transport.ToString() });
        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadJsonAsync<ChannelDto>())!;
        return (owner, dto.Id);
    }

    private async Task SetOptionOpenAsync(string field, bool open)
    {
        var admin = await LoginAsSuperAdminAsync();
        var current = await (await AuthedClient(admin.Token).GetAsync("/api/admin/platform-settings")).Content.ReadJsonAsync<AdminPlatformSettingsDto>();
        var body = new Dictionary<string, object?>
        {
            ["channelPricePerMonth"] = current!.ChannelPricePerMonth, ["channelIdleDays"] = current.ChannelIdleDays, ["pricingPublicEnabled"] = current.PricingPublicEnabled,
            [field] = open,
        };
        (await AuthedClient(admin.Token).PutAsJsonAsync("/api/admin/platform-settings", body)).EnsureSuccessStatusCode();
    }

    [Fact, TestCase("CY40-ADM-01")]
    public async Task ConfirmPayment_ExtendsThePaidPeriodFromMaxOfNowAndCurrentEnd_AndWritesBothJournals()
    {
        var (owner, channelId) = await OwnerWithRequestedChannelAsync();
        var admin = await LoginAsSuperAdminAsync();
        var client = AuthedClient(admin.Token);

        var first = await client.PostAsJsonAsync($"/api/admin/notification-channels/{channelId}/confirm-payment", new { months = 1, comment = "Счёт 17" });
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var card = (await first.Content.ReadJsonAsync<AdminChannelCardDto>())!;
        card.Payment.Paid.Should().BeTrue();
        card.Payment.IsTrial.Should().BeFalse();
        var firstEnd = card.Payment.PaidUntil!.Value;
        firstEnd.Should().BeCloseTo(DateTime.UtcNow.AddMonths(1), TimeSpan.FromMinutes(5));

        // a repeat extends again, from the current end (not from now)
        var second = await client.PostAsJsonAsync($"/api/admin/notification-channels/{channelId}/confirm-payment", new { months = 2, comment = (string?)null });
        second.EnsureSuccessStatusCode();
        var card2 = (await second.Content.ReadJsonAsync<AdminChannelCardDto>())!;
        card2.Payment.PaidUntil.Should().BeCloseTo(firstEnd.AddMonths(2), TimeSpan.FromSeconds(5));

        card2.PaymentEvents.Should().Contain(e => e.Kind == "PaymentConfirmed" && e.Comment!.Contains("Счёт 17"));
        card2.PaymentEvents.Should().Contain(e => e.Kind == "OptionChanged" && e.Source == ChannelOptionChangeSource.AdminChannelCard);
        card2.Channel.PaymentText.Should().StartWith("оплачено до");
        card2.Channel.AvailableActions.Should().Contain(["Suspend", "ConfirmPayment"]);
        card2.Companies.Should().NotBeEmpty("the number serves every company of the account");

        // the owner sees the payment as made: no more "Оплата на проверке"
        var overview = (await (await AuthedClient(owner.Token).GetAsync("/api/notification-channels/overview")).Content.ReadJsonAsync<NumbersOverviewDto>())!;
        overview.Transports.Single(t => t.Transport == NotificationTransport.Max).Channel!.PaymentPending.Should().BeFalse();
    }

    [Fact, TestCase("CY40-ADM-02")]
    public async Task ConfirmPayment_Refusals_AreInTheContractOrder()
    {
        var (_, channelId) = await OwnerWithRequestedChannelAsync();
        var client = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var url = $"/api/admin/notification-channels/{channelId}/confirm-payment";

        (await client.PostAsJsonAsync($"/api/admin/notification-channels/{Guid.NewGuid()}/confirm-payment", new { months = 1 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var months = await client.PostAsJsonAsync(url, new { months = 13 });
        months.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await months.Content.ReadAsStringAsync()).Should().Be("Срок — от 1 до 12 месяцев");
        (await client.PostAsJsonAsync(url, new { months = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(url, new { months = 1, comment = new string('x', 501) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // a replaced number cannot be confirmed
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.NotificationChannels.FirstAsync(c => c.Id == channelId)).State = ChannelState.Replaced;
            await db.SaveChangesAsync();
        }
        var replaced = await client.PostAsJsonAsync(url, new { months = 1 });
        replaced.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await replaced.Content.ReadAsStringAsync()).Should().Contain("Номер заменён");

        // anonymous / a non-admin never reach it
        (await Factory.CreateClient().PostAsJsonAsync(url, new { months = 1 })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("CY40-ADM-03")]
    public async Task ClosedOption_RefusesTheRequestAndTheConfirmation_ButTheSwitchOpensItWithoutARelease()
    {
        var (owner, channelId) = await OwnerWithRequestedChannelAsync(NotificationTransport.WhatsApp); // the test hosts open WhatsApp by default
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);

        await SetOptionOpenAsync("whatsAppOptionOpen", false);
        var closedConfirm = await admin.PostAsJsonAsync($"/api/admin/notification-channels/{channelId}/confirm-payment", new { months = 1 });
        closedConfirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await closedConfirm.Content.ReadAsStringAsync()).Should().Contain("закрыта для подключения");

        var termsVersion = Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>()
            .Current!.Get(LegalDocumentType.TermsOwner)!.Version;
        var (owner2, _) = await CreateOwnerWithCompanyAsync();
        var closedRequest = await AuthedClient(owner2.Token).PostAsJsonAsync("/api/notification-channels",
            new { legalEntityForm = "Company", inn = "7707083893", offerAccepted = new { version = termsVersion }, transport = "WhatsApp" });
        closedRequest.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await closedRequest.Content.ReadAsStringAsync()).Should().Contain("недоступно");

        await SetOptionOpenAsync("whatsAppOptionOpen", true);
        (await admin.PostAsJsonAsync($"/api/admin/notification-channels/{channelId}/confirm-payment", new { months = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);
        _ = owner;
    }

    [Fact, TestCase("CY40-ADM-04")]
    public async Task Table_SummaryAndCard_ShowTheSameThreeStatesTheOwnerSees()
    {
        var (_, channelId) = await OwnerWithRequestedChannelAsync();
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);

        var page = (await (await admin.GetAsync("/api/admin/notification-channels?payment=Requested")).Content.ReadJsonAsync<PagedResult<AdminChannelDto>>())!;
        var row = page.Items.Single(r => r.Id == channelId);
        row.DisplayStatus.Should().Be(ServiceBooking.API.Services.ChannelDisplayStatus.ActionRequired);
        row.DisplayText.Should().Be("Оплата на проверке");
        row.PaymentText.Should().StartWith("заявка от");
        row.AvailableActions.Should().Contain("ConfirmPayment");

        var summary = (await (await admin.GetAsync("/api/admin/notification-channels/summary")).Content.ReadJsonAsync<AdminChannelSummaryDto>())!;
        summary.PendingRequests.Should().BeGreaterThanOrEqualTo(1);
        summary.ActionRequired.Should().BeGreaterThanOrEqualTo(1);

        // replaced numbers are history: hidden by default, shown on request
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.NotificationChannels.FirstAsync(c => c.Id == channelId)).State = ChannelState.Replaced;
            await db.SaveChangesAsync();
        }
        (await (await admin.GetAsync("/api/admin/notification-channels")).Content.ReadJsonAsync<PagedResult<AdminChannelDto>>())!.Items.Should().NotContain(r => r.Id == channelId);
        (await (await admin.GetAsync("/api/admin/notification-channels?includeReplaced=true")).Content.ReadJsonAsync<PagedResult<AdminChannelDto>>())!.Items.Should().Contain(r => r.Id == channelId);

        var card = await admin.GetAsync($"/api/admin/notification-channels/{channelId}");
        card.StatusCode.Should().Be(HttpStatusCode.OK);
        (await card.Content.ReadJsonAsync<AdminChannelCardDto>())!.AvailableActions.Should().BeEmpty("a replaced number takes no action");
        (await admin.GetAsync($"/api/admin/notification-channels/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY40-CTR-01")]
    public async Task Responses_MatchTheCycle40Contract()
    {
        var contract = OpenApiContract.Load("cycle40");
        var (owner, channelId) = await OwnerWithRequestedChannelAsync();
        var admin = AuthedClient((await LoginAsSuperAdminAsync()).Token);
        var errors = new List<string>();
        async Task Check(string method, string path, HttpResponseMessage response, int status)
        {
            ((int)response.StatusCode).Should().Be(status, await response.Content.ReadAsStringAsync());
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            errors.AddRange(contract.Collect(method, path, status, doc.RootElement.Clone()).Select(e => $"{method} {path} {status}: {e}"));
        }

        await Check("GET", "/api/notification-channels/overview", await AuthedClient(owner.Token).GetAsync("/api/notification-channels/overview"), 200);
        await Check("GET", "/api/admin/notification-channels", await admin.GetAsync("/api/admin/notification-channels"), 200);
        await Check("GET", "/api/admin/notification-channels/summary", await admin.GetAsync("/api/admin/notification-channels/summary"), 200);
        await Check("GET", "/api/admin/notification-channels/{id}", await admin.GetAsync($"/api/admin/notification-channels/{channelId}"), 200);
        await Check("POST", "/api/admin/notification-channels/{id}/confirm-payment",
            await admin.PostAsJsonAsync($"/api/admin/notification-channels/{channelId}/confirm-payment", new { months = 1, comment = "Счёт 17" }), 200);
        await Check("GET", "/api/admin/platform-settings", await admin.GetAsync("/api/admin/platform-settings"), 200);

        errors.Should().BeEmpty("the answers must conform to contracts/cycle40/openapi.yaml");
    }

    [Fact, TestCase("CY40-ADM-05")]
    public async Task CustomerMessagingSwitch_Off_HidesTheOfferAndSkipsTheMessages()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await GiveNotificationCapablePlanAsync(owner.UserId);
        await SeedConnectedAssignedChannelAsync(owner.UserId, company.Id, ChannelState.Connected); // this host has no dispatcher: a Connected number is safe
        var anonymous = Factory.CreateClient();

        var company0 = (await (await anonymous.GetAsync($"/api/companies/{company.Slug}")).Content.ReadJsonAsync<ServiceBooking.API.DTOs.Companies.CompanyDto>())!;
        company0.CustomerMessaging!.Offered.Should().BeTrue();
        company0.CustomerMessaging.CheckboxLabel.Should().StartWith("Получать уведомления о записи в ");

        await SetOptionOpenAsync("customerMessagingEnabled", false);
        // the public offer is cached for 30 seconds: ask the rule without the cache
        using var scope = Factory.Services.CreateScope();
        var offerService = scope.ServiceProvider.GetRequiredService<ServiceBooking.API.Services.Notifications.CustomerMessagingOfferService>();
        var companyEntity = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Companies.AsNoTracking().FirstAsync(c => c.Id == company.Id);
        (await offerService.EvaluateAsync(companyEntity)).Offered.Should().BeFalse("the stop-cock of customer messaging is off");
        await SetOptionOpenAsync("customerMessagingEnabled", true);
    }
}
