using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Shops;

/// <summary>Everything one request needs to know about the shop's time and acceptance, loaded once.</summary>
public sealed record ShopGateContext(
    Company Shop, ShopSettings Settings, OrdersPlan Plan, ShopScheduleSnapshot Schedule, DateTime NowUtc, ShopGateResult Gate,
    bool SettingsRowExists = true)
{
    public PickupSchedule Pickup => Gate.Schedule;
    public TimeZoneInfo Zone => Schedule.Zone;
}

/// <summary>
/// ARCHITECTURE_CYCLE24.md §450 — the ONLY place that assembles the input of <see cref="ShopOrderingGate"/>: the settings row, the special
/// days of the horizon (one range query), the tariff of the "Заказы" line and the month counter (a primary-key lookup) — a handful of
/// indexed reads. The storefront, <c>pickup-slots</c>, <c>quote</c>, order creation, <c>GET /api/shops/{id}</c> and <c>ordering-status</c>
/// all call it, so no second place can decide whether a shop takes orders.
/// </summary>
public class ShopGateLoader(AppDbContext db, OrdersPlanResolver plans)
{
    /// <summary>Special days are read from yesterday (an overnight tail) to this many days ahead: the pre-order horizon (≤ 14) and the "opens on …" look-ahead (14) both fit.</summary>
    public const int LookAheadDays = 16;

    public async Task<ShopGateContext> LoadAsync(Company shop, DateTime nowUtc, CancellationToken ct = default, ShopSettings? settings = null)
    {
        var rowExists = true;
        if (settings is null)
        {
            settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shop.Id, ct);
            rowExists = settings is not null;
            settings ??= new ShopSettings { CompanyId = shop.Id };
        }
        var zone = ZoneOf(shop);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(nowUtc), zone));

        var schedule = await LoadScheduleAsync(shop, settings, zone, today, ct);
        var plan = shop.BillingAccountId is { } accountId
            ? await plans.GetForAccountAsync(accountId, ct)
            : OrdersPlan.FallbackFree;
        var used = shop.BillingAccountId is { } acc ? await OrdersThisMonthAsync(acc, MonthStart(today), ct) : 0;

        var gate = ShopOrderingGate.Evaluate(new ShopGateInput(shop, settings, schedule, plan, used, nowUtc));
        return new ShopGateContext(shop, settings, plan, schedule, nowUtc, gate, rowExists);
    }

    /// <summary>The shop's schedule: weekly hours from the settings row + special days from <c>today − 1</c> for <see cref="LookAheadDays"/>.</summary>
    public async Task<ShopScheduleSnapshot> LoadScheduleAsync(
        Company shop, ShopSettings settings, TimeZoneInfo zone, DateOnly today, CancellationToken ct = default)
    {
        var from = today.AddDays(-1);
        var to = today.AddDays(LookAheadDays);
        var rows = await db.ShopSpecialDays.AsNoTracking().Where(d => d.CompanyId == shop.Id && d.Date >= from && d.Date <= to).ToListAsync(ct);
        var special = rows.ToDictionary(
            r => r.Date,
            r => r.IsClosed ? SpecialDayHours.Closed : new SpecialDayHours(false, ShopScheduleRules.ParseIntervals(r.IntervalsJson)));
        return new ShopScheduleSnapshot(zone, ShopScheduleRules.Parse(settings.WorkingHoursJson), special);
    }

    public async Task<int> OrdersThisMonthAsync(Guid billingAccountId, DateOnly monthStart, CancellationToken ct = default) =>
        await db.OrderMonthlyUsages.AsNoTracking()
            .Where(u => u.BillingAccountId == billingAccountId && u.Month == monthStart)
            .Select(u => (int?)u.Count).FirstOrDefaultAsync(ct) ?? 0;

    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    public static TimeZoneInfo ZoneOf(Company shop) => TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
