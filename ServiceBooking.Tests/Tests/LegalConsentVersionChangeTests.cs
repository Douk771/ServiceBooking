using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// US-37 п.4-6 (SPEC.md §3.3, ARCHITECTURE.md §6.3, API_CONTRACT.md §0.3/§0.4) — scenarios that need to
/// change the published legal document version mid-test, against a dedicated
/// <see cref="LegalDocumentsTestFactory"/> so the shared "Api" collection's static documents are never
/// touched. One factory instance per test class run (not per test) — <see cref="ResetVersion"/> in each
/// test's Arrange step gives every test its own fresh version tag so tests don't interfere with each
/// other's accepted-version state.
/// </summary>
public class LegalConsentVersionChangeTests : IAsyncLifetime
{
    private LegalDocumentsTestFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new LegalDocumentsTestFactory();
        _ = _factory.Services; // boot the host now, not lazily inside the first test
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Anon() => _factory.CreateClient();

    private HttpClient Authed(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string UniquePhone()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsDigit).Take(9).ToArray();
        return $"+79{new string(digits).PadRight(9, '0')}";
    }

    private async Task<AuthResponseDto> RegisterAsync()
    {
        var response = await Anon().PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Т", lastName = "Т", phone = UniquePhone(), password = "Password123!", acceptedLegal = true
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    /// <summary>Minimal owner + company + free-plan-compatible guest-booking setup, local to this test
    /// class (its own factory, not ApiTestBase's) — just enough for LEG-013b below.</summary>
    private async Task<(string OwnerToken, System.Text.Json.JsonElement Company, string MasterId, System.Text.Json.JsonElement Service)>
        SetUpBookableCompanyAsync()
    {
        var owner = await RegisterAsync();
        var slug = "legcyc-" + Guid.NewGuid().ToString("N")[..12];
        var companyResponse = await Authed(owner.Token).PostAsJsonAsync("/api/companies", new
        {
            name = "Legal Cycle Co " + slug, slug, description = (string?)null, address = (string?)null,
            phone = (string?)null, email = (string?)null, allowSelfBooking = true
        });
        companyResponse.EnsureSuccessStatusCode();
        var company = await companyResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var companyId = company.GetProperty("id").GetGuid();

        var master = await RegisterAsync();
        (await Authed(owner.Token).PostAsJsonAsync($"/api/companies/{companyId}/members", new
        {
            phone = master.Phone, firstName = master.FirstName, lastName = master.LastName, role = "Master", bio = (string?)null, email = (string?)null
        })).EnsureSuccessStatusCode();
        var ownerReLoggedIn = await Anon().PostAsJsonAsync("/api/auth/login", new { phone = owner.Phone, password = "Password123!" });
        var ownerAuth = (await ownerReLoggedIn.Content.ReadFromJsonAsync<AuthResponseDto>())!;

        var serviceResponse = await Authed(ownerAuth.Token).PostAsJsonAsync("/api/services", new
        {
            companyId, name = "Test Service", description = (string?)null, durationMinutes = 60, price = 1000m
        });
        serviceResponse.EnsureSuccessStatusCode();
        var service = await serviceResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        (await Authed(ownerAuth.Token).PutAsJsonAsync("/api/workinghours", new
        {
            masterId = master.UserId, companyId, date, isWorking = true,
            startTime = new TimeOnly(9, 0), endTime = new TimeOnly(18, 0), breaks = Array.Empty<object>()
        })).EnsureSuccessStatusCode();

        return (ownerAuth.Token, company, master.UserId, service);
    }

    [Fact, TestCase("LEG-013")]
    public async Task MaterialChange_BlocksOldToken_OnProtectedEndpoint_ButAllowsAllowListedOnes()
    {
        _factory.ResetToDefault();
        var user = await RegisterAsync();

        // A new Material redaction is published — the token this user was issued still carries the OLD
        // accepted version.
        _factory.WriteManifest("v2-material-draft", isDraft: true, changeKind: "Material");
        await Task.Delay(1200); // > Legal:ReloadSeconds (1s), so the provider's mtime cache expires

        var client = Authed(user.Token);

        // Any ordinary [Authorize] endpoint outside the allow-list → 451.
        var blocked = await client.GetAsync("/api/companies/my");
        blocked.StatusCode.Should().Be((HttpStatusCode)451);
        (await blocked.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace();

        // Allow-listed endpoints (API_CONTRACT.md §0.4) keep working even while blocked everywhere else.
        (await client.GetAsync("/api/legal/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/legal/consent-status")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/profile")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/profile/export")).StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await (await client.GetAsync("/api/legal/consent-status")).Content
            .ReadFromJsonAsync<ConsentStatusDto>();
        status!.RequiresAcceptance.Should().BeTrue();
        status.ShowBanner.Should().BeFalse();
    }

    [Fact, TestCase("LEG-014")]
    public async Task EditorialChange_NeverBlocks_ButShowsBanner()
    {
        _factory.ResetToDefault();
        var user = await RegisterAsync();

        _factory.WriteManifest("v2-editorial-draft", isDraft: true, changeKind: "Editorial");
        await Task.Delay(1200);

        var client = Authed(user.Token);
        var response = await client.GetAsync("/api/companies/my");
        response.StatusCode.Should().NotBe((HttpStatusCode)451);

        var status = await (await client.GetAsync("/api/legal/consent-status")).Content
            .ReadFromJsonAsync<ConsentStatusDto>();
        status!.RequiresAcceptance.Should().BeFalse();
        status.ShowBanner.Should().BeTrue();
    }

    [Fact, TestCase("LEG-015")]
    public async Task Accept_AfterMaterialChange_ReturnsFreshToken_ThatIsNoLongerBlocked()
    {
        _factory.ResetToDefault();
        var user = await RegisterAsync();

        _factory.WriteManifest("v2-material-draft", isDraft: true, changeKind: "Material");
        await Task.Delay(1200);

        var oldClient = Authed(user.Token);
        (await oldClient.GetAsync("/api/companies/my")).StatusCode.Should().Be((HttpStatusCode)451);

        var docs = await (await Anon().GetAsync("/api/legal/documents")).Content
            .ReadFromJsonAsync<LegalDocumentListDto>();
        var privacy = docs!.Documents.First(d => d.Type == "Privacy").Version;
        var terms = docs.Documents.First(d => d.Type == "Terms").Version;

        var acceptResponse = await oldClient.PostAsJsonAsync("/api/legal/accept", new { privacyVersion = privacy, termsVersion = terms });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = await acceptResponse.Content.ReadFromJsonAsync<AcceptLegalResponseDto>();

        var newClient = Authed(accepted!.Token);
        (await newClient.GetAsync("/api/companies/my")).StatusCode.Should().NotBe((HttpStatusCode)451);

        // The old token is still blocked — accept() minted a NEW token, it did not retroactively fix
        // the old one (a JWT's claims are baked in at issuance).
        (await oldClient.GetAsync("/api/companies/my")).StatusCode.Should().Be((HttpStatusCode)451);
    }

    [Fact, TestCase("LEG-016")]
    public async Task ReplacingDraftWithVettedText_IsTreatedAsMaterial_EvenWithoutAnyOtherFieldChanging()
    {
        // SPEC.md §3.3 п.5: "замена черновой редакции на вычитанную ... обрабатывается именно как
        // существенная правка — это проверяется отдельным тестом". The only thing that changes here is
        // isDraft: true → false and the version losing its "-draft" suffix; changeKind is still Material.
        var draftVersion = _factory.ResetToDefault(); // isDraft:true, Material
        draftVersion.Should().EndWith("-draft");
        var user = await RegisterAsync();

        var vettedVersion = "vetted-" + DateTime.UtcNow.Ticks;
        _factory.WriteManifest(vettedVersion, isDraft: false, changeKind: "Material");
        await Task.Delay(1200);

        var response = await Authed(user.Token).GetAsync("/api/companies/my");
        response.StatusCode.Should().Be((HttpStatusCode)451,
            "replacing a draft with the vetted text is defined as a Material change regardless of changeKind staying the same value");
    }

    [Fact, TestCase("LEG-017")]
    public async Task ReplacingTheFileOnDisk_IsPickedUpWithoutARestart()
    {
        // T-B4 DoD (ARCHITECTURE.md §18.2): "замена текста на стенде выполнена вживую и заняла минуты" —
        // this is the automated equivalent: swap legal.json/*.html on disk while the host keeps running,
        // no process restart, and confirm the NEW text is served once ReloadSeconds has elapsed.
        var v1 = _factory.ResetToDefault();
        var before = await (await Anon().GetAsync("/api/legal/documents/privacy")).Content
            .ReadFromJsonAsync<LegalDocumentDto>();
        before!.Version.Should().Be(v1);
        before.ContentHtml.Should().Contain(v1);

        var v2 = "swapped-" + DateTime.UtcNow.Ticks + "-draft";
        _factory.WriteManifest(v2, isDraft: true, changeKind: "Editorial", bodyMarker: "LIVE-SWAP-MARKER");
        await Task.Delay(1200);

        var after = await (await Anon().GetAsync("/api/legal/documents/privacy")).Content
            .ReadFromJsonAsync<LegalDocumentDto>();
        after!.Version.Should().Be(v2);
        after.ContentHtml.Should().Contain("LIVE-SWAP-MARKER");
    }

    [Fact, TestCase("LEG-018")]
    public async Task InvalidReplacementManifest_KeepsServingThePreviousGoodSnapshot()
    {
        // ARCHITECTURE.md §4.3: a broken manifest on disk (here: isDraft:true without the required
        // "-draft" version suffix) must never take the site down — the last good snapshot keeps serving.
        var v1 = _factory.ResetToDefault();
        // Force a successful load of v1 BEFORE corrupting the manifest — otherwise the provider would
        // never have had a good snapshot to fall back to in the first place.
        (await Anon().GetAsync("/api/legal/documents/privacy")).EnsureSuccessStatusCode();

        _factory.WriteManifest("no-suffix-here", isDraft: true, changeKind: "Material"); // invalid: isDraft without "-draft"
        await Task.Delay(1200);

        var response = await Anon().GetAsync("/api/legal/documents/privacy");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LegalDocumentDto>();
        body!.Version.Should().Be(v1, "an invalid manifest must not silently publish half-broken data — the last valid snapshot keeps serving");
    }

    [Fact, TestCase("LEG-019")]
    public async Task DeleteAccount_IsAllowed_EvenWhileBlockedByAPendingMaterialRedaction()
    {
        // SPEC.md §3.5 п.9 / API_CONTRACT.md §0.4: someone who does not want to accept the new redaction
        // must still be able to leave — delete-account is allow-listed.
        _factory.ResetToDefault();
        var user = await RegisterAsync();

        _factory.WriteManifest("v2-material-draft", isDraft: true, changeKind: "Material");
        await Task.Delay(1200);

        var client = Authed(user.Token);
        (await client.GetAsync("/api/companies/my")).StatusCode.Should().Be((HttpStatusCode)451);

        var deleteResponse = await client.PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
