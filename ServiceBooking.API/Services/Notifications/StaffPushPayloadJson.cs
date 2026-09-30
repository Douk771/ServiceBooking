using System.Text.Encodings.Web;
using System.Text.Json;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// API_CONTRACT_CYCLE33.md §33.27 — serialises the <c>{ title, body, tag, url }</c> body of a staff push. Cyrillic is written as is
/// (no \uXXXX), and the whole JSON is kept within the 1000-character column by shortening <c>body</c> with "…".
/// </summary>
public static class StaffPushPayloadJson
{
    public const int MaxLength = 1000;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Build(string title, string body, string tag, string url)
    {
        var json = Serialize(title, body, tag, url);
        if (json.Length <= MaxLength) return json;

        // Escaping can make a character longer than one, so shorten by the measured overflow until it fits.
        var keep = body.Length;
        while (keep > 0 && json.Length > MaxLength)
        {
            keep = Math.Max(0, keep - Math.Max(1, json.Length - MaxLength));
            json = Serialize(title, body[..keep] + "…", tag, url);
        }
        return json;
    }

    private static string Serialize(string title, string body, string tag, string url) =>
        JsonSerializer.Serialize(new { title, body, tag, url }, Options);
}
