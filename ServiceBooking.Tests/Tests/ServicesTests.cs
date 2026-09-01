using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class ServicesTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── GET /api/services?companyId= ─────────────────────────────────────────

    [Fact, TestCase("SVC-001")]
    public async Task GetByCompany_OnlyReturnsActiveServices()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var activeService = await CreateServiceAsync(owner.Token, company.Id);
        var deletedService = await CreateServiceAsync(owner.Token, company.Id);

        var deleteResponse = await AuthedClient(owner.Token).DeleteAsync($"/api/services/{deletedService.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await AnonymousClient().GetAsync($"/api/services?companyId={company.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var services = await response.Content.ReadFromJsonAsync<List<ServiceDto>>();
        services.Should().Contain(s => s.Id == activeService.Id);
        services.Should().NotContain(s => s.Id == deletedService.Id);
    }

    // ── POST /api/services ────────────────────────────────────────────────────

    [Fact, TestCase("SVC-002")]
    public async Task Create_ByOwner_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/services",
            new CreateServiceDto(company.Id, "Haircut", "A basic haircut", 45, 800, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<ServiceDto>();
        created!.CompanyId.Should().Be(company.Id);
        created.Name.Should().Be("Haircut");
        created.DurationMinutes.Should().Be(45);
        created.Price.Should().Be(800);
    }

    [Fact, TestCase("SVC-003")]
    public async Task Create_ByMaster_ReturnsForbidden()
    {
        // US-09 (decision Q1): only the CompanyOwner manages the service catalog now — a Master reads
        // services (GetByCompany stays public/unrestricted, see SVC-014) but no longer edits them.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/services",
            new CreateServiceDto(company.Id, "Manicure", null, 30, 500, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("SVC-004")]
    public async Task Create_ByUnrelatedUser_ReturnsForbidden()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PostAsJsonAsync("/api/services",
            new CreateServiceDto(company.Id, "Unauthorized Service", null, 30, 500, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("SVC-005")]
    public async Task Create_Anonymous_ReturnsUnauthorized()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();

        var response = await AnonymousClient().PostAsJsonAsync("/api/services",
            new CreateServiceDto(company.Id, "Guest Service", null, 30, 500, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── PUT /api/services/{id} ────────────────────────────────────────────────

    [Fact, TestCase("SVC-006")]
    public async Task Update_ByOwner_UpdatesFields()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var service = await CreateServiceAsync(owner.Token, company.Id, name: "Old Name", durationMinutes: 30, price: 500);

        var response = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/services/{service.Id}",
            new CreateServiceDto(company.Id, "New Name", "New description", 60, 1200, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ServiceDto>();
        updated!.Name.Should().Be("New Name");
        updated.Description.Should().Be("New description");
        updated.DurationMinutes.Should().Be(60);
        updated.Price.Should().Be(1200);
    }

    [Fact, TestCase("SVC-007")]
    public async Task Update_ByMaster_ReturnsForbidden_FieldsUnchanged()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, name: "Original", price: 500);

        var response = await AuthedClient(master.Token).PutAsJsonAsync($"/api/services/{service.Id}",
            new CreateServiceDto(company.Id, "Master Updated", null, 20, 300, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var listResponse = await AnonymousClient().GetAsync($"/api/services?companyId={company.Id}");
        var services = await listResponse.Content.ReadFromJsonAsync<List<ServiceDto>>();
        var unchanged = services.Should().ContainSingle(s => s.Id == service.Id).Subject;
        unchanged.Name.Should().Be("Original");
        unchanged.Price.Should().Be(500);
    }

    [Fact, TestCase("SVC-008")]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var user = await RegisterAsync();

        var response = await AuthedClient(user.Token).PutAsJsonAsync($"/api/services/{Guid.NewGuid()}",
            new CreateServiceDto(Guid.NewGuid(), "Doesn't Matter", null, 30, 500, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("SVC-009")]
    public async Task Update_ByUnrelatedUser_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).PutAsJsonAsync($"/api/services/{service.Id}",
            new CreateServiceDto(company.Id, "Hijacked", null, 30, 500, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── DELETE /api/services/{id} ─────────────────────────────────────────────

    [Fact, TestCase("SVC-010")]
    public async Task Delete_SoftDeletes_AndDisappearsFromPublicList()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var service = await CreateServiceAsync(owner.Token, company.Id);

        var response = await AuthedClient(owner.Token).DeleteAsync($"/api/services/{service.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await AnonymousClient().GetAsync($"/api/services?companyId={company.Id}");
        var services = await listResponse.Content.ReadFromJsonAsync<List<ServiceDto>>();
        services.Should().NotContain(s => s.Id == service.Id);
    }

    [Fact, TestCase("SVC-011")]
    public async Task Delete_ByUnrelatedUser_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).DeleteAsync($"/api/services/{service.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("SVC-012")]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        var user = await RegisterAsync();

        var response = await AuthedClient(user.Token).DeleteAsync($"/api/services/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("SVC-013")]
    public async Task Delete_ByMaster_ReturnsForbidden_ServiceStaysActive()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);

        var response = await AuthedClient(master.Token).DeleteAsync($"/api/services/{service.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var listResponse = await AnonymousClient().GetAsync($"/api/services?companyId={company.Id}");
        var services = await listResponse.Content.ReadFromJsonAsync<List<ServiceDto>>();
        services.Should().Contain(s => s.Id == service.Id);
    }

    [Fact, TestCase("SVC-014")]
    public async Task GetByCompany_ByMaster_StillReturnsServices()
    {
        // GET stays unrestricted for a Master — required by ManualBookingModal, which lets a master
        // pick a service when recording a walk-in (ARCHITECTURE.md §11 T-B7 "Готово, когда").
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);

        var response = await AuthedClient(master.Token).GetAsync($"/api/services?companyId={company.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var services = await response.Content.ReadFromJsonAsync<List<ServiceDto>>();
        services.Should().Contain(s => s.Id == service.Id);
    }

    [Fact, TestCase("SVC-015")]
    public async Task Create_WithZeroDuration_ReturnsBadRequest()
    {
        // Reproduces audit hypothesis E1 before the fix: a zero-length service degenerates the
        // conflict-check interval (b.StartTime < slotEnd && b.EndTime > startTime is never true for a
        // zero-width slot), allowing unlimited overlapping bookings on the same time. [Range(1, 1440)]
        // on CreateServiceDto.DurationMinutes closes this at the validation layer.
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/services",
            new CreateServiceDto(company.Id, "Zero Duration", null, 0, 500, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("SVC-016")]
    public async Task Create_BySuperAdmin_UnknownCompanyId_Returns500ProblemJson()
    {
        // Deliberately left uncovered by US-09 (out of scope — that story is only about the Master
        // role): SuperAdmin bypasses CanManageCompany unconditionally, and ServicesController.Create
        // never checks the company exists, so this reaches SaveChangesAsync and violates the
        // Services.CompanyId -> Companies FK. Used here as a stable trigger for the cycle's new global
        // exception handler (T-B14, ARCHITECTURE.md §6): a 500 must now come back as
        // application/problem+json with a non-empty traceId, not an empty body / developer page.
        var admin = await LoginAsSuperAdminAsync();

        var response = await AuthedClient(admin.Token).PostAsJsonAsync("/api/services",
            new CreateServiceDto(Guid.NewGuid(), "Ghost Company Service", null, 30, 500, null));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }
}
