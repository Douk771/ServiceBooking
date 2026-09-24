namespace ServiceBooking.API.Services.Geo;

/// <summary>
/// The one normalization function for the whole address-verification feature (ARCHITECTURE_CYCLE13.md
/// §203, R5/R9). Pure, no DB, no network. Its input is ALWAYS our own text (the owner's own address
/// field, or our own outbound query string) — NEVER the geocoder's response: that distinction is the
/// entire reason cycle 13's model can hold the owner's text under the standard Yandex licence at all
/// (§202, R18).
///
/// One function, three legal uses of its output:
/// 1. comparison — "is the current Address still the one we verified" (both operands are ours);
/// 2. the geocoder cache key (§209.2) — keyed by our own outbound query;
/// 3. the value stored in <c>Company.AddressVerifiedInputKey</c> — our own text, byte-derived.
/// </summary>
public static class AddressNormalization
{
    /// <summary>Trim, collapse any run of whitespace to a single space, lowercase (invariant), fold 'ё'
    /// to 'е', and drop a single trailing '.'/',' (repeated to also catch "..,"/",.," etc. — a comma-dot
    /// tail collapses to nothing, not to one leftover character). Empty/whitespace/null input yields "".</summary>
    public static string Key(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "";

        var collapsed = CollapseWhitespace(address.Trim()).ToLowerInvariant().Replace('ё', 'е');

        var end = collapsed.Length;
        while (end > 0 && (collapsed[end - 1] == '.' || collapsed[end - 1] == ',')) end--;

        return collapsed[..end];
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                builder.Append(c);
                lastWasSpace = false;
            }
        }

        return builder.ToString();
    }
}
