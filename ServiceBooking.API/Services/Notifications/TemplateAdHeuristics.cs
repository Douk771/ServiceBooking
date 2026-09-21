namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// T5-B12 (ARCHITECTURE_CYCLE5.md §51.2, US-70). Deliberately a dictionary of substrings, not NLP — the
/// same word list the frontend receives from <c>PlatformSetting["notifications.template.ad-markers"]</c>
/// via <c>GET .../notification-templates</c>'s `adMarkers` field, so a suggestion shown before saving and
/// the server's own final check can never drift apart (§51.2: "фронт ничего не решает"). Pure, no DB.
/// </summary>
public static class TemplateAdHeuristics
{
    public static IReadOnlyList<string> Scan(string body, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrEmpty(body) || markers.Count == 0) return [];

        var hits = new List<string>();
        foreach (var marker in markers)
        {
            if (string.IsNullOrWhiteSpace(marker)) continue;
            if (body.Contains(marker, StringComparison.OrdinalIgnoreCase))
                hits.Add(marker);
        }
        return hits;
    }
}
