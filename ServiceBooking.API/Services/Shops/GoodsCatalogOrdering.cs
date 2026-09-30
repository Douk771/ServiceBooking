using System.Globalization;

namespace ServiceBooking.API.Services.Shops;

public enum CatalogAcceptance
{
    AcceptingNow,
    PreorderOnly,
    NotAccepting
}

/// <summary>
/// ARCHITECTURE_CYCLE25.md §505.3 — the pure parts of the goods catalog: the three acceptance groups, their texts, the order (group, then name in the
/// Russian culture, then id), the search match and the page size. The acceptance itself comes from <see cref="ShopOrderingGate"/>; this class only
/// classifies its result, so the catalog and the storefront cannot disagree about "takes orders".
/// </summary>
public static class GoodsCatalogOrdering
{
    public const int MaxSearchLength = 100;
    private static readonly CultureInfo Ru = new("ru-RU");

    /// <summary>Group 1: accepting ∧ ASAP available; group 2: accepting ∧ no ASAP ∧ a slot is available; group 3: everything else (pause, switch, limit, tariff, no time).</summary>
    public static CatalogAcceptance Classify(bool accepting, bool asapAvailable, bool scheduledAvailable)
    {
        if (accepting && asapAvailable) return CatalogAcceptance.AcceptingNow;
        if (accepting && scheduledAvailable) return CatalogAcceptance.PreorderOnly;
        return CatalogAcceptance.NotAccepting;
    }

    public static string Text(CatalogAcceptance acceptance) => acceptance switch
    {
        CatalogAcceptance.AcceptingNow => "Принимает заказы",
        CatalogAcceptance.PreorderOnly => "Можно заказать заранее",
        _ => "Временно не принимает заказы"
    };

    /// <summary>Group → name (Russian culture, case-insensitive) → id.</summary>
    public static List<T> Order<T>(IEnumerable<T> items, Func<T, CatalogAcceptance> acceptance, Func<T, string> name, Func<T, Guid> id) =>
        items.OrderBy(i => (int)acceptance(i))
            .ThenBy(i => name(i), StringComparer.Create(Ru, ignoreCase: true))
            .ThenBy(id)
            .ToList();

    /// <summary>The search text: trimmed, at most 100 characters (longer is cut, like <c>companies/public</c>); null when empty.</summary>
    public static string? NormalizeSearch(string? search)
    {
        var text = search?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length > MaxSearchLength ? text[..MaxSearchLength] : text;
    }

    /// <summary>Case-insensitive "contains" in the name or the address.</summary>
    public static bool Matches(string name, string? address, string search) =>
        Ru.CompareInfo.IndexOf(name, search, CompareOptions.IgnoreCase) >= 0 ||
        (address is not null && Ru.CompareInfo.IndexOf(address, search, CompareOptions.IgnoreCase) >= 0);

    /// <summary>A page number: below 1 (or unparsable, passed as 0) becomes 1.</summary>
    public static int NormalizePage(int? page) => page is >= 1 ? page.Value : 1;
}
