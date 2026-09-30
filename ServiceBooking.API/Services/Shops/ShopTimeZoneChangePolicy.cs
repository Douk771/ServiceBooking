using System.Globalization;

namespace ServiceBooking.API.Services.Shops;

/// <summary>ARCHITECTURE_CYCLE26.md §548.2 — a shop with orders may only move to a city with the same UTC offset (order pickup text is drawn in the shop zone).</summary>
public static class ShopTimeZoneChangePolicy
{
    public static bool IsAllowed(string currentZoneId, string newZoneId, bool hasOrders, DateTime nowUtc)
    {
        if (string.Equals(currentZoneId, newZoneId, StringComparison.Ordinal)) return true;
        if (!hasOrders) return true;
        return TimeZoneOffset.TryGetUtcOffsetMinutes(currentZoneId, nowUtc, out var current) &&
               TimeZoneOffset.TryGetUtcOffsetMinutes(newZoneId, nowUtc, out var next) &&
               current == next;
    }

    public static string LockedText(int? currentOffsetMinutes) =>
        "У магазина уже есть заказы, поэтому часовой пояс сменить нельзя — сдвинулось бы время уже оформленных заказов. " +
        $"Выберите город в том же часовом поясе ({FormatOffset(currentOffsetMinutes ?? 0)}).";

    public static string FormatOffset(int minutes)
    {
        var sign = minutes < 0 ? "-" : "+";
        var abs = Math.Abs(minutes);
        var hours = abs / 60;
        var rest = abs % 60;
        return rest == 0
            ? $"UTC{sign}{hours.ToString(CultureInfo.InvariantCulture)}"
            : $"UTC{sign}{hours.ToString(CultureInfo.InvariantCulture)}:{rest:00}";
    }
}
