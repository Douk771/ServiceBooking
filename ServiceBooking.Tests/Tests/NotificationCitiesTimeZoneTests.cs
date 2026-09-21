using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 4 (SPEC.md §6.2/§15.1, API_CONTRACT_CYCLE4.md §31) — city lookup and the breaking change to
/// company creation: <c>cityId</c> is now required, and Barnaul/Novosibirsk are DIFFERENT IANA zones
/// despite sharing today's UTC+7 offset (the exact confusion §34.2's backfill comment warns against).
/// </summary>
public class NotificationCitiesTimeZoneTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("NTF-G001")]
    public async Task GetCities_Search_ReturnsBarnaulWithItsOwnZone()
    {
        var response = await AnonymousClient().GetAsync("/api/cities?search=%D0%B1%D0%B0%D1%80"); // "бар"
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("Барнаул");
        json.Should().Contain("Asia/Barnaul");
    }

    [Fact, TestCase("NTF-G002")]
    public async Task GetCities_BarnaulAndNovosibirsk_HaveDifferentTimeZoneIds()
    {
        var barnaul = await AnonymousClient().GetAsync("/api/cities?search=%D0%B1%D0%B0%D1%80%D0%BD%D0%B0%D1%83%D0%BB"); // "барнаул"
        var novosibirsk = await AnonymousClient().GetAsync("/api/cities?search=%D0%BD%D0%BE%D0%B2%D0%BE%D1%81%D0%B8%D0%B1%D0%B8%D1%80%D1%81%D0%BA"); // "новосибирск"

        var barnaulJson = await barnaul.Content.ReadAsStringAsync();
        var novosibirskJson = await novosibirsk.Content.ReadAsStringAsync();

        // Both share today's UTC+7 offset — the exact confusion the migration's own backfill comment
        // warns against (ARCHITECTURE_CYCLE4.md §34.2). Asserting BOTH literal strings are present,
        // via independent searches, catches a regression that collapsed them onto the same zone id.
        barnaulJson.Should().Contain("Asia/Barnaul");
        novosibirskJson.Should().Contain("Asia/Novosibirsk");
        novosibirskJson.Should().NotContain("Asia/Barnaul");
    }

    [Fact, TestCase("NTF-G003")]
    public async Task CreateCompany_WithoutCityId_Returns400()
    {
        var owner = await RegisterAsync();
        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto("No City Co", Unique("nocity-"), null, null, null, null, null, null, true));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "API_CONTRACT_CYCLE4.md §31.2: cityId is now required — this is the cycle's one breaking change");
    }

    [Fact, TestCase("NTF-G004")]
    public async Task CreateCompany_UnknownCityId_Returns400()
    {
        var owner = await RegisterAsync();
        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto("Bad City Co", Unique("badcity-"), null, null, null, null, 999999, null, true));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("NTF-G005")]
    public async Task CreateCompany_UnknownTimeZoneId_Returns400()
    {
        var owner = await RegisterAsync();
        var cityId = await AnyCityIdAsync();
        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto("Bad TZ Co", Unique("badtz-"), null, null, null, null, cityId, "Not/A_Real_Zone", true));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("NTF-G006")]
    public async Task CreateCompany_WithBarnaul_CarriesCorrectZoneOnDto()
    {
        var owner = await RegisterAsync();
        var barnaulId = await BarnaulCityIdAsync();
        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto("Barnaul Co", Unique("barnaul-"), null, null, null, null, barnaulId, null, true));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var company = (await response.Content.ReadJsonAsync<CompanyDto>())!;
        company.CityId.Should().Be(barnaulId);
        company.TimeZoneId.Should().Be("Asia/Barnaul");
        company.UtcOffsetMinutes.Should().Be(420);
    }

    private async Task<int> BarnaulCityIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Cities.Where(c => c.Name == "Барнаул").Select(c => c.Id).FirstAsync();
    }
}
