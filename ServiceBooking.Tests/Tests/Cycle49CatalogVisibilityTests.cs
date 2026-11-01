using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 49, «Вызов 2»: видимость салона в каталоге по активной услуге и сотруднику (US-49-01, US-49-02).
/// Написано по SPEC.md, независимо от реализации.
/// </summary>
public class Cycle49CatalogVisibilityTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private static string Url(Guid id) => $"/api/companies/{id}/catalog-listing";

    private async Task<JsonElement> GetListingAsync(string token, Guid id)
    {
        var r = await AuthedClient(token).GetAsync(Url(id));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string[] Codes(JsonElement e) =>
        e.GetProperty("checklist").EnumerateArray().Select(x => x.GetProperty("code").GetString()!).ToArray();

    private static bool Done(JsonElement e, string code) =>
        e.GetProperty("checklist").EnumerateArray().Single(x => x.GetProperty("code").GetString() == code).GetProperty("done").GetBoolean();

    private async Task<bool> InGetAllAsync(Guid id) =>
        (await AnonymousClient().GetFromJsonAsync<List<CompanyDto>>("/api/companies"))!.Any(c => c.Id == id);

    private async Task<(bool Present, int Total)> InPublicAsync(CompanyDto company)
    {
        var page = await AnonymousClient().GetFromJsonAsync<JsonElement>(
            $"/api/companies/public?pageSize=100&search={Uri.EscapeDataString(company.Name)}");
        return (page.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("id").GetGuid() == company.Id),
            page.GetProperty("total").GetInt32());
    }

    private async Task AssertCatalogAsync(CompanyDto company, bool expected, string label)
    {
        (await InGetAllAsync(company.Id)).Should().Be(expected, "GET /api/companies: " + label);
        var (present, total) = await InPublicAsync(company);
        present.Should().Be(expected, "GET /api/companies/public: " + label);
        total.Should().Be(expected ? 1 : 0, "total считает только видимые: " + label);
    }

    private Task SetOwnerProvidesAsync(Guid companyId, bool value) =>
        WithDbAsync(async db =>
        {
            var m = await db.CompanyMembers.SingleAsync(x => x.CompanyId == companyId && x.Role == UserRole.CompanyOwner);
            m.ProvidesServices = value;
            await db.SaveChangesAsync();
        });

    private Task SetAllServicesActiveAsync(Guid companyId, bool value) =>
        WithDbAsync(async db =>
        {
            foreach (var s in await db.Services.Where(x => x.CompanyId == companyId).ToListAsync()) s.IsActive = value;
            await db.SaveChangesAsync();
        });

    private Task SetShowAsync(Guid id, bool show) =>
        WithDbAsync(async db =>
        {
            (await db.Companies.SingleAsync(x => x.Id == id)).ShowInPublicListing = show;
            await db.SaveChangesAsync();
        });

    [Fact, TestCase("CY49-01")]
    public async Task NoServices_NotInCatalogs_ChecklistExplains()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        await AssertCatalogAsync(company, false, "нет услуг");
        var e = await GetListingAsync(owner.Token, company.Id);
        e.GetProperty("visible").GetBoolean().Should().BeFalse();
        Done(e, "NoActiveServices").Should().BeFalse();
        Done(e, "NoServiceProviders").Should().BeTrue("владелец оказывает услуги по умолчанию");
    }

    [Fact, TestCase("CY49-02")]
    public async Task AllServicesInactive_NotInCatalogs()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        await CreateServiceAsync(owner.Token, company.Id);
        await CreateServiceAsync(owner.Token, company.Id);
        await SetAllServicesActiveAsync(company.Id, false);
        await AssertCatalogAsync(company, false, "все услуги выключены");
        Done(await GetListingAsync(owner.Token, company.Id), "NoActiveServices").Should().BeFalse();
    }

    [Fact, TestCase("CY49-03")]
    public async Task ServiceButNobodyProvides_NotInCatalogs()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        await CreateServiceAsync(owner.Token, company.Id);
        await SetOwnerProvidesAsync(company.Id, false);
        await AssertCatalogAsync(company, false, "никто не оказывает услуги");
        var e = await GetListingAsync(owner.Token, company.Id);
        Done(e, "NoActiveServices").Should().BeTrue();
        Done(e, "NoServiceProviders").Should().BeFalse();
        e.GetProperty("visible").GetBoolean().Should().BeFalse();
    }

    [Fact, TestCase("CY49-04")]
    public async Task OnlyOwnerProviding_OneActiveService_NoMasterServiceLink_InCatalogs()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        await CreateServiceAsync(owner.Token, company.Id);
        await AssertCatalogAsync(company, true, "только владелец, без привязок");
        (await GetListingAsync(owner.Token, company.Id)).GetProperty("visible").GetBoolean().Should().BeTrue();
    }

    [Fact, TestCase("CY49-05")]
    public async Task ClientMemberWithProvidesServices_DoesNotCount()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        await CreateServiceAsync(owner.Token, company.Id);
        await SetOwnerProvidesAsync(company.Id, false);
        var client = await RegisterAsync();
        await WithDbAsync(async db =>
        {
            db.CompanyMembers.Add(new CompanyMember
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, UserId = client.UserId,
                Role = UserRole.Client, ProvidesServices = true
            });
            await db.SaveChangesAsync();
        });
        await AssertCatalogAsync(company, false, "клиент с ProvidesServices=true");
        Done(await GetListingAsync(owner.Token, company.Id), "NoServiceProviders").Should().BeFalse();
    }

    [Fact, TestCase("CY49-06")]
    public async Task MasterProviding_OwnerNot_InCatalogs_MasterOff_NotInCatalogs()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        await CreateServiceAsync(owner.Token, company.Id);
        await SetOwnerProvidesAsync(company.Id, false);
        var master = await AddMasterAsync(owner.Token, company.Id);
        await AssertCatalogAsync(company, true, "мастер оказывает услуги");

        await WithDbAsync(async db =>
        {
            (await db.CompanyMembers.SingleAsync(x => x.CompanyId == company.Id && x.UserId == master.UserId)).ProvidesServices = false;
            await db.SaveChangesAsync();
        });
        await AssertCatalogAsync(company, false, "мастер выключен");
    }

    [Fact, TestCase("CY49-07")]
    public async Task Dynamic_AddAndRemoveLastService_AppearsAndDisappears()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        await AssertCatalogAsync(company, false, "до услуги");
        await CreateServiceAsync(owner.Token, company.Id);
        await AssertCatalogAsync(company, true, "после услуги");
        await SetAllServicesActiveAsync(company.Id, false);
        await AssertCatalogAsync(company, false, "после выключения услуги");
        await SetAllServicesActiveAsync(company.Id, true);
        await SetOwnerProvidesAsync(company.Id, false);
        await AssertCatalogAsync(company, false, "после выключения «оказывает услуги»");
        await SetOwnerProvidesAsync(company.Id, true);
        await AssertCatalogAsync(company, true, "после включения");
    }

    [Fact, TestCase("CY49-08")]
    public async Task Checklist_AlwaysHasNewItems_InSpecOrder_WithConditionsText()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await CreateServiceAsync(owner.Token, company.Id);
        await SetShowAsync(company.Id, true);
        var e = await GetListingAsync(owner.Token, company.Id);
        Codes(e).Should().Equal("NoActiveServices", "NoServiceProviders", "HiddenByOwner");
        var items = e.GetProperty("checklist").EnumerateArray().ToArray();
        items[0].GetProperty("text").GetString().Should().Be("Есть услуги");
        items[1].GetProperty("text").GetString().Should().Be("Есть сотрудники, оказывающие услуги");
        e.GetProperty("conditionsText").GetString().Should()
            .Be("Салон появится в каталоге ezbook.ru, только когда выполнены все условия ниже.", "показывается и когда салон виден");
        e.GetProperty("statusText").GetString().Should().Be("Салон виден в каталоге ezbook.ru");
        e.GetProperty("checklist").EnumerateArray().All(i => i.GetProperty("done").GetBoolean()).Should().Be(e.GetProperty("visible").GetBoolean());

        await SetAllServicesActiveAsync(company.Id, false);
        var off = await GetListingAsync(owner.Token, company.Id);
        off.GetProperty("statusText").GetString().Should().Be("Салона сейчас нет в каталоге");
        off.GetProperty("conditionsText").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact, TestCase("CY49-09")]
    public async Task Checklist_BlockedAndPlanOrder()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var plan = await CreateTestPlanConfigAsync(allowPublicListing: false);
        await SetSubscriptionAsync(company.Id, plan);
        await WithDbAsync(async db =>
        {
            (await db.Companies.SingleAsync(x => x.Id == company.Id)).IsActive = false;
            await db.SaveChangesAsync();
        });
        Codes(await GetListingAsync(owner.Token, company.Id))
            .Should().Equal("SalonBlocked", "NoActiveServices", "NoServiceProviders", "NotAllowedByPlan", "HiddenByOwner");
    }

    [Fact, TestCase("CY49-10")]
    public async Task Put_ShowOn_WithoutServices_Is200_AndReportsMissingItems()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, false);
        var put = await AuthedClient(owner.Token).PutJsonAsync(Url(company.Id), new { showInCatalog = true });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var e = await put.Content.ReadFromJsonAsync<JsonElement>();
        e.GetProperty("visible").GetBoolean().Should().BeFalse();
        Done(e, "NoActiveServices").Should().BeFalse();
        Done(e, "HiddenByOwner").Should().BeTrue();
    }

    [Fact, TestCase("CY49-11")]
    public async Task Matrix_ServiceXProviderXShow_VisibleMatchesChecklistAndBothCatalogs()
    {
        foreach (var hasService in new[] { true, false })
        foreach (var provides in new[] { true, false })
        foreach (var show in new[] { true, false })
        {
            var (owner, company) = await CreateOwnerWithCompanyAsync();
            if (hasService) await CreateServiceAsync(owner.Token, company.Id);
            await SetOwnerProvidesAsync(company.Id, provides);
            await SetShowAsync(company.Id, show);
            var expected = hasService && provides && show;
            var label = $"service={hasService} provides={provides} show={show}";
            var e = await GetListingAsync(owner.Token, company.Id);
            e.GetProperty("visible").GetBoolean().Should().Be(expected, label);
            e.GetProperty("checklist").EnumerateArray().All(i => i.GetProperty("done").GetBoolean()).Should().Be(expected, label);
            await AssertCatalogAsync(company, expected, label);
        }
    }

    [Fact, TestCase("CY49-12")]
    public async Task UnlistedSalon_DirectSlugPageStillWorks()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        await AssertCatalogAsync(company, false, "без услуг");
        var r = await AnonymousClient().GetAsync($"/api/companies/{company.Slug}");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    [Fact, TestCase("CY49-13")]
    public async Task Access_Stranger404_Anonymous401_ShopCodesUnchanged()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();
        (await AuthedClient(stranger.Token).GetAsync(Url(company.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync(Url(company.Id))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var shop = await CreateShopAsync();
        var r = await AuthedClient(shop.OwnerToken).GetAsync($"/api/shops/{shop.Id}/catalog-listing");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var e = await r.Content.ReadFromJsonAsync<JsonElement>();
        Codes(e).Should().NotContain(new[] { "NoActiveServices", "NoServiceProviders" });
    }

    [Fact, TestCase("CY49-14")]
    public async Task Public_CityFilterSearchPaging_StillWorkWithNewRule()
    {
        var (o1, c1) = await CreateOwnerWithCompanyAsync();
        var (o2, c2) = await CreateOwnerWithCompanyAsync();
        await CreateServiceAsync(o1.Token, c1.Id);
        await SetShowAsync(c1.Id, true);
        await SetShowAsync(c2.Id, true);
        var page = await AnonymousClient().GetFromJsonAsync<JsonElement>("/api/companies/public?page=1&pageSize=1");
        page.GetProperty("items").GetArrayLength().Should().BeLessThanOrEqualTo(1);
        var byName = await AnonymousClient().GetFromJsonAsync<JsonElement>($"/api/companies/public?pageSize=100&search={Uri.EscapeDataString(c2.Name)}");
        byName.GetProperty("total").GetInt32().Should().Be(0, "c2 без услуг");
        var byName1 = await AnonymousClient().GetFromJsonAsync<JsonElement>($"/api/companies/public?pageSize=100&search={Uri.EscapeDataString(c1.Name)}");
        byName1.GetProperty("total").GetInt32().Should().Be(1);
    }
}
