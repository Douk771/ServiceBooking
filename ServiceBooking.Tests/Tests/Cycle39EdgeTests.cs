using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39, «Вызов 2»: граничные случаи и злоупотребления услугами — позиции (дубли и лимит), подтверждения оплаты (типы, число, чужой файл), снимок зазора, прошлые даты расписания,
/// зарезервированные адреса, фото услуг, неверный ввод. Тесты по SPEC §4.5, §4.6, §5 (US-39-01, 05, 12) и API_CONTRACT_CYCLE39.md.
/// </summary>
public class Cycle39EdgeTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private async Task<(StaysCtx Company, SvcCtx Svc)> SceneAsync(int? prepay = null, int buffer = 30)
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        return (company, await CreateServiceAsync(company, prepay: prepay, buffer: buffer));
    }

    // ── позиции ──────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-170")]
    public async Task Items_SameItemTwiceInOneRequest_CannotExceedMaxPerSession()
    {
        var (company, svc) = await SceneAsync();
        var broom = await AddItemAsync(company, svc.Id, "Веник", 300, max: 3);
        var date = InDays(9);
        // две записи одной и той же позиции по 3 (максимум на сеанс — 3): всего 6 > максимума
        var doubled = new[] { new ItemSelectionInput(broom.Id, 3), new ItemSelectionInput(broom.Id, 3) };
        var quote = await QuoteServiceAsync(svc.Id, date, 720, 2, doubled);
        var r = await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, 4000 + 1800, UniquePhone(), items: doubled));
        var created = r.StatusCode == HttpStatusCode.Created;
        var stored = created ? (await WithDbAsync(db => db.StayServiceSessions.AsNoTracking().SingleAsync(s => s.ServiceId == svc.Id))).ItemsAmountRub : 0;
        (quote.Ok && quote.ItemsAmountRub > 900).Should().BeFalse("«максимум на сеанс» — лимит на позицию в целом, а не на каждую запись списка: квота = " + quote.ItemsAmountRub + ", создан = " + created + ", сумма позиций в сеансе = " + stored);
        created.Should().BeFalse();
    }

    [Fact, TestCase("CY39-171")]
    public async Task Items_ZeroQuantity_IsIgnored_NegativeAndHuge_400()
    {
        var (company, svc) = await SceneAsync();
        var broom = await AddItemAsync(company, svc.Id, "Веник", 300, 5);
        var date = InDays(9);
        (await QuoteServiceAsync(svc.Id, date, 720, 2, [new ItemSelectionInput(broom.Id, 0)])).ItemsAmountRub.Should().Be(0, "нулевое количество — позиция не выбрана");
        var neg = await AnonymousClient().PostJsonAsync($"/api/stays/public/services/{svc.Id}/quote", new PublicServiceQuoteInput(date, 720, 2, [new ItemSelectionInput(broom.Id, -1)], null, null, null));
        neg.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var big = await AnonymousClient().PostJsonAsync($"/api/stays/public/services/{svc.Id}/quote", new PublicServiceQuoteInput(date, 720, 2, [new ItemSelectionInput(broom.Id, int.MaxValue)], null, null, null));
        big.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── подтверждения оплаты ─────────────────────────────────────────────────────

    [Fact, TestCase("CY39-172")]
    public async Task Proofs_TypesCountAndForeignFile_AreRefusedLikeForHouses()
    {
        var (company, svc) = await SceneAsync(prepay: 30);
        var a = await OrderOkAsync(svc.Id, InDays(9), 720, 2);
        var b = await OrderOkAsync(svc.Id, InDays(9), 1080, 2);
        var anon = AnonymousClient();
        // не тот тип
        var text = await anon.PostAsync($"/api/stays/service-orders/public/{a.Token}/payment-proofs", FileContent(System.Text.Encoding.UTF8.GetBytes("hello"), "text/plain", "x.txt"));
        text.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // подмена: файл с расширением .jpg, но не картинка
        var fake = await anon.PostAsync($"/api/stays/service-orders/public/{a.Token}/payment-proofs", FileContent(System.Text.Encoding.UTF8.GetBytes("MZ not an image"), "image/jpeg", "x.jpg"));
        fake.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // PDF проходит; до трёх файлов
        (await anon.PostAsync($"/api/stays/service-orders/public/{a.Token}/payment-proofs", FileContent(SamplePdf(), "application/pdf", "p.pdf"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await AttachOrderProofAsync(a.Token)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await anon.PostAsync($"/api/stays/service-orders/public/{a.Token}/payment-proofs", FileContent(TestImages.SolidJpeg(80, 50), "image/jpeg", "c.jpg"))).StatusCode.Should().Be(HttpStatusCode.Created);
        var fourth = await AttachOrderProofAsync(a.Token);
        fourth.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        (await GetOrderAsync(a.Token)).PaymentProofs.Should().HaveCount(3);

        // чужой файл по своей ссылке: id файла заказа A под токеном заказа B
        await AttachOrderProofAsync(b.Token);
        var foreignProof = (await GetOrderAsync(a.Token)).PaymentProofs.First().Id;
        (await anon.GetAsync($"/api/stays/service-orders/public/{b.Token}/payment-proofs/{foreignProof}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anon.GetAsync($"/api/stays/service-orders/public/{a.Token}/payment-proofs/{foreignProof}")).StatusCode.Should().Be(HttpStatusCode.OK);
        // и в кабинете: файл заказа A под сеансом заказа B
        var sessionB = await SessionIdOfOrderAsync(b.Token);
        (await AuthedClient(company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/service-sessions/{sessionB}/payment-proofs/{foreignProof}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // файл заказа не отдаётся как подтверждение дома и наоборот
        (await anon.GetAsync($"/api/stays/bookings/public/{a.Token}/payment-proofs/{foreignProof}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // просмотр файла персоналом пишется в журнал
        var sessionA = await SessionIdOfOrderAsync(a.Token);
        (await AuthedClient(company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/service-sessions/{sessionA}/payment-proofs/{foreignProof}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SessionCardAsync(company, sessionA)).Events.Should().Contain(e => e.Kind.Contains("ProofViewed"));
    }

    [Fact, TestCase("CY39-173")]
    public async Task OrderPage_UnknownOrMalformedTokens_404_WithoutOracle()
    {
        var anon = AnonymousClient();
        foreach (var token in new[] { "x", new string('a', 43), new string('a', 5000), "..%2f..%2fetc", "%00" })
        {
            (await anon.GetAsync($"/api/stays/service-orders/public/{Uri.EscapeDataString(token)}")).StatusCode.Should().Be(HttpStatusCode.NotFound, token.Length > 20 ? "длинный" : token);
            (await anon.PostJsonAsync($"/api/stays/service-orders/public/{Uri.EscapeDataString(token)}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await anon.PostAsync($"/api/stays/service-orders/public/{Uri.EscapeDataString(token)}/payment-proofs", FileContent(TestImages.SolidJpeg(10, 10), "image/jpeg", "x.jpg"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        // токен брони дома на маршруте заказа и наоборот — тоже 404
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company);
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(11));
        (await anon.GetAsync($"/api/stays/service-orders/public/{booked.Token}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── снимок зазора и расписания ───────────────────────────────────────────────

    [Fact, TestCase("CY39-174")]
    public async Task ChangingBuffer_AffectsOnlyNewSessions_ExistingKeepTheirSnapshot()
    {
        var (company, svc) = await SceneAsync(buffer: 60);
        var date = InDays(9);
        await OrderOkAsync(svc.Id, date, 720, 2); // 12:00–14:00 + 60 → занято до 15:00
        var before = (await ActiveSessionsAsync(svc.Id)).Single();
        before.BufferMinutesSnapshot.Should().Be(60);
        var c = AuthedClient(company.OwnerToken);
        var setup = await c.PutJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/setup",
            new ServiceSetupInput("Баня", svc.Slug, 2, 6, 60, 0, false, 0, null, StayServiceCancellationPolicy.NoDeductions, 12, true));
        setup.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await ActiveSessionsAsync(svc.Id)).Single();
        after.OccupiedUntilUtc.Should().Be(before.OccupiedUntilUtc, "снимок зазора: уже созданный сеанс не пересчитывается");
        var starts = (await StartsAsync(svc.Id, date)).Starts.Select(s => s.StartMinute).ToList();
        starts.Should().NotContain(840, "14:00 всё ещё внутри старого зазора сеанса 12:00–14:00 + 60 минут");
        starts.Should().Contain(900);
        await OrderOkAsync(svc.Id, date, 900, 2);
        (await ActiveSessionsAsync(svc.Id)).Last().OccupiedUntilUtc.Should().Be(StartUtc(date, 1020), "новый сеанс — с новым зазором 0");
    }

    [Fact, TestCase("CY39-175")]
    public async Task ScheduleEdits_PastDate_Rejected_FutureAccepted_AndJournalRecordsWho()
    {
        var company = await CreateStaysCompanyAsync();
        var manager = await AddStaffAsync(company, "Manager");
        var svc = await CreateServiceAsync(company);
        var b = $"/api/stays/companies/{company.Id}/services/{svc.Id}/date-overrides";
        var mgr = AuthedClient(manager.Token);
        var past = await mgr.PutJsonAsync($"{b}/{D(InDays(-3))}", new DateOverrideInput(true, [], null));
        past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await past.Content.ReadAsStringAsync()).Should().Contain("прошедшие");
        (await mgr.PutJsonAsync($"{b}/{D(InDays(6))}", new DateOverrideInput(true, [], "Баня на ремонте"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await mgr.DeleteAsync($"{b}/{D(InDays(6))}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var events = await WithDbAsync(db => db.StayServiceScheduleEvents.AsNoTracking().Where(e => e.ServiceId == svc.Id).OrderBy(e => e.OccurredAtUtc).ToListAsync());
        var byManager = events.Where(e => e.ActorUserId == manager.User.UserId).ToList();
        byManager.Should().HaveCountGreaterOrEqualTo(2, "изменение и удаление ручной даты управляющим пишутся в журнал: кто, когда");
        byManager.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.ActorNameSnapshot) && e.BusinessDate == InDays(6));
        var month = await mgr.GetAsync($"{b}?month={InDays(6):yyyy-MM}");
        month.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── адреса и фото ────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-176")]
    public async Task Slugs_HouseCannotTakeServiceWord_ServiceSlugRules()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, publish: false);
        var c = AuthedClient(company.OwnerToken);
        foreach (var word in new[] { "uslugi", "ical", "kalendar" })
        {
            var r = await c.PutJsonAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}/setup", new HouseSetupInput(house.House.Name, word, 4, false, 0, 0, false, false));
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, $"«{word}» зарезервировано под страницы услуг и календари");
            (await Code(r)).Should().Be("SlugReserved");
        }
        var svc = await CreateServiceAsync(company, publish: false);
        foreach (var bad in new[] { "A", "-x", "x-", "банька", "has space", new string('a', 51), "UPPER_case" })
        {
            var r = await c.PutJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/setup",
                new ServiceSetupInput("Баня", bad, 2, 6, 60, 30, false, 0, null, StayServiceCancellationPolicy.NoDeductions, 12, true));
            r.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.BadRequest, HttpStatusCode.OK }, bad);
            if (r.StatusCode == HttpStatusCode.OK) (await r.Content.ReadJsonAsync<ServiceManageDto>())!.Slug.Should().MatchRegex("^[a-z0-9][a-z0-9-]{0,48}[a-z0-9]$", $"«{bad}» нормализован");
        }
        // страница услуги под зарезервированным словом дома не пересекается: /<slug>/uslugi/<serviceSlug> ≠ /<slug>/<houseSlug>
        (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/houses/uslugi")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY39-177")]
    public async Task ServicePhotos_UploadLimitOrderDelete_ForeignServiceAndBadFiles()
    {
        var company = await CreateStaysCompanyAsync();
        var svc = await CreateServiceAsync(company);
        var other = await CreateServiceAsync(company, "Чан");
        var c = AuthedClient(company.OwnerToken);
        var b = $"/api/stays/companies/{company.Id}/services/{svc.Id}/photos";
        (await c.PostAsync(b, FileContent(System.Text.Encoding.UTF8.GetBytes("not an image"), "image/jpeg", "x.jpg"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            var r = await c.PostAsync(b, FileContent(TestImages.SolidJpeg(300 + i, 200), "image/jpeg", $"p{i}.jpg"));
            r.StatusCode.Should().Be(HttpStatusCode.Created, $"фото {i + 1}");
            ids.Add((await r.Content.ReadJsonAsync<ServicePhotoDto>())!.Id);
        }
        var over = await c.PostAsync(b, FileContent(TestImages.SolidJpeg(500, 300), "image/jpeg", "p11.jpg"));
        over.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(over)).Should().Be("PhotoLimitReached");
        // порядок — полный список; удаление чужого фото через свою услугу — 404
        ids.Reverse();
        (await c.PutJsonAsync(b + "/order", new IdsOrderInput(ids))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.PutJsonAsync(b + "/order", new IdsOrderInput(ids.Take(5).ToList()))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.DeleteAsync($"/api/stays/companies/{company.Id}/services/{other.Id}/photos/{ids[0]}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.DeleteAsync($"{b}/{ids[0]}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var page = (await (await AnonymousClient().GetAsync($"/api/stays/public/companies/{company.Slug}/services/{svc.Slug}")).Content.ReadJsonAsync<PublicServiceDto>())!;
        page.Photos.Should().HaveCount(9);
        page.Photos.Select(p => p.Position).Should().BeInAscendingOrder();
        // горничная фото не меняет
        var hk = await AddStaffAsync(company, "Housekeeper");
        (await AuthedClient(hk.Token).PostAsync(b, FileContent(TestImages.SolidJpeg(300, 200), "image/jpeg", "h.jpg"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var manager = await AddStaffAsync(company, "Manager");
        (await AuthedClient(manager.Token).PostAsync(b, FileContent(TestImages.SolidJpeg(310, 200), "image/jpeg", "m.jpg"))).StatusCode.Should().Be(HttpStatusCode.Created, "фото услуги — право управляющего (EditServiceContent)");
    }

    [Fact, TestCase("CY39-178")]
    public async Task BlockedCompany_RefusesNewOrdersAndSessions_ExistingStayVisible()
    {
        var (company, svc) = await SceneAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var booked = await BookOkAsync(house.Id, InDays(12), InDays(15));
        var order = await OrderOkAsync(svc.Id, InDays(10), 720, 2);
        await WithDbAsync(async db =>
        {
            var c = await db.Companies.SingleAsync(x => x.Id == company.Id);
            c.IsActive = false;
            await db.SaveChangesAsync();
        });
        var date = InDays(10);
        var r = await PostOrderAsync(svc.Id, OrderInput(date, 1080, 2, 4000, UniquePhone()));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(r)).Should().Be("NotAcceptingBookings");
        var add = await AddSessionAsync(booked.Token, SessionInput(svc.Id, InDays(13), 720, 2, 4000));
        add.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(add)).Should().Be("NotAcceptingBookings");
        (await GetOrderAsync(order.Token)).Status.Should().Be(StayBookingStatus.Confirmed, "страница существующего заказа открывается");
        (await CancelOrderAsync(order.Token)).StatusCode.Should().Be(HttpStatusCode.OK, "гость может отменить свой заказ и у заблокированной компании");
    }

    [Fact, TestCase("CY39-179")]
    public async Task SessionAdditions_RateLimits_PerBookingLink_AndPerIp_429WithText()
    {
        await using var perToken = new StaysTestFactory(ConnectionString, settings: new Dictionary<string, string> { ["RateLimits:stay-session-add:PermitLimit"] = "3" });
        var (company, svc) = await SceneAsync();
        var house = await CreateHouseAsync(company, price: 1000);
        var ci = InDays(12);
        var booked = await BookOkAsync(house.Id, ci, ci.AddDays(4));
        var statuses = new List<HttpStatusCode>();
        string? text = null;
        for (var i = 0; i < 5; i++)
        {
            var r = await AddSessionAsync(booked.Token, SessionInput(svc.Id, ci.AddDays(1 + i % 3), 480 + 180 * (i / 3), 2, 4000), perToken.Client());
            statuses.Add(r.StatusCode);
            if (r.StatusCode == (HttpStatusCode)429) text = await r.Content.ReadAsStringAsync();
        }
        statuses.Count(x => x == (HttpStatusCode)429).Should().BeGreaterOrEqualTo(1, "не больше N добавлений в окно на ссылку брони: " + string.Join(",", statuses));
        text.Should().Be("Слишком много попыток. Попробуйте позже");

        await using var perIp = new StaysTestFactory(ConnectionString, settings: new Dictionary<string, string> { ["RateLimits:stay-session-ip:PermitLimit"] = "2" });
        var other = await BookOkAsync(house.Id, ci.AddDays(10), ci.AddDays(14));
        var ipStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            ipStatuses.Add((await AddSessionAsync(other.Token, SessionInput(svc.Id, ci.AddDays(11 + i % 3), 480 + 180 * (i / 3), 2, 4000), perIp.Client())).StatusCode);
        ipStatuses.Should().Contain((HttpStatusCode)429, "второе звено цепочки: не больше N добавлений в окно с одного адреса: " + string.Join(",", ipStatuses));
    }

    [Fact, TestCase("CY39-180")]
    public async Task UnpublishedService_UsableByStaffOnly_ArchivedKeepsSessionsOnBoard_CatalogHasNoServices()
    {
        var (company, svc) = await SceneAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var date = InDays(9);
        var c = AuthedClient(company.OwnerToken);
        var order = await OrderOkAsync(svc.Id, date, 720, 2);
        await c.PostJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/unpublish", new EmptyInput());

        // гостю — нет, персоналу — да (ручной сеанс по неопубликованной услуге)
        (await PostOrderAsync(svc.Id, OrderInput(date, 1080, 2, 4000, UniquePhone()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var staffStarts = await c.GetAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/starts?date={D(date)}");
        staffStarts.StatusCode.Should().Be(HttpStatusCode.OK);
        (await staffStarts.Content.ReadJsonAsync<ServiceStartsDto>())!.Starts.Should().NotBeEmpty();
        var manual = await c.PostJsonAsync($"/api/stays/companies/{company.Id}/service-sessions",
            new ManualServiceOrderInput(svc.Id, date, 1080, 2, [], "Свои", null, null, StayServiceRequestBasis.Phone, Guid.NewGuid()));
        manual.StatusCode.Should().Be(HttpStatusCode.Created, await manual.Content.ReadAsStringAsync());

        // архив: сеансы остаются в «Дне услуг» и в шахматке
        await c.PostJsonAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/archive", new EmptyInput());
        (await c.GetAsync($"/api/stays/companies/{company.Id}/services/{svc.Id}/starts?date={D(date)}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "архивной услуге новый сеанс не добавить");
        var board = (await (await c.GetAsync($"/api/stays/companies/{company.Id}/board?from={D(date)}&days=3")).Content.ReadJsonAsync<StaysBoardDto>())!;
        board.Services.Should().ContainSingle(x => x.Id == svc.Id && x.IsArchived, "архивные с сеансами в окне остаются в группе «Услуги»");
        var day = (await (await c.GetAsync($"/api/stays/companies/{company.Id}/service-day?date={D(date)}")).Content.ReadJsonAsync<ServiceDayDto>())!;
        day.Services.Should().ContainSingle(x => x.Id == svc.Id).Which.Bars.Count(b => b.Kind == ServiceDayBarKind.Session).Should().Be(2);
        (await GetOrderAsync(order.Token)).Status.Should().Be(StayBookingStatus.Confirmed);

        // каталог остаётся каталогом домов
        InvalidateCatalog();
        var catalog = await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50");
        var raw = await catalog.Content.ReadAsStringAsync();
        raw.Should().NotContain("Баня").And.NotContain("uslugi").And.NotContain(svc.Slug);
        _ = house;
    }

    [Fact, TestCase("CY39-181")]
    public async Task StayMode_StartsAndAvailability_OnlyInsideTheStay_RealTime_PartialParametersAre400()
    {
        var (company, svc) = await SceneAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var settings = (await GetCompanyAsync(company)).Settings!;
        var inT = TimeOnly.Parse(settings.CheckInTime);
        var outT = TimeOnly.Parse(settings.CheckOutTime);
        var ci = InDays(12);
        var co = ci.AddDays(2);
        var anon = AnonymousClient();
        string Q(DateOnly d) => $"/api/stays/public/services/{svc.Id}/starts?date={D(d)}&houseId={house.Id}&checkIn={D(ci)}&checkOut={D(co)}";

        (await StartsAsync(svc.Id, ci.AddDays(-1))).Starts.Should().NotBeEmpty("без проживания вчерашний день доступен");
        (await (await anon.GetAsync(Q(ci.AddDays(-1)))).Content.ReadJsonAsync<ServiceStartsDto>())!.Starts.Should().BeEmpty("до заезда в режиме проживания стартов нет");
        var first = (await (await anon.GetAsync(Q(ci))).Content.ReadJsonAsync<ServiceStartsDto>())!;
        first.Starts.Should().NotBeEmpty();
        first.Starts.Should().OnlyContain(x => x.StartUtc >= StartUtc(ci, inT.Hour * 60 + inT.Minute), "в день заезда — только после времени заезда");
        var middle = (await (await anon.GetAsync(Q(ci.AddDays(1)))).Content.ReadJsonAsync<ServiceStartsDto>())!;
        middle.Starts.Select(x => x.StartMinute).Should().Contain(480).And.Contain(1380, "в середине проживания — всё окно, включая ночные старты");
        var last = (await (await anon.GetAsync(Q(co))).Content.ReadJsonAsync<ServiceStartsDto>())!;
        last.Starts.Should().OnlyContain(x => x.StartUtc.AddHours(x.MaxHours) <= StartUtc(co, outT.Hour * 60 + outT.Minute), "в день выезда сеанс заканчивается до времени выезда");
        // ночной сеанс последней ночи, заканчивающийся после полуночи, но до выезда (бизнес-день = предыдущая дата) — допустим
        (await (await anon.GetAsync(Q(co.AddDays(-1)))).Content.ReadJsonAsync<ServiceStartsDto>())!.Starts.Should().Contain(x => x.StartMinute == 1380);

        var avail = (await (await anon.GetAsync($"/api/stays/public/services/{svc.Id}/availability?from={D(ci.AddDays(-2))}&days=6&houseId={house.Id}&checkIn={D(ci)}&checkOut={D(co)}")).Content.ReadJsonAsync<ServiceAvailabilityDto>())!;
        avail.Days.Where(d => d.BusinessDate < ci).Should().OnlyContain(d => !d.HasStarts);
        avail.Days.Single(d => d.BusinessDate == ci.AddDays(1)).HasStarts.Should().BeTrue();
        avail.Days.Where(d => d.BusinessDate > co).Should().OnlyContain(d => !d.HasStarts);

        // расчёт в режиме проживания: вне проживания — проблема OutsideStay, деньги не показываются как «можно»
        var outside = await anon.PostJsonAsync($"/api/stays/public/services/{svc.Id}/quote", new PublicServiceQuoteInput(ci.AddDays(-1), 720, 2, [], house.Id, ci, co));
        var outsideQuote = (await outside.Content.ReadJsonAsync<ServiceQuoteDto>())!;
        outsideQuote.Ok.Should().BeFalse();
        outsideQuote.Problems.Should().Contain(p => p.Code == ServiceRefusalCode.OutsideStay);
        outsideQuote.Problems.First(p => p.Code == ServiceRefusalCode.OutsideStay).Message.Should().Contain("пределах проживания");

        // неполный набор «дом + обе даты» — 400; чужой дом — 404
        foreach (var url in new[] { $"/api/stays/public/services/{svc.Id}/starts?date={D(ci)}&houseId={house.Id}", $"/api/stays/public/services/{svc.Id}/starts?date={D(ci)}&checkIn={D(ci)}&checkOut={D(co)}",
                     $"/api/stays/public/services/{svc.Id}/starts?date={D(ci)}&houseId={house.Id}&checkIn={D(co)}&checkOut={D(ci)}" })
            (await anon.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.BadRequest, url);
        var strangerHouse = await CreateHouseAsync(await CreateStaysCompanyAsync(), price: 1000);
        (await anon.GetAsync($"/api/stays/public/services/{svc.Id}/starts?date={D(ci)}&houseId={strangerHouse.Id}&checkIn={D(ci)}&checkOut={D(co)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
