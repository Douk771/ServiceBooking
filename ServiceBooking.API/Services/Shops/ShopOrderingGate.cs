using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Shops;

/// <summary>Everything the acceptance rule reads. <see cref="ShopGateLoader"/> is the single place that assembles it.</summary>
public sealed record ShopGateInput(
    Company Company, ShopSettings Settings, ShopScheduleSnapshot Schedule, OrdersPlan Plan, int OrdersThisMonth, DateTime NowUtc);

/// <summary>
/// The verdict. <c>ReasonText</c> is what the CUSTOMER reads, <c>OwnerText</c> the exact reason for staff; both null when accepting.
/// <c>Dates</c> / <c>PickupSchedule</c> ride along so the storefront does not compute the schedule a second time.
/// </summary>
public sealed record ShopGateResult(
    bool Accepting, ShopNotAcceptingCode? Code, string? ReasonText, string? OwnerText, AsapOption Asap, bool ScheduledAvailable,
    ShopOpenState OpenState, ShopAcceptanceState Acceptance, OrderLimitInfo OrderLimit, IReadOnlyList<PickupDate> Dates,
    PickupSchedule Schedule);

/// <summary>
/// ARCHITECTURE_CYCLE24.md §450 (US-24-04) — "does this shop take orders right now" as ONE pure function. Cycle 23 had "active shop";
/// cycle 24 adds hours, pause, the switch, the tariff and the monthly limit. Order of the checks (the first that fires gives the code):
/// blocked → no hours → stopped → paused → plan forbids → monthly limit → no pickup time at all → accepting. Closed "now" blocks
/// only "as soon as possible" (Q-24-4): a shop that is closed but takes pre-orders keeps accepting. Reference vectors:
/// <c>contracts/cycle24/pickup-schedule-vectors.json</c>.
/// </summary>
public static class ShopOrderingGate
{
    public const string ShopUnavailable = OrderTexts.ShopNotAvailable;
    public const string NoHoursCustomer = "Магазин пока не принимает заказы";
    public const string TemporarilyNotAccepting = "Магазин временно не принимает заказы";

    public static PickupSettings PickupSettingsOf(ShopSettings settings) => new(
        settings.AsapEnabled, settings.ScheduledEnabled, settings.SlotStepMinutes, settings.PreorderDays, settings.MinPrepMinutes);

    public static ShopGateResult Evaluate(ShopGateInput input)
    {
        var (company, settings, schedule, plan, ordersThisMonth, now) = input;
        var pickup = new PickupSchedule(schedule, PickupSettingsOf(settings));
        var open = pickup.OpenState(now);
        var asap = pickup.Asap(now);
        var dates = pickup.Dates(now);
        var scheduledAvailable = dates.Any(d => d.HasSlots);
        var acceptance = ShopAcceptanceRules.State(settings, now, schedule.Zone);
        var monthDate = pickup.LocalDate(now);
        var limit = OrderLimitRules.Describe(ordersThisMonth, plan.MaxOrdersPerMonth, monthDate);

        ShopGateResult Refuse(ShopNotAcceptingCode code, string customerText, string ownerText) =>
            new(false, code, customerText, ownerText, asap, scheduledAvailable, open, acceptance, limit, dates, pickup);

        if (company.Kind != CompanyKind.Orders || !company.IsActive)
            return Refuse(ShopNotAcceptingCode.Blocked, ShopUnavailable, "Магазин заблокирован администратором");
        if (!schedule.HoursSet)
            return Refuse(ShopNotAcceptingCode.NoWorkingHours, NoHoursCustomer, "Задайте часы работы — без них магазин не принимает заказы");
        if (acceptance.Mode == ShopAcceptanceMode.Stopped)
            return Refuse(ShopNotAcceptingCode.Stopped, TemporarilyNotAccepting, "Приём заказов выключен");
        if (acceptance.Mode == ShopAcceptanceMode.Paused)
        {
            var until = ShopAcceptanceRules.PausedUntilText(acceptance.PausedUntilUtc!.Value, now, schedule.Zone);
            return Refuse(ShopNotAcceptingCode.Paused, $"{TemporarilyNotAccepting} — до {until}", $"Пауза до {until}");
        }
        if (!plan.AllowOrders)
            return Refuse(ShopNotAcceptingCode.NotAllowedByPlan, TemporarilyNotAccepting, "Ваш тариф не включает приём заказов");
        if (plan.MaxOrdersPerMonth is { } max && ordersThisMonth >= max)
            return Refuse(ShopNotAcceptingCode.MonthlyLimitReached, TemporarilyNotAccepting,
                OrderLimitRules.ReachedOwnerText(ordersThisMonth, max, monthDate));
        if (!asap.Available && !scheduledAvailable)
        {
            var text = open.IsOpen ? PickupSchedule.NotEnoughTimeAsapText : open.Text;
            return Refuse(ShopNotAcceptingCode.NoPickupTimeAvailable, text, text);
        }
        return new ShopGateResult(true, null, null, null, asap, scheduledAvailable, open, acceptance, limit, dates, pickup);
    }
}
