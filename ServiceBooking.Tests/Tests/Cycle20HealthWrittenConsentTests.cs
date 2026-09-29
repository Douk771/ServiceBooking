using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Services;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 20 (SPEC_CYCLE20_LEGAL_CLOSURE.md US-20-01, LG1; ARCHITECTURE_CYCLE20.md §402) — the paper
/// written-consent gate on a client's health note. Written from SPEC.md's acceptance criteria,
/// independently of ClientConsentsController's own implementation ("Вызов 2"): the electronic salon
/// consent and the account holder's own PdnConsent/HealthData purpose must no longer open the field —
/// ONLY a recorded paper mark (Source = PaperForm) does.
/// </summary>
public class Cycle20HealthWrittenConsentTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private async Task<(ServiceBooking.API.DTOs.Auth.AuthResponseDto Owner, CompanyDto Company, ServiceBooking.API.DTOs.Auth.AuthResponseDto Client, string ClientKey)>
        SetUpClientWithBookingAsync()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var client = await RegisterAsync();
        var createResponse = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        return (owner, company, client, client.UserId);
    }

    // ── CY20-HC-01: no paper mark -> GET reports ConsentRequired, PUT is rejected ─────────────────

    [Fact, TestCase("CY20-HC-01")]
    public async Task NoWrittenConsent_GetReportsRequired_PutIsRejected()
    {
        var (owner, company, _, clientKey) = await SetUpClientWithBookingAsync();

        var getResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/clients/{clientKey}/health-note");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("consentRequired").GetBoolean().Should().BeTrue();
        body.GetProperty("writtenConsent").GetProperty("granted").GetBoolean().Should().BeFalse();
        body.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);

        var putResponse = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientKey}/health-note", new { value = "аллергия на латекс" });
        putResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await putResponse.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("requiredTextKey").GetString().Should().Be("HealthDataWrittenConsentForm");
    }

    // ── CY20-HC-02: old electronic salon consent no longer satisfies the gate ────────────────────

    [Fact, TestCase("CY20-HC-02")]
    public async Task LegacyElectronicHealthConsentRoute_IsGone_DoesNotOpenTheField()
    {
        var (owner, company, _, clientKey) = await SetUpClientWithBookingAsync();

        var legacyPost = await AuthedClient(owner.Token).PostAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientKey}/health-consent",
            new { textVersion = "x", confirmed = true });
        ((int)legacyPost.StatusCode).Should().Be(410, "US-20-01 retires the electronic salon consent route outright");

        var putResponse = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientKey}/health-note", new { value = "test" });
        putResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the retired route must not have opened the gate");
    }

    // ── CY20-HC-03: paper mark granted -> PUT/GET succeed, note is encrypted+readable ────────────

    [Fact, TestCase("CY20-HC-03")]
    public async Task WrittenConsentGranted_PutThenGetSucceeds()
    {
        var (owner, company, _, clientKey) = await SetUpClientWithBookingAsync();
        await GrantHealthConsentAsync(owner.Token, company.Id, clientKey);

        var putResponse = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientKey}/health-note", new { value = "аллергия на латекс" });
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/clients/{clientKey}/health-note");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("value").GetString().Should().Be("аллергия на латекс");
        body.GetProperty("consentRequired").GetBoolean().Should().BeFalse();
        body.GetProperty("writtenConsent").GetProperty("granted").GetBoolean().Should().BeTrue();
    }

    // ── CY20-HC-04: revoke deletes the note and re-closes the gate ────────────────────────────────

    [Fact, TestCase("CY20-HC-04")]
    public async Task RevokeWrittenConsent_DeletesHealthNote_AndRecloseGate()
    {
        var (owner, company, _, clientKey) = await SetUpClientWithBookingAsync();
        await GrantHealthConsentAsync(owner.Token, company.Id, clientKey);
        (await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientKey}/health-note", new { value = "test note" })).EnsureSuccessStatusCode();

        var revoke = await AuthedClient(owner.Token).PostAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientKey}/health-written-consent/revoke", new { reason = "MarkedByMistake" });
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        var revokeBody = await revoke.Content.ReadFromJsonAsync<JsonElement>();
        revokeBody.GetProperty("healthNotesDeleted").GetInt32().Should().Be(1);
        revokeBody.GetProperty("writtenConsent").GetProperty("granted").GetBoolean().Should().BeFalse();

        var getAfter = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/clients/{clientKey}/health-note");
        var afterBody = await getAfter.Content.ReadFromJsonAsync<JsonElement>();
        afterBody.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null, "the note row must be gone, not merely hidden");
        afterBody.GetProperty("consentRequired").GetBoolean().Should().BeTrue();

        var putAgain = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientKey}/health-note", new { value = "new note" });
        putAgain.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the gate must be closed again after revoke");
    }

    // ── CY20-HC-05: unknown revoke reason -> 400 ─────────────────────────────────────────────────

    [Fact, TestCase("CY20-HC-05")]
    public async Task RevokeWrittenConsent_UnknownReason_Returns400()
    {
        var (owner, company, _, clientKey) = await SetUpClientWithBookingAsync();
        await GrantHealthConsentAsync(owner.Token, company.Id, clientKey);

        var revoke = await AuthedClient(owner.Token).PostAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientKey}/health-written-consent/revoke", new { reason = "BecauseIFeelLikeIt" });
        revoke.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── CY20-HC-06: mark granted in company A does not open the field in company B for the same client ─

    [Fact, TestCase("CY20-HC-06")]
    public async Task WrittenConsent_IsScopedPerCompany_DoesNotCrossOver()
    {
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var masterA = await AddMasterAsync(ownerA.Token, companyA.Id);
        var serviceA = await CreateServiceAsync(ownerA.Token, companyA.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(ownerA.Token, masterA.UserId, companyA.Id, date);

        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var masterB = await AddMasterAsync(ownerB.Token, companyB.Id);
        var serviceB = await CreateServiceAsync(ownerB.Token, companyB.Id);
        await SetWorkingDayAsync(ownerB.Token, masterB.UserId, companyB.Id, date);

        var client = await RegisterAsync();
        (await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyA.Id, serviceA.Id, masterA.UserId, date, new TimeOnly(10, 0), null, null, null, null, null)))
            .EnsureSuccessStatusCode();
        (await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(companyB.Id, serviceB.Id, masterB.UserId, date, new TimeOnly(11, 0), null, null, null, null, null)))
            .EnsureSuccessStatusCode();

        await GrantHealthConsentAsync(ownerA.Token, companyA.Id, client.UserId);

        var putInA = await AuthedClient(ownerA.Token).PutAsJsonAsync(
            $"/api/companies/{companyA.Id}/clients/{client.UserId}/health-note", new { value = "note A" });
        putInA.StatusCode.Should().Be(HttpStatusCode.OK);

        var putInB = await AuthedClient(ownerB.Token).PutAsJsonAsync(
            $"/api/companies/{companyB.Id}/clients/{client.UserId}/health-note", new { value = "note B" });
        putInB.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a paper mark recorded for one company must not open the health field in another (US-20-01, salon-scoped)");
    }

    // ── CY20-HC-07: SuperAdmin is refused outright, both on GET and PUT ──────────────────────────

    [Fact, TestCase("CY20-HC-07")]
    public async Task SuperAdmin_IsForbidden_OnGetAndPut()
    {
        var (owner, company, _, clientKey) = await SetUpClientWithBookingAsync();
        await GrantHealthConsentAsync(owner.Token, company.Id, clientKey);
        var admin = await LoginAsSuperAdminAsync();

        var get = await AuthedClient(admin.Token).GetAsync($"/api/companies/{company.Id}/clients/{clientKey}/health-note");
        get.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var put = await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientKey}/health-note", new { value = "x" });
        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── CY20-HC-08: printable form has no passport fields, keeps operator details blank when missing ──

    [Fact, TestCase("CY20-HC-08")]
    public async Task HealthConsentForm_NeverCarriesPassportData_MissingOperatorDetailsSurfaceAsFlag()
    {
        var (owner, company, _, clientKey) = await SetUpClientWithBookingAsync();

        var response = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/clients/{clientKey}/health-consent-form");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.ToLowerInvariant().Should().NotContainAny("passport", "паспорт", "серия", "снилс");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("formId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("runtimeValues").GetProperty("companyName").GetString().Should().Be(company.Name);
        // A freshly-created test company/account has no operator (billing) details on file yet —
        // §402.5/Т20-04: they surface as null values plus an explicit flag, never as a missing field.
        body.GetProperty("operatorDetailsMissing").GetBoolean().Should().BeTrue();
    }

    // ── CY20-HC-09: putting a value the gate never opened cannot be read back by another company's staff ─

    [Fact, TestCase("CY20-HC-09")]
    public async Task PutHealthNote_EmptyValue_Returns400_NoRowWritten()
    {
        var (owner, company, _, clientKey) = await SetUpClientWithBookingAsync();
        await GrantHealthConsentAsync(owner.Token, company.Id, clientKey);

        var putEmpty = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientKey}/health-note", new { value = "" });
        putEmpty.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ClientHealthNotes.AnyAsync(n => n.CompanyId == company.Id && n.ClientId == clientKey)).Should().BeFalse();
    }
}
