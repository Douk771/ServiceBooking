using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services;
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
        // A genuinely decodable JPEG (US-19): the new loader checks the signature AND decodes the file,
        // so bare magic-number bytes (accepted pre-cycle-B, when only Content-Type was trusted) are no
        // longer enough — see CO-0xx below for that regression, which is the whole point of this cycle.
        var fileContent = new ByteArrayContent(TestImages.SolidJpeg());
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

    [Fact, TestCase("CO-074")]
    public async Task UploadLogo_ContentTypeClaimsImage_ButBytesAreNot_ReturnsBadRequest()
    {
        // US-19 p.1 / US-25 p.6 regression guard: before this cycle, only Content-Type was checked
        // (CompaniesController.cs:271-281), so a non-image file with a spoofed image Content-Type was
        // accepted. The new loader decides purely from the byte signature — plain text with a claimed
        // "image/jpeg" Content-Type is rejected the same way a ".jpg" filename would be (MC-2xx shares
        // this exact scenario for client-note photos).
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("this is not an image at all"u8.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "logo.jpg");

        var response = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/logo", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Unsupported image type");
    }

    [Fact, TestCase("CO-075")]
    public async Task UploadLogo_RealSignatureButUndecodableBytes_ReturnsBadRequest()
    {
        // Two independent checks (ARCHITECTURE.md §4.1 steps 5 and 8): a file whose first bytes ARE a
        // real JPEG signature but whose content isn't a decodable image (truncated/corrupt) is rejected
        // by the decoder, one layer past the signature check that CO-063 exercises.
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9]); // JPEG magic bytes, no real scan data
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "logo.jpg");

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
            var fileContent = new ByteArrayContent(TestImages.SolidPng());
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

    // ── GET /api/companies/{id}/photo-usage ─────────────────────────────────

    [Fact, TestCase("CO-076")]
    public async Task GetPhotoUsage_ByOwner_ReturnsQuotaAndZeroUsageForNewCompany()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/photo-usage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadJsonAsync<CompanyPhotoUsageDto>();
        dto!.PhotoCount.Should().Be(0);
        dto.UsedBytes.Should().Be(0);
    }

    [Fact, TestCase("CO-077")]
    public async Task GetPhotoUsage_ByOutsider_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).GetAsync($"/api/companies/{company.Id}/photo-usage");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CO-078")]
    public async Task GetPhotoUsage_BySuperAdmin_ReturnsCountersWithoutError()
    {
        // Decision Q5: SuperAdmin gets numbers, never content — this is the one endpoint where that
        // shows up as a 200, contrasted with GET /api/client-notes/photos/{id}'s 403 for the same role.
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();

        var response = await AuthedClient(admin.Token).GetAsync($"/api/companies/{company.Id}/photo-usage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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

    [Fact, TestCase("CO-067")]
    public async Task GetMasters_ExcludesClientRoleMembers_KeepsMasterAndOwner()
    {
        // A Client-role CompanyMember row exists for a company's own customers, never for staff — it
        // must never surface in the public "book a master" picker (US-12, decision Q6).
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var clientUser = await RegisterAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
            db.CompanyMembers.Add(new ServiceBooking.Core.Entities.CompanyMember
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, UserId = clientUser.UserId, Role = UserRole.Client
            });
            await db.SaveChangesAsync();
        }

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Id}/masters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var masters = await response.Content.ReadFromJsonAsync<List<MasterPublicDto>>();
        masters.Should().Contain(m => m.UserId == master.UserId);
        masters.Should().Contain(m => m.UserId == owner.UserId);
        masters.Should().NotContain(m => m.UserId == clientUser.UserId);
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

    [Fact, TestCase("CO-069")]
    public async Task UpdateMemberServices_ServiceFromAnotherCompany_ReturnsBadRequest_LeavesLinksUnchanged()
    {
        // Audit B2: without this check an owner could attach their master to a service that belongs to
        // someone else's company. Also verifies the write is atomic: an existing valid link must survive
        // a rejected request untouched.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var ownService = await CreateServiceAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);

        var setup = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/services", new List<Guid> { ownService.Id });
        setup.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var (otherOwner, otherCompany) = await CreateOwnerWithCompanyAsync();
        var foreignService = await CreateServiceAsync(otherOwner.Token, otherCompany.Id);

        var response = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/services", new List<Guid> { foreignService.Id });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var updated = await GetMemberAsync(owner.Token, company.Id, master.UserId);
        updated.ServiceIds.Should().ContainSingle(id => id == ownService.Id);
        updated.ServiceIds.Should().NotContain(foreignService.Id);
    }

    [Fact, TestCase("CO-070")]
    public async Task UpdateMemberServices_UnknownServiceId_ReturnsBadRequest_NotServerError()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);

        var response = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/services", new List<Guid> { Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── PUT /api/companies/{id}/members/{memberId}/commission ────────────────

    [Fact, TestCase("CO-021")]
    public async Task UpdateMemberCommission_ByOwner_PersistsInMembers()
    {
        // Commission is per-membership (CompanyMember.CommissionPercent) since cycle A; cycle B (US-22)
        // removes the account-level ProfileDto.CommissionPercent field entirely, since it never
        // reflected this value and the UI never showed it — so GET /api/profile is no longer part of
        // this test at all.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var member = await GetMemberAsync(owner.Token, company.Id, master.UserId);

        var response = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/commission", new { commissionPercent = 35 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedMember = await GetMemberAsync(owner.Token, company.Id, master.UserId);
        updatedMember.CommissionPercent.Should().Be(35);
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

    [Fact, TestCase("CO-068")]
    public async Task UpdateMemberCommission_ForMoonlightingMaster_DoesNotLeakIntoOtherCompany()
    {
        // US-15 (B1): commission is per-membership now — a master working at two companies can have a
        // different rate at each, and setting it in company A must not change what company B sees.
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(ownerA.Token, companyA.Id);
        await AddMasterAsync(ownerB.Token, companyB.Id); // registers a different master by default...

        // ...so explicitly add the SAME master (by phone) to company B as well.
        var addToB = await AuthedClient(ownerB.Token).PostAsJsonAsync($"/api/companies/{companyB.Id}/members",
            new { phone = master.Phone, firstName = master.FirstName, lastName = master.LastName,
                  role = "Master", bio = (string?)null, email = (string?)null });
        addToB.StatusCode.Should().Be(HttpStatusCode.OK);

        var memberInA = await GetMemberAsync(ownerA.Token, companyA.Id, master.UserId);
        var setCommission = await AuthedClient(ownerA.Token).PutAsJsonAsync(
            $"/api/companies/{companyA.Id}/members/{memberInA.Id}/commission", new { commissionPercent = 40 });
        setCommission.StatusCode.Should().Be(HttpStatusCode.OK);

        var memberInB = await GetMemberAsync(ownerB.Token, companyB.Id, master.UserId);
        memberInB.CommissionPercent.Should().Be(0);
    }

    // ── POST /api/companies ───────────────────────────────────────────────────

    [Fact, TestCase("CO-027")]
    public async Task Create_ValidData_CreatesCompanyAndMakesCallerOwner()
    {
        var user = await RegisterAsync();
        var slug = Unique("newco-");
        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto($"Company {slug}", slug, null, null, null, null, await AnyCityIdAsync(), null));

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
            new CreateCompanyDto("Another Name", company.Slug, null, null, null, null, await AnyCityIdAsync(), null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CO-029")]
    public async Task Create_Anonymous_ReturnsUnauthorized()
    {
        var slug = Unique("anonco-");
        var response = await AnonymousClient().PostAsJsonAsync("/api/companies",
            new CreateCompanyDto($"Company {slug}", slug, null, null, null, null, await AnyCityIdAsync(), null));

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

    [Fact, TestCase("CO-066")]
    public async Task AddMember_BySuperAdmin_UnknownRoleName_ReturnsBadRequest_NotServerError()
    {
        // Audit D3: SuperAdmin's CanAssignRole accepted any string sight unseen, so a typo'd role name
        // used to sail through to Enum.Parse<UserRole> and blow up with an unhandled 500.
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();
        var toAdd = await RegisterAsync();

        var response = await AuthedClient(admin.Token).PostAsJsonAsync($"/api/companies/{company.Id}/members",
            new { phone = toAdd.Phone, firstName = toAdd.FirstName, lastName = toAdd.LastName, role = "Bogus", bio = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
        // US-26: canonical (digits-only) phone is what's stored/returned.
        member!.Phone.Should().Be(PhoneNormalizer.Normalize(phone));

        // Derived temporary password: "Sb" + last 6 digits of the CANONICAL phone, right-padded to 8
        // (ARCHITECTURE.md §11.2 — for a "+7..." input like UniquePhone() this is the same tail as
        // before, but it is now explicitly the canonical digits, not whatever the caller sent).
        var digits = PhoneNormalizer.Normalize(phone);
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
        // T-B12 (US-18, decision Q11): GetStats filters by the VISIT date (Booking.Date), not by
        // CreatedAt — so the window below spans the visit date itself, not "now" (the row is created
        // "now" during this test, but the visit is NextWeekday(), up to a week out).
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

        var from = Uri.EscapeDataString(date.ToDateTime(TimeOnly.MinValue).ToString("o"));
        var to = Uri.EscapeDataString(date.ToDateTime(TimeOnly.MinValue).ToString("o"));
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

    [Fact, TestCase("CO-071")]
    public async Task GetStats_FiltersByVisitDate_MatchesReportsMastersForSamePeriod()
    {
        // T-B12 (US-18, decision Q11): the row is created "now" (CreatedAt), but the visit is
        // NextWeekday() — days later (Booking.Date). GetStats must land it in the report for the VISIT
        // date, and agree with GET /api/reports/masters (which already filters by Date), not the date
        // the row happened to be created on.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, price: 1500);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var clientUser = await RegisterAsync();
        var createResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        var booking = (await createResponse.Content.ReadJsonAsync<BookingDto>())!;
        (await AuthedClient(master.Token).PatchAsync($"/api/bookings/{booking.Id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var from = Uri.EscapeDataString(date.ToDateTime(TimeOnly.MinValue).ToString("o"));
        var to = Uri.EscapeDataString(date.ToDateTime(TimeOnly.MinValue).ToString("o"));
        var statsResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/stats?from={from}&to={to}");
        var stats = await statsResponse.Content.ReadFromJsonAsync<StatsResult>();

        var reportResponse = await AuthedClient(owner.Token).GetAsync(
            $"/api/reports/masters?companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
        var report = await reportResponse.Content.ReadJsonAsync<List<MasterReportDto>>();

        stats!.TotalRevenue.Should().Be(1500);
        report.Should().ContainSingle(r => r.MasterId == master.UserId && r.TotalAmount == 1500);
    }

    [Fact, TestCase("CO-072")]
    public async Task GetStats_MissingFromOrTo_ReturnsBadRequest()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/stats");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CO-073")]
    public async Task GetStats_ToBeforeFrom_ReturnsBadRequest()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var from = Uri.EscapeDataString(DateTime.UtcNow.ToString("o"));
        var to = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("o"));
        var response = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/stats?from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
            new CreateCompanyDto("Second Branch", Unique("branch-"), null, null, null, null, await AnyCityIdAsync(), null));

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
            new CreateCompanyDto("Branch 2", Unique("branch-"), null, null, null, null, await AnyCityIdAsync(), null));
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var third = await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto("Branch 3", Unique("branch-"), null, null, null, null, await AnyCityIdAsync(), null));
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

        var cityId = await AnyCityIdAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
                new CreateCompanyDto($"Branch {Unique("")}", Unique("branch-"), null, null, null, null, cityId, null))));

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

    // ── US-62: "provides services" (ARCHITECTURE_CYCLE6.md §40.3, SPEC.md US-62) ──────────

    [Fact, TestCase("CO-079")]
    public async Task DisablingOwnerProvidesServices_RemovesThemFromPublicMastersListAndCount()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var member = await GetMemberAsync(owner.Token, company.Id, owner.UserId);
        // SPEC.md US-62 "low-risk assumption": existing/new owners default to enabled, so rollout
        // doesn't silently hide a specialist who actually works in the chair.
        member.ProvidesServices.Should().BeTrue();

        var before = await AnonymousClient().GetFromJsonAsync<List<MasterPublicDto>>($"/api/companies/{company.Id}/masters");
        before!.Should().ContainSingle(m => m.UserId == owner.UserId);

        var toggle = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/provides-services",
            new { providesServices = false, confirm = false });
        toggle.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Not just "excluded from the list" — the count of "how many active specialists" (US-64) is the
        // same list, so a zero-length result here IS the "no specialists" count.
        var after = await AnonymousClient().GetFromJsonAsync<List<MasterPublicDto>>($"/api/companies/{company.Id}/masters");
        after.Should().BeEmpty("a specialist with ProvidesServices=false must not be offered to clients, " +
            "nor counted toward how many active specialists a company has (US-64)");

        // The flag hides the person from the booking picker only — it must not evict them from the
        // company's own member list.
        var members = await AuthedClient(owner.Token).GetFromJsonAsync<List<MemberDto>>($"/api/companies/{company.Id}/members");
        members!.Should().ContainSingle(m => m.UserId == owner.UserId);
    }

    [Fact, TestCase("CO-080")]
    public async Task DisablingProvidesServices_WithFutureBookings_RequiresConfirm_AndLeavesBookingsIntact()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        // The owner is the only specialist and provides services (default) — book a future visit
        // directly with them so the "future bookings" guard has something to count.
        var member = await GetMemberAsync(owner.Token, company.Id, owner.UserId);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, owner.UserId, company.Id, date);
        var clientUser = await RegisterAsync();
        var bookingResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, owner.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        bookingResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await bookingResponse.Content.ReadJsonAsync<BookingDto>())!;

        // Without confirm: 409, and the count is visible to the owner in the message.
        var withoutConfirm = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/provides-services",
            new { providesServices = false, confirm = false });
        withoutConfirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var message = await withoutConfirm.Content.ReadAsStringAsync();
        message.Should().Contain("1", "the warning must tell the owner how many future bookings are at stake");

        // The booking survives untouched — turning the flag off must never silently drop it.
        var stillThere = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking.Id}");
        stillThere.StatusCode.Should().Be(HttpStatusCode.OK);
        var stillBooking = (await stillThere.Content.ReadJsonAsync<BookingDto>())!;
        stillBooking.Status.Should().Be(BookingStatus.Confirmed);

        // With confirm=true: succeeds, and the booking is STILL untouched (only visibility changes).
        var withConfirm = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/members/{member.Id}/provides-services",
            new { providesServices = false, confirm = true });
        withConfirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterConfirm = await AuthedClient(owner.Token).GetAsync($"/api/bookings/{booking.Id}");
        var afterConfirmBooking = (await afterConfirm.Content.ReadJsonAsync<BookingDto>())!;
        afterConfirmBooking.Status.Should().Be(BookingStatus.Confirmed);
        afterConfirmBooking.MasterId.Should().Be(owner.UserId);
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
