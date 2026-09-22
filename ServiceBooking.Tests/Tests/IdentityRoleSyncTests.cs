using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>US-46 (SPEC.md §5.6, ARCHITECTURE.md §8, API_CONTRACT — role recomputation on membership change).</summary>
public class IdentityRoleSyncTests(ApiDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("SEC-050")]
    public async Task RemovingSoleMembership_RevokesTheRole_Immediately()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        // Master-gated endpoint works before removal.
        (await AuthedClient(master.Token).GetAsync($"/api/bookings/master?date={NextWeekday():yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var members = await (await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/members"))
            .Content.ReadJsonAsync<List<MemberDto>>();
        var memberId = members!.Single(m => m.UserId == master.UserId).Id;

        var removeResponse = await AuthedClient(owner.Token).DeleteAsync($"/api/companies/{company.Id}/members/{memberId}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Roles are re-read from the DB on every request (OnTokenValidated) — no new token needed.
        var afterResponse = await AuthedClient(master.Token).GetAsync($"/api/bookings/master?date={NextWeekday():yyyy-MM-dd}");
        afterResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("SEC-051")]
    public async Task RemovingFromOneOfTwoCompanies_KeepsAccessViaTheOther()
    {
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(ownerA.Token, companyA.Id);

        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var addResponse = await AuthedClient(ownerB.Token).PostAsJsonAsync($"/api/companies/{companyB.Id}/members",
            new { phone = master.Phone, firstName = master.FirstName, lastName = master.LastName, role = "Master", bio = (string?)null, email = (string?)null });
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var membersA = await (await AuthedClient(ownerA.Token).GetAsync($"/api/companies/{companyA.Id}/members"))
            .Content.ReadJsonAsync<List<MemberDto>>();
        var memberIdA = membersA!.Single(m => m.UserId == master.UserId).Id;

        (await AuthedClient(ownerA.Token).DeleteAsync($"/api/companies/{companyA.Id}/members/{memberIdA}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Still Master via companyB.
        (await AuthedClient(master.Token).GetAsync($"/api/bookings/master?date={NextWeekday():yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("SEC-052")]
    public async Task ChangingCompanyOwner_TransfersTheRole_ToBothParties()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var newOwner = await RegisterAsync();

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/companies/{company.Id}/owner",
            new UpdateCompanyOwnerDto(newOwner.UserId));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // New owner gains CompanyOwner-gated access (e.g. members listing).
        (await AuthedClient(newOwner.Token).GetAsync($"/api/companies/{company.Id}/members"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Old owner is demoted to Master on this company's CompanyMember row (ARCHITECTURE.md §19.3),
        // so a strictly CompanyOwner/SuperAdmin-gated action they no longer have any other claim to
        // (e.g. deleting members) is refused — while a Master-level one (working the schedule) still works.
        var membersAfter = await (await AuthedClient(newOwner.Token).GetAsync($"/api/companies/{company.Id}/members"))
            .Content.ReadJsonAsync<List<MemberDto>>();
        membersAfter.Should().Contain(m => m.UserId == owner.UserId && m.Role == "Master");
    }

    [Fact, TestCase("SEC-053")]
    public async Task ClientRole_IsNeverRemoved_EvenAfterLeavingEveryCompany()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var members = await (await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/members"))
            .Content.ReadJsonAsync<List<MemberDto>>();
        var memberId = members!.Single(m => m.UserId == master.UserId).Id;
        (await AuthedClient(owner.Token).DeleteAsync($"/api/companies/{company.Id}/members/{memberId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Every account keeps the base "Client" role regardless of company membership — a Client-gated
        // endpoint (their own booking history) still works after being removed from every company.
        (await AuthedClient(master.Token).GetAsync("/api/bookings/client"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
