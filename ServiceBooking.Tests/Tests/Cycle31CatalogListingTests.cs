using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 31, «Вызов 2»: блок «Каталог ezbook.ru» салона (US-31-05), текст пункта магазина (US-31-04).
/// Написано по SPEC.md и API_CONTRACT_CYCLE31.md §31.21–§31.24, а не по реализации.
/// </summary>
public class Cycle31CatalogListingTests(TestDatabaseFixture fixture) : Cycle25TestBase(fixture)
{
    private const string OnText = "Салон виден в каталоге ezbook.ru";
    private const string OffText = "Салона сейчас нет в каталоге";
    private const string HiddenByOwnerText = "Показ включен в настройках";
    private const string PlanText = "Показ в каталоге не входит в ваш тариф — повысьте тариф, чтобы включить";
    private const string ShopRefusal = "Это магазин: записи, услуги и расписание для него недоступны.";

    private static string Url(Guid id) => $"/api/companies/{id}/catalog-listing";

    private async Task<JsonElement> GetOkAsync(string token, Guid id)
    {
        var r = await AuthedClient(token).GetAsync(Url(id));
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> PutAsync(string token, Guid id, object body) =>
        await AuthedClient(token).PutJsonAsync(Url(id), body);

    private static string[] Codes(JsonElement e) =>
        e.GetProperty("checklist").EnumerateArray().Select(x => x.GetProperty("code").GetString()!).ToArray();

    private async Task<bool> InGetAllAsync(Guid id)
    {
        var list = await AnonymousClient().GetFromJsonAsync<List<CompanyDto>>("/api/companies");
        return list!.Any(c => c.Id == id);
    }

    private async Task<bool> InPublicAsync(CompanyDto company)
    {
        var page = await AnonymousClient().GetFromJsonAsync<JsonElement>(
            $"/api/companies/public?pageSize=100&search={Uri.EscapeDataString(company.Name)}");
        return page.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("id").GetGuid() == company.Id);
    }

    private async Task SetShowAsync(Guid id, bool show) =>
        await WithDbAsync(async db =>
        {
            var c = await db.Companies.SingleAsync(x => x.Id == id);
            c.ShowInPublicListing = show;
            await db.SaveChangesAsync();
        });

    private async Task<bool> ShowColumnAsync(Guid id) =>
        await WithDbAsync(db => db.Companies.AsNoTracking().Where(c => c.Id == id).Select(c => c.ShowInPublicListing).SingleAsync());

    private async Task SetActiveAsync(Guid id, bool active) =>
        await WithDbAsync(async db =>
        {
            var c = await db.Companies.SingleAsync(x => x.Id == id);
            c.IsActive = active;
            await db.SaveChangesAsync();
        });

    private async Task<(AuthResponseDto Owner, CompanyDto Company)> NoListingPlanSalonAsync()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: false);
        var plan = await CreateTestPlanConfigAsync(allowPublicListing: false);
        await SetSubscriptionAsync(company.Id, plan);
        return (owner, company);
    }

    [Fact, TestCase("CY31-01")]
    public async Task Owner_AllOk_Visible_SingleDoneItem()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        var e = await GetOkAsync(owner.Token, company.Id);
        e.GetProperty("visible").GetBoolean().Should().BeTrue();
        e.GetProperty("showInCatalog").GetBoolean().Should().BeTrue();
        e.GetProperty("allowedByPlan").GetBoolean().Should().BeTrue();
        e.GetProperty("statusText").GetString().Should().Be(OnText);
        e.TryGetProperty("notAllowedByPlanText", out var np).Should().BeTrue("поле всегда присутствует");
        np.ValueKind.Should().Be(JsonValueKind.Null);
        var items = e.GetProperty("checklist").EnumerateArray().ToArray();
        items.Should().HaveCount(1);
        items[0].GetProperty("code").GetString().Should().Be("HiddenByOwner");
        items[0].GetProperty("text").GetString().Should().Be(HiddenByOwnerText);
        items[0].GetProperty("done").GetBoolean().Should().BeTrue();
    }

    [Fact, TestCase("CY31-02")]
    public async Task Owner_ShowOff_NotVisible_ItemNotDone()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, false);
        var e = await GetOkAsync(owner.Token, company.Id);
        e.GetProperty("visible").GetBoolean().Should().BeFalse();
        e.GetProperty("statusText").GetString().Should().Be(OffText);
        var item = e.GetProperty("checklist").EnumerateArray().Single();
        item.GetProperty("text").GetString().Should().Be(HiddenByOwnerText);
        item.GetProperty("done").GetBoolean().Should().BeFalse();
    }

    [Fact, TestCase("CY31-03")]
    public async Task PlanDisallows_TwoItems_PlanTextPresent()
    {
        var (owner, company) = await NoListingPlanSalonAsync();
        await SetShowAsync(company.Id, true);
        var e = await GetOkAsync(owner.Token, company.Id);
        e.GetProperty("allowedByPlan").GetBoolean().Should().BeFalse();
        e.GetProperty("visible").GetBoolean().Should().BeFalse();
        e.GetProperty("notAllowedByPlanText").GetString().Should().Be(PlanText);
        Codes(e).Should().Equal("NotAllowedByPlan", "HiddenByOwner");
        var items = e.GetProperty("checklist").EnumerateArray().ToArray();
        items[0].GetProperty("text").GetString().Should().Be("Показ в каталоге не входит в ваш тариф");
        items[0].GetProperty("done").GetBoolean().Should().BeFalse();
        items[1].GetProperty("done").GetBoolean().Should().BeTrue();
    }

    [Fact, TestCase("CY31-04")]
    public async Task InactiveSalon_FirstItemSalonBlocked_AllThreeNotDone()
    {
        var (owner, company) = await NoListingPlanSalonAsync();
        await SetShowAsync(company.Id, false);
        await SetActiveAsync(company.Id, false);
        var e = await GetOkAsync(owner.Token, company.Id);
        Codes(e).Should().Equal("SalonBlocked", "NotAllowedByPlan", "HiddenByOwner");
        var items = e.GetProperty("checklist").EnumerateArray().ToArray();
        items[0].GetProperty("text").GetString().Should().Be("Салон заблокирован администратором");
        items.Should().OnlyContain(i => !i.GetProperty("done").GetBoolean());
        e.GetProperty("visible").GetBoolean().Should().BeFalse();
    }

    [Fact, TestCase("CY31-05")]
    public async Task Matrix_ActiveXPlanXShow_VisibleFlagMatchesBothCatalogs()
    {
        var plan = await CreateTestPlanConfigAsync(allowPublicListing: false);
        foreach (var active in new[] { true, false })
        foreach (var planAllows in new[] { true, false })
        foreach (var show in new[] { true, false })
        {
            var (owner, company) = await CreateOwnerWithCompanyAsync(attachPlan: planAllows ? true : false);
            if (!planAllows) await SetSubscriptionAsync(company.Id, plan);
            await SetShowAsync(company.Id, show);
            await SetActiveAsync(company.Id, active);

            var expected = active && planAllows && show;
            var label = $"active={active} plan={planAllows} show={show}";
            var e = await GetOkAsync(owner.Token, company.Id);
            e.GetProperty("visible").GetBoolean().Should().Be(expected, label);
            (await InGetAllAsync(company.Id)).Should().Be(expected, "GET /api/companies: " + label);
            (await InPublicAsync(company)).Should().Be(expected, "GET /api/companies/public: " + label);
            e.GetProperty("checklist").EnumerateArray().All(i => i.GetProperty("done").GetBoolean())
                .Should().Be(expected, "visible <=> все пункты done: " + label);
        }
    }

    [Fact, TestCase("CY31-06")]
    public async Task Put_TrueFalse_AppearsAndDisappearsInCatalogs_Idempotent()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, false);
        (await InGetAllAsync(company.Id)).Should().BeFalse();

        var on = await PutAsync(owner.Token, company.Id, new { showInCatalog = true });
        on.StatusCode.Should().Be(HttpStatusCode.OK, await on.Content.ReadAsStringAsync());
        var dto = await on.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("visible").GetBoolean().Should().BeTrue("ответ — состояние после сохранения");
        dto.GetProperty("statusText").GetString().Should().Be(OnText);
        (await InGetAllAsync(company.Id)).Should().BeTrue();
        (await InPublicAsync(company)).Should().BeTrue();
        (await PutAsync(owner.Token, company.Id, new { showInCatalog = true })).StatusCode.Should().Be(HttpStatusCode.OK);

        var off = await PutAsync(owner.Token, company.Id, new { showInCatalog = false });
        off.StatusCode.Should().Be(HttpStatusCode.OK);
        (await off.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("visible").GetBoolean().Should().BeFalse();
        (await InGetAllAsync(company.Id)).Should().BeFalse();
        (await InPublicAsync(company)).Should().BeFalse();
        (await PutAsync(owner.Token, company.Id, new { showInCatalog = false })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY31-07")]
    public async Task Put_TrueOnPlanWithoutListing_409Json_ColumnUnchanged_FalseStill200()
    {
        var (owner, company) = await NoListingPlanSalonAsync();
        await SetShowAsync(company.Id, false);
        var r = await PutAsync(owner.Token, company.Id, new { showInCatalog = true });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        r.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        j.GetProperty("code").GetString().Should().Be("CatalogListingNotAllowedByPlan");
        j.GetProperty("message").GetString().Should().Be(PlanText);
        (await ShowColumnAsync(company.Id)).Should().BeFalse();

        await SetShowAsync(company.Id, true);
        (await PutAsync(owner.Token, company.Id, new { showInCatalog = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ShowColumnAsync(company.Id)).Should().BeFalse();
    }

    [Fact, TestCase("CY31-07b")]
    public async Task Put_False_WorksForBlockedSalon()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        await SetActiveAsync(company.Id, false);
        (await PutAsync(owner.Token, company.Id, new { showInCatalog = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ShowColumnAsync(company.Id)).Should().BeFalse();
    }

    [Fact, TestCase("CY31-08")]
    public async Task Put_MissingOrNullField_400Text_ColumnUnchanged()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetShowAsync(company.Id, true);
        foreach (var body in new object[] { new { }, new { showInCatalog = (bool?)null } })
        {
            var r = await PutAsync(owner.Token, company.Id, body);
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await r.Content.ReadAsStringAsync()).Should().Contain("Не указано, показывать ли салон в каталоге");
        }
        (await ShowColumnAsync(company.Id)).Should().BeTrue();
    }

    [Fact, TestCase("CY31-08b")]
    public async Task Put_NonJsonBody_415_Or_400()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var r = await AuthedClient(owner.Token).PutAsync(Url(company.Id), new StringContent("showInCatalog=true", System.Text.Encoding.UTF8, "text/plain"));
        r.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var bad = await AuthedClient(owner.Token).PutAsync(Url(company.Id), new StringContent("{oops", System.Text.Encoding.UTF8, "application/json"));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY31-09")]
    public async Task Stranger_404_SameAsRandomGuid_Anonymous_401()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var stranger = await RegisterAsync();
        var before = await ShowColumnAsync(company.Id);

        var foreignGet = await AuthedClient(stranger.Token).GetAsync(Url(company.Id));
        var randomGet = await AuthedClient(stranger.Token).GetAsync(Url(Guid.NewGuid()));
        foreignGet.StatusCode.Should().Be(HttpStatusCode.NotFound);
        randomGet.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreignGet.Content.ReadAsStringAsync()).Should().Be(await randomGet.Content.ReadAsStringAsync());

        (await PutAsync(stranger.Token, company.Id, new { showInCatalog = !before })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PutAsync(stranger.Token, Guid.NewGuid(), new { showInCatalog = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ShowColumnAsync(company.Id)).Should().Be(before);

        (await AnonymousClient().GetAsync(Url(company.Id))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AnonymousClient().PutAsJsonAsync(Url(company.Id), new { showInCatalog = true })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("CY31-10")]
    public async Task SuperAdmin_CanGetAndPut()
    {
        var (_, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();
        (await AuthedClient(admin.Token).GetAsync(Url(company.Id))).StatusCode.Should().Be(HttpStatusCode.OK);
        var r = await PutAsync(admin.Token, company.Id, new { showInCatalog = false });
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        (await ShowColumnAsync(company.Id)).Should().BeFalse();
    }

    [Fact, TestCase("CY31-11")]
    public async Task ShopOwner_OnShopId_409ShopText_Unchanged_StrangerGets404()
    {
        var shop = await CreateShopAsync();
        var before = await ShowColumnAsync(shop.Id);
        var g = await AuthedClient(shop.OwnerToken).GetAsync(Url(shop.Id));
        g.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await g.Content.ReadAsStringAsync()).Should().Contain(ShopRefusal);
        var p = await PutAsync(shop.OwnerToken, shop.Id, new { showInCatalog = !before });
        p.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await p.Content.ReadAsStringAsync()).Should().Contain(ShopRefusal);
        (await ShowColumnAsync(shop.Id)).Should().Be(before);

        var stranger = await RegisterAsync();
        (await AuthedClient(stranger.Token).GetAsync(Url(shop.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY31-12")]
    public async Task Master_NotOwner_404()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        (await AuthedClient(master.Token).GetAsync(Url(company.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PutAsync(master.Token, company.Id, new { showInCatalog = false })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY31-13")]
    public async Task LegacyPutCompany_WithField_ChangesFlag_WithoutField_KeepsIt()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var client = AuthedClient(owner.Token);
        (await client.PutAsJsonAsync($"/api/companies/{company.Id}", new { showInPublicListing = false })).EnsureSuccessStatusCode();
        (await ShowColumnAsync(company.Id)).Should().BeFalse();
        (await client.PutAsJsonAsync($"/api/companies/{company.Id}", new { showInPublicListing = true })).EnsureSuccessStatusCode();
        (await ShowColumnAsync(company.Id)).Should().BeTrue();

        (await PutAsync(owner.Token, company.Id, new { showInCatalog = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        // основная форма кабинета больше не шлёт поле: другое изменение не должно вернуть показ
        var save = await client.PutAsJsonAsync($"/api/companies/{company.Id}", new { description = "Новое описание " + Guid.NewGuid().ToString("N")[..6] });
        save.EnsureSuccessStatusCode();
        (await ShowColumnAsync(company.Id)).Should().BeFalse("PUT без поля не затирает значение, выставленное переключателем");
        (await client.PutAsJsonAsync($"/api/companies/{company.Id}", new { showInPublicListing = (bool?)null, description = "x" })).EnsureSuccessStatusCode();
        (await ShowColumnAsync(company.Id)).Should().BeFalse("null = не менять");
    }

    [Fact, TestCase("CY31-14")]
    public async Task Shop_HiddenByOwnerText_IsNewWording_BothStates_CodesUnchanged()
    {
        var shop = await CreateShopAsync();
        var client = AuthedClient(shop.OwnerToken);
        foreach (var state in new[] { true, false })
        {
            var put = await client.PutJsonAsync($"/api/shops/{shop.Id}/catalog-listing", new { showInCatalog = state });
            put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
            var get = await client.GetAsync($"/api/shops/{shop.Id}/catalog-listing");
            get.StatusCode.Should().Be(HttpStatusCode.OK);
            var e = await get.Content.ReadFromJsonAsync<JsonElement>();
            var item = e.GetProperty("checklist").EnumerateArray().Single(i => i.GetProperty("code").GetString() == "HiddenByOwner");
            item.GetProperty("text").GetString().Should().Be(HiddenByOwnerText);
            item.GetProperty("done").GetBoolean().Should().Be(state);
            (await get.Content.ReadAsStringAsync()).Should().NotContain("Показ выключен в настройках");
        }
        // существующие коды магазина не изменились
        var all = (await (await client.GetAsync($"/api/shops/{shop.Id}/catalog-listing")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("checklist").EnumerateArray().Select(i => i.GetProperty("code").GetString()).ToArray();
        all.Should().Contain("HiddenByOwner");
        all.Should().NotContain("SalonBlocked", "код салона магазину не отдаётся");
    }
}
