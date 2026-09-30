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

    /// <summary>One shop — the special case of <see cref="LoadManyAsync"/>, so the input of the acceptance rule is assembled by ONE piece of code.</summary>
    public async Task<ShopGateContext> LoadAsync(Company shop, DateTime nowUtc, CancellationToken ct = default, ShopSettings? settings = null)
    {
        var loaded = await LoadManyAsync([shop], nowUtc, ct, settings is null ? null : new Dictionary<Guid, ShopSettings> { [shop.Id] = settings });
        return loaded[shop.Id];
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE25.md §505.3 — the gate of MANY shops with a fixed number of queries (settings, special days of the whole horizon, tariffs, month
    /// counters — four, however many shops), each verdict from the same <see cref="ShopOrderingGate.Evaluate"/> as the storefront's. <paramref name="settingsById"/>
    /// and <paramref name="plansByAccount"/> let a caller that already holds them (the catalog reads the tariffs to filter first) skip that query.
    /// </summary>
    public async Task<Dictionary<Guid, ShopGateContext>> LoadManyAsync(
        IReadOnlyList<Company> shops, DateTime nowUtc, CancellationToken ct = default,
        IReadOnlyDictionary<Guid, ShopSettings>? settingsById = null, IReadOnlyDictionary<Guid, OrdersPlan>? plansByAccount = null)
    {
        var result = new Dictionary<Guid, ShopGateContext>();
        if (shops.Count == 0) return result;
        var ids = shops.Select(c => c.Id).ToList();
        var utc = AsUtc(nowUtc);

        var settingsMap = settingsById ?? await db.ShopSettings.AsNoTracking().Where(s => ids.Contains(s.CompanyId)).ToDictionaryAsync(s => s.CompanyId, ct);
        var specialRows = await LoadSpecialDaysAsync(ids, utc, ct);

        var accountIds = shops.Where(c => c.BillingAccountId != null).Select(c => c.BillingAccountId!.Value).Distinct().ToList();
        var plansMap = plansByAccount ?? (accountIds.Count == 0 ? new Dictionary<Guid, OrdersPlan>() : await plans.GetForAccountsAsync(accountIds, ct));

        var todays = shops.ToDictionary(c => c.Id, c => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, ZoneOf(c))));
        var months = todays.Values.Select(MonthStart).Distinct().ToList();
        var usage = accountIds.Count == 0
            ? new Dictionary<(Guid, DateOnly), int>()
            : (await db.OrderMonthlyUsages.AsNoTracking().Where(u => accountIds.Contains(u.BillingAccountId) && months.Contains(u.Month))
                .Select(u => new { u.BillingAccountId, u.Month, u.Count }).ToListAsync(ct)).ToDictionary(u => (u.BillingAccountId, u.Month), u => u.Count);

        foreach (var shop in shops)
        {
            var rowExists = settingsMap.TryGetValue(shop.Id, out var found);
            var settings = found ?? new ShopSettings { CompanyId = shop.Id };
            var zone = ZoneOf(shop);
            var today = todays[shop.Id];
            var schedule = new ShopScheduleSnapshot(zone, ShopScheduleRules.Parse(settings.WorkingHoursJson), SpecialFor(specialRows, shop.Id, today));
            var plan = shop.BillingAccountId is { } accountId ? plansMap[accountId] : OrdersPlan.FallbackFree;
            var used = shop.BillingAccountId is { } acc ? usage.GetValueOrDefault((acc, MonthStart(today))) : 0;

            var gate = ShopOrderingGate.Evaluate(new ShopGateInput(shop, settings, schedule, plan, used, nowUtc));
            result[shop.Id] = new ShopGateContext(shop, settings, plan, schedule, nowUtc, gate, rowExists);
        }
        return result;
    }

    /// <summary>
    /// The special days of many shops for the whole horizon in ONE query. Time zones span at most ±14 h, so the calendar date of any shop lies within a day
    /// of the UTC date; each shop then keeps its own window (<c>today − 1</c> … <see cref="LookAheadDays"/>).
    /// </summary>
    private async Task<Dictionary<Guid, List<ShopSpecialDay>>> LoadSpecialDaysAsync(IReadOnlyList<Guid> shopIds, DateTime utc, CancellationToken ct)
    {
        var utcDay = DateOnly.FromDateTime(utc);
        var from = utcDay.AddDays(-2);
        var to = utcDay.AddDays(LookAheadDays + 1);
        var rows = await db.ShopSpecialDays.AsNoTracking().Where(d => shopIds.Contains(d.CompanyId) && d.Date >= from && d.Date <= to).ToListAsync(ct);
        return rows.GroupBy(r => r.CompanyId).ToDictionary(g => g.Key, g => g.ToList());
    }

    private static Dictionary<DateOnly, SpecialDayHours> SpecialFor(Dictionary<Guid, List<ShopSpecialDay>> rowsByShop, Guid shopId, DateOnly today) =>
        (rowsByShop.GetValueOrDefault(shopId) ?? [])
            .Where(r => r.Date >= today.AddDays(-1) && r.Date <= today.AddDays(LookAheadDays))
            .ToDictionary(
                r => r.Date,
                r => r.IsClosed ? SpecialDayHours.Closed : new SpecialDayHours(false, ShopScheduleRules.ParseIntervals(r.IntervalsJson)));

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
        var settingsById = await db.ShopSettings.AsNoTracking().Where(s => ids.Contains(s.CompanyId)).ToDictionaryAsync(s => s.CompanyId, ct);
        var specialRows = await LoadSpecialDaysAsync(ids, utc, ct);

        foreach (var shop in zones)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);
            var settings = settingsById.GetValueOrDefault(shop.Id) ?? new ShopSettings { CompanyId = shop.Id };
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
            var schedule = new ShopScheduleSnapshot(zone, ShopScheduleRules.Parse(settings.WorkingHoursJson), SpecialFor(specialRows, shop.Id, today));
            var workingDay = new PickupSchedule(schedule, ShopOrderingGate.PickupSettingsOf(settings)).CurrentWorkingDay(utc);
            result[shop.Id] = new ServiceBooking.API.Services.Orders.OrderPickupContext(zone, utc, workingDay);
        }
        return result;
    }

    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    public static TimeZoneInfo ZoneOf(Company shop) => TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
