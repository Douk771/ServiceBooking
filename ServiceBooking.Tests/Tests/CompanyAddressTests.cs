using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 19 (SPEC_CYCLE19_TARIFF_LIMITS_GEOCODER.md US-19-06/US-19-07/US-19-08/US-19-09, ARCHITECTURE_CYCLE19.md §388/§413) —
/// written independently from SPEC_CYCLE19_TARIFF_LIMITS_GEOCODER.md's acceptance criteria, not from the implementation.
///
/// The geocoder ("проверка адреса по карте", cycle 13) is removed целиком in this cycle. This file
/// replaces the old `AddressVerificationTests.cs` (ADDR-001…028, cycle 13): every scenario in that file
/// that was actually ABOUT the geocoder (verify:true triggering a lookup, precision/candidates,
/// StoreResults/coordinates, the geocoder's own licensed wording) has no meaning any more and is
/// intentionally not reproduced here — see TEST_CATALOG.md for the "removed in cycle 19" note. What
/// this file keeps/re-proves, addressed directly at the surviving endpoints
/// (<c>PUT /api/companies/{id}/address</c>, <c>POST /api/companies/address/notice</c>): auth/rights,
/// byte-for-byte save, empty-string clears, search-by-address, the public-address-notice legal gate,
/// the shared address-verify rate limit, and that address/lookup is gone (404 unconditionally) —
/// including with old cycle-13 `ADDRESSVERIFICATION__*` env values still present (US-19-08).
/// </summary>
public class CompanyAddressTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── Auth/rights matrix (US-19-06, same matrix as before geocoder removal) ───────────────────────

    [Fact, TestCase("ADDR-004")]
    public async Task SaveAddress_Anonymous_Returns401()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var response = await factory.CreateClient().PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "x" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("ADDR-006")]
    public async Task SaveAddress_NonMemberClient_ReturnsForbidden()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();
        var client = AuthedFor(factory, stranger.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "x" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ADDR-007")]
    public async Task SaveAddress_MasterOfCompany_ReturnsForbidden_OnlyOwnerAndSuperAdminManage()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var client = AuthedFor(factory, master.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "x" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ADDR-009")]
    public async Task SaveAddress_SuperAdmin_CanManageAnyCompany_SameCheckAsOwner()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();
        var client = AuthedFor(factory, admin.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("ADDR-010")]
    public async Task SaveAddress_UnknownCompany_Returns404()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var owner = await RegisterAsync();
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{Guid.NewGuid()}/address", new { address = "x" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Byte-for-byte save (US-19-06) ────────────────────────────────────────────────────────────

    [Fact, TestCase("ADDR-011")]
    public async Task SaveAddress_SavesExactlyAsTyped_ByteForByte()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "  Ленина, д. 5, оф. 3  " });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;
        result.Company.Address.Should().Be("  Ленина, д. 5, оф. 3  ", "§413.2: never trimmed, never normalized");
    }

    [Fact, TestCase("ADDR-030")]
    public async Task SaveAddress_Verify_ParameterAcceptedAndIgnored_CachedOldFrontendDoesNotBreak()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5", verify = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "an old cached frontend still sending verify:true must not break");
        var result = (await response.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;
        result.Company.Address.Should().Be("Ленина 5");
    }

    [Fact, TestCase("ADDR-022")]
    public async Task SaveAddress_EmptyString_ClearsAddress_RemovesFromPublicPageAndSearch()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var marker = Unique("ГдеТоУлица");
        var withAddress = AuthedFor(factory, owner.Token);
        (await withAddress.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = marker }))
            .EnsureSuccessStatusCode();

        var foundBefore = await AnonymousClient().GetFromJsonAsync<PagedResult<CompanyDto>>($"/api/companies/public?search={Uri.EscapeDataString(marker)}");
        foundBefore!.Items.Should().Contain(c => c.Id == company.Id);

        var clear = await withAddress.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "" });
        clear.EnsureSuccessStatusCode();
        var cleared = (await clear.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;
        cleared.Company.Address.Should().BeNull();

        var pub = await factory.CreateClient().GetAsync($"/api/companies/{company.Slug}");
        var dto = (await pub.Content.ReadJsonAsync<CompanyDto>())!;
        dto.Address.Should().BeNull();

        var foundAfter = await AnonymousClient().GetFromJsonAsync<PagedResult<CompanyDto>>($"/api/companies/public?search={Uri.EscapeDataString(marker)}");
        foundAfter!.Items.Should().NotContain(c => c.Id == company.Id, "§220.3's promise: deleting the address must remove it from search too");
    }

    [Fact, TestCase("ADDR-023")]
    public async Task PublicSearch_ByAddress_FindsCompany_R7()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var marker = Unique("проездМонтажников");
        var client = AuthedFor(factory, owner.Token);

        (await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = marker + " 5" }))
            .EnsureSuccessStatusCode();
        var search = await AnonymousClient().GetFromJsonAsync<PagedResult<CompanyDto>>($"/api/companies/public?search={Uri.EscapeDataString(marker)}");
        search!.Items.Should().Contain(c => c.Id == company.Id);
    }

    [Fact, TestCase("ADDR-031")]
    public async Task CreateCompany_WithAddress_SavesAddress()
    {
        var owner = await RegisterAsync();
        var cityId = await AnyCityIdAsync();
        var dto = new CreateCompanyDto(
            Unique("Салон "), Unique("salon-"), null, "Ленина, 5", null, null, cityId, null,
            OwnerTerms: CurrentOwnerTermsDto());
        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var envelope = (await response.Content.ReadJsonAsync<CreateCompanyResponseDto>())!;
        envelope.Company.Address.Should().Be("Ленина, 5");
    }

    // ── Rate limiting (still "address-verify" under the old name, §388.2) ───────────────────────────

    [Fact, TestCase("ADDR-024")]
    public async Task SaveAddress_ExceedingPermitLimit_Returns429_WithNonEmptyBody()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString, permitLimit: 3);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);

        for (var i = 0; i < 3; i++)
        {
            var r = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = $"адрес {i}" });
            r.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var fourth = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "адрес 4" });
        fourth.StatusCode.Should().Be((HttpStatusCode)429);
        (await fourth.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace();
    }

    // ── Public-address notice (§220, §242) ───────────────────────────────────────────────────────

    [Fact, TestCase("ADDR-025")]
    public async Task ConfirmNotice_NotConfirmed_Returns400()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);
        var version = await CurrentPublicAddressNoticeVersionAsync(factory);

        var response = await client.PostJsonAsync("/api/companies/address/notice", new { textVersion = version, confirmed = false });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("ADDR-026")]
    public async Task ConfirmNotice_StaleVersion_Returns409()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PostJsonAsync("/api/companies/address/notice", new { textVersion = "not-the-real-version", confirmed = true });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("ADDR-027")]
    public async Task ConfirmNotice_Confirmed_WritesConsentRecord_VisibleInExport()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);
        var version = await CurrentPublicAddressNoticeVersionAsync(factory);

        var response = await client.PostJsonAsync("/api/companies/address/notice", new { textVersion = version, confirmed = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var exportResponse = await AuthedClient(owner.Token).GetAsync("/api/profile/export");
        exportResponse.EnsureSuccessStatusCode();
        var export = (await exportResponse.Content.ReadJsonAsync<ProfileExportDto>())!;
        export.Consents.Should().ContainSingle(c => c.DocumentKey == "PublicAddressNotice" && c.DocumentVersion == version);
    }

    [Fact, TestCase("ADDR-028")]
    public async Task ConfirmNotice_RepeatedWithinIdempotencyWindow_DoesNotCreateSecondRecord()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);
        var version = await CurrentPublicAddressNoticeVersionAsync(factory);

        var first = await client.PostJsonAsync("/api/companies/address/notice", new { textVersion = version, confirmed = true });
        first.EnsureSuccessStatusCode();
        var second = await client.PostJsonAsync("/api/companies/address/notice", new { textVersion = version, confirmed = true });
        second.EnsureSuccessStatusCode();

        var exportResponse = await AuthedClient(owner.Token).GetAsync("/api/profile/export");
        var export = (await exportResponse.Content.ReadJsonAsync<ProfileExportDto>())!;
        export.Consents.Count(c => c.DocumentKey == "PublicAddressNotice" && c.RevokedAt == null)
            .Should().Be(1, "the ledger's own idempotency window (§220.3) must swallow a double-click, not double-write");
    }

    // ── US-19-09: address/lookup is gone unconditionally ─────────────────────────────────────────

    [Fact, TestCase("ADDR-002")]
    public async Task Lookup_Authenticated_Returns404_EndpointNoLongerExists()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);
        var response = await client.PostJsonAsync("/api/companies/address/lookup", new { address = "Ленина 5" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("ADDR-001")]
    public async Task Lookup_Anonymous_Returns404_NotEnabledAndBroken()
    {
        await using var factory = new CompanyAddressTestFactory(ConnectionString);
        var response = await factory.CreateClient().PostJsonAsync("/api/companies/address/lookup", new { address = "Ленина 5" });
        // Route no longer exists at all — unlike a real 401-guarded route, an unauthenticated caller
        // also gets 404 (no route to challenge), same observable outcome §19-09 requires either way.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── US-19-08: start with legacy ADDRESSVERIFICATION__* env values still present ─────────────────

    [Fact, TestCase("ADDR-029")]
    public async Task Host_StartsAndSavesAddress_WithLegacyGeocoderEnvValues_Present_US19_08()
    {
        // Old, now-unread cycle-13 keys at values that used to ROT the start (yandex provider without a
        // key, MaxCandidates/CacheHours out of range) — the host must still start and still save.
        var legacySettings = new Dictionary<string, string?>
        {
            ["AddressVerification:Provider"] = "yandex",
            ["AddressVerification:YandexApiKey"] = "",
            ["AddressVerification:CacheHours"] = "-1",
            ["AddressVerification:MaxCandidates"] = "999",
        };
        await using var factory = new CompanyAddressTestFactory(ConnectionString, extraSettings: legacySettings);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "US-19-08: stale ADDRESSVERIFICATION__* env values must never block startup or saving");

        var lookup = await client.PostJsonAsync("/api/companies/address/lookup", new { address = "Ленина 5" });
        lookup.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    private static HttpClient AuthedFor(CompanyAddressTestFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> CurrentPublicAddressNoticeVersionAsync(CompanyAddressTestFactory factory)
    {
        var response = await factory.CreateClient().GetAsync($"/api/legal/texts/{LegalTextKey.PublicAddressNotice}");
        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadJsonAsync<ServiceBooking.API.Controllers.LegalUiTextDto>())!;
        return dto.Version;
    }
}
