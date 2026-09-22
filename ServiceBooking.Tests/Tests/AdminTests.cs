using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class AdminTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── Authorization boilerplate (checked explicitly for stats, roles, plans) ─────────────

    [Fact, TestCase("ADM-001")]
    public async Task GetStats_AsSuperAdmin_ReturnsOk()
    {
        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/stats");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("ADM-002")]
    public async Task GetStats_AsNonSuperAdmin_ReturnsForbidden()
    {
        var user = await RegisterAsync();
        var response = await AuthedClient(user.Token).GetAsync("/api/admin/stats");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ADM-003")]
    public async Task GetStats_Anonymous_ReturnsUnauthorized()
    {
        var response = await AnonymousClient().GetAsync("/api/admin/stats");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("ADM-004")]
    public async Task UpdateRoles_AsNonSuperAdmin_ReturnsForbidden()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var target = await RegisterAsync();
        var response = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/admin/users/{target.UserId}/roles", new List<string> { "Master" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ADM-005")]
    public async Task UpdateRoles_Anonymous_ReturnsUnauthorized()
    {
        var response = await AnonymousClient().PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/roles", new List<string> { "Master" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("ADM-006")]
    public async Task CreatePlan_AsNonSuperAdmin_ReturnsForbidden()
    {
        var user = await RegisterAsync();
        var plan = NewPlanConfig();
        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/admin/plans", plan);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ADM-007")]
    public async Task CreatePlan_Anonymous_ReturnsUnauthorized()
    {
        var plan = NewPlanConfig();
        var response = await AnonymousClient().PostAsJsonAsync("/api/admin/plans", plan);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Stats ────────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("ADM-008")]
    public async Task GetStats_ReflectsNewlyCreatedCompanyAndBooking()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);

        var before = await (await adminClient.GetAsync("/api/admin/stats")).Content.ReadFromJsonAsync<AdminStatsDto>();

        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 1000);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await (await adminClient.GetAsync("/api/admin/stats")).Content.ReadFromJsonAsync<AdminStatsDto>();

        after!.TotalCompanies.Should().BeGreaterThan(before!.TotalCompanies);
        after.TotalBookings.Should().BeGreaterThan(before.TotalBookings);
    }

    // ── Users ────────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("ADM-009")]
    public async Task GetUsers_WithSearchByEmailSubstring_ReturnsOnlyMatchingUser()
    {
        var admin = await LoginAsSuperAdminAsync();
        var uniqueTag = Unique("searchtag");
        var email = UniqueEmail(uniqueTag);
        var user = await RegisterAsync(email: email);

        // Admin search matches email (as well as phone/name); the tag lives only in this user's email.
        var response = await AuthedClient(admin.Token).GetAsync($"/api/admin/users?search={uniqueTag}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var usersPage = await response.Content.ReadFromJsonAsync<PagedResult<AdminUserDto>>();
        var users = usersPage?.Items;

        users.Should().ContainSingle(u => u.Id == user.UserId);
        users!.Should().OnlyContain(u => u.Email != null && u.Email.Contains(uniqueTag));
    }

    [Fact, TestCase("ADM-010")]
    public async Task GetUsers_WithSearchByFirstName_ReturnsMatchingUser()
    {
        var admin = await LoginAsSuperAdminAsync();
        var firstName = Unique("Firstname");
        var user = await RegisterAsync(firstName: firstName);

        var response = await AuthedClient(admin.Token).GetAsync($"/api/admin/users?search={firstName}");
        var usersPage = await response.Content.ReadFromJsonAsync<PagedResult<AdminUserDto>>();
        var users = usersPage?.Items;

        users.Should().Contain(u => u.Id == user.UserId);
    }

    [Fact, TestCase("ADM-044")]
    public async Task GetUsers_SearchOfNonAsciiDigitsOnly_DoesNotMatchEveryUser()
    {
        // Arabic-Indic digits (٠١٢٣٤) satisfy the "looks like a phone" heuristic in AdminController.GetUsers
        // (>= 5 Unicode digits, no letters — char.IsDigit is Unicode-aware) but PhoneNormalizer.Normalize
        // only keeps ASCII 0-9, so a naive implementation normalizes this search to "". Without the
        // `phoneSearch.Length > 0` guard, `PhoneNumber.Contains("")` is true for every row and the search
        // silently returns every user regardless of what was typed (code review finding, data leak).
        var admin = await LoginAsSuperAdminAsync();
        var uniqueTag = Unique("nonasciiguard");
        var controlUser = await RegisterAsync(firstName: uniqueTag);

        var response = await AuthedClient(admin.Token).GetAsync($"/api/admin/users?search={Uri.EscapeDataString("٠١٢٣٤")}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var usersPage = await response.Content.ReadFromJsonAsync<PagedResult<AdminUserDto>>();
        var users = usersPage?.Items;

        // The control user's name/email/phone contain no Arabic-Indic digits — if the search silently
        // fell back to matching everyone, it would show up here anyway.
        users.Should().NotContain(u => u.Id == controlUser.UserId);
    }

    [Fact, TestCase("ADM-011")]
    public async Task UpdateRoles_AsSuperAdmin_ReplacesOldRolesWithNewOnes()
    {
        var admin = await LoginAsSuperAdminAsync();
        var user = await RegisterAsync(); // starts with only "Client" role

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/users/{user.UserId}/roles", new List<string> { "Master" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A fresh login always reflects the current roles.
        var relogged = await LoginAsync(user.Phone, "Password123!");
        relogged.Roles.Should().ContainSingle().Which.Should().Be("Master");
        relogged.Roles.Should().NotContain("Client");
    }

    [Fact, TestCase("ADM-025")]
    public async Task UpdateRoles_GrantedRole_UsableImmediately_WithoutRelogin()
    {
        var admin = await LoginAsSuperAdminAsync();
        var user = await RegisterAsync(); // starts with only "Client" role, token issued now

        // The role-gated endpoint rejects the original "Client"-only token.
        var before = await AuthedClient(user.Token).GetAsync("/api/bookings/master");
        before.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var update = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/users/{user.UserId}/roles", new List<string> { "Master" });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The SAME original token (never re-issued) now passes the Master-only check, because roles
        // are re-read from the database on every request instead of trusting the JWT's baked-in claims.
        var after = await AuthedClient(user.Token).GetAsync("/api/bookings/master");
        after.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("ADM-026")]
    public async Task UpdateRoles_RevokedRole_LosesAccessImmediately_WithoutRelogin()
    {
        var admin = await LoginAsSuperAdminAsync();
        var user = await RegisterAsync();

        var grant = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/users/{user.UserId}/roles", new List<string> { "Master" });
        grant.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var before = await AuthedClient(user.Token).GetAsync("/api/bookings/master");
        before.StatusCode.Should().Be(HttpStatusCode.OK);

        var revoke = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/users/{user.UserId}/roles", new List<string> { "Client" });
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Same token as before — access to the Master-only endpoint is gone without re-login, closing
        // the security gap where a revoked/demoted user (e.g. an ex-SuperAdmin) kept elevated access
        // for as long as their existing token remained valid (up to 7 days).
        var after = await AuthedClient(user.Token).GetAsync("/api/bookings/master");
        after.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ADM-012")]
    public async Task UpdateRoles_UnknownUserId_ReturnsNotFound()
    {
        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/users/{Guid.NewGuid()}/roles", new List<string> { "Master" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("ADM-013")]
    public async Task UpdateRoles_WithUnknownRoleName_ReturnsBadRequest_NotUnhandledException()
    {
        var admin = await LoginAsSuperAdminAsync();
        var user = await RegisterAsync(); // starts with only "Client" role

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/users/{user.UserId}/roles", new List<string> { "TotallyBogusRoleXYZ" });

        // Identity's UserStore.AddToRoleAsync throws InvalidOperationException for an unrecognized
        // role name rather than returning a failed IdentityResult — the controller now validates
        // every role exists up front (RoleManager.RoleExistsAsync) before touching anything, so this
        // is a clean 400 instead of an unhandled 500.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("ADM-014")]
    public async Task UpdateRoles_WithUnknownRoleName_LeavesExistingRolesUntouched()
    {
        var admin = await LoginAsSuperAdminAsync();
        var user = await RegisterAsync(); // starts with only "Client" role

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/users/{user.UserId}/roles", new List<string> { "TotallyBogusRoleXYZ" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Before the fix, the old roles were removed BEFORE the unknown-role AddToRolesAsync call
        // threw — so a failed request could silently strip the user down to zero roles. Verify the
        // "Client" role survives an attempted (and rejected) update.
        var relogged = await LoginAsync(user.Phone, "Password123!");
        relogged.Roles.Should().ContainSingle().Which.Should().Be("Client");
    }

    [Fact, TestCase("ADM-015")]
    public async Task UpdateRoles_WithMixOfValidAndUnknownRoles_RejectsWholeRequest()
    {
        var admin = await LoginAsSuperAdminAsync();
        var user = await RegisterAsync();

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/users/{user.UserId}/roles", new List<string> { "Master", "TotallyBogusRoleXYZ" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var relogged = await LoginAsync(user.Phone, "Password123!");
        relogged.Roles.Should().ContainSingle().Which.Should().Be("Client");
    }

    // ── Companies ────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("ADM-016")]
    public async Task GetCompanies_WithSearch_PopulatesMemberBookingAndPlanFields()
    {
        var admin = await LoginAsSuperAdminAsync();
        var slugTag = Unique("admco");
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        // Give the company a distinctive, searchable name.
        var companyName = $"AdminSearchCo-{slugTag}";
        await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/companies/{company.Id}",
            new AdminUpdateCompanyDto(companyName, true, true));

        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 500);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();
        await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));

        await SetSubscriptionAsync(company.Id);

        var response = await AuthedClient(admin.Token).GetAsync($"/api/admin/companies?search={slugTag}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var companiesPage = await response.Content.ReadJsonAsync<PagedResult<AdminCompanyDto>>();
        var companies = companiesPage?.Items;

        var found = companies.Should().ContainSingle(c => c.Id == company.Id).Subject;
        found.MemberCount.Should().Be(2); // owner + master
        found.BookingCount.Should().Be(1);
        found.PlanName.Should().Be("QA Full Access");
    }

    // ADM-017 ("UpdateSubscription_CalledTwice_UpdatesExistingRowRatherThanDuplicating") tested
    // AdminController.UpdateSubscription's own upsert behavior via the now-retired
    // PUT /api/admin/owners/{ownerUserId}/subscription — see the comment above ADM-022 for why it was
    // removed rather than rewritten.

    [Fact, TestCase("ADM-048")]
    public async Task LegacyOwnerSubscriptionEndpoint_ReturnsGoneWithReplacementRoute()
    {
        // openapi-cycle5.yaml (legacyAssignOwnerSubscription, redaction 2.1): this route is retired in
        // favor of PUT /admin/billing-accounts/{accountId}/subscription and must answer 410 Gone,
        // unconditionally, without touching the body — regression coverage for AdminController's
        // LegacyEndpointGone so a future change can't silently resurrect the old write behavior.
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);

        var response = await adminClient.PutAsJsonAsync($"/api/admin/owners/{Guid.NewGuid()}/subscription",
            new { planConfigId = (Guid?)null, paidUntil = (DateTime?)null, isActive = true, comment = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("/admin/billing-accounts/{accountId}/subscription");
    }

    [Fact, TestCase("ADM-049")]
    public async Task LegacyNotificationChannelPaymentEndpoint_ReturnsGoneWithReplacementRoute()
    {
        // Same retirement (openapi-cycle5.yaml, legacyChannelPayment) on the notification-channel side.
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);

        var response = await adminClient.PostAsJsonAsync($"/api/admin/notification-channels/{Guid.NewGuid()}/payment",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("/admin/billing-accounts/{accountId}/subscription");
    }

    [Fact, TestCase("ADM-018")]
    public async Task UpdateCompany_DeactivatingCompany_HidesItFromPublicCompanyList()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var publicListBefore = await AnonymousClient().GetAsync("/api/companies");
        var before = await publicListBefore.Content.ReadFromJsonAsync<List<CompanyDto>>();
        before.Should().Contain(c => c.Id == company.Id);

        var response = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/companies/{company.Id}",
            new AdminUpdateCompanyDto(company.Name, false, true));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var publicListAfter = await AnonymousClient().GetAsync("/api/companies");
        var after = await publicListAfter.Content.ReadFromJsonAsync<List<CompanyDto>>();
        after.Should().NotContain(c => c.Id == company.Id);
    }

    // ── PUT /api/admin/companies/{id}/owner ─────────────────────────────────────────────────

    [Fact, TestCase("ADM-027")]
    public async Task UpdateCompanyOwner_AsSuperAdmin_ReassignsOwnerAndGrantsManagementAccess()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var newOwner = await RegisterAsync(); // starts as a plain Client, not yet a member of this company

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/companies/{company.Id}/owner", new UpdateCompanyOwnerDto(newOwner.UserId));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var companiesPage = await (await AuthedClient(admin.Token).GetAsync($"/api/admin/companies?search={company.Slug}"))
            .Content.ReadFromJsonAsync<PagedResult<AdminCompanyDto>>();
            var companies = companiesPage?.Items;
        companies.Should().ContainSingle(c => c.Id == company.Id && c.OwnerUserId == newOwner.UserId);

        // Reassigning OwnerUserId alone wouldn't let the new owner actually manage the company (that's
        // gated by CompanyMembers, not Company.OwnerUserId) — confirm they were also made a member.
        var reloggedNewOwner = await LoginAsync(newOwner.Phone, "Password123!");
        var updateResponse = await AuthedClient(reloggedNewOwner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}", new { name = "Renamed by new owner" });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("ADM-028")]
    public async Task UpdateCompanyOwner_AsNonSuperAdmin_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var newOwner = await RegisterAsync();

        var response = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/admin/companies/{company.Id}/owner", new UpdateCompanyOwnerDto(newOwner.UserId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ADM-029")]
    public async Task UpdateCompanyOwner_UnknownTargetUserId_ReturnsBadRequest()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (_, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/companies/{company.Id}/owner", new UpdateCompanyOwnerDto(Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("ADM-030")]
    public async Task UpdateCompanyOwner_UnknownCompanyId_ReturnsNotFound()
    {
        var admin = await LoginAsSuperAdminAsync();
        var newOwner = await RegisterAsync();

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/companies/{Guid.NewGuid()}/owner", new UpdateCompanyOwnerDto(newOwner.UserId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Code review finding: a deleted account is a tombstone row (DeletedAtUtc set, no password, no
    // way to ever log in again — US-39/ARCHITECTURE.md §7.4), not a real candidate for ownership.
    // Before the fix, UpdateCompanyOwner accepted any existing user id, including a tombstone's,
    // handing a company to an owner who can never authenticate to manage it.
    [Fact, TestCase("ADM-039")]
    public async Task UpdateCompanyOwner_TargetIsADeletedAccount_ReturnsBadRequest()
    {
        var admin = await LoginAsSuperAdminAsync();
        var (_, company) = await CreateOwnerWithCompanyAsync();

        var deletedUser = await RegisterAsync();
        var deleteResponse = await AuthedClient(deletedUser.Token).PostAsJsonAsync("/api/profile/delete-account",
            new { currentPassword = "Password123!" });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/companies/{company.Id}/owner", new UpdateCompanyOwnerDto(deletedUser.UserId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a tombstoned account must never be assignable as a company owner");
    }

    // Cycle 5 (SPEC.md US-64, ARCHITECTURE_CYCLE5.md §50) DELIBERATELY REVERSES this behavior: the
    // tariff is now resolved through the company's BillingAccountId, not through Company.OwnerUserId,
    // so a stand-alone change of the responsible person must NOT move any money at all. Written from
    // SPEC.md US-64 independently of AdminController's implementation — replaces the pre-cycle-5 test
    // above, which asserted the opposite (now-obsolete) behavior and would be red against today's
    // intentional product change.
    [Fact, TestCase("ADM-031")]
    public async Task UpdateCompanyOwner_StandAlone_DoesNotTouchBillingOrChannelOrSubscriptionLog()
    {
        var admin = await LoginAsSuperAdminAsync();
        // Paid tariff with options connected — attachPlan: true gives the account "QA Full Access"
        // (paid, unlimited, every feature on).
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        Guid billingAccountId;
        Guid pendingNotificationId;
        int subscriptionChangeLogCountBefore;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
            billingAccountId = (await db.Companies.Where(c => c.Id == company.Id)
                .Select(c => c.BillingAccountId).FirstAsync())!.Value;

            // Connect a paid option to the account so "с подключёнными опциями" is not vacuously true.
            var option = new SubscriptionOption
            {
                Id = Guid.NewGuid(), Code = Unique("opt-"), Name = "Доп. сотрудники",
                Kind = OptionKind.Quantity, PricePerMonth = 100, UnitName = "сотрудник",
                IsActive = true, IsPublic = true,
            };
            db.SubscriptionOptions.Add(option);
            db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
            {
                Id = Guid.NewGuid(), BillingAccountId = billingAccountId, OptionId = option.Id,
                Quantity = 3, ActivatedAtUtc = DateTime.UtcNow,
            });

            // Connect the company to a number and queue a pending message (US-64 p.3: both must
            // survive a stand-alone owner change, unlike cycle 4's now-reversed behavior).
            var channel = new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = owner.UserId, BillingAccountId = billingAccountId,
                State = ChannelState.Connected, PhoneNumber = "79990001122",
            };
            db.NotificationChannels.Add(channel);
            db.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
            {
                Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = company.Id,
                BillingAccountId = billingAccountId, AssignedByUserId = owner.UserId,
            });
            var pending = new OutboundNotification
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, ChannelId = channel.Id,
                Type = NotificationType.BookingConfirmed, RecipientPhone = "79990001122", Body = "Test",
                DueAtUtc = DateTime.UtcNow.AddMinutes(-1), VisitStartUtc = DateTime.UtcNow.AddHours(2),
                Status = NotificationStatus.Pending, IdempotencyKey = $"test:{Guid.NewGuid()}",
            };
            db.OutboundNotifications.Add(pending);
            pendingNotificationId = pending.Id;
            await db.SaveChangesAsync();

            subscriptionChangeLogCountBefore = await db.SubscriptionChangeLogs.CountAsync();
        }

        var before = (await (await AuthedClient(owner.Token).GetAsync("/api/companies/my"))
            .Content.ReadFromJsonAsync<List<CompanyDto>>())!.Single(c => c.Id == company.Id);

        var newOwner = await RegisterAsync(); // no subscription of their own — would be Free alone

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/companies/{company.Id}/owner", new UpdateCompanyOwnerDto(newOwner.UserId));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Tariff/paidUntil/subscriptionActive on the admin projection are unchanged — still the SAME
        // billing account's plan, not the new owner's (who has none of their own).
        var companiesPage = await (await AuthedClient(admin.Token).GetAsync($"/api/admin/companies?search={company.Slug}"))
            .Content.ReadFromJsonAsync<PagedResult<AdminCompanyDto>>();
        var entry = companiesPage!.Items.Should().ContainSingle(c => c.Id == company.Id).Subject;
        entry.OwnerUserId.Should().Be(newOwner.UserId);
        entry.PlanName.Should().Be("QA Full Access", "money is not supposed to move on a stand-alone owner change (US-64)");
        entry.SubscriptionActive.Should().BeTrue();
        entry.PaidUntil.Should().NotBeNull();

        // All capability flags/maxEmployees on the owner-facing CompanyDto are unchanged — fetched
        // through the NEW owner, since the old one is demoted off the CompanyOwner role. EmployeeCount/
        // AccountSeatsUsed/CanAddEmployee are excluded because gaining a member is an expected,
        // unrelated side effect of ANY owner change, not something US-64 makes any promise about.
        var after = (await (await AuthedClient(newOwner.Token).GetAsync("/api/companies/my"))
            .Content.ReadFromJsonAsync<List<CompanyDto>>())!.Single(c => c.Id == company.Id);
        after.Should().BeEquivalentTo(before, opts => opts
            .Excluding(c => c.EmployeeCount)
            .Excluding(c => c.AccountSeatsUsed)
            .Excluding(c => c.CanAddEmployee));

        // The new owner does not get 402'd on a scenario that worked for the old one — feature access
        // is governed by the (unchanged) billing account, not by the new owner personally.
        var reportsResponse = await AuthedClient(newOwner.Token).GetAsync($"/api/reports/masters?companyId={company.Id}");
        ((int)reportsResponse.StatusCode).Should().NotBe(402, "capabilities follow the billing account, not whoever manages it personally");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();

            // The channel assignment survives, and the pending message stays pending (US-64 p.3 —
            // a deliberate reversal of cycle 4's behavior of unassigning on owner change).
            (await db.ChannelCompanyAssignments.AnyAsync(a => a.CompanyId == company.Id)).Should().BeTrue(
                "the number belongs to the billing account, not to whoever manages the company");
            var pendingRow = await db.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == pendingNotificationId);
            pendingRow.Status.Should().Be(NotificationStatus.Pending);

            // No money moved: not a single new SubscriptionChangeLog row (US-64 p.4).
            (await db.SubscriptionChangeLogs.CountAsync()).Should().Be(subscriptionChangeLogCountBefore,
                "a stand-alone owner change must not write to the subscription log — no money moved");

            // Exactly one CompanyOwnerChangeLog row, WithTransfer = false (§50).
            var ownerLogs = await db.CompanyOwnerChangeLogs.Where(l => l.CompanyId == company.Id).ToListAsync();
            ownerLogs.Should().ContainSingle();
            ownerLogs[0].WithTransfer.Should().BeFalse();
            ownerLogs[0].OldOwnerUserId.Should().Be(owner.UserId);
            ownerLogs[0].NewOwnerUserId.Should().Be(newOwner.UserId);
        }
    }

    // ── Bookings ─────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("ADM-019")]
    public async Task GetBookings_FiltersByCompanyDateRangeAndStatus()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);

        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 700);

        var date1 = NextWeekday();
        var date2 = date1.AddDays(7);
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date1);
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date2);

        var client1 = await RegisterAsync();
        var booking1Response = await AuthedClient(client1.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date1, new TimeOnly(9, 0), null, null, null, null, null));
        var booking1 = await booking1Response.Content.ReadJsonAsync<BookingDto>();

        var client2 = await RegisterAsync();
        var booking2Response = await AuthedClient(client2.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date2, new TimeOnly(9, 0), null, null, null, null, null));
        var booking2 = await booking2Response.Content.ReadJsonAsync<BookingDto>();

        // Complete booking2 so it has a different status from booking1 (Confirmed).
        var masterClient = AuthedClient(master.Token);
        (await masterClient.PatchAsync($"/api/bookings/{booking2!.Id}/complete", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Filter by companyId only -> both bookings.
        var byCompany = await (await adminClient.GetAsync($"/api/admin/bookings?companyId={company.Id}"))
            .Content.ReadJsonAsync<List<AdminBookingDto>>();
        byCompany.Should().HaveCount(2);
        byCompany!.Select(b => b.Id).Should().Contain([booking1!.Id, booking2.Id]);

        // Filter by companyId + status=Completed -> only booking2.
        var byStatus = await (await adminClient.GetAsync($"/api/admin/bookings?companyId={company.Id}&status=Completed"))
            .Content.ReadJsonAsync<List<AdminBookingDto>>();
        byStatus.Should().ContainSingle(b => b.Id == booking2.Id);

        // Filter by companyId + date range covering only date1 -> only booking1.
        var byRange = await (await adminClient.GetAsync($"/api/admin/bookings?companyId={company.Id}&from={date1:yyyy-MM-dd}&to={date1:yyyy-MM-dd}"))
            .Content.ReadJsonAsync<List<AdminBookingDto>>();
        byRange.Should().ContainSingle(b => b.Id == booking1.Id);

        // Combined companyId + range + status -> booking2 only.
        var combined = await (await adminClient.GetAsync(
            $"/api/admin/bookings?companyId={company.Id}&from={date2:yyyy-MM-dd}&to={date2:yyyy-MM-dd}&status=Completed"))
            .Content.ReadJsonAsync<List<AdminBookingDto>>();
        combined.Should().ContainSingle(b => b.Id == booking2.Id);
    }

    // ── Subscription Plan Configs ────────────────────────────────────────────────────────

    [Fact, TestCase("ADM-020")]
    public async Task PlansCrud_FullCycle_CreateUpdateSoftDeleteAndNotFoundOnUnknownId()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);

        var newPlan = NewPlanConfig();
        var createResponse = await adminClient.PostAsJsonAsync("/api/admin/plans", newPlan);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadJsonAsync<AdminPlanDto>();
        created!.Id.Should().NotBeEmpty();
        created.Name.Should().Be(newPlan.Name);
        // US-24: photo quota/retention travel through the same create/update/GET cycle as every other
        // tariff field — the response is the contract's AdminPlanDto projection (openapi-cycle5.yaml),
        // not the entity serialized directly.
        created.PhotoQuotaMb.Should().Be(newPlan.PhotoQuotaMb);
        created.PhotoRetention.Should().Be(newPlan.PhotoRetention);

        // GET /plans includes the new plan.
        var listResponse = await adminClient.GetAsync("/api/admin/plans");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var plans = (await listResponse.Content.ReadJsonAsync<AdminPlansListDto>())!.Plans;
        plans.Should().Contain(p => p.Id == created.Id);

        // Update.
        var updateDto = ToUpdateDto(created, name: "Updated Plan Name", pricePerMonth: 999.99m,
            maxEmployees: 5, maxCompanies: 3, allowAnalytics: true, photoQuotaMb: 2048, photoRetention: PhotoRetention.Forever);
        var updateResponse = await adminClient.PutAsJsonAsync($"/api/admin/plans/{created.Id}", updateDto);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadJsonAsync<AdminPlanDto>();
        updated!.Name.Should().Be("Updated Plan Name");
        updated.PricePerMonth.Should().Be(999.99m);
        updated.MaxEmployees.Should().Be(5);
        updated.MaxCompanies.Should().Be(3);
        updated.AllowAnalytics.Should().BeTrue();
        updated.PhotoQuotaMb.Should().Be(2048);
        updated.PhotoRetention.Should().Be(PhotoRetention.Forever);

        // Soft-delete: sets IsActive = false but GetPlans does NOT filter by IsActive, so it still shows up.
        var deleteResponse = await adminClient.DeleteAsync($"/api/admin/plans/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfterDelete = await adminClient.GetAsync("/api/admin/plans");
        var plansAfterDelete = (await listAfterDelete.Content.ReadJsonAsync<AdminPlansListDto>())!.Plans;
        var stillPresent = plansAfterDelete.Should().ContainSingle(p => p.Id == created.Id).Subject;
        stillPresent.IsActive.Should().BeFalse();

        // 404s for unknown id.
        var unknownId = Guid.NewGuid();
        (await adminClient.PutAsJsonAsync($"/api/admin/plans/{unknownId}", updateDto)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await adminClient.DeleteAsync($"/api/admin/plans/{unknownId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ADM-021 ("UpdateSubscription_WithBareDateStringForPaidUntil_Succeeds") and ADM-017
    // ("UpdateSubscription_CalledTwice_UpdatesExistingRowRatherThanDuplicating") tested
    // AdminController.UpdateSubscription's own body-parsing/upsert behavior. That endpoint
    // (PUT /api/admin/owners/{ownerUserId}/subscription) is now contractually retired
    // (openapi-cycle5.yaml, redaction 2.1) and answers 410 Gone unconditionally without touching its
    // body — see ADM-048 below for coverage of the retirement itself. Its replacement
    // (PUT /admin/billing-accounts/{accountId}/subscription) doesn't exist yet (cycle-07 backend
    // report), so there is currently no endpoint whose date-parsing/upsert behavior these two tests
    // could exercise; removed rather than kept red or rewritten against dead code. Re-add equivalent
    // coverage once the replacement endpoint ships (flagged in the QA report as a follow-up).

    [Fact, TestCase("ADM-022")]
    public async Task GetSubscriptionHistory_AfterTwoChanges_ReturnsThemNewestFirstWithOldAndNewValues()
    {
        // Written against SubscriptionChangeLogDto (openapi-cycle5.yaml)/GetSubscriptionHistory, which
        // is unaffected by the owners/subscription retirement above — only the write side (previously
        // AdminController.UpdateSubscription, now gone with no replacement yet) is gone. Nothing in the
        // running system currently writes SubscriptionChangeLog rows (flagged separately in the QA
        // report), so this test seeds them directly, the same way ApiTestBase's other helpers seed
        // AccountSubscription/SubscriptionPlanConfig rows straight into the DB.
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var configId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
            db.SubscriptionChangeLogs.AddRange(
                new SubscriptionChangeLog
                {
                    Id = Guid.NewGuid(), OwnerUserId = owner.UserId, ChangedByUserId = admin.UserId,
                    ChangedAt = DateTime.UtcNow.AddMinutes(-1),
                    OldPlanConfigId = null, NewPlanConfigId = configId,
                    OldPaidUntil = null, NewPaidUntil = DateTime.UtcNow.AddMonths(1),
                    OldIsActive = false, NewIsActive = true, Comment = "activated",
                },
                new SubscriptionChangeLog
                {
                    Id = Guid.NewGuid(), OwnerUserId = owner.UserId, ChangedByUserId = admin.UserId,
                    ChangedAt = DateTime.UtcNow,
                    OldPlanConfigId = configId, NewPlanConfigId = null,
                    OldPaidUntil = DateTime.UtcNow.AddMonths(1), NewPaidUntil = null,
                    OldIsActive = true, NewIsActive = false, Comment = "cancelled",
                });
            await db.SaveChangesAsync();
        }

        var response = await adminClient.GetAsync($"/api/admin/owners/{owner.UserId}/subscription-history");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await response.Content.ReadFromJsonAsync<List<SubscriptionChangeLogDto>>();

        history.Should().HaveCount(2);
        history![0].Comment.Should().Be("cancelled");
        history[0].NewIsActive.Should().BeFalse();
        history[0].NewPlanName.Should().Be("Free");
        history[1].Comment.Should().Be("activated");
        history[1].NewIsActive.Should().BeTrue();
    }

    [Fact, TestCase("ADM-023")]
    public async Task AccountSubscription_CoversAllCompaniesOfTheSameOwner()
    {
        // The tariff is account-level: assigning it to the owner reflects on EVERY company they own,
        // not just the one it was set through.
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var (owner, firstCompany) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        // A branch-capable plan so the owner can open a second company, then set it on the account.
        var configId = await CreateTestPlanConfigAsync(allowMailing: true, maxCompanies: 3);
        await SetSubscriptionAsync(firstCompany.Id, configId);

        var secondSlug = Unique("branch-");
        var secondResponse = await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto($"Branch {secondSlug}", secondSlug, null, null, null, null, await AnyCityIdAsync(), null));
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondCompany = (await secondResponse.Content.ReadFromJsonAsync<CompanyDto>())!;

        // Search matches company name/email (not owner email), so fetch a large page and pick the two by
        // id — pageSize=500 (cycle C pagination, US-49) comfortably covers what this shared-database
        // suite accumulates by the time this test runs; ordered oldest-first, a small default page would
        // miss companies created late in the run.
        var companiesPage = await (await adminClient.GetAsync("/api/admin/companies?pageSize=500"))
            .Content.ReadJsonAsync<PagedResult<AdminCompanyDto>>();
        var companies = companiesPage!.Items;

        // Both companies resolve to the same owner account and therefore the same plan.
        var first = companies.Should().ContainSingle(c => c.Id == firstCompany.Id).Subject;
        var second = companies.Should().ContainSingle(c => c.Id == secondCompany.Id).Subject;
        first.OwnerUserId.Should().Be(owner.UserId);
        second.OwnerUserId.Should().Be(owner.UserId);
        first.PlanConfigId.Should().Be(configId);
        second.PlanConfigId.Should().Be(configId);
    }

    [Fact, TestCase("ADM-024")]
    public async Task GetUsers_SurfacesAccountPlanAndOwnedCompanyCountForOwner()
    {
        // The Users tab is where the account tariff is managed, so GetUsers must expose each owner's
        // resolved plan and how many companies they own.
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var configId = await CreateTestPlanConfigAsync(allowMailing: true);
        await SetSubscriptionAsync(company.Id, configId);

        var usersPage = await (await adminClient.GetAsync($"/api/admin/users?search={Uri.EscapeDataString(owner.Phone)}"))
            .Content.ReadFromJsonAsync<PagedResult<AdminUserDto>>();
            var users = usersPage?.Items;

        var entry = users.Should().ContainSingle(u => u.Id == owner.UserId).Subject;
        entry.OwnedCompanyCount.Should().Be(1);
        entry.PlanConfigId.Should().Be(configId);
        entry.SubscriptionActive.Should().BeTrue();
    }

    // ── DELETE /api/admin/plans/{id} & PUT .../subscription — US-08 (Q2) ──────

    [Fact, TestCase("ADM-032")]
    public async Task DeletePlan_WithActiveSubscriber_ReturnsConflict_AndPlanStaysActive()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var configId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);
        await SetSubscriptionAsync(company.Id, configId);

        var response = await adminClient.DeleteAsync($"/api/admin/plans/{configId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("1");

        var plans = (await (await adminClient.GetAsync("/api/admin/plans")).Content.ReadJsonAsync<AdminPlansListDto>())!.Plans;
        plans.Should().ContainSingle(p => p.Id == configId && p.IsActive);
    }

    [Fact, TestCase("ADM-037")]
    public async Task UpdatePlan_DeactivatingPlanWithActiveSubscriber_ReturnsConflict_AndPlanStaysActive()
    {
        // The 409 on DeletePlan is worth nothing if the same deactivation goes through the edit form:
        // both doors lead to PlanConfig.IsActive = false, which drops every subscriber to Free.
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var configId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);
        await SetSubscriptionAsync(company.Id, configId);

        var existing = (await (await adminClient.GetAsync("/api/admin/plans")).Content.ReadJsonAsync<AdminPlansListDto>())!.Plans;
        var plan = existing.Single(p => p.Id == configId);
        var updateDto = ToUpdateDto(plan, isActive: false);

        var response = await adminClient.PutJsonAsync($"/api/admin/plans/{configId}", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("1");

        var after = (await (await adminClient.GetAsync("/api/admin/plans")).Content.ReadJsonAsync<AdminPlansListDto>())!.Plans;
        after.Should().ContainSingle(p => p.Id == configId && p.IsActive);
    }

    [Fact, TestCase("ADM-038")]
    public async Task UpdatePlan_DeactivatingPlanWithoutSubscribers_Succeeds()
    {
        // The guard must only bite when someone is actually on the plan — retiring an unused plan
        // through the edit form stays a normal operation.
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var configId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);

        var existing = (await (await adminClient.GetAsync("/api/admin/plans")).Content.ReadJsonAsync<AdminPlansListDto>())!.Plans;
        var plan = existing.Single(p => p.Id == configId);
        var updateDto = ToUpdateDto(plan, isActive: false);

        var response = await adminClient.PutJsonAsync($"/api/admin/plans/{configId}", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await (await adminClient.GetAsync("/api/admin/plans")).Content.ReadJsonAsync<AdminPlansListDto>())!.Plans;
        after.Should().ContainSingle(p => p.Id == configId && !p.IsActive);
    }

    // ADM-033 ("UpdateSubscription_WithDeactivatedPlan_ReturnsBadRequest") and ADM-036
    // ("UpdateSubscription_UnknownOwnerUserId_ReturnsNotFound") tested validation that lived in
    // AdminController.UpdateSubscription (rejecting an assignment to a deactivated plan / an unknown
    // owner) via the now-retired PUT /api/admin/owners/{ownerUserId}/subscription — see the comment
    // above ADM-022 for why they were removed rather than rewritten. Re-add equivalent validation
    // coverage against the replacement endpoint once it exists — it is exactly the kind of check that's
    // easy to silently drop while porting an endpoint (this cycle's own QA report flags the risk).

    [Fact, TestCase("ADM-034")]
    public async Task Owner_OnDeactivatedPlan_ResolvesToFree()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false, allowSelfBooking: true);
        var configId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);
        await SetSubscriptionAsync(company.Id, configId);

        var before = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        (await before.Content.ReadFromJsonAsync<CompanyDto>())!.OnlineBookingEnabled.Should().BeTrue();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
            var plan = await db.SubscriptionPlanConfigs.FindAsync(configId);
            plan!.IsActive = false;
            await db.SaveChangesAsync();
        }

        var after = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        (await after.Content.ReadFromJsonAsync<CompanyDto>())!.OnlineBookingEnabled.Should().BeFalse();
    }

    private SubscriptionPlanConfig NewPlanConfig() => new()
    {
        Name = $"Plan {Unique("plan")}",
        PricePerMonth = 100,
        MaxEmployees = 3,
        MaxCompanies = 2,
        AllowOnlineBooking = true,
        AllowMailing = false,
        AllowAnalytics = false,
        Description = "Test plan",
        IsActive = true,
        NotifyDaysBefore = 7,
        PhotoQuotaMb = 5120,
        PhotoRetention = PhotoRetention.TwelveMonths
    };

    // ── US-24: photo quota / retention validation ────────────────────────────

    [Fact, TestCase("ADM-041")]
    public async Task CreatePlan_NegativePhotoQuota_ReturnsBadRequest()
    {
        var admin = await LoginAsSuperAdminAsync();
        var plan = NewPlanConfig();
        plan.PhotoQuotaMb = -1;

        var response = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/plans", plan);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("ADM-042")]
    public async Task UpdatePlan_NegativePhotoQuota_ReturnsBadRequest()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var created = (await (await adminClient.PostAsJsonAsync("/api/admin/plans", NewPlanConfig()))
            .Content.ReadJsonAsync<AdminPlanDto>())!;

        var response = await adminClient.PutJsonAsync($"/api/admin/plans/{created.Id}",
            ToUpdateDto(created, photoQuotaMb: -5));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("ADM-043")]
    public async Task CreatePlan_NullPhotoQuota_MeansUnlimited_PersistsAsNull()
    {
        var admin = await LoginAsSuperAdminAsync();
        var plan = NewPlanConfig();
        plan.PhotoQuotaMb = null;

        var response = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/plans", plan);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadJsonAsync<AdminPlanDto>();
        created!.PhotoQuotaMb.Should().BeNull();
    }

    // ── Cycle 5 (SPEC.md П4, US-66): IsSystemFree/Highlights/IsPublic/SortOrder on plan writes ────
    // Written from the reviewer's finding, independently of AdminController's own implementation:
    // exactly one plan may be flagged IsSystemFree (§39: "бесплатный тариф публикуется обычной строкой";
    // a second system-free row would make the "current free plan" lookup ambiguous), and it must not
    // carry a non-zero price. Both writes below (POST and PUT) must reject bad combinations instead of
    // letting the DB's partial unique index turn it into an unhandled 500.
    //
    // Response bodies are read as AdminPlanDto/AdminPlansListDto (openapi-cycle5.yaml's actual response
    // shape — highlights as an array), not SubscriptionPlanConfig — see ADM-020 and the QA report for
    // why deserializing straight into the EF entity is the wrong pattern here. Numbered ADM-050+ (not
    // ADM-044+, which collided with the pre-existing ADM-044 "GetUsers_SearchOfNonAsciiDigitsOnly").

    [Fact, TestCase("ADM-050")]
    public async Task CreatePlan_IsSystemFreeWithNonZeroPrice_ReturnsBadRequest()
    {
        var admin = await LoginAsSuperAdminAsync();
        var plan = NewPlanConfig();
        plan.IsSystemFree = true;
        plan.PricePerMonth = 100;

        var response = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/plans", plan);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("ADM-051")]
    public async Task CreatePlan_SecondIsSystemFreePlan_ReturnsConflict()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var first = NewPlanConfig();
        first.IsSystemFree = true;
        first.PricePerMonth = 0;
        (await adminClient.PostAsJsonAsync("/api/admin/plans", first)).StatusCode.Should().Be(HttpStatusCode.Created);

        var second = NewPlanConfig();
        second.IsSystemFree = true;
        second.PricePerMonth = 0;
        var response = await adminClient.PostAsJsonAsync("/api/admin/plans", second);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("ADM-052")]
    public async Task UpdatePlan_IsSystemFreeWithNonZeroPrice_ReturnsBadRequest()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var created = (await (await adminClient.PostAsJsonAsync("/api/admin/plans", NewPlanConfig()))
            .Content.ReadJsonAsync<AdminPlanDto>())!;

        var response = await adminClient.PutJsonAsync($"/api/admin/plans/{created.Id}",
            ToUpdateDto(created, isSystemFree: true, pricePerMonth: 50));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("ADM-053")]
    public async Task UpdatePlan_PersistsHighlightsIsPublicSortOrderAndIsSystemFree()
    {
        // Regression: the original PUT silently dropped these four fields, so a plan published via
        // POST could never be edited, reordered or unpublished again through this endpoint.
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var plan = NewPlanConfig();
        plan.Highlights = "Было";
        plan.IsPublic = false;
        plan.SortOrder = 1;
        plan.IsSystemFree = false;
        var created = (await (await adminClient.PostAsJsonAsync("/api/admin/plans", plan))
            .Content.ReadJsonAsync<AdminPlanDto>())!;
        created.Highlights.Should().Equal("Было");

        var updateResponse = await adminClient.PutJsonAsync($"/api/admin/plans/{created.Id}",
            ToUpdateDto(created, highlights: "Стало\nВторая строка", isPublic: true, sortOrder: 42));

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadJsonAsync<AdminPlanDto>();
        updated!.Highlights.Should().Equal("Стало", "Вторая строка");
        updated.IsPublic.Should().BeTrue();
        updated.SortOrder.Should().Be(42);

        var reread = (await (await adminClient.GetAsync("/api/admin/plans")).Content
            .ReadJsonAsync<AdminPlansListDto>())!.Plans;
        var persisted = reread.Should().ContainSingle(p => p.Id == created.Id).Subject;
        persisted.Highlights.Should().Equal("Стало", "Вторая строка");
        persisted.IsPublic.Should().BeTrue();
        persisted.SortOrder.Should().Be(42);
    }

    /// <summary>
    /// Builds a PUT /api/admin/plans/{id} request body from a previously-fetched AdminPlanDto plus
    /// explicit overrides — the request (UpdatePlanDto) and response (AdminPlanDto) shapes differ (most
    /// notably: request Highlights is a single newline-joined string, response Highlights is a
    /// List&lt;string&gt; — see the QA report's note on this being an AdminPlanInput/contract mismatch
    /// worth a backend follow-up), so the response body can't just be echoed back as the next PUT's body.
    /// </summary>
    private static UpdatePlanDto ToUpdateDto(AdminPlanDto plan,
        string? name = null, decimal? pricePerMonth = null, int? maxEmployees = null, int? maxCompanies = null,
        bool? allowAnalytics = null, int? photoQuotaMb = null, PhotoRetention? photoRetention = null,
        bool? isActive = null, string? highlights = null, bool? isPublic = null, int? sortOrder = null,
        bool? isSystemFree = null) => new(
            name ?? plan.Name, pricePerMonth ?? plan.PricePerMonth,
            maxEmployees ?? plan.MaxEmployees, maxCompanies ?? plan.MaxCompanies,
            plan.AllowOnlineBooking, plan.AllowMailing, allowAnalytics ?? plan.AllowAnalytics,
            plan.AllowPublicListing, plan.AllowOnlinePayment,
            photoQuotaMb ?? plan.PhotoQuotaMb, photoRetention ?? plan.PhotoRetention,
            plan.Description, isActive ?? plan.IsActive, plan.NotifyDaysBefore,
            highlights, isPublic, sortOrder, isSystemFree);
}
