using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders.Reports;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §503, API_CONTRACT_CYCLE25.md §528 — the pick list: the accepted (and, on request, new) orders of one pickup day and interval, grouped
/// by product and by time. One indexed query (the §451.5 selection widened to <c>Accepted</c> + optionally <c>New</c>); the grouping is the pure
/// <see cref="PickListBuilder"/>. NO customer name or phone is read or returned [legal L18]; the order comment is.
/// </summary>
public sealed class PickListService(
    AppDbContext db, ShopGateLoader gates, ServiceBooking.API.Services.Notifications.INotificationClock clock)
{
    public const string TimeFormatText = "Укажите время в формате ЧЧ:ММ";
    public const string IntervalBothText = "Укажите начало и конец интервала";
    public const string WholeDayLabel = "Весь день";

    public async Task<(string? Error, PickListDto? List)> BuildAsync(
        Company shop, DateOnly? date, string? from, string? to, bool includeNew, CancellationToken ct)
    {
        var nowUtc = clock.UtcNow;
        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shop.Id, ct) ?? new ShopSettings { CompanyId = shop.Id };
        var zone = ShopGateLoader.ZoneOf(shop);
        var context = await gates.PickupContextAsync(shop, settings, nowUtc, ct);
        var workingDay = context.WorkingDay;

        var day = date ?? workingDay;
        var latest = workingDay.AddDays(Math.Max(0, settings.PreorderDays));
        if (day > latest) return ($"Дата — не позже {ShopTimeTexts.DateLong(latest)}", null);

        // The schedule is anchored at the requested day, so its special days (and a past day's hours) are the ones loaded.
        var schedule = await gates.LoadScheduleAsync(shop, settings, zone, day, ct);
        var pickup = new PickupSchedule(schedule, ShopOrderingGate.PickupSettingsOf(settings));

        // ── the interval (local time of the working day `day`; end ≤ start goes through midnight, "00:00" as the end is midnight) ──
        var whole = from is null && to is null;
        DateTime fromUtc, toUtc;
        string? fromText = null, toText = null;
        if (whole)
        {
            fromUtc = PickupSchedule.ToUtc(zone, day, 0);
            var midnight = PickupSchedule.ToUtc(zone, day, 1440);
            var lastEnd = pickup.IntervalsFor(day).Select(i => i.EndUtc).DefaultIfEmpty(midnight).Max();
            toUtc = lastEnd > midnight ? lastEnd : midnight;
        }
        else
        {
            if (from is null || to is null) return (IntervalBothText, null);
            if (!TryParseClock(from, out var fromMinutes) || !TryParseClock(to, out var toMinutes)) return (TimeFormatText, null);
            var endMinutes = toMinutes == 0 ? 1440 : toMinutes <= fromMinutes ? toMinutes + 1440 : toMinutes;
            fromUtc = PickupSchedule.ToUtc(zone, day, fromMinutes);
            toUtc = PickupSchedule.ToUtc(zone, day, endMinutes);
            fromText = ShopTimeTexts.Hhmm(fromMinutes);
            toText = ShopTimeTexts.Hhmm(toMinutes);
        }

        var statuses = includeNew ? new[] { OrderStatus.Accepted, OrderStatus.New } : new[] { OrderStatus.Accepted };
        var orders = await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.CompanyId == shop.Id && o.PickupDate == day && statuses.Contains(o.Status) && o.PickupStartUtc >= fromUtc && o.PickupStartUtc < toUtc)
            .OrderBy(o => o.PickupStartUtc).ThenBy(o => o.CreatedAtUtc).ThenBy(o => o.Number)
            .AsSplitQuery().ToListAsync(ct);

        var inputs = await ToInputsAsync(shop.Id, orders, ct);
        var slots = pickup.DaySlots(day);
        var result = PickListBuilder.Build(inputs, slots, zone);

        var intervalLabel = whole ? WholeDayLabel : $"{fromText}–{toText}";
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc.Kind == DateTimeKind.Utc ? nowUtc : DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        return (null, new PickListDto(
            shop.Name, day, ShopTimeTexts.DateWithWeekday(day), fromText, toText, intervalLabel, includeNew,
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), $"по состоянию на {ShopTimeTexts.Clock(localNow.Hour * 60 + localNow.Minute)}", orders.Count,
            slots.Select(s => new PickListSlotOptionDto(PickListBuilder.Hhmm(s.StartUtc, zone), PickListBuilder.Hhmm(s.EndUtc, zone), s.Label)).ToList(),
            result.ByProduct.Select(p => new PickListProductDto(
                p.ProductId, p.Name, p.CategoryName, p.Unit, p.TotalQuantity, p.QuantityText, p.OrderCount, p.WeightBreakdownText, p.HasUnaccepted)).ToList(),
            result.ByTime.Select(g => new PickListTimeGroupDto(g.From, g.To, g.Label, g.Orders.Select(o => new PickListOrderDto(
                o.OrderId, o.Number, o.Status, o.IsUnaccepted, o.PickupText, o.Comment,
                o.Lines.Select(l => new PickListLineDto(l.Name, l.Unit, l.Quantity, l.QuantityText, l.PortionText)).ToList())).ToList())).ToList(),
            orders.Count > 0 ? null : whole ? $"На {ShopTimeTexts.DateLong(day)} заказов нет" : $"На {intervalLabel} заказов нет"));
    }

    /// <summary>"HH:mm" (also "H:mm"), 00:00–23:59 → minutes of the day.</summary>
    public static bool TryParseClock(string text, out int minutes)
    {
        minutes = 0;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2 || parts[0].Length is < 1 or > 2 || parts[1].Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) || h > 23 || m > 59)
            return false;
        minutes = h * 60 + m;
        return true;
    }

    /// <summary>Order lines with the CURRENT name and the catalogue position of their product; a deleted product (or one without a category) goes last, by name.</summary>
    private async Task<List<PickListOrderInput>> ToInputsAsync(Guid shopId, List<Order> orders, CancellationToken ct)
    {
        var productIds = orders.SelectMany(o => o.Items).Where(i => i.ProductId != null).Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = productIds.Count == 0
            ? []
            : await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.CategoryId, p.Position, Deleted = p.DeletedAtUtc != null }).ToDictionaryAsync(p => p.Id, ct);
        var categories = await db.ProductCategories.AsNoTracking().Where(c => c.CompanyId == shopId)
            .Select(c => new { c.Id, c.Name, c.Position }).ToDictionaryAsync(c => c.Id, ct);

        return orders.Select(o => new PickListOrderInput(
            o.Id, o.Number, o.Status, o.PickupKind, o.PickupStartUtc, o.CreatedAtUtc, o.Comment,
            o.Items.OrderBy(i => i.Position).Select(i =>
            {
                var product = i.ProductId is { } id ? products.GetValueOrDefault(id) : null;
                var live = product is { Deleted: false };
                var category = live && product!.CategoryId is { } cid ? categories.GetValueOrDefault(cid) : null;
                return new PickListItemInput(
                    i.ProductId, live ? product!.Name : i.NameSnapshot, i.Unit, i.QuantityOrdered, i.PortionTextSnapshot, category?.Name,
                    category?.Position ?? int.MaxValue, category is null ? int.MaxValue : product!.Position);
            }).ToList())).ToList();
    }
}
