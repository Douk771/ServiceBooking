namespace ServiceBooking.API.Services.Companies;

public enum MapLinkService { Yandex, TwoGis }

/// <summary>
/// ARCHITECTURE_CYCLE15.md §253.2/§253.3, API_CONTRACT_CYCLE15.md §284 — the SINGLE place in the
/// product that decides whether a map link the owner pasted in is acceptable. Called from exactly one
/// write path (CompaniesController.Update) — see the architecture doc for why POST /api/companies and
/// PUT /api/admin/companies/{id} deliberately don't get a second copy of this check.
///
/// Every rule below is a direct consequence of "we show this link to every anonymous visitor of the
/// public company page" (R10, open-redirect risk): the domain allow-list is closed and exact, the path/
/// query/fragment are preserved byte-for-byte (never re-encoded via Uri.ToString(), which would rewrite
/// percent-escapes like %2C), and a bare "*.yandex.*" wildcard is deliberately NOT how the Yandex branch
/// is implemented — that would also accept https://yandex.evil.com/….
/// </summary>
public static class MapLinkValidation
{
    public const int MaxLength = 500;

    // ARCHITECTURE_CYCLE15.md §253.3 — the closed list of Yandex TLDs the product accepts. Extending it
    // is a one-line change to this array plus one new test (§253.3's own promise), never a wildcard.
    private static readonly string[] YandexTlds =
    [
        "ru", "com", "com.tr", "by", "kz", "uz", "az", "com.ge", "ee", "lv", "lt", "md", "fr", "com.am", "tm", "tj", "kg",
    ];

    /// <summary>
    /// Ok(null) = the field was cleared (empty/whitespace input) — a valid, obtainable result, not an
    /// error. Ok(value) = the trimmed, unmodified input is safe to store. Error(message) = the caller
    /// should answer 400 with this exact text (API_CONTRACT_CYCLE15.md §283's table).
    /// </summary>
    public static bool TryNormalize(string? raw, MapLinkService service, out string? value, out string? error)
    {
        value = null;
        error = null;

        // §253.2 п.2 — "" or whitespace-only clears the field. Whoever calls this decides whether `raw`
        // is null at all (null = "field not sent, don't touch") — this method only ever sees a non-null
        // string when the field WAS sent.
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return true;

        // §253.2 п.3 — control characters / embedded whitespace anywhere in the trimmed string are
        // rejected outright (this also catches \r\n header-injection-shaped input before it ever
        // reaches Uri.TryCreate).
        foreach (var ch in trimmed)
        {
            if (ch < 0x20 || char.IsWhiteSpace(ch))
            {
                error = "Ссылка должна начинаться с https://";
                return false;
            }
        }

        // §253.2 п.4 — length is checked on the ALREADY-TRIMMED string; no silent truncation.
        if (trimmed.Length > MaxLength)
        {
            error = "Ссылка слишком длинная — не больше 500 символов";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = "Ссылка должна начинаться с https://";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Ссылка должна начинаться с https://";
            return false;
        }

        // §253.2 п.7 — a non-empty userinfo (https://user:pass@…) or a non-default port is rejected;
        // System.Uri already normalizes Host to lower-case and decodes IDN, so the comparisons below
        // don't need their own case-folding.
        if (uri.UserInfo.Length > 0 || (!uri.IsDefaultPort && uri.Port != 443))
        {
            error = DomainErrorFor(service);
            return false;
        }

        if (!BelongsToService(uri.Host, service))
        {
            error = DomainErrorFor(service);
            return false;
        }

        // §253.2 п.9 — path/query/fragment are preserved exactly as the owner typed them. `uri.ToString()`
        // would re-normalize percent-escapes (e.g. %2C -> ,), which 0-bis П2 explicitly forbids.
        value = trimmed;
        return true;
    }

    private static string DomainErrorFor(MapLinkService service) => service switch
    {
        MapLinkService.Yandex => "Ждём ссылку на Яндекс Карты — например, https://yandex.ru/maps/org/.../...",
        MapLinkService.TwoGis => "Ждём ссылку на 2ГИС — например, https://2gis.ru/barnaul/firm/...",
        _ => throw new ArgumentOutOfRangeException(nameof(service), service, null),
    };

    private static bool BelongsToService(string host, MapLinkService service) => service switch
    {
        MapLinkService.TwoGis =>
            string.Equals(host, "2gis.ru", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".2gis.ru", StringComparison.OrdinalIgnoreCase),
        MapLinkService.Yandex => YandexTlds.Any(tld =>
            string.Equals(host, "yandex." + tld, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + "yandex." + tld, StringComparison.OrdinalIgnoreCase)),
        _ => throw new ArgumentOutOfRangeException(nameof(service), service, null),
    };
}
