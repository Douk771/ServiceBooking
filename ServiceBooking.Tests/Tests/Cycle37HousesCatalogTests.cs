using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 37, «Вызов 2»: дома, цены, реестр, заверение и публикация, каталог и публичные страницы (US-37-10, 11, 25, 26, решение заказчика ЮР-2).
/// Написано по SPEC_CYCLE37_STAYS_HOUSES.md, ARCHITECTURE_CYCLE37.md §37.0a и API_CONTRACT_CYCLE37.md §37.22, §37.28.
/// </summary>
public class Cycle37HousesCatalogTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    private static string Hp(StaysCtx c, Guid houseId) => $"/api/stays/companies/{c.Id}/houses/{houseId}";

    // ── карточка дома ───────────────────────────────────────────────────────────

    [Fact, TestCase("CY37-80")]
    public async Task HouseCard_Validation_SlugUniqueInCompany_AndNewHouseIsDraftWithoutPrice()
    {
        var company = await CreateStaysCompanyAsync();
        var c = AuthedClient(company.OwnerToken);
        var url = $"/api/stays/companies/{company.Id}/houses";

        var noName = await c.PostJsonAsync(url, new HouseCreateInput("  ", 2));
        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noName.Content.ReadAsStringAsync()).Should().Be("Укажите название дома");
        foreach (var capacity in new[] { 0, -1, 51 })
        {
            var r = await c.PostJsonAsync(url, new HouseCreateInput("Дом", capacity));
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"вместимость {capacity}");
            (await r.Content.ReadAsStringAsync()).Should().Be("Вместимость — от 1 до 50");
        }
        (await c.PostJsonAsync(url, new HouseCreateInput(new string('н', 101), 2))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var created = await c.PostJsonAsync(url, new HouseCreateInput("Дом у подъёмника", 6));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var house = (await created.Content.ReadJsonAsync<HouseManageDto>())!;
        house.IsPublished.Should().BeFalse("новый дом не опубликован");
        house.IsArchived.Should().BeFalse();
        house.PriceMode.Should().Be(HousePriceMode.Constant);
        house.ConstantPriceRub.Should().BeNull();
        house.PublishProblems.Should().Contain("NoPrice");
        house.Slug.Should().MatchRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$");
        house.PublicUrl.Should().Be($"https://dom.ezbook.ru/{company.Slug}/{house.Slug}");

        var second = (await (await c.PostJsonAsync(url, new HouseCreateInput("Дом у подъёмника", 6))).Content.ReadJsonAsync<HouseManageDto>())!;
        second.Slug.Should().NotBe(house.Slug, "адрес дома уникален в компании");

        // setup: чужой slug -> 409, плохой формат -> 400, вместимость и доп. места
        var taken = await c.PutJsonAsync(Hp(company, second.Id) + "/setup", new HouseSetupInput("Дом", house.Slug, 4, false, 0, 0, false, false));
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(taken)).Should().Be("SlugTaken");
        var bad = await c.PutJsonAsync(Hp(company, second.Id) + "/setup", new HouseSetupInput("Дом", "Плохой Адрес", 4, false, 0, 0, false, false));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Be("Адрес дома — латиница, цифры и дефис, 2–50 символов");
        (await c.PutJsonAsync(Hp(company, second.Id) + "/setup", new HouseSetupInput("Дом", "dom-two", 4, true, 11, 0, false, false))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutJsonAsync(Hp(company, second.Id) + "/setup", new HouseSetupInput("Дом", "dom-two", 4, true, 2, -5, false, false))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ok = await c.PutJsonAsync(Hp(company, second.Id) + "/setup", new HouseSetupInput("Дом Два", "dom-two", 4, true, 2, 500, true, true));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var saved = (await ok.Content.ReadJsonAsync<HouseManageDto>())!;
        saved.ExtraBeds.Should().Be(new ExtraBedsDto(true, 2, 500));
        saved.DogsForbidden.Should().BeTrue();
        saved.HasCot.Should().BeTrue();

        // содержимое: удобства только из справочника, длины, ссылки карт
        var content = Hp(company, second.Id) + "/content";
        (await c.PutJsonAsync(content, new HouseContentInput("описание", ["Wifi", "Unicorn"], null, null, null, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutJsonAsync(content, new HouseContentInput(new string('о', 4001), [], null, null, null, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutJsonAsync(content, new HouseContentInput(null, [], new string('а', 501), null, null, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutJsonAsync(content, new HouseContentInput(null, [], null, null, null, new string('к', 2001)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutJsonAsync(content, new HouseContentInput(null, [], null, "javascript:alert(1)", null, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var amenities = await c.PutJsonAsync(content, new HouseContentInput("Хороший дом", ["Wifi", "Kitchen", "Parking"], "Шерегеш, Лесная 5", null, null, "Код ключницы 1234"));
        amenities.StatusCode.Should().Be(HttpStatusCode.OK, await amenities.Content.ReadAsStringAsync());
        (await amenities.Content.ReadJsonAsync<HouseManageDto>())!.Amenities.Should().BeEquivalentTo([HouseAmenity.Wifi, HouseAmenity.Kitchen, HouseAmenity.Parking]);

        // порядок домов
        var list = (await J(await c.GetAsync(url))).EnumerateArray().Select(h => h.GetProperty("id").GetGuid()).ToList();
        list.Reverse();
        var reordered = await c.PutJsonAsync(url + "/order", new IdsOrderInput(list));
        reordered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await J(reordered)).EnumerateArray().Select(h => h.GetProperty("id").GetGuid()).Should().Equal(list);
        (await c.PutJsonAsync(url + "/order", new IdsOrderInput([list[0]]))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "нужен полный список");
    }

    [Fact, TestCase("CY37-81")]
    public async Task HousePhotos_UpToFifteen_FirstIsCover_OrderAndDelete()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company);
        var c = AuthedClient(company.OwnerToken);
        var ids = new List<Guid>();
        for (var i = 0; i < 15; i++)
        {
            var r = await c.PostAsync(Hp(company, house.Id) + "/photos", FileContent(TestImages.SolidJpeg(40 + i, 40), "image/jpeg", $"p{i}.jpg"));
            r.StatusCode.Should().Be(HttpStatusCode.Created, $"фото {i + 1}: " + await r.Content.ReadAsStringAsync());
            ids.Add((await r.Content.ReadJsonAsync<HousePhotoDto>())!.Id);
        }
        var sixteenth = await c.PostAsync(Hp(company, house.Id) + "/photos", FileContent(TestImages.SolidJpeg(99, 40), "image/jpeg", "p.jpg"));
        sixteenth.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(sixteenth)).Should().Be("PhotoLimitReached");
        (await J(sixteenth)).GetProperty("message").GetString().Should().Be("У дома может быть не больше 15 фото");

        var notImage = await c.PostAsync(Hp(company, house.Id) + "/photos", FileContent("<html>x</html>"u8.ToArray(), "image/jpeg", "p.jpg"));
        notImage.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);

        // обложка — первое фото; смена порядка меняет обложку и публичную страницу
        var reversed = Enumerable.Reverse(ids).ToList();
        var order = await c.PutJsonAsync(Hp(company, house.Id) + "/photos/order", new IdsOrderInput(reversed));
        order.StatusCode.Should().Be(HttpStatusCode.OK, await order.Content.ReadAsStringAsync());
        var pub = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}"));
        pub.GetProperty("photos").GetArrayLength().Should().Be(15);
        pub.GetProperty("photos")[0].GetProperty("url").GetString().Should().NotBeNullOrEmpty();
        (await c.DeleteAsync(Hp(company, house.Id) + $"/photos/{ids[0]}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetHouseAsync(company, house.Id)).Photos.Should().HaveCount(14);
        (await c.PostAsync(Hp(company, house.Id) + "/photos", FileContent(TestImages.SolidJpeg(120, 40), "image/jpeg", "p.jpg"))).StatusCode.Should().Be(HttpStatusCode.Created);

        // горничная фото не меняет
        var housekeeper = await AddStaffAsync(company, "Housekeeper");
        (await AuthedClient(housekeeper.Token).DeleteAsync(Hp(company, house.Id) + $"/photos/{ids[1]}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // управляющий — может (описание, фото)
        var manager = await AddStaffAsync(company, "Manager");
        (await AuthedClient(manager.Token).PostAsync(Hp(company, house.Id) + "/photos", FileContent(TestImages.SolidJpeg(121, 40), "image/jpeg", "p.jpg")))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "уже 15 фото — лимит работает и для управляющего");
    }

    // ── цены ─────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY37-82")]
    public async Task PricePeriods_OverlapIs409WithConflictingPeriod_SingleDayInsideLongOneIsAllowed_ValidationAndCalendarOfUncovered()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 4000, publish: false);
        var c = AuthedClient(company.OwnerToken);
        var hp = Hp(company, house.Id);
        var from = InDays(30);

        (await c.PutJsonAsync(hp + "/pricing", new HousePricingInput(HousePriceMode.ByDates, null))).StatusCode.Should().Be(HttpStatusCode.OK, "дом не опубликован — режим можно переключить без периодов");
        var big = await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(from, from.AddDays(19), 5000));
        big.StatusCode.Should().Be(HttpStatusCode.Created);
        var bigDto = (await big.Content.ReadJsonAsync<PricePeriodDto>())!;

        var overlap = await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(from.AddDays(15), from.AddDays(25), 5500));
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(overlap);
        body.GetProperty("code").GetString().Should().Be("PricePeriodOverlap");
        body.GetProperty("message").GetString().Should().StartWith("Период пересекается с ").And.EndWith("₽)");
        body.GetProperty("conflictingPeriod").GetProperty("id").GetGuid().Should().Be(bigDto.Id, "указано, с каким периодом конфликт");

        // однодневный период внутри длинного — допустим; второй на ту же дату — нет; сосед встык — допустим
        (await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(from.AddDays(3), from.AddDays(3), 9000))).StatusCode.Should().Be(HttpStatusCode.Created);
        var dup = await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(from.AddDays(3), from.AddDays(3), 9500));
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(dup)).Should().Be("PricePeriodOverlap");
        (await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(from.AddDays(20), from.AddDays(25), 6000))).StatusCode.Should().Be(HttpStatusCode.Created);

        // валидация
        var reversed = await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(from.AddDays(40), from.AddDays(39), 5000));
        reversed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reversed.Content.ReadAsStringAsync()).Should().Be("Дата окончания не может быть раньше начала");
        var tooLong = await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(from.AddDays(100), from.AddDays(100 + 732), 5000));
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).Should().Be("Период — не длиннее двух лет");
        foreach (var price in new[] { 0, -1, 1_000_001 })
        {
            var r = await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(from.AddDays(60), from.AddDays(61), price));
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"цена {price}");
            (await r.Content.ReadAsStringAsync()).Should().Be("Цена — от 1 до 1 000 000 ₽");
        }

        // календарь цен: даты без цены на горизонте
        var manage = await GetHouseAsync(company, house.Id);
        manage.UncoveredDates.Should().NotBeEmpty("есть будущие даты без цены");
        manage.UncoveredDates.Should().NotContain(r => r.StartDate <= from.AddDays(5) && from.AddDays(5) <= r.EndDate);
        manage.UncoveredDates.Should().Contain(r => r.StartDate <= from.AddDays(40) && from.AddDays(40) <= r.EndDate);

        // изменение и удаление периода; прошлые по умолчанию скрыты
        (await c.PutJsonAsync($"{hp}/price-periods/{bigDto.Id}", new PricePeriodInput(from, from.AddDays(19), 5200))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.DeleteAsync($"{hp}/price-periods/{bigDto.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.DeleteAsync($"{hp}/price-periods/{bigDto.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(InDays(-20), InDays(-10), 1000));
        var current = await J(await c.GetAsync(hp + "/price-periods"));
        current.EnumerateArray().Should().OnlyContain(p => p.GetProperty("endDate").GetString()!.CompareTo(D(InDays(0))) >= 0, "прошедшие периоды по умолчанию не показываются");
        (await J(await c.GetAsync(hp + "/price-periods?includePast=true"))).GetArrayLength().Should().BeGreaterThan(current.GetArrayLength());
    }

    // ── реестр, заверение и публикация (ЮР-2) ─────────────────────────────────────

    [Fact, TestCase("CY37-83")]
    public async Task Publish_WithoutRegistryNumber_UnderAttestation_Succeeds_ForEveryObjectKind()
    {
        var company = await CreateStaysCompanyAsync();
        foreach (var kind in Enum.GetValues<HouseObjectKind>())
        {
            var house = await CreateHouseAsync(company, publish: false);
            var c = AuthedClient(company.OwnerToken);
            var registry = await c.PutJsonAsync(Hp(company, house.Id) + "/registry", new HouseRegistryInput(kind, null, null, null));
            registry.StatusCode.Should().Be(HttpStatusCode.OK, await registry.Content.ReadAsStringAsync());
            var draft = (await registry.Content.ReadJsonAsync<HouseManageDto>())!;
            draft.RegistryNotice.Text.Should().NotBeNullOrWhiteSpace();
            draft.RegistryNotice.Version.Should().NotBeNullOrWhiteSpace();

            var published = await c.PostJsonAsync(Hp(company, house.Id) + "/publish", new HousePublishInput(new AttestationInput(true, draft.RegistryNotice.Version)));
            published.StatusCode.Should().Be(HttpStatusCode.OK, $"{kind}: дом без номера реестра публикуется под заверением владельца: " + await published.Content.ReadAsStringAsync());
            var dto = (await published.Content.ReadJsonAsync<HouseManageDto>())!;
            dto.IsPublished.Should().BeTrue();
            dto.Registry.RegistryNumber.Should().BeNull();
            dto.Registry.LastAttestation.Should().NotBeNull("заверение хранится с датой, автором и версией");
            dto.Registry.LastAttestation!.ObjectKind.Should().Be(kind);
            dto.Registry.LastAttestation.AttestedByName.Should().NotBeNullOrWhiteSpace();
            dto.Registry.LastAttestation.AttestedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));

            var attestation = await WithDbAsync(db => db.HouseRegistryAttestations.AsNoTracking().SingleAsync(a => a.HouseId == house.Id));
            attestation.NoticeVersion.Should().Be(draft.RegistryNotice.Version);
            attestation.AttestedByUserId.Should().Be(company.Owner.UserId);
            attestation.ObjectKind.Should().Be(kind);

            var page = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}"));
            page.GetProperty("registry").GetProperty("objectKind").GetString().Should().Be(kind.ToString());
            page.GetProperty("registry").GetProperty("objectKindLabel").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact, TestCase("CY37-84")]
    public async Task Publish_WithoutAttestation_Is409AttestationRequired_AndNothingIsPublished()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, publish: false);
        var c = AuthedClient(company.OwnerToken);
        var url = Hp(company, house.Id) + "/publish";
        var version = house.House.RegistryNotice.Version;

        var cases = new (string Label, HousePublishInput Input)[]
        {
            ("без заверения", new HousePublishInput(null)),
            ("accepted=false", new HousePublishInput(new AttestationInput(false, version))),
            ("версия текста не та", new HousePublishInput(new AttestationInput(true, "old-version"))),
            ("без версии текста", new HousePublishInput(new AttestationInput(true, null))),
        };
        foreach (var (label, input) in cases)
        {
            var r = await c.PostJsonAsync(url, input);
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, label);
            var body = await J(r);
            body.GetProperty("code").GetString().Should().Be("AttestationRequired", label);
            body.GetProperty("message").GetString().Should().Be("Подтвердите сведения о доме");
        }
        (await GetHouseAsync(company, house.Id)).IsPublished.Should().BeFalse();
        (await WithDbAsync(db => db.HouseRegistryAttestations.CountAsync(a => a.HouseId == house.Id))).Should().Be(0);

        // прочие причины отказа идут в порядке контракта
        var fresh = await c.PostJsonAsync($"/api/stays/companies/{company.Id}/houses", new HouseCreateInput("Без цены", 2));
        var h2 = (await fresh.Content.ReadJsonAsync<HouseManageDto>())!;
        var noPrice = await c.PostJsonAsync(Hp(company, h2.Id) + "/publish", new HousePublishInput(new AttestationInput(true, h2.RegistryNotice.Version)));
        noPrice.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(noPrice)).Should().Be("NoPrice");
        await c.PutJsonAsync(Hp(company, h2.Id) + "/pricing", new HousePricingInput(HousePriceMode.Constant, 3000));
        var noKind = await c.PostJsonAsync(Hp(company, h2.Id) + "/publish", new HousePublishInput(new AttestationInput(true, h2.RegistryNotice.Version)));
        (await Code(noKind)).Should().Be("ObjectKindRequired");
        await c.PostJsonAsync(Hp(company, h2.Id) + "/archive", new { });
        var archived = await c.PostJsonAsync(Hp(company, h2.Id) + "/publish", new HousePublishInput(new AttestationInput(true, h2.RegistryNotice.Version)));
        (await Code(archived)).Should().Be("HouseArchived");
    }

    [Fact, TestCase("CY37-85")]
    public async Task ChangingRegistryOfPublishedHouse_RequiresNewAttestation_AndAddsOne()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company);
        var c = AuthedClient(company.OwnerToken);
        var url = Hp(company, house.Id) + "/registry";
        var version = house.House.RegistryNotice.Version;

        var without = await c.PutJsonAsync(url, new HouseRegistryInput(HouseObjectKind.GuestHouse, "ГД-12345", "https://example.test/r/1", null));
        without.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(without)).Should().Be("AttestationRequired");
        (await GetHouseAsync(company, house.Id)).Registry.RegistryNumber.Should().BeNull("отказ ничего не меняет");

        var withAtt = await c.PutJsonAsync(url, new HouseRegistryInput(HouseObjectKind.GuestHouse, "ГД-12345", "https://example.test/r/1", new AttestationInput(true, version)));
        withAtt.StatusCode.Should().Be(HttpStatusCode.OK, await withAtt.Content.ReadAsStringAsync());
        var dto = (await withAtt.Content.ReadJsonAsync<HouseManageDto>())!;
        dto.Registry.RegistryNumber.Should().Be("ГД-12345");
        dto.Registry.LastAttestation!.RegistryNumber.Should().Be("ГД-12345");
        (await WithDbAsync(db => db.HouseRegistryAttestations.CountAsync(a => a.HouseId == house.Id))).Should().Be(2, "прежнее заверение не удаляется — это доказательство");

        // номер виден гостю на карточке, в каталоге и на странице дома
        InvalidateCatalog();
        var catalog = await J(await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50"));
        catalog.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("houseId").GetGuid() == house.Id).GetProperty("registryNumber").GetString().Should().Be("ГД-12345");
        var page = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}"));
        page.GetProperty("registry").GetProperty("registryNumber").GetString().Should().Be("ГД-12345");
        page.GetProperty("registry").GetProperty("registryUrl").GetString().Should().Be("https://example.test/r/1");

        // формат номера и ссылки
        foreach (var number in new[] { "abc", new string('1', 33), "№ 12 345", "12345;DROP" })
        {
            var r = await c.PutJsonAsync(url, new HouseRegistryInput(HouseObjectKind.GuestHouse, number, null, new AttestationInput(true, version)));
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, number);
        }
        var http = await c.PutJsonAsync(url, new HouseRegistryInput(HouseObjectKind.GuestHouse, "ГД-12345", "http://example.test/r", new AttestationInput(true, version)));
        http.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await http.Content.ReadAsStringAsync()).Should().Be("Ссылка на запись в реестре должна начинаться с https://");
        (await c.PutJsonAsync(url, new HouseRegistryInput(null, null, null, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY37-86")]
    public async Task Unpublish_Archive_Delete_RulesAndBookingsSurvive()
    {
        var company = await CreateStaysCompanyAsync();
        var withBooking = await CreateHouseAsync(company, price: 2000);
        var empty = await CreateHouseAsync(company, price: 2000, publish: false);
        var c = AuthedClient(company.OwnerToken);
        var booked = await BookOkAsync(withBooking.Id, InDays(10), InDays(12));

        // дом с бронью не удаляется
        var del = await c.DeleteAsync(Hp(company, withBooking.Id));
        del.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(del)).Should().Be("HouseHasBookings");
        (await J(del)).GetProperty("message").GetString().Should().Be("У дома есть брони — его можно только архивировать");

        // неопубликованный не виден гостям, но виден в шахматке
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{empty.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var board = await J(await c.GetAsync($"/api/stays/companies/{company.Id}/board"));
        board.GetProperty("houses").EnumerateArray().Select(h => h.GetProperty("id").GetGuid()).Should().Contain(empty.Id);
        (await QuoteStatusAsync(empty.Id)).Should().Be(HttpStatusCode.NotFound, "гости не считают неопубликованный дом");

        // архив: пропадает из публичного, из счёта тарифа; брони сохраняются
        var archived = await c.PostJsonAsync(Hp(company, withBooking.Id) + "/archive", new { });
        archived.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await archived.Content.ReadJsonAsync<HouseManageDto>())!;
        dto.IsArchived.Should().BeTrue();
        dto.IsPublished.Should().BeFalse();
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{withBooking.Slug}")).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
        var pubPage = await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{withBooking.Slug}");
        if (pubPage.StatusCode == HttpStatusCode.OK) (await J(pubPage)).GetProperty("available").GetBoolean().Should().BeFalse();
        (await GetPublicBookingAsync(booked.Token)).Status.Should().Be(StayBookingStatus.Held, "брони архивного дома живут");
        (await GetCompanyAsync(company)).Plan.HousesPublished.Should().Be(0, "архивный дом не считается в тарифе");
        (await QuoteStatusAsync(withBooking.Id)).Should().Be(HttpStatusCode.NotFound);
        var list = await J(await c.GetAsync($"/api/stays/companies/{company.Id}/houses"));
        list.EnumerateArray().Single(h => h.GetProperty("id").GetGuid() == withBooking.Id).GetProperty("isArchived").GetBoolean().Should().BeTrue();

        // дом без броней и без публикаций удаляется
        (await c.DeleteAsync(Hp(company, empty.Id))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync(Hp(company, empty.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<HttpStatusCode> QuoteStatusAsync(Guid houseId) =>
        (await AnonymousClient().PostJsonAsync($"/api/stays/public/houses/{houseId}/quote", new StayQuoteInput(InDays(10), InDays(11), 2, 0, 0, false))).StatusCode;

    // ── каталог и публичные страницы ──────────────────────────────────────────────

    [Fact, TestCase("CY37-87")]
    public async Task Catalog_FiltersByDatesGuestsPrice_SortsByPrice_AndShowsTotals()
    {
        var company = await CreateStaysCompanyAsync();
        var cheap = await CreateHouseAsync(company, name: Unique("Дешёвый "), capacity: 2, price: 2000);
        var mid = await CreateHouseAsync(company, name: Unique("Средний "), capacity: 4, price: 3000, extraBedsMax: 2, extraBedPrice: 500);
        var rich = await CreateHouseAsync(company, name: Unique("Дорогой "), capacity: 8, price: 7000);
        var ci = InDays(40);
        var co = InDays(43);
        // занять «средний» на часть периода — он не должен попасть при поиске по датам
        await BookOkAsync(mid.Id, ci.AddDays(1), ci.AddDays(2));
        InvalidateCatalog();

        async Task<List<JsonElement>> Items(string query)
        {
            var r = await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50" + query);
            r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            var all = (await J(r)).GetProperty("items").EnumerateArray().ToList();
            var ours = new[] { cheap.Id, mid.Id, rich.Id };
            return all.Where(i => ours.Contains(i.GetProperty("houseId").GetGuid())).ToList();
        }

        // без дат: все три, по цене за ночь по возрастанию, «от …»
        var noDates = await Items("");
        noDates.Select(i => i.GetProperty("houseId").GetGuid()).Should().Equal(cheap.Id, mid.Id, rich.Id);
        noDates.Select(i => i.GetProperty("priceFromRub").GetInt32()).Should().Equal(2000, 3000, 7000);
        noDates.Should().OnlyContain(i => i.GetProperty("totalRub").ValueKind == JsonValueKind.Null);
        noDates[0].GetProperty("url").GetString().Should().Be($"/{company.Slug}/{cheap.Slug}");
        noDates[1].GetProperty("extraBedsMax").GetInt32().Should().Be(2);

        // с датами: «средний» занят на одну из ночей — его нет; у остальных итог и средняя
        var withDates = await Items($"&checkIn={D(ci)}&checkOut={D(co)}&guests=2");
        withDates.Select(i => i.GetProperty("houseId").GetGuid()).Should().Equal(cheap.Id, rich.Id);
        withDates[0].GetProperty("totalRub").GetInt32().Should().Be(6000);
        withDates[0].GetProperty("averageNightRub").GetInt32().Should().Be(2000);
        withDates[0].GetProperty("nights").GetInt32().Should().Be(3);

        // гости: 5 помещаются только в «средний» (4 + 2 доп. места) и «дорогой»; в «дешёвый» (2) — нет
        var five = await Items("&guests=5");
        five.Select(i => i.GetProperty("houseId").GetGuid()).Should().Equal(mid.Id, rich.Id);
        var seven = await Items("&guests=7");
        seven.Select(i => i.GetProperty("houseId").GetGuid()).Should().Equal(rich.Id);
        // цена: «до X ₽ за ночь»
        (await Items("&maxPricePerNight=3000")).Select(i => i.GetProperty("houseId").GetGuid()).Should().Equal(cheap.Id, mid.Id);
        (await Items($"&maxPricePerNight=2500&checkIn={D(ci)}&checkOut={D(co)}")).Select(i => i.GetProperty("houseId").GetGuid()).Should().Equal(cheap.Id);

        // пагинация
        var page1 = await J(await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=1&page=1"));
        page1.GetProperty("items").GetArrayLength().Should().Be(1);
        page1.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        page1.GetProperty("pageSize").GetInt32().Should().Be(1);
    }

    [Fact, TestCase("CY37-88")]
    public async Task Catalog_InvalidQueries_PlainText400()
    {
        async Task Expect(string query, string text)
        {
            var r = await AnonymousClient().GetAsync("/api/stays/public/catalog" + query);
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, query);
            (await r.Content.ReadAsStringAsync()).Should().Be(text, query);
        }
        await Expect($"?checkIn={D(InDays(10))}", "Укажите обе даты: заезд и выезд");
        await Expect($"?checkOut={D(InDays(10))}", "Укажите обе даты: заезд и выезд");
        await Expect($"?checkIn={D(InDays(10))}&checkOut={D(InDays(10))}", "Дата выезда должна быть позже даты заезда");
        await Expect($"?checkIn={D(InDays(12))}&checkOut={D(InDays(10))}", "Дата выезда должна быть позже даты заезда");
        await Expect("?checkIn=31.12.2026&checkOut=02.01.2027", "Неверный формат даты");
        await Expect("?guests=0", "Число гостей — от 1 до 60");
        await Expect("?guests=61", "Число гостей — от 1 до 60");
        (await AnonymousClient().GetAsync("/api/stays/public/catalog?maxPricePerNight=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // даты, не проходящие правила, — не ошибка
        var tooLong = await AnonymousClient().GetAsync($"/api/stays/public/catalog?checkIn={D(InDays(10))}&checkOut={D(InDays(300))}");
        tooLong.StatusCode.Should().Be(HttpStatusCode.OK);
        var past = await AnonymousClient().GetAsync($"/api/stays/public/catalog?checkIn={D(InDays(-10))}&checkOut={D(InDays(-8))}");
        past.StatusCode.Should().Be(HttpStatusCode.OK);
        (await J(past)).GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact, TestCase("CY37-89")]
    public async Task Catalog_HidesDraftsArchivedAndUnlistedCompanies_ButDirectLinksStillWork()
    {
        var company = await CreateStaysCompanyAsync(settings: s => s with { ShowInCatalog = false });
        var house = await CreateHouseAsync(company, price: 2000);
        var draft = await CreateHouseAsync(company, price: 2000, publish: false);
        InvalidateCatalog();

        var items = (await J(await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50"))).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("houseId").GetGuid()).ToList();
        items.Should().NotContain([house.Id, draft.Id], "показ в каталоге выключен");

        // страница компании и дома по прямой ссылке работают, неопубликованного дома в списке нет
        var page = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}"));
        page.GetProperty("available").GetBoolean().Should().BeTrue();
        page.GetProperty("houses").EnumerateArray().Select(h => h.GetProperty("houseId").GetGuid()).Should().Equal(house.Id);
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{draft.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync("/api/stays/public/companies/no-such-company-xyz")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // бронь по прямой ссылке при этом возможна
        (await BookOkAsync(house.Id, InDays(10), InDays(11))).Booking.Status.Should().Be(StayBookingStatus.Held);

        // включили показ — дом в каталоге
        await PutSettingsAsync(company, (await GetCompanyAsync(company)).Settings! with { ShowInCatalog = true });
        InvalidateCatalog();
        (await J(await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50"))).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("houseId").GetGuid()).Should().Contain(house.Id);
    }

    [Fact, TestCase("CY37-90")]
    public async Task CompanyPage_WithDates_ShowsUnavailableHousesWithFlag_NotHiding_AndProviderPublicFieldsFollowStatus()
    {
        var company = await CreateStaysCompanyAsync();
        var free = await CreateHouseAsync(company, name: Unique("Свободный "), price: 2000);
        var taken = await CreateHouseAsync(company, name: Unique("Занятый "), price: 2500);
        var ci = InDays(20);
        var co = InDays(22);
        await BookOkAsync(taken.Id, ci, co);

        var page = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}?checkIn={D(ci)}&checkOut={D(co)}&guests=2"));
        var houses = page.GetProperty("houses").EnumerateArray().ToList();
        houses.Should().HaveCount(2, "недоступный на даты дом не скрывается");
        houses.Single(h => h.GetProperty("houseId").GetGuid() == free.Id).GetProperty("availableForDates").GetBoolean().Should().BeTrue();
        houses.Single(h => h.GetProperty("houseId").GetGuid() == taken.Id).GetProperty("availableForDates").GetBoolean().Should().BeFalse();
        page.GetProperty("acceptingBookings").GetBoolean().Should().BeTrue();

        // самозанятый: публично статус и ИНН, без ФИО и адреса для претензий
        var provider = page.GetProperty("provider");
        provider.GetProperty("status").GetString().Should().Be("SelfEmployed");
        provider.GetProperty("inn").GetString().Should().Be(ValidPersonInn);
        provider.GetProperty("name").ValueKind.Should().Be(JsonValueKind.Null);
        provider.GetProperty("claimsAddress").ValueKind.Should().Be(JsonValueKind.Null);
        provider.GetProperty("ogrn").ValueKind.Should().Be(JsonValueKind.Null);
        provider.GetProperty("statusLabel").GetString().Should().Be("Плательщик налога на профессиональный доход");

        // ИП и организация: публично и наименование, ОГРН, адрес для претензий
        var ie = await CreateStaysCompanyAsync();
        var ieHouse = await CreateHouseAsync(ie);
        await PutProviderAsync(ie, StayProviderStatus.IndividualEntrepreneur, "ИП Иванов", ValidPersonInn, "304500116000157", "г. Новокузнецк, ул. Мира, 1");
        var iePage = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{ie.Slug}"));
        iePage.GetProperty("provider").GetProperty("name").GetString().Should().Be("ИП Иванов");
        iePage.GetProperty("provider").GetProperty("ogrn").GetString().Should().Be("304500116000157");
        iePage.GetProperty("provider").GetProperty("claimsAddress").GetString().Should().NotBeNullOrEmpty();
        _ = ieHouse;

        // валидация исполнителя
        var c = AuthedClient(ie.OwnerToken);
        var url = $"/api/stays/companies/{ie.Id}/provider";
        (await c.PutJsonAsync(url, new ProviderInput(StayProviderStatus.IndividualEntrepreneur, "ИП", "123", "304500116000157", "адрес"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var noOgrnip = await c.PutJsonAsync(url, new ProviderInput(StayProviderStatus.IndividualEntrepreneur, "ИП", ValidPersonInn, null, "адрес"));
        noOgrnip.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noOgrnip.Content.ReadAsStringAsync()).Should().Be("Укажите ОГРНИП");
        var ogrnForSelfEmployed = await c.PutJsonAsync(url, new ProviderInput(StayProviderStatus.SelfEmployed, "Иванов", ValidPersonInn, "304500116000157", "адрес"));
        ogrnForSelfEmployed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ogrnForSelfEmployed.Content.ReadAsStringAsync()).Should().Be("ОГРН указывается только для организации и ИП");
        (await c.PutJsonAsync(url, new ProviderInput(null, "Иванов", ValidPersonInn, null, "адрес"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PutJsonAsync(url, new ProviderInput(StayProviderStatus.SelfEmployed, "Иванов", ValidPersonInn, null, ""))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY37-91")]
    public async Task HousePage_ShowsRulesTemperedByCompanySettings_AndNoPaymentDetails()
    {
        var company = await CreateStaysCompanyAsync(settings: s => s with { MinNights = 2, MaxNights = 14, PrepayPercent = 40, HoldMinutes = 45, CheckInTime = "15:00", CheckOutTime = "11:00" });
        var house = await CreateHouseAsync(company, capacity: 4, price: 3500, extraBedsMax: 2, extraBedPrice: 800, dogsForbidden: true, hasCot: true);
        var page = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/{house.Slug}"));

        var rules = page.GetProperty("rules");
        rules.GetProperty("checkInTime").GetString().Should().Be("15:00");
        rules.GetProperty("checkOutTime").GetString().Should().Be("11:00");
        rules.GetProperty("minNights").GetInt32().Should().Be(2);
        rules.GetProperty("maxNights").GetInt32().Should().Be(14);
        rules.GetProperty("prepayPercent").GetInt32().Should().Be(40);
        rules.GetProperty("holdMinutes").GetInt32().Should().Be(45);
        rules.GetProperty("cancellationSummary").GetString().Should().NotBeNullOrWhiteSpace();
        page.GetProperty("dogsForbidden").GetBoolean().Should().BeTrue();
        page.GetProperty("hasCot").GetBoolean().Should().BeTrue();
        page.GetProperty("extraBeds").GetProperty("enabled").GetBoolean().Should().BeTrue();
        page.GetProperty("priceFromRub").GetInt32().Should().Be(3500);
        page.GetProperty("address").GetString().Should().Be("Шерегеш, ул. Лесная, 5");
        page.GetProperty("timeZoneId").GetString().Should().Be("Asia/Novokuznetsk");
        page.GetProperty("company").GetProperty("url").GetString().Should().Be($"/{company.Slug}");
        page.GetProperty("available").GetBoolean().Should().BeTrue();
        page.GetProperty("acceptingBookings").GetBoolean().Should().BeTrue();

        // первое время заезда в форме брони — не раньше времени заезда компании
        var early = await PostBookingAsync(house.Id, Booking(InDays(10), InDays(12), 7000, arrival: "14:30"));
        early.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await early.Content.ReadAsStringAsync()).Should().Be("Время прибытия — от времени заезда до 23:30 с шагом 30 минут");
    }

    [Fact, TestCase("CY37-92")]
    public async Task Settings_Validation_PlainText400_AndOnlyOwnerChangesThem_AndNewBookingsOnly()
    {
        var company = await CreateStaysCompanyAsync();
        var s = (await GetCompanyAsync(company)).Settings!;
        s.CheckInTime.Should().Be("14:00");
        s.CheckOutTime.Should().Be("12:00");
        s.MinNights.Should().Be(1);
        s.MaxNights.Should().Be(30);
        s.HorizonDays.Should().Be(365);
        s.HoldMinutes.Should().Be(30);
        s.PrepayPercent.Should().Be(30);
        s.CancellationPolicy.Should().Be(StayCancellationPolicy.Standard);
        s.AllowGapFill.Should().BeFalse();
        s.AllowSameDayCheckIn.Should().BeTrue();
        s.HousekeeperSeesGuestComment.Should().BeFalse("ЮР-5: комментарий гостя горничной по умолчанию скрыт");
        s.CheckInInfoSendFullText.Should().BeFalse("ЮР-4: по умолчанию в мессенджер уходит ссылка, а не коды доступа");
        s.ArrivalReminderEnabled.Should().BeTrue();
        s.ShowInCatalog.Should().BeTrue();

        async Task Expect(StaysSettingsDto bad, string text)
        {
            var r = await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/settings", bad);
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
            (await r.Content.ReadAsStringAsync()).Should().Be(text);
        }
        await Expect(s with { CheckInTime = "14:10" }, "Время — с шагом 30 минут");
        await Expect(s with { CheckInTime = "12:00", CheckOutTime = "13:00" }, "Время выезда не может быть позже времени заезда");
        await Expect(s with { MinNights = 0 }, "Минимум ночей — от 1 до 30");
        await Expect(s with { MinNights = 31 }, "Минимум ночей — от 1 до 30");
        await Expect(s with { MaxNights = 91 }, "Максимум ночей — от 1 до 90");
        await Expect(s with { MinNights = 10, MaxNights = 5 }, "Минимум не может быть больше максимума");
        await Expect(s with { HorizonDays = 29 }, "Горизонт бронирования — от 30 до 730 дней");
        await Expect(s with { HorizonDays = 731 }, "Горизонт бронирования — от 30 до 730 дней");
        await Expect(s with { HoldMinutes = 9 }, "Время на оплату — от 10 до 180 минут");
        await Expect(s with { HoldMinutes = 181 }, "Время на оплату — от 10 до 180 минут");
        await Expect(s with { PrepayPercent = -1 }, "Предоплата — от 0 до 100 %");
        await Expect(s with { PrepayPercent = 101 }, "Предоплата — от 0 до 100 %");
        await Expect(s with { DogFeeRub = -1 }, "Сумма — от 0 до 100 000 ₽");
        await Expect(s with { CotFeeRub = 100_001 }, "Сумма — от 0 до 100 000 ₽");
        await Expect(s with { CheckInInfoText = new string('т', 2001) }, "Текст к заселению — не длиннее 2000 символов");
        // границы допустимого проходят
        var edge = await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/settings",
            s with { MinNights = 30, MaxNights = 90, HorizonDays = 730, HoldMinutes = 180, PrepayPercent = 100, DogFeeRub = 100_000, CotFeeRub = 0, CheckInTime = "23:30", CheckOutTime = "00:00" });
        edge.StatusCode.Should().Be(HttpStatusCode.OK, await edge.Content.ReadAsStringAsync());

        // реквизиты: длины
        var payments = $"/api/stays/companies/{company.Id}/payment-details";
        var longDetails = await AuthedClient(company.OwnerToken).PutJsonAsync(payments, new PaymentDetailsDto(new string('р', 1001), null));
        longDetails.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await longDetails.Content.ReadAsStringAsync()).Should().Be("Реквизиты — не длиннее 1000 символов");
        var longPurpose = await AuthedClient(company.OwnerToken).PutJsonAsync(payments, new PaymentDetailsDto("ok", new string('н', 201)));
        (await longPurpose.Content.ReadAsStringAsync()).Should().Be("Назначение платежа — не длиннее 200 символов");
        (await AuthedClient(company.OwnerToken).PutJsonAsync(payments, new PaymentDetailsDto(new string('р', 1000), new string('н', 200)))).StatusCode.Should().Be(HttpStatusCode.OK);

        // slug: чужой/зарезервированный/некорректный
        var other = await CreateStaysCompanyAsync();
        var slugUrl = $"/api/stays/companies/{company.Id}/slug";
        foreach (var (slug, code) in new[] { (other.Slug, "SlugTaken"), ("admin", "SlugReserved"), ("Плохо Слово", "SlugInvalid"), ("a", "SlugInvalid") })
        {
            var r = await AuthedClient(company.OwnerToken).PutJsonAsync(slugUrl, new SlugInput(slug));
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, slug);
            (await Code(r)).Should().Be(code, slug);
        }
        var changed = Unique("dom-new-");
        (await AuthedClient(company.OwnerToken).PutJsonAsync(slugUrl, new SlugInput(changed))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "редиректа со старого адреса нет");
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{changed}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
