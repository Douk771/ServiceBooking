using System.Text.Json;
using ServiceBooking.UnitTests.Cycle40;

namespace ServiceBooking.UnitTests.Cycle43;

/// <summary>Reads contracts/cycle43/messenger-vectors.json — the reference of the cycle-43 pure rules (ARCHITECTURE_CYCLE43.md §43.10).</summary>
internal static class MessengerVectors
{
    private static readonly JsonElement Root = ContractFiles.Load("cycle43", "messenger-vectors.json").RootElement.Clone();

    public static DateTime Now => Root.GetProperty("now").GetDateTime().ToUniversalTime();

    public static IEnumerable<object[]> Indexes(string section) =>
        Enumerable.Range(0, Root.GetProperty(section).GetArrayLength()).Select(i => new object[] { i });

    public static JsonElement Input(string section, int index) => Root.GetProperty(section)[index].GetProperty("input");
    public static JsonElement Expect(string section, int index) => Root.GetProperty(section)[index].GetProperty("expect");

    public static string Name(string section, int index) =>
        Root.GetProperty(section)[index].TryGetProperty("name", out var n) ? n.GetString()! : $"{section}[{index}]";
}
