using System.Text.Json;
using ServiceBooking.API.DTOs.Stays;

namespace ServiceBooking.API.Services.Stays;

/// <summary>The jsonb snapshots of a session: the price of every hour and the chosen positions (name and price at the moment of the choice).</summary>
public static class ServiceJson
{
    public sealed record HourPriceSnapshot(int StartMinute, int PriceRub);

    public sealed record ItemSnapshot(Guid ItemId, string Name, int UnitPriceRub, int Quantity, int AmountRub);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string HourPrices(int startMinute, IReadOnlyList<int> prices) =>
        JsonSerializer.Serialize(prices.Select((p, k) => new HourPriceSnapshot(startMinute + 60 * k, p)), Options);

    public static string Items(IEnumerable<ResolvedItem> items) =>
        JsonSerializer.Serialize(items.Select(i => new ItemSnapshot(i.ItemId, i.Name, i.UnitPriceRub, i.Quantity, i.UnitPriceRub * i.Quantity)), Options);

    public static List<HourPriceSnapshot> ReadHourPrices(string json) => JsonSerializer.Deserialize<List<HourPriceSnapshot>>(json, Options) ?? [];

    public static List<ItemSnapshot> ReadItems(string json) => JsonSerializer.Deserialize<List<ItemSnapshot>>(json, Options) ?? [];

    public static List<ServiceWindowSnapshot> ReadWindows(string json) => JsonSerializer.Deserialize<List<ServiceWindowSnapshot>>(json, Options) ?? [];

    public static string Windows(IEnumerable<WindowSpec> windows) => JsonSerializer.Serialize(windows.Select(w => new ServiceWindowSnapshot(w.StartMinute, w.EndMinute)), Options);

    public sealed record ServiceWindowSnapshot(int StartMinute, int EndMinute);
}
