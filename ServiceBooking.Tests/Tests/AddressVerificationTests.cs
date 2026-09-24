using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services.Geo;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 13 — SPEC.md US-132…US-140, ARCHITECTURE_CYCLE13.md §207/§209/§214,
/// API_CONTRACT_CYCLE13.md §233/§234/§242. Written from SPEC.md's acceptance criteria and
/// LEGAL_REVIEW.md §16.2/§16.5's licence constraints, independently of the controller's own
/// implementation, per this cycle's QA brief. Every test that reaches the geocoder does so through
/// <see cref="AddressVerificationTestFactory.Geocoder"/> (<see cref="FakeAddressGeocoder"/>) — never
/// the real network — and setup (register/company/plan) reuses the shared "Api" host via
/// <see cref="ApiTestBase"/>, since JWTs minted there are valid against any host pointed at the same
/// database (same fixed Jwt:Key/Issuer/Audience, TestHostSettings.Apply).
/// </summary>
public class AddressVerificationTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── US-140 — rubильник off ───────────────────────────────────────────────────────────────────

    [Fact, TestCase("ADDR-001")]
    public async Task Lookup_ProviderLogging_Returns404_NotEnabledAndBroken()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString, provider: "logging");
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.Token);

        var response = await client.PostJsonAsync("/api/companies/address/lookup", new { address = "Ленина 5" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("ADDR-002")]
    public async Task SaveAddress_ProviderLogging_StillSaves_AsUnverified_US140()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString, provider: "logging");
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address",
            new { address = "Ленина 5", verify = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "rubильник off must never block an ordinary save");
        var result = (await response.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;
        result.Company.Address.Should().Be("Ленина 5");
        result.Verification.Status.Should().Be("Unverified");
        factory.Geocoder.CallCount.Should().Be(0, "the switch being off must mean no call, not a call that's ignored");
    }

    [Fact, TestCase("ADDR-003")]
    public async Task CompanyDto_ProviderLogging_AddressVerificationAvailableIsFalse()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString, provider: "logging");
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.Token);

        var response = await client.GetAsync("/api/companies/my");
        response.EnsureSuccessStatusCode();
        var companies = (await response.Content.ReadJsonAsync<List<CompanyDto>>())!;
        var mine = companies.Single(c => c.Id == company.Id);
        mine.AddressVerification!.Available.Should().BeFalse();
    }

    // ── Auth/rights matrix (US-138, R5) ──────────────────────────────────────────────────────────

    [Fact, TestCase("ADDR-004")]
    public async Task Lookup_Anonymous_Returns401()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var response = await factory.CreateClient().PostJsonAsync("/api/companies/address/lookup", new { address = "Ленина 5" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("ADDR-005")]
    public async Task SaveAddress_Anonymous_Returns401()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var response = await factory.CreateClient().PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "x", verify = false });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("ADDR-006")]
    public async Task SaveAddress_NonMemberClient_ReturnsForbidden()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync(); // never joined this company
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", stranger.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "x", verify = false });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ADDR-007")]
    public async Task SaveAddress_MasterOfCompany_ReturnsForbidden_OnlyOwnerAndSuperAdminManage()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", master.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "x", verify = false });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ADDR-008")]
    public async Task Lookup_WithCompanyId_NonManagerClient_Returns404_NotConfirmingExistence()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", stranger.Token);

        var response = await client.PostJsonAsync("/api/companies/address/lookup",
            new { address = "Ленина 5", companyId = company.Id });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("ADDR-009")]
    public async Task SaveAddress_SuperAdmin_CanManageAnyCompany_SameCheckAsOwner_US138()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5", verify = false });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("ADDR-010")]
    public async Task SaveAddress_UnknownCompany_Returns404()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var owner = await RegisterAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{Guid.NewGuid()}/address", new { address = "x", verify = false });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── US-134 — verification flow ───────────────────────────────────────────────────────────────

    [Fact, TestCase("ADDR-011")]
    public async Task SaveAddress_Verify_HouseFound_MarksVerified_WithDateAndPrecision()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        factory.Geocoder.Enqueue(FakeAddressGeocoder.Ok(
            "Россия, край, город, проспект Ленина, 5", AddressPrecision.House, null, new GeoPoint(1, 2)));
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5", verify = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;

        result.Verification.Outcome.Should().Be("Ok");
        result.Verification.Status.Should().Be("Verified");
        result.Verification.Precision.Should().Be("House");
        result.Verification.VerifiedAt.Should().NotBeNull();
    }

    [Fact, TestCase("ADDR-012")]
    public async Task SaveAddress_EditedAfterVerify_FallsBackToUnverified_WithoutCallingGeocoderAgain_US134()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);

        factory.Geocoder.Enqueue(FakeAddressGeocoder.Ok("irrelevant", AddressPrecision.House, null, null));
        var first = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5", verify = true });
        first.EnsureSuccessStatusCode();
        factory.Geocoder.CallCount.Should().Be(1);

        // Owner edits the text by hand, saving WITHOUT re-verifying (verify: false, as a plain "save"
        // would do while the owner is still typing) — §203's own rule: this alone must already fall back
        // to Unverified, and it must cost zero geocoder calls.
        var second = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 6", verify = false });
        second.EnsureSuccessStatusCode();
        var result = (await second.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;

        result.Verification.Status.Should().Be("Unverified");
        factory.Geocoder.CallCount.Should().Be(1, "editing the address text must never itself trigger a geocoder call");
    }

    [Fact, TestCase("ADDR-013")]
    public async Task SaveAddress_StreetPrecision_UnverifiedWithWarning_US135()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        factory.Geocoder.Enqueue(FakeAddressGeocoder.Ok("Ленина", AddressPrecision.Street, null, null));
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина", verify = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;

        result.Verification.Status.Should().Be("Unverified");
        result.Verification.Warnings.Should().ContainSingle(w => w.Code == "PrecisionStreet");
    }

    [Fact, TestCase("ADDR-014")]
    public async Task SaveAddress_NotFound_SavesAnyway_US136()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        factory.Geocoder.Enqueue(FakeAddressGeocoder.Empty());
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address",
            new { address = "деревня без карт, 1", verify = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "§6 SPEC: недоступность/отсутствие результата не блокирует сохранение");
        var result = (await response.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;
        result.Company.Address.Should().Be("деревня без карт, 1");
        result.Verification.Status.Should().Be("Unverified");
        result.Verification.Warnings.Should().ContainSingle(w => w.Code == "NotFound");
    }

    [Fact, TestCase("ADDR-015")]
    public async Task SaveAddress_GeocoderUnavailable_SavesAnyway_Returns200_US136()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        factory.Geocoder.EnqueueThrow(new HttpRequestException("simulated network failure"));
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5", verify = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "внешний сбой геокодера никогда не блокирует сохранение своей компании");
        var result = (await response.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;
        result.Company.Address.Should().Be("Ленина 5", "the address the owner typed must be saved even though verification failed");
        result.Verification.Status.Should().Be("Unverified");
        result.Verification.Warnings.Should().ContainSingle(w => w.Code == "Unavailable");
    }

    // ── Licence (LEGAL_REVIEW.md §16.2, R18) — the geocoder's own wording must never land in the DB ──

    [Fact, TestCase("ADDR-016")]
    public async Task SaveAddress_Verify_NeverStoresGeocoderWording_OnlyTheOwnersOwnText()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        factory.Geocoder.Enqueue(FakeAddressGeocoder.Ok(
            "Россия, Алтайский край, Барнаул, проспект Ленина, 5", AddressPrecision.House, null, new GeoPoint(1, 2)));
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5", verify = true });
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;

        result.Company.Address.Should().Be("Ленина 5");
        result.Company.Address.Should().NotContain("проспект", "the geocoder's own formatted wording must never be written to the DB (standard licence, §16.2)");

        // Read the raw row back — MapToDto only ever surfaces Address, never AddressVerifiedInputKey;
        // this asserts the actual stored column, not just the DTO's own text field.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
        var stored = await db.Companies.FindAsync(company.Id);
        stored!.Address.Should().Be("Ленина 5");
        stored.AddressVerifiedInputKey.Should().Be(AddressNormalization.Key("Ленина 5"));
    }

    [Fact, TestCase("ADDR-017")]
    public async Task Lookup_CandidateFormattedAddress_IsGeocoderWording_ButNeverPersisted()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        factory.Geocoder.Enqueue(FakeAddressGeocoder.Ok(
            "Россия, Алтайский край, Барнаул, проспект Ленина, 5", AddressPrecision.House, "Барнаул", new GeoPoint(1, 2)));
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PostJsonAsync("/api/companies/address/lookup", new { address = "Ленина 5" });
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadJsonAsync<AddressLookupResultDto>())!;

        result.Candidates.Should().ContainSingle();
        result.Candidates[0].FormattedAddress.Should().Contain("проспект", "the geocoder's own wording is legitimate to SHOW here (§16.2's safe variant)");
    }

    // ── P3/§209.2 — StoreResults gates coordinates, independently of the cache ──────────────────────

    [Fact, TestCase("ADDR-018")]
    public async Task StoreResultsFalse_Verified_NoCoordinates_PublicAddressPointNull()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString, storeResults: false);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        factory.Geocoder.Enqueue(FakeAddressGeocoder.Ok("x", AddressPrecision.House, null, new GeoPoint(53.1, 83.2)));
        var client = AuthedFor(factory, owner.Token);
        var save = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5", verify = true });
        save.EnsureSuccessStatusCode();

        var pub = await factory.CreateClient().GetAsync($"/api/companies/{company.Slug}");
        pub.EnsureSuccessStatusCode();
        var dto = (await pub.Content.ReadJsonAsync<CompanyDto>())!;
        dto.AddressPoint.Should().BeNull("standard Yandex licence — coordinates may not be stored without StoreResults");
    }

    [Fact, TestCase("ADDR-019")]
    public async Task StoreResultsTrue_Verified_CoordinatesSaved_PublicAddressPointPresent()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString, storeResults: true);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        factory.Geocoder.Enqueue(FakeAddressGeocoder.Ok("x", AddressPrecision.House, null, new GeoPoint(53.1, 83.2)));
        var client = AuthedFor(factory, owner.Token);
        var save = await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5", verify = true });
        save.EnsureSuccessStatusCode();

        var pub = await factory.CreateClient().GetAsync($"/api/companies/{company.Slug}");
        pub.EnsureSuccessStatusCode();
        var dto = (await pub.Content.ReadJsonAsync<CompanyDto>())!;
        dto.AddressPoint.Should().NotBeNull();
        dto.AddressPoint!.Latitude.Should().BeApproximately(53.1, 0.0001);
        dto.AddressPoint.Longitude.Should().BeApproximately(83.2, 0.0001);
    }

    [Fact, TestCase("ADDR-020")]
    public async Task Cache_IndependentOfStoreResults_RepeatedLookupSameText_DoesNotCallGeocoderTwice()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString, storeResults: false, cacheHours: 24);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        factory.Geocoder.Enqueue(FakeAddressGeocoder.Ok("x", AddressPrecision.House, null, new GeoPoint(1, 2)));
        var client = AuthedFor(factory, owner.Token);

        var first = await client.PostJsonAsync("/api/companies/address/lookup", new { address = "Ленина 5" });
        first.EnsureSuccessStatusCode();
        var second = await client.PostJsonAsync("/api/companies/address/lookup", new { address = "Ленина 5" });
        second.EnsureSuccessStatusCode();

        factory.Geocoder.CallCount.Should().Be(1, "§209.2 — the cache is legal (and expected) under the standard licence regardless of StoreResults");
    }

    // ── Anonymous CompanyDto never leaks the status (MVP §2 п.6) ─────────────────────────────────

    [Fact, TestCase("ADDR-021")]
    public async Task PublicGetBySlug_AddressVerification_IsNull_StatusNeverLeaksToClient()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        factory.Geocoder.Enqueue(FakeAddressGeocoder.Ok("x", AddressPrecision.House, null, null));
        var owned = AuthedFor(factory, owner.Token);
        (await owned.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "Ленина 5", verify = true }))
            .EnsureSuccessStatusCode();

        var pub = await factory.CreateClient().GetAsync($"/api/companies/{company.Slug}");
        pub.EnsureSuccessStatusCode();
        var dto = (await pub.Content.ReadJsonAsync<CompanyDto>())!;
        dto.AddressVerification.Should().BeNull();
    }

    // ── Deletion (§220.3 — the notice's own promise) ─────────────────────────────────────────────

    [Fact, TestCase("ADDR-022")]
    public async Task SaveAddress_EmptyString_ClearsAddress_RemovesFromPublicPageAndSearch()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var marker = Unique("ГдеТоУлица");
        var withAddress = AuthedFor(factory, owner.Token);
        (await withAddress.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = marker, verify = false }))
            .EnsureSuccessStatusCode();

        var foundBefore = await AnonymousClient().GetFromJsonAsync<PagedResult<CompanyDto>>($"/api/companies/public?search={Uri.EscapeDataString(marker)}");
        foundBefore!.Items.Should().Contain(c => c.Id == company.Id);

        var clear = await withAddress.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = "", verify = false });
        clear.EnsureSuccessStatusCode();
        var cleared = (await clear.Content.ReadJsonAsync<CompanyAddressUpdateResultDto>())!;
        cleared.Company.Address.Should().BeNull();

        var pub = await factory.CreateClient().GetAsync($"/api/companies/{company.Slug}");
        var dto = (await pub.Content.ReadJsonAsync<CompanyDto>())!;
        dto.Address.Should().BeNull();

        var foundAfter = await AnonymousClient().GetFromJsonAsync<PagedResult<CompanyDto>>($"/api/companies/public?search={Uri.EscapeDataString(marker)}");
        foundAfter!.Items.Should().NotContain(c => c.Id == company.Id, "§220.3's promise: deleting the address must remove it from search too");
    }

    // ── R7 regression — search by address still works, before AND after verification ───────────────

    [Fact, TestCase("ADDR-023")]
    public async Task PublicSearch_ByAddress_FindsCompany_BeforeAndAfterVerification_R7()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var marker = Unique("проездМонтажников");
        var client = AuthedFor(factory, owner.Token);

        (await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = marker + " 5", verify = false }))
            .EnsureSuccessStatusCode();
        var beforeSearch = await AnonymousClient().GetFromJsonAsync<PagedResult<CompanyDto>>($"/api/companies/public?search={Uri.EscapeDataString(marker)}");
        beforeSearch!.Items.Should().Contain(c => c.Id == company.Id);

        factory.Geocoder.Enqueue(FakeAddressGeocoder.Ok(
            "Россия, где-то, " + marker + " СОВСЕМ ИНАЧЕ, 5", AddressPrecision.House, null, null));
        (await client.PutJsonAsync($"/api/companies/{company.Id}/address", new { address = marker + " 5", verify = true }))
            .EnsureSuccessStatusCode();

        var afterSearch = await AnonymousClient().GetFromJsonAsync<PagedResult<CompanyDto>>($"/api/companies/public?search={Uri.EscapeDataString(marker)}");
        afterSearch!.Items.Should().Contain(c => c.Id == company.Id, "the DB address is still the owner's own text, so the same search term must still match");
    }

    // ── Rate limiting (§210/§238) ─────────────────────────────────────────────────────────────────

    [Fact, TestCase("ADDR-024")]
    public async Task Lookup_ExceedingPermitLimit_Returns429_WithNonEmptyBody()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString, permitLimit: 3);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);

        for (var i = 0; i < 3; i++)
        {
            var r = await client.PostJsonAsync("/api/companies/address/lookup", new { address = $"адрес {i}" });
            r.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var fourth = await client.PostJsonAsync("/api/companies/address/lookup", new { address = "адрес 4" });
        fourth.StatusCode.Should().Be((HttpStatusCode)429);
        (await fourth.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace();
    }

    // ── Public-address notice (§220, §242) ───────────────────────────────────────────────────────

    [Fact, TestCase("ADDR-025")]
    public async Task ConfirmNotice_NotConfirmed_Returns400()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);
        var version = await CurrentPublicAddressNoticeVersionAsync(factory);

        var response = await client.PostJsonAsync("/api/companies/address/notice", new { textVersion = version, confirmed = false });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("ADDR-026")]
    public async Task ConfirmNotice_StaleVersion_Returns409()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);

        var response = await client.PostJsonAsync("/api/companies/address/notice", new { textVersion = "not-the-real-version", confirmed = true });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("ADDR-027")]
    public async Task ConfirmNotice_Confirmed_WritesConsentRecord_VisibleInExport()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var client = AuthedFor(factory, owner.Token);
        var version = await CurrentPublicAddressNoticeVersionAsync(factory);

        var response = await client.PostJsonAsync("/api/companies/address/notice", new { textVersion = version, confirmed = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The record lives in the shared DB — read it back through the shared "Api" host's own export
        // endpoint (US-38), independently of which host wrote it.
        var exportResponse = await AuthedClient(owner.Token).GetAsync("/api/profile/export");
        exportResponse.EnsureSuccessStatusCode();
        var export = (await exportResponse.Content.ReadJsonAsync<ProfileExportDto>())!;
        export.Consents.Should().ContainSingle(c => c.DocumentKey == "PublicAddressNotice" && c.DocumentVersion == version);
    }

    [Fact, TestCase("ADDR-028")]
    public async Task ConfirmNotice_RepeatedWithinIdempotencyWindow_DoesNotCreateSecondRecord()
    {
        await using var factory = new AddressVerificationTestFactory(ConnectionString);
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

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    private static HttpClient AuthedFor(AddressVerificationTestFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> CurrentPublicAddressNoticeVersionAsync(AddressVerificationTestFactory factory)
    {
        var response = await factory.CreateClient().GetAsync($"/api/legal/texts/{LegalTextKey.PublicAddressNotice}");
        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadJsonAsync<ServiceBooking.API.Controllers.LegalUiTextDto>())!;
        return dto.Version;
    }
}
