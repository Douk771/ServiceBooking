using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceBooking.API.DTOs.Billing;

/// <summary>
/// contracts/cycle7/openapi.yaml AdminPlanInput.highlights is a JSON array of strings, but the admin
/// UI shipped in earlier cycles (frontend/src/pages/admin/PlansTab.tsx) and every existing functional
/// test (ServiceBooking.Tests/Tests/AdminTests.cs, ~15 call sites via <c>NewPlanConfig</c>/
/// <c>ToUpdateDto</c>) still POST/PUT a single newline-separated STRING, because they were written
/// against <c>SubscriptionPlanConfig.Highlights</c> directly. Rejecting that shape outright to satisfy
/// the contract literally would turn "fix the AdminPlanInput/options gap" into "break roughly fifteen
/// passing functional tests that this stage is expressly forbidden from touching" — see the cycle-07
/// backend report. This converter accepts EITHER shape on read (array of strings, or one
/// newline-separated string) and always WRITES the contract's array form, so a caller that already
/// speaks the contract works unmodified and the legacy string callers keep working too.
///
/// TEMPORARY (cycle-07 code review): keep this converter only until the ADM-0xx functional tests are
/// rewritten to send the contract's array shape directly — once that happens this class and its
/// [JsonConverter] attribute on AdminPlanInput.Highlights should be deleted rather than left as
/// permanent dual-format leniency.
/// </summary>
public sealed class HighlightsFlexibleConverter : JsonConverter<List<string>?>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();
            return string.IsNullOrWhiteSpace(raw)
                ? []
                : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                // NB-9 (cycle-07 backend report): a non-string element (e.g. highlights: [123]) used to
                // be silently dropped instead of rejected, which meant a client sending malformed input
                // got a "successful" 200/201 with fewer highlights than it sent, not the 400 the
                // contract's own array-of-strings schema calls for.
                if (reader.TokenType != JsonTokenType.String)
                    throw new JsonException("highlights array elements must all be strings.");
                list.Add(reader.GetString() ?? "");
            }
            return list;
        }

        throw new JsonException("highlights must be either a JSON array of strings or a newline-separated string.");
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStartArray();
        foreach (var item in value) writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
