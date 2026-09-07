using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Bookings;
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
        var users = await response.Content.ReadFromJsonAsync<List<AdminUserDto>>();

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
        var users = await response.Content.ReadFromJsonAsync<List<AdminUserDto>>();

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
        var users = await response.Content.ReadFromJsonAsync<List<AdminUserDto>>();

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
        var companies = await response.Content.ReadJsonAsync<List<AdminCompanyDto>>();

        var found = companies.Should().ContainSingle(c => c.Id == company.Id).Subject;
        found.MemberCount.Should().Be(2); // owner + master
        found.BookingCount.Should().Be(1);
        found.PlanName.Should().Be("QA Full Access");
    }

    [Fact, TestCase("ADM-017")]
    public async Task UpdateSubscription_CalledTwice_UpdatesExistingRowRatherThanDuplicating()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var firstConfigId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);
        var secondConfigId = await CreateTestPlanConfigAsync(allowOnlineBooking: true, allowMailing: true);

        var firstPaidUntil = DateTime.UtcNow.AddMonths(1);
        var first = await adminClient.PutJsonAsync($"/api/admin/owners/{owner.UserId}/subscription",
            new UpdateSubscriptionDto(firstConfigId, firstPaidUntil, true, "first"));
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var secondPaidUntil = DateTime.UtcNow.AddMonths(2);
        var second = await adminClient.PutJsonAsync($"/api/admin/owners/{owner.UserId}/subscription",
            new UpdateSubscriptionDto(secondConfigId, secondPaidUntil, false, "second"));
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await adminClient.GetAsync($"/api/admin/companies?search={company.Slug}");
        var companies = await listResponse.Content.ReadJsonAsync<List<AdminCompanyDto>>();

        // Exactly one row for this company — a duplicate-insert bug would still show one row here
        // (GetCompanies groups by company id), but the reflected plan must be the *second* call's value,
        // proving the existing subscription row was updated in place, not left stale by a duplicate insert.
        var found = companies.Should().ContainSingle(c => c.Id == company.Id).Subject;
        found.PlanConfigId.Should().Be(secondConfigId);
        found.SubscriptionActive.Should().BeFalse();
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

        var companies = await (await AuthedClient(admin.Token).GetAsync($"/api/admin/companies?search={company.Slug}"))
            .Content.ReadFromJsonAsync<List<AdminCompanyDto>>();
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

    [Fact, TestCase("ADM-031")]
    public async Task UpdateCompanyOwner_MovesBillingToNewOwnersPlan()
    {
        // Since the tariff is resolved through Company.OwnerUserId, transferring ownership must move the
        // company onto the NEW owner's plan — the whole point of the OwnerUserId field.
        var admin = await LoginAsSuperAdminAsync();
        var (_, company) = await CreateOwnerWithCompanyAsync(attachPlan: false); // original owner: Free

        // Prepare a second owner who has an active paid plan on their own account.
        var (paidOwner, _) = await CreateOwnerWithCompanyAsync();

        var transfer = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/admin/companies/{company.Id}/owner", new UpdateCompanyOwnerDto(paidOwner.UserId));
        transfer.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var companies = await (await AuthedClient(admin.Token).GetAsync($"/api/admin/companies?search={company.Slug}"))
            .Content.ReadFromJsonAsync<List<AdminCompanyDto>>();
        var entry = companies.Should().ContainSingle(c => c.Id == company.Id).Subject;
        entry.OwnerUserId.Should().Be(paidOwner.UserId);
        entry.PlanName.Should().Be("QA Full Access"); // now governed by the new owner's paid plan
        entry.SubscriptionActive.Should().BeTrue();
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
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.Content.ReadJsonAsync<SubscriptionPlanConfig>();
        created!.Id.Should().NotBeEmpty();
        created.Name.Should().Be(newPlan.Name);
        // US-24: photo quota/retention travel through the same create/update/GET cycle as every other
        // tariff field — no separate DTO, the entity is serialized directly (API_CONTRACT.md §11).
        created.PhotoQuotaMb.Should().Be(newPlan.PhotoQuotaMb);
        created.PhotoRetention.Should().Be(newPlan.PhotoRetention);

        // GET /plans includes the new plan.
        var listResponse = await adminClient.GetAsync("/api/admin/plans");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var plans = await listResponse.Content.ReadJsonAsync<List<SubscriptionPlanConfig>>();
        plans.Should().Contain(p => p.Id == created.Id);

        // Update.
        created.Name = "Updated Plan Name";
        created.PricePerMonth = 999.99m;
        created.MaxEmployees = 5;
        created.MaxCompanies = 3;
        created.AllowAnalytics = true;
        created.PhotoQuotaMb = 2048;
        created.PhotoRetention = PhotoRetention.Forever;
        var updateResponse = await adminClient.PutAsJsonAsync($"/api/admin/plans/{created.Id}", created);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadJsonAsync<SubscriptionPlanConfig>();
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
        var plansAfterDelete = await listAfterDelete.Content.ReadJsonAsync<List<SubscriptionPlanConfig>>();
        var stillPresent = plansAfterDelete.Should().ContainSingle(p => p.Id == created.Id).Subject;
        stillPresent.IsActive.Should().BeFalse();

        // 404s for unknown id.
        var unknownId = Guid.NewGuid();
        (await adminClient.PutAsJsonAsync($"/api/admin/plans/{unknownId}", created)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await adminClient.DeleteAsync($"/api/admin/plans/{unknownId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("ADM-021")]
    public async Task UpdateSubscription_WithBareDateStringForPaidUntil_Succeeds()
    {
        // Regression test: a plain <input type="date"> on the admin page posts "paidUntil" as a bare
        // "2026-08-01" string with no time or timezone offset. System.Text.Json deserializes that into
        // a DateTime with Kind=Unspecified, and Npgsql previously threw
        // "Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'"
        // when that value was assigned straight to the subscription's PaidUntil. AdminController.UpdateSubscription
        // must normalize it to UTC before saving instead of erroring.
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var rawJson = "{\"planConfigId\":null,\"paidUntil\":\"2026-08-01\",\"isActive\":true,\"comment\":\"bare date\"}";
        var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var response = await adminClient.PutAsync($"/api/admin/owners/{owner.UserId}/subscription", content);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("ADM-022")]
    public async Task GetSubscriptionHistory_AfterTwoChanges_ReturnsThemNewestFirstWithOldAndNewValues()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var configId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);

        var first = await adminClient.PutJsonAsync($"/api/admin/owners/{owner.UserId}/subscription",
            new UpdateSubscriptionDto(configId, DateTime.UtcNow.AddMonths(1), true, "activated"));
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await adminClient.PutJsonAsync($"/api/admin/owners/{owner.UserId}/subscription",
            new UpdateSubscriptionDto(null, null, false, "cancelled"));
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

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
            new CreateCompanyDto($"Branch {secondSlug}", secondSlug, null, null, null, null));
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondCompany = (await secondResponse.Content.ReadFromJsonAsync<CompanyDto>())!;

        // Search matches company name/email (not owner email), so fetch all and pick the two by id.
        var companies = await (await adminClient.GetAsync("/api/admin/companies"))
            .Content.ReadJsonAsync<List<AdminCompanyDto>>();

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

        var users = await (await adminClient.GetAsync($"/api/admin/users?search={Uri.EscapeDataString(owner.Phone)}"))
            .Content.ReadFromJsonAsync<List<AdminUserDto>>();

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

        var plans = await (await adminClient.GetAsync("/api/admin/plans")).Content.ReadJsonAsync<List<SubscriptionPlanConfig>>();
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

        var existing = await (await adminClient.GetAsync("/api/admin/plans")).Content.ReadJsonAsync<List<SubscriptionPlanConfig>>();
        var plan = existing!.Single(p => p.Id == configId);
        plan.IsActive = false;

        var response = await adminClient.PutJsonAsync($"/api/admin/plans/{configId}", plan);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("1");

        var after = await (await adminClient.GetAsync("/api/admin/plans")).Content.ReadJsonAsync<List<SubscriptionPlanConfig>>();
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

        var existing = await (await adminClient.GetAsync("/api/admin/plans")).Content.ReadJsonAsync<List<SubscriptionPlanConfig>>();
        var plan = existing!.Single(p => p.Id == configId);
        plan.IsActive = false;

        var response = await adminClient.PutJsonAsync($"/api/admin/plans/{configId}", plan);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await (await adminClient.GetAsync("/api/admin/plans")).Content.ReadJsonAsync<List<SubscriptionPlanConfig>>();
        after.Should().ContainSingle(p => p.Id == configId && !p.IsActive);
    }

    [Fact, TestCase("ADM-033")]
    public async Task UpdateSubscription_WithDeactivatedPlan_ReturnsBadRequest()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var configId = await CreateTestPlanConfigAsync(allowOnlineBooking: true);

        // Deactivate it directly — no active subscriber yet, so DeletePlan itself would succeed too,
        // but going straight to the DB keeps this test focused on UpdateSubscription's own check.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
            var plan = await db.SubscriptionPlanConfigs.FindAsync(configId);
            plan!.IsActive = false;
            await db.SaveChangesAsync();
        }

        var response = await adminClient.PutJsonAsync($"/api/admin/owners/{owner.UserId}/subscription",
            new UpdateSubscriptionDto(configId, DateTime.UtcNow.AddMonths(1), true, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

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

    [Fact, TestCase("ADM-036")]
    public async Task UpdateSubscription_UnknownOwnerUserId_ReturnsNotFound()
    {
        var admin = await LoginAsSuperAdminAsync();
        var adminClient = AuthedClient(admin.Token);

        var response = await adminClient.PutJsonAsync($"/api/admin/owners/{Guid.NewGuid()}/subscription",
            new UpdateSubscriptionDto(null, null, true, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
            .Content.ReadJsonAsync<SubscriptionPlanConfig>())!;

        created.PhotoQuotaMb = -5;
        var response = await adminClient.PutAsJsonAsync($"/api/admin/plans/{created.Id}", created);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("ADM-043")]
    public async Task CreatePlan_NullPhotoQuota_MeansUnlimited_PersistsAsNull()
    {
        var admin = await LoginAsSuperAdminAsync();
        var plan = NewPlanConfig();
        plan.PhotoQuotaMb = null;

        var response = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/plans", plan);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await response.Content.ReadJsonAsync<SubscriptionPlanConfig>();
        created!.PhotoQuotaMb.Should().BeNull();
    }
}
