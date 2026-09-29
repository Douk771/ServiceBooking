using System.Text.Json;
using System.Text.Json.Serialization;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders;

/// <summary>One side of a line change: how the line looked before or after.</summary>
public sealed record ChangeSide(int Qty, decimal UnitPrice, ProductUnit Unit);

/// <summary>An edit entry stored in <c>OrderEvent.ChangesJson</c>: null Before = the line was added, null After = removed.</summary>
public sealed record ChangeEntry(string Name, ChangeSide? Before, ChangeSide? After);

/// <summary>An issue entry: the write-off of stock for one line (only lines that reserved stock).</summary>
public sealed record StockWriteOff(string Name, ProductUnit Unit, int Requested, int Written, bool Zeroed);

/// <summary>The <c>ChangesJson</c> of an Issued event.</summary>
public sealed record IssueLog(List<StockWriteOff> Stock);

/// <summary>One side of a pickup change (ARCHITECTURE_CYCLE24.md §451.4): how the pickup looked before / after, with the order number of that side.</summary>
public sealed record PickupSide(PickupKind Kind, DateOnly Date, DateTime StartUtc, DateTime? EndUtc, int Number);

/// <summary>The <c>ChangesJson</c> of a PickupChanged event.</summary>
public sealed record PickupChangeLog(PickupSide Before, PickupSide After);

/// <summary>ARCHITECTURE_CYCLE23.md §388.2 — (de)serialization of <c>OrderEvent.ChangesJson</c>, in one place.</summary>
public static class OrderChangeLog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SerializeEdit(IReadOnlyList<ChangeEntry> entries) => JsonSerializer.Serialize(entries, Options);

    public static string SerializeIssue(IssueLog log) => JsonSerializer.Serialize(log, Options);

    public static string SerializePickup(PickupChangeLog log) => JsonSerializer.Serialize(log, Options);

    public static PickupChangeLog? ParsePickup(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PickupChangeLog>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static List<ChangeEntry> ParseEdit(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ChangeEntry>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IssueLog? ParseIssue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<IssueLog>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
