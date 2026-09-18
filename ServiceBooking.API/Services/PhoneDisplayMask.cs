namespace ServiceBooking.API.Services;

/// <summary>
/// Formats a canonical (<see cref="PhoneNormalizer"/>) phone number for display in an API response —
/// <c>phoneMasked</c>/<c>recipientPhoneMasked</c> across every cycle-4 DTO (API_CONTRACT_CYCLE4.md
/// §19.5, §30.1, §32.2). Deliberately separate from <see cref="LogMasking"/>: that class exists to keep
/// a number out of LOG lines (keeps the first/last four raw digits, no punctuation, optimized for a
/// human scanning a log), while this one produces the punctuated, human-readable shape the contract's
/// examples show ("+7 999 ***-**-45") for a number shown ON SCREEN to the number's own owner or to
/// company staff — a different audience with a different trust level than "whoever reads server logs".
/// </summary>
public static class PhoneDisplayMask
{
    /// <summary>
    /// <paramref name="canonicalPhone"/> is digits-only, country code first (<see cref="PhoneNormalizer"/>).
    /// For an 11-digit Russian-shaped number ("7" + 10 digits) this reproduces the contract's exact
    /// example shape: <c>+7 999 ***-**-45</c> — country code, then the 3-digit operator code in the
    /// clear, then the remaining 7 digits with only the last 2 shown. Any other length (a foreign number,
    /// or a canonical value that doesn't fit the RU shape) falls back to a generic mask that still never
    /// reveals more than the last 2 digits, so this never throws on an unexpected input length.
    /// </summary>
    public static string Mask(string? canonicalPhone)
    {
        if (string.IsNullOrEmpty(canonicalPhone)) return string.Empty;

        if (canonicalPhone.Length == 11 && canonicalPhone[0] == '7')
        {
            var operatorCode = canonicalPhone[1..4];
            var lastTwo = canonicalPhone[9..11];
            return $"+7 {operatorCode} ***-**-{lastTwo}";
        }

        // Generic fallback for non-RU-shaped canonical numbers: show the country/leading digit(s) and
        // the last 2, mask everything between with a single run of asterisks (no attempt to reproduce
        // RU-style grouping for a shape we don't actually know).
        if (canonicalPhone.Length <= 4)
            return new string('*', canonicalPhone.Length);

        var lead = canonicalPhone[..2];
        var tail = canonicalPhone[^2..];
        return $"+{lead}***{tail}";
    }
}
