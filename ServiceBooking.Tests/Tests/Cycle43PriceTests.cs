using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 43, US-43-01 (SPEC.md): цена WhatsApp и MAX в админке — права, валидация, журнал, мгновенная видимость, последствия, гонка двух админов. Написано по SPEC.md и
/// API_CONTRACT_CYCLE43.md §43.22–§43.24; тексты ожидаются дословно из контракта.
/// </summary>
public class Cycle43PriceTests(TestDatabaseFixture fixture) : Cycle43TestBase(fixture)
{
    private const string Comment = "Причина изменения цены";

    private async Task<JsonElement> ItemAsync(HttpClient admin, string transport) =>
        (await J(await Expect(await admin.GetAsync("/api/admin/messenger-options"), HttpStatusCode.OK))).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("transport").GetString() == transport);

    private static decimal? Price(JsonElement item) => item.GetProperty("pricePerMonth").ValueKind == JsonValueKind.Null ? null : item.GetProperty("pricePerMonth").GetDecimal();

    private Task<HttpResponseMessage> PutPriceAsync(HttpClient admin, string transport, decimal? price, bool? active, string? comment, decimal? expectedPrice, bool expectedActive) =>
        admin.PutAsJsonAsync($"/api/admin/messenger-options/{transport}/price",
            new { pricePerMonth = price, isActive = active, comment, expectedPricePerMonth = expectedPrice, expectedIsActive = expectedActive });

    private Task<int> JournalCountAsync(string transport) =>
        DbAsync(db => db.SubscriptionOptionPriceChangeLogs.CountAsync(l => l.OptionCode == CodeOf(transport)));

    // ── права ────────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-01")]
    public async Task PriceScreen_AnonymousGets401_Owner403_AndNothingIsWritten()
    {
        await SetCatalogAsync("Max", 490);
        var owner = await NewAccountAsync();
        var journalBefore = await JournalCountAsync("Max");
        var priceBody = new { pricePerMonth = 1, isActive = (bool?)null, comment = Comment, expectedPricePerMonth = 490, expectedIsActive = true };

        foreach (var (client, code) in new[] { (AnonymousClient(), HttpStatusCode.Unauthorized), (AuthedClient(owner.Owner.Token), HttpStatusCode.Forbidden) })
        {
            var get = await client.GetAsync("/api/admin/messenger-options");
            get.StatusCode.Should().Be(code);
            (await get.Content.ReadAsStringAsync()).Should().BeEmpty();
            (await client.PostAsJsonAsync("/api/admin/messenger-options/Max/price/preview", new { pricePerMonth = 1, isActive = (bool?)null })).StatusCode.Should().Be(code);
            (await client.PutAsJsonAsync("/api/admin/messenger-options/Max/price", priceBody)).StatusCode.Should().Be(code);
            (await client.GetAsync($"/api/admin/billing-accounts/{owner.AccountId}/messengers/Max/quote?months=1")).StatusCode.Should().Be(code);
            (await ExtendAsync(client, owner.AccountId, "Max", 1, 490)).StatusCode.Should().Be(code);
            (await DisableAsync(client, owner.AccountId, "Max", "Now")).StatusCode.Should().Be(code);
        }

        (await ItemAsync(await AdminAsync(), "Max")).GetProperty("pricePerMonth").GetDecimal().Should().Be(490m, "a refused request changes nothing");
        (await JournalCountAsync("Max")).Should().Be(journalBefore);
        (await RowAsync(owner.AccountId, "Max")).Should().BeNull("a refused extend creates no row");
    }

    // ── валидация ────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-02")]
    public async Task PriceValidation_RefusesWithServerRussianTexts_AndWritesNothing()
    {
        await SetCatalogAsync("Max", 490);
        var admin = await AdminAsync();
        var journalBefore = await JournalCountAsync("Max");

        async Task Refuse(HttpResponseMessage response, HttpStatusCode code, string text)
        {
            response.StatusCode.Should().Be(code, await response.Content.ReadAsStringAsync());
            (await response.Content.ReadAsStringAsync()).Should().Be(text);
        }

        await Refuse(await PutPriceAsync(admin, "Max", -1, null, Comment, 490, true), HttpStatusCode.BadRequest, "Цена не может быть отрицательной");
        await Refuse(await PutPriceAsync(admin, "Max", 100000000m, null, Comment, 490, true), HttpStatusCode.BadRequest, "Цена — не больше 99 999 999,99 ₽");
        await Refuse(await PutPriceAsync(admin, "Max", 10.123m, null, Comment, 490, true), HttpStatusCode.BadRequest, "Цена — не больше двух знаков после запятой");
        await Refuse(await PutPriceAsync(admin, "Max", 500, null, "", 490, true), HttpStatusCode.BadRequest, "Укажите причину изменения цены");
        await Refuse(await PutPriceAsync(admin, "Max", 500, null, "   ", 490, true), HttpStatusCode.BadRequest, "Укажите причину изменения цены");
        await Refuse(await PutPriceAsync(admin, "Max", 500, null, null, 490, true), HttpStatusCode.BadRequest, "Укажите причину изменения цены");
        await Refuse(await PutPriceAsync(admin, "Max", 500, null, new string('я', 501), 490, true), HttpStatusCode.BadRequest, "Причина — не длиннее 500 символов");
        await Refuse(await PutPriceAsync(admin, "Max", 490, null, Comment, 490, true), HttpStatusCode.BadRequest, "Цена и активность не изменились — сохранять нечего");
        await Refuse(await PutPriceAsync(admin, "Max", 490, true, Comment, 490, true), HttpStatusCode.BadRequest, "Цена и активность не изменились — сохранять нечего");

        // the same texts on the preview (it needs no comment)
        await Refuse(await admin.PostAsJsonAsync("/api/admin/messenger-options/Max/price/preview", new { pricePerMonth = -0.01m, isActive = (bool?)null }), HttpStatusCode.BadRequest, "Цена не может быть отрицательной");
        await Refuse(await admin.PostAsJsonAsync("/api/admin/messenger-options/Max/price/preview", new { pricePerMonth = 1.005m, isActive = (bool?)null }), HttpStatusCode.BadRequest, "Цена — не больше двух знаков после запятой");
        await Refuse(await admin.PostAsJsonAsync("/api/admin/messenger-options/Max/price/preview", new { pricePerMonth = 490, isActive = (bool?)null }), HttpStatusCode.BadRequest, "Цена и активность не изменились — сохранять нечего");

        // an unknown transport is a 404 with an empty body, not a model-binding 400
        foreach (var bad in new[] { "Telegram", "viber", "0" })
        {
            var put = await PutPriceAsync(admin, bad, 500, null, Comment, 490, true);
            put.StatusCode.Should().Be(HttpStatusCode.NotFound, bad);
            (await put.Content.ReadAsStringAsync()).Should().BeEmpty();
            (await admin.PostAsJsonAsync($"/api/admin/messenger-options/{bad}/price/preview", new { pricePerMonth = 500, isActive = (bool?)null })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        (await ItemAsync(admin, "Max")).GetProperty("pricePerMonth").GetDecimal().Should().Be(490m);
        (await JournalCountAsync("Max")).Should().Be(journalBefore, "a refusal writes no journal row");
    }

    [Fact, TestCase("CY43-03")]
    public async Task PriceBoundaries_ZeroAndMaximumAreValid_EmptyClearsThePrice_AndZeroIsNotEmpty()
    {
        await SetCatalogAsync("WhatsApp", 100);
        var admin = await AdminAsync();
        var current = 100m;
        async Task<JsonElement> Put(decimal? price, string comment = Comment)
        {
            var item = await J(await Expect(await PutPriceAsync(admin, "WhatsApp", price, null, comment, current, true), HttpStatusCode.OK));
            current = price ?? 0m;
            return item;
        }

        (await Put(99999999.99m)).GetProperty("pricePerMonth").GetDecimal().Should().Be(99999999.99m);
        (await PutPriceAsync(admin, "WhatsApp", 0.01m, null, new string('я', 500), 99999999.99m, true)).StatusCode.Should().Be(HttpStatusCode.OK, "a 500-character reason is the allowed maximum");
        current = 0.01m;

        var zero = await Put(0m);
        zero.GetProperty("pricePerMonth").GetDecimal().Should().Be(0m);
        zero.GetProperty("sale").GetProperty("sellable").GetBoolean().Should().BeTrue("zero is a legal price (\"бесплатно\"), distinct from an empty one");
        zero.GetProperty("sale").GetProperty("text").GetString().Should().Be("Продаётся");

        var cleared = await J(await Expect(await PutPriceAsync(admin, "WhatsApp", null, null, "Остановка продаж", 0m, true), HttpStatusCode.OK));
        cleared.GetProperty("pricePerMonth").ValueKind.Should().Be(JsonValueKind.Null);
        cleared.GetProperty("priceText").GetString().Should().Be("не задана");
        cleared.GetProperty("sale").GetProperty("sellable").GetBoolean().Should().BeFalse();
        cleared.GetProperty("sale").GetProperty("reason").GetString().Should().Be("NoPrice");
        cleared.GetProperty("sale").GetProperty("text").GetString().Should().Be("Не продаётся: цена не задана");
        (await ItemAsync(admin, "WhatsApp")).GetProperty("pricePerMonth").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── сохранение только цены и журнал ──────────────────────────────────────────────────

    [Fact, TestCase("CY43-04")]
    public async Task SavingThePrice_ChangesOnlyPrice_AndWritesOneJournalRowWithAuthorAndReason()
    {
        await SetCatalogAsync("Max", 490);
        var admin = await AdminAsync();
        var adminId = (await LoginAsSuperAdminAsync()).UserId;
        await DbAsync(async db =>
        {
            var o = await db.SubscriptionOptions.FirstAsync(x => x.Code == MaxCode);
            o.Description = "Рассылки клиентам в MAX";
            o.UnitName = "номер";
            o.SortOrder = 7;
            o.IsPublic = false;
            o.MaxQuantity = 3;
            await db.SaveChangesAsync();
        });
        var before = await DbAsync(db => db.SubscriptionOptions.AsNoTracking().FirstAsync(x => x.Code == MaxCode));
        var journalBefore = await JournalCountAsync("Max");

        var saved = await J(await Expect(await PutPriceAsync(admin, "Max", 590.50m, null, "  Новая цена с 1 числа  ", 490, true), HttpStatusCode.OK));
        saved.GetProperty("pricePerMonth").GetDecimal().Should().Be(590.50m);
        saved.GetProperty("priceText").GetString().Should().Be("590,50 ₽/мес");

        var after = await DbAsync(db => db.SubscriptionOptions.AsNoTracking().FirstAsync(x => x.Code == MaxCode));
        after.PricePerMonth.Should().Be(590.50m);
        after.IsActive.Should().BeTrue("the activity was not passed, so it is not touched");
        (after.Id, after.Code, after.Name, after.Description, after.Kind, after.CapabilityKey, after.UnitName, after.MaxQuantity, after.IsPublic, after.SortOrder, after.CreatedAtUtc)
            .Should().Be((before.Id, before.Code, before.Name, before.Description, before.Kind, before.CapabilityKey, before.UnitName, before.MaxQuantity, before.IsPublic, before.SortOrder, before.CreatedAtUtc),
                "only the price (and, if asked, the activity) changes; name, description, unit and the rest stay as they were");

        (await JournalCountAsync("Max")).Should().Be(journalBefore + 1, "exactly one journal row per change");
        var row = await DbAsync(db => db.SubscriptionOptionPriceChangeLogs.AsNoTracking().Where(l => l.OptionCode == MaxCode).OrderByDescending(l => l.ChangedAtUtc).FirstAsync());
        row.OldPricePerMonth.Should().Be(490m);
        row.NewPricePerMonth.Should().Be(590.50m);
        row.OldIsActive.Should().BeTrue();
        row.NewIsActive.Should().BeTrue();
        row.Comment.Should().Be("Новая цена с 1 числа", "the reason is trimmed");
        row.ChangedByUserId.Should().Be(adminId);
        row.Source.Should().Be(0, "the price screen");
        row.ChangedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));

        // the history on the screen shows the same row first
        var top = (await ItemAsync(admin, "Max")).GetProperty("history")[0];
        top.GetProperty("oldPricePerMonth").GetDecimal().Should().Be(490m);
        top.GetProperty("newPricePerMonth").GetDecimal().Should().Be(590.50m);
        top.GetProperty("comment").GetString().Should().Be("Новая цена с 1 числа");
        top.GetProperty("changedByName").ValueKind.Should().NotBe(JsonValueKind.Undefined);

        // the activity travels with the price when asked: one row, both fields
        var off = await J(await Expect(await PutPriceAsync(admin, "Max", 590.50m, false, "Выключаем", 590.50m, true), HttpStatusCode.OK));
        off.GetProperty("isActive").GetBoolean().Should().BeFalse();
        off.GetProperty("sale").GetProperty("reason").GetString().Should().Be("Inactive");
        var last = await DbAsync(db => db.SubscriptionOptionPriceChangeLogs.AsNoTracking().Where(l => l.OptionCode == MaxCode).OrderByDescending(l => l.ChangedAtUtc).FirstAsync());
        (last.OldIsActive, last.NewIsActive).Should().Be((true, false));
        await SetCatalogAsync("Max", 490);
    }

    [Fact, TestCase("CY43-04b")]
    public async Task Journal_ShowsAtLeastTwentyNewestFirst_AndHasNoRouteToEditOrDeleteIt()
    {
        await SetCatalogAsync("WhatsApp", 100);
        var admin = await AdminAsync();
        var journalBefore = await JournalCountAsync("WhatsApp");
        decimal current = 100m;
        for (var i = 1; i <= 22; i++)
        {
            var next = 100m + i;
            await Expect(await PutPriceAsync(admin, "WhatsApp", next, null, $"Изменение {i}", current, true), HttpStatusCode.OK);
            current = next;
        }
        (await JournalCountAsync("WhatsApp")).Should().Be(journalBefore + 22, "one journal row per saved change, no more and no less");

        var history = (await ItemAsync(admin, "WhatsApp")).GetProperty("history").EnumerateArray().ToList();
        history.Count.Should().BeInRange(20, 50, "at least 20 latest changes are visible, the screen contract caps it at 50");
        history[0].GetProperty("comment").GetString().Should().Be("Изменение 22", "newest first");
        history[0].GetProperty("newPricePerMonth").GetDecimal().Should().Be(122m);
        history[1].GetProperty("comment").GetString().Should().Be("Изменение 21");
        var dates = history.Select(h => h.GetProperty("changedAt").GetDateTime()).ToList();
        dates.Should().BeInDescendingOrder();

        // the journal cannot be edited or deleted from the interface: no such route answers success, and the rows stay
        var id = Guid.NewGuid();
        foreach (var path in new[] { "/api/admin/messenger-options/WhatsApp/history", $"/api/admin/messenger-options/WhatsApp/history/{id}", "/api/admin/messenger-options/WhatsApp/price" })
        {
            (await admin.DeleteAsync(path)).IsSuccessStatusCode.Should().BeFalse(path);
            (await admin.PatchAsync(path, JsonContent.Create(new { comment = "x" }))).IsSuccessStatusCode.Should().BeFalse(path);
        }
        (await JournalCountAsync("WhatsApp")).Should().Be(journalBefore + 22);
    }

    [Fact, TestCase("CY43-05")]
    public async Task Preview_WritesNothing_AndEachCallIsIndependentOfTheLastOne()
    {
        await SetCatalogAsync("Max", 490);
        var admin = await AdminAsync();
        var journalBefore = await JournalCountAsync("Max");
        var optionBefore = await DbAsync(db => db.SubscriptionOptions.AsNoTracking().FirstAsync(x => x.Code == MaxCode));

        for (var i = 0; i < 3; i++)
            await Expect(await admin.PostAsJsonAsync("/api/admin/messenger-options/Max/price/preview", new { pricePerMonth = 900 + i, isActive = (bool?)null }), HttpStatusCode.OK);

        (await JournalCountAsync("Max")).Should().Be(journalBefore);
        var optionAfter = await DbAsync(db => db.SubscriptionOptions.AsNoTracking().FirstAsync(x => x.Code == MaxCode));
        optionAfter.PricePerMonth.Should().Be(optionBefore.PricePerMonth);
        optionAfter.UpdatedAtUtc.Should().Be(optionBefore.UpdatedAtUtc, "a preview does not even touch the row");
    }

    // ── мгновенная видимость ─────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-06")]
    public async Task NewPrice_IsVisibleEverywhere_OnTheNextRequestAfterTheSaveAnswer()
    {
        await SetCatalogAsync("WhatsApp", 300);
        await SetCatalogAsync("Max", 490);
        var admin = await AdminAsync();
        await SetPlatformFieldAsync(admin, "pricingPublicEnabled", true);
        var a = await NewAccountAsync();
        var owner = AuthedClient(a.Owner.Token);

        // warm every cache that holds the price (catalog of the numbers wizard, public pricing)
        (await OverviewTransportAsync(a, "Max")).GetProperty("pricePerMonth").GetDecimal().Should().Be(490m);
        var pricing = await J(await Expect(await AnonymousClient().GetAsync("/api/pricing"), HttpStatusCode.OK));
        pricing.GetProperty("messengerAddons").EnumerateArray().Single(m => m.GetProperty("transport").GetString() == "Max").GetProperty("pricePerMonth").GetDecimal().Should().Be(490m);
        await CardAsync(a.AccountId, admin);
        await OwnerSubscriptionAsync(a);

        await Expect(await PutPriceAsync(admin, "Max", 590, null, "Рост цены", 490, true), HttpStatusCode.OK);

        var overview = await OverviewTransportAsync(a, "Max");
        overview.GetProperty("pricePerMonth").GetDecimal().Should().Be(590m);
        overview.GetProperty("priceText").GetString().Should().Contain("590");
        overview.GetProperty("sellable").GetBoolean().Should().BeTrue();
        (await J(await Expect(await AnonymousClient().GetAsync("/api/pricing"), HttpStatusCode.OK))).GetProperty("messengerAddons").EnumerateArray()
            .Single(m => m.GetProperty("transport").GetString() == "Max").GetProperty("pricePerMonth").GetDecimal().Should().Be(590m, "the public price list must not lag behind the save");
        Line(await CardAsync(a.AccountId, admin), "Max").GetProperty("pricePerMonth").GetDecimal().Should().Be(590m);
        Line(await CardAsync(a.AccountId, admin), "Max").GetProperty("priceText").GetString().Should().Be("590 ₽/мес");
        OwnerLine(await OwnerSubscriptionAsync(a), "Max").GetProperty("priceText").GetString().Should().Contain("590");

        // a price paid for with a row is seen at the new value by the sums of the admin and the profile at once
        await SeedRowAsync(a.AccountId, "Max", NoonIn(20));
        (await ProfileTotalAsync(a)).Should().Be(1000m + 590m);
        (await ListTotalAsync(a, admin)).Should().Be(1000m + 590m);
        await Expect(await PutPriceAsync(admin, "Max", 610, null, "Ещё рост", 590, true), HttpStatusCode.OK);
        (await ProfileTotalAsync(a)).Should().Be(1000m + 610m);
        (await ListTotalAsync(a, admin)).Should().Be(1000m + 610m);
        (await CardAsync(a.AccountId, admin)).GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1610m);

        // clearing the price stops the sales everywhere at once
        await Expect(await PutPriceAsync(admin, "Max", null, null, "Остановка продаж", 610, true), HttpStatusCode.OK);
        (await J(await Expect(await AnonymousClient().GetAsync("/api/pricing"), HttpStatusCode.OK))).GetProperty("messengerAddons").EnumerateArray()
            .Should().NotContain(m => m.GetProperty("transport").GetString() == "Max");
        var stopped = await OverviewTransportAsync(a, "Max");
        stopped.GetProperty("sellable").GetBoolean().Should().BeFalse();
        stopped.GetProperty("canRequestPayment").GetBoolean().Should().BeFalse();
        var line = Line(await CardAsync(a.AccountId, admin), "Max");
        line.GetProperty("canExtend").GetBoolean().Should().BeFalse();
        line.GetProperty("extendDisabledText").GetString().Should().Be("Не продаётся: цена не задана");
        _ = owner;
        await SetCatalogAsync("Max", 490);
    }

    // ── последствия ──────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-07")]
    public async Task Preview_Consequences_AreServerTextsInFixedOrder()
    {
        var admin = await AdminAsync();
        async Task<JsonElement> Preview(string transport, decimal? price, bool? active = null) =>
            await J(await Expect(await admin.PostAsJsonAsync($"/api/admin/messenger-options/{transport}/price/preview", new { pricePerMonth = price, isActive = active }), HttpStatusCode.OK));
        static List<string> Texts(JsonElement p) => p.GetProperty("consequences").EnumerateArray().Select(x => x.GetString()!).ToList();

        // 1. a price makes the messenger sellable
        await SetCatalogAsync("Max", null);
        var starts = await Preview("Max", 490);
        starts.GetProperty("startsSelling").GetBoolean().Should().BeTrue();
        starts.GetProperty("stopsSelling").GetBoolean().Should().BeFalse();
        starts.GetProperty("sellableBefore").GetBoolean().Should().BeFalse();
        starts.GetProperty("sellableAfter").GetBoolean().Should().BeTrue();
        Texts(starts)[0].Should().Be("MAX начнёт продаваться: цена появится на странице тарифов и в мастере подключения");
        starts.GetProperty("confirmTitle").GetString().Should().Be("Изменить цену MAX?");
        starts.GetProperty("confirmButtonText").GetString().Should().Be("Сохранить");

        // 2. clearing the price stops the sales
        await SetCatalogAsync("Max", 490);
        var stops = await Preview("Max", null);
        stops.GetProperty("stopsSelling").GetBoolean().Should().BeTrue();
        Texts(stops)[0].Should().Be("Продажи MAX остановятся. Уже оплаченные периоды продолжат действовать");
        var inactive = await Preview("Max", 490, false);
        inactive.GetProperty("stopsSelling").GetBoolean().Should().BeTrue("switching the option off stops the sales too");

        // 3. a price change with paid accounts: the number comes from the server, the wording is fixed
        var baseline = (await Preview("Max", 600)).GetProperty("activePaidAccounts").GetInt32();
        var paid1 = await NewAccountAsync();
        var paid2 = await NewAccountAsync();
        var trial = await NewAccountAsync();
        var expired = await NewAccountAsync();
        await SeedRowAsync(paid1.AccountId, "Max", NoonIn(10));
        await SeedRowAsync(paid2.AccountId, "Max", NoonIn(40));
        await SeedRowAsync(trial.AccountId, "Max", NoonIn(5), trial: true);
        await SeedRowAsync(expired.AccountId, "Max", NoonIn(-3));
        var change = await Preview("Max", 600);
        var n = change.GetProperty("activePaidAccounts").GetInt32();
        n.Should().Be(baseline + 2, "only accounts with a currently valid PAID period count: not the trial one, not the expired one");
        var word = n % 10 == 1 && n % 100 != 11 ? "аккаунта" : "аккаунтов";
        Texts(change).Should().Contain($"Оплата MAX действует у {n} {word}: их оплаченные периоды не меняются. Новая цена — для следующих подтверждений оплаты");
        change.GetProperty("startsSelling").GetBoolean().Should().BeFalse();
        change.GetProperty("stopsSelling").GetBoolean().Should().BeFalse();

        // 4. nobody paid: a separate wording (WhatsApp has no paid rows in this class)
        await SetCatalogAsync("WhatsApp", 300);
        Texts(await Preview("WhatsApp", 350)).Should().Contain("Аккаунтов с действующей оплатой WhatsApp нет");

        // 5. zero price asks again with its own sentence
        var zero = await Preview("Max", 0);
        zero.GetProperty("isZeroPrice").GetBoolean().Should().BeTrue();
        Texts(zero).Should().Contain("Цена 0 ₽: MAX будет подключаться бесплатно");

        // 6. a price is set but the messenger is still not sold for another reason
        await SetCatalogAsync("Max", null, open: false);
        var closed = await Preview("Max", 490);
        closed.GetProperty("startsSelling").GetBoolean().Should().BeFalse();
        Texts(closed).Should().Contain("MAX не продаётся: закрыт для владельцев. Цена начнёт действовать, когда это изменится");
        await SetCatalogAsync("Max", null, active: false, open: true);
        Texts(await Preview("Max", 490)).Should().Contain("MAX не продаётся: опция неактивна. Цена начнёт действовать, когда это изменится");

        // 7. clearing a price of a messenger that was not sold anyway: nothing to warn about
        (await Preview("Max", null, true)).GetProperty("stopsSelling").GetBoolean().Should().BeFalse();
        await SetCatalogAsync("Max", 490);
    }

    // ── гонка двух админов ───────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-08")]
    public async Task TwoAdmins_StaleExpectedValues_Get409_AndOnlyOneJournalRowIsWritten()
    {
        await SetCatalogAsync("Max", 490);
        var first = await AdminAsync();
        var second = await AdminAsync();
        var journalBefore = await JournalCountAsync("Max");

        await Expect(await PutPriceAsync(first, "Max", 500, null, "Первый", 490, true), HttpStatusCode.OK);
        var stale = await PutPriceAsync(second, "Max", 700, null, "Второй, по старому экрану", 490, true);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().Be("Цену MAX уже изменил другой администратор. Обновите страницу и проверьте последствия ещё раз");
        var staleActive = await PutPriceAsync(second, "Max", 700, null, "Второй, активность", 500, false);
        staleActive.StatusCode.Should().Be(HttpStatusCode.Conflict, "a stale expectedIsActive is a race too");

        (await ItemAsync(first, "Max")).GetProperty("pricePerMonth").GetDecimal().Should().Be(500m);
        (await JournalCountAsync("Max")).Should().Be(journalBefore + 1);

        // really simultaneous: both read 500, exactly one wins
        var results = await Task.WhenAll(PutPriceAsync(first, "Max", 510, null, "Гонка А", 500, true), PutPriceAsync(second, "Max", 520, null, "Гонка Б", 500, true));
        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var winner = results.Single(r => r.StatusCode == HttpStatusCode.OK);
        (await ItemAsync(first, "Max")).GetProperty("pricePerMonth").GetDecimal().Should().Be((await J(winner)).GetProperty("pricePerMonth").GetDecimal());
        (await JournalCountAsync("Max")).Should().Be(journalBefore + 2, "the loser leaves no trace in the journal");
        await SetCatalogAsync("Max", 490);
    }

    // ── старые пути ──────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-09")]
    public async Task OldOptionsApi_PriceChangeIsJournalledAndSeenWithoutWaitingForTheCache()
    {
        await SetCatalogAsync("Max", 490);
        var admin = await AdminAsync();
        var a = await NewAccountAsync();
        (await OverviewTransportAsync(a, "Max")).GetProperty("pricePerMonth").GetDecimal().Should().Be(490m); // warm the catalog cache

        var options = await J(await Expect(await admin.GetAsync("/api/admin/options"), HttpStatusCode.OK));
        var max = options.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("code").GetString() == MaxCode);
        object Body(decimal? price, string name, bool active = true) => new
        {
            code = MaxCode, name, description = max.GetProperty("description").ValueKind == JsonValueKind.Null ? null : max.GetProperty("description").GetString(),
            kind = max.GetProperty("kind").GetString(), capabilityKey = max.GetProperty("capabilityKey").ValueKind == JsonValueKind.Null ? null : max.GetProperty("capabilityKey").GetString(),
            pricePerMonth = price, unitName = max.GetProperty("unitName").ValueKind == JsonValueKind.Null ? null : max.GetProperty("unitName").GetString(),
            maxQuantity = max.GetProperty("maxQuantity").ValueKind == JsonValueKind.Null ? (int?)null : max.GetProperty("maxQuantity").GetInt32(),
            isPublic = max.GetProperty("isPublic").GetBoolean(), isActive = active, sortOrder = max.GetProperty("sortOrder").GetInt32(),
        };
        var id = max.GetProperty("id").GetGuid();
        var name = max.GetProperty("name").GetString()!;
        var journalBefore = await JournalCountAsync("Max");

        await Expect(await admin.PutAsJsonAsync($"/api/admin/options/{id}", Body(555, name)), HttpStatusCode.OK);
        (await OverviewTransportAsync(a, "Max")).GetProperty("pricePerMonth").GetDecimal().Should().Be(555m, "the old route resets the caches too");
        (await JournalCountAsync("Max")).Should().Be(journalBefore + 1);
        var row = await DbAsync(db => db.SubscriptionOptionPriceChangeLogs.AsNoTracking().Where(l => l.OptionCode == MaxCode).OrderByDescending(l => l.ChangedAtUtc).FirstAsync());
        (row.Source, row.OldPricePerMonth, row.NewPricePerMonth, row.Comment).Should().Be((1, 490m, 555m, (string?)null));
        row.ChangedByUserId.Should().NotBeNullOrEmpty();

        // a change of anything but the price and the activity is no price event
        await Expect(await admin.PutAsJsonAsync($"/api/admin/options/{id}", Body(555, name + " ")), HttpStatusCode.OK);
        (await JournalCountAsync("Max")).Should().Be(journalBefore + 1);
        await Expect(await admin.PutAsJsonAsync($"/api/admin/options/{id}", Body(555, name)), HttpStatusCode.OK);

        // the activity switch is an event
        await Expect(await admin.PutAsJsonAsync($"/api/admin/options/{id}", Body(555, name, active: false)), HttpStatusCode.OK);
        (await JournalCountAsync("Max")).Should().Be(journalBefore + 2);
        (await OverviewTransportAsync(a, "Max")).GetProperty("sellable").GetBoolean().Should().BeFalse("an inactive option is not sold, and the wizard sees it at once");
        await SetCatalogAsync("Max", 490);
    }

    [Fact, TestCase("CY43-10")]
    public async Task PlatformSettings_StillAcceptsTheOldChannelPriceField_WithoutErrorAndWithoutEffect()
    {
        await SetCatalogAsync("Max", 490);
        var admin = await AdminAsync();
        var current = await J(await Expect(await admin.GetAsync("/api/admin/platform-settings"), HttpStatusCode.OK));

        var withOld = await admin.PutAsJsonAsync("/api/admin/platform-settings",
            new { channelPricePerMonth = 999, channelIdleDays = current.GetProperty("channelIdleDays").GetInt32(), pricingPublicEnabled = current.GetProperty("pricingPublicEnabled").GetBoolean() });
        withOld.StatusCode.Should().Be(HttpStatusCode.OK, await withOld.Content.ReadAsStringAsync());
        var withoutOld = await admin.PutAsJsonAsync("/api/admin/platform-settings",
            new { channelIdleDays = current.GetProperty("channelIdleDays").GetInt32(), pricingPublicEnabled = current.GetProperty("pricingPublicEnabled").GetBoolean() });
        withoutOld.StatusCode.Should().Be(HttpStatusCode.OK, await withoutOld.Content.ReadAsStringAsync());

        (await ItemAsync(admin, "Max")).GetProperty("pricePerMonth").GetDecimal().Should().Be(490m, "the old field does not move the price of an option");
    }

    // ── итог продажи и подсказки ────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-11")]
    public async Task Screen_ShowsSaleVerdictWithReason_Hints_AndTwoLinesInOrder()
    {
        var admin = await AdminAsync();
        var screen = await J(await Expect(await admin.GetAsync("/api/admin/messenger-options"), HttpStatusCode.OK));
        screen.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("transport").GetString()).Should().Equal("WhatsApp", "Max");
        screen.GetProperty("planMatrixNote").GetString().Should().Be("Мессенджеры WhatsApp и MAX подключаются отдельно от тарифа. Цена — во вкладке уведомлений");
        screen.GetProperty("priceHint").GetString().Should().Be(
            "Цена действует для всех аккаунтов и всех линеек. Мессенджер продаётся, когда он открыт для владельцев, опция активна, цена задана и оферта опубликована. Изменение цены не меняет уже оплаченные периоды");

        async Task<JsonElement> Sale(decimal? price, bool active, bool open)
        {
            await SetCatalogAsync("Max", price, active, open);
            return (await ItemAsync(admin, "Max")).GetProperty("sale");
        }

        var sold = await Sale(490, true, true);
        (sold.GetProperty("sellable").GetBoolean(), sold.GetProperty("reason").ValueKind, sold.GetProperty("text").GetString()).Should().Be((true, JsonValueKind.Null, "Продаётся"));

        var closed = await Sale(null, false, false); // everything is wrong at once: the first failing check wins
        (closed.GetProperty("reason").GetString(), closed.GetProperty("text").GetString()).Should().Be(("Closed", "Не продаётся: закрыт для владельцев"));
        var inactive = await Sale(null, false, true);
        (inactive.GetProperty("reason").GetString(), inactive.GetProperty("text").GetString()).Should().Be(("Inactive", "Не продаётся: опция неактивна"));
        var noPrice = await Sale(null, true, true);
        (noPrice.GetProperty("reason").GetString(), noPrice.GetProperty("text").GetString()).Should().Be(("NoPrice", "Не продаётся: цена не задана"));
        await SetCatalogAsync("Max", 490);
    }

    [Fact, TestCase("CY43-11b")]
    public async Task OpenSwitchAndSaleVerdict_AgreeWithTheNumbersWizard()
    {
        // the verdict of the screen is the rule the wizard uses: whatever the screen calls "Продаётся", the wizard offers, and vice versa
        var admin = await AdminAsync();
        var a = await NewAccountAsync();
        foreach (var (price, active, open) in new (decimal?, bool, bool)[] { (490, true, true), (null, true, true), (490, false, true), (490, true, false), (0, true, true) })
        {
            await SetCatalogAsync("Max", price, active, open);
            var screen = (await ItemAsync(admin, "Max")).GetProperty("sale").GetProperty("sellable").GetBoolean();
            var overview = await J(await Expect(await AuthedClient(a.Owner.Token).GetAsync("/api/notification-channels/overview"), HttpStatusCode.OK));
            var row = overview.GetProperty("transports").EnumerateArray().Where(t => t.GetProperty("transport").GetString() == "Max").ToList();
            var wizard = row.Count == 1 && row[0].GetProperty("sellable").GetBoolean(); // a closed transport is not in the wizard at all
            wizard.Should().Be(screen, $"price={price} active={active} open={open}");
            Line(await CardAsync(a.AccountId, admin), "Max").GetProperty("canExtend").GetBoolean().Should().Be(screen, "the account card offers to connect exactly what is sold");
        }
        await SetCatalogAsync("Max", 490);
    }
}
