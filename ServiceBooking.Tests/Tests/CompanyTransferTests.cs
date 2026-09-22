using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 7 (SPEC.md US-77, ARCHITECTURE_CYCLE7.md §51) — moving a company between billing accounts,
/// with or without changing its responsible owner in the same transaction. Written from SPEC.md/
/// ARCHITECTURE_CYCLE7.md §51, independently of CompanyTransferController's/CompanyTransferService's own
/// implementation, per this cycle's QA brief ("Вызов 2"). Developers proposed these scenarios but did
/// not write them (backend report) — this is that missing coverage.
/// </summary>
public class CompanyTransferTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── Combined transfer + owner change (§51.3) ─────────────────────────────────────────────────

    [Fact, TestCase("TRF-001")]
    public async Task Transfer_WithNewOwner_WritesTwoSubscriptionLogRows_OneOwnerLogRow_AndDetachesChannel()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (sourceOwner, company) = await CreateOwnerWithCompanyAsync(); // paid, unlimited "QA Full Access"
        var (targetOwner, _) = await CreateOwnerWithCompanyAsync(); // also paid/unlimited — no limits in play here

        var (sourceAccountId, targetAccountId) = await GetAccountIdsAsync(company.Id, targetOwner.UserId);

        // Connect the source company to a number so the transfer's channel-detach step has something
        // real to detach (§51.3 step 6).
        await ConnectChannelAndQueuePendingAsync(company.Id, sourceOwner.UserId, sourceAccountId);

        var response = await AuthedClient(admin.Token).PostAsJsonAsync(
            $"/api/admin/companies/{company.Id}/transfer",
            new CompanyTransferInput(targetAccountId, targetOwner.UserId)); // holder of B — trivially "linked" (§51.1)

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reloadedCompany = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == company.Id);
        reloadedCompany.BillingAccountId.Should().Be(targetAccountId);
        reloadedCompany.OwnerUserId.Should().Be(targetOwner.UserId);

        // Two SubscriptionChangeLog rows — one in each account's history (§51.3 step 10).
        var transferLogs = await db.SubscriptionChangeLogs
            .Where(l => l.CompanyId == company.Id && l.ChangeKind == SubscriptionChangeKind.CompanyTransferred)
            .ToListAsync();
        transferLogs.Should().HaveCount(2);
        transferLogs.Select(l => l.BillingAccountId).Should().BeEquivalentTo([sourceAccountId, targetAccountId]);

        // Exactly one CompanyOwnerChangeLog row, WithTransfer = true (§51.3 step 11).
        var ownerLogs = await db.CompanyOwnerChangeLogs.Where(l => l.CompanyId == company.Id).ToListAsync();
        ownerLogs.Should().ContainSingle();
        ownerLogs[0].WithTransfer.Should().BeTrue();
        ownerLogs[0].NewOwnerUserId.Should().Be(targetOwner.UserId);

        // The number stays with the SOURCE account; the company loses its assignment.
        (await db.ChannelCompanyAssignments.AnyAsync(a => a.CompanyId == company.Id)).Should().BeFalse(
            "a company leaving the account can no longer be served by that account's number (§43.6, §47.4)");
        (await db.OutboundNotifications.Where(n => n.CompanyId == company.Id).AllAsync(n => n.Status == NotificationStatus.Cancelled))
            .Should().BeTrue();
    }

    // ── Company-limit refusal (§51.2, primary US-77 acceptance criterion) ───────────────────────────

    [Fact, TestCase("TRF-002")]
    public async Task Transfer_TargetAtCompanyLimit_Returns402_AndNothingChanges()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (sourceOwner, company) = await CreateOwnerWithCompanyAsync();
        // Free plan: AccountMaxCompanies = 1 (SubscriptionResolver.Free). A brand-new owner's first
        // company already fills that limit.
        var (targetOwner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var (sourceAccountId, targetAccountId) = await GetAccountIdsAsync(company.Id, targetOwner.UserId);

        int logsBefore;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            logsBefore = await db.SubscriptionChangeLogs.CountAsync();
        }

        var response = await AuthedClient(admin.Token).PostAsJsonAsync(
            $"/api/admin/companies/{company.Id}/transfer", new CompanyTransferInput(targetAccountId, null));

        ((int)response.StatusCode).Should().Be(402);
        (await response.Content.ReadAsStringAsync()).Should().NotBeNullOrEmpty();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reloadedCompany = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == company.Id);
            reloadedCompany.BillingAccountId.Should().Be(sourceAccountId, "a rejected transfer must not move the company at all");
            (await db.SubscriptionChangeLogs.CountAsync()).Should().Be(logsBefore, "a 402 refusal must write nothing");
        }
    }

    // ── Seat-overflow confirmation gate (§51.2) ──────────────────────────────────────────────────

    [Fact, TestCase("TRF-003")]
    public async Task Transfer_SeatOverflow_Requires409WithoutConfirmation_ThenSucceedsWithIt()
    {
        var admin = await LoginAsSuperAdminAsync();
        // Source company has exactly one seat (the owner, no extra staff).
        var (sourceOwner, company) = await CreateOwnerWithCompanyAsync();

        // Target: unlimited companies, but only 1 employee seat total — and already at that limit
        // through its own first company (the owner). Adding a 1-seat company tips it over.
        var (targetOwner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var (sourceAccountId, targetAccountId) = await GetAccountIdsAsync(company.Id, targetOwner.UserId);
        await SetPlanForAccountAsync(targetAccountId, maxEmployees: 1, maxCompanies: 5);

        int logsBefore;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            logsBefore = await db.SubscriptionChangeLogs.CountAsync();
        }

        var refused = await AuthedClient(admin.Token).PostAsJsonAsync(
            $"/api/admin/companies/{company.Id}/transfer", new CompanyTransferInput(targetAccountId, null, ConfirmSeatOverflow: false));
        ((int)refused.StatusCode).Should().Be(409);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reloadedCompany = await db.Companies.AsNoTracking().FirstAsync(c => c.Id == company.Id);
            reloadedCompany.BillingAccountId.Should().Be(sourceAccountId, "a 409 refusal must not move the company");
            (await db.SubscriptionChangeLogs.CountAsync()).Should().Be(logsBefore, "a 409 refusal must write nothing");
        }

        var confirmed = await AuthedClient(admin.Token).PostAsJsonAsync(
            $"/api/admin/companies/{company.Id}/transfer", new CompanyTransferInput(targetAccountId, null, ConfirmSeatOverflow: true));
        confirmed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Companies.AsNoTracking().FirstAsync(c => c.Id == company.Id)).BillingAccountId.Should().Be(targetAccountId);
        }
    }

    // ── Preview must be told about the prospective new owner to be truthful (§51.2, risk A8) ───────

    [Fact, TestCase("TRF-004")]
    public async Task Preview_WithAndWithoutNewOwner_DisagreeOnSeatOverflow()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (sourceOwner, company) = await CreateOwnerWithCompanyAsync(); // 1 seat: the owner alone

        // A target account with zero companies of its own yet, and a tight 1-seat limit.
        var targetOwner = await RegisterAsync();
        var targetAccountId = await CreateBareBillingAccountAsync(targetOwner.UserId);
        await SetPlanForAccountAsync(targetAccountId, maxEmployees: 1, maxCompanies: 5);

        var withoutOwner = await (await AuthedClient(admin.Token).GetAsync(
            $"/api/admin/companies/{company.Id}/transfer/preview?targetBillingAccountId={targetAccountId}"))
            .Content.ReadFromJsonAsync<CompanyTransferPreviewDto>();
        withoutOwner!.SeatOverflow.Should().BeFalse("0 (target seats) + 1 (company) = 1, exactly at the 1-seat limit");

        // Same transfer, but naming the target's OWN holder as the new responsible owner — legitimate
        // per §51.1 (he's the account's holder), and not yet a member of the transferred company, so
        // he would occupy one MORE seat than the no-owner preview accounted for.
        var withOwner = await (await AuthedClient(admin.Token).GetAsync(
            $"/api/admin/companies/{company.Id}/transfer/preview?targetBillingAccountId={targetAccountId}&newOwnerUserId={targetOwner.UserId}"))
            .Content.ReadFromJsonAsync<CompanyTransferPreviewDto>();
        withOwner!.SeatOverflow.Should().BeTrue(
            "risk A8: the new owner isn't yet a member of the transferred company and would occupy an extra seat — " +
            "a preview that ignores newOwnerUserId would falsely say this transfer is safe");
    }

    // ── Company with an active channel assignment (§43.6 composite FK) ──────────────────────────────

    [Fact, TestCase("TRF-005")]
    public async Task Transfer_CompanyWithActiveChannelAssignment_Succeeds_AndDetachesTheNumber()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (sourceOwner, company) = await CreateOwnerWithCompanyAsync();
        var (targetOwner, _) = await CreateOwnerWithCompanyAsync();
        var (_, targetAccountId) = await GetAccountIdsAsync(company.Id, targetOwner.UserId);

        var (_, sourceAccountIdForChannel) = await GetAccountIdsAsync(company.Id, sourceOwner.UserId);
        var channelId = await ConnectChannelAndQueuePendingAsync(company.Id, sourceOwner.UserId, sourceAccountIdForChannel);

        var response = await AuthedClient(admin.Token).PostAsJsonAsync(
            $"/api/admin/companies/{company.Id}/transfer", new CompanyTransferInput(targetAccountId, null));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ChannelCompanyAssignments.AnyAsync(a => a.CompanyId == company.Id)).Should().BeFalse(
            "the composite FK (§43.6) makes it physically impossible to keep the company on a number belonging to another account");
        // The channel itself still exists (it belongs to the SOURCE account, unaffected) — only the
        // assignment to this particular company is gone.
        (await db.NotificationChannels.AnyAsync(c => c.Id == channelId)).Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private async Task<(Guid Source, Guid Target)> GetAccountIdsAsync(Guid sourceCompanyId, string targetOwnerUserId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sourceAccountId = (await db.Companies.Where(c => c.Id == sourceCompanyId).Select(c => c.BillingAccountId).FirstAsync())!.Value;
        var targetAccountId = (await db.BillingAccounts.Where(a => a.OwnerUserId == targetOwnerUserId).Select(a => a.Id).FirstAsync());
        return (sourceAccountId, targetAccountId);
    }

    private async Task<Guid> CreateBareBillingAccountAsync(string ownerUserId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId);
        if (existing is not null) return existing.Id;
        var account = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = ownerUserId };
        db.BillingAccounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task SetPlanForAccountAsync(Guid billingAccountId, int? maxEmployees, int? maxCompanies)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var planConfig = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(), Name = Unique("TRF Plan "),
            PricePerMonth = 100, MaxEmployees = maxEmployees, MaxCompanies = maxCompanies,
            AllowOnlineBooking = true, AllowMailing = true, AllowAnalytics = true,
            AllowPublicListing = true, AllowOnlinePayment = true, IsActive = true, CreatedAt = DateTime.UtcNow,
        };
        db.SubscriptionPlanConfigs.Add(planConfig);

        var sub = await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == billingAccountId);
        if (sub is null)
        {
            var ownerUserId = await db.BillingAccounts.Where(a => a.Id == billingAccountId).Select(a => a.OwnerUserId).FirstAsync();
            db.AccountSubscriptions.Add(new AccountSubscription
            {
                Id = Guid.NewGuid(), OwnerUserId = ownerUserId, BillingAccountId = billingAccountId,
                PlanConfigId = planConfig.Id, PaidUntil = DateTime.UtcNow.AddMonths(1), IsActive = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            sub.PlanConfigId = planConfig.Id;
            sub.PaidUntil = DateTime.UtcNow.AddMonths(1);
            sub.IsActive = true;
            sub.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    private async Task<Guid> ConnectChannelAndQueuePendingAsync(Guid companyId, string ownerUserId, Guid billingAccountId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerUserId, BillingAccountId = billingAccountId,
            State = ChannelState.Connected, PhoneNumber = "79990001199",
        };
        db.NotificationChannels.Add(channel);
        db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = companyId,
            BillingAccountId = billingAccountId, AssignedByUserId = ownerUserId,
        });
        db.OutboundNotifications.Add(new OutboundNotification
        {
            Id = Guid.NewGuid(), CompanyId = companyId, ChannelId = channel.Id,
            Type = NotificationType.BookingConfirmed, RecipientPhone = "79990001199", Body = "Test",
            DueAtUtc = DateTime.UtcNow.AddMinutes(-1), VisitStartUtc = DateTime.UtcNow.AddHours(2),
            Status = NotificationStatus.Pending, IdempotencyKey = $"test:{Guid.NewGuid()}",
        });
        await db.SaveChangesAsync();
        return channel.Id;
    }
}
