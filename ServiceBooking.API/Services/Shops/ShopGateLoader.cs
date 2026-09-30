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

    /// <summary>
    /// The pickup context of ONE shop with its current working day (CY24-35): order DTOs built with it agree with the storefront's
    /// "Сегодня"/"Завтра" in the after-midnight tail of an overnight interval.
    /// </summary>
    public async Task<ServiceBooking.API.Services.Orders.OrderPickupContext> PickupContextAsync(
        Company shop, ShopSettings settings, DateTime nowUtc, CancellationToken ct = default)
    {
        var zone = ZoneOf(shop);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(nowUtc), zone));
        var schedule = await LoadScheduleAsync(shop, settings, zone, today, ct);
        var workingDay = new PickupSchedule(schedule, ShopOrderingGate.PickupSettingsOf(settings)).CurrentWorkingDay(nowUtc);
        return new ServiceBooking.API.Services.Orders.OrderPickupContext(zone, AsUtc(nowUtc), workingDay);
    }

    /// <summary>
    /// The pickup contexts of MANY shops (T-25-04): three queries — companies (zone), settings (hours, time settings) and the special days of
    /// the whole horizon — for any number of shops, so a list ("Мои заказы") does not query per row. Same working-day rule as the single form.
    /// </summary>
    public async Task<Dictionary<Guid, ServiceBooking.API.Services.Orders.OrderPickupContext>> PickupContextsAsync(
        IReadOnlyCollection<Guid> shopIds, DateTime nowUtc, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, ServiceBooking.API.Services.Orders.OrderPickupContext>();
        if (shopIds.Count == 0) return result;
        var ids = shopIds.Distinct().ToList();
        var utc = AsUtc(nowUtc);

        var zones = await db.Companies.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.TimeZoneId }).ToListAsync(ct);
        var settingsById = (await db.ShopSettings.AsNoTracking().Where(s => ids.Contains(s.CompanyId)).ToListAsync(ct))
            .ToDictionary(s => s.CompanyId);
        // Time zones span at most ±14 h, so the calendar date of any shop lies within a day of the UTC date.
        var utcDay = DateOnly.FromDateTime(utc);
        var from = utcDay.AddDays(-2);
        var to = utcDay.AddDays(LookAheadDays + 1);
        var specialRows = await db.ShopSpecialDays.AsNoTracking()
            .Where(d => ids.Contains(d.CompanyId) && d.Date >= from && d.Date <= to).ToListAsync(ct);
        var specialByShop = specialRows.GroupBy(r => r.CompanyId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var shop in zones)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);
            var settings = settingsById.GetValueOrDefault(shop.Id) ?? new ShopSettings { CompanyId = shop.Id };
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
            var special = (specialByShop.GetValueOrDefault(shop.Id) ?? [])
                .Where(r => r.Date >= today.AddDays(-1) && r.Date <= today.AddDays(LookAheadDays))
                .ToDictionary(
                    r => r.Date,
                    r => r.IsClosed ? SpecialDayHours.Closed : new SpecialDayHours(false, ShopScheduleRules.ParseIntervals(r.IntervalsJson)));
            var schedule = new ShopScheduleSnapshot(zone, ShopScheduleRules.Parse(settings.WorkingHoursJson), special);
            var workingDay = new PickupSchedule(schedule, ShopOrderingGate.PickupSettingsOf(settings)).CurrentWorkingDay(utc);
            result[shop.Id] = new ServiceBooking.API.Services.Orders.OrderPickupContext(zone, utc, workingDay);
        }
        return result;
    }

    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    public static TimeZoneInfo ZoneOf(Company shop) => TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
