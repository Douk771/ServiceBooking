using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders.Reports;

/// <summary>One line of an order in the pick list. Positions of the catalog order (category, product) — <see cref="int.MaxValue"/> when the product is deleted or has no category.</summary>
public sealed record PickListItemInput(
    Guid? ProductId, string Name, ProductUnit Unit, int Quantity, string? PortionText, string? CategoryName, int CategoryPosition, int ProductPosition);

public sealed record PickListOrderInput(
    Guid OrderId, int Number, OrderStatus Status, PickupKind PickupKind, DateTime PickupStartUtc, DateTime CreatedAtUtc, string? Comment,
    IReadOnlyList<PickListItemInput> Items);

public sealed record PickListProductRow(
    Guid? ProductId, string Name, string? CategoryName, ProductUnit Unit, int TotalQuantity, string QuantityText, int OrderCount,
    string? WeightBreakdownText, bool HasUnaccepted);

public sealed record PickListLineRow(string Name, ProductUnit Unit, int Quantity, string QuantityText, string? PortionText);

public sealed record PickListOrderRow(
    Guid OrderId, int Number, OrderStatus Status, bool IsUnaccepted, string PickupText, string? Comment, IReadOnlyList<PickListLineRow> Lines);

/// <summary>A group of the "by time" view; <see cref="From"/>/<see cref="To"/> are null for the "Вне расписания" group.</summary>
public sealed record PickListTimeGroup(string? From, string? To, string Label, IReadOnlyList<PickListOrderRow> Orders);

public sealed record PickListResult(IReadOnlyList<PickListProductRow> ByProduct, IReadOnlyList<PickListTimeGroup> ByTime);

/// <summary>
/// ARCHITECTURE_CYCLE25.md §503 — grouping of a pick list. Pure. No customer name or phone anywhere in its input or output [legal L18].
/// </summary>
public static class PickListBuilder
{
    public const string OutOfScheduleLabel = "Вне расписания";

    public static PickListResult Build(IReadOnlyList<PickListOrderInput> orders, IReadOnlyList<PickupSlot> slots, TimeZoneInfo zone)
    {
        var ordered = orders.OrderBy(o => o.PickupStartUtc).ThenBy(o => o.CreatedAtUtc).ThenBy(o => o.Number).ToList();
        return new PickListResult(ByProduct(ordered), ByTime(ordered, slots, zone));
    }

    private static List<PickListProductRow> ByProduct(List<PickListOrderInput> orders)
    {
        // Key: (ProductId ?? name, unit) — a renamed product stays one row.
        var groups = new Dictionary<(string Key, ProductUnit Unit), List<(PickListOrderInput Order, PickListItemInput Item)>>();
        foreach (var order in orders)
            foreach (var item in order.Items)
            {
                var key = (item.ProductId?.ToString() ?? "n:" + item.Name, item.Unit);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
                list.Add((order, item));
            }

        var ru = StringComparer.Create(new System.Globalization.CultureInfo("ru-RU"), ignoreCase: true);
        return groups.Values
            .Select(list =>
            {
                var first = list[0].Item;
                var total = list.Sum(t => t.Item.Quantity);
                var perOrder = list.GroupBy(t => t.Order.OrderId).Select(g => g.Sum(t => t.Item.Quantity)).ToList();
                var breakdown = first.Unit == ProductUnit.Weight
                    ? $"{perOrder.Count} {ShopTimeTexts.Plural(perOrder.Count, "заказ", "заказа", "заказов")}: " +
                      string.Join(", ", perOrder.Select(q => OrderTexts.QuantityCompact(ProductUnit.Weight, q)))
                    : null;
                var row = new PickListProductRow(
                    first.ProductId, first.Name, first.CategoryName, first.Unit, total, OrderTexts.QuantityCompact(first.Unit, total),
                    perOrder.Count, breakdown, list.Any(t => t.Order.Status == OrderStatus.New));
                return (Row: row, first.CategoryPosition, first.ProductPosition);
            })
            .OrderBy(t => t.CategoryPosition).ThenBy(t => t.ProductPosition).ThenBy(t => t.Row.Name, ru).ThenBy(t => t.Row.ProductId)
            .Select(t => t.Row)
            .ToList();
    }

    private static List<PickListTimeGroup> ByTime(List<PickListOrderInput> orders, IReadOnlyList<PickupSlot> slots, TimeZoneInfo zone)
    {
        var groups = new List<PickListTimeGroup>();
        var outside = new List<PickListOrderRow>();
        var bySlot = new Dictionary<int, List<PickListOrderRow>>();

        foreach (var order in orders)
        {
            var row = ToRow(order, zone);
            var index = -1;
            for (var i = 0; i < slots.Count; i++)
                if (slots[i].StartUtc <= order.PickupStartUtc && order.PickupStartUtc < slots[i].EndUtc) { index = i; break; }
            if (index < 0) { outside.Add(row); continue; }
            if (!bySlot.TryGetValue(index, out var list)) bySlot[index] = list = [];
            list.Add(row);
        }

        foreach (var (index, list) in bySlot.OrderBy(p => p.Key))
        {
            var slot = slots[index];
            groups.Add(new PickListTimeGroup(Hhmm(slot.StartUtc, zone), Hhmm(slot.EndUtc, zone), slot.Label, list));
        }
        if (outside.Count > 0) groups.Add(new PickListTimeGroup(null, null, OutOfScheduleLabel, outside));
        return groups;
    }

    private static PickListOrderRow ToRow(PickListOrderInput order, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(order.PickupStartUtc), zone);
        var clock = ShopTimeTexts.Clock(local.Hour * 60 + local.Minute);
        var pickupText = order.PickupKind == PickupKind.Asap ? $"≈ {clock}" : $"к {clock}";
        return new PickListOrderRow(
            order.OrderId, order.Number, order.Status, order.Status == OrderStatus.New, pickupText, order.Comment,
            order.Items.Select(i => new PickListLineRow(i.Name, i.Unit, i.Quantity, OrderTexts.QuantityCompact(i.Unit, i.Quantity), i.PortionText)).ToList());
    }

    public static string Hhmm(DateTime utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone);
        return ShopTimeTexts.Hhmm(local.Hour * 60 + local.Minute);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
