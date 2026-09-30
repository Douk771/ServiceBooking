using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// QA cycle 28, pass A — US-28-05 (no outgoing message for showcase data), CY28-05. "Гарантия держится пометкой, а не текущей конфигурацией":
/// the company here has a notification-capable plan and a funded, assigned channel — exactly what would send if it were a real company (proved by the
/// control run on a real company) — and only the showcase mark differs.
/// </summary>
public class Cycle28OutboundSuppressionTests(TestDatabaseFixture fixture) : Cycle28ShowcaseTestBase(fixture)
{
    private async Task<(string OwnerToken, string OwnerId, Guid CompanyId, string MasterId, Guid ServiceId, DateOnly Date)> BuildWithChannelAsync()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        // The shared "QA Full Access" plan plus the WhatsApp option rule/funding — a company that DOES send when it is a real one.
        await DbAsync(async db =>
        {
            var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig).FirstAsync(s => s.OwnerUserId == owner.UserId);
            sub.PlanConfig!.AllowNotificationChannel = true;
            await NotificationTestBase.EnsureWhatsAppPlanRuleAsync(db, sub.PlanConfigId!.Value);
            await db.SaveChangesAsync();
            var accountId = (await db.Companies.Where(c => c.Id == company.Id).Select(c => c.BillingAccountId).FirstAsync())!.Value;
            // Disconnected on purpose (see NotificationQueueingTests): the gate does not look at the state, and the real dispatcher never picks the rows up.
            var channel = new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = owner.UserId, BillingAccountId = accountId, State = ChannelState.Disconnected,
                PhoneNumber = "79990009999", ProviderInstanceId = Unique("instance"),
                ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1),
            };
            db.NotificationChannels.Add(channel);
            db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
            {
                Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = company.Id, BillingAccountId = accountId, AssignedByUserId = owner.UserId,
            });
            await db.SaveChangesAsync();
            await NotificationTestBase.EnsureWhatsAppPaidAsync(db, accountId);
        });
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        return (owner.Token, owner.UserId, company.Id, master.UserId, service.Id, date);
    }

    /// <summary>Marks company, account and people as showcase but KEEPS the notification-capable plan: only the mark can be what suppresses.</summary>
    private async Task MarkOnlyAsync(Guid companyId, bool open, params string[] userIds)
    {
        await DbAsync(async db =>
        {
            var company = await db.Companies.FirstAsync(c => c.Id == companyId);
            company.IsShowcase = true;
            company.ShowcaseBookingOpen = open;
            (await db.BillingAccounts.FirstAsync(a => a.Id == company.BillingAccountId)).IsShowcase = true;
            var ids = userIds.Append(company.OwnerUserId).ToList();
            foreach (var user in await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync()) user.IsShowcase = true;
            await db.SaveChangesAsync();
        });
    }

    private async Task<(int Outbound, int Push, int Mail)> QueuesAsync(Guid companyId) => await DbAsync(async db => (
        await db.OutboundNotifications.CountAsync(n => n.CompanyId == companyId),
        await db.StaffPushNotifications.CountAsync(n => n.CompanyId == companyId),
        await db.MailLogs.CountAsync(n => n.CompanyId == companyId)));

    private object Body(Guid companyId, Guid serviceId, string masterId, DateOnly date, string time, string phone) => new
    {
        companyId, serviceId, masterId, date = date.ToString("yyyy-MM-dd"), startTime = time, guestName = "Гость", guestPhone = phone,
    };

    [Fact, TestCase("CY28-05")]
    public async Task ShowcaseCompany_GuestClientAndStaffBookings_CreateRescheduleCancel_LeaveZeroOutgoingRows()
    {
        // Control: the same setup on a REAL company queues messages — otherwise "zero rows" below would prove nothing.
        var control = await BuildWithChannelAsync();
        var client0 = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(client0.Token);
        var controlBooking = await AuthedClient(client0.Token).PostAsJsonAsync("/api/bookings", new
        {
            companyId = control.CompanyId, serviceId = control.ServiceId, masterId = control.MasterId, date = control.Date.ToString("yyyy-MM-dd"), startTime = "10:00:00",
        });
        controlBooking.StatusCode.Should().Be(HttpStatusCode.Created, await controlBooking.Content.ReadAsStringAsync());
        (await QueuesAsync(control.CompanyId)).Outbound.Should().BeGreaterThan(0, "control: a real company with a funded channel does queue confirmation/reminder rows");

        // The showcase.
        var s = await BuildWithChannelAsync();
        await MarkOnlyAsync(s.CompanyId, open: true, s.MasterId);

        var guest = await AnonymousClient().PostAsJsonAsync("/api/bookings", Body(s.CompanyId, s.ServiceId, s.MasterId, s.Date, "10:00:00", UniquePhone()));
        guest.StatusCode.Should().Be(HttpStatusCode.Created, await guest.Content.ReadAsStringAsync());
        var guestId = JsonDocument.Parse(await guest.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var client = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(client.Token);
        var byClient = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings", new
        {
            companyId = s.CompanyId, serviceId = s.ServiceId, masterId = s.MasterId, date = s.Date.ToString("yyyy-MM-dd"), startTime = "12:00:00",
        });
        byClient.StatusCode.Should().Be(HttpStatusCode.Created, await byClient.Content.ReadAsStringAsync());
        var clientBookingId = JsonDocument.Parse(await byClient.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var byStaff = await AuthedClient(s.OwnerToken).PostAsJsonAsync("/api/bookings", Body(s.CompanyId, s.ServiceId, s.MasterId, s.Date, "14:00:00", UniquePhone()));
        byStaff.StatusCode.Should().Be(HttpStatusCode.Created, await byStaff.Content.ReadAsStringAsync());

        // Move and cancel (staff, and the signed-in client cancels their own).
        (await AuthedClient(s.OwnerToken).PatchAsJsonAsync($"/api/bookings/{guestId}/reschedule", new { date = s.Date.ToString("yyyy-MM-dd"), startTime = "16:00:00" }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await AuthedClient(client.Token).PatchAsJsonAsync($"/api/bookings/{clientBookingId}/cancel", "передумал"))
            .StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await AuthedClient(s.OwnerToken).PatchAsJsonAsync($"/api/bookings/{guestId}/cancel", "проверка"))
            .StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var queues = await QueuesAsync(s.CompanyId);
        queues.Should().Be((0, 0, 0), "US-28-05: no OutboundNotifications, StaffPushNotifications or MailLogs row for a showcase company, whatever plan and channel it has");
    }

    [Fact, TestCase("CY28-05B")]
    public async Task ShowcaseCompany_MailingRoute_IsRefused_AndWritesNothing()
    {
        var s = await BuildWithChannelAsync();
        await MarkOnlyAsync(s.CompanyId, open: true, s.MasterId);
        var response = await AuthedClient(s.OwnerToken).PostAsJsonAsync($"/api/companies/{s.CompanyId}/mail", new { subject = "Акция", message = "Скидка 10%" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await QueuesAsync(s.CompanyId)).Should().Be((0, 0, 0));
    }
}

/// <summary>CY28-05D — the safety net of the dispatch task: a row that is already in the queue for a showcase company is skipped with ShowcaseSuppressed, never sent.</summary>
public class Cycle28DispatchSafetyNetTests(TestDatabaseFixture fixture) : IClassFixture<TestDatabaseFixture>
{
    private readonly int _recorded = Record(fixture);

    private static int Record(TestDatabaseFixture f)
    {
        f.RecordTestClass(nameof(Cycle28DispatchSafetyNetTests));
        return 0;
    }

    [Fact, TestCase("CY28-05D")]
    public async Task RowInQueueForShowcaseCompany_IsSkippedWithReason_TransportNeverCalled_RealCompanyRowIsSent()
    {
        await using var factory = new NotificationDispatchTestFactory(fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var showcase = await SeedFundedConnectedChannelAsync(factory, db, showcase: true);
        var real = await SeedFundedConnectedChannelAsync(factory, db, showcase: false);

        const string showcasePhone = "72005550101", realPhone = "79990000201";
        var showcaseRow = Pending(showcase.CompanyId, showcase.ChannelId, showcasePhone);
        var realRow = Pending(real.CompanyId, real.ChannelId, realPhone);
        db.OutboundNotifications.AddRange(showcaseRow, realRow);
        await db.SaveChangesAsync();

        // Control first: the real row is sent, so the runner works and the channel setup is valid.
        await WaitAsync(() => factory.Transport.Calls.Any(c => c.CanonicalPhone == realPhone), 30);
        await WaitAsync(async () =>
        {
            await using var poll = factory.Services.CreateAsyncScope();
            var pdb = poll.ServiceProvider.GetRequiredService<AppDbContext>();
            return (await pdb.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == showcaseRow.Id)).Status != NotificationStatus.Pending;
        }, 30);

        await using var check = factory.Services.CreateAsyncScope();
        var cdb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var after = await cdb.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == showcaseRow.Id);
        after.Status.Should().Be(NotificationStatus.Skipped);
        after.Reason.Should().Be(NotificationReason.ShowcaseSuppressed);
        factory.Transport.Calls.Should().NotContain(c => c.CanonicalPhone == showcasePhone, "nothing may reach the transport for a showcase company");
        (await cdb.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == realRow.Id)).Status.Should().Be(NotificationStatus.Sent);
    }

    private sealed record Seeded(Guid CompanyId, Guid ChannelId);

    private static async Task<Seeded> SeedFundedConnectedChannelAsync(NotificationDispatchTestFactory factory, AppDbContext db, bool showcase)
    {
        var phone = "+79" + string.Concat(Guid.NewGuid().ToString("N").Where(char.IsDigit).Take(9).Concat("000000000")).Substring(0, 9);
        var provider = factory.Services.GetRequiredService<ServiceBooking.API.Services.Legal.LegalDocumentProvider>().Current!;
        var register = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterDto("Test", "Owner", phone, "Password123!", null,
            new RegisterLegalDto(provider.Get(LegalDocumentType.Privacy)!.Version, provider.Get(LegalDocumentType.TermsClient)!.Version)));
        register.EnsureSuccessStatusCode();
        var auth = (await register.Content.ReadFromJsonAsync<AuthResponseDto>())!;

        var plan = new SubscriptionPlanConfig { Id = Guid.NewGuid(), Name = "Plan " + Guid.NewGuid().ToString("N")[..8], AllowNotificationChannel = true, IsActive = true };
        var account = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = auth.UserId, IsShowcase = showcase };
        var company = new Company
        {
            Id = Guid.NewGuid(), Name = "Co " + Guid.NewGuid().ToString("N")[..8], Slug = "co-" + Guid.NewGuid().ToString("N")[..10], OwnerUserId = auth.UserId,
            BillingAccountId = account.Id, TimeZoneId = "Europe/Moscow", IsShowcase = showcase, ShowcaseBookingOpen = showcase,
        };
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = auth.UserId, BillingAccountId = account.Id, State = ChannelState.Connected,
            PhoneNumber = "79990000000", ProviderInstanceId = "instance-" + Guid.NewGuid().ToString("N")[..8], ConnectedAtUtc = DateTime.UtcNow.AddDays(-1),
        };
        channel.ProviderSecretCiphertext = SecretProtector.Encrypt("test-provider-token", NotificationDispatchTestFactory.TestEncryptionKeyBase64, channel.Id);
        db.SubscriptionPlanConfigs.Add(plan);
        db.BillingAccounts.Add(account);
        db.AccountSubscriptions.Add(new AccountSubscription
        {
            Id = Guid.NewGuid(), OwnerUserId = auth.UserId, PlanConfigId = plan.Id, BillingAccountId = account.Id, IsActive = true, PaidUntil = DateTime.UtcNow.AddDays(30),
        });
        db.Companies.Add(company);
        db.NotificationChannels.Add(channel);
        db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = company.Id, BillingAccountId = account.Id, AssignedByUserId = auth.UserId,
        });
        await NotificationTestBase.EnsureWhatsAppPlanRuleAsync(db, plan.Id);
        await db.SaveChangesAsync();
        await NotificationTestBase.EnsureWhatsAppPaidAsync(db, account.Id);
        return new Seeded(company.Id, channel.Id);
    }

    private static OutboundNotification Pending(Guid companyId, Guid channelId, string phone)
    {
        var id = Guid.NewGuid();
        return new OutboundNotification
        {
            Id = id, CompanyId = companyId, ChannelId = channelId, Type = NotificationType.BookingConfirmed, RecipientPhone = phone, Body = "Тест",
            DueAtUtc = DateTime.UtcNow.AddMinutes(-1), VisitStartUtc = DateTime.UtcNow.AddHours(2), Status = NotificationStatus.Pending, IdempotencyKey = $"cy28:{id}",
        };
    }

    private static async Task WaitAsync(Func<bool> predicate, int seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until) { if (predicate()) return; await Task.Delay(200); }
        predicate().Should().BeTrue($"not true within {seconds}s");
    }

    private static async Task WaitAsync(Func<Task<bool>> predicate, int seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until) { if (await predicate()) return; await Task.Delay(200); }
        (await predicate()).Should().BeTrue($"not true within {seconds}s");
    }
}
