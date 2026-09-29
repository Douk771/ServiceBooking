using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §450 — ShopOrderingGate v2: the ORDER of the checks and the way they combine (each condition alone is in the reference
/// vectors, PickupScheduleVectorsTests). 2026-09-30 is a Wednesday; Europe/Moscow = UTC+3.
/// </summary>
public class ShopOrderingGateV2Tests
{
    private static readonly DateTime Noon = new(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc); // 12:00 local

    private static ShopScheduleSnapshot Weekdays(int start = 540, int end = 1260)
    {
        var hours = new WeeklyHours(new Dictionary<DayOfWeek, IReadOnlyList<TimeInterval>>
        {
            [DayOfWeek.Monday] = [new TimeInterval(start, end)], [DayOfWeek.Tuesday] = [new TimeInterval(start, end)],
            [DayOfWeek.Wednesday] = [new TimeInterval(start, end)], [DayOfWeek.Thursday] = [new TimeInterval(start, end)],
            [DayOfWeek.Friday] = [new TimeInterval(start, end)],
        });
        return new ShopScheduleSnapshot(TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"), hours, new Dictionary<DateOnly, SpecialDayHours>());
    }

    private static ShopGateResult Gate(
        Action<ShopSettings>? settings = null, Action<Company>? company = null, ShopScheduleSnapshot? schedule = null,
        OrdersPlan? plan = null, int used = 0, DateTime? now = null)
    {
        var s = new ShopSettings { ScheduledEnabled = true, PreorderDays = 1 };
        settings?.Invoke(s);
        var c = new Company { Kind = CompanyKind.Orders, IsActive = true };
        company?.Invoke(c);
        return ShopOrderingGate.Evaluate(new ShopGateInput(c, s, schedule ?? Weekdays(), plan ?? OrdersPlan.FallbackFree with { MaxOrdersPerMonth = null }, used, now ?? Noon));
    }

    [Fact]
    public void Open_Accepts_WithNoReasonAtAll()
    {
        var r = Gate();
        r.Accepting.Should().BeTrue();
        (r.Code, r.ReasonText, r.OwnerText).Should().Be((null, null, null));
    }

    [Fact]
    public void Order_BlockedBeatsEverything()
    {
        var r = Gate(s => { s.OrdersStopped = true; s.PausedUntilUtc = Noon.AddHours(1); }, c => c.IsActive = false,
            schedule: new ShopScheduleSnapshot(TimeZoneInfo.Utc, null, new Dictionary<DateOnly, SpecialDayHours>()),
            plan: OrdersPlan.FallbackFree with { AllowOrders = false }, used: 1000);
        r.Code.Should().Be(ShopNotAcceptingCode.Blocked);
    }

    [Fact]
    public void Order_NoHoursBeatsStoppedPausedPlanAndLimit()
    {
        var r = Gate(s => { s.OrdersStopped = true; s.PausedUntilUtc = Noon.AddHours(1); },
            schedule: new ShopScheduleSnapshot(TimeZoneInfo.Utc, null, new Dictionary<DateOnly, SpecialDayHours>()),
            plan: OrdersPlan.FallbackFree with { AllowOrders = false });
        r.Code.Should().Be(ShopNotAcceptingCode.NoWorkingHours);
    }

    [Fact]
    public void Order_StoppedBeatsPaused_PausedBeatsPlan_PlanBeatsLimit()
    {
        Gate(s => { s.OrdersStopped = true; s.PausedUntilUtc = Noon.AddHours(1); }).Code.Should().Be(ShopNotAcceptingCode.Stopped);
        Gate(s => s.PausedUntilUtc = Noon.AddHours(1), plan: OrdersPlan.FallbackFree with { AllowOrders = false }).Code.Should().Be(ShopNotAcceptingCode.Paused);
        Gate(plan: OrdersPlan.FallbackFree with { AllowOrders = false }, used: 150).Code.Should().Be(ShopNotAcceptingCode.NotAllowedByPlan);
        Gate(plan: OrdersPlan.FallbackFree with { MaxOrdersPerMonth = 150 }, used: 150).Code.Should().Be(ShopNotAcceptingCode.MonthlyLimitReached);
    }

    [Fact]
    public void ExpiredPause_IsNoPause_AtTheBoundaryToo()
    {
        Gate(s => s.PausedUntilUtc = Noon.AddSeconds(-1)).Accepting.Should().BeTrue();
        Gate(s => s.PausedUntilUtc = Noon).Accepting.Should().BeTrue("pausedUntil == now already reads as resumed");
        Gate(s => s.PausedUntilUtc = Noon.AddSeconds(1)).Code.Should().Be(ShopNotAcceptingCode.Paused);
    }

    [Fact]
    public void Monthly_LimitIsInclusive_AndNullMeansUnlimited()
    {
        var plan = OrdersPlan.FallbackFree with { MaxOrdersPerMonth = 150 };
        Gate(plan: plan, used: 149).Accepting.Should().BeTrue();
        Gate(plan: plan, used: 150).Accepting.Should().BeFalse();
        Gate(plan: OrdersPlan.FallbackFree with { MaxOrdersPerMonth = null }, used: 1_000_000).Accepting.Should().BeTrue();
    }

    [Fact]
    public void ClosedNow_ButPreordersOn_StillAccepts_AsapOnlyIsRefused()
    {
        // Saturday noon: closed; Q-24-4 — closed "now" blocks ONLY "as soon as possible".
        var saturday = new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);
        Gate(s => s.PreorderDays = 2, now: saturday).Accepting.Should().BeTrue();
        Gate(s => { s.ScheduledEnabled = false; }, now: saturday).Code.Should().Be(ShopNotAcceptingCode.NoPickupTimeAvailable);
    }

    [Fact]
    public void NoPickupTime_TextDependsOnOpenOrClosed()
    {
        var justBeforeClose = new DateTime(2026, 9, 30, 17, 55, 0, DateTimeKind.Utc); // 20:55, closes 21:00
        var open = Gate(s => s.ScheduledEnabled = false, now: justBeforeClose);
        open.Code.Should().Be(ShopNotAcceptingCode.NoPickupTimeAvailable);
        open.ReasonText.Should().Be("Сегодня уже не успеем приготовить заказ");

        var closed = Gate(s => s.ScheduledEnabled = false, now: new DateTime(2026, 9, 30, 18, 30, 0, DateTimeKind.Utc));
        closed.ReasonText.Should().Be("Закрыто, откроемся завтра в 9:00");
        closed.OwnerText.Should().Be(closed.ReasonText);
    }

    [Fact]
    public void CompanyOfAnotherKind_IsBlocked() =>
        Gate(company: c => c.Kind = CompanyKind.Services).Code.Should().Be(ShopNotAcceptingCode.Blocked);

    [Fact]
    public void OrderLimit_RidesInEveryResult()
    {
        var r = Gate(plan: OrdersPlan.FallbackFree with { MaxOrdersPerMonth = 150 }, used: 120);
        r.OrderLimit.Should().Be(new OrderLimitInfo(120, 150, "сентябрь 2026", OrderLimitWarningLevel.Warning80, "Заказов в этом месяце: 120 из 150"));
    }
}
