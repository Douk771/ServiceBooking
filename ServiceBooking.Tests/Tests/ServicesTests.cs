using System.Net;
using System.Net.Http.Json;
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
    public async Task Create_ByMaster_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var response = await AuthedClient(master.Token).PostAsJsonAsync("/api/services",
            new CreateServiceDto(company.Id, "Manicure", null, 30, 500, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<ServiceDto>();
        created!.Name.Should().Be("Manicure");
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
    public async Task Update_ByMaster_UpdatesFields()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);

        var response = await AuthedClient(master.Token).PutAsJsonAsync($"/api/services/{service.Id}",
            new CreateServiceDto(company.Id, "Master Updated", null, 20, 300, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ServiceDto>();
        updated!.Name.Should().Be("Master Updated");
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
}
