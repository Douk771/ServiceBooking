namespace ServiceBooking.LegalKit;

/// <summary>
/// Pure comparison of the operator requisites on the site (<c>contracts/legal/operator-public.json</c>) with <c>legal.values.json</c>
/// (ТЛ48-06, API_CONTRACT_CYCLE48_STOREFRONT.md §48.29): for every key shared by both, the values must be strictly equal — ordinal, no trim, no normalisation.
/// A key that is missing from <c>legal.values.json</c> is a difference too. No I/O.
/// </summary>
internal static class RequisitesDrift
{
    /// <summary>One shared key whose values differ. <see cref="LegalValue"/> is <c>null</c> when <c>legal.values.json</c> has no such key.</summary>
    public sealed record Difference(string Key, string? LegalValue, string SiteValue);

    /// <summary>Differences for <paramref name="sharedKeys"/> in the order of that list. A shared key that the site file itself lacks is a defect of the
    /// source file and throws <see cref="InvalidOperationException"/> (it must never be read as "equal").</summary>
    public static IReadOnlyList<Difference> Compare(
        IReadOnlyDictionary<string, string> legalValues, IReadOnlyDictionary<string, string> siteValues, IReadOnlyList<string> sharedKeys)
    {
        ArgumentNullException.ThrowIfNull(legalValues);
        ArgumentNullException.ThrowIfNull(siteValues);
        ArgumentNullException.ThrowIfNull(sharedKeys);

        var differences = new List<Difference>();
        foreach (var key in sharedKeys)
        {
            if (!siteValues.TryGetValue(key, out var site))
                throw new InvalidOperationException($"Ключ {key} входит в sharedWithLegalValues, но отсутствует в values файла operator-public.json.");

            if (!legalValues.TryGetValue(key, out var legal) || !string.Equals(legal, site, StringComparison.Ordinal))
                differences.Add(new Difference(key, legalValues.GetValueOrDefault(key), site));
        }

        return differences;
    }

    /// <summary>The refusal line of <c>legal publish</c> (§48.29.2).</summary>
    public static string RefusalText(Difference difference) =>
        $"Реквизит {difference.Key} на сайте (contracts/legal/operator-public.json) отличается от legal.values.json: на сайте «{difference.SiteValue}», "
        + $"в файле значений «{difference.LegalValue ?? "— нет —"}». Публиковать документы с другими реквизитами, чем на сайте, нельзя (ТЛ48-06): "
        + "выровняйте оба места и выкатите релиз, затем повторите publish (DEPLOY.md §35.3).";

    /// <summary>The block of <c>legal status --values</c> (§48.29.3): one line per shared key.</summary>
    public static IReadOnlyList<string> StatusLines(
        IReadOnlyDictionary<string, string> legalValues, IReadOnlyDictionary<string, string> siteValues, IReadOnlyList<string> sharedKeys)
    {
        var differing = Compare(legalValues, siteValues, sharedKeys).ToDictionary(d => d.Key, StringComparer.Ordinal);
        var lines = new List<string> { "Реквизиты сайта (contracts/legal/operator-public.json) против legal.values.json:" };
        foreach (var key in sharedKeys)
        {
            lines.Add(differing.TryGetValue(key, out var difference)
                ? $"  {key}: РАСХОДИТСЯ — на сайте «{difference.SiteValue}», в файле «{difference.LegalValue ?? "— нет —"}»"
                : $"  {key}: совпадает");
        }

        return lines;
    }
}
