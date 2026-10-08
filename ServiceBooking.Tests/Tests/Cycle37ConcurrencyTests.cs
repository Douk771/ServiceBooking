using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 37, «Вызов 2»: целостность — главный риск продукта (SPEC §6, ARCHITECTURE_CYCLE37.md §37.5.4). Двойная бронь одной ночи одного дома
/// невозможна при параллельных запросах (гость ↔ гость, гость ↔ блокировка владельца, гость ↔ ручная бронь), гонка «истёк таймер / загружено
/// подтверждение» даёт ровно один исход (поддельные часы <see cref="FakeStaysClock"/>), истёкшее, но не снятое удержание не мешает новой брони.
/// </summary>
public class Cycle37ConcurrencyTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    // ── параллельные брони ─────────────────────────────────────────────────────

    [Fact, TestCase("CY37-60")]
    public async Task TwentyParallelGuestBookings_SameDates_ExactlyOneCreated_OthersJson409DatesUnavailable()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 3000);
        var ci = InDays(14);
        var co = InDays(17);
        var total = (await QuoteAsync(house.Id, ci, co)).TotalRub;

        var gate = new TaskCompletionSource();
        var calls = Enumerable.Range(0, 20).Select(async _ =>
        {
            await gate.Task;
            return await PostBookingAsync(house.Id, Booking(ci, co, total)); // свой телефон и свой ключ у каждого
        }).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(calls);

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "создана ровно одна бронь");
        var refused = results.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        refused.Should().HaveCount(19).And.OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var r in refused) (await Code(r)).Should().Be("DatesUnavailable");

        await AssertNoOverlapAsync(house.Id);
        (await WithDbAsync(db => db.StayBookings.CountAsync(b => b.HouseId == house.Id))).Should().Be(1);
        (await WithDbAsync(db => db.HouseOccupancies.CountAsync(o => o.HouseId == house.Id && o.ReleasedAtUtc == null))).Should().Be(1);
    }

    [Fact, TestCase("CY37-61")]
    public async Task ParallelGuestAndOwnerBlock_ExactlyOneSideWins_RepeatedRounds()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 3000);
        var outcomes = new List<string>();
        for (var round = 0; round < 6; round++)
        {
            var ci = InDays(10 + round * 4);
            var co = ci.AddDays(2);
            var total = (await QuoteAsync(house.Id, ci, co)).TotalRub;
            var gate = new TaskCompletionSource();
            var guest = Task.Run(async () => { await gate.Task; return await PostBookingAsync(house.Id, Booking(ci, co, total)); });
            var block = Task.Run(async () => { await gate.Task; return await PostBlockAsync(company, house.Id, ci, co); });
            gate.SetResult();
            var (g, b) = (await guest, await block);

            var guestWon = g.StatusCode == HttpStatusCode.Created;
            var blockWon = b.StatusCode == HttpStatusCode.Created;
            (guestWon ^ blockWon).Should().BeTrue($"раунд {round}: ровно одна сторона успешна, а было guest={g.StatusCode} block={b.StatusCode}");
            if (!guestWon) (await Code(g)).Should().Be("DatesUnavailable");
            if (!blockWon) (await Code(b)).Should().Be("BlockConflictsWithBooking");
            outcomes.Add(guestWon ? "guest" : "block");
        }
        await AssertNoOverlapAsync(house.Id);
        outcomes.Should().NotBeEmpty();
    }

    [Fact, TestCase("CY37-62")]
    public async Task ParallelGuestAndManualBooking_ExactlyOneSideWins_RepeatedRounds()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 3000);
        for (var round = 0; round < 6; round++)
        {
            var ci = InDays(10 + round * 4);
            var co = ci.AddDays(2);
            var total = (await QuoteAsync(house.Id, ci, co)).TotalRub;
            var gate = new TaskCompletionSource();
            var guest = Task.Run(async () => { await gate.Task; return await PostBookingAsync(house.Id, Booking(ci, co, total)); });
            var manual = Task.Run(async () =>
            {
                await gate.Task;
                return await AuthedClient(company.OwnerToken).PostJsonAsync($"/api/stays/companies/{company.Id}/bookings",
                    new ManualStayBookingInput(house.Id, ci, co, 2, 0, 0, false, "Звонок", null, false, null, null));
            });
            gate.SetResult();
            var (g, m) = (await guest, await manual);
            ((g.StatusCode == HttpStatusCode.Created) ^ (m.StatusCode == HttpStatusCode.Created)).Should().BeTrue($"раунд {round}: guest={g.StatusCode} manual={m.StatusCode}");
        }
        await AssertNoOverlapAsync(house.Id);
    }

    [Fact, TestCase("CY37-63")]
    public async Task ParallelOverlappingRanges_NeverShareANight_AdjacentRangesBothAllowed()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 1000);
        var d = InDays(20);
        // [0,2) [1,3) [2,4) [3,5): соседние (0-2 и 2-4, 1-3 и 3-5) совместимы, пересекающиеся — нет
        var ranges = new[] { (0, 2), (1, 3), (2, 4), (3, 5), (0, 2), (2, 4) };
        var gate = new TaskCompletionSource();
        var calls = ranges.Select(r => Task.Run(async () =>
        {
            await gate.Task;
            var q = (await QuoteAsync(house.Id, d.AddDays(r.Item1), d.AddDays(r.Item2))).TotalRub;
            return await PostBookingAsync(house.Id, Booking(d.AddDays(r.Item1), d.AddDays(r.Item2), q));
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(calls);
        results.Select(r => r.StatusCode).Should().OnlyContain(c => c == HttpStatusCode.Created || c == HttpStatusCode.Conflict);
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().BeInRange(1, 2);
        await AssertNoOverlapAsync(house.Id);
    }

    [Fact, TestCase("CY37-64")]
    public async Task ParallelBookings_OfDifferentHouses_DoNotBlockEachOther()
    {
        var company = await CreateStaysCompanyAsync();
        var houses = new List<HouseCtx>();
        for (var i = 0; i < 4; i++) houses.Add(await CreateHouseAsync(company, price: 1500));
        var ci = InDays(12);
        var co = InDays(14);
        var gate = new TaskCompletionSource();
        var calls = houses.SelectMany(h => Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            var q = (await QuoteAsync(h.Id, ci, co)).TotalRub;
            return (House: h.Id, Response: await PostBookingAsync(h.Id, Booking(ci, co, q)));
        }))).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(calls);
        foreach (var byHouse in results.GroupBy(r => r.House))
            byHouse.Count(r => r.Response.StatusCode == HttpStatusCode.Created).Should().Be(1, "по одной брони на каждый дом");
    }

    [Fact, TestCase("CY37-65")]
    public async Task DatabaseConstraint_StopsOverlap_EvenWithoutTheApplicationLock()
    {
        // Уровень 1 защиты отдельно от уровня 2 (ARCHITECTURE_CYCLE37.md §37.5.1): параллельные вставки пересекающихся периодов прямо в БД,
        // минуя advisory-lock и правила приложения, — ограничение EX_HouseOccupancies_NoOverlap не пускает вторую.
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 1000);
        var a = await BookOkAsync(house.Id, InDays(30), InDays(32)); // фиксируем строки bookings, чтобы взять готовую занятость
        _ = a;
        var start = InDays(40);

        async Task<bool> InsertAsync(int offset)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var block = new HouseBlock
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, HouseId = house.Id, StartDate = start.AddDays(offset), EndDate = start.AddDays(offset + 3),
                Kind = HouseBlockKind.Other, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, CreatedByUserId = company.Owner.UserId,
            };
            db.HouseBlocks.Add(block);
            db.HouseOccupancies.Add(new HouseOccupancy
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, HouseId = house.Id, StartDate = block.StartDate, EndDate = block.EndDate,
                Source = OccupancySource.OwnerBlock, HouseBlockId = block.Id, CreatedAtUtc = DateTime.UtcNow,
            });
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23P01" }) { return false; }
        }

        var gate = new TaskCompletionSource();
        var inserts = Enumerable.Range(0, 8).Select(i => Task.Run(async () => { await gate.Task; return await InsertAsync(i % 3); })).ToList();
        gate.SetResult();
        var ok = await Task.WhenAll(inserts);
        ok.Count(x => x).Should().Be(1, "все 8 периодов пересекаются между собой (сдвиг 0..2 при длине 3) — вставка удаётся только одному");
        await AssertNoOverlapAsync(house.Id);
    }

    private async Task AssertNoOverlapAsync(Guid houseId)
    {
        var rows = await WithDbAsync(db => db.HouseOccupancies.AsNoTracking().Where(o => o.HouseId == houseId && o.ReleasedAtUtc == null)
            .Select(o => new { o.StartDate, o.EndDate }).ToListAsync());
        for (var i = 0; i < rows.Count; i++)
            for (var j = i + 1; j < rows.Count; j++)
                (rows[i].StartDate < rows[j].EndDate && rows[j].StartDate < rows[i].EndDate).Should().BeFalse(
                    $"две неосвобождённые занятости с общей ночью: {rows[i]} и {rows[j]}");
    }

    // ── двойное нажатие и лимиты ───────────────────────────────────────────────

    [Fact, TestCase("CY37-66")]
    public async Task DoubleClick_WithRealPhoneLimits_SecondRequestGetsExistingBooking_Not429Not409()
    {
        await using var host = new StaysTestFactory(ConnectionString, phoneLimits: true);
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var client = host.Client();
        var ci = InDays(10);
        var co = InDays(12);
        var total = (await QuoteAsync(house.Id, ci, co, client: client)).TotalRub;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var input = Booking(ci.AddDays(attempt * 5), co.AddDays(attempt * 5), total, key: Guid.NewGuid());
            // один человек, один ключ, два клика подряд почти одновременно
            var gate = new TaskCompletionSource();
            var calls = Enumerable.Range(0, 2).Select(_ => Task.Run(async () => { await gate.Task; return await PostBookingAsync(house.Id, input, client); })).ToList();
            gate.SetResult();
            var results = await Task.WhenAll(calls);
            // первое удержание этого номера в компании: после каждого цикла отменяем, чтобы лимит «одно удержание» не вмешивался
            results.Select(r => r.StatusCode).Should().OnlyContain(c => c == HttpStatusCode.Created || c == HttpStatusCode.OK,
                "повтор запроса с тем же ключом — существующая бронь, а не отказ по лимиту и не «даты заняты»: " +
                string.Join(" | ", await Task.WhenAll(results.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}"))));
            var tokens = await Task.WhenAll(results.Select(async r => (await r.Content.ReadJsonAsync<CreateStayBookingResponse>())!.Token));
            tokens.Distinct().Should().ContainSingle();
            (await client.PostAsJsonNullAsync($"/api/stays/bookings/public/{tokens[0]}/cancel")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact, TestCase("CY37-67")]
    public async Task PhoneLimits_OneHeldPerCompany_TwoOnPlatform_TenPerDay_WithRussianTexts()
    {
        await using var host = new StaysTestFactory(ConnectionString, phoneLimits: true);
        var client = host.Client();
        var phone = UniquePhone();
        var a = await CreateStaysCompanyAsync();
        var b = await CreateStaysCompanyAsync();
        var c = await CreateStaysCompanyAsync();
        var ha = await CreateHouseAsync(a, price: 1000);
        var ha2 = await CreateHouseAsync(a, price: 1000);
        var hb = await CreateHouseAsync(b, price: 1000);
        var hc = await CreateHouseAsync(c, price: 1000);

        async Task<HttpResponseMessage> Try(Guid houseId, int dayShift)
        {
            var q = (await QuoteAsync(houseId, InDays(10 + dayShift), InDays(11 + dayShift), client: client)).TotalRub;
            return await PostBookingAsync(houseId, Booking(InDays(10 + dayShift), InDays(11 + dayShift), q, phone), client);
        }

        var first = await Try(ha.Id, 0);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await Try(ha2.Id, 0); // та же компания — второе удержание
        second.StatusCode.Should().Be((HttpStatusCode)429);
        (await second.Content.ReadAsStringAsync()).Should().Be("Слишком много неоплаченных броней. Оплатите или отмените текущую бронь");
        (await Try(hb.Id, 0)).StatusCode.Should().Be(HttpStatusCode.Created, "другая компания — второе удержание на платформе");
        var third = await Try(hc.Id, 0);
        third.StatusCode.Should().Be((HttpStatusCode)429, "не больше двух удержаний на номер на платформе");

        // отмена освобождает лимит
        var token = (await first.Content.ReadJsonAsync<CreateStayBookingResponse>())!.Token;
        (await client.PostAsJsonNullAsync($"/api/stays/bookings/public/{token}/cancel")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Try(hc.Id, 0)).StatusCode.Should().Be(HttpStatusCode.Created);

        // лимит «10 созданий в сутки на номер»: создаём и отменяем, пока не упрёмся
        var daily = UniquePhone();
        HttpResponseMessage? refused = null;
        for (var i = 0; i < 12 && refused is null; i++)
        {
            var q = (await QuoteAsync(ha.Id, InDays(40 + i), InDays(41 + i), client: client)).TotalRub;
            var r = await PostBookingAsync(ha.Id, Booking(InDays(40 + i), InDays(41 + i), q, daily), client);
            if (r.StatusCode == HttpStatusCode.Created)
                (await client.PostAsJsonNullAsync($"/api/stays/bookings/public/{(await r.Content.ReadJsonAsync<CreateStayBookingResponse>())!.Token}/cancel")).StatusCode.Should().Be(HttpStatusCode.OK);
            else refused = r;
        }
        refused.Should().NotBeNull("не больше 10 созданий броней в сутки на один номер");
        refused!.StatusCode.Should().Be((HttpStatusCode)429);
        (await refused.Content.ReadAsStringAsync()).Should().Be("Слишком много броней с этого номера. Попробуйте позже");
    }

    [Fact, TestCase("CY37-68")]
    public async Task IpRateLimit_AnonymousBookingCreation_429WithText()
    {
        await using var host = new StaysTestFactory(ConnectionString, stayCreateAnonymousPermits: 2);
        var client = host.Client();
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 1000);
        var statuses = new List<HttpStatusCode>();
        string? text = null;
        for (var i = 0; i < 4; i++)
        {
            var q = (await QuoteAsync(house.Id, InDays(10 + i * 2), InDays(11 + i * 2), client: client)).TotalRub;
            var r = await PostBookingAsync(house.Id, Booking(InDays(10 + i * 2), InDays(11 + i * 2), q), client);
            statuses.Add(r.StatusCode);
            if (r.StatusCode == (HttpStatusCode)429) text = await r.Content.ReadAsStringAsync();
        }
        statuses.Should().Contain((HttpStatusCode)429, "с одного адреса не больше N созданий в окно: " + string.Join(",", statuses));
        text.Should().Be("Слишком много попыток. Попробуйте позже");
    }

    // ── часы: гонка «таймер / подтверждение оплаты» (поддельные часы) ─────────────

    private sealed record Held(string Token, Guid BookingId, DateTime HoldExpiresAtUtc, string Phone);

    private async Task<(Held Booking, HouseCtx House, StaysCtx Company)> HeldBookingAsync(StaysTestFactory host, int dayShift = 0, StaysCtx? company = null, HouseCtx? house = null)
    {
        company ??= await CreateStaysCompanyAsync();
        house ??= await CreateHouseAsync(company, price: 2000);
        var created = await BookOkAsync(house.Id, InDays(10 + dayShift), InDays(12 + dayShift), client: host.Client());
        var id = await BookingIdAsync(created.Token);
        var expires = (await WithDbAsync(db => db.StayBookings.AsNoTracking().Where(b => b.Id == id).Select(b => b.HoldExpiresAtUtc).SingleAsync()))!.Value;
        return (new Held(created.Token, id, expires, "+79001112233"), house, company);
    }

    private async Task<StayBookingStatus> StatusAsync(Guid bookingId) =>
        await WithDbAsync(db => db.StayBookings.AsNoTracking().Where(b => b.Id == bookingId).Select(b => b.Status).SingleAsync());

    private async Task<List<StayBookingEventKind>> EventsAsync(Guid bookingId) =>
        await WithDbAsync(db => db.StayBookingEvents.AsNoTracking().Where(e => e.StayBookingId == bookingId).OrderBy(e => e.OccurredAtUtc).Select(e => e.Kind).ToListAsync());

    [Theory, TestCase("CY37-69")]
    [InlineData(-1000, true)]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1000, false)]
    public async Task ProofUpload_AtHoldBoundary_ExactlyOneOutcome(int offsetMs, bool proofAccepted)
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (held, house, _) = await HeldBookingAsync(host);
        host.StaysClock.Set(held.HoldExpiresAtUtc.AddMilliseconds(offsetMs));

        var response = await AttachProofAsync(held.Token, host.Client());
        if (proofAccepted)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            var page = (await response.Content.ReadJsonAsync<PublicStayBookingDto>())!;
            page.Status.Should().Be(StayBookingStatus.AwaitingPaymentCheck);
            page.HoldExpiresAtUtc.Should().BeNull();
            (await StatusAsync(held.BookingId)).Should().Be(StayBookingStatus.AwaitingPaymentCheck);
            (await EventsAsync(held.BookingId)).Should().Equal(StayBookingEventKind.Created, StayBookingEventKind.PaymentProofUploaded);
            await host.RunTaskAsync("stays-hold-expiry"); // задача общая на БД класса: проверяем только нашу бронь
            (await StatusAsync(held.BookingId)).Should().Be(StayBookingStatus.AwaitingPaymentCheck);
            (await CalendarAsync(house.Id, InDays(9), InDays(14), host.Client())).Days.Single(d => d.Date == InDays(10)).State.Should().Be(CalendarDayState.Occupied);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await J(response);
            body.GetProperty("code").GetString().Should().Be("HoldExpired");
            body.GetProperty("message").GetString().Should().Be("Время на оплату истекло, бронь снята. Если вы уже оплатили — свяжитесь с компанией: +79001112233");
            body.GetProperty("booking").GetProperty("status").GetString().Should().Be("ExpiredUnpaid", "в теле — уже снятая бронь");
            (await StatusAsync(held.BookingId)).Should().Be(StayBookingStatus.ExpiredUnpaid);
            (await EventsAsync(held.BookingId)).Should().Equal(StayBookingEventKind.Created, StayBookingEventKind.HoldExpired);
            (await WithDbAsync(db => db.StayPaymentProofs.CountAsync(p => p.StayBookingId == held.BookingId))).Should().Be(0, "файл не принят");
            await host.RunTaskAsync("stays-hold-expiry"); // повторное снятие не делает второго события
            (await EventsAsync(held.BookingId)).Count(k => k == StayBookingEventKind.HoldExpired).Should().Be(1);
            (await CalendarAsync(house.Id, InDays(9), InDays(14), host.Client())).Days.Single(d => d.Date == InDays(10)).State.Should().Be(CalendarDayState.Free);
        }
    }

    [Theory, TestCase("CY37-70")]
    [InlineData(-1000)]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task ProofUploadAndExpiryTask_RunTogether_AtBoundary_ExactlyOneOutcomeAndJournalAgrees(int offsetMs)
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 2000);
        var rounds = new List<Held>();
        for (var i = 0; i < 6; i++) rounds.Add((await HeldBookingAsync(host, dayShift: i * 3, company: company, house: house)).Booking);

        // все удержания одной минуты: часы ставим по самому позднему
        host.StaysClock.Set(rounds.Max(r => r.HoldExpiresAtUtc).AddMilliseconds(offsetMs));
        var gate = new TaskCompletionSource();
        var uploads = rounds.Select(r => Task.Run(async () => { await gate.Task; return await AttachProofAsync(r.Token, host.Client()); })).ToList();
        var task = Task.Run(async () => { await gate.Task; return await host.RunTaskAsync("stays-hold-expiry"); });
        gate.SetResult();
        var responses = await Task.WhenAll(uploads);
        await task;

        for (var i = 0; i < rounds.Count; i++)
        {
            var status = await StatusAsync(rounds[i].BookingId);
            var events = await EventsAsync(rounds[i].BookingId);
            var proofs = await WithDbAsync(db => db.StayPaymentProofs.CountAsync(p => p.StayBookingId == rounds[i].BookingId));
            if (responses[i].StatusCode == HttpStatusCode.Created)
            {
                status.Should().Be(StayBookingStatus.AwaitingPaymentCheck, $"бронь {i}: файл принят");
                proofs.Should().Be(1);
                events.Should().Contain(StayBookingEventKind.PaymentProofUploaded).And.NotContain(StayBookingEventKind.HoldExpired);
            }
            else
            {
                responses[i].StatusCode.Should().Be(HttpStatusCode.Conflict);
                status.Should().Be(StayBookingStatus.ExpiredUnpaid, $"бронь {i}: время вышло");
                proofs.Should().Be(0);
                events.Should().Contain(StayBookingEventKind.HoldExpired).And.NotContain(StayBookingEventKind.PaymentProofUploaded);
                events.Count(k => k == StayBookingEventKind.HoldExpired).Should().Be(1);
            }
        }
        // инвариант: статус и строка занятости согласованы (удержана/ожидает — занята, снята — освобождена)
        var inconsistent = await WithDbAsync(async db =>
        {
            var occupancies = await db.HouseOccupancies.AsNoTracking().Where(o => o.HouseId == house.Id && o.StayBookingId != null).ToListAsync();
            var statuses = await db.StayBookings.AsNoTracking().Where(b => b.HouseId == house.Id).ToDictionaryAsync(b => b.Id, b => b.Status);
            return occupancies.Count(o => (o.ReleasedAtUtc == null) == (statuses[o.StayBookingId!.Value] == StayBookingStatus.ExpiredUnpaid));
        });
        inconsistent.Should().Be(0, "занятость освобождена ⇔ бронь в конечном статусе");
    }

    [Fact, TestCase("CY37-71")]
    public async Task ExpiredButUnprocessedHold_DoesNotBlockNewBooking_AndIsLazilyReleased()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (held, house, _) = await HeldBookingAsync(host);
        var client = host.Client();

        host.StaysClock.Set(held.HoldExpiresAtUtc.AddSeconds(1)); // таймер истёк, фоновая задача ещё не работала
        (await StatusAsync(held.BookingId)).Should().Be(StayBookingStatus.Held, "задача ещё не отработала");

        var cal = await CalendarAsync(house.Id, InDays(9), InDays(14), client);
        cal.Days.Single(d => d.Date == InDays(10)).State.Should().Be(CalendarDayState.Free, "публичный календарь перестаёт показывать «Возможно освободится»");
        var q = await QuoteAsync(house.Id, InDays(10), InDays(12), client: client);
        q.Ok.Should().BeTrue("расчёт видит истёкшее удержание свободным: " + string.Join("; ", q.Problems.Select(p => p.Message)));
        var catalog = await J(await client.GetAsync($"/api/stays/public/catalog?checkIn={D(InDays(10))}&checkOut={D(InDays(12))}&pageSize=50"));
        catalog.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("houseId").GetGuid()).Should().Contain(house.Id);

        var second = await BookOkAsync(house.Id, InDays(10), InDays(12), client: client);
        second.Booking.Status.Should().Be(StayBookingStatus.Held, "новая бронь не ждёт фоновой задачи");

        (await StatusAsync(held.BookingId)).Should().Be(StayBookingStatus.ExpiredUnpaid, "старая снята тем же кодом, что и задача");
        (await EventsAsync(held.BookingId)).Should().Contain(StayBookingEventKind.HoldExpired);
        (await WithDbAsync(db => db.HouseOccupancies.CountAsync(o => o.HouseId == house.Id && o.ReleasedAtUtc == null))).Should().Be(1);
        (await GetPublicBookingAsync(held.Token, client)).StatusText.Should().Be("Снята: не оплачена");
        await host.RunTaskAsync("stays-hold-expiry");
        (await EventsAsync(held.BookingId)).Count(k => k == StayBookingEventKind.HoldExpired).Should().Be(1, "задача не снимает уже снятое вторично");
    }

    [Fact, TestCase("CY37-72")]
    public async Task ExpiryTask_ReleasesDue_InBatches_WithinOneMinuteOfExpiry_Idempotent()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var (held, house, company) = await HeldBookingAsync(host);
        host.StaysClock.Advance(TimeSpan.FromMinutes(10)); // вторая бронь создана на 10 минут позже — её таймер истекает позже
        var (second, _, _) = await HeldBookingAsync(host, dayShift: 5, company: company, house: house);

        using (var scope = host.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduledTask>().First(t => t.Name == "stays-hold-expiry");
            task.DefaultPeriod.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(1), "снятие не позже чем через минуту после истечения (US-37-21)");
        }

        // задача общая на БД класса (в ней могут лежать удержания других тестов), поэтому смотрим только на свои брони
        host.StaysClock.Set(held.HoldExpiresAtUtc.AddSeconds(-5));
        await host.RunTaskAsync("stays-hold-expiry");
        (await StatusAsync(held.BookingId)).Should().Be(StayBookingStatus.Held, "до истечения ничего не снимается");
        (await StatusAsync(second.BookingId)).Should().Be(StayBookingStatus.Held);
        host.StaysClock.Set(held.HoldExpiresAtUtc.AddSeconds(1));
        await host.RunTaskAsync("stays-hold-expiry");
        (await StatusAsync(held.BookingId)).Should().Be(StayBookingStatus.ExpiredUnpaid);
        (await StatusAsync(second.BookingId)).Should().Be(StayBookingStatus.Held, "у второй брони таймер ещё идёт");
        host.StaysClock.Set(second.HoldExpiresAtUtc.AddSeconds(1));
        await host.RunTaskAsync("stays-hold-expiry");
        await host.RunTaskAsync("stays-hold-expiry"); // повторный проход
        foreach (var id in new[] { held.BookingId, second.BookingId })
            (await EventsAsync(id)).Count(k => k == StayBookingEventKind.HoldExpired).Should().Be(1, "каждое удержание снимается ровно один раз");
        (await StatusAsync(held.BookingId)).Should().Be(StayBookingStatus.ExpiredUnpaid);
        (await StatusAsync(second.BookingId)).Should().Be(StayBookingStatus.ExpiredUnpaid);
        (await CalendarAsync(house.Id, InDays(9), InDays(20), host.Client())).Days.Should().OnlyContain(d => d.State == CalendarDayState.Free);
        var page = await GetPublicBookingAsync(held.Token, host.Client());
        page.StatusText.Should().Be("Снята: не оплачена");
        page.OutcomeText.Should().Contain("Время на оплату истекло, бронь снята").And.Contain("свяжитесь с компанией");
        page.Payment.Should().BeNull();
    }

    [Fact, TestCase("CY37-73")]
    public async Task GuestCancel_OfExpiredHold_IsRefused_AndAfterCheckInTime_ShowsCompanyPhone()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var client = host.Client();
        var (held, house, company) = await HeldBookingAsync(host);

        host.StaysClock.Set(held.HoldExpiresAtUtc.AddSeconds(1));
        var late = await client.PostAsJsonNullAsync($"/api/stays/bookings/public/{held.Token}/cancel");
        late.StatusCode.Should().Be(HttpStatusCode.Conflict, "таймер истёк: отменять уже нечего");
        (await J(late)).GetProperty("code").GetString().Should().Be("CancelNotAllowed");

        // подтверждённая бронь: до времени заезда отмена есть, после — нет
        var confirmed = await BookOkAsync(house.Id, InDays(20), InDays(22), client: client);
        var id = await BookingIdAsync(confirmed.Token);
        await AttachProofOkAsync(confirmed.Token, client);
        await StaffActionOkAsync(company, id, "confirm-payment", (await StaffCardAsync(company, id)).Version);
        host.StaysClock.Set(LocalToUtc(InDays(20), 14, 0).AddSeconds(-1));
        var before = await GetPublicBookingAsync(confirmed.Token, client);
        before.Cancellation.CanCancel.Should().BeTrue();
        host.StaysClock.Set(LocalToUtc(InDays(20), 14, 0));
        var after = await GetPublicBookingAsync(confirmed.Token, client);
        after.Cancellation.CanCancel.Should().BeFalse("после времени заезда кнопки нет");
        after.Cancellation.CannotCancelText.Should().Be("Время заезда наступило — по вопросам отмены свяжитесь с компанией: " + company.Company.Phone);
        var refused = await client.PostAsJsonNullAsync($"/api/stays/bookings/public/{confirmed.Token}/cancel");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await StatusAsync(id)).Should().Be(StayBookingStatus.Confirmed);
    }

    // ── возврат по трём шаблонам (ЮР-1) ─────────────────────────────────────────────

    [Theory, TestCase("CY37-74")]
    [InlineData(StayCancellationPolicy.Standard, "dayBefore", "Full", 1500)]
    [InlineData(StayCancellationPolicy.Standard, "dayStart", "Partial", 500)]
    [InlineData(StayCancellationPolicy.Standard, "beforeCheckIn", "Partial", 500)]
    [InlineData(StayCancellationPolicy.Flexible, "dayStart", "Full", 1500)]
    [InlineData(StayCancellationPolicy.Flexible, "beforeCheckIn", "Full", 1500)]
    [InlineData(StayCancellationPolicy.Flexible, "afterCheckIn", "Partial", 500)]
    [InlineData(StayCancellationPolicy.NoDeductions, "dayStart", "Full", 1500)]
    [InlineData(StayCancellationPolicy.NoDeductions, "afterCheckIn", "Full", 1500)]
    public async Task RefundByTemplate_ShownToGuest_NotLessThan_NeverMoreThanFirstNightDeduction(StayCancellationPolicy policy, string moment, string kind, int refundAtLeast)
    {
        // 5 ночей по 1000 ₽ = 5000, предоплата 30 % = 1500, первая ночь 1000 ₽ -> удержание не больше 1000, к возврату не меньше 500
        await using var host = new StaysTestFactory(ConnectionString);
        var client = host.Client();
        var company = await CreateStaysCompanyAsync(policy: policy);
        var house = await CreateHouseAsync(company, price: 1000);
        var checkIn = InDays(20);
        var booked = await BookOkAsync(house.Id, checkIn, checkIn.AddDays(5), client: client);
        booked.Booking.PrepayRub.Should().Be(1500);
        await AttachProofOkAsync(booked.Token, client);

        host.StaysClock.Set(moment switch
        {
            "dayBefore" => LocalToUtc(checkIn, 0, 0).AddSeconds(-1),
            "dayStart" => LocalToUtc(checkIn, 0, 0),
            "beforeCheckIn" => LocalToUtc(checkIn, 13, 59),
            _ => LocalToUtc(checkIn, 14, 0),
        });
        var refund = (await GetPublicBookingAsync(booked.Token, client)).Cancellation.Refund;
        refund.Kind.ToString().Should().Be(kind);
        refund.RefundAtLeastRub.Should().Be(refundAtLeast);
        if (kind == "Partial")
        {
            refund.MaxDeductionRub.Should().Be(1000, "удержание не больше стоимости первой ночи");
            refund.Text.Should().Contain("К возврату не меньше 500 ₽").And.Contain("не больше 1 000 ₽").And.Contain("стоимость первой ночи");
        }
        else
        {
            refund.MaxDeductionRub.Should().Be(0);
            refund.Text.Should().Contain("К возврату не меньше 1 500 ₽").And.Contain("полностью");
        }
        refund.RefundAtLeastRub.Should().BeGreaterThanOrEqualTo(1500 - 1000, "возврат считается от внесённой предоплаты");

        // до заезда гость реально может отменить, и в конечном итоге доплат/удержаний продукт не считает сам
        if (moment != "afterCheckIn")
        {
            var cancel = await client.PostAsJsonNullAsync($"/api/stays/bookings/public/{booked.Token}/cancel");
            cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact, TestCase("CY37-75")]
    public async Task Refund_CapsDeductionAtPrepayment_WhenFirstNightIsMoreThanPrepay_AndHeldIsNothingPaid_OwnerCancelIsFull()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var client = host.Client();
        var company = await CreateStaysCompanyAsync(policy: StayCancellationPolicy.Standard);
        var house = await CreateHouseAsync(company, price: 10000);
        var checkIn = InDays(20);
        var held = await BookOkAsync(house.Id, checkIn, checkIn.AddDays(2), client: client); // 20 000, предоплата 6 000, первая ночь 10 000 > предоплата
        (await GetPublicBookingAsync(held.Token, client)).Cancellation.Refund.Kind.Should().Be(StayRefundKind.NothingPaid);
        await AttachProofOkAsync(held.Token, client);
        host.StaysClock.Set(LocalToUtc(checkIn, 9, 0));
        var refund = (await GetPublicBookingAsync(held.Token, client)).Cancellation.Refund;
        refund.Kind.Should().Be(StayRefundKind.Partial);
        refund.RefundAtLeastRub.Should().Be(0, "удержание не больше самой предоплаты");
        refund.MaxDeductionRub.Should().Be(6000);
        var id = await BookingIdAsync(held.Token);
        (await StaffCardAsync(company, id)).OwnerCancelRefundText.Should().Contain("6 000 ₽", "отмена владельцем — всегда полный возврат");
    }
}

internal static class StaysHttpExtensions
{
    public static Task<HttpResponseMessage> PostAsJsonNullAsync(this HttpClient client, string url) =>
        client.PostAsync(url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
}
