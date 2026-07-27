using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.WorkingHours;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class ScheduleTemplateTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── GET /api/schedule-template ────────────────────────────────────────────

    [Fact, TestCase("ST-001")]
    public async Task Get_ReturnsEmptyList_WhenNoTemplateSaved()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var response = await AuthedClient(owner.Token).GetAsync(
            $"/api/schedule-template?masterId={master.UserId}&companyId={company.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<TemplateItemDto>>();
        items.Should().BeEmpty();
    }

    // ── PUT /api/schedule-template ────────────────────────────────────────────

    [Fact, TestCase("ST-002")]
    public async Task Put_SavesTemplate_AndGetReturnsExactlyWhatWasSaved()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var days = Enumerable.Range(1, 7)
            .Select(d => new DayTemplate(d, d <= 5, new TimeOnly(9, 0), new TimeOnly(18, 0)))
            .ToList();

        var putResponse = await AuthedClient(owner.Token).PutAsJsonAsync("/api/schedule-template",
            new PutTemplateRequest(master.UserId, company.Id, days));
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await AuthedClient(owner.Token).GetAsync(
            $"/api/schedule-template?masterId={master.UserId}&companyId={company.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await getResponse.Content.ReadFromJsonAsync<List<TemplateItemDto>>())!;

        items.Should().HaveCount(7);
        items.Should().OnlyContain(i => i.StartTime == new TimeOnly(9, 0) && i.EndTime == new TimeOnly(18, 0));
        items.Select(i => i.DayOfWeek).Should().BeEquivalentTo(Enumerable.Range(1, 7));
        items.Where(i => i.DayOfWeek <= 5).Should().OnlyContain(i => i.IsWorking);
        items.Where(i => i.DayOfWeek > 5).Should().OnlyContain(i => !i.IsWorking);
    }

    [Fact, TestCase("ST-003")]
    public async Task Put_CalledAgainWithDifferentDays_ReplacesOldEntirely()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var firstDays = new List<DayTemplate>
        {
            new(1, true, new TimeOnly(9, 0), new TimeOnly(18, 0)),
            new(2, true, new TimeOnly(9, 0), new TimeOnly(18, 0)),
        };
        var firstPut = await AuthedClient(owner.Token).PutAsJsonAsync("/api/schedule-template",
            new PutTemplateRequest(master.UserId, company.Id, firstDays));
        firstPut.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondDays = new List<DayTemplate> { new(3, true, new TimeOnly(10, 0), new TimeOnly(16, 0)) };
        var secondPut = await AuthedClient(owner.Token).PutAsJsonAsync("/api/schedule-template",
            new PutTemplateRequest(master.UserId, company.Id, secondDays));
        secondPut.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await AuthedClient(owner.Token).GetAsync(
            $"/api/schedule-template?masterId={master.UserId}&companyId={company.Id}");
        var items = await getResponse.Content.ReadFromJsonAsync<List<TemplateItemDto>>();

        items.Should().ContainSingle();
        items![0].DayOfWeek.Should().Be(3);
        items[0].StartTime.Should().Be(new TimeOnly(10, 0));
    }

    [Fact, TestCase("ST-004")]
    public async Task Put_ByUnrelatedAuthenticatedUser_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var stranger = await RegisterAsync();

        var days = new List<DayTemplate> { new(1, true, new TimeOnly(9, 0), new TimeOnly(18, 0)) };

        // ScheduleTemplateController.CanManage requires the caller to be SuperAdmin, the target
        // master themselves, or the CompanyOwner of the target company. A random authenticated
        // stranger is none of those.
        var response = await AuthedClient(stranger.Token).PutAsJsonAsync("/api/schedule-template",
            new PutTemplateRequest(master.UserId, company.Id, days));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ST-005")]
    public async Task Get_ByUnrelatedAuthenticatedUser_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var stranger = await RegisterAsync();

        var response = await AuthedClient(stranger.Token).GetAsync(
            $"/api/schedule-template?masterId={master.UserId}&companyId={company.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ST-006")]
    public async Task Apply_ByUnrelatedAuthenticatedUser_ReturnsForbidden()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var stranger = await RegisterAsync();
        var from = NextWeekday();
        var to = from.AddDays(6);

        var response = await AuthedClient(stranger.Token).PostAsync(
            $"/api/schedule-template/apply?masterId={master.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("ST-007")]
    public async Task Put_ByTheMasterThemselves_Succeeds_EvenThoughNotCompanyOwner()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var days = new List<DayTemplate> { new(1, true, new TimeOnly(9, 0), new TimeOnly(18, 0)) };

        // CanManage explicitly allows requesterId == masterId — a master manages their own template.
        var response = await AuthedClient(master.Token).PutAsJsonAsync("/api/schedule-template",
            new PutTemplateRequest(master.UserId, company.Id, days));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("ST-008")]
    public async Task Put_BySuperAdmin_Succeeds_ForAnyMasterAndCompany()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var admin = await LoginAsSuperAdminAsync();
        var days = new List<DayTemplate> { new(2, true, new TimeOnly(9, 0), new TimeOnly(18, 0)) };

        var response = await AuthedClient(admin.Token).PutAsJsonAsync("/api/schedule-template",
            new PutTemplateRequest(master.UserId, company.Id, days));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── POST /api/schedule-template/apply ─────────────────────────────────────

    [Fact, TestCase("ST-009")]
    public async Task Apply_NoTemplateExists_ReturnsBadRequest()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var from = NextWeekday();
        var to = from.AddDays(6);

        var response = await AuthedClient(owner.Token).PostAsync(
            $"/api/schedule-template/apply?masterId={master.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("ST-010")]
    public async Task Apply_FreshDateRange_CreatesWorkingHoursMatchingTemplate()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        var days = Enumerable.Range(1, 7)
            .Select(d => new DayTemplate(d, true, new TimeOnly(8, 0), new TimeOnly(16, 0)))
            .ToList();
        var putResponse = await AuthedClient(owner.Token).PutAsJsonAsync("/api/schedule-template",
            new PutTemplateRequest(master.UserId, company.Id, days));
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var from = NextWeekday();
        var to = from.AddDays(6);
        var applyResponse = await AuthedClient(owner.Token).PostAsync(
            $"/api/schedule-template/apply?masterId={master.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
            null);
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var whResponse = await AuthedClient(owner.Token).GetAsync(
            $"/api/workinghours?masterId={master.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={from:yyyy-MM-dd}");
        var whList = await whResponse.Content.ReadFromJsonAsync<List<WorkingHoursDto>>();

        whList.Should().ContainSingle(wh => wh.IsWorking && wh.StartTime == new TimeOnly(8, 0) && wh.EndTime == new TimeOnly(16, 0));
    }

    [Fact, TestCase("ST-011")]
    public async Task Apply_ExistingWorkingHoursEntry_GetsOverwrittenNotDuplicated()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var from = NextWeekday();
        var to = from.AddDays(6);

        // Pre-existing WorkingHours row for `from`, with hours that differ from the template.
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, from,
            start: new TimeOnly(7, 0), end: new TimeOnly(11, 0));

        var iso = from.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)from.DayOfWeek;
        var days = new List<DayTemplate> { new(iso, true, new TimeOnly(9, 0), new TimeOnly(20, 0)) };
        var putResponse = await AuthedClient(owner.Token).PutAsJsonAsync("/api/schedule-template",
            new PutTemplateRequest(master.UserId, company.Id, days));
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var applyResponse = await AuthedClient(owner.Token).PostAsync(
            $"/api/schedule-template/apply?masterId={master.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
            null);
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var whResponse = await AuthedClient(owner.Token).GetAsync(
            $"/api/workinghours?masterId={master.UserId}&companyId={company.Id}&from={from:yyyy-MM-dd}&to={from:yyyy-MM-dd}");
        var whList = await whResponse.Content.ReadFromJsonAsync<List<WorkingHoursDto>>();

        whList.Should().ContainSingle();
        whList![0].StartTime.Should().Be(new TimeOnly(9, 0));
        whList[0].EndTime.Should().Be(new TimeOnly(20, 0));
    }

    private record TemplateItemDto(Guid Id, int DayOfWeek, bool IsWorking, TimeOnly StartTime, TimeOnly EndTime);
}
