using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.WorkingHours;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class WorkingHoursTests(ApiDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── GET /api/workinghours ─────────────────────────────────────────────────

    [Fact, TestCase("WH-001")]
    public async Task Get_ReturnsEmptyList_WhenNoEntriesExist()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var from = NextWeekday();
        var to = from.AddDays(7);

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/workinghours?masterId={master.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var hours = await response.Content.ReadFromJsonAsync<List<WorkingHoursDto>>();
        hours.Should().BeEmpty();
    }

    [Fact, TestCase("WH-002")]
    public async Task Get_ReturnsEntriesWithinRange()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/workinghours?masterId={master.UserId}&companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var hours = await response.Content.ReadFromJsonAsync<List<WorkingHoursDto>>();
        hours.Should().ContainSingle(wh => wh.Date == date && wh.IsWorking);
    }

    // ── PUT /api/workinghours ──────────────────────────────────────────────────

    [Fact, TestCase("WH-003")]
    public async Task Put_MasterSetsOwnHours_WithBreak_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var date = NextWeekday();

        var dto = new UpsertWorkingHoursDto(master.UserId, company.Id, date, true,
            new TimeOnly(9, 0), new TimeOnly(18, 0), [new UpsertBreakDto(new TimeOnly(13, 0), new TimeOnly(14, 0))]);

        var response = await AuthedClient(master.Token).PutAsJsonAsync("/api/workinghours", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkingHoursDto>();
        result!.MasterId.Should().Be(master.UserId);
        result.Breaks.Should().ContainSingle(b => b.StartTime == new TimeOnly(13, 0) && b.EndTime == new TimeOnly(14, 0));
    }

    [Fact, TestCase("WH-004")]
    public async Task Put_OwnerSetsMastersHours_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var date = NextWeekday();

        var dto = new UpsertWorkingHoursDto(master.UserId, company.Id, date, true,
            new TimeOnly(10, 0), new TimeOnly(16, 0), []);

        var response = await AuthedClient(owner.Token).PutAsJsonAsync("/api/workinghours", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkingHoursDto>();
        result!.StartTime.Should().Be(new TimeOnly(10, 0));
        result.EndTime.Should().Be(new TimeOnly(16, 0));
    }

    [Fact, TestCase("WH-005")]
    public async Task Put_ByUnrelatedAuthenticatedUser_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var stranger = await RegisterAsync();
        var date = NextWeekday();

        var dto = new UpsertWorkingHoursDto(master.UserId, company.Id, date, true,
            new TimeOnly(9, 0), new TimeOnly(18, 0), []);

        var response = await AuthedClient(stranger.Token).PutAsJsonAsync("/api/workinghours", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("WH-006")]
    public async Task Put_SameDateTwice_UpdatesExistingEntry_ReplacesBreaks()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var date = NextWeekday();

        var first = new UpsertWorkingHoursDto(master.UserId, company.Id, date, true,
            new TimeOnly(9, 0), new TimeOnly(17, 0), [new UpsertBreakDto(new TimeOnly(12, 0), new TimeOnly(13, 0))]);
        var firstResponse = await AuthedClient(master.Token).PutAsJsonAsync("/api/workinghours", first);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstResult = await firstResponse.Content.ReadFromJsonAsync<WorkingHoursDto>();

        var second = new UpsertWorkingHoursDto(master.UserId, company.Id, date, true,
            new TimeOnly(10, 0), new TimeOnly(19, 0), []);
        var secondResponse = await AuthedClient(master.Token).PutAsJsonAsync("/api/workinghours", second);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondResult = await secondResponse.Content.ReadFromJsonAsync<WorkingHoursDto>();

        secondResult!.Id.Should().Be(firstResult!.Id);
        secondResult.StartTime.Should().Be(new TimeOnly(10, 0));
        secondResult.EndTime.Should().Be(new TimeOnly(19, 0));
        secondResult.Breaks.Should().BeEmpty();

        var listResponse = await AuthedClient(master.Token).GetAsync(
            $"/api/workinghours?masterId={master.UserId}&companyId={company.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
        var list = await listResponse.Content.ReadFromJsonAsync<List<WorkingHoursDto>>();
        list.Should().ContainSingle();
    }

    // ── DELETE /api/workinghours/{id} ─────────────────────────────────────────

    [Fact, TestCase("WH-007")]
    public async Task Delete_ByOwner_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var wh = await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, NextWeekday());

        var response = await AuthedClient(owner.Token).DeleteAsync($"/api/workinghours/{wh.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("WH-008")]
    public async Task Delete_BySelfMaster_Succeeds()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var wh = await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, NextWeekday());

        var response = await AuthedClient(master.Token).DeleteAsync($"/api/workinghours/{wh.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("WH-009")]
    public async Task Delete_ByUnrelatedUser_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var wh = await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, NextWeekday());
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).DeleteAsync($"/api/workinghours/{wh.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("WH-010")]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        var user = await RegisterAsync();

        var response = await AuthedClient(user.Token).DeleteAsync($"/api/workinghours/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Predicate fix (US-04, audit A5) ────────────────────────────────────────

    [Fact, TestCase("WH-011")]
    public async Task Get_ByUnrelatedAuthenticatedUser_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var stranger = await RegisterAsync();
        var from = NextWeekday();

        var response = await AuthedClient(stranger.Token).GetAsync(
            $"/api/workinghours?masterId={master.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={from:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("WH-012")]
    public async Task Get_ByMasterOfSameCompany_AboutAnotherMaster_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master1 = await AddMasterAsync(owner.Token, company.Id);
        var master2 = await AddMasterAsync(owner.Token, company.Id);
        var from = NextWeekday();

        var response = await AuthedClient(master1.Token).GetAsync(
            $"/api/workinghours?masterId={master2.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={from:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("WH-013")]
    public async Task Get_ByMasterAboutThemselves_InTheirOwnCompany_ReturnsOk()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var from = NextWeekday();

        var response = await AuthedClient(master.Token).GetAsync(
            $"/api/workinghours?masterId={master.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={from:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("WH-014")]
    public async Task Get_ByOwnerAboutTheirMaster_ReturnsOk()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var from = NextWeekday();

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/workinghours?masterId={master.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={from:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("WH-015")]
    public async Task Put_MasterInACompanyTheyDoNotBelongTo_ReturnsForbidden_AndWritesNothing()
    {
        // Audit A5: requesterId == masterId alone used to be enough, regardless of companyId — a
        // master could set themselves working hours inside a company they never joined.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var (otherOwner, otherCompany) = await CreateOwnerWithCompanyAsync();
        var date = NextWeekday();

        var dto = new UpsertWorkingHoursDto(master.UserId, otherCompany.Id, date, true,
            new TimeOnly(9, 0), new TimeOnly(18, 0), []);
        var response = await AuthedClient(master.Token).PutAsJsonAsync("/api/workinghours", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var check = await AuthedClient(otherOwner.Token).GetAsync(
            $"/api/workinghours?masterId={master.UserId}&companyId={otherCompany.Id}&from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
        check.StatusCode.Should().Be(HttpStatusCode.OK);
        var hours = await check.Content.ReadFromJsonAsync<List<WorkingHoursDto>>();
        hours.Should().BeEmpty();
    }

    [Fact, TestCase("WH-016")]
    public async Task Put_ByFormerMemberAfterRemoval_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var membersResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/members");
        var members = await membersResponse.Content.ReadFromJsonAsync<List<ServiceBooking.API.DTOs.Companies.MemberDto>>();
        var memberId = members!.Single(m => m.UserId == master.UserId).Id;

        var removeResponse = await AuthedClient(owner.Token).DeleteAsync($"/api/companies/{company.Id}/members/{memberId}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var date = NextWeekday();
        var dto = new UpsertWorkingHoursDto(master.UserId, company.Id, date, true,
            new TimeOnly(9, 0), new TimeOnly(18, 0), []);
        var response = await AuthedClient(master.Token).PutAsJsonAsync("/api/workinghours", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Atomicity of Upsert (audit B3) ───────────────────────────────────────

    [Fact, TestCase("WH-017")]
    public async Task Put_ConcurrentRequestsForSameDay_ResultInExactlyOneRow()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var date = NextWeekday();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            AuthedClient(master.Token).PutAsJsonAsync("/api/workinghours",
                new UpsertWorkingHoursDto(master.UserId, company.Id, date, true,
                    new TimeOnly(9, 0), new TimeOnly(18 - i, 0), []))));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceBooking.Infrastructure.Data.AppDbContext>();
        var count = await db.WorkingHours.CountAsync(wh =>
            wh.MasterId == master.UserId && wh.CompanyId == company.Id && wh.Date == date);
        count.Should().Be(1);
    }
}
