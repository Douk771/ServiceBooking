using System.Text.Json;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>Reads contracts/cycle40/channel-vectors.json — the one file shared with the frontend (API_CONTRACT_CYCLE40.md §40.39).</summary>
internal static class ChannelVectors
{
    private static readonly JsonElement Root = ContractFiles.Load("cycle40", "channel-vectors.json").RootElement.Clone();

    public static DateTime Now => Root.GetProperty("now").GetDateTime().ToUniversalTime();

    public static JsonElement Case(string section, int index) => Root.GetProperty(section)[index];

    public static IEnumerable<object[]> Cases(string section) =>
        Root.GetProperty(section).EnumerateArray().Select((c, i) => new object[] { i, c.GetProperty("name").GetString()! });

    public static JsonElement Input(string section, int index) => Case(section, index).GetProperty("input");
    public static JsonElement Expect(string section, int index) => Case(section, index).GetProperty("expect");

    public static string? Str(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;

    public static bool Bool(this JsonElement e, string name) => e.GetProperty(name).GetBoolean();

    public static bool? NullableBool(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetBoolean() : null;

    public static int? NullableInt(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetInt32() : null;

    public static decimal? NullableDecimal(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetDecimal() : null;

    public static DateTime? Date(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetDateTime().ToUniversalTime() : null;

    public static T Enum<T>(this JsonElement e, string name) where T : struct, Enum => System.Enum.Parse<T>(e.GetProperty(name).GetString()!);

    public static T? NullableEnum<T>(this JsonElement e, string name) where T : struct, Enum =>
        e.Str(name) is { } s ? System.Enum.Parse<T>(s) : null;

    public static NotificationTransport[] Transports(this JsonElement e, string name) =>
        e.GetProperty(name).EnumerateArray().Select(x => System.Enum.Parse<NotificationTransport>(x.GetString()!)).ToArray();
}
