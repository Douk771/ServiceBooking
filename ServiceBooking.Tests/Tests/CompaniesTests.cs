using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class CompaniesTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── GET /api/companies ──────────────────────────────────────────────────

    [Fact, TestCase("CO-001")]
    public async Task GetAll_OnlyReturnsActiveCompanies()
    {
        var (_, activeCompany) = await CreateOwnerWithCompanyAsync();
        var (_, inactiveCompany) = await CreateOwnerWithCompanyAsync();

        var admin = await LoginAsSuperAdminAsync();
        var deactivate = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/companies/{inactiveCompany.Id}",
            new { name = inactiveCompany.Name, isActive = false, allowSelfBooking = true });
        deactivate.EnsureSuccessStatusCode();

        var response = await AnonymousClient().GetAsync("/api/companies");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var companies = await response.Content.ReadFromJsonAsync<List<CompanyDto>>();
        companies.Should().Contain(c => c.Id == activeCompany.Id);
        companies.Should().NotContain(c => c.Id == inactiveCompany.Id);
    }

    // ── GET /api/companies/my ────────────────────────────────────────────────

    [Fact, TestCase("CO-002")]
    public async Task GetMy_ForCompanyOwner_ReturnsOwnedCompany()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).GetAsync("/api/companies/my");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var companies = await response.Content.ReadFromJsonAsync<List<CompanyDto>>();
        companies.Should().ContainSingle(c => c.Id == company.Id);
    }

    [Fact, TestCase("CO-003")]
    public async Task GetMy_ForPlainClient_ReturnsEmpty()
    {
        var client = await RegisterAsync();

        var response = await AuthedClient(client.Token).GetAsync("/api/companies/my");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var companies = await response.Content.ReadFromJsonAsync<List<CompanyDto>>();
        companies.Should().BeEmpty();
    }

    // ── GET /api/companies/member ────────────────────────────────────────────

    [Fact, TestCase("CO-004")]
    public async Task GetMemberOf_ReturnsCompaniesForAnyRole()
    {
        var user = await RegisterAsync();
        var ownedCompany = await CreateCompanyAsync(user.Token);

        var (otherOwner, otherCompany) = await CreateOwnerWithCompanyAsync();
        var addMasterResponse = await AuthedClient(otherOwner.Token).PostAsJsonAsync($"/api/companies/{otherCompany.Id}/members",
            new { phone = user.Phone, firstName = user.FirstName, lastName = user.LastName, role = "Master", bio = (string?)null });
        addMasterResponse.EnsureSuccessStatusCode();

        var response = await AuthedClient(user.Token).GetAsync("/api/companies/member");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var companies = await response.Content.ReadFromJsonAsync<List<CompanyDto>>();
        companies.Should().Contain(c => c.Id == ownedCompany.Id);
        companies.Should().Contain(c => c.Id == otherCompany.Id);
    }

    // ── GET /api/companies/{slug} ─────────────────────────────────────────────

    [Fact, TestCase("CO-005")]
    public async Task GetBySlug_ExistingActiveCompany_ReturnsOk()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<CompanyDto>();
        dto!.Id.Should().Be(company.Id);
    }

    [Fact, TestCase("CO-006")]
    public async Task GetBySlug_UnknownSlug_ReturnsNotFound()
    {
        var response = await AnonymousClient().GetAsync($"/api/companies/{Unique("no-such-slug-")}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── onlineBookingEnabled flag ──────────────────────────────────────────────

    [Fact, TestCase("CO-007")]
    public async Task GetBySlug_FreePlanCompany_OnlineBookingEnabledIsFalse()
    {
        // Free plan (onlineBooking: false) → online booking blocked even though allowSelfBooking is true.
        // The flag must reflect that so the client UI can avoid a booking that will fail with 402.
        var (_, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true, onlineBooking: false);

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        var dto = await response.Content.ReadFromJsonAsync<CompanyDto>();

        dto!.AllowSelfBooking.Should().BeTrue();
        dto.OnlineBookingEnabled.Should().BeFalse();
    }

    [Fact, TestCase("CO-008")]
    public async Task GetBySlug_PaidPlanCompany_OnlineBookingEnabledIsTrue()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        await SetSubscriptionAsync(company.Id);

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        var dto = await response.Content.ReadFromJsonAsync<CompanyDto>();

        dto!.OnlineBookingEnabled.Should().BeTrue();
    }

    [Fact, TestCase("CO-009")]
    public async Task GetBySlug_PaidPlanButSelfBookingDisabled_OnlineBookingEnabledIsFalse()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: false);
        await SetSubscriptionAsync(company.Id);

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        var dto = await response.Content.ReadFromJsonAsync<CompanyDto>();

        // A paid plan alone isn't enough — allowSelfBooking must also be on.
        dto!.OnlineBookingEnabled.Should().BeFalse();
    }

    [Fact, TestCase("CO-010")]
    public async Task GetBySlug_ExpiredPaidSubscription_OnlineBookingEnabledIsFalse()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        await SetSubscriptionAsync(company.Id, paidUntil: DateTime.UtcNow.AddDays(-1));

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        var dto = await response.Content.ReadFromJsonAsync<CompanyDto>();

        // Matches the same expiry check the booking endpoint applies before allowing a guest booking.
        dto!.OnlineBookingEnabled.Should().BeFalse();
    }

    [Fact, TestCase("CO-011")]
    public async Task GetAll_ReflectsOnlineBookingEnabledPerCompany()
    {
        var (_, freeCompany) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true, onlineBooking: false);
        var (_, paidCompany) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        await SetSubscriptionAsync(paidCompany.Id);

        var response = await AnonymousClient().GetAsync("/api/companies");
        var companies = (await response.Content.ReadFromJsonAsync<List<CompanyDto>>())!;

        companies.Single(c => c.Id == freeCompany.Id).OnlineBookingEnabled.Should().BeFalse();
        companies.Single(c => c.Id == paidCompany.Id).OnlineBookingEnabled.Should().BeTrue();
    }

    [Fact, TestCase("CO-052")]
    public async Task GetMy_ReflectsAllowAnalyticsPerCompany()
    {
        // Regression coverage for CompanyDto.AllowAnalytics, which the owner-facing UI uses to hide the
        // Reports tab instead of letting the owner open a report that silently 402s (ReportsController
        // gates GET /api/reports/masters on this same plan flag).
        var (freeOwner, freeCompany) = await CreateOwnerWithCompanyAsync(onlineBooking: false);
        var (paidOwner, paidCompany) = await CreateOwnerWithCompanyAsync();
        await SetSubscriptionAsync(paidCompany.Id);

        var freeResponse = await AuthedClient(freeOwner.Token).GetAsync("/api/companies/my");
        var freeCompanies = (await freeResponse.Content.ReadFromJsonAsync<List<CompanyDto>>())!;
        freeCompanies.Single(c => c.Id == freeCompany.Id).AllowAnalytics.Should().BeFalse();

        var paidResponse = await AuthedClient(paidOwner.Token).GetAsync("/api/companies/my");
        var paidCompanies = (await paidResponse.Content.ReadFromJsonAsync<List<CompanyDto>>())!;
        paidCompanies.Single(c => c.Id == paidCompany.Id).AllowAnalytics.Should().BeTrue();
    }

    // ── Public directory listing (owner opt-out × tariff gate) ───────────────

    [Fact, TestCase("CO-053")]
    public async Task GetAll_ExcludesCompany_WhenOwnerOptsOutOfPublicListing()
    {
        // The owner's own ShowInPublicListing=false hides the company from the directory even on a
        // fully-featured paid plan — it's an independent opt-out, not something the tariff can override.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}", new { showInPublicListing = false });

        var response = await AnonymousClient().GetAsync("/api/companies");
        var companies = (await response.Content.ReadFromJsonAsync<List<CompanyDto>>())!;

        companies.Should().NotContain(c => c.Id == company.Id);
    }

    [Fact, TestCase("CO-054")]
    public async Task GetAll_ExcludesCompany_WhenTariffDisallowsPublicListing()
    {
        // Even though the owner wants to be listed (ShowInPublicListing defaults true), a tariff with
        // AllowPublicListing=false still hides the company from the directory.
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var configId = await CreateTestPlanConfigAsync(allowPublicListing: false);
        await SetSubscriptionAsync(company.Id, configId);

        var response = await AnonymousClient().GetAsync("/api/companies");
        var companies = (await response.Content.ReadFromJsonAsync<List<CompanyDto>>())!;

        companies.Should().NotContain(c => c.Id == company.Id);
    }

    [Fact, TestCase("CO-055")]
    public async Task GetBySlug_StillReachable_WhenHiddenFromPublicDirectory()
    {
        // Being unlisted from the general directory doesn't make the company's own page unreachable —
        // only GET /api/companies (the directory) is gated; a direct link by slug still works.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}", new { showInPublicListing = false });

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CO-056")]
    public async Task GetMy_StillShowsCompany_WhenHiddenFromPublicDirectory()
    {
        // The owner's own dashboard isn't the public directory — it must keep showing companies they
        // deliberately unlisted, and reflect both the raw toggle and the computed flag correctly.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}", new { showInPublicListing = false });

        var response = await AuthedClient(owner.Token).GetAsync("/api/companies/my");
        var companies = (await response.Content.ReadFromJsonAsync<List<CompanyDto>>())!;

        var entry = companies.Should().ContainSingle(c => c.Id == company.Id).Subject;
        entry.ShowInPublicListing.Should().BeFalse();
        entry.PublicListingEnabled.Should().BeFalse();
    }

    // ── Online payment (owner toggle × tariff gate) — mirrors public listing ─

    [Fact, TestCase("CO-057")]
    public async Task Update_ReflectsPrepaymentEnabled_OnlyWhenBothOwnerAndTariffAllowIt()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        // Owner wants prepayment, but the Free baseline doesn't allow online payment.
        var beforePlan = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { requirePrepayment = true });
        var beforeDto = await beforePlan.Content.ReadJsonAsync<CompanyDto>();
        beforeDto!.RequirePrepayment.Should().BeTrue();
        beforeDto.PrepaymentEnabled.Should().BeFalse();

        // Once the tariff allows online payment, the combined flag turns on too.
        var configId = await CreateTestPlanConfigAsync(allowOnlinePayment: true);
        await SetSubscriptionAsync(company.Id, configId);

        var afterResponse = await AuthedClient(owner.Token).GetAsync("/api/companies/my");
        var afterCompanies = (await afterResponse.Content.ReadFromJsonAsync<List<CompanyDto>>())!;
        afterCompanies.Single(c => c.Id == company.Id).PrepaymentEnabled.Should().BeTrue();
    }

    // ── POST /api/companies/{id}/logo ──────────────────────────────────────────

    [Fact, TestCase("CO-058")]
    public async Task UploadLogo_ValidImage_SetsLogoUrlServedUnderUploadsPath()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        using var content = new MultipartFormDataContent();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }; // not a real JPEG, content-type is what's validated
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "logo.jpg");

        var response = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/logo", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadJsonAsync<CompanyDto>();
        dto!.LogoUrl.Should().NotBeNullOrEmpty();
        dto.LogoUrl.Should().StartWith("/uploads/companies/");
        dto.LogoUrl.Should().EndWith(".jpg");
    }

    [Fact, TestCase("CO-059")]
    public async Task UploadLogo_UnsupportedContentType_ReturnsBadRequest()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "not-an-image.pdf");

        var response = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/logo", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CO-060")]
    public async Task UploadLogo_ByStranger_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3]);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "logo.png");

        var response = await AuthedClient(stranger.Token).PostAsync($"/api/companies/{company.Id}/logo", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CO-061")]
    public async Task UploadLogo_Replace_ServesNewFileAndRemovesTheOldOne()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        async Task<string> UploadAsync()
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9]);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", "logo.png");
            var res = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/logo", content);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await res.Content.ReadJsonAsync<CompanyDto>())!.LogoUrl!;
        }

        var firstUrl = await UploadAsync();
        // The uploaded file is reachable via static files.
        (await AnonymousClient().GetAsync(firstUrl)).StatusCode.Should().Be(HttpStatusCode.OK);

        var secondUrl = await UploadAsync();
        secondUrl.Should().NotBe(firstUrl);

        // New logo is served; the previous file was cleaned up and now 404s.
        (await AnonymousClient().GetAsync(secondUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnonymousClient().GetAsync(firstUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CO-062")]
    public async Task GetAll_ExpiredPaidSubscription_KeepsCompanyListed_ButOnlineBookingOff()
    {
        // An expired subscription resolves to the Free baseline, which still allows public listing
        // (so the company stays in the directory) but not online booking.
        var (_, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        await SetSubscriptionAsync(company.Id, paidUntil: DateTime.UtcNow.AddDays(-1));

        var response = await AnonymousClient().GetAsync("/api/companies");
        var companies = (await response.Content.ReadFromJsonAsync<List<CompanyDto>>())!;

        var entry = companies.Should().ContainSingle(c => c.Id == company.Id).Subject;
        entry.PublicListingEnabled.Should().BeTrue();
        entry.OnlineBookingEnabled.Should().BeFalse();
    }

    // ── GET /api/companies/{id}/masters ──────────────────────────────────────

    [Fact, TestCase("CO-012")]
    public async Task GetMasters_WithoutServiceFilter_ReturnsAllCompanyMasters()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master1 = await AddMasterAsync(owner.Token, company.Id);
        var master2 = await AddMasterAsync(owner.Token, company.Id);

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Id}/masters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var masters = await response.Content.ReadFromJsonAsync<List<MasterPublicDto>>();
        masters.Should().Contain(m => m.UserId == master1.UserId);
        masters.Should().Contain(m => m.UserId == master2.UserId);
    }

    [Fact, TestCase("CO-013")]
    public async Task GetMasters_FilteredByServiceId_ReturnsOnlyAssignedMasters()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var assignedMaster = await AddMasterAsync(owner.Token, company.Id);
        var otherMaster = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);

        var member = await GetMemberAsync(owner.Token, company.Id, assignedMaster.UserId);
        var assign = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/services", new List<Guid> { service.Id });
        assign.EnsureSuccessStatusCode();

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Id}/masters?serviceId={service.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var masters = await response.Content.ReadFromJsonAsync<List<MasterPublicDto>>();
        masters.Should().ContainSingle(m => m.UserId == assignedMaster.UserId);
        masters.Should().NotContain(m => m.UserId == otherMaster.UserId);
    }

    [Fact, TestCase("CO-014")]
    public async Task GetMasters_ServiceIdWithNoAssignments_FallsBackToAllMasters()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        // No MasterService rows created for this service at all.

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Id}/masters?serviceId={service.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var masters = await response.Content.ReadFromJsonAsync<List<MasterPublicDto>>();
        masters.Should().Contain(m => m.UserId == master.UserId);
    }

    // ── GET /api/companies/{id}/members ──────────────────────────────────────

    [Fact, TestCase("CO-015")]
    public async Task GetMembers_AsOwner_ReturnsOk()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var response = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var members = await response.Content.ReadFromJsonAsync<List<MemberDto>>();
        members.Should().Contain(m => m.UserId == owner.UserId);
        members.Should().Contain(m => m.UserId == master.UserId);
    }

    [Fact, TestCase("CO-016")]
    public async Task GetMembers_AsSuperAdmin_ReturnsOk()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();

        var response = await AuthedClient(admin.Token).GetAsync($"/api/companies/{company.Id}/members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CO-017")]
    public async Task GetMembers_AsUnrelatedAuthenticatedUser_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).GetAsync($"/api/companies/{company.Id}/members");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CO-018")]
    public async Task GetMembers_Anonymous_ReturnsUnauthorized()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Id}/members");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── PUT /api/companies/{id}/members/{memberId}/services ─────────────────

    [Fact, TestCase("CO-019")]
    public async Task UpdateMemberServices_ByOwner_PersistsAndVisibleInMembers()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);

        var response = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/services", new List<Guid> { service.Id });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updated = await GetMemberAsync(owner.Token, company.Id, master.UserId);
        updated.ServiceIds.Should().ContainSingle(id => id == service.Id);
    }

    [Fact, TestCase("CO-020")]
    public async Task UpdateMemberServices_ByNonOwner_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/services", new List<Guid> { service.Id });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/companies/{id}/members/{memberId}/commission ────────────────

    [Fact, TestCase("CO-021")]
    public async Task UpdateMemberCommission_ByOwner_PersistsAndVisibleInMembersAndProfile()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);

        var response = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/commission", new { commissionPercent = 35 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedMember = await GetMemberAsync(owner.Token, company.Id, master.UserId);
        updatedMember.CommissionPercent.Should().Be(35);

        // The master sees the owner-set value on their own profile too (read-only from their side).
        var profileResponse = await AuthedClient(master.Token).GetAsync("/api/profile");
        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileDto>();
        profile!.CommissionPercent.Should().Be(35);
    }

    [Fact, TestCase("CO-022")]
    public async Task UpdateMemberCommission_BySuperAdmin_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);
        var admin = await LoginAsSuperAdminAsync();

        var response = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/commission", new { commissionPercent = 20 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CO-023")]
    public async Task UpdateMemberCommission_ByTheMasterThemselves_ReturnsForbidden()
    {
        // The whole point of this endpoint: a master cannot set their own commission — only the
        // company owner (or SuperAdmin) can. Unlike WorkingHours/ScheduleTemplate, CanManageCompany
        // has no "requesterId == masterId" carve-out here.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);

        var response = await AuthedClient(master.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/commission", new { commissionPercent = 90 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CO-024")]
    public async Task UpdateMemberCommission_ByUnrelatedUser_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/commission", new { commissionPercent = 50 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CO-025")]
    public async Task UpdateMemberCommission_UnknownMemberId_ReturnsNotFound()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{Guid.NewGuid()}/commission", new { commissionPercent = 50 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory, TestCase("CO-026")]
    [InlineData(150, 100)]
    [InlineData(-10, 0)]
    [InlineData(55, 55)]
    public async Task UpdateMemberCommission_IsClampedToZeroToHundredRange(decimal input, decimal expected)
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);

        var response = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/commission", new { commissionPercent = input });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedMember = await GetMemberAsync(owner.Token, company.Id, master.UserId);
        updatedMember.CommissionPercent.Should().Be(expected);
    }

    // ── POST /api/companies ───────────────────────────────────────────────────

    [Fact, TestCase("CO-027")]
    public async Task Create_ValidData_CreatesCompanyAndMakesCallerOwner()
    {
        var user = await RegisterAsync();
        var slug = Unique("newco-");
        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto($"Company {slug}", slug, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CompanyDto>();
        created!.Slug.Should().Be(slug);

        var myResponse = await AuthedClient(user.Token).GetAsync("/api/companies/my");
        var mine = await myResponse.Content.ReadFromJsonAsync<List<CompanyDto>>();
        mine.Should().ContainSingle(c => c.Id == created.Id);
    }

    [Fact, TestCase("CO-028")]
    public async Task Create_DuplicateSlug_ReturnsConflict()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var user = await RegisterAsync();

        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto("Another Name", company.Slug, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CO-029")]
    public async Task Create_Anonymous_ReturnsUnauthorized()
    {
        var slug = Unique("anonco-");
        var response = await AnonymousClient().PostAsJsonAsync("/api/companies",
            new CreateCompanyDto($"Company {slug}", slug, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── PUT /api/companies/{id} ───────────────────────────────────────────────

    [Fact, TestCase("CO-030")]
    public async Task Update_ByOwner_UpdatesFields()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}", new
        {
            name = "Updated Name",
            description = "Updated description",
            allowSelfBooking = false,
            requirePrepayment = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<CompanyDto>();
        updated!.Name.Should().Be("Updated Name");
        updated.Description.Should().Be("Updated description");
        updated.AllowSelfBooking.Should().BeFalse();
        updated.RequirePrepayment.Should().BeTrue();
    }

    [Fact, TestCase("CO-031")]
    public async Task Update_ByNonMember_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PutAsJsonAsync($"/api/companies/{company.Id}", new
        {
            name = "Hacked Name"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CO-032")]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var user = await RegisterAsync();

        var response = await AuthedClient(user.Token).PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new
        {
            name = "Doesn't Matter"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/companies/{id}/members ─────────────────────────────────────

    [Fact, TestCase("CO-033")]
    public async Task AddMember_ByOwner_AddsMaster()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var toAdd = await RegisterAsync();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = toAdd.Phone, firstName = toAdd.FirstName, lastName = toAdd.LastName, role = "Master", bio = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var member = await response.Content.ReadFromJsonAsync<MemberDto>();
        member!.Role.Should().Be("Master");
        member.UserId.Should().Be(toAdd.UserId);
    }

    [Fact, TestCase("CO-034")]
    public async Task AddMember_BySuperAdmin_CanAssignCompanyOwnerRole()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();
        var toAdd = await RegisterAsync();

        var response = await AuthedClient(admin.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = toAdd.Phone, firstName = toAdd.FirstName, lastName = toAdd.LastName, role = "CompanyOwner", bio = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var member = await response.Content.ReadFromJsonAsync<MemberDto>();
        member!.Role.Should().Be("CompanyOwner");
    }

    [Fact, TestCase("CO-035")]
    public async Task AddMember_BySuperAdmin_CanAssignSuperAdminRole()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();
        var toAdd = await RegisterAsync();

        var response = await AuthedClient(admin.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = toAdd.Phone, firstName = toAdd.FirstName, lastName = toAdd.LastName, role = "SuperAdmin", bio = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var member = await response.Content.ReadFromJsonAsync<MemberDto>();
        member!.Role.Should().Be("SuperAdmin");
    }

    [Fact, TestCase("CO-036")]
    public async Task AddMember_ByPlainOwnerAssigningSuperAdminRole_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var toAdd = await RegisterAsync();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = toAdd.Phone, firstName = toAdd.FirstName, lastName = toAdd.LastName, role = "SuperAdmin", bio = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CO-037")]
    public async Task AddMember_DuplicateMember_ReturnsConflict()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = master.Phone, firstName = master.FirstName, lastName = master.LastName, role = "Master", bio = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CO-038")]
    public async Task AddMember_UnrelatedUser_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();
        var toAdd = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = toAdd.Phone, firstName = toAdd.FirstName, lastName = toAdd.LastName, role = "Master", bio = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CO-039")]
    public async Task AddMember_UnknownPhone_AutoCreatesUser_AndDerivedPasswordAllowsLogin()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var phone = UniquePhone();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone, firstName = "Alina", lastName = "Newperson", role = "Master", bio = (string?)null, email = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var member = await response.Content.ReadFromJsonAsync<MemberDto>();
        member!.Phone.Should().Be(phone);

        // Derived temporary password: "Sb" + last 6 digits of the phone, right-padded to 8.
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var tail = digits.Length >= 6 ? digits[^6..] : digits;
        var expectedPassword = $"Sb{tail}".PadRight(8, '0');

        var loginResponse = await LoginRawAsync(phone, expectedPassword);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── DELETE /api/companies/{id}/members/{memberId} ────────────────────────

    [Fact, TestCase("CO-040")]
    public async Task RemoveMember_ByOwner_RemovesMaster()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);

        var response = await AuthedClient(owner.Token).DeleteAsync($"/api/companies/{company.Id}/members/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var membersResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/members");
        var members = await membersResponse.Content.ReadFromJsonAsync<List<MemberDto>>();
        members.Should().NotContain(m => m.Id == member.Id);
    }

    [Fact, TestCase("CO-041")]
    public async Task RemoveMember_ByNonOwner_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).DeleteAsync($"/api/companies/{company.Id}/members/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CO-042")]
    public async Task RemoveMember_UnknownMemberId_ReturnsNotFound()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).DeleteAsync($"/api/companies/{company.Id}/members/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/companies/{id}/stats ─────────────────────────────────────────

    [Fact, TestCase("CO-043")]
    public async Task GetStats_AggregatesRevenueAndCounts_ForOwner()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 2500);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = await createResponse.Content.ReadJsonAsync<BookingDto>();

        var completeResponse = await AuthedClient(master.Token).PatchAsync($"/api/bookings/{booking!.Id}/complete", null);
        completeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var from = Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(-15).ToString("o"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(15).ToString("o"));
        var statsResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/stats?from={from}&to={to}");

        statsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var stats = await statsResponse.Content.ReadFromJsonAsync<StatsResult>();
        stats!.TotalRevenue.Should().Be(2500);
        stats.BookingsCount.Should().Be(1);
        stats.CompletedCount.Should().Be(1);
    }

    [Fact, TestCase("CO-044")]
    public async Task GetStats_ByNonOwner_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        var from = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("o"));
        var response = await AuthedClient(stranger.Token).GetAsync($"/api/companies/{company.Id}/stats?from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CO-045")]
    public async Task AddMember_AtTariffMemberLimit_ReturnsPaymentRequired()
    {
        // CreateOwnerWithCompanyAsync already makes the owner a CompanyMember, so a MaxEmployees=1 tariff
        // is already at capacity — adding anyone else must be rejected without touching the existing member.
        var (owner, company) = await CreateOwnerWithCompanyAsync(onlineBooking: false);
        var configId = await CreateTestPlanConfigAsync(maxEmployees: 1);
        await SetSubscriptionAsync(company.Id, configId);
        var toAdd = await RegisterAsync();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = toAdd.Phone, firstName = toAdd.FirstName, lastName = toAdd.LastName, role = "Master", bio = (string?)null });

        response.StatusCode.Should().Be((HttpStatusCode)402);
    }

    [Fact, TestCase("CO-046")]
    public async Task AddMember_UnderTariffMemberLimit_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(onlineBooking: false);
        var configId = await CreateTestPlanConfigAsync(maxEmployees: 2);
        await SetSubscriptionAsync(company.Id, configId);
        var toAdd = await RegisterAsync();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = toAdd.Phone, firstName = toAdd.FirstName, lastName = toAdd.LastName, role = "Master", bio = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CO-047")]
    public async Task AddMember_OnPlanlessAccount_ReturnsPaymentRequired_OwnerOnlySeat()
    {
        // No subscription at all → the restrictive Free baseline: MaxEmployees = 1. The owner already
        // occupies that one seat, so even a first master cannot be added until the account is upgraded.
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var toAdd = await RegisterAsync();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = toAdd.Phone, firstName = toAdd.FirstName, lastName = toAdd.LastName, role = "Master", bio = (string?)null });

        response.StatusCode.Should().Be((HttpStatusCode)402);
    }

    // ── POST /api/companies — branch (MaxCompanies) limit ────────────────────

    [Fact, TestCase("CO-048")]
    public async Task CreateCompany_SecondCompanyOnPlanlessAccount_ReturnsPaymentRequired()
    {
        // Free baseline MaxCompanies = 1: the first company always creates (0 < 1), but a second branch
        // for the same owner is blocked until a plan raises the limit. This is what stops free-company spam.
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto("Second Branch", Unique("branch-"), null, null, null, null));

        response.StatusCode.Should().Be((HttpStatusCode)402);
    }

    [Fact, TestCase("CO-049")]
    public async Task CreateCompany_UpToTariffCompanyLimit_ThenBlocksBeyond()
    {
        // Owner already has 1 company; a plan with MaxCompanies = 2 lets them open exactly one more,
        // and the third is rejected.
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var configId = await CreateTestPlanConfigAsync(maxCompanies: 2);
        await SetSubscriptionAsync(company.Id, configId);

        var second = await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto("Branch 2", Unique("branch-"), null, null, null, null));
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var third = await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto("Branch 3", Unique("branch-"), null, null, null, null));
        third.StatusCode.Should().Be((HttpStatusCode)402);
    }

    // ── Concurrent tariff-limit requests (TOCTOU race) ───────────────────────

    [Fact, TestCase("CO-050")]
    public async Task AddMember_ConcurrentRequestsAtTariffLimit_OnlyFillsRemainingSeats()
    {
        // Regression test for a TOCTOU race: counting current members and comparing to MaxEmployees
        // used to happen without any lock, so concurrent requests could all read the same pre-insert
        // count, all pass the check, and all insert — leaving the company over its seat limit. The
        // owner already occupies one seat; MaxEmployees=2 leaves exactly one more free.
        var (owner, company) = await CreateOwnerWithCompanyAsync(onlineBooking: false);
        var configId = await CreateTestPlanConfigAsync(maxEmployees: 2);
        await SetSubscriptionAsync(company.Id, configId);

        var candidates = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RegisterAsync()));
        var responses = await Task.WhenAll(candidates.Select(c =>
            AuthedClient(owner.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
                new { phone = c.Phone, firstName = c.FirstName, lastName = c.LastName, role = "Master", bio = (string?)null })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == (HttpStatusCode)402).Should().Be(4);
    }

    [Fact, TestCase("CO-051")]
    public async Task CreateCompany_ConcurrentRequestsAtTariffLimit_OnlyOneNewCompanySucceeds()
    {
        // Same TOCTOU race as CO-050, but for the per-owner MaxCompanies branch limit. The owner
        // already has 1 company; MaxCompanies=2 leaves room for exactly one more.
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var configId = await CreateTestPlanConfigAsync(maxCompanies: 2);
        await SetSubscriptionAsync(company.Id, configId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
                new CreateCompanyDto($"Branch {Unique("")}", Unique("branch-"), null, null, null, null))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == (HttpStatusCode)402).Should().Be(4);
    }

    // ── Raw tariff capability flags (PlanAllows*, MaxEmployees) ──────────────
    //
    // Unlike OnlineBookingEnabled/PrepaymentEnabled/PublicListingEnabled (which fold in the owner's own
    // toggle), these fields must reflect the tariff alone — the owner-facing settings UI greys out a
    // checkbox the tariff blocks entirely, which requires knowing that independent of whatever the
    // owner's stored toggle currently is.

    [Fact, TestCase("CO-063")]
    public async Task GetMy_OnFreeBaseline_ExposesRestrictivePlanCapabilityFlags()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var response = await AuthedClient(owner.Token).GetAsync("/api/companies/my");
        var companies = (await response.Content.ReadFromJsonAsync<List<CompanyDto>>())!;
        var dto = companies.Single(c => c.Id == company.Id);

        dto.PlanAllowsOnlineBooking.Should().BeFalse();
        dto.PlanAllowsOnlinePayment.Should().BeFalse();
        dto.PlanAllowsPublicListing.Should().BeTrue();
        dto.MaxEmployees.Should().Be(1);
    }

    [Fact, TestCase("CO-064")]
    public async Task GetMy_WithFullFeaturePlan_ExposesPermissivePlanCapabilityFlags()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetSubscriptionAsync(company.Id);

        var response = await AuthedClient(owner.Token).GetAsync("/api/companies/my");
        var companies = (await response.Content.ReadFromJsonAsync<List<CompanyDto>>())!;
        var dto = companies.Single(c => c.Id == company.Id);

        dto.PlanAllowsOnlineBooking.Should().BeTrue();
        dto.PlanAllowsOnlinePayment.Should().BeTrue();
        dto.PlanAllowsPublicListing.Should().BeTrue();
        dto.MaxEmployees.Should().BeNull();
    }

    [Fact, TestCase("CO-065")]
    public async Task GetMy_PlanAllowsOnlinePayment_StaysTrue_EvenWhileOwnersOwnToggleIsOff()
    {
        // The whole point of this field: PlanAllowsOnlinePayment describes what the tariff permits,
        // not the AND'd effective state — it must stay true here even though RequirePrepayment (and
        // therefore PrepaymentEnabled) is false.
        var (owner, company) = await CreateOwnerWithCompanyAsync(requirePrepayment: false);
        await SetSubscriptionAsync(company.Id);

        var response = await AuthedClient(owner.Token).GetAsync("/api/companies/my");
        var companies = (await response.Content.ReadFromJsonAsync<List<CompanyDto>>())!;
        var dto = companies.Single(c => c.Id == company.Id);

        dto.RequirePrepayment.Should().BeFalse();
        dto.PrepaymentEnabled.Should().BeFalse();
        dto.PlanAllowsOnlinePayment.Should().BeTrue();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<MemberDto> GetMemberAsync(string ownerToken, Guid companyId, string userId)
    {
        var response = await AuthedClient(ownerToken).GetAsync($"/api/companies/{companyId}/members");
        var members = await response.Content.ReadFromJsonAsync<List<MemberDto>>();
        return members!.Single(m => m.UserId == userId);
    }

    private record StatsResult(decimal TotalRevenue, int BookingsCount, int CompletedCount, int CancelledCount, int NewClientsCount);
}
