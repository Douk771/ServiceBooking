using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 15, Block B (US-15-05/US-15-06, T-B1) — PUT /api/companies/{id} is the single write path
/// for the owner-supplied Яндекс Карты / 2ГИС links, and GET /api/companies/{slug} is the single public
/// read path. Written against SPEC.md §5 (US-15-05/06), §0-bis П1/П2 and
/// API_CONTRACT_CYCLE15.md/ARCHITECTURE_CYCLE15.md §253/§283/§284, not against the implementation.
/// </summary>
public class Cycle15MapLinksTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // 0-bis П2 — real examples that MUST be accepted byte-for-byte, including the raw (non-normalized)
    // percent-escaped query string.
    private const string RealYandexUrl =
        "https://yandex.ru/maps/org/syrovarnya/11766054863/?ll=83.795110%2C53.330510&z=17";
    private const string RealTwoGisUrl =
        "https://2gis.ru/barnaul/firm/563478234628539/83.795014%2C53.330486?m=83.795954%2C53.330025%2F17.89";

    [Fact, TestCase("CY15-B1-01")]
    public async Task Update_BothRealLinks_PersistedByteForByte_And_VisibleAnonymouslyOnPublicCard()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var update = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}", new
        {
            yandexMapsUrl = RealYandexUrl,
            twoGisUrl = RealTwoGisUrl,
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await update.Content.ReadJsonAsync<CompanyDto>())!;
        updated.YandexMapsUrl.Should().Be(RealYandexUrl, "the raw string must be stored byte-for-byte, never re-normalized");
        updated.TwoGisUrl.Should().Be(RealTwoGisUrl);

        // §253.5 — the public card is served with zero extra requests: the same GET already returns
        // the links, for an ANONYMOUS caller.
        var publicView = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        publicView.StatusCode.Should().Be(HttpStatusCode.OK);
        var publicDto = (await publicView.Content.ReadJsonAsync<CompanyDto>())!;
        publicDto.YandexMapsUrl.Should().Be(RealYandexUrl);
        publicDto.TwoGisUrl.Should().Be(RealTwoGisUrl);
    }

    [Fact, TestCase("CY15-B1-02")]
    public async Task Update_EmptyString_ClearsField_ReturnsToNoLink()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        (await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { yandexMapsUrl = RealYandexUrl, twoGisUrl = RealTwoGisUrl })).EnsureSuccessStatusCode();

        // §283 three-state semantics: "" clears (obtainable — the setting is reversible), null/omitted
        // leaves untouched.
        var clear = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { twoGisUrl = "" });
        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterClear = (await clear.Content.ReadJsonAsync<CompanyDto>())!;
        afterClear.TwoGisUrl.Should().BeNull("empty string clears the field");
        afterClear.YandexMapsUrl.Should().Be(RealYandexUrl, "the field not sent must not be touched");
    }

    [Theory, TestCase("CY15-B1-03")]
    [InlineData("yandex.ru/maps/org/syrovarnya/11766054863/")] // no scheme
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("https://yandex.evil.com/maps/org/1")] // R10: not a real Yandex TLD
    [InlineData("https://clck.ru/abc")] // 0-bis: shorteners rejected
    [InlineData("http://yandex.ru/maps/org/1")] // http, not https
    public async Task Update_GarbageYandexLink_RejectedWithoutSaving(string garbage)
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { yandexMapsUrl = garbage });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace("400 body must explain which link is expected");

        // Nothing saved — the field is still empty, not the garbage value.
        var check = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        var dto = (await check.Content.ReadJsonAsync<CompanyDto>())!;
        dto.YandexMapsUrl.Should().BeNull();
    }

    [Fact, TestCase("CY15-B1-04")]
    public async Task Update_LinkLongerThan500_Rejected()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var tooLong = "https://yandex.ru/maps/org/1?" + new string('a', 500);

        var response = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { yandexMapsUrl = tooLong });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY15-B1-05")]
    public async Task Update_UserInfoInLink_Rejected()
    {
        // §253.2 п.7 — https://user:pass@2gis.ru/... must not slip through as "belongs to 2gis.ru".
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var response = await AuthedClient(owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { twoGisUrl = "https://user:pass@2gis.ru/barnaul/firm/1" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY15-B1-06")]
    public async Task CompanyCreatedBeforeThisCycle_HasBothFieldsNull_NothingChanged()
    {
        // 0-bis: existing companies (created without these fields) must show NOTHING, not a
        // reconstructed search link (the cycle 13 behavior this cycle deliberately removes).
        var (_, company) = await CreateOwnerWithCompanyAsync();

        var view = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        var dto = (await view.Content.ReadJsonAsync<CompanyDto>())!;
        dto.YandexMapsUrl.Should().BeNull();
        dto.TwoGisUrl.Should().BeNull();
    }

    [Fact, TestCase("CY15-B1-07")]
    public async Task SuperAdminEditsCompany_SameServerSideValidationApplies()
    {
        // R5 cycle 13 / §253.4 — the check lives on the server for EVERY caller of this one write path,
        // not only the owner's own form.
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();

        var response = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { yandexMapsUrl = "javascript:alert(1)" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { yandexMapsUrl = RealYandexUrl });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY15-B1-08")]
    public async Task ForeignOwner_CannotEditAnotherCompanysLinks()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(stranger.Owner.Token).PutAsJsonAsync($"/api/companies/{company.Id}",
            new { yandexMapsUrl = RealYandexUrl });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
