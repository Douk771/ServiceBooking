using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.WorkingHours;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class WorkingHoursTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
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
}
