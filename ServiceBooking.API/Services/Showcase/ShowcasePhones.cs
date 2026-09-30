namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §574.4 — phone numbers of the showcase, taken one after another from the block <c>72005550000</c>–<c>72005559999</c> (canonical form:
/// 11 digits, leading 7; <c>+7 (200) 555-00-00</c> …). The code 200 does not exist in the Russian numbering plan, so no real person owns such a number;
/// <c>PhoneNormalizer.TryNormalizeRussian</c> still accepts them, so the product's own validation is not bypassed. Real subscribers are protected
/// independently of the range: every phone-matching query excludes showcase rows (§574.5).
/// </summary>
public sealed class ShowcasePhones
{
    private long _next = ShowcaseCatalog.PhoneBlockFirst;

    /// <summary>How many numbers were handed out.</summary>
    public int Issued => (int)(_next - ShowcaseCatalog.PhoneBlockFirst);

    /// <summary>The next unused number of the block; throws when the block is exhausted.</summary>
    public string Next()
    {
        if (_next > ShowcaseCatalog.PhoneBlockLast)
            throw new InvalidOperationException("The showcase phone block is exhausted.");
        return (_next++).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool IsShowcasePhone(string? canonicalPhone) =>
        long.TryParse(canonicalPhone, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
        && canonicalPhone!.Length == 11
        && value >= ShowcaseCatalog.PhoneBlockFirst && value <= ShowcaseCatalog.PhoneBlockLast;
}
