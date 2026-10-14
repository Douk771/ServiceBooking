using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: сценарии по замечаниям независимого ревью (REVIEW_CYCLE42.md) — M-1 (двойной Publish), M-3 (кеш каталога), M-5 (журнал подтверждений переживает удаление ресурса),
/// M-8 (банщик без тарифа и чек-листа), M-9 (вместимость у «Домов» не хранится), I-3 (тексты владельца и номер карты). Писано по API_CONTRACT_CYCLE42.md и ревью, без чтения реализации.
/// </summary>
public class Cycle42ReviewFollowUpTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private Task<HttpResponseMessage> PublishAsync(ResCtx r) =>
        AuthedClient(r.Company.Token).PostJsonAsync($"/api/baths/companies/{r.Company.CompanyId}/services/{r.Id}/publish", new { });

    private Task GiveBathsPlanAsync(Guid companyId, Guid planId) => WithDbAsync(async db =>
    {
        var accountId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BillingAccountId!.Value).FirstAsync();
        var sub = await db.BathsSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == accountId);
        if (sub is null) db.BathsSubscriptions.Add(sub = new BathsSubscription { Id = Guid.NewGuid(), BillingAccountId = accountId });
        sub.PlanConfigId = planId;
        sub.IsActive = true;
        sub.PaidUntil = DateTime.UtcNow.AddYears(1);
        await db.SaveChangesAsync();
    });

    private async Task<List<HttpResponseMessage>> RaceAsync(IEnumerable<Func<Task<HttpResponseMessage>>> calls)
    {
        var gate = new TaskCompletionSource();
        var tasks = calls.Select(call => Task.Run(async () => { await gate.Task; return await call(); })).ToList();
        gate.SetResult();
        return (await Task.WhenAll(tasks)).ToList();
    }

    private async Task<(string Token, string UserId)> AddStaffAsync(BathCtx c, string position)
    {
        var user = await RegisterAsync();
        var r = await AuthedClient(c.Token).PostJsonAsync($"/api/Companies/{c.CompanyId}/members",
            new { phone = user.Phone, firstName = user.FirstName, lastName = user.LastName, role = "Master", bio = (string?)null, email = (string?)null, position });
        r.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await r.Content.ReadAsStringAsync());
        return ((await LoginAsync(user.Phone, "Password123!")).Token, user.UserId);
    }

    private Task<HttpResponseMessage> SetupAsync(ResCtx r, string name, string slug, int? capacity, int minHours = 1) =>
        AuthedClient(r.Company.Token).PutJsonAsync($"/api/baths/companies/{r.Company.CompanyId}/services/{r.Id}/setup",
            new ServiceSetupInput(name, slug, minHours, 6, 30, 30, false, 0, 30, StayServiceCancellationPolicy.NoDeductions, 12, false, Capacity: capacity));

    // ── CY42-110: M-1, двойной клик «Опубликовать» на тарифе «Одна баня» ──────────

    [Fact, TestCase("CY42-110")]
    public async Task Publish_TwoParallelRequests_OfTheSameResource_OnOneBathPlan_BothAre200()
    {
        var c = await CreateBathAsync(trial: false);
        await GiveBathsPlanAsync(c.CompanyId, BathsPlans.OneBathSeedId);
        var r = await AddResourceAsync(c, publish: false);

        var results = await RaceAsync([() => PublishAsync(r), () => PublishAsync(r)]);

        foreach (var x in results) x.StatusCode.Should().Be(HttpStatusCode.OK, "повторная публикация уже опубликованного ресурса не упирается в лимит: " + await x.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.StayServices.AsNoTracking().Where(s => s.Id == r.Id).Select(s => s.IsPublished).SingleAsync())).Should().BeTrue();
        (await J(await AuthedClient(c.Token).GetAsync($"/api/baths/companies/{c.CompanyId}"))).GetProperty("plan").GetProperty("resourcesPublished").GetInt32().Should().Be(1);
    }

    [Fact, TestCase("CY42-110")]
    public async Task Publish_ManyParallelRequests_OfTheSameResource_OnOneBathPlan_AllAre200_AndSequentialRepeatToo()
    {
        var c = await CreateBathAsync(trial: false);
        await GiveBathsPlanAsync(c.CompanyId, BathsPlans.OneBathSeedId);
        var r = await AddResourceAsync(c, publish: false);

        var results = await RaceAsync(Enumerable.Range(0, 6).Select(_ => (Func<Task<HttpResponseMessage>>)(() => PublishAsync(r))));

        results.Should().OnlyContain(x => x.StatusCode == HttpStatusCode.OK);
        (await PublishAsync(r)).StatusCode.Should().Be(HttpStatusCode.OK, "и повтор после гонки");
    }

    [Fact, TestCase("CY42-110")]
    public async Task Publish_TwoParallelRequests_OfDifferentResources_OnOneBathPlan_ExactlyOneWins_OtherIs402()
    {
        var c = await CreateBathAsync(trial: false);
        await GiveBathsPlanAsync(c.CompanyId, BathsPlans.OneBathSeedId);
        var a = await AddResourceAsync(c, "Баня А", publish: false);
        var b = await AddResourceAsync(c, "Баня Б", publish: false);

        var results = await RaceAsync([() => PublishAsync(a), () => PublishAsync(b)]);

        results.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1, "лимит тарифа — одна опубликованная баня");
        results.Count(x => (int)x.StatusCode == 402).Should().Be(1);
        (await WithDbAsync(db => db.StayServices.CountAsync(s => s.CompanyId == c.CompanyId && s.IsPublished))).Should().Be(1);
    }

    // ── CY42-111: M-3, правки видны в каталоге и на странице комплекса сразу ──────

    [Fact, TestCase("CY42-111")]
    public async Task Catalog_AndComplexPage_ShowSetupChanges_Immediately_NotAfterCacheExpires()
    {
        var city = await NthCityIdAsync(21);
        var c = await CreateBathAsync(city);
        var r = await AddResourceAsync(c, "Старое имя", capacity: 6);
        InvalidateBathsCatalog();
        var before = (await CatalogAsync($"?cityId={city}&pageSize=50")).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("resourceId").GetGuid() == r.Id);
        before.GetProperty("resourceName").GetString().Should().Be("Старое имя");
        (await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK); // кеш «базы» прогрет

        var newSlug = Unique("res-").ToLowerInvariant();
        var setup = await SetupAsync(r, "Новое имя", newSlug, capacity: 9, minHours: 2);
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());

        var after = (await CatalogAsync($"?cityId={city}&pageSize=50")).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("resourceId").GetGuid() == r.Id);
        after.GetProperty("resourceName").GetString().Should().Be("Новое имя");
        after.GetProperty("capacity").GetInt32().Should().Be(9);
        after.GetProperty("minHours").GetInt32().Should().Be(2);
        after.GetProperty("url").GetString().Should().Be($"/{c.Slug}/{newSlug}");

        var page = await J(await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}"));
        var onPage = page.GetProperty("resources").EnumerateArray().Single(x => x.GetProperty("resourceId").GetGuid() == r.Id);
        onPage.GetProperty("resourceName").GetString().Should().Be("Новое имя");
        (await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}/services/{newSlug}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}/services/{r.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "прежний адрес ресурса не работает");
    }

    [Fact, TestCase("CY42-111")]
    public async Task Catalog_ShowsPriceChange_Immediately()
    {
        var city = await NthCityIdAsync(22);
        var c = await CreateBathAsync(city);
        var r = await AddResourceAsync(c, "Баня", priceRub: 1500);
        InvalidateBathsCatalog();
        int FromRub(JsonElement page) => page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("resourceId").GetGuid() == r.Id).GetProperty("priceFromRub").GetInt32();
        FromRub(await CatalogAsync($"?cityId={city}&pageSize=50")).Should().Be(1500);

        var client = AuthedClient(c.Token);
        var baseUrl = $"/api/baths/companies/{c.CompanyId}/services/{r.Id}/price-rules";
        var rules = await J(await client.GetAsync(baseUrl));
        var ruleId = rules.GetProperty("rules").EnumerateArray().Single().GetProperty("id").GetGuid();
        var put = await client.PutJsonAsync($"{baseUrl}/{ruleId}", new PriceRuleInput(127, 6, 30, 2400));
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        FromRub(await CatalogAsync($"?cityId={city}&pageSize=50")).Should().Be(2400, "цена «от» в каталоге обновилась сразу");
    }

    [Fact, TestCase("CY42-111")]
    public async Task Catalog_AndComplexPage_FollowTheChangeOfCompanyAddress_Immediately()
    {
        var city = await NthCityIdAsync(23);
        var c = await CreateBathAsync(city);
        var r = await AddResourceAsync(c, "Баня");
        InvalidateBathsCatalog();
        CatalogIds(await CatalogAsync($"?cityId={city}&pageSize=50")).Should().Contain(r.Id);
        (await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var fresh = Unique("nw-").ToLowerInvariant();
        var changed = await AuthedClient(c.Token).PutJsonAsync($"/api/baths/companies/{c.CompanyId}/slug", new SlugInput(fresh));
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());

        var card = (await CatalogAsync($"?cityId={city}&pageSize=50")).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("resourceId").GetGuid() == r.Id);
        card.GetProperty("companySlug").GetString().Should().Be(fresh);
        card.GetProperty("url").GetString().Should().Be($"/{fresh}/{r.Slug}", "карточка ведёт на новый адрес, а не на 404");
        (await AnonymousClient().GetAsync($"/api/baths/public/companies/{fresh}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "старый адрес не работает");
    }

    // ── CY42-112: M-5, журнал подтверждений переживает удаление ресурса ───────────

    public static IEnumerable<object[]> Prefixes => [["baths"], ["stays"]];

    [Theory, MemberData(nameof(Prefixes)), TestCase("CY42-112")]
    public async Task DeletingAResource_KeepsConfirmationJournalRows_WithNullServiceId_AndNameSnapshot(string prefix)
    {
        Guid companyId, serviceId;
        string token;
        const string resName = "Журнальная баня";
        if (prefix == "baths")
        {
            var c = await CreateBathAsync();
            var r = await AddResourceAsync(c, resName, publish: false);
            (token, companyId, serviceId) = (c.Token, c.CompanyId, r.Id);
        }
        else
        {
            var company = await CreateStaysCompanyAsync();
            await EnableOrdersWithoutStayAsync(company);
            var svc = await CreateServiceAsync(company, resName, publish: false);
            (token, companyId, serviceId) = (company.OwnerToken, company.Id, svc.Service.Id);
        }
        var client = AuthedClient(token);
        var items = $"/api/{prefix}/companies/{companyId}/services/{serviceId}/items";
        foreach (var name in new[] { "Пиво светлое", "Кальян яблочный" })
            (await client.PostJsonAsync(items, new { name, priceRub = 300, maxPerSession = 2, isActive = true, confirmRestricted = true })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await WithDbAsync(db => db.StayServiceItemConfirmations.CountAsync(x => x.ServiceId == serviceId))).Should().Be(2);

        var del = await client.DeleteAsync($"/api/{prefix}/companies/{companyId}/services/{serviceId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent, await del.Content.ReadAsStringAsync());

        var rows = await WithDbAsync(db => db.StayServiceItemConfirmations.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.ConfirmedAtUtc).ToListAsync());
        rows.Should().HaveCount(2, "доказательство подтверждения хранится, пока существует компания");
        rows.Should().OnlyContain(x => x.ServiceId == null && x.ItemId == null);
        rows.Should().OnlyContain(x => x.ServiceNameSnapshot == resName);
        rows.Select(x => x.ItemNameSnapshot).Should().BeEquivalentTo("Пиво светлое", "Кальян яблочный");
        rows.Should().OnlyContain(x => x.MarkersHit != "" && x.ConfirmedByUserId != "" && x.NoticeVersion != "");
        (await WithDbAsync(db => db.StayServices.CountAsync(s => s.Id == serviceId))).Should().Be(0);
    }

    [Fact, TestCase("CY42-112")]
    public async Task ResourceDeletion_DoesNotTouchJournalOfAnotherResource()
    {
        var c = await CreateBathAsync();
        var a = await AddResourceAsync(c, "Баня А", publish: false);
        var b = await AddResourceAsync(c, "Баня Б", publish: false);
        var client = AuthedClient(c.Token);
        foreach (var r in new[] { a, b })
            (await client.PostJsonAsync($"/api/baths/companies/{c.CompanyId}/services/{r.Id}/items", new { name = "Пиво", priceRub = 300, maxPerSession = 2, isActive = true, confirmRestricted = true }))
                .StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.DeleteAsync($"/api/baths/companies/{c.CompanyId}/services/{a.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await WithDbAsync(db => db.StayServiceItemConfirmations.CountAsync(x => x.ServiceId == b.Id))).Should().Be(1, "журнал оставшегося ресурса на месте и привязан к нему");
        (await WithDbAsync(db => db.StayServiceItemConfirmations.CountAsync(x => x.CompanyId == c.CompanyId && x.ServiceId == null && x.ServiceNameSnapshot == "Баня А"))).Should().Be(1);
    }

    // ── CY42-113: M-8, банщик не видит тариф и чек-лист ──────────────────────────

    [Fact, TestCase("CY42-113")]
    public async Task Housekeeper_InTheCompanyCard_SeesEmptyChecklistAndPlan_OwnerSeesThem()
    {
        var c = await CreateBathAsync(provider: false, paymentDetails: false);
        await AddResourceAsync(c, "Баня");
        var keeper = await AddStaffAsync(c, "Housekeeper");

        var ownerCard = await J(await AuthedClient(c.Token).GetAsync($"/api/baths/companies/{c.CompanyId}"));
        ownerCard.GetProperty("checklist").GetArrayLength().Should().BeGreaterThan(0, "владельцу чек-лист нужен (нет исполнителя и реквизитов)");
        ownerCard.GetProperty("plan").GetProperty("planName").GetString().Should().NotBeNullOrEmpty();

        var r = await AuthedClient(keeper.Token).GetAsync($"/api/baths/companies/{c.CompanyId}");
        var text = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.OK, text);
        var card = JsonDocument.Parse(text).RootElement;
        card.GetProperty("checklist").GetArrayLength().Should().Be(0, "банщику чек-лист не отдаётся");
        var plan = card.GetProperty("plan");
        plan.GetProperty("planName").ValueKind.Should().Be(JsonValueKind.Null);
        plan.GetProperty("maxResources").ValueKind.Should().Be(JsonValueKind.Null);
        plan.GetProperty("resourcesPublished").GetInt32().Should().Be(0);
        plan.GetProperty("paidUntilUtc").ValueKind.Should().Be(JsonValueKind.Null);
        plan.GetProperty("text").ValueKind.Should().Be(JsonValueKind.Null);
        var gate = card.GetProperty("gate");
        gate.GetProperty("reasonCode").ValueKind.Should().Be(JsonValueKind.Null);
        gate.GetProperty("reasonText").ValueKind.Should().Be(JsonValueKind.Null);
        card.GetProperty("settings").ValueKind.Should().Be(JsonValueKind.Null);
        text.Should().NotContain(PaymentDetailsText).And.NotContain("Иванов Иван");
    }

    // ── CY42-114: M-9, у «Домов» вместимости нет ─────────────────────────────────

    [Fact, TestCase("CY42-114")]
    public async Task DomSetup_WithCapacity_StoresNull_AndTheOrderGoesThroughWithoutGuestsCount()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: 30);
        var client = AuthedClient(company.OwnerToken);

        var setup = await client.PutJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Service.Id}/setup",
            new ServiceSetupInput("Баня", svc.Slug, 2, 6, 60, 30, false, 0, 30, StayServiceCancellationPolicy.NoDeductions, 12, true, Capacity: 5));
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());

        (await WithDbAsync(db => db.StayServices.AsNoTracking().Where(s => s.Id == svc.Service.Id).Select(s => s.Capacity).SingleAsync())).Should().BeNull("вместимость у «Домов» не хранится");
        var order = await OrderOkAsync(svc.Service.Id, InDays(10), 720, 2);
        order.Token.Should().NotBeNullOrEmpty();
        (await WithDbAsync(db => db.StayServiceOrders.AsNoTracking().Where(o => o.PublicToken == order.Token).Select(o => o.GuestsCount).SingleAsync())).Should().BeNull();
    }

    // ── CY42-115: I-3, тексты владельца ──────────────────────────────────────────

    private async Task<List<string>> ContentWarningsAsync(string text)
    {
        var c = await CreateBathAsync();
        var r = await AddResourceAsync(c, publish: false);
        var resp = await AuthedClient(c.Token).PutJsonAsync($"/api/baths/companies/{c.CompanyId}/services/{r.Id}/content", new { description = text });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"«{text}»: " + await resp.Content.ReadAsStringAsync());
        var body = await J(resp);
        return body.TryGetProperty("contentWarnings", out var w) && w.ValueKind == JsonValueKind.Array ? w.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
    }

    private const string HealthText = "Не обещайте лечебного или оздоровительного эффекта";
    private const string ExtraChargeText = "Все обязательные платежи должны быть в цене часов — не требуйте доплат на месте";
    private const string CardText = "Похоже на номер карты — не публикуйте данные карт";

    [Fact, TestCase("CY42-115")]
    public async Task OwnerText_Contraindications_AreAskedByTheHint_AndGetNoHealthWarning()
    {
        (await ContentWarningsAsync("Противопоказания: гипертония, беременность")).Should().NotContain(w => w == HealthText || w == "HealthClaim",
            "владельца просят указать противопоказания — предупреждать об этом нельзя");
        (await ContentWarningsAsync("Есть противопоказания, уточняйте у врача")).Should().NotContain(w => w == HealthText || w == "HealthClaim");
        (await ContentWarningsAsync("Лечебный пар, противопоказаний нет")).Should().Contain(w => w == HealthText || w == "HealthClaim", "обещание эффекта по-прежнему ловится");
    }

    [Theory, TestCase("CY42-115")]
    [InlineData("500 ₽ за каждого гостя")]
    [InlineData("300 ₽ за каждого")]
    [InlineData("Доплата за человека 300 ₽")]
    public async Task OwnerText_PerHeadSurcharge_GetsExtraChargeWarning(string text) =>
        (await ContentWarningsAsync(text)).Should().Contain(w => w == ExtraChargeText || w == "MandatoryExtraCharge");

    [Theory, TestCase("CY42-115")]
    [InlineData("Оплата на карту 2200 1234 5678 9012 345")] // 19 цифр
    [InlineData("Карта 2200123456789012345")]
    [InlineData("Карта 220012345678 9")] // 13 цифр
    public async Task OwnerText_CardNumber_From13To19Digits_GetsCardWarning(string text) =>
        (await ContentWarningsAsync(text)).Should().Contain(w => w == CardText || w == "CardNumber");

    [Theory, TestCase("CY42-115")]
    [InlineData("Телефон 8 900 111 22 33")] // 11 цифр
    [InlineData("Работаем с 10:00 до 23:00, до 12 человек")]
    [InlineData("Сверху веник в подарок")]
    public async Task OwnerText_OrdinaryNumbersAndWords_GetNoCardOrExtraChargeWarning(string text)
    {
        var warnings = await ContentWarningsAsync(text);
        warnings.Should().NotContain(w => w == CardText || w == "CardNumber");
        warnings.Should().NotContain(w => w == ExtraChargeText || w == "MandatoryExtraCharge");
    }
}
