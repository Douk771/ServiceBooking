using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: публичная сторона — каталог, города, фильтр даты, страница комплекса, страница ресурса, бронь с числом гостей, страница брони, «Мои брони», изоляция видов
/// (CY42-40…49). Писано по SPEC_CYCLE42_BANI.md (US-42-11…17) и API_CONTRACT_CYCLE42.md §42.22–§42.26, без чтения реализации. Каждый тест берёт свой город справочника
/// (<see cref="Cycle42TestBase.NthCityIdAsync"/>), чтобы данные соседних тестов класса не попадали в проверяемую выдачу.
/// </summary>
public class Cycle42PublicTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private static readonly string[] ForbiddenWords = ["заказ", "Заказ", "проживани", "заселени", "заезд", "бизнес-день", "задаток", "депозит", "туристическ"];

    private static void NoDomWords(string text, string because)
    {
        foreach (var w in ForbiddenWords) text.Should().NotContain(w, because);
    }

    // ── CY42-40: каталог — состав выдачи ─────────────────────────────────────────

    [Fact, TestCase("CY42-40")]
    public async Task Catalog_ShowsOnlyPublishedResources_OfAcceptingListedBathCompanies()
    {
        var city = await NthCityIdAsync(10);
        var good = await CreateBathAsync(city);
        var goodRes = await AddResourceAsync(good, "Русская баня");
        var draft = await AddResourceAsync(good, "Черновик", publish: false);
        var noPlan = await CreateBathAsync(city, trial: false);
        var noPlanRes = await AddResourceAsync(noPlan, "Без тарифа", publish: false);
        var hidden = await CreateBathAsync(city);
        var hiddenRes = await AddResourceAsync(hidden, "Скрытая");
        var noProvider = await CreateBathAsync(city, provider: false);
        var noProviderRes = await AddResourceAsync(noProvider, "Без исполнителя");
        var blocked = await CreateBathAsync(city);
        var blockedRes = await AddResourceAsync(blocked, "Заблокированная");

        // «Показывать в каталоге» выключено владельцем
        var hiddenCompany = await (await AuthedClient(hidden.Token).GetAsync($"/api/baths/companies/{hidden.CompanyId}")).Content.ReadJsonAsync<BathsCompanyManageDto>();
        var off = await AuthedClient(hidden.Token).PutJsonAsync($"/api/baths/companies/{hidden.CompanyId}/settings", hiddenCompany!.Settings! with { ShowInCatalog = false });
        off.StatusCode.Should().Be(HttpStatusCode.OK, await off.Content.ReadAsStringAsync());
        await WithDbAsync(async db =>
        {
            var c = await db.Companies.SingleAsync(x => x.Id == blocked.CompanyId);
            c.IsActive = false;
            await db.SaveChangesAsync();
        });
        InvalidateBathsCatalog();

        var ids = CatalogIds(await CatalogAsync($"?cityId={city}&pageSize=50"));
        ids.Should().Contain(goodRes.Id);
        ids.Should().NotContain([draft.Id, noPlanRes.Id, hiddenRes.Id, noProviderRes.Id, blockedRes.Id],
            "в каталог не попадают черновики, компании без тарифа, скрытые настройкой, без сведений об исполнителе и заблокированные");

        // публикация видна сразу, не через 30 секунд кеша «базы»
        var baseline = await CatalogAsync($"?cityId={city}&pageSize=50");
        baseline.GetProperty("totalCount").GetInt32().Should().Be(1);
        var publish = await AuthedClient(good.Token).PostJsonAsync($"/api/baths/companies/{good.CompanyId}/services/{draft.Id}/publish", new { });
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());
        CatalogIds(await CatalogAsync($"?cityId={city}&pageSize=50")).Should().Contain(draft.Id, "публикация сбрасывает кеш каталога");
    }

    [Fact, TestCase("CY42-40")]
    public async Task Catalog_Card_HasTheAnnouncedFields_AndNothingPrivate()
    {
        var city = await NthCityIdAsync(11);
        var c = await CreateBathAsync(city);
        var res = await AddResourceAsync(c, "Баня на дровах", capacity: 8, minHours: 2, priceRub: 1500);
        InvalidateBathsCatalog();

        var r = await AnonymousClient().GetAsync($"/api/baths/catalog?cityId={city}");
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var page = JsonDocument.Parse(body).RootElement;
        page.GetProperty("cityId").GetInt32().Should().Be(city);
        page.GetProperty("dateFilterApplied").GetBoolean().Should().BeFalse();
        var card = page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("resourceId").GetGuid() == res.Id);
        card.GetProperty("resourceName").GetString().Should().Be("Баня на дровах");
        card.GetProperty("companyName").GetString().Should().Be(c.Name);
        card.GetProperty("companySlug").GetString().Should().Be(c.Slug);
        card.GetProperty("address").GetString().Should().Be("Шерегеш, ул. Лесная, 5");
        card.GetProperty("cityName").GetString().Should().NotBeNullOrEmpty();
        card.GetProperty("priceFromRub").GetInt32().Should().Be(1500);
        card.GetProperty("minHours").GetInt32().Should().Be(2);
        card.GetProperty("capacity").GetInt32().Should().Be(8);
        card.GetProperty("url").GetString().Should().Be($"/{c.Slug}/{res.Slug}");
        body.Should().NotContain(PaymentDetailsText, "реквизитов в каталоге нет").And.NotContain("Иванов Иван", "ФИО исполнителя в каталоге нет")
            .And.NotContain(ValidPersonInn).And.NotContain(c.Owner.Phone);
    }

    // ── CY42-41: каталог — страницы и порядок ────────────────────────────────────

    [Fact, TestCase("CY42-41")]
    public async Task Catalog_Pagination_OutOfRangePage_AndPageSizeLimits()
    {
        var city = await NthCityIdAsync(12);
        var c = await CreateBathAsync(city);
        var ids = new List<Guid>();
        foreach (var n in new[] { "А-баня", "Б-чан", "В-купель" }) ids.Add((await AddResourceAsync(c, n)).Id);
        InvalidateBathsCatalog();

        var first = await CatalogAsync($"?cityId={city}&pageSize=2&page=1");
        first.GetProperty("totalCount").GetInt32().Should().Be(3);
        first.GetProperty("items").GetArrayLength().Should().Be(2);
        var second = await CatalogAsync($"?cityId={city}&pageSize=2&page=2");
        second.GetProperty("items").GetArrayLength().Should().Be(1);
        CatalogIds(first).Concat(CatalogIds(second)).Should().Equal(ids, "порядок ресурсов комплекса — порядок владельца (позиция), страницы не пересекаются");

        var beyond = await CatalogAsync($"?cityId={city}&pageSize=2&page=9");
        beyond.GetProperty("items").GetArrayLength().Should().Be(0);
        beyond.GetProperty("totalCount").GetInt32().Should().Be(3, "страница за пределами — пустая, но с верным общим числом");

        (await CatalogAsync($"?cityId={city}&pageSize=1000")).GetProperty("pageSize").GetInt32().Should().Be(50);
        (await CatalogAsync($"?cityId={city}&pageSize=0")).GetProperty("pageSize").GetInt32().Should().Be(1);
        (await CatalogAsync($"?cityId={city}")).GetProperty("pageSize").GetInt32().Should().Be(12);
    }

    [Fact, TestCase("CY42-41")]
    public async Task Catalog_OrderedByCompanyName_ThenByResourcePosition()
    {
        var city = await NthCityIdAsync(13);
        var zeta = await CreateBathAsync(city, name: Unique("Яр-"));
        var alpha = await CreateBathAsync(city, name: Unique("Аист-"));
        var zetaRes = await AddResourceAsync(zeta, "Зета-баня");
        var alphaFirst = await AddResourceAsync(alpha, "Я-первая");
        var alphaSecond = await AddResourceAsync(alpha, "А-вторая");
        InvalidateBathsCatalog();

        CatalogIds(await CatalogAsync($"?cityId={city}&pageSize=50")).Should().Equal(new[] { alphaFirst.Id, alphaSecond.Id, zetaRes.Id },
            "сначала компания по названию, внутри — ресурсы в порядке владельца, а не по алфавиту названий");
    }

    // ── CY42-42: города ──────────────────────────────────────────────────────────

    [Fact, TestCase("CY42-42")]
    public async Task Cities_ListOnlyCitiesWithResources_WithCounts_AndUnknownCityIs400()
    {
        var cityA = await NthCityIdAsync(20);
        var cityB = await NthCityIdAsync(21);
        var emptyCity = await NthCityIdAsync(22);
        var a = await CreateBathAsync(cityA);
        await AddResourceAsync(a, "Баня 1");
        await AddResourceAsync(a, "Баня 2");
        var b = await CreateBathAsync(cityB);
        await AddResourceAsync(b, "Чан");
        InvalidateBathsCatalog();

        var r = await AnonymousClient().GetAsync("/api/baths/catalog/cities");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var items = (await J(r)).GetProperty("items").EnumerateArray().ToList();
        items.Single(i => i.GetProperty("id").GetInt32() == cityA).GetProperty("resourcesCount").GetInt32().Should().Be(2);
        items.Single(i => i.GetProperty("id").GetInt32() == cityB).GetProperty("resourcesCount").GetInt32().Should().Be(1);
        items.Should().NotContain(i => i.GetProperty("id").GetInt32() == emptyCity, "города без ресурсов в списке нет");
        var names = items.Select(i => i.GetProperty("name").GetString()!).ToList();
        names.Should().BeInAscendingOrder(StringComparer.CurrentCultureIgnoreCase);

        var empty = await CatalogAsync($"?cityId={emptyCity}");
        empty.GetProperty("items").GetArrayLength().Should().Be(0);
        empty.GetProperty("emptyText").GetString().Should().Be("В этом городе пока нет бань");

        var unknown = await AnonymousClient().GetAsync("/api/baths/catalog?cityId=99999999");
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("Город не найден");
    }

    // ── CY42-43: фильтр даты ─────────────────────────────────────────────────────

    [Fact, TestCase("CY42-43")]
    public async Task Catalog_DateFilter_KeepsOnlyResourcesWithAFreeStartThatDay()
    {
        var city = await NthCityIdAsync(30);
        var c = await CreateBathAsync(city);
        var daily = await AddResourceAsync(c, "Каждый день");
        var mondaysOnly = await AddResourceAsync(c, "Только по понедельникам", windowsByDay: new Dictionary<int, (int, int)[]> { [1] = [(600, 720)] });
        InvalidateBathsCatalog();
        var monday = NextWeekday(DayOfWeek.Monday, 4);
        var tuesday = monday.AddDays(1);

        var onMonday = await CatalogAsync($"?cityId={city}&date={D(monday)}");
        onMonday.GetProperty("dateFilterApplied").GetBoolean().Should().BeTrue();
        CatalogIds(onMonday).Should().BeEquivalentTo([daily.Id, mondaysOnly.Id]);

        var onTuesday = await CatalogAsync($"?cityId={city}&date={D(tuesday)}");
        CatalogIds(onTuesday).Should().Equal(new[] { daily.Id }, "во вторник у второго ресурса окон нет");
        onTuesday.GetProperty("date").GetString().Should().Be(D(tuesday));

        // занятость учитывается без кеша: окно 10:00–12:00 целиком занято одной бронью
        var wholeWindow = await BookBathAsync(mondaysOnly.Id, monday, 600, 2);
        (await BathOrderPageAsync(wholeWindow)).GetProperty("status").GetString().Should().BeOneOf("Held", "AwaitingPaymentCheck", "Confirmed");
        var afterBooking = await CatalogAsync($"?cityId={city}&date={D(monday)}");
        CatalogIds(afterBooking).Should().Equal(new[] { daily.Id });
    }

    [Fact, TestCase("CY42-43")]
    public async Task Catalog_DateFilter_BadInputs_AndEmptyText()
    {
        var city = await NthCityIdAsync(31);
        var c = await CreateBathAsync(city);
        await AddResourceAsync(c, "Только по пятницам", windowsByDay: new Dictionary<int, (int, int)[]> { [5] = [(600, 1200)] });
        InvalidateBathsCatalog();
        var saturday = NextWeekday(DayOfWeek.Saturday, 4);

        var past = await AnonymousClient().GetAsync($"/api/baths/catalog?date={D(InDays(-3))}");
        past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await past.Content.ReadAsStringAsync()).Should().Contain("Эта дата уже прошла");
        var bad = await AnonymousClient().GetAsync("/api/baths/catalog?date=31.12.2030");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("Неверный формат даты");

        var none = await CatalogAsync($"?cityId={city}&date={D(saturday)}");
        none.GetProperty("items").GetArrayLength().Should().Be(0);
        none.GetProperty("emptyText").GetString().Should().Be("На эту дату свободного времени нет");
    }

    // ── CY42-44: страница комплекса ──────────────────────────────────────────────

    [Fact, TestCase("CY42-44")]
    public async Task ComplexPage_ShowsPublishedResources_PublicProvider_AndNoPrivateData()
    {
        var c = await CreateBathAsync();
        var published = await AddResourceAsync(c, "Баня");
        var draft = await AddResourceAsync(c, "Черновик", publish: false);

        var r = await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug.ToUpperInvariant()}");
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.OK, "адрес без учёта регистра: " + body);
        var page = JsonDocument.Parse(body).RootElement;
        page.GetProperty("name").GetString().Should().Be(c.Name);
        page.GetProperty("slug").GetString().Should().Be(c.Slug);
        page.GetProperty("address").GetString().Should().Be("Шерегеш, ул. Лесная, 5");
        page.GetProperty("cityName").GetString().Should().NotBeNullOrEmpty();
        page.GetProperty("timeZoneId").GetString().Should().NotBeNullOrEmpty();
        page.GetProperty("acceptingBookings").GetBoolean().Should().BeTrue();
        var resources = page.GetProperty("resources").EnumerateArray().ToList();
        resources.Should().ContainSingle().Which.GetProperty("resourceId").GetGuid().Should().Be(published.Id);
        resources.Select(x => x.GetProperty("resourceId").GetGuid()).Should().NotContain(draft.Id);
        page.GetProperty("provider").GetProperty("inn").GetString().Should().Be(ValidPersonInn);
        body.Should().NotContain("Иванов Иван", "ФИО физлица-исполнителя не показывается (ЮР-3)").And.NotContain(PaymentDetailsText, "реквизитов нет");
        body.Should().NotContainEquivalentOf("trial").And.NotContainEquivalentOf("plan", "тарифа и причин закрытого гейта в ответе нет");
    }

    [Fact, TestCase("CY42-44")]
    public async Task ComplexPage_NotFound_ForMissingBlockedOrForeignKindCompany()
    {
        var c = await CreateBathAsync();
        await AddResourceAsync(c);
        var stays = await CreateStaysCompanyAsync();

        (await AnonymousClient().GetAsync("/api/baths/public/companies/net-takoy-adres")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var foreign = await AnonymousClient().GetAsync($"/api/baths/public/companies/{stays.Slug}");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().BeEmpty("без оракула существования");

        await WithDbAsync(async db =>
        {
            var company = await db.Companies.SingleAsync(x => x.Id == c.CompanyId);
            company.IsActive = false;
            await db.SaveChangesAsync();
        });
        (await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "заблокированная компания");
    }

    // ── CY42-45: страница ресурса ────────────────────────────────────────────────

    [Fact, TestCase("CY42-45")]
    public async Task ResourcePage_CapacityLocalTime_Ordering_AndNoOccupancy()
    {
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c, "Чан на дровах", capacity: 6);
        var friday = NextWeekday(DayOfWeek.Friday, 5);
        await BookBathAsync(res.Id, friday, 720, 2, guests: 3);

        var r = await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}/services/{res.Slug}");
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var page = JsonDocument.Parse(body).RootElement;
        page.GetProperty("name").GetString().Should().Be("Чан на дровах");
        page.GetProperty("capacity").GetInt32().Should().Be(6);
        page.GetProperty("cityName").GetString().Should().NotBeNullOrEmpty();
        page.GetProperty("localTimeNote").GetString().Should().StartWith("Время местное").And.Contain(page.GetProperty("cityName").GetString()!);
        page.GetProperty("standalone").GetProperty("ordering").GetBoolean().Should().BeTrue("бронь без проживания у бани включена всегда");
        page.GetProperty("acceptingBookings").GetBoolean().Should().BeTrue();
        body.Should().NotContain("Анна Гость", "имён гостей нет").And.NotContain(PaymentDetailsText);
        body.Should().NotContainEquivalentOf("touristTax", "туристического налога у бани нет");

        var starts = await AnonymousClient().GetAsync($"/api/baths/public/services/{res.Id}/starts?date={D(friday)}");
        starts.StatusCode.Should().Be(HttpStatusCode.OK, await starts.Content.ReadAsStringAsync());
        var startMinutes = (await J(starts)).GetProperty("starts").EnumerateArray().Select(s => s.GetProperty("startMinute").GetInt32()).ToList();
        startMinutes.Should().NotContain(720, "занятый старт не предлагается");
        startMinutes.Should().Contain(360);
        var avail = await AnonymousClient().GetAsync($"/api/baths/public/services/{res.Id}/availability?from={D(friday)}&days=7");
        avail.StatusCode.Should().Be(HttpStatusCode.OK, await avail.Content.ReadAsStringAsync());
    }

    [Fact, TestCase("CY42-45")]
    public async Task ResourcePage_Unpublished_Or_WrongCompanySlug_IsNotFound()
    {
        var c = await CreateBathAsync();
        var draft = await AddResourceAsync(c, "Черновик", publish: false);
        var other = await CreateBathAsync();
        var otherRes = await AddResourceAsync(other);

        (await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}/services/{draft.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync($"/api/baths/public/companies/{c.Slug}/services/{otherRes.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "ресурс чужого комплекса");
        (await AnonymousClient().GetAsync($"/api/baths/public/services/{draft.Id}/starts?date={D(InDays(5))}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BathQuoteAsync(draft.Id, InDays(5), 720, 2)).StatusCode.Should().Be(HttpStatusCode.NotFound, "неопубликованный ресурс не считается");
    }

    // ── CY42-46: бронь с гостями и страница брони ────────────────────────────────

    [Fact, TestCase("CY42-46")]
    public async Task Booking_WithGuests_CreatesHeldBooking_WithBathWording_AndLocalTime()
    {
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c, capacity: 6);
        var friday = NextWeekday(DayOfWeek.Friday, 6);

        var quote = await J(await BathQuoteAsync(res.Id, friday, 1200, 2));
        quote.GetProperty("ok").GetBoolean().Should().BeTrue(quote.ToString());
        quote.GetProperty("totalRub").GetInt32().Should().Be(2 * HourPrice);
        var phone = UniquePhone();
        var r = await PostBathOrderAsync(res.Id, friday, 1200, 2, guests: 4, phone: phone, key: Guid.NewGuid());
        var body = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var created = JsonDocument.Parse(body).RootElement;
        var token = created.GetProperty("token").GetString()!;
        created.GetProperty("orderUrl").GetString().Should().Be($"https://bani.ezbook.ru/s/{token}");

        var page = await BathOrderPageAsync(token);
        page.GetProperty("status").GetString().Should().Be("Held");
        page.GetProperty("guestsCount").GetInt32().Should().Be(4);
        page.GetProperty("totalRub").GetInt32().Should().Be(4000);
        page.GetProperty("prepayRub").GetInt32().Should().Be(1200, "30 % от 4000");
        page.GetProperty("cityName").GetString().Should().NotBeNullOrEmpty();
        page.GetProperty("localTimeNote").GetString().Should().StartWith("Время местное");
        page.GetProperty("bookAgainUrl").GetString().Should().Be($"/{c.Slug}", "в адресе нет имени и телефона гостя");
        page.GetRawText().Should().NotContain(phone, "полный телефон гостю не возвращается");
        page.GetProperty("availableActions").EnumerateArray().Select(a => a.GetString()).Should().NotBeEmpty();
        NoDomWords(page.GetRawText(), "гостевые тексты бани говорят «бронь»");
        page.GetProperty("statusText").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact, TestCase("CY42-46")]
    public async Task Booking_Price_DoesNotDependOnGuests_AndCancelByGuest_FreesTheTime()
    {
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c, capacity: 6);
        var date = NextWeekday(DayOfWeek.Saturday, 5);
        var one = await PostBathOrderAsync(res.Id, date, 600, 2, guests: 1);
        one.StatusCode.Should().Be(HttpStatusCode.Created, await one.Content.ReadAsStringAsync());
        var six = await PostBathOrderAsync(res.Id, date, 900, 2, guests: 6);
        six.StatusCode.Should().Be(HttpStatusCode.Created, await six.Content.ReadAsStringAsync());
        (await J(one)).GetProperty("order").GetProperty("totalRub").GetInt32().Should().Be((await J(six)).GetProperty("order").GetProperty("totalRub").GetInt32());

        var token = (await J(one)).GetProperty("token").GetString()!;
        var cancel = await AnonymousClient().PostJsonAsync($"/api/baths/service-orders/public/{token}/cancel", new { });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());
        var after = await BathOrderPageAsync(token);
        after.GetProperty("status").GetString().Should().Be("CancelledByGuest");
        NoDomWords(after.GetRawText(), "после отмены тексты тоже словом «бронь»");
        ((await J(await AnonymousClient().GetAsync($"/api/baths/public/services/{res.Id}/starts?date={D(date)}"))).GetProperty("starts").EnumerateArray()
            .Select(s => s.GetProperty("startMinute").GetInt32())).Should().Contain(600, "время освободилось");
        (await ActiveSessionsAsync(res.Id)).Should().HaveCount(1);
    }

    // ── CY42-47: число гостей, идемпотентность, занято ───────────────────────────

    [Theory, TestCase("CY42-47")]
    [InlineData(null, "Укажите, сколько человек придёт")]
    [InlineData(0, "Число гостей — от 1 до 6")]
    [InlineData(7, "Число гостей — от 1 до 6")]
    [InlineData(-1, "Число гостей — от 1 до 6")]
    public async Task Booking_GuestsOutOfRange_Is400WithExactText(int? guests, string text)
    {
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c, capacity: 6);
        var r = await PostBathOrderAsync(res.Id, InDays(6), 720, 2, guests: guests);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await r.Content.ReadAsStringAsync()).Should().Contain(text);
        (await ActiveSessionsAsync(res.Id)).Should().BeEmpty("отказ не оставляет удержания");
    }

    [Fact, TestCase("CY42-47")]
    public async Task Booking_BoundaryGuests_OneAndCapacity_AreAccepted()
    {
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c, capacity: 6);
        var date = InDays(7);
        (await PostBathOrderAsync(res.Id, date, 480, 1, guests: 1)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostBathOrderAsync(res.Id, date, 780, 1, guests: 6)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CY42-47")]
    public async Task Booking_SameIdempotencyKey_ReturnsTheSameBooking_AndBusyTimeIsSlotTaken()
    {
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c);
        var date = InDays(8);
        var key = Guid.NewGuid();
        var phone = UniquePhone();
        var first = await PostBathOrderAsync(res.Id, date, 720, 2, phone: phone, key: key);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var repeat = await PostBathOrderAsync(res.Id, date, 720, 2, phone: phone, key: key);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK, "повтор с тем же ключом — та же бронь");
        (await J(repeat)).GetProperty("token").GetString().Should().Be((await J(first)).GetProperty("token").GetString());
        (await ActiveSessionsAsync(res.Id)).Should().HaveCount(1);

        var busy = await PostBathOrderAsync(res.Id, date, 780, 2);
        busy.StatusCode.Should().Be(HttpStatusCode.Conflict, "13:00 попадает в бронь 12:00–14:00");
        (await Code(busy)).Should().Be("SlotTaken");
        var buffer = await PostBathOrderAsync(res.Id, date, 840, 1);
        buffer.StatusCode.Should().Be(HttpStatusCode.Conflict, "14:00 внутри зазора 30 минут после брони");
        (await PostBathOrderAsync(res.Id, date, 870, 1)).StatusCode.Should().Be(HttpStatusCode.Created, "14:30 — ровно конец зазора");
    }

    [Fact, TestCase("CY42-47")]
    public async Task Booking_WrongPrice_IsPriceChanged_AndBadPhoneOrEmptyName_Is400()
    {
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c);
        var date = InDays(9);
        var wrong = await PostBathOrderAsync(res.Id, date, 720, 2, total: 1);
        wrong.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(wrong)).Should().Be("PriceChanged");
        (await PostBathOrderAsync(res.Id, date, 720, 2, phone: "123")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var noName = await AnonymousClient().PostJsonAsync($"/api/baths/public/services/{res.Id}/orders", BathOrderBody(date, 720, 2, 2, 4000, UniquePhone(), name: ""));
        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ActiveSessionsAsync(res.Id)).Should().BeEmpty();
    }

    // ── CY42-48: «Мои брони» с гейтом подтверждённого номера ─────────────────────

    [Fact, TestCase("CY42-48")]
    public async Task MyBookings_RequiresLogin()
    {
        (await AnonymousClient().GetAsync("/api/baths/service-orders/my")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("CY42-48")]
    public async Task MyBookings_ByAccount_AndByConfirmedPhone_ButNotByUnconfirmedPhone()
    {
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c);
        var c2 = await CreateBathAsync();
        var res2 = await AddResourceAsync(c2);
        var date = NextWeekday(DayOfWeek.Friday, 6);

        var confirmed = await RegisterAsync();
        await MarkPhoneVerifiedAsync(confirmed.Phone, confirmed.UserId);
        var unconfirmed = await RegisterAsync();

        // анонимная бронь на номер подтверждённого аккаунта — видна ему; на номер неподтверждённого — нет
        var anonConfirmed = await BookBathAsync(res.Id, date, 600, 2, phone: confirmed.Phone);
        var anonUnconfirmed = await BookBathAsync(res.Id, date, 840, 2, phone: unconfirmed.Phone);
        // бронь вошедшего неподтверждённого — по аккаунту видна ему
        var signed = await BookBathAsync(res2.Id, date, 1080, 2, phone: unconfirmed.Phone, client: AuthedClient(unconfirmed.Token));
        // чужая бронь
        var stranger = await BookBathAsync(res.Id, date.AddDays(1), 600, 2);

        async Task<List<JsonElement>> MyAsync(string token)
        {
            var r = await AuthedClient(token).GetAsync("/api/baths/service-orders/my");
            r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            return (await J(r)).GetProperty("items").EnumerateArray().ToList();
        }

        var mineConfirmed = await MyAsync(confirmed.Token);
        mineConfirmed.Select(i => i.GetProperty("orderUrl").GetString()).Should().BeEquivalentTo([$"/s/{anonConfirmed}"]);
        var item = mineConfirmed.Single();
        item.GetProperty("companyName").GetString().Should().Be(c.Name);
        item.GetProperty("resourceName").GetString().Should().Be("Русская баня");
        item.GetProperty("totalRub").GetInt32().Should().Be(2 * HourPrice);
        item.GetProperty("isActive").GetBoolean().Should().BeTrue();
        item.GetProperty("timeLabel").GetString().Should().NotBeNullOrEmpty();
        NoDomWords(item.GetRawText(), "«Мои брони» говорят словом «бронь»");

        var mineUnconfirmed = await MyAsync(unconfirmed.Token);
        mineUnconfirmed.Select(i => i.GetProperty("orderUrl").GetString()).Should().BeEquivalentTo([$"/s/{signed}"],
            "номер без подтверждения брони не открывает: только бронь, сделанная под этим аккаунтом (SUBJECT-PHONE-GATE)");
        mineUnconfirmed.Select(i => i.GetProperty("orderUrl").GetString()).Should().NotContain([$"/s/{anonUnconfirmed}", $"/s/{stranger}"]);
    }

    [Fact, TestCase("CY42-48")]
    public async Task MyBookings_ActiveFirstByStartAscending_ThenOthersByStartDescending_AndNoStaysBookings()
    {
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c);
        var c2 = await CreateBathAsync();
        var res2 = await AddResourceAsync(c2);
        var user = await RegisterAsync();
        await MarkPhoneVerifiedAsync(user.Phone, user.UserId);
        var date = InDays(10);
        // лимиты номера: одна неоплаченная бронь в компании и две на платформе, поэтому отменённые создаём первыми и сразу отменяем
        var cancelledEarly = await BookBathAsync(res.Id, date.AddDays(1), 600, 2, phone: user.Phone);
        (await AnonymousClient().PostJsonAsync($"/api/baths/service-orders/public/{cancelledEarly}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelledLate = await BookBathAsync(res.Id, date.AddDays(1), 1080, 2, phone: user.Phone);
        (await AnonymousClient().PostJsonAsync($"/api/baths/service-orders/public/{cancelledLate}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        var late = await BookBathAsync(res.Id, date, 1080, 2, phone: user.Phone);
        var early = await BookBathAsync(res2.Id, date, 600, 2, phone: user.Phone);

        var r = await AuthedClient(user.Token).GetAsync("/api/baths/service-orders/my");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var urls = (await J(r)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("orderUrl").GetString()).ToList();
        urls.Should().Equal($"/s/{early}", $"/s/{late}", $"/s/{cancelledLate}", $"/s/{cancelledEarly}");

        // у «Домов» свой список: этот пользователь не получает чужих списков и наоборот
        var staysMy = await AuthedClient(user.Token).GetAsync("/api/stays/bookings/my");
        staysMy.StatusCode.Should().Be(HttpStatusCode.OK);
        (await staysMy.Content.ReadAsStringAsync()).Should().NotContain(early);
    }

    // ── CY42-49: изоляция видов на публичных маршрутах ───────────────────────────

    [Fact, TestCase("CY42-49")]
    public async Task BathOrder_IsNotReachableByStaysRoutes_AndStaysResourceNotByBathRoutes()
    {
        var c = await CreateBathAsync();
        var res = await AddResourceAsync(c);
        var token = await BookBathAsync(res.Id, InDays(11), 720, 2);

        (await AnonymousClient().GetAsync($"/api/stays/service-orders/public/{token}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().PostJsonAsync($"/api/stays/service-orders/public/{token}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync($"/api/stays/public/services/{res.Id}/starts?date={D(InDays(11))}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{c.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync($"/api/baths/service-orders/public/{Guid.NewGuid():N}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // и наоборот: ресурс «Домов» по маршрутам бани — 404
        var stays = await CreateStaysCompanyAsync();
        var staysSvc = await CreateServiceAsync(stays, "Баня дома");
        (await AnonymousClient().GetAsync($"/api/baths/public/services/{staysSvc.Id}/starts?date={D(InDays(11))}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BathQuoteAsync(staysSvc.Id, InDays(11), 720, 2)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostBathOrderAsync(staysSvc.Id, InDays(11), 720, 2)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        CatalogIds(await CatalogAsync("?pageSize=50")).Should().NotContain(staysSvc.Id, "услуги «Домов» в каталоге бань не показываются");
    }
}
