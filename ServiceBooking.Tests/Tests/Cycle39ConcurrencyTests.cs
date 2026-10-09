using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39, «Вызов 2»: целостность сеансов — главный риск цикла (SPEC §6, ARCHITECTURE_CYCLE39.md §39.5.4). Двойная бронь времени услуги невозможна при параллельных запросах
/// (заказ ↔ заказ, заказ ↔ добавление к брони, через полночь), порядок блокировок «дом → услуга» не даёт 40P01, ленивое снятие истёкшего удержания, гонки «таймер / файл / подтверждение / отмена».
/// </summary>
public class Cycle39ConcurrencyTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private async Task AssertNoOverlapAsync(Guid serviceId)
    {
        var rows = await ActiveSessionsAsync(serviceId);
        for (var i = 0; i < rows.Count; i++)
            for (var j = i + 1; j < rows.Count; j++)
                (rows[i].StartUtc < rows[j].OccupiedUntilUtc && rows[j].StartUtc < rows[i].OccupiedUntilUtc).Should().BeFalse(
                    $"два активных сеанса одной услуги с пересечением [старт, конец+зазор): {rows[i].StartUtc:O}–{rows[i].OccupiedUntilUtc:O} и {rows[j].StartUtc:O}–{rows[j].OccupiedUntilUtc:O}");
    }

    /// <summary>Активный сеанс у неактивного родителя не существует (инварианты 3–4 ARCHITECTURE §39.2.6).</summary>
    private async Task AssertParentsConsistentAsync(Guid serviceId)
    {
        var bad = await WithDbAsync(async db =>
        {
            var brokenBooking = await (from s in db.StayServiceSessions.AsNoTracking()
                                       join b in db.StayBookings.AsNoTracking() on s.StayBookingId equals b.Id
                                       where s.ServiceId == serviceId && s.ReleasedAtUtc == null
                                             && (b.Status == StayBookingStatus.ExpiredUnpaid || b.Status == StayBookingStatus.PaymentRejected
                                                 || b.Status == StayBookingStatus.CancelledByGuest || b.Status == StayBookingStatus.CancelledByOwner)
                                       select s.Id).CountAsync();
            var brokenOrder = await (from s in db.StayServiceSessions.AsNoTracking()
                                     join o in db.StayServiceOrders.AsNoTracking() on s.StayServiceOrderId equals o.Id
                                     where s.ServiceId == serviceId && s.ReleasedAtUtc == null
                                           && (o.Status == StayBookingStatus.ExpiredUnpaid || o.Status == StayBookingStatus.PaymentRejected
                                               || o.Status == StayBookingStatus.CancelledByGuest || o.Status == StayBookingStatus.CancelledByOwner)
                                     select s.Id).CountAsync();
            var releasedButParentLive = await (from s in db.StayServiceSessions.AsNoTracking()
                                               join o in db.StayServiceOrders.AsNoTracking() on s.StayServiceOrderId equals o.Id
                                               where s.ServiceId == serviceId && s.ReleasedAtUtc != null
                                                     && (o.Status == StayBookingStatus.Held || o.Status == StayBookingStatus.AwaitingPaymentCheck || o.Status == StayBookingStatus.Confirmed)
                                                     && s.State != StayServiceSessionState.CancelledByGuest && s.State != StayServiceSessionState.CancelledByOwner
                                               select s.Id).CountAsync();
            return brokenBooking + brokenOrder + releasedButParentLive;
        });
        bad.Should().Be(0, "сеанс активен тогда и только тогда, когда активен его родитель");
    }

    // ── заказы ───────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-90")]
    public async Task TwentyParallelOrders_SameStart_ExactlyOneCreated_OthersSlotTaken()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company);
        var date = InDays(9);
        var gate = new TaskCompletionSource();
        var calls = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await PostOrderAsync(svc.Id, OrderInput(date, 720, 2, 4000, UniquePhone()));
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(calls);
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "создан ровно один заказ");
        var refused = results.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        refused.Should().HaveCount(19).And.OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var r in refused) (await Code(r)).Should().Be("SlotTaken");
        await AssertNoOverlapAsync(svc.Id);
        (await ActiveSessionsAsync(svc.Id)).Should().HaveCount(1);
        (await WithDbAsync(db => db.StayServiceOrders.CountAsync(o => o.ServiceId == svc.Id))).Should().Be(1, "проигравшие запросы не оставляют заказов");
    }

    [Fact, TestCase("CY39-91")]
    public async Task ParallelOverlappingOrders_ShiftedStarts_NeverShareTime_BufferIncluded()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, step: 30, minHours: 1, maxHours: 3, buffer: 30);
        var date = InDays(9);
        var gate = new TaskCompletionSource();
        // 12 запросов с разными стартами внутри 6 часов: часть совместима, часть пересекается
        var starts = new[] { 600, 630, 660, 690, 720, 780, 840, 900, 960, 990, 1020, 1080 };
        var calls = starts.Select(st => Task.Run(async () =>
        {
            await gate.Task;
            return await PostOrderAsync(svc.Id, OrderInput(date, st, 1, 2000, UniquePhone()));
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(calls);
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);
        await AssertNoOverlapAsync(svc.Id);
        (await ActiveSessionsAsync(svc.Id)).Count.Should().Be(results.Count(r => r.StatusCode == HttpStatusCode.Created));
    }

    [Fact, TestCase("CY39-92")]
    public async Task ParallelOrderAndHouseAddition_SameMidnightTime_ExactlyOneWins_ManyRounds()
    {
        // «Пт 23:00–01:00» (заказ без проживания) против «Пт 00:30 (ночь на сб) – 02:30» (добавление к брони дома) — один отрезок реального времени
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var house = await CreateHouseAsync(company, price: 1000);
        var svc = await CreateServiceAsync(company, step: 30, minHours: 1, maxHours: 4, buffer: 0, windows: [(480, 1800)]);
        var outcomes = new List<string>();
        for (var round = 0; round < 8; round++)
        {
            var friday = NextWeekday(DayOfWeek.Friday, 8).AddDays(7 * round);
            var booked = await BookOkAsync(house.Id, friday, friday.AddDays(2));
            var gate = new TaskCompletionSource();
            var order = Task.Run(async () => { await gate.Task; return await PostOrderAsync(svc.Id, OrderInput(friday, 1380, 2, 4000, UniquePhone())); });
            var add = Task.Run(async () => { await gate.Task; return await AddSessionAsync(booked.Token, SessionInput(svc.Id, friday, 1470, 2, 4000)); });
            gate.SetResult();
            var (o, a) = (await order, await add);
            var oWon = o.StatusCode == HttpStatusCode.Created;
            var aWon = a.StatusCode == HttpStatusCode.Created;
            (oWon ^ aWon).Should().BeTrue($"раунд {round}: ровно одна сторона успешна, а было order={o.StatusCode} add={a.StatusCode}");
            if (!oWon) (await Code(o)).Should().Be("SlotTaken");
            if (!aWon) (await Code(a)).Should().Be("SlotTaken");
            outcomes.Add(oWon ? "order" : "add");
        }
        await AssertNoOverlapAsync(svc.Id);
        outcomes.Should().NotBeEmpty();
    }

    [Fact, TestCase("CY39-93")]
    public async Task DatabaseConstraint_Stops_Friday0400to0600_PlusBuffer_And_Saturday0600_WithoutAppLocks()
    {
        // тестовый двойник писателя БЕЗ замков и без проверок приложения: защиту даёт только EXCLUDE-ограничение (A39-3)
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, buffer: 30, step: 30, minHours: 1, windows: [(480, 1800)]);
        var friday = NextWeekday(DayOfWeek.Friday, 10);
        var order = await OrderOkAsync(svc.Id, friday, 1680, 2); // Пт 04:00–06:00, занято до 06:30 субботы
        var house = await CreateHouseAsync(company, price: 1000);
        var parent = await BookingIdAsync((await BookOkAsync(house.Id, friday, friday.AddDays(3))).Token); // родитель для «двойника» (у заказа сеанс может быть только один)
        var original = (await ActiveSessionsAsync(svc.Id)).Single();
        original.OccupiedUntilUtc.Should().Be(StartUtc(friday, 1680).AddHours(2).AddMinutes(30));

        async Task<string?> InsertCloneAsync(DateTime startUtc, int hours)
        {
            var clone = new StayServiceSession
            {
                Id = Guid.NewGuid(), CompanyId = original.CompanyId, ServiceId = original.ServiceId, StayBookingId = parent,
                BusinessDate = original.BusinessDate.AddDays(1), StartMinute = 360, Hours = hours, StartUtc = startUtc, EndUtc = startUtc.AddHours(hours),
                BufferMinutesSnapshot = 30, OccupiedUntilUtc = startUtc.AddHours(hours).AddMinutes(30), ServiceNameSnapshot = "Баня", HourPricesJson = original.HourPricesJson,
                ItemsJson = "[]", TotalRub = 0, ServiceAmountRub = 0, ItemsAmountRub = 0, AddedByKind = original.AddedByKind, Version = 1,
            };
            return await WithDbAsync(async db =>
            {
                db.StayServiceSessions.Add(clone);
                try { await db.SaveChangesAsync(); return null; }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg) { return pg.SqlState; }
            });
        }

        var sat0600 = StartUtc(friday.AddDays(1), 360);
        (await InsertCloneAsync(sat0600, 1)).Should().Be("23P01", "Сб 06:00 попадает в зазор пятничного сеанса: пересечение на [23:00Z, 23:30Z)");
        (await InsertCloneAsync(sat0600.AddMinutes(-30), 1)).Should().Be("23P01", "Сб 05:30 внутри самого сеанса");
        (await InsertCloneAsync(sat0600.AddMinutes(30), 1)).Should().BeNull("Сб 06:30 — ровно конец зазора, пересечения нет");
        await AssertNoOverlapAsync(svc.Id);
        _ = order;
    }

    // ── порядок блокировок ───────────────────────────────────────────────────────

    [Fact, TestCase("CY39-94")]
    public async Task BookingsWithSeveralServices_InReverseOrder_NoDeadlock_NoServerErrors_ManyRounds()
    {
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        var h1 = await CreateHouseAsync(company, price: 1000);
        var h2 = await CreateHouseAsync(company, price: 1000);
        var a = await CreateServiceAsync(company, "Баня", step: 30, minHours: 1);
        var b = await CreateServiceAsync(company, "Чан", step: 30, minHours: 1);
        var statuses = new List<HttpStatusCode>();
        for (var round = 0; round < 15; round++)
        {
            var ci = InDays(10 + round * 3);
            var co = ci.AddDays(2);
            var d = ci.AddDays(1);
            var q1 = (await QuoteAsync(h1.Id, ci, co)).TotalRub;
            var q2 = (await QuoteAsync(h2.Id, ci, co)).TotalRub;
            // дом 1: услуги [A, B]; дом 2: услуги [B, A] — замки услуг берутся по возрастанию Guid независимо от порядка в запросе
            var s1 = new[] { new StayServiceSelectionInput(a.Id, d, 600, 2, []), new StayServiceSelectionInput(b.Id, d, 600, 2, []) };
            var s2 = new[] { new StayServiceSelectionInput(b.Id, d, 900, 2, []), new StayServiceSelectionInput(a.Id, d, 900, 2, []) };
            var gate = new TaskCompletionSource();
            var t1 = Task.Run(async () => { await gate.Task; return await PostBookingAsync(h1.Id, Booking(ci, co, q1 + 8000) with { Services = s1.ToList() }); });
            var t2 = Task.Run(async () => { await gate.Task; return await PostBookingAsync(h2.Id, Booking(ci, co, q2 + 8000) with { Services = s2.ToList() }); });
            var t3 = Task.Run(async () => { await gate.Task; return await PostBookingAsync(h1.Id, Booking(ci.AddDays(0), co, q1 + 8000) with { Services = s2.ToList() }); }); // конфликт дома
            gate.SetResult();
            var rs = await Task.WhenAll(t1, t2, t3);
            statuses.AddRange(rs.Select(r => r.StatusCode));
            rs.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict, $"раунд {round}: " + string.Join(",", rs.Select(r => (int)r.StatusCode)));
        }
        statuses.Should().NotContain(s => (int)s >= 500, "40P01 → 500 не допускается");
        await AssertNoOverlapAsync(a.Id);
        await AssertNoOverlapAsync(b.Id);
        await AssertParentsConsistentAsync(a.Id);
    }

    [Fact, TestCase("CY39-95")]
    public async Task ParallelAddSessions_ToDifferentBookings_OrdersAndTransitions_NoDeadlock()
    {
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        await EnableOrdersWithoutStayAsync(company);
        var houses = new[] { await CreateHouseAsync(company, price: 1000), await CreateHouseAsync(company, price: 1000), await CreateHouseAsync(company, price: 1000) };
        var a = await CreateServiceAsync(company, "Баня", step: 30, minHours: 1);
        var b = await CreateServiceAsync(company, "Чан", step: 30, minHours: 1);
        var statuses = new List<HttpStatusCode>();
        for (var round = 0; round < 10; round++)
        {
            var ci = InDays(10 + round * 4);
            var co = ci.AddDays(3);
            var bookings = new List<(string Token, Guid Id)>();
            foreach (var h in houses)
            {
                var booked = await BookOkAsync(h.Id, ci, co);
                bookings.Add((booked.Token, await BookingIdAsync(booked.Token)));
            }
            var d = ci.AddDays(1);
            var gate = new TaskCompletionSource();
            var calls = new List<Task<HttpResponseMessage>>();
            for (var i = 0; i < 3; i++)
            {
                var (token, id) = bookings[i];
                var svcFirst = i % 2 == 0 ? a : b;
                var svcSecond = i % 2 == 0 ? b : a;
                calls.Add(Task.Run(async () => { await gate.Task; return await AddSessionAsync(token, SessionInput(svcFirst.Id, d, 600 + 120 * i, 2, 4000)); }));
                calls.Add(Task.Run(async () => { await gate.Task; return await AddSessionAsync(token, SessionInput(svcSecond.Id, d, 600 + 120 * i, 2, 4000)); }));
                // отмена брони гостем — пути «конечный статус» берут дом → бронь → ночи → сеансы
                if (i == 2) calls.Add(Task.Run(async () => { await gate.Task; return await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{token}/cancel", new { }); }));
            }
            calls.Add(Task.Run(async () => { await gate.Task; return await PostOrderAsync(a.Id, OrderInput(d, 600, 2, 4000, UniquePhone())); }));
            calls.Add(Task.Run(async () => { await gate.Task; return await PostOrderAsync(b.Id, OrderInput(d, 720, 2, 4000, UniquePhone())); }));
            gate.SetResult();
            var rs = await Task.WhenAll(calls);
            statuses.AddRange(rs.Select(r => r.StatusCode));
        }
        statuses.Should().NotContain(s => (int)s >= 500);
        await AssertNoOverlapAsync(a.Id);
        await AssertNoOverlapAsync(b.Id);
        await AssertParentsConsistentAsync(a.Id);
        await AssertParentsConsistentAsync(b.Id);
    }

    // ── ленивое снятие ───────────────────────────────────────────────────────────

    [Fact, TestCase("CY39-96")]
    public async Task ExpiredUnreleasedHouseHold_ThenOrderOfSameService_LazyReleaseUnderServiceLock()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        await EnableOrdersWithoutStayAsync(company);
        var house = await CreateHouseAsync(company, price: 1000);
        var svc = await CreateServiceAsync(company, step: 30, minHours: 1);
        var ci = InDays(12);
        var held = await BookOkAsync(house.Id, ci, ci.AddDays(2), client: host.Client());
        var d = ci.AddDays(1);
        await AddSessionOkAsync(held.Token, svc.Id, d, 720, 2);
        var holdUntil = (await GetPublicBookingAsync(held.Token, host.Client())).HoldExpiresAtUtc!.Value;

        host.StaysClock.Set(holdUntil.AddSeconds(2)); // истёкшее, но не снятое задачей удержание дома держит время услуги
        var r = await PostOrderAsync(svc.Id, OrderInput(d, 720, 2, 4000, UniquePhone()), host.Client());
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.StayBookings.AsNoTracking().SingleAsync(b => b.PublicToken == held.Token))).Status.Should().Be(StayBookingStatus.ExpiredUnpaid, "бронь доснята при проверке");
        await AssertNoOverlapAsync(svc.Id);
        await AssertParentsConsistentAsync(svc.Id);
    }

    [Fact, TestCase("CY39-97")]
    public async Task ExpiredHouseHold_WhoseHouseIsLockedByAnother_OrderGets409SlotTaken_WithoutWaiting()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        await EnableOrdersWithoutStayAsync(company);
        var house = await CreateHouseAsync(company, price: 1000);
        var svc = await CreateServiceAsync(company, step: 30, minHours: 1);
        var ci = InDays(12);
        var held = await BookOkAsync(house.Id, ci, ci.AddDays(2), client: host.Client());
        var d = ci.AddDays(1);
        await AddSessionOkAsync(held.Token, svc.Id, d, 720, 2);
        var holdUntil = (await GetPublicBookingAsync(held.Token, host.Client())).HoldExpiresAtUtc!.Value;
        host.StaysClock.Set(holdUntil.AddSeconds(2));

        var locked = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var holder = WithDbAsync(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await AdvisoryLock.AcquireAsync(db, $"stay-house:{house.Id}");
            locked.SetResult();
            await release.Task;
            await tx.RollbackAsync();
        });
        await locked.Task;
        var sw = Stopwatch.StartNew();
        var r = await PostOrderAsync(svc.Id, OrderInput(d, 720, 2, 4000, UniquePhone()), host.Client());
        sw.Stop();
        r.StatusCode.Should().Be(HttpStatusCode.Conflict, await r.Content.ReadAsStringAsync());
        (await Code(r)).Should().Be("SlotTaken");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "ленивое снятие чужого дома не ждёт замка (pg_try_advisory_xact_lock)");
        release.SetResult();
        await holder;

        // замок снят — задача снимет бронь, и время освободится
        await host.RunTaskAsync("stays-hold-expiry");
        (await PostOrderAsync(svc.Id, OrderInput(d, 720, 2, 4000, UniquePhone()), host.Client())).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── таймеры и подтверждение ──────────────────────────────────────────────────

    [Fact, TestCase("CY39-98")]
    public async Task TimerVsProofUpload_AroundDeadline_OneOutcome_ManyRounds()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: 30, step: 30, minHours: 1);
        var client = host.Client();
        var statuses = new List<HttpStatusCode>();
        var round = 0;
        foreach (var offsetSeconds in new[] { -1, 0, 1 })
            for (var k = 0; k < 6; k++, round++)
            {
                var date = InDays(9 + round);
                var created = await OrderOkAsync(svc.Id, date, 720, 2, client: client);
                var token = created.Token;
                var holdUntil = (await GetOrderAsync(token, client)).HoldExpiresAtUtc!.Value;
                host.StaysClock.Set(holdUntil.AddSeconds(offsetSeconds));
                var gate = new TaskCompletionSource();
                var task = Task.Run(async () => { await gate.Task; await host.RunTaskAsync("stays-hold-expiry"); });
                var upload = Task.Run(async () => { await gate.Task; return await AttachOrderProofAsync(token, client); });
                gate.SetResult();
                await task;
                var u = await upload;
                statuses.Add(u.StatusCode);
                var final = (await GetOrderAsync(token, client)).Status;
                if (u.StatusCode == HttpStatusCode.Created) final.Should().Be(StayBookingStatus.AwaitingPaymentCheck, $"смещение {offsetSeconds} с, раунд {round}: файл принят — заказ ждёт проверки, таймер его не снимает");
                else
                {
                    u.StatusCode.Should().Be(HttpStatusCode.Conflict);
                    final.Should().Be(StayBookingStatus.ExpiredUnpaid, $"смещение {offsetSeconds} с, раунд {round}: файл отклонён — заказ снят по таймеру");
                }
                if (offsetSeconds < 0) u.StatusCode.Should().Be(HttpStatusCode.Created, "за секунду до конца таймера файл принимается");
                if (offsetSeconds > 0) u.StatusCode.Should().Be(HttpStatusCode.Conflict, "через секунду после конца таймера — нет");
                var sessionActive = (await AllSessionsAsync(svc.Id)).Single(x => x.BusinessDate == date).ReleasedAtUtc == null;
                sessionActive.Should().Be(final is StayBookingStatus.AwaitingPaymentCheck, "сеанс активен тогда и только тогда, когда заказ жив");
                host.StaysClock.Set(DateTime.UtcNow);
            }
        statuses.Should().NotContain(s => (int)s >= 500);
    }

    [Fact, TestCase("CY39-99")]
    public async Task ConfirmPayment_VsTimerAfterDeadline_AwaitingOrderIsNeverExpired()
    {
        await using var host = new StaysTestFactory(ConnectionString);
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: 30, step: 30, minHours: 1);
        var client = host.Client();
        for (var round = 0; round < 6; round++)
        {
            var date = InDays(9 + round);
            var created = await OrderOkAsync(svc.Id, date, 720, 2, client: client);
            var token = created.Token;
            (await AttachOrderProofAsync(token, client)).StatusCode.Should().Be(HttpStatusCode.Created);
            var sessionId = await SessionIdOfOrderAsync(token);
            var version = (await SessionCardAsync(company, sessionId)).Version;
            host.StaysClock.Set(DateTime.UtcNow.AddMinutes(31 + round)); // таймер удержания (30 минут) давно вышел, но заказ уже ждёт проверки
            var gate = new TaskCompletionSource();
            var timer = Task.Run(async () => { await gate.Task; await host.RunTaskAsync("stays-hold-expiry"); });
            var confirm = Task.Run(async () => { await gate.Task; return await SessionActionAsync(company, sessionId, "confirm-payment", version); });
            gate.SetResult();
            await timer;
            (await confirm).StatusCode.Should().Be(HttpStatusCode.OK, $"раунд {round}: подтверждение платежа не должно проигрывать таймеру");
            (await GetOrderAsync(token, client)).Status.Should().Be(StayBookingStatus.Confirmed);
            host.StaysClock.Set(DateTime.UtcNow);
        }
        await AssertParentsConsistentAsync(svc.Id);
    }

    [Fact, TestCase("CY39-100")]
    public async Task StaffConfirmVsGuestProofUpload_ManyRounds_NoServerErrors_NoDeadlock()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: 30, step: 30, minHours: 1);
        var statuses = new List<HttpStatusCode>();
        for (var round = 0; round < 25; round++)
        {
            var date = InDays(9 + round);
            var created = await OrderOkAsync(svc.Id, date, 720, 2);
            var token = created.Token;
            (await AttachOrderProofAsync(token)).StatusCode.Should().Be(HttpStatusCode.Created);
            var sessionId = await SessionIdOfOrderAsync(token);
            var version = (await SessionCardAsync(company, sessionId)).Version;
            var gate = new TaskCompletionSource();
            var confirm = Task.Run(async () => { await gate.Task; return await SessionActionAsync(company, sessionId, "confirm-payment", version); });
            var upload = Task.Run(async () => { await gate.Task; return await AttachOrderProofAsync(token); });
            gate.SetResult();
            var (c, u) = (await confirm, await upload);
            statuses.Add(c.StatusCode);
            statuses.Add(u.StatusCode);
            c.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, $"раунд {round}");
            u.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.Conflict }, $"раунд {round}");
            (await GetOrderAsync(token)).Status.Should().Be(c.StatusCode == HttpStatusCode.OK ? StayBookingStatus.Confirmed : StayBookingStatus.AwaitingPaymentCheck);
        }
        statuses.Should().NotContain(s => (int)s >= 500);
        await AssertParentsConsistentAsync(svc.Id);
    }

    [Fact, TestCase("CY39-101")]
    public async Task GuestCancelVsStaffConfirmOrReject_FinalStateConsistent_ManyRounds()
    {
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: 30, step: 30, minHours: 1);
        var statuses = new List<HttpStatusCode>();
        for (var round = 0; round < 24; round++)
        {
            var date = InDays(9 + round);
            var created = await OrderOkAsync(svc.Id, date, 720, 2);
            var token = created.Token;
            (await AttachOrderProofAsync(token)).StatusCode.Should().Be(HttpStatusCode.Created);
            var sessionId = await SessionIdOfOrderAsync(token);
            var version = (await SessionCardAsync(company, sessionId)).Version;
            var reject = round % 2 == 1;
            var gate = new TaskCompletionSource();
            var staff = Task.Run(async () => { await gate.Task; return await SessionActionAsync(company, sessionId, reject ? "reject-payment" : "confirm-payment", version, reject ? "Не пришло" : null); });
            var guest = Task.Run(async () => { await gate.Task; return await CancelOrderAsync(token); });
            gate.SetResult();
            var (s, g) = (await staff, await guest);
            statuses.Add(s.StatusCode);
            statuses.Add(g.StatusCode);
            var final = (await GetOrderAsync(token)).Status;
            s.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, $"раунд {round}");
            g.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, $"раунд {round}");
            if (reject && s.StatusCode == HttpStatusCode.OK) final.Should().Be(StayBookingStatus.PaymentRejected, "отклонённый заказ отменить нельзя: гость получает 409");
            if (!reject && s.StatusCode == HttpStatusCode.OK && g.StatusCode == HttpStatusCode.OK) final.Should().Be(StayBookingStatus.CancelledByGuest, "подтверждённый заказ гость вправе отменить после подтверждения");
            if (g.StatusCode == HttpStatusCode.OK) final.Should().Be(StayBookingStatus.CancelledByGuest);
            if (s.StatusCode == HttpStatusCode.OK && g.StatusCode == HttpStatusCode.Conflict) final.Should().Be(reject ? StayBookingStatus.PaymentRejected : StayBookingStatus.Confirmed);
        }
        statuses.Should().NotContain(x => (int)x >= 500);
        await AssertParentsConsistentAsync(svc.Id);
    }

    // ── бронь дома ↔ сеансы ──────────────────────────────────────────────────────

    [Fact, TestCase("CY39-102")]
    public async Task BookingCancelVsAddSession_NoActiveSessionOfInactiveBooking_ManyRounds()
    {
        var company = await CreateStaysCompanyAsync(prepayPercent: 30);
        var house = await CreateHouseAsync(company, price: 1000);
        var svc = await CreateServiceAsync(company, step: 30, minHours: 1);
        var statuses = new List<HttpStatusCode>();
        for (var round = 0; round < 20; round++)
        {
            var ci = InDays(10 + round * 3);
            var booked = await BookOkAsync(house.Id, ci, ci.AddDays(2));
            var gate = new TaskCompletionSource();
            var cancel = Task.Run(async () => { await gate.Task; return await AnonymousClient().PostJsonAsync($"/api/stays/bookings/public/{booked.Token}/cancel", new { }); });
            var add = Task.Run(async () => { await gate.Task; return await AddSessionAsync(booked.Token, SessionInput(svc.Id, ci.AddDays(1), 600, 2, 4000)); });
            gate.SetResult();
            var (c, a) = (await cancel, await add);
            statuses.Add(c.StatusCode);
            statuses.Add(a.StatusCode);
            a.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, $"раунд {round}");
            (await GetPublicBookingAsync(booked.Token)).Status.Should().Be(StayBookingStatus.CancelledByGuest);
            var sessions = await WithDbAsync(db => db.StayServiceSessions.AsNoTracking().Where(x => x.StayBookingId == db.StayBookings.Where(b => b.PublicToken == booked.Token).Select(b => b.Id).First()).ToListAsync());
            sessions.Where(x => x.ReleasedAtUtc == null).Should().BeEmpty($"раунд {round}: сеанс либо не создан, либо создан и освобождён каскадом");
        }
        statuses.Should().NotContain(x => (int)x >= 500);
        (await ActiveSessionsAsync(svc.Id)).Should().BeEmpty();
        await AssertParentsConsistentAsync(svc.Id);
    }

    [Fact, TestCase("CY39-103")]
    public async Task EightParallelAdditions_ToOneBooking_LimitFiveHolds_ExactlyFiveCreated()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 1000);
        var svc = await CreateServiceAsync(company, step: 30, minHours: 1, maxHours: 1, buffer: 0);
        var ci = InDays(12);
        var booked = await BookOkAsync(house.Id, ci, ci.AddDays(3));
        var d = ci.AddDays(1);
        var gate = new TaskCompletionSource();
        var calls = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            await gate.Task;
            return await AddSessionAsync(booked.Token, SessionInput(svc.Id, d, 600 + 60 * i, 1, 2000));
        })).ToList();
        gate.SetResult();
        var rs = await Task.WhenAll(calls);
        rs.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(5, string.Join(",", rs.Select(r => (int)r.StatusCode)));
        foreach (var r in rs.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            r.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await Code(r)).Should().Be("TooManySessions");
        }
        var page = await GetPublicBookingAsync(booked.Token);
        page.Sessions.Should().HaveCount(5);
        page.Lines.Sum(l => l.AmountRub).Should().Be(page.TotalRub);
    }

    [Fact, TestCase("CY39-104")]
    public async Task ParallelSamePhone_HeldOrders_PhoneLockMakesLimitExact()
    {
        await using var host = new StaysTestFactory(ConnectionString, settings: new Dictionary<string, string>
        {
            ["Stays:Services:PhoneLimits:MaxHeldPerPhone"] = "2", ["Stays:Services:PhoneLimits:MaxHeldPerPhonePerCompany"] = "1", ["Stays:Services:PhoneLimits:MaxCreatedPerPhonePerDay"] = "10",
        });
        var company = await CreateStaysCompanyAsync();
        await EnableOrdersWithoutStayAsync(company);
        var svc = await CreateServiceAsync(company, prepay: 30, step: 30, minHours: 1);
        var phone = UniquePhone();
        var date = InDays(9);
        await host.Client().GetAsync($"/api/stays/public/services/{svc.Id}/starts?date={D(date)}"); // прогрев хоста: первый запрос создаёт файл правовых текстов
        var gate = new TaskCompletionSource();
        var calls = Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
        {
            await gate.Task;
            return await PostOrderAsync(svc.Id, OrderInput(date, 480 + 120 * i, 1, 2000, phone), host.Client());
        })).ToList();
        gate.SetResult();
        var rs = await Task.WhenAll(calls);
        rs.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "одно удержание на номер в компании, даже при параллельных запросах: " + string.Join(",", rs.Select(r => (int)r.StatusCode)));
        rs.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => (int)r.StatusCode == 429);
    }
}
