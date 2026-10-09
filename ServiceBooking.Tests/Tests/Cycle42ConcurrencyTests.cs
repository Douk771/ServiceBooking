using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, «Бани»: целостность времени ресурса при параллельных запросах (CY42-50…55) — главный риск вертикали: баня работает ночью, и бизнес-день тянется за полночь.
/// Писано по SPEC_CYCLE42_BANI.md (US-42-24) и ARCHITECTURE_CYCLE42.md §42.15.4: N параллельных «Пт 23:00–01:00» и «Пт 00:30–02:30» дают ровно одну бронь;
/// «Пт 04:00–06:00» + зазор 30 мин и «Сб 06:00» не уживаются; ни один запрос не заканчивается 5xx (в том числе 40P01 «взаимная блокировка»).
/// </summary>
public class Cycle42ConcurrencyTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private const int Fri2300 = 1380;
    private const int Fri0030 = 1470; // «Пт 00:30» после полуночи — минуты 24:30 пятничного бизнес-дня
    private const int Fri0400 = 1680;
    private const int Sat0600 = 360;

    private async Task<ResCtx> NightBathAsync(int buffer = 30)
    {
        var c = await CreateBathAsync();
        return await AddResourceAsync(c, "Ночная баня", step: 30, minHours: 1, maxHours: 4, buffer: buffer, windows: [(360, 1800)]);
    }

    private static async Task<string> CodeOrEmpty(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        return text.Contains("\"code\"") ? System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("code").GetString()! : text;
    }

    private async Task<List<HttpResponseMessage>> RaceAsync(IEnumerable<Func<Task<HttpResponseMessage>>> calls)
    {
        var gate = new TaskCompletionSource();
        var tasks = calls.Select(call => Task.Run(async () => { await gate.Task; return await call(); })).ToList();
        gate.SetResult();
        return (await Task.WhenAll(tasks)).ToList();
    }

    /// <summary>Сеанс активен тогда и только тогда, когда активна его бронь (инвариант целостности).</summary>
    private async Task AssertSessionsMatchOrdersAsync(Guid serviceId)
    {
        var bad = await WithDbAsync(async db =>
        {
            var cancelled = new[] { StayBookingStatus.ExpiredUnpaid, StayBookingStatus.PaymentRejected, StayBookingStatus.CancelledByGuest, StayBookingStatus.CancelledByOwner };
            var activeOfDead = await (from s in db.StayServiceSessions.AsNoTracking()
                                      join o in db.StayServiceOrders.AsNoTracking() on s.StayServiceOrderId equals o.Id
                                      where s.ServiceId == serviceId && s.ReleasedAtUtc == null && cancelled.Contains(o.Status)
                                      select s.Id).CountAsync();
            var releasedOfLive = await (from s in db.StayServiceSessions.AsNoTracking()
                                        join o in db.StayServiceOrders.AsNoTracking() on s.StayServiceOrderId equals o.Id
                                        where s.ServiceId == serviceId && s.ReleasedAtUtc != null && !cancelled.Contains(o.Status) && s.State != StayServiceSessionState.CancelledByGuest
                                              && s.State != StayServiceSessionState.CancelledByOwner
                                        select s.Id).CountAsync();
            return activeOfDead + releasedOfLive;
        });
        bad.Should().Be(0, "сеанс активен тогда и только тогда, когда активна бронь");
    }

    // ── CY42-50: одно и то же время ──────────────────────────────────────────────

    [Fact, TestCase("CY42-50")]
    public async Task TwentyParallelBookings_SameFriday2300to0100_ExactlyOneCreated_OthersSlotTaken()
    {
        var res = await NightBathAsync();
        var friday = NextWeekday(DayOfWeek.Friday, 8);
        var results = await RaceAsync(Enumerable.Range(0, 20).Select(_ => (Func<Task<HttpResponseMessage>>)(() => PostBathOrderAsync(res.Id, friday, Fri2300, 2))));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "создана ровно одна бронь");
        var refused = results.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        refused.Should().HaveCount(19).And.OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var r in refused) (await CodeOrEmpty(r)).Should().Be("SlotTaken");
        (await ActiveSessionsAsync(res.Id)).Should().HaveCount(1);
        (await WithDbAsync(db => db.StayServiceOrders.CountAsync(o => o.ServiceId == res.Id))).Should().Be(1, "проигравшие запросы не оставляют броней");
    }

    // ── CY42-51: через полночь ───────────────────────────────────────────────────

    [Fact, TestCase("CY42-51")]
    public async Task Parallel_Friday2300to0100_And_Friday0030to0230_ExactlyOneWins_ManyRounds()
    {
        var res = await NightBathAsync();
        for (var round = 0; round < 6; round++)
        {
            var friday = NextWeekday(DayOfWeek.Friday, 8).AddDays(7 * round);
            var calls = new List<Func<Task<HttpResponseMessage>>>();
            for (var i = 0; i < 6; i++)
            {
                calls.Add(() => PostBathOrderAsync(res.Id, friday, Fri2300, 2));
                calls.Add(() => PostBathOrderAsync(res.Id, friday, Fri0030, 2));
            }
            var results = await RaceAsync(calls);
            var codes = string.Join(",", results.Select(r => (int)r.StatusCode));
            results.Should().NotContain(r => (int)r.StatusCode >= 500, $"раунд {round}: {codes}");
            results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, $"раунд {round}: ровно одна бронь на пересекающиеся 23:00–01:00 и 00:30–02:30, а было {codes}");
            foreach (var r in results.Where(r => r.StatusCode != HttpStatusCode.Created))
            {
                r.StatusCode.Should().Be(HttpStatusCode.Conflict);
                (await CodeOrEmpty(r)).Should().Be("SlotTaken");
            }
        }
        (await ActiveSessionsAsync(res.Id)).Should().HaveCount(6);
        await AssertBathNoOverlapAsync(res.Id);
    }

    [Fact, TestCase("CY42-51")]
    public async Task Friday2300to0100_Then_Friday0030_IsRejected_AndFriday0100_Plus_Buffer_IsFree()
    {
        var res = await NightBathAsync();
        var friday = NextWeekday(DayOfWeek.Friday, 9);
        (await PostBathOrderAsync(res.Id, friday, Fri2300, 2)).StatusCode.Should().Be(HttpStatusCode.Created);

        var overlap = await PostBathOrderAsync(res.Id, friday, Fri0030, 2);
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict, "00:30 внутри сеанса 23:00–01:00");
        (await CodeOrEmpty(overlap)).Should().Be("SlotTaken");
        var inBuffer = await PostBathOrderAsync(res.Id, friday, 1500, 1);
        inBuffer.StatusCode.Should().Be(HttpStatusCode.Conflict, "01:00 — в зазоре 30 минут после сеанса");
        (await PostBathOrderAsync(res.Id, friday, 1530, 1)).StatusCode.Should().Be(HttpStatusCode.Created, "01:30 — ровно конец зазора");
        await AssertBathNoOverlapAsync(res.Id);
    }

    // ── CY42-52: зазор через границу бизнес-дней ─────────────────────────────────

    [Fact, TestCase("CY42-52")]
    public async Task Friday0400to0600_PlusBuffer_RejectsSaturday0600_ButNotSaturday0630()
    {
        var res = await NightBathAsync();
        var friday = NextWeekday(DayOfWeek.Friday, 9);
        var saturday = friday.AddDays(1);
        (await PostBathOrderAsync(res.Id, friday, Fri0400, 2)).StatusCode.Should().Be(HttpStatusCode.Created);

        var startsSat = (await J(await AnonymousClient().GetAsync($"/api/baths/public/services/{res.Id}/starts?date={D(saturday)}")))
            .GetProperty("starts").EnumerateArray().Select(s => s.GetProperty("startMinute").GetInt32()).ToList();
        startsSat.Should().NotContain(Sat0600, "06:00 субботы — в зазоре пятничного сеанса, который кончается ровно в 06:00");
        startsSat.Should().Contain(390);

        var second = await PostBathOrderAsync(res.Id, saturday, Sat0600, 1);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOrEmpty(second)).Should().Be("SlotTaken");
        (await PostBathOrderAsync(res.Id, saturday, 390, 1)).StatusCode.Should().Be(HttpStatusCode.Created, "06:30 — ровно конец зазора");
        await AssertBathNoOverlapAsync(res.Id);
    }

    [Fact, TestCase("CY42-52")]
    public async Task Parallel_Friday0400to0600_And_Saturday0600_ExactlyOneWins_NoServerErrors()
    {
        var res = await NightBathAsync();
        for (var round = 0; round < 6; round++)
        {
            var friday = NextWeekday(DayOfWeek.Friday, 8).AddDays(7 * round);
            var saturday = friday.AddDays(1);
            var calls = new List<Func<Task<HttpResponseMessage>>>();
            for (var i = 0; i < 5; i++)
            {
                calls.Add(() => PostBathOrderAsync(res.Id, friday, Fri0400, 2));
                calls.Add(() => PostBathOrderAsync(res.Id, saturday, Sat0600, 1));
            }
            var results = await RaceAsync(calls);
            var codes = string.Join(",", results.Select(r => (int)r.StatusCode));
            results.Should().NotContain(r => (int)r.StatusCode >= 500, $"раунд {round}: ни одного 5xx, в том числе 40P01: {codes}");
            results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, $"раунд {round}: уживается одна из двух сторон, а было {codes}");
            results.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        }
        await AssertBathNoOverlapAsync(res.Id);
    }

    // ── CY42-53: несколько ресурсов, отмены, взаимная блокировка ─────────────────

    [Fact, TestCase("CY42-53")]
    public async Task ParallelBookingsAndCancels_AcrossTwoResources_NoServerErrors_NoOverlap()
    {
        var c = await CreateBathAsync();
        var a = await AddResourceAsync(c, "Баня", step: 30, minHours: 1, windows: [(360, 1800)]);
        var b = await AddResourceAsync(c, "Чан", step: 30, minHours: 1, windows: [(360, 1800)]);
        var statuses = new List<HttpStatusCode>();
        for (var round = 0; round < 6; round++)
        {
            var friday = NextWeekday(DayOfWeek.Friday, 8).AddDays(7 * round);
            var seedA = await BookBathAsync(a.Id, friday, 720, 2);
            var seedB = await BookBathAsync(b.Id, friday, 720, 2);
            var calls = new List<Func<Task<HttpResponseMessage>>>
            {
                () => AnonymousClient().PostJsonAsync($"/api/baths/service-orders/public/{seedA}/cancel", new { }),
                () => AnonymousClient().PostJsonAsync($"/api/baths/service-orders/public/{seedB}/cancel", new { })
            };
            for (var i = 0; i < 4; i++)
            {
                calls.Add(() => PostBathOrderAsync(a.Id, friday, 720, 2));
                calls.Add(() => PostBathOrderAsync(b.Id, friday, 720, 2));
                calls.Add(() => PostBathOrderAsync(a.Id, friday, Fri2300, 2));
                calls.Add(() => PostBathOrderAsync(b.Id, friday, Fri0030, 2));
            }
            var results = await RaceAsync(calls);
            statuses.AddRange(results.Select(r => r.StatusCode));
            results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.OK,
                $"раунд {round}: " + string.Join(",", results.Select(r => (int)r.StatusCode)));
        }
        statuses.Should().NotContain(s => (int)s >= 500, "40P01 → 500 не допускается");
        await AssertBathNoOverlapAsync(a.Id);
        await AssertBathNoOverlapAsync(b.Id);
        await AssertSessionsMatchOrdersAsync(a.Id);
        await AssertSessionsMatchOrdersAsync(b.Id);
    }

    // ── CY42-54: лимиты номера при параллельных запросах ─────────────────────────

    [Fact, TestCase("CY42-54")]
    public async Task ParallelBookings_FromOneNumber_InOneCompany_OnlyOneHeld_OthersTooManyRequests()
    {
        var res = await NightBathAsync();
        var phone = UniquePhone();
        var friday = NextWeekday(DayOfWeek.Friday, 8);
        var results = await RaceAsync(Enumerable.Range(0, 8).Select(i => (Func<Task<HttpResponseMessage>>)(() => PostBathOrderAsync(res.Id, friday, 480 + 180 * i, 1, phone: phone))));

        results.Should().NotContain(r => (int)r.StatusCode >= 500);
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "в компании у номера одна неоплаченная бронь");
        var refused = results.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        refused.Should().OnlyContain(r => r.StatusCode == (HttpStatusCode)429);
        foreach (var r in refused) (await r.Content.ReadAsStringAsync()).Should().Contain("Слишком много неоплаченных броней");
        (await ActiveSessionsAsync(res.Id)).Should().HaveCount(1);
    }

    // ── CY42-55: повтор и гонка отмены с чеком ───────────────────────────────────

    [Fact, TestCase("CY42-55")]
    public async Task ParallelRepeats_WithOneIdempotencyKey_GiveOneBooking()
    {
        var res = await NightBathAsync();
        var friday = NextWeekday(DayOfWeek.Friday, 8);
        var key = Guid.NewGuid();
        var phone = UniquePhone();
        var results = await RaceAsync(Enumerable.Range(0, 10).Select(_ => (Func<Task<HttpResponseMessage>>)(() => PostBathOrderAsync(res.Id, friday, Fri2300, 2, phone: phone, key: key))));

        results.Should().NotContain(r => (int)r.StatusCode >= 500, string.Join(",", results.Select(r => (int)r.StatusCode)));
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        var tokens = new HashSet<string>();
        foreach (var r in results.Where(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)) tokens.Add((await J(r)).GetProperty("token").GetString()!);
        tokens.Should().HaveCount(1, "повторы с тем же ключом возвращают ту же бронь");
        (await ActiveSessionsAsync(res.Id)).Should().HaveCount(1);
    }

    [Fact, TestCase("CY42-55")]
    public async Task GuestCancelRacingWithProofUpload_NoServerErrors_FinalStateIsConsistent()
    {
        var res = await NightBathAsync();
        for (var round = 0; round < 8; round++)
        {
            var friday = NextWeekday(DayOfWeek.Friday, 8).AddDays(7 * round);
            var token = await BookBathAsync(res.Id, friday, Fri2300, 2);
            var results = await RaceAsync(
            [
                () => AnonymousClient().PostJsonAsync($"/api/baths/service-orders/public/{token}/cancel", new { }),
                () => AnonymousClient().PostAsync($"/api/baths/service-orders/public/{token}/payment-proofs", FileContent(TestImages.SolidJpeg(60, 40), "image/jpeg", "check.jpg")),
                () => AnonymousClient().PostJsonAsync($"/api/baths/service-orders/public/{token}/cancel", new { })
            ]);
            results.Should().NotContain(r => (int)r.StatusCode >= 500, $"раунд {round}: " + string.Join(",", results.Select(r => (int)r.StatusCode)));
            var page = await BathOrderPageAsync(token);
            var status = page.GetProperty("status").GetString();
            status.Should().BeOneOf("CancelledByGuest", "AwaitingPaymentCheck");
            var active = (await ActiveSessionsAsync(res.Id)).Count(s => s.BusinessDate == friday && s.StartMinute == Fri2300);
            active.Should().Be(status == "CancelledByGuest" ? 0 : 1, $"раунд {round}: время держится ровно пока бронь жива");
        }
        await AssertSessionsMatchOrdersAsync(res.Id);
    }
}
