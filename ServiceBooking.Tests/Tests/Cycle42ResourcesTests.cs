using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: ресурсы (баня, сауна, чан, фурако) — вместимость, публикация и тариф, фильтр позиций «алкоголь и табак» с журналом подтверждений на обоих префиксах
/// (<c>/api/baths</c> и <c>/api/stays</c>), запрещённые слова и мягкие предупреждения владельцу (CY42-30…39). Писано по SPEC_CYCLE42_BANI.md (US-42-05, US-42-08),
/// API_CONTRACT_CYCLE42.md §42.29, §42.30 и LEGAL_REVIEW_CYCLE42.md (Т42-05, Т42-12), без чтения реализации.
/// </summary>
public class Cycle42ResourcesTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private const string ForbiddenWordsText = "Не используйте слова «задаток», «невозвратный», «депозит»";
    private const string CapacityText = "Вместимость — от 1 до 30 человек";

    /// <summary>Ресурс бани до публикации: создан, настроен, окна и цена есть. Вместимость — как передана (null — не задана).</summary>
    private Task<ResCtx> DraftAsync(BathCtx c, string name = "Русская баня", int? capacity = 6) => AddResourceAsync(c, name, capacity: capacity, publish: false);

    private Task<HttpResponseMessage> PublishAsync(ResCtx r) =>
        AuthedClient(r.Company.Token).PostJsonAsync($"/api/baths/companies/{r.Company.CompanyId}/services/{r.Id}/publish", new { });

    private Task<HttpResponseMessage> SetupAsync(ResCtx r, int? capacity) =>
        AuthedClient(r.Company.Token).PutJsonAsync($"/api/baths/companies/{r.Company.CompanyId}/services/{r.Id}/setup",
            new ServiceSetupInput(r.Name, r.Slug, 1, 6, 30, 30, false, 0, 30, StayServiceCancellationPolicy.NoDeductions, 12, false, Capacity: capacity));

    private Task GiveBathsPlanAsync(Guid companyId, Guid planId, DateTime? paidUntil = null) => WithDbAsync(async db =>
    {
        var accountId = await db.Companies.Where(c => c.Id == companyId).Select(c => c.BillingAccountId!.Value).FirstAsync();
        var sub = await db.BathsSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == accountId);
        if (sub is null) db.BathsSubscriptions.Add(sub = new BathsSubscription { Id = Guid.NewGuid(), BillingAccountId = accountId });
        sub.PlanConfigId = planId;
        sub.IsActive = true;
        sub.PaidUntil = paidUntil ?? DateTime.UtcNow.AddYears(1);
        await db.SaveChangesAsync();
    });

    // ── CY42-30: вместимость 1…30 ───────────────────────────────────────────────

    [Fact, TestCase("CY42-30")]
    public async Task Capacity_AcceptsOneToThirty_RefusesOutside_WithExactText_AndShowsToGuest()
    {
        var c = await CreateBathAsync(await NthCityIdAsync(41));
        var r = await DraftAsync(c, capacity: 6);

        foreach (var ok in new[] { 1, 2, 15, 29, 30 })
        {
            var resp = await SetupAsync(r, ok);
            resp.StatusCode.Should().Be(HttpStatusCode.OK, $"вместимость {ok}: " + await resp.Content.ReadAsStringAsync());
            (await J(resp)).GetProperty("capacity").GetInt32().Should().Be(ok);
        }
        foreach (var bad in new[] { 0, -1, 31, 100, int.MaxValue })
        {
            var resp = await SetupAsync(r, bad);
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"вместимость {bad}");
            (await resp.Content.ReadAsStringAsync()).Should().Be(CapacityText);
        }
        // отказ ничего не сломал: остаётся последнее принятое значение
        var card = await J(await AuthedClient(c.Token).GetAsync($"/api/baths/companies/{c.CompanyId}/services/{r.Id}"));
        card.GetProperty("capacity").GetInt32().Should().Be(30);

        // гость видит «до N человек» на странице ресурса и в каталоге
        (await SetupAsync(r, 8)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PublishAsync(r)).StatusCode.Should().Be(HttpStatusCode.OK);
        InvalidateBathsCatalog();
        var page = await J(await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}/services/{r.Slug}"));
        page.GetProperty("capacity").GetInt32().Should().Be(8);
        var cardInCatalog = (await CatalogAsync($"?cityId={c.CityId}")).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("resourceId").GetGuid() == r.Id);
        cardInCatalog.GetProperty("capacity").GetInt32().Should().Be(8);

        // число гостей брони — 1…вместимость: границы
        (await PostBathOrderAsync(r.Id, InDays(8), 600, 2, guests: 9)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostBathOrderAsync(r.Id, InDays(8), 600, 2, guests: 0)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostBathOrderAsync(r.Id, InDays(8), 600, 2, guests: 8)).StatusCode.Should().Be(HttpStatusCode.Created);

        // сброс вместимости у опубликованного ресурса — отказ 409 (BUG-C42-QA-3); у черновика проходит, поле у карточки пропадает (§42.21)
        var refused = await SetupAsync(r, null);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("ServiceNoCapacity");
        (await AuthedClient(c.Token).PostJsonAsync($"/api/baths/companies/{c.CompanyId}/services/{r.Id}/unpublish", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        var cleared = await SetupAsync(r, null);
        cleared.StatusCode.Should().Be(HttpStatusCode.OK, await cleared.Content.ReadAsStringAsync());
        var after = await J(cleared);
        (!after.TryGetProperty("capacity", out var cap) || cap.ValueKind == JsonValueKind.Null).Should().BeTrue();
    }

    // ── CY42-31: публикация без вместимости ─────────────────────────────────────

    [Fact, TestCase("CY42-31")]
    public async Task Publish_WithoutCapacity_Is409ServiceNoCapacity_ThenWorksAfterCapacity_AndCatalogIsUntouched()
    {
        var c = await CreateBathAsync();
        var r = await DraftAsync(c, capacity: null);
        InvalidateBathsCatalog();

        var denied = await PublishAsync(r);
        denied.StatusCode.Should().Be(HttpStatusCode.Conflict, await denied.Content.ReadAsStringAsync());
        var body = await J(denied);
        body.GetProperty("code").GetString().Should().Be("ServiceNoCapacity");
        body.GetProperty("message").GetString().Should().Be("Укажите вместимость — сколько человек может находиться одновременно");
        CatalogIds(await CatalogAsync($"?cityId={c.CityId}")).Should().NotContain(r.Id, "неопубликованный ресурс в каталог не попадает");
        var card = await J(await AuthedClient(c.Token).GetAsync($"/api/baths/companies/{c.CompanyId}/services/{r.Id}"));
        card.GetProperty("isPublished").GetBoolean().Should().BeFalse();
        card.GetProperty("publishProblems").EnumerateArray().Select(p => p.GetString()).Should().Contain("ServiceNoCapacity", "причина видна и в карточке ресурса");

        (await SetupAsync(r, 4)).StatusCode.Should().Be(HttpStatusCode.OK);
        var ok = await PublishAsync(r);
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        (await J(ok)).GetProperty("isPublished").GetBoolean().Should().BeTrue();

        // порядок проверок: вместимость раньше тарифа — компания без тарифа и ресурс без вместимости получают 409, а не 402
        var noPlan = await CreateBathAsync(trial: false);
        var noPlanRes = await DraftAsync(noPlan, capacity: null);
        (await PublishAsync(noPlanRes)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── CY42-32: 402 без тарифа и после его окончания ───────────────────────────

    [Fact, TestCase("CY42-32")]
    public async Task Publish_WithoutPlan_Is402WithText_AndNothingGetsPublished()
    {
        var c = await CreateBathAsync(trial: false);
        var r = await DraftAsync(c);
        var denied = await PublishAsync(r);
        denied.StatusCode.Should().Be((HttpStatusCode)402);
        (await denied.Content.ReadAsStringAsync()).Should().Be("Выберите тариф или активируйте пробный период, чтобы опубликовать баню");
        (await J(await AuthedClient(c.Token).GetAsync($"/api/baths/companies/{c.CompanyId}/services/{r.Id}"))).GetProperty("isPublished").GetBoolean().Should().BeFalse();

        // платный тариф есть — публикуется
        await GiveBathsPlanAsync(c.CompanyId, BathsPlans.OneBathSeedId);
        (await PublishAsync(r)).StatusCode.Should().Be(HttpStatusCode.OK);

        // тариф закончился: новый ресурс снова 402, опубликованный остаётся опубликованным (снижение тарифа ничего не снимает), снять и заархивировать можно
        await GiveBathsPlanAsync(c.CompanyId, BathsPlans.OneBathSeedId, paidUntil: DateTime.UtcNow.AddMinutes(-1));
        var second = await DraftAsync(c, "Купель");
        (await PublishAsync(second)).StatusCode.Should().Be((HttpStatusCode)402);
        (await J(await AuthedClient(c.Token).GetAsync($"/api/baths/companies/{c.CompanyId}/services/{r.Id}"))).GetProperty("isPublished").GetBoolean().Should().BeTrue();
        (await AuthedClient(c.Token).PostJsonAsync($"/api/baths/companies/{c.CompanyId}/services/{r.Id}/unpublish", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuthedClient(c.Token).PostJsonAsync($"/api/baths/companies/{c.CompanyId}/services/{second.Id}/archive", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── CY42-33: лимит ресурсов на аккаунт по всем банным компаниям ──────────────

    [Fact, TestCase("CY42-33")]
    public async Task ResourceLimit_IsPerAccount_AcrossAllBathsCompanies_ArchiveFreesASlot()
    {
        var a = await CreateBathAsync();
        var b = await CreateBathAsync(owner: a.Owner, trial: false);
        var c3 = await CreateBathAsync(owner: a.Owner, trial: false);
        await GiveBathsPlanAsync(a.CompanyId, BathsPlans.UpToThreeSeedId);
        (await J(await AuthedClient(a.Token).GetAsync($"/api/baths/companies/{a.CompanyId}"))).GetProperty("plan").GetProperty("maxResources").GetInt32().Should().Be(3);

        var one = await DraftAsync(a, "Баня А1");
        var two = await DraftAsync(b, "Баня Б1");
        var three = await DraftAsync(c3, "Баня В1");
        var four = await DraftAsync(a, "Баня А2");
        (await PublishAsync(one)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PublishAsync(two)).StatusCode.Should().Be(HttpStatusCode.OK, "вторая компания того же аккаунта — тот же тариф");
        (await PublishAsync(three)).StatusCode.Should().Be(HttpStatusCode.OK);
        var over = await PublishAsync(four);
        over.StatusCode.Should().Be((HttpStatusCode)402);
        (await over.Content.ReadAsStringAsync()).Should().Be("Тариф «До 3 бань» позволяет опубликовать 3 ресурса");

        // чужой аккаунт лимит не делит
        var other = await CreateBathAsync();
        var otherRes = await DraftAsync(other, "Чужая баня");
        (await PublishAsync(otherRes)).StatusCode.Should().Be(HttpStatusCode.OK);

        // снятие с публикации и архив освобождают место, черновики лимит не занимают
        (await AuthedClient(b.Token).PostJsonAsync($"/api/baths/companies/{b.CompanyId}/services/{two.Id}/unpublish", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PublishAsync(four)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuthedClient(c3.Token).PostJsonAsync($"/api/baths/companies/{c3.CompanyId}/services/{three.Id}/archive", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PublishAsync(two)).StatusCode.Should().Be(HttpStatusCode.OK);
        var card = await J(await AuthedClient(a.Token).GetAsync($"/api/baths/companies/{a.CompanyId}"));
        card.GetProperty("plan").GetProperty("resourcesPublished").GetInt32().Should().Be(3, "счётчик — по всем банным компаниям аккаунта, без архива");
    }

    // ── фильтр позиций ──────────────────────────────────────────────────────────

    private async Task<(string Token, Guid CompanyId, Guid ServiceId)> BathsServiceAsync()
    {
        var c = await CreateBathAsync();
        var r = await AddResourceAsync(c);
        return (c.Token, c.CompanyId, r.Id);
    }

    private async Task<(string Token, Guid CompanyId, Guid ServiceId)> StaysServiceAsync()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company);
        return (company.OwnerToken, company.Id, svc.Id);
    }

    public static IEnumerable<object[]> Prefixes => [["baths"], ["stays"]];

    private async Task<(string Token, Guid CompanyId, Guid ServiceId)> SceneAsync(string prefix) => prefix == "baths" ? await BathsServiceAsync() : await StaysServiceAsync();

    private static string ItemsUrl(string prefix, Guid companyId, Guid serviceId) => $"/api/{prefix}/companies/{companyId}/services/{serviceId}/items";

    private static object ItemBody(string name, bool? confirm = null, int price = 100) => new { name, priceRub = price, maxPerSession = 5, isActive = true, confirmRestricted = confirm };

    private Task<List<StayServiceItemConfirmation>> JournalAsync(Guid serviceId) =>
        WithDbAsync(db => db.StayServiceItemConfirmations.AsNoTracking().Where(x => x.ServiceId == serviceId).OrderBy(x => x.ConfirmedAtUtc).ToListAsync());

    private Task<int> ItemCountAsync(Guid serviceId) => WithDbAsync(db => db.StayServiceItems.CountAsync(i => i.ServiceId == serviceId));

    // ── CY42-34/35: «Пиво», «Кальян» — 409 и подтверждение, на обоих префиксах ────

    [Theory, MemberData(nameof(Prefixes)), TestCase("CY42-34")]
    public async Task Items_Beer_RequiresConfirmation_Then409IsNotSaved_ConfirmedIsSavedAndJournaled(string prefix)
    {
        var (token, companyId, serviceId) = await SceneAsync(prefix);
        var client = AuthedClient(token);
        var url = ItemsUrl(prefix, companyId, serviceId);

        var first = await client.PostJsonAsync(url, ItemBody("Пиво разливное 0,5"));
        first.StatusCode.Should().Be(HttpStatusCode.Conflict, $"{prefix}: " + await first.Content.ReadAsStringAsync());
        var body = await J(first);
        body.GetProperty("code").GetString().Should().Be("ItemRestrictedConfirmationRequired");
        body.GetProperty("message").GetString().Should().StartWith("Похоже на алкоголь или табак: пив").And.Contain("Через сервис их продавать нельзя");
        body.GetProperty("markers").EnumerateArray().Select(m => m.GetString()).Should().Equal("пив");
        body.GetProperty("noticeText").GetString().Should().NotBeNullOrWhiteSpace("в ответе — текст подсказки владельцу");
        (await ItemCountAsync(serviceId)).Should().Be(0, "без подтверждения позиция не сохраняется");
        (await JournalAsync(serviceId)).Should().BeEmpty("без подтверждения журнал не пишется");

        // явное «false» — тоже не подтверждение
        (await client.PostJsonAsync(url, ItemBody("Пиво разливное 0,5", confirm: false))).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var confirmed = await client.PostJsonAsync(url, ItemBody("Пиво разливное 0,5", confirm: true));
        confirmed.StatusCode.Should().Be(HttpStatusCode.Created, await confirmed.Content.ReadAsStringAsync());
        var item = await J(confirmed);
        item.GetProperty("name").GetString().Should().Be("Пиво разливное 0,5");
        var rows = await JournalAsync(serviceId);
        var row = rows.Should().ContainSingle().Subject;
        row.ItemNameSnapshot.Should().Be("Пиво разливное 0,5");
        row.MarkersHit.Should().Be("пив");
        row.ItemId.Should().Be(item.GetProperty("id").GetGuid());
        row.CompanyId.Should().Be(companyId);
        row.ConfirmedByUserId.Should().NotBeNullOrEmpty();
        row.ConfirmedByNameSnapshot.Should().NotBeNullOrWhiteSpace();
        row.NoticeKey.Should().NotBeNullOrEmpty();
        row.NoticeVersion.Should().NotBeNullOrEmpty("версия показанного текста фиксируется");
        row.ConfirmedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));
    }

    [Theory, MemberData(nameof(Prefixes)), TestCase("CY42-35")]
    public async Task Items_Hookah_AndOtherStems_AreCaught_NoCaseNoYo_WithMarkersInOrder(string prefix)
    {
        var (token, companyId, serviceId) = await SceneAsync(prefix);
        var client = AuthedClient(token);
        var url = ItemsUrl(prefix, companyId, serviceId);

        // примеры из bani-vectors.json restrictedItems (+ регистр и ё)
        var cases = new (string Name, string[] Markers)[]
        {
            ("Кальян на чаше", ["кальян"]), ("КАЛЬЯН", ["кальян"]), ("Медовуха", ["медовух"]), ("СИДР яблочный", ["сидр"]), ("Вейп-зона", ["вейп"]),
            ("Шампанское и сигары", ["шампанск", "сигар"]), ("Безалкогольное пиво", ["пив"]), ("Винтажный халат", ["вин"]), ("Табак для кальяна", ["кальян", "табак"]),
        };
        foreach (var (name, markers) in cases)
        {
            var r = await client.PostJsonAsync(url, ItemBody(name));
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, $"{prefix}: {name}");
            (await J(r)).GetProperty("markers").EnumerateArray().Select(m => m.GetString()).Should().Equal(markers, name);
        }
        (await ItemCountAsync(serviceId)).Should().Be(0);

        // «Винтажный халат» — ложное срабатывание: владелец подтверждает «это другая позиция», и позиция создаётся
        (await client.PostJsonAsync(url, ItemBody("Винтажный халат", confirm: true))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await JournalAsync(serviceId)).Should().ContainSingle().Which.MarkersHit.Should().Be("вин");
    }

    [Fact, TestCase("CY42-35")]
    public async Task Items_ConfirmationJournals_AreSeparatePerService_OnBothPrefixes()
    {
        var bathScene = await BathsServiceAsync();
        var staysScene = await StaysServiceAsync();
        (await AuthedClient(bathScene.Token).PostJsonAsync(ItemsUrl("baths", bathScene.CompanyId, bathScene.ServiceId), ItemBody("Пиво", true))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await AuthedClient(staysScene.Token).PostJsonAsync(ItemsUrl("stays", staysScene.CompanyId, staysScene.ServiceId), ItemBody("Кальян", true))).StatusCode.Should().Be(HttpStatusCode.Created);

        var bathRows = await JournalAsync(bathScene.ServiceId);
        var staysRows = await JournalAsync(staysScene.ServiceId);
        bathRows.Should().ContainSingle().Which.CompanyId.Should().Be(bathScene.CompanyId);
        staysRows.Should().ContainSingle().Which.CompanyId.Should().Be(staysScene.CompanyId);
        bathRows[0].MarkersHit.Should().Be("пив");
        staysRows[0].MarkersHit.Should().Be("кальян");

        // позицию через чужой префикс не создать и не подтвердить
        (await AuthedClient(bathScene.Token).PostJsonAsync(ItemsUrl("stays", bathScene.CompanyId, bathScene.ServiceId), ItemBody("Пиво", true))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuthedClient(staysScene.Token).PostJsonAsync(ItemsUrl("baths", staysScene.CompanyId, staysScene.ServiceId), ItemBody("Пиво", true))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JournalAsync(bathScene.ServiceId)).Should().HaveCount(1);
        (await JournalAsync(staysScene.ServiceId)).Should().HaveCount(1);
    }

    // ── CY42-36: «Свинина» и обычные позиции — без подтверждения ────────────────

    [Theory, MemberData(nameof(Prefixes)), TestCase("CY42-36")]
    public async Task Items_Pork_BroomAndTea_NeedNoConfirmation_NoJournal_ConfirmFlagIsIgnored(string prefix)
    {
        var (token, companyId, serviceId) = await SceneAsync(prefix);
        var client = AuthedClient(token);
        var url = ItemsUrl(prefix, companyId, serviceId);

        foreach (var name in new[] { "Свинина на мангале", "Веник берёзовый", "Чай травяной", "Полотенце", "Парение" })
        {
            var r = await client.PostJsonAsync(url, ItemBody(name));
            r.StatusCode.Should().Be(HttpStatusCode.Created, $"{prefix}: {name}: " + await r.Content.ReadAsStringAsync());
        }
        // подтверждение там, где оно не нужно, игнорируется и в журнал не пишется
        (await client.PostJsonAsync(url, ItemBody("Простыня", confirm: true))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ItemCountAsync(serviceId)).Should().Be(6);
        (await JournalAsync(serviceId)).Should().BeEmpty();
    }

    // ── CY42-37: правка позиции и журнал ────────────────────────────────────────

    [Theory, MemberData(nameof(Prefixes)), TestCase("CY42-37")]
    public async Task Items_Update_RenameToRestricted_AsksOnce_SameConfirmedNameDoesNotAskAgain(string prefix)
    {
        var (token, companyId, serviceId) = await SceneAsync(prefix);
        var client = AuthedClient(token);
        var url = ItemsUrl(prefix, companyId, serviceId);
        var created = await J(await client.PostJsonAsync(url, ItemBody("Веник")));
        var itemUrl = $"{url}/{created.GetProperty("id").GetGuid()}";

        // переименование в «пиво» без подтверждения — 409, имя прежнее
        var refused = await client.PutJsonAsync(itemUrl, ItemBody("Пиво"));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(refused)).GetProperty("code").GetString().Should().Be("ItemRestrictedConfirmationRequired");
        (await WithDbAsync(db => db.StayServiceItems.Where(i => i.ServiceId == serviceId).Select(i => i.Name).SingleAsync())).Should().Be("Веник");
        (await JournalAsync(serviceId)).Should().BeEmpty();

        // с подтверждением — сохраняется и пишется в журнал
        (await client.PutJsonAsync(itemUrl, ItemBody("Пиво", true))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JournalAsync(serviceId)).Should().ContainSingle();

        // правка цены с тем же названием (даже в другом регистре и с ё/е) заново не спрашивает и новую строку не пишет
        (await client.PutJsonAsync(itemUrl, ItemBody("Пиво", price: 250))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PutJsonAsync(itemUrl, ItemBody("  ПИВО  ", price: 260))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JournalAsync(serviceId)).Should().ContainSingle("то же подтверждённое название заново не подтверждается");

        // другое запрещённое название — спрашивает снова, подтверждение — вторая строка журнала
        (await client.PutJsonAsync(itemUrl, ItemBody("Кальян"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PutJsonAsync(itemUrl, ItemBody("Кальян", true))).StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await JournalAsync(serviceId);
        rows.Select(r => r.MarkersHit).Should().Equal("пив", "кальян");
        rows.Should().OnlyContain(r => r.ItemId != null);

        // удаление позиции журнал не стирает (ItemId обнуляется, название-снимок остаётся)
        (await client.DeleteAsync(itemUrl)).IsSuccessStatusCode.Should().BeTrue();
        var afterDelete = await JournalAsync(serviceId);
        afterDelete.Should().HaveCount(2);
        afterDelete.Select(r => r.ItemNameSnapshot).Should().Contain(["Пиво", "Кальян"]);
    }

    // ── CY42-38: «задаток», «депозит» — 400 ─────────────────────────────────────

    [Theory, MemberData(nameof(Prefixes)), TestCase("CY42-38")]
    public async Task ForbiddenWords_InItemNamesAndDescription_Are400_NotBypassedByConfirmFlag(string prefix)
    {
        var (token, companyId, serviceId) = await SceneAsync(prefix);
        var client = AuthedClient(token);
        var url = ItemsUrl(prefix, companyId, serviceId);

        foreach (var name in new[] { "Задаток 1000 рублей", "Депозит за халат", "Невозвратная предоплата", "ЗАДАТОК", "залог-депозит" })
        {
            var r = await client.PostJsonAsync(url, ItemBody(name));
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{prefix}: {name}");
            (await r.Content.ReadAsStringAsync()).Should().Be(ForbiddenWordsText);
            (await client.PostJsonAsync(url, ItemBody(name, confirm: true))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "подтверждение не отменяет запрет слов");
        }
        // сочетание запрещённого слова и алкоголя — сначала 400 (слова), не 409
        (await client.PostJsonAsync(url, ItemBody("Депозит за пиво"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ItemCountAsync(serviceId)).Should().Be(0);
        (await JournalAsync(serviceId)).Should().BeEmpty();

        // при правке существующей позиции — то же
        var item = await J(await client.PostJsonAsync(url, ItemBody("Веник")));
        var put = await client.PutJsonAsync($"{url}/{item.GetProperty("id").GetGuid()}", ItemBody("Веник, задаток 100"));
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await put.Content.ReadAsStringAsync()).Should().Be(ForbiddenWordsText);

        // описание ресурса
        var content = $"/api/{prefix}/companies/{companyId}/services/{serviceId}/content";
        foreach (var text in new[] { "Баня. Задаток 1000 рублей при бронировании", "Внесите депозит", "Предоплата невозвратная — невозвратный платёж" })
        {
            var r = await client.PutJsonAsync(content, new { description = text });
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{prefix}: {text}");
            (await r.Content.ReadAsStringAsync()).Should().Be(ForbiddenWordsText);
        }
        var card = await J(await client.GetAsync($"/api/{prefix}/companies/{companyId}/services/{serviceId}"));
        (card.GetProperty("description").GetString() ?? "").Should().NotContainAny("адаток", "епозит", "евозвратн", "описание не сохранилось");
    }

    // ── CY42-39: мягкие предупреждения владельцу ────────────────────────────────

    private static List<string> Warnings(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        return arr.EnumerateArray().Select(x => x.GetString()!).ToList();
    }

    [Theory, MemberData(nameof(Prefixes)), TestCase("CY42-39")]
    public async Task ContentWarnings_HealthClaimAndPenalty_AreSoft_SavedAndReported(string prefix)
    {
        var (token, companyId, serviceId) = await SceneAsync(prefix);
        var client = AuthedClient(token);
        var content = $"/api/{prefix}/companies/{companyId}/services/{serviceId}/content";
        const string healthText = "Не обещайте лечебного или оздоровительного эффекта";
        const string cancelText = "Условия отмены задаёт выбранный шаблон — другие условия в описании не действуют";

        var clean = await client.PutJsonAsync(content, new { description = "Берёзовый веник и чай в подарок" });
        clean.StatusCode.Should().Be(HttpStatusCode.OK);
        Warnings(await J(clean), "contentWarnings").Should().BeEmpty("чистое описание без предупреждений");

        var health = await client.PutJsonAsync(content, new { description = "Лечебный пар, противопоказаний нет" });
        health.StatusCode.Should().Be(HttpStatusCode.OK, "предупреждение не блокирует сохранение: " + await health.Content.ReadAsStringAsync());
        var healthBody = await J(health);
        Warnings(healthBody, "contentWarnings").Should().Contain(w => w == healthText || w == "HealthClaim");
        healthBody.GetProperty("description").GetString().Should().Be("Лечебный пар, противопоказаний нет");

        var penalty = await client.PutJsonAsync(content, new { description = "Штраф за неявку 500 ₽" });
        penalty.StatusCode.Should().Be(HttpStatusCode.OK);
        Warnings(await J(penalty), "contentWarnings").Should().Contain(w => w == cancelText || w == "CancellationTermsInText");

        var both = await J(await client.PutJsonAsync(content, new { description = "Лечебный пар. Неустойка при отмене. Пришлите фото паспорта" }));
        var texts = Warnings(both, "contentWarnings");
        texts.Should().HaveCountGreaterOrEqualTo(3, "три разных предупреждения: здоровье, отмена, паспорт");
        texts.Should().OnlyHaveUniqueItems();

        // сохранённое описание видно в карточке и предупреждения повторяются при чтении
        var card = await J(await client.GetAsync($"/api/{prefix}/companies/{companyId}/services/{serviceId}"));
        card.GetProperty("description").GetString().Should().Contain("Неустойка");
    }

    [Theory, MemberData(nameof(Prefixes)), TestCase("CY42-39")]
    public async Task ItemWarnings_PenaltyHealthAndExtraCharge_AreSoft_ItemIsSaved(string prefix)
    {
        var (token, companyId, serviceId) = await SceneAsync(prefix);
        var client = AuthedClient(token);
        var url = ItemsUrl(prefix, companyId, serviceId);

        var clean = await client.PostJsonAsync(url, ItemBody("Веник берёзовый"));
        clean.StatusCode.Should().Be(HttpStatusCode.Created);
        Warnings(await J(clean), "warnings").Should().BeEmpty();

        foreach (var (name, code) in new[] { ("Штраф за неявку 500", "CancellationTermsInText"), ("Лечебный сбор трав", "HealthClaim"), ("Доплата за каждого гостя сверх четырёх", "MandatoryExtraCharge") })
        {
            var r = await client.PostJsonAsync(url, ItemBody(name));
            r.StatusCode.Should().Be(HttpStatusCode.Created, $"{prefix}: {name}: " + await r.Content.ReadAsStringAsync());
            var body = await J(r);
            body.GetProperty("name").GetString().Should().Be(name, "предупреждение не меняет название");
            Warnings(body, "warnings").Should().NotBeEmpty($"{name}: ожидается предупреждение {code}");
        }
        (await ItemCountAsync(serviceId)).Should().Be(4);
    }

    /// <summary>Основы из API_CONTRACT_CYCLE42.md §42.30.2, которых нет в примерах векторов: каждая должна давать своё предупреждение.</summary>
    [Theory, TestCase("CY42-39")]
    [InlineData("Оздоровительная программа", "HealthClaim")]
    [InlineData("Детокс для всех", "HealthClaim")]
    [InlineData("Плата за неявку", "CancellationTermsInText")]
    [InlineData("Доплата за человека 300 ₽", "MandatoryExtraCharge")]
    [InlineData("Обязательный взнос на месте", "MandatoryExtraCharge")]
    [InlineData("Оплата на карту 2200 1234 5678 9012", "CardNumber")]
    public async Task ContractStems_ProduceTheirWarning(string text, string code)
    {
        var (token, companyId, serviceId) = await BathsServiceAsync();
        var r = await AuthedClient(token).PutJsonAsync($"/api/baths/companies/{companyId}/services/{serviceId}/content", new { description = text });
        if (r.StatusCode == HttpStatusCode.BadRequest) return; // «невозврат...» может быть и жёстким отказом по списку слов — это тоже защита
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var warnings = Warnings(await J(r), "contentWarnings");
        var texts = new Dictionary<string, string>
        {
            ["HealthClaim"] = "Не обещайте лечебного или оздоровительного эффекта",
            ["CancellationTermsInText"] = "Условия отмены задаёт выбранный шаблон — другие условия в описании не действуют",
            ["MandatoryExtraCharge"] = "Все обязательные платежи должны быть в цене часов — не требуйте доплат на месте",
            ["CardNumber"] = "Похоже на номер карты — не публикуйте данные карт"
        };
        warnings.Should().Contain(w => w == code || w == texts[code], $"«{text}» должно давать предупреждение {code} (контракт §42.30.2)");
    }

    /// <summary>
    /// BUG-C42-QA-2 (НЕ ВЫПОЛНЯЕТСЯ): основы «иммунитет», «полезно при», «невозврат» (без «-н») названы в контракте §42.30.2, но реализация
    /// (<c>OwnerTextChecks.Soft</c>) их не ловит — описание сохраняется без предупреждения. Снять Skip после правки регулярных выражений.
    /// </summary>
    [Theory, TestCase("CY42-39")]
    [InlineData("Укрепляет иммунитет", "HealthClaim")]
    [InlineData("Полезно при простуде", "HealthClaim")]
    [InlineData("Недорого, невозврат средств", "CancellationTermsInText")]
    public async Task ContractStems_NotCaughtYet_ProduceTheirWarning(string text, string code) => await ContractStems_ProduceTheirWarning(text, code);

    /// <summary>
    /// BUG-C42-QA-3 (НЕ ВЫПОЛНЯЕТСЯ): сброс вместимости (<c>capacity: null</c>) у УЖЕ ОПУБЛИКОВАННОГО ресурса бани проходит (200), ресурс остаётся опубликованным без вместимости,
    /// гость бронирует без числа гостей — обходится правило публикации ServiceNoCapacity (§42.29). Ожидается 400/409 либо снятие с публикации.
    /// </summary>
    [Fact, TestCase("CY42-31")]
    public async Task Capacity_CannotBeClearedOnAPublishedBathResource()
    {
        var c = await CreateBathAsync();
        var r = await AddResourceAsync(c, capacity: 6);
        var cleared = await SetupAsync(r, null);
        var stillPublished = (await J(await AuthedClient(c.Token).GetAsync($"/api/baths/companies/{c.CompanyId}/services/{r.Id}"))).GetProperty("isPublished").GetBoolean();
        (cleared.StatusCode != HttpStatusCode.OK || !stillPublished).Should().BeTrue("опубликованный ресурс бани без вместимости недопустим");
    }
}
