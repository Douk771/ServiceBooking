using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 37, «Вызов 2»: расчёт, бронь гостя, подтверждение оплаты, отмена, действия персонала (US-37-15…21, US-37-24, SPEC §4.5–§4.7).
/// Написано по SPEC_CYCLE37_STAYS_HOUSES.md и API_CONTRACT_CYCLE37.md §37.23–§37.30, а не по реализации. Цены целые рубли.
/// </summary>
public class Cycle37BookingFlowTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    // ── расчёт (SPEC §4.6) ──────────────────────────────────────────────────────

    [Fact, TestCase("CY37-30")]
    public async Task Quote_BreakdownAndPrepayment_DefaultTemplate()
    {
        var company = await CreateStaysCompanyAsync(); // предоплата 30 % по умолчанию
        var house = await CreateHouseAsync(company, price: 5000);

        var q = await QuoteAsync(house.Id, InDays(10), InDays(13));
        q.Ok.Should().BeTrue();
        q.Nights.Should().Be(3);
        q.NightPrices.Should().HaveCount(3).And.OnlyContain(n => n.PriceRub == 5000);
        q.TotalRub.Should().Be(15000);
        q.PrepayPercent.Should().Be(30);
        q.PrepayRub.Should().Be(4500, "30 % от стоимости ночей");
        q.DueAtCheckInRub.Should().Be(10500);
        q.HoldMinutes.Should().Be(30);
        q.CheckInTime.Should().Be("14:00");
        q.CheckOutTime.Should().Be("12:00");
        q.AcceptingBookings.Should().BeTrue();
        q.CancellationPolicy.Should().Be(StayCancellationPolicy.Standard, "по умолчанию — «Стандартный» (ЮР-1)");
        q.CancellationSummary.Should().NotBeNullOrWhiteSpace();
        q.Lines.Should().ContainSingle(l => l.Kind == StayChargeKind.Nights && l.AmountRub == 15000 && l.PrepayEligible);
    }

    [Fact, TestCase("CY37-31")]
    public async Task Quote_ExtraBedsDogsCot_AreOutsidePrepayment_AndRoundingIsHalfUp()
    {
        var company = await CreateStaysCompanyAsync(settings: s => s with { DogFeeRub = 300, CotFeeRub = 200, PrepayPercent = 30 });
        var house = await CreateHouseAsync(company, capacity: 4, price: 3333, extraBedsMax: 2, extraBedPrice: 700, hasCot: true);

        // 6 гостей при вместимости 4 -> 2 доп. места; 2 собаки; манеж; 2 ночи
        var q = await QuoteAsync(house.Id, InDays(10), InDays(12), adults: 4, children: 2, dogs: 2, needCot: true);
        q.Ok.Should().BeTrue(string.Join("; ", q.Problems.Select(p => p.Message)));
        q.ExtraBeds.Should().Be(2);
        q.Lines.Single(l => l.Kind == StayChargeKind.ExtraBeds).AmountRub.Should().Be(2 * 700 * 2, "нужных мест × цена × ночей");
        q.Lines.Single(l => l.Kind == StayChargeKind.Dogs).AmountRub.Should().Be(2 * 300 * 2, "собак × сумма × ночей");
        q.Lines.Single(l => l.Kind == StayChargeKind.Cot).AmountRub.Should().Be(200 * 2, "манеж: цена × ночей");
        q.Lines.Where(l => l.Kind != StayChargeKind.Nights).Should().OnlyContain(l => !l.PrepayEligible, "в предоплату входит только стоимость ночей");
        var nights = 3333 * 2;
        q.TotalRub.Should().Be(nights + 2800 + 1200 + 400);
        // 6666 * 30 / 100 = 1999.8 -> 2000 (до рубля)
        q.PrepayRub.Should().Be(2000);
        q.DueAtCheckInRub.Should().Be(q.TotalRub - 2000);

        // половина — вверх: 5 ₽ × 10 % = 0,5 -> 1 ₽
        var tiny = await CreateStaysCompanyAsync(prepayPercent: 10);
        var tinyHouse = await CreateHouseAsync(tiny, price: 5);
        var tq = await QuoteAsync(tinyHouse.Id, InDays(10), InDays(11));
        tq.PrepayRub.Should().Be(1, "округление до рубля, половина — вверх");
    }

    [Fact, TestCase("CY37-32")]
    public async Task Quote_ExtraBedsRowAppearsOnlyWhenNeeded_AndGuestsBeyondLimitAreRefused()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, capacity: 4, price: 4000, extraBedsMax: 1, extraBedPrice: 500);
        var within = await QuoteAsync(house.Id, InDays(10), InDays(11), adults: 4);
        within.Lines.Should().NotContain(l => l.Kind == StayChargeKind.ExtraBeds);
        within.ExtraBeds.Should().Be(0);

        var over = await QuoteAsync(house.Id, InDays(10), InDays(11), adults: 4, children: 2);
        over.Ok.Should().BeFalse();
        over.Problems.Should().ContainSingle(p => p.Code == StayRefusalCode.TooManyGuests).Which.Message.Should().Contain("5").And.Contain("доп. места");
        over.TotalRub.Should().Be(0);
        over.Lines.Should().BeEmpty();

        var noExtra = await CreateHouseAsync(company, capacity: 2, price: 1000);
        var q = await QuoteAsync(noExtra.Id, InDays(10), InDays(11), adults: 3);
        q.Problems.Should().ContainSingle(p => p.Code == StayRefusalCode.TooManyGuests).Which.Message.Should().NotContain("доп. места");
    }

    [Fact, TestCase("CY37-33")]
    public async Task Quote_ByDatesMode_PricePerNightByStartOfNight_AndSingleDayOverride()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 4000);
        var c = AuthedClient(company.OwnerToken);
        var hp = $"/api/stays/companies/{company.Id}/houses/{house.Id}";

        var from = InDays(20);
        // опубликованный дом нельзя оставить без цены: режим «по датам» без периодов — 409 NoPrice
        var noPeriods = await c.PutJsonAsync(hp + "/pricing", new HousePricingInput(HousePriceMode.ByDates, null));
        noPeriods.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(noPeriods)).Should().Be("NoPrice");
        (await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(from, from.AddDays(9), 6000))).StatusCode.Should().Be(HttpStatusCode.Created);
        // одна дата внутри длинного периода переопределяет цену (SPEC §4.4)
        (await c.PostJsonAsync(hp + "/price-periods", new PricePeriodInput(from.AddDays(2), from.AddDays(2), 9000))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await c.PutJsonAsync(hp + "/pricing", new HousePricingInput(HousePriceMode.ByDates, null))).StatusCode.Should().Be(HttpStatusCode.OK);

        var q = await QuoteAsync(house.Id, from.AddDays(1), from.AddDays(4));
        q.NightPrices.Select(n => n.PriceRub).Should().Equal(6000, 9000, 6000);
        q.TotalRub.Should().Be(21000);

        // ночь без цены не бронируется
        var gap = await QuoteAsync(house.Id, from.AddDays(8), from.AddDays(12));
        gap.Ok.Should().BeFalse();
        gap.Problems.Should().Contain(p => p.Code == StayRefusalCode.NoPriceForNights);
        gap.TotalRub.Should().Be(0);
        var b = await PostBookingAsync(house.Id, Booking(from.AddDays(8), from.AddDays(12), 0));
        b.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(b)).Should().Be("NoPriceForNights");

        // переключение режима не стирает данные второго режима: константа 4000 хранится, но не действует
        var back = await c.PutJsonAsync(hp + "/pricing", new HousePricingInput(HousePriceMode.Constant, null));
        back.StatusCode.Should().Be(HttpStatusCode.OK);
        (await QuoteAsync(house.Id, from.AddDays(1), from.AddDays(2))).TotalRub.Should().Be(4000);
        (await c.PutJsonAsync(hp + "/pricing", new HousePricingInput(HousePriceMode.ByDates, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync(hp + "/price-periods")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await QuoteAsync(house.Id, from.AddDays(1), from.AddDays(2))).TotalRub.Should().Be(6000, "периоды режима «по датам» сохранились");
    }

    [Fact, TestCase("CY37-34")]
    public async Task ExistingBooking_KeepsItsPriceSnapshot_WhenPricesAndSettingsChange()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 5000);
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12));

        var c = AuthedClient(company.OwnerToken);
        (await c.PutJsonAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}/pricing", new HousePricingInput(HousePriceMode.Constant, 9000))).StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = (await GetCompanyAsync(company)).Settings!;
        await PutSettingsAsync(company, settings with { PrepayPercent = 100, CancellationPolicy = StayCancellationPolicy.NoDeductions });
        await PutPaymentDetailsAsync(company, "Другой банк, другие реквизиты", null);

        var page = await GetPublicBookingAsync(booked.Token);
        page.TotalRub.Should().Be(10000);
        page.PrepayPercent.Should().Be(30);
        page.PrepayRub.Should().Be(3000);
        page.Cancellation.Policy.Should().Be(StayCancellationPolicy.Standard, "правило отмены копируется в бронь");
        page.Payment!.Details.Should().Be(PaymentDetailsText, "реквизиты в брони — снимок на момент создания");
        (await QuoteAsync(house.Id, InDays(20), InDays(22))).TotalRub.Should().Be(18000, "новые брони считаются по новым ценам");
    }

    // ── создание брони (US-37-15) ──────────────────────────────────────────────

    [Fact, TestCase("CY37-35")]
    public async Task CreateBooking_HappyPath_HeldWithPaymentInstructions_AndOneTimeSecretLink()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 5000, address: "Шерегеш, ул. Лесная, 5");
        var ci = InDays(10);
        var co = InDays(13);

        var created = await BookOkAsync(house.Id, ci, co, phone: "+79051234512", comment: "Едем с ёлкой", arrival: "18:30");
        created.BookingUrl.Should().Be($"https://dom.ezbook.ru/b/{created.Token}");
        created.Token.Length.Should().BeGreaterThanOrEqualTo(43, "не меньше 128 бит случайности (256 бит в base64url)");
        created.Token.Should().MatchRegex("^[A-Za-z0-9_-]+$");

        var b = created.Booking;
        b.Status.Should().Be(StayBookingStatus.Held);
        b.StatusText.Should().Be("Ожидает оплаты");
        b.HoldExpiresAtUtc.Should().NotBeNull();
        (b.HoldExpiresAtUtc!.Value - b.ServerTimeUtc).Should().BeCloseTo(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(30));
        b.TotalRub.Should().Be(15000);
        b.PrepayRub.Should().Be(4500);
        b.DueAtCheckInRub.Should().Be(10500);
        b.Payment.Should().NotBeNull();
        b.Payment!.AmountRub.Should().Be(4500);
        b.Payment.Details.Should().Be(PaymentDetailsText);
        b.Payment.Purpose.Should().Be(PaymentPurposeText);
        b.House.Address.Should().Be("Шерегеш, ул. Лесная, 5", "адрес виден сразу, с момента создания");
        b.GuestPhoneMasked.Should().NotContain("1234").And.Contain("12", "телефон гостя — маской, виден только хвост");
        b.GuestPhoneMasked.Should().EndWith("12");
        b.Comment.Should().Be("Едем с ёлкой");
        b.ArrivalTime.Should().Be("18:30");
        b.CheckInTime.Should().Be("14:00");
        b.CheckOutTime.Should().Be("12:00");
        b.Proofs.CanAttach.Should().BeTrue();
        b.Proofs.MaxCount.Should().Be(3);
        b.Proofs.MaxBytes.Should().Be(10 * 1024 * 1024);
        b.Cancellation.CanCancel.Should().BeTrue();
        b.Cancellation.Refund.Kind.Should().Be(StayRefundKind.NothingPaid, "в статусе «Удержана» денег ещё нет — отмена без последствий");
        b.Provider.Should().NotBeNull("сведения об исполнителе — на странице брони");
        b.Provider!.Name.Should().Be("Иванов Иван Иванович", "ФИО физлица видно гостю на странице его брони (ЮР-3)");

        // календарь: ночи удержаны — «Возможно освободится» для двух; вне брони свободно
        var cal = await CalendarAsync(house.Id, ci.AddDays(-1), co.AddDays(1));
        cal.Days.Single(d => d.Date == ci).State.Should().Be(CalendarDayState.MayFreeUp);
        cal.Days.Single(d => d.Date == co.AddDays(-1)).State.Should().Be(CalendarDayState.MayFreeUp);
        cal.Days.Single(d => d.Date == co).State.Should().Be(CalendarDayState.Free, "день выезда — свободная ночь для следующего заезда");
        cal.Days.Single(d => d.Date == ci.AddDays(-1)).State.Should().Be(CalendarDayState.Free);
    }

    [Fact, TestCase("CY37-36")]
    public async Task CreateBooking_FormValidation_PlainTextRussian400()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 5000);
        var ci = InDays(10);
        var co = InDays(12);
        var total = (await QuoteAsync(house.Id, ci, co)).TotalRub;

        async Task Expect400(Func<CreateStayBookingInput, CreateStayBookingInput> change, string text)
        {
            var r = await PostBookingAsync(house.Id, change(Booking(ci, co, total)));
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
            r.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
            (await r.Content.ReadAsStringAsync()).Should().Contain(text);
        }

        await Expect400(b => b with { CheckIn = null }, "Укажите даты заезда и выезда");
        await Expect400(b => b with { Adults = 0 }, "Взрослых — от 1 до 30");
        await Expect400(b => b with { Children = -1 }, "Детей — от 0 до 30");
        await Expect400(b => b with { Dogs = 21 }, "Собак — от 0 до 20");
        await Expect400(b => b with { GuestName = "   " }, "Укажите имя");
        await Expect400(b => b with { GuestName = new string('Я', 101) }, "Имя — не длиннее 100 символов");
        await Expect400(b => b with { GuestPhone = "12345" }, "Введите номер телефона в формате +7 (900) 000-00-00");
        await Expect400(b => b with { GuestPhone = null }, "Введите номер телефона в формате +7 (900) 000-00-00");
        await Expect400(b => b with { ArrivalTime = "13:30" }, "Время прибытия — от времени заезда до 23:30 с шагом 30 минут");
        await Expect400(b => b with { ArrivalTime = "15:10" }, "Время прибытия — от времени заезда до 23:30 с шагом 30 минут");
        await Expect400(b => b with { ArrivalTime = "23:59" }, "Время прибытия — от времени заезда до 23:30 с шагом 30 минут");
        await Expect400(b => b with { Comment = new string('к', 501) }, "Комментарий — не длиннее 500 символов");
        await Expect400(b => b with { IdempotencyKey = null }, "Нужен ключ запроса — обновите страницу");

        // граница допустимого проходит: 500 символов комментария, 23:30, «не знаю»
        (await PostBookingAsync(house.Id, Booking(ci, co, total, comment: new string('к', 500), arrival: "23:30"))).StatusCode.Should().Be(HttpStatusCode.Created);

        // пустое тело / не JSON — не 500
        var empty = await AnonymousClient().PostAsync($"/api/stays/public/houses/{house.Id}/bookings", new StringContent("", System.Text.Encoding.UTF8, "application/json"));
        ((int)empty.StatusCode).Should().BeInRange(400, 415);
        var garbage = await AnonymousClient().PostAsync($"/api/stays/public/houses/{house.Id}/bookings", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));
        ((int)garbage.StatusCode).Should().BeInRange(400, 415);
    }

    [Fact, TestCase("CY37-37")]
    public async Task CreateBooking_RuleRefusals_AreJson409WithCode_AndNothingIsCreated()
    {
        var company = await CreateStaysCompanyAsync(settings: s => s with { MinNights = 2, MaxNights = 5, HorizonDays = 30, AllowSameDayCheckIn = false });
        var house = await CreateHouseAsync(company, capacity: 2, price: 1000, dogsForbidden: true, hasCot: false);
        var today = InDays(0);

        async Task<(string Code, string Message)> Refused(DateOnly ci, DateOnly co, int adults = 2, int dogs = 0, bool cot = false)
        {
            var r = await PostBookingAsync(house.Id, Booking(ci, co, 1, adults: adults, dogs: dogs, needCot: cot));
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
            r.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
            var j = await J(r);
            return (j.GetProperty("code").GetString()!, j.GetProperty("message").GetString()!);
        }

        (await Refused(today.AddDays(5), today.AddDays(5))).Code.Should().Be("InvalidDates");
        (await Refused(today.AddDays(6), today.AddDays(5))).Code.Should().Be("InvalidDates");
        (await Refused(today.AddDays(-3), today.AddDays(-1))).Code.Should().Be("CheckInInPast");
        var sameDay = await Refused(today, today.AddDays(2));
        sameDay.Code.Should().Be("SameDayNotAllowed");
        sameDay.Message.Should().Be("Заезд в день бронирования недоступен — выберите дату с завтрашнего дня");
        var beyond = await Refused(today.AddDays(29), today.AddDays(31));
        beyond.Code.Should().Be("BeyondHorizon");
        beyond.Message.Should().StartWith("Бронирование открыто до ");
        var tooLong = await Refused(today.AddDays(2), today.AddDays(9));
        tooLong.Code.Should().Be("MaxNightsExceeded");
        tooLong.Message.Should().Be("Максимальный срок проживания — 5 ночей");
        var tooShort = await Refused(today.AddDays(2), today.AddDays(3));
        tooShort.Code.Should().Be("MinNightsNotMet");
        tooShort.Message.Should().Be("Минимальный срок проживания — 2 ночи");
        (await Refused(today.AddDays(2), today.AddDays(4), adults: 3)).Code.Should().Be("TooManyGuests");
        (await Refused(today.AddDays(2), today.AddDays(4), dogs: 1)).Message.Should().Be("В этом доме нельзя проживать с собаками");
        (await Refused(today.AddDays(2), today.AddDays(4), cot: true)).Message.Should().Be("В этом доме нет детской кроватки");

        (await WithDbAsync(db => db.StayBookings.CountAsync(b => b.HouseId == house.Id))).Should().Be(0, "бронь не создаётся молча");
    }

    [Fact, TestCase("CY37-38")]
    public async Task CreateBooking_MinNights_GapFillRule()
    {
        var closed = await CreateStaysCompanyAsync(settings: s => s with { MinNights = 3, AllowGapFill = false });
        var open = await CreateStaysCompanyAsync(settings: s => s with { MinNights = 3, AllowGapFill = true });
        var closedHouse = await CreateHouseAsync(closed, price: 1000);
        var openHouse = await CreateHouseAsync(open, price: 1000);

        foreach (var (house, allowed) in new[] { (closedHouse, false), (openHouse, true) })
        {
            // занять [10,13) и [14,17): между ними один свободный день — разрыв короче минимума
            await BookOkAsync(house.Id, InDays(10), InDays(13));
            await BookOkAsync(house.Id, InDays(14), InDays(17));
            var q = await QuoteAsync(house.Id, InDays(13), InDays(14));
            if (allowed)
            {
                q.Ok.Should().BeTrue(string.Join("; ", q.Problems.Select(p => p.Message)));
                (await PostBookingAsync(house.Id, Booking(InDays(13), InDays(14), q.TotalRub))).StatusCode.Should().Be(HttpStatusCode.Created);
            }
            else
            {
                q.Problems.Should().Contain(p => p.Code == StayRefusalCode.MinNightsNotMet);
            }
            // короткая бронь не у разрыва — всегда отказ
            var elsewhere = await QuoteAsync(house.Id, InDays(30), InDays(31));
            elsewhere.Problems.Should().Contain(p => p.Code == StayRefusalCode.MinNightsNotMet);
        }
    }

    [Fact, TestCase("CY37-39")]
    public async Task CreateBooking_SameDayCheckInAllowed_Works_And_DayOfDepartureIsDayOfArrival()
    {
        var company = await CreateStaysCompanyAsync(settings: s => s with { AllowSameDayCheckIn = true });
        var house = await CreateHouseAsync(company, price: 2000);
        var first = await BookOkAsync(house.Id, InDays(10), InDays(12));
        // выезд предыдущей брони может быть датой заезда следующей (12:00 / 14:00)
        var second = await BookOkAsync(house.Id, InDays(12), InDays(14));
        first.Booking.Status.Should().Be(StayBookingStatus.Held);
        second.Booking.CheckInDate.Should().Be(InDays(12));
        // а нахлёст на одну ночь — нет
        var q = await QuoteAsync(house.Id, InDays(11), InDays(13));
        q.Problems.Should().Contain(p => p.Code == StayRefusalCode.DatesUnavailable);
        // заезд «сегодня» разрешён настройкой
        var today = await QuoteAsync(house.Id, InDays(0), InDays(1));
        today.Ok.Should().BeTrue(string.Join("; ", today.Problems.Select(p => p.Message)));
    }

    [Fact, TestCase("CY37-40")]
    public async Task CreateBooking_Idempotency_RepeatWithSameKeyIsNotASecondBooking()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 3000);
        var ci = InDays(10);
        var co = InDays(12);
        var total = (await QuoteAsync(house.Id, ci, co)).TotalRub;
        var input = Booking(ci, co, total);

        var first = await PostBookingAsync(house.Id, input);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstToken = (await first.Content.ReadJsonAsync<CreateStayBookingResponse>())!.Token;
        var repeat = await PostBookingAsync(house.Id, input);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK, "повтор запроса с тем же ключом — существующая бронь");
        (await repeat.Content.ReadJsonAsync<CreateStayBookingResponse>())!.Token.Should().Be(firstToken);

        // «двойное нажатие»: параллельно, тот же ключ — одна бронь в БД, оба ответа с одним токеном
        var key = Guid.NewGuid();
        var other = Booking(InDays(20), InDays(22), total, key: key);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostBookingAsync(house.Id, other)));
        results.Select(r => r.StatusCode).Should().OnlyContain(c => c == HttpStatusCode.Created || c == HttpStatusCode.OK);
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().BeLessThanOrEqualTo(1);
        var tokens = await Task.WhenAll(results.Select(async r => (await r.Content.ReadJsonAsync<CreateStayBookingResponse>())!.Token));
        tokens.Distinct().Should().ContainSingle();
        (await WithDbAsync(db => db.StayBookings.CountAsync(b => b.HouseId == house.Id && b.CheckInDate == InDays(20)))).Should().Be(1);
    }

    [Fact, TestCase("CY37-41")]
    public async Task CreateBooking_PriceChanged_Json409WithNewQuote_ThenRetryWithSameKeyAndNewTotal()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 3000);
        var ci = InDays(10);
        var co = InDays(12);
        var shown = (await QuoteAsync(house.Id, ci, co)).TotalRub;
        var key = Guid.NewGuid();

        // владелец поднимает цену, пока гость смотрит на форму
        (await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/houses/{house.Id}/pricing", new HousePricingInput(HousePriceMode.Constant, 3500)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var r = await PostBookingAsync(house.Id, Booking(ci, co, shown, key: key));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(r);
        body.GetProperty("code").GetString().Should().Be("PriceChanged");
        body.GetProperty("message").GetString().Should().Contain("7").And.Contain("000");
        body.GetProperty("quote").GetProperty("totalRub").GetInt32().Should().Be(7000);
        (await WithDbAsync(db => db.StayBookings.CountAsync(b => b.HouseId == house.Id))).Should().Be(0, "бронь не создаётся с другими данными");

        var retry = await PostBookingAsync(house.Id, Booking(ci, co, 7000, key: key));
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        (await retry.Content.ReadJsonAsync<CreateStayBookingResponse>())!.Booking.TotalRub.Should().Be(7000);
    }

    [Fact, TestCase("CY37-42")]
    public async Task CreateBooking_ZeroPrepayment_IsConfirmedAtOnce_WithoutHold()
    {
        var company = await CreateStaysCompanyAsync(prepayPercent: 0);
        var house = await CreateHouseAsync(company, price: 4000);
        var created = await BookOkAsync(house.Id, InDays(10), InDays(12));
        created.Booking.Status.Should().Be(StayBookingStatus.Confirmed);
        created.Booking.HoldExpiresAtUtc.Should().BeNull();
        created.Booking.PrepayRub.Should().Be(0);
        created.Booking.DueAtCheckInRub.Should().Be(8000);
        created.Booking.AvailableActions.Should().NotContain("AttachProof");
        var cal = await CalendarAsync(house.Id, InDays(9), InDays(13));
        cal.Days.Single(d => d.Date == InDays(10)).State.Should().Be(CalendarDayState.Occupied);
        (await GetCompanyAsync(company)).Settings!.PrepayPercent.Should().Be(0);
    }

    [Fact, TestCase("CY37-43")]
    public async Task CompanyGate_ClosedBookings_ReasonCodes_ButPagesStayVisible()
    {
        // нет тарифа
        var noPlan = await CreateStaysCompanyAsync(plan: false);
        var noPlanHouseDraft = await CreateHouseAsync(noPlan, publish: false);
        var publish = await AuthedClient(noPlan.OwnerToken).PostJsonAsync($"/api/stays/companies/{noPlan.Id}/houses/{noPlanHouseDraft.Id}/publish",
            new HousePublishInput(new AttestationInput(true, noPlanHouseDraft.House.RegistryNotice.Version)));
        publish.StatusCode.Should().Be((HttpStatusCode)402, "без тарифа публиковать нельзя");
        (await publish.Content.ReadAsStringAsync()).Should().Be("Выберите тариф, чтобы публиковать дома");
        await GiveStaysPlanAsync(noPlan.Id, StaysPlans.UnlimitedSeedId);
        var published = await PublishHouseAsync(noPlan, noPlanHouseDraft.House);
        await GiveStaysPlanAsync(noPlan.Id, StaysPlans.UnlimitedSeedId, paidUntil: DateTime.UtcNow.AddDays(-1)); // тариф закончился

        async Task<JsonElement> Refusal(StaysCtx c, Guid houseId)
        {
            var q = (await QuoteAsync(houseId, InDays(10), InDays(11))).TotalRub;
            var r = await PostBookingAsync(houseId, Booking(InDays(10), InDays(11), q));
            r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
            return await J(r);
        }

        var expired = await Refusal(noPlan, published.Id);
        expired.GetProperty("code").GetString().Should().Be("NotAcceptingBookings");
        expired.GetProperty("reasonCode").GetString().Should().Be("NoPlan");
        expired.GetProperty("message").GetString().Should().Be("Бронирование временно недоступно", "гостю — без подробностей для владельца");
        var pageAfterExpiry = await J(await AnonymousClient().GetAsync($"/api/stays/public/companies/{noPlan.Slug}"));
        pageAfterExpiry.GetProperty("available").GetBoolean().Should().BeTrue("страницы остаются видны");
        pageAfterExpiry.GetProperty("acceptingBookings").GetBoolean().Should().BeFalse();
        pageAfterExpiry.GetProperty("notAcceptingText").GetString().Should().Be("Бронирование временно недоступно");
        InvalidateCatalog();
        (await J(await AnonymousClient().GetAsync("/api/stays/public/catalog?pageSize=50"))).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("houseId").GetGuid()).Should().NotContain(published.Id, "в каталоге только дома компаний, принимающих брони");
        (await GetCompanyAsync(noPlan)).Gate.ReasonCode.Should().Be("NoPlan");

        // нет реквизитов при предоплате > 0
        var noDetails = await CreateStaysCompanyAsync(paymentDetails: false);
        var h2 = await CreateHouseAsync(noDetails);
        (await Refusal(noDetails, h2.Id)).GetProperty("reasonCode").GetString().Should().Be("NoPaymentDetails");
        (await GetCompanyAsync(noDetails)).Checklist.Should().Contain(i => i.Code == "PaymentDetails" && !i.Done);
        // …но при предоплате 0 % реквизиты не нужны
        var zero = await CreateStaysCompanyAsync(paymentDetails: false, prepayPercent: 0);
        var h3 = await CreateHouseAsync(zero);
        (await BookOkAsync(h3.Id, InDays(10), InDays(11))).Booking.Status.Should().Be(StayBookingStatus.Confirmed);

        // нет сведений об исполнителе
        var noProvider = await CreateStaysCompanyAsync(provider: false);
        var h4 = await CreateHouseAsync(noProvider);
        (await Refusal(noProvider, h4.Id)).GetProperty("reasonCode").GetString().Should().Be("NoProviderInfo");
        // ЮР39-2 (изменённое требование цикла 39, закрывает §37.19 п. 5): сведения об исполнителе обязательны при ЛЮБОЙ предоплате, в том числе при 0 %
        var zeroNoProvider = await CreateStaysCompanyAsync(provider: false, paymentDetails: false, prepayPercent: 0);
        var h5 = await CreateHouseAsync(zeroNoProvider);
        (await Refusal(zeroNoProvider, h5.Id)).GetProperty("reasonCode").GetString().Should().Be("NoProviderInfo");
        (await GetCompanyAsync(zeroNoProvider)).Checklist.Should().Contain(i => i.Code == "ProviderInfo" && !i.Done);
    }

    [Fact, TestCase("CY37-44")]
    public async Task CompanyGate_HousesOverTariffLimit_CloseBookings_UntilOwnerUnpublishes()
    {
        var company = await CreateStaysCompanyAsync();
        var h1 = await CreateHouseAsync(company, price: 1000);
        var h2 = await CreateHouseAsync(company, price: 1000);
        (await BookOkAsync(h1.Id, InDays(10), InDays(11))).Booking.Status.Should().Be(StayBookingStatus.Held);

        // тариф «Один дом» при двух опубликованных домах
        await GiveStaysPlanAsync(company.Id, StaysPlans.OneHouseSeedId);
        var q = (await QuoteAsync(h2.Id, InDays(12), InDays(13))).TotalRub;
        var r = await PostBookingAsync(h2.Id, Booking(InDays(12), InDays(13), q));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(r)).GetProperty("reasonCode").GetString().Should().Be("OverHouseLimit");
        var manage = await GetCompanyAsync(company);
        manage.Plan.WarningLevel.Should().Be("OverLimit");
        manage.Plan.HousesPublished.Should().Be(2);

        // уже созданные брони живут: страница брони открывается
        var existing = await WithDbAsync(db => db.StayBookings.Where(b => b.HouseId == h1.Id).Select(b => b.PublicToken).SingleAsync());
        (await AnonymousClient().GetAsync($"/api/stays/bookings/public/{existing}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // владелец снимает лишний дом с публикации — бронь снова возможна
        (await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/houses/{h2.Id}/unpublish", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await BookOkAsync(h1.Id, InDays(20), InDays(21))).Booking.Status.Should().Be(StayBookingStatus.Held);

        // публикация сверх лимита — 402 с названием тарифа
        var again = await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/houses/{h2.Id}/publish",
            new HousePublishInput(new AttestationInput(true, h2.House.RegistryNotice.Version)));
        again.StatusCode.Should().Be((HttpStatusCode)402);
        (await again.Content.ReadAsStringAsync()).Should().StartWith("Тариф «").And.Contain("позволяет опубликовать 1 дом");
    }

    // ── страница брони и подтверждение оплаты (US-37-17, 18, 20) ───────────────────

    [Fact, TestCase("CY37-45")]
    public async Task BookingPage_UnknownOrMalformedToken_Is404WithEmptyBody_EverywhereIndistinguishable()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company);
        var real = await BookOkAsync(house.Id, InDays(10), InDays(11));

        foreach (var token in new[] { "no-such-token", new string('a', 43), real.Token + "x", real.Token[..^1], new string('z', 500) })
        {
            var get = await AnonymousClient().GetAsync($"/api/stays/bookings/public/{token}");
            get.StatusCode.Should().Be(HttpStatusCode.NotFound, token);
            (await get.Content.ReadAsStringAsync()).Should().BeEmpty();
            (await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{token}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await AttachProofAsync(token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await AnonymousClient().GetAsync($"/api/stays/bookings/public/{token}/payment-proofs/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact, TestCase("CY37-46")]
    public async Task PaymentProof_FirstFileMovesToAwaitingCheck_TimerGone_DatesOccupied_StaffSeesQueueOldestFirst()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var first = await BookOkAsync(house.Id, InDays(10), InDays(11));
        var second = await BookOkAsync(house.Id, InDays(12), InDays(13));

        // оплатил второй раньше первого — в очереди персонала он первый
        var afterSecond = await AttachProofOkAsync(second.Token);
        await Task.Delay(1100);
        var afterFirst = await AttachProofOkAsync(first.Token);
        afterFirst.Status.Should().Be(StayBookingStatus.AwaitingPaymentCheck);
        afterFirst.StatusText.Should().Be("Ожидает проверки оплаты");
        afterFirst.HoldExpiresAtUtc.Should().BeNull("у «Ожидает проверки оплаты» таймера нет");
        afterFirst.PaymentProofs.Should().ContainSingle();
        afterSecond.Status.Should().Be(StayBookingStatus.AwaitingPaymentCheck);

        var cal = await CalendarAsync(house.Id, InDays(9), InDays(14));
        cal.Days.Single(d => d.Date == InDays(10)).State.Should().Be(CalendarDayState.Occupied, "уже не «Возможно освободится»");

        var list = await J(await AuthedClient(company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/bookings"));
        var items = list.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items[0].GetProperty("checkInDate").GetString().Should().Be(D(InDays(12)), "от старых чеков к новым");
        items[0].GetProperty("guestPhone").GetString().Should().NotBeNullOrEmpty("персоналу телефон виден полностью");

        var board = await J(await AuthedClient(company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/board"));
        board.GetProperty("awaitingPaymentCount").GetInt32().Should().Be(2);
        board.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("state").GetString() == "AwaitingPaymentCheck" && i.GetProperty("needsAction").GetBoolean());
    }

    [Fact, TestCase("CY37-47")]
    public async Task PaymentProof_FileValidation_ByContent_NotByExtension_LimitsAndHeaders()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company);
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(11));

        async Task<(HttpStatusCode Code, string Body)> Try(byte[] bytes, string contentType, string name)
        {
            var r = await AnonymousClient().PostAsync($"/api/stays/bookings/public/{booked.Token}/payment-proofs", FileContent(bytes, contentType, name));
            return (r.StatusCode, await r.Content.ReadAsStringAsync());
        }

        (await AnonymousClient().PostAsync($"/api/stays/bookings/public/{booked.Token}/payment-proofs", new MultipartFormDataContent()))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var script = await Try("MZ-not-an-image <script>alert(1)</script>"u8.ToArray(), "image/jpeg", "check.jpg");
        script.Code.Should().Be(HttpStatusCode.BadRequest, "тип проверяется по содержимому, а не по расширению");
        script.Body.Should().Be("Можно приложить PDF, JPEG, PNG или WebP");
        var heic = await Try([0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63, 0, 0, 0, 0], "image/heic", "photo.heic");
        heic.Code.Should().Be(HttpStatusCode.BadRequest, "HEIC сервер не принимает (iOS конвертирует сам)");
        var tooBig = await Try(new byte[10 * 1024 * 1024 + 1024], "application/pdf", "huge.pdf");
        tooBig.Code.Should().Be(HttpStatusCode.BadRequest);
        tooBig.Body.Should().Be("Файл больше 10 МБ");
        (await GetPublicBookingAsync(booked.Token)).Status.Should().Be(StayBookingStatus.Held, "отказ не меняет статус");

        // PDF принимается
        var pdf = await AttachProofAsync(booked.Token, bytes: SamplePdf(), contentType: "application/pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.Created, await pdf.Content.ReadAsStringAsync());
        // до трёх файлов
        (await AttachProofAsync(booked.Token, width: 61)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await AttachProofAsync(booked.Token, width: 62)).StatusCode.Should().Be(HttpStatusCode.Created);
        var fourth = await AttachProofAsync(booked.Token, width: 63);
        fourth.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await J(fourth)).GetProperty("code").GetString().Should().Be("ProofLimitReached");
        (await GetPublicBookingAsync(booked.Token)).PaymentProofs.Should().HaveCount(3);

        // файл отдаётся по ссылке брони: PDF — attachment, заголовки безопасности
        var page = await GetPublicBookingAsync(booked.Token);
        var pdfProof = page.PaymentProofs.Single(p => p.ContentType == "application/pdf");
        var file = await AnonymousClient().GetAsync($"/api/stays/bookings/public/{booked.Token}/payment-proofs/{pdfProof.Id}");
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        file.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        file.Headers.CacheControl!.ToString().Should().Contain("no-store").And.Contain("private");
        file.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        file.Headers.GetValues("Content-Security-Policy").Should().Contain(h => h.Contains("sandbox"));
        // чужой/несуществующий proofId — 404; чужой токен — 404
        (await AnonymousClient().GetAsync($"/api/stays/bookings/public/{booked.Token}/payment-proofs/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var other = await BookOkAsync(house.Id, InDays(20), InDays(21));
        (await AnonymousClient().GetAsync($"/api/stays/bookings/public/{other.Token}/payment-proofs/{pdfProof.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "файл одной брони не открывается по ссылке другой");
        // имя файла клиента не хранится
        (await WithDbAsync(db => db.StayPaymentProofs.Select(p => p.StorageKey).Where(k => k != null).ToListAsync()))
            .Should().OnlyContain(k => !k!.Contains("check") && !k.Contains("huge"));
    }

    [Fact, TestCase("CY37-48")]
    public async Task PaymentProof_ForStaff_ViewedEventNotFlooded_AndOtherCompanyCannotOpenIt()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company);
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(11));
        var page = await AttachProofOkAsync(booked.Token);
        var bookingId = await BookingIdAsync(booked.Token);
        var proof = page.PaymentProofs.Single();
        var manager = await AddStaffAsync(company, "Manager");
        var housekeeper = await AddStaffAsync(company, "Housekeeper");

        var url = $"/api/stays/companies/{company.Id}/bookings/{bookingId}/payment-proofs/{proof.Id}";
        var viewed = await AuthedClient(manager.Token).GetAsync(url);
        viewed.StatusCode.Should().Be(HttpStatusCode.OK);
        viewed.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await AuthedClient(manager.Token).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AuthedClient(housekeeper.Token).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "горничная не видит чеки");
        var foreign = await CreateStaysCompanyAsync();
        (await AuthedClient(foreign.OwnerToken).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuthedClient(foreign.OwnerToken).GetAsync($"/api/stays/companies/{foreign.Id}/bookings/{bookingId}/payment-proofs/{proof.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AnonymousClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var card = await StaffCardAsync(company, bookingId);
        card.Events.Count(e => e.Kind == nameof(StayBookingEventKind.PaymentProofViewed)).Should().Be(1, "просмотры одного файла одним сотрудником не чаще раза в 10 минут");
        card.Events.Should().Contain(e => e.Kind == nameof(StayBookingEventKind.Created));
        card.Events.Should().Contain(e => e.Kind == nameof(StayBookingEventKind.PaymentProofUploaded));
    }

    [Fact, TestCase("CY37-49")]
    public async Task StaffConfirmPayment_ConfirmsBooking_RecordsWhoAndWhen_AndGuestSeesIt()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 5000);
        var manager = await AddStaffAsync(company, "Manager");
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12));
        var id = await BookingIdAsync(booked.Token);
        await AttachProofOkAsync(booked.Token);
        var card = await StaffCardAsync(company, id, manager.Token);
        card.Status.Should().Be(StayBookingStatus.AwaitingPaymentCheck);
        card.AvailableActions.Should().Contain(["ConfirmPayment", "RejectPayment", "Cancel"]);
        card.GuestPhone.Should().NotContain("•", "персоналу телефон виден полностью");

        var confirmed = await StaffActionAsync(company, id, "confirm-payment", card.Version, token: manager.Token);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        var after = (await confirmed.Content.ReadJsonAsync<StaffStayBookingCardDto>())!;
        after.Status.Should().Be(StayBookingStatus.Confirmed);
        after.Version.Should().BeGreaterThan(card.Version);
        after.PaymentConfirmed.Should().NotBeNull();
        after.PaymentConfirmed!.ByName.Should().Contain(manager.User.FirstName);
        after.Events.Should().Contain(e => e.Kind == nameof(StayBookingEventKind.PaymentConfirmed));

        var page = await GetPublicBookingAsync(booked.Token);
        page.Status.Should().Be(StayBookingStatus.Confirmed);
        page.StatusText.Should().Be("Подтверждена");
        page.PaymentConfirmedAtUtc.Should().NotBeNull();
        (await J(await AuthedClient(company.OwnerToken).GetAsync($"/api/stays/companies/{company.Id}/bookings?status=Confirmed")))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact, TestCase("CY37-50")]
    public async Task StaffReject_RequiresReason_FreesDates_GuestSeesReasonAndOutcomeText()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 5000);
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12));
        var id = await BookingIdAsync(booked.Token);
        await AttachProofOkAsync(booked.Token);
        var card = await StaffCardAsync(company, id);

        var noReason = await StaffActionAsync(company, id, "reject-payment", card.Version, reason: "   ");
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noReason.Content.ReadAsStringAsync()).Should().Be("Укажите причину — гость её увидит");
        var tooLong = await StaffActionAsync(company, id, "reject-payment", card.Version, reason: new string('п', 301));
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var noVersion = await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/bookings/{id}/reject-payment", new { reason = "x" });
        noVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var rejected = await StaffActionOkAsync(company, id, "reject-payment", card.Version, "Деньги не пришли");
        rejected.Status.Should().Be(StayBookingStatus.PaymentRejected);
        rejected.StatusReason.Should().Be("Деньги не пришли");

        var page = await GetPublicBookingAsync(booked.Token);
        page.Status.Should().Be(StayBookingStatus.PaymentRejected);
        page.StatusReason.Should().Be("Деньги не пришли");
        page.OutcomeText.Should().Contain("обязана вернуть деньги или восстановить бронь").And.Contain("Деньги не пришли");
        page.Payment.Should().BeNull("в конечных статусах реквизиты не показываются");
        page.Proofs.CanAttach.Should().BeFalse();

        var cal = await CalendarAsync(house.Id, InDays(9), InDays(13));
        cal.Days.Single(d => d.Date == InDays(10)).State.Should().Be(CalendarDayState.Free, "даты свободны сразу");
        (await BookOkAsync(house.Id, InDays(10), InDays(12))).Booking.Status.Should().Be(StayBookingStatus.Held);
        (await AttachProofAsync(booked.Token)).StatusCode.Should().Be(HttpStatusCode.Conflict, "из конечного статуса отката нет");
    }

    [Fact, TestCase("CY37-51")]
    public async Task StaffActions_VersionMismatch_InvalidTransition_AndParallelClicks_OneWins()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 5000);
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(12));
        var id = await BookingIdAsync(booked.Token);

        // «Оплата прошла» для «Удержана» — недопустимый переход
        var held = await StaffCardAsync(company, id);
        var invalid = await StaffActionAsync(company, id, "confirm-payment", held.Version);
        invalid.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var invalidBody = await J(invalid);
        invalidBody.GetProperty("code").GetString().Should().Be("InvalidTransition");
        invalidBody.GetProperty("message").GetString().Should().Be("Действие недоступно в статусе «Ожидает оплаты»");
        invalidBody.GetProperty("booking").GetProperty("status").GetString().Should().Be("Held", "в теле — актуальная карточка");
        (await GetPublicBookingAsync(booked.Token)).Status.Should().Be(StayBookingStatus.Held, "действие не применено");

        await AttachProofOkAsync(booked.Token);
        var card = await StaffCardAsync(company, id);

        // устаревшая версия
        var stale = await StaffActionAsync(company, id, "confirm-payment", card.Version - 1);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var staleBody = await J(stale);
        staleBody.GetProperty("code").GetString().Should().Be("VersionMismatch");
        staleBody.GetProperty("message").GetString().Should().Be("Бронь уже изменена — проверьте актуальное состояние");

        // двое жмут одновременно: подтвердить и отклонить с одной и той же версией -> ровно один успех
        var manager = await AddStaffAsync(company, "Manager");
        var tasks = new[]
        {
            StaffActionAsync(company, id, "confirm-payment", card.Version),
            StaffActionAsync(company, id, "reject-payment", card.Version, "нет денег", manager.Token),
        };
        var results = await Task.WhenAll(tasks);
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        var loser = results.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        (await J(loser)).GetProperty("code").GetString().Should().BeOneOf("VersionMismatch", "InvalidTransition");
        var final = await StaffCardAsync(company, id);
        final.Status.Should().BeOneOf(StayBookingStatus.Confirmed, StayBookingStatus.PaymentRejected);
        final.Events.Count(e => e.Kind is nameof(StayBookingEventKind.PaymentConfirmed) or nameof(StayBookingEventKind.PaymentRejected)).Should().Be(1, "в журнале ровно одно решение");
    }

    [Fact, TestCase("CY37-52")]
    public async Task OwnerCancel_RequiresReason_ShowsFullRefundToStaff_GuestSeesCompanyCancelledText()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 5000);
        foreach (var withProof in new[] { false, true })
        {
            var booked = await BookOkAsync(house.Id, InDays(10 + (withProof ? 10 : 0)), InDays(12 + (withProof ? 10 : 0)));
            var id = await BookingIdAsync(booked.Token);
            if (withProof) await AttachProofOkAsync(booked.Token);
            var card = await StaffCardAsync(company, id);
            if (withProof) card.OwnerCancelRefundText.Should().Contain("Гостю нужно вернуть предоплату полностью").And.Contain("3 000 ₽");
            else card.OwnerCancelRefundText.Should().BeNull("в статусе «Удержана» денег ещё нет — возвращать нечего");

            (await StaffActionAsync(company, id, "cancel", card.Version, reason: "")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var cancelled = await StaffActionOkAsync(company, id, "cancel", card.Version, "Прорвало трубу");
            cancelled.Status.Should().Be(StayBookingStatus.CancelledByOwner);
            var page = await GetPublicBookingAsync(booked.Token);
            page.StatusText.Should().Be("Отменена компанией");
            page.OutcomeText.Should().Contain("Компания отменила бронь").And.Contain("Прорвало трубу").And.Contain("возвращена полностью");
            page.Cancellation.CanCancel.Should().BeFalse();
            (await StaffActionAsync(company, id, "cancel", cancelled.Version, reason: "ещё раз")).StatusCode.Should().Be(HttpStatusCode.Conflict, "конечный статус");
        }
        (await CalendarAsync(house.Id, InDays(9), InDays(23))).Days.Should().OnlyContain(d => d.State == CalendarDayState.Free);
    }

    [Fact, TestCase("CY37-53")]
    public async Task GuestCancel_FreesDates_RefundTextByStatus_NotifiesNothingWrong()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 5000);

        // «Удержана» — без последствий
        var held = await BookOkAsync(house.Id, InDays(10), InDays(12));
        var r1 = await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{held.Token}/cancel", new { });
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var p1 = (await r1.Content.ReadJsonAsync<PublicStayBookingDto>())!;
        p1.Status.Should().Be(StayBookingStatus.CancelledByGuest);
        p1.StatusText.Should().Be("Отменена гостем");
        (await CalendarAsync(house.Id, InDays(9), InDays(13))).Days.Should().OnlyContain(d => d.State == CalendarDayState.Free);

        // «Ожидает проверки» — далеко до заезда: полный возврат
        var paid = await BookOkAsync(house.Id, InDays(20), InDays(22));
        var withProof = await AttachProofOkAsync(paid.Token);
        withProof.Cancellation.Refund.Kind.Should().Be(StayRefundKind.Full);
        withProof.Cancellation.Refund.RefundAtLeastRub.Should().Be(3000);
        withProof.Cancellation.Refund.Text.Should().Contain("К возврату не меньше 3 000 ₽");
        var r2 = await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{paid.Token}/cancel", new { });
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r2.Content.ReadJsonAsync<PublicStayBookingDto>())!.Status.Should().Be(StayBookingStatus.CancelledByGuest);

        // повторная отмена — 409 с актуальной бронью
        var again = await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{paid.Token}/cancel", new { });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await J(again);
        body.GetProperty("code").GetString().Should().Be("CancelNotAllowed");
        body.GetProperty("message").GetString().Should().Be("Бронь уже отменена");
        body.GetProperty("booking").GetProperty("status").GetString().Should().Be("CancelledByGuest");
    }

    [Fact, TestCase("CY37-54")]
    public async Task ManualBooking_IsConfirmedAtOnce_NoMinNights_OverridesTotal_AndConflictsLikeAnyBooking()
    {
        var company = await CreateStaysCompanyAsync(settings: s => s with { MinNights = 3 });
        var house = await CreateHouseAsync(company, price: 4000);
        var c = AuthedClient(company.OwnerToken);
        var url = $"/api/stays/companies/{company.Id}/bookings";

        var quote = await c.PostJsonAsync(url + "/quote", new StaffStayQuoteInput(house.Id, InDays(10), InDays(11), 2, 0, 0, false));
        var qDto = (await quote.Content.ReadJsonAsync<StayQuoteDto>())!;
        qDto.Problems.Should().NotContain(p => p.Code == StayRefusalCode.MinNightsNotMet, "минимум ночей для ручной брони не действует");

        var manual = await c.PostJsonAsync(url, new ManualStayBookingInput(house.Id, InDays(10), InDays(11), 2, 0, 0, false, "Звонок с Авито", null, false, 3500, "по телефону"));
        manual.StatusCode.Should().Be(HttpStatusCode.Created, await manual.Content.ReadAsStringAsync());
        var card = (await manual.Content.ReadJsonAsync<StaffStayBookingCardDto>())!;
        card.Status.Should().Be(StayBookingStatus.Confirmed);
        card.IsManual.Should().BeTrue();
        card.PrepayRub.Should().Be(0);
        card.TotalRub.Should().Be(3500, "итог можно исправить вручную");
        card.Lines.Should().ContainSingle(l => l.Kind == StayChargeKind.ManualTotal && l.Label == "Итог изменён вручную");
        card.GuestPhone.Should().BeNull("телефон необязателен");

        // те же проверки занятости
        var clash = await c.PostJsonAsync(url, new ManualStayBookingInput(house.Id, InDays(10), InDays(12), 2, 0, 0, false, "Ещё один", null, false, null, null));
        clash.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(clash)).Should().Be("DatesUnavailable");
        // «уведомить гостя» без телефона
        var notify = await c.PostJsonAsync(url, new ManualStayBookingInput(house.Id, InDays(30), InDays(31), 2, 0, 0, false, "Без телефона", null, true, null, null));
        notify.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await notify.Content.ReadAsStringAsync()).Should().Be("Чтобы уведомить гостя, укажите телефон");
        // гость не может занять те же ночи
        var q = await QuoteAsync(house.Id, InDays(10), InDays(13));
        q.Problems.Should().Contain(p => p.Code == StayRefusalCode.DatesUnavailable);
        // итог вне допустимого
        var big = await c.PostJsonAsync(url, new ManualStayBookingInput(house.Id, InDays(40), InDays(41), 2, 0, 0, false, "Много", null, false, 10_000_001, null));
        big.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CY37-55")]
    public async Task Blocks_ConflictWithBooking_TouchingAllowed_PastForbidden_CommentHiddenFromPublic()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var manager = await AddStaffAsync(company, "Manager");
        var housekeeper = await AddStaffAsync(company, "Housekeeper");
        var booked = await BookOkAsync(house.Id, InDays(10), InDays(13));

        // поверх активной брони — конфликт с указанием брони
        var onTop = await PostBlockAsync(company, house.Id, InDays(12), InDays(15));
        onTop.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflict = await J(onTop);
        conflict.GetProperty("code").GetString().Should().Be("BlockConflictsWithBooking");
        conflict.GetProperty("conflicts").GetArrayLength().Should().Be(1);
        conflict.GetProperty("conflicts")[0].GetProperty("guestName").GetString().Should().Be("Пётр Гость");

        // соприкасающиеся блокировки допустимы, пересекающиеся — нет
        var b1 = await BlockAsync(company, house.Id, InDays(13), InDays(16), HouseBlockKind.Repair, "Секретная причина ремонта", manager.Token);
        var touching = await BlockAsync(company, house.Id, InDays(16), InDays(18), HouseBlockKind.Personal);
        var overlapping = await PostBlockAsync(company, house.Id, InDays(17), InDays(19));
        overlapping.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Code(overlapping)).Should().Be("BlockOverlapsBlock");

        // прошлое, перевёрнутый диапазон, длинный комментарий — 400
        var past = await PostBlockAsync(company, house.Id, InDays(-3), InDays(-1));
        past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await past.Content.ReadAsStringAsync()).Should().Be("Нельзя блокировать прошедшие даты");
        (await PostBlockAsync(company, house.Id, InDays(30), InDays(30))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostBlockAsync(company, house.Id, InDays(30), InDays(31), comment: new string('к', 301))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostBlockAsync(company, house.Id, InDays(30), InDays(31), token: housekeeper.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // гость не может занять заблокированные ночи; публичный календарь — «Занято», причины нет
        var q = await QuoteAsync(house.Id, InDays(14), InDays(15));
        q.Problems.Should().Contain(p => p.Code == StayRefusalCode.DatesUnavailable);
        var cal = await AnonymousClient().GetStringAsync($"/api/stays/public/houses/{house.Id}/calendar?from={D(InDays(12))}&to={D(InDays(20))}");
        cal.Should().NotContain("Секретная причина").And.NotContain("Repair").And.NotContain("Ремонт");
        (await CalendarAsync(house.Id, InDays(12), InDays(20))).Days.Single(d => d.Date == InDays(14)).State.Should().Be(CalendarDayState.Occupied);

        // шахматка показывает блокировку персоналу с типом
        var board = await J(await AuthedClient(manager.Token).GetAsync($"/api/stays/companies/{company.Id}/board"));
        board.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("kind").GetString() == "Block" && i.GetProperty("blockKind").GetString() == "Repair");

        // изменение и удаление блокировки: после удаления ночи свободны
        var upd = await AuthedClient(company.OwnerToken).PutJsonAsync($"/api/stays/companies/{company.Id}/blocks/{b1.Id}", new HouseBlockInput(house.Id, InDays(13), InDays(14), HouseBlockKind.Other, null));
        upd.StatusCode.Should().Be(HttpStatusCode.OK, await upd.Content.ReadAsStringAsync());
        (await AuthedClient(company.OwnerToken).DeleteAsync($"/api/stays/companies/{company.Id}/blocks/{touching.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CalendarAsync(house.Id, InDays(12), InDays(20))).Days.Single(d => d.Date == InDays(17)).State.Should().Be(CalendarDayState.Free);
        _ = booked;
    }

    [Fact, TestCase("CY37-56")]
    public async Task Calendar_StatesAndPrivacy_NoNamesNoStatusesOfOthers_UnavailableForNoPriceAndPast()
    {
        var company = await CreateStaysCompanyAsync(settings: s => s with { HorizonDays = 30 });
        var house = await CreateHouseAsync(company, price: 2500);
        var held = await BookOkAsync(house.Id, InDays(5), InDays(6), name: "Секретное Имя Гостя", phone: "+79051112233");
        var awaiting = await BookOkAsync(house.Id, InDays(8), InDays(9));
        await AttachProofOkAsync(awaiting.Token);
        var raw = await AnonymousClient().GetStringAsync($"/api/stays/public/houses/{house.Id}/calendar");
        raw.Should().NotContain("Секретное").And.NotContain("9051112233").And.NotContain("AwaitingPaymentCheck").And.NotContain("Confirmed").And.NotContain("guestName");

        var cal = await CalendarAsync(house.Id);
        cal.Today.Should().Be(InDays(0));
        cal.Days.Single(d => d.Date == InDays(-0)).State.Should().BeOneOf(CalendarDayState.Free, CalendarDayState.Unavailable);
        cal.Days.Single(d => d.Date == InDays(5)).State.Should().Be(CalendarDayState.MayFreeUp);
        cal.Days.Single(d => d.Date == InDays(8)).State.Should().Be(CalendarDayState.Occupied);
        cal.Days.Single(d => d.Date == InDays(7)).State.Should().Be(CalendarDayState.Free);
        cal.Days.Single(d => d.Date == InDays(7)).PriceRub.Should().Be(2500);
        cal.Days.Last().Date.Should().BeOnOrBefore(cal.LastNight.AddDays(1));
        // прошлое и за горизонтом — «Недоступно»
        var wide = await CalendarAsync(house.Id, InDays(-3), InDays(60));
        wide.Days.Where(d => d.Date < InDays(0)).Should().OnlyContain(d => d.State == CalendarDayState.Unavailable);
        wide.Days.Where(d => d.Date > cal.LastNight).Should().OnlyContain(d => d.State == CalendarDayState.Unavailable);
        _ = held;

        // неверный период и неизвестный дом
        (await AnonymousClient().GetAsync($"/api/stays/public/houses/{house.Id}/calendar?from={D(InDays(10))}&to={D(InDays(5))}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AnonymousClient().GetAsync($"/api/stays/public/houses/{Guid.NewGuid()}/calendar")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
