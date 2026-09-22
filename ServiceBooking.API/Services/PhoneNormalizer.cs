namespace ServiceBooking.API.Services;

/// <summary>
/// Reduces any phone number a human might type to one canonical, digits-only form, so the same
/// person is recognised regardless of how they entered their number ("+7 999...", "8 999...",
/// "8(999)...", etc — SPEC US-26). Deliberately a pure static class with no AppDbContext/EF types:
/// it is called both from application code (seven entry points, ARCHITECTURE.md §11.2) and,
/// transliterated by hand into SQL, from the phone-normalisation migration (§14.3) — keeping it free
/// of framework dependencies is what makes that hand transliteration a small, auditable diff.
/// </summary>
public static class PhoneNormalizer
{
    private const int MinDigits = 10;
    private const int MaxDigits = 15;

    /// <summary>
    /// Digits only: no '+', spaces, brackets or dashes. An 11-digit RU number starting with '8' becomes
    /// '7...'; a 10-digit number starting with '9' is padded to '7XXXXXXXXXX'. Everything else keeps its
    /// digits exactly as given, so international numbers are never mangled. Does not validate length —
    /// call <see cref="IsValid"/> separately (see <see cref="TryNormalize"/> for both in one call).
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        // ASCII digits only — deliberately NOT char.IsDigit, which is Unicode-aware and also accepts
        // non-ASCII decimal digits (e.g. Arabic-Indic '٠'-'٩', Devanagari digits). The SQL transliteration
        // of this rule in the NormalizePhoneNumbers migration strips only the ASCII digit class
        // (regexp_replace with '[^0-9]', not the locale-sensitive '\D'), so this has to match it exactly
        // — otherwise the C# canon computed at runtime and the SQL canon computed once during the
        // migration could disagree for the same input (ARCHITECTURE.md §14.3, risk R4).
        var digits = new string(raw.Where(c => c is >= '0' and <= '9').ToArray());

        if (digits.Length == 11 && digits[0] == '8')
            return "7" + digits[1..];

        if (digits.Length == 10 && digits[0] == '9')
            return "7" + digits;

        return digits;
    }

    /// <summary>E.164 bounds: 10 to 15 digits.</summary>
    public static bool IsValid(string canonical) =>
        canonical.Length is >= MinDigits and <= MaxDigits;

    /// <summary>Normalizes and validates in one call — the shape every call site actually needs.</summary>
    public static bool TryNormalize(string? raw, out string canonical)
    {
        canonical = Normalize(raw);
        return IsValid(canonical);
    }

    /// <summary>
    /// Temporary (cycle 6, `SPEC_CYCLE6_BOOKING_FIXES.md` §0.1 Q4) country policy: only the Russian number format is accepted
    /// for NEW data. Lifting this restriction later is a one-line change to this predicate alone — no
    /// caller is touched (ARCHITECTURE_CYCLE6.md §48.1). Deliberately a separate predicate from
    /// <see cref="IsValid"/> (the permanent E.164 technical bound, also relied on by the historical
    /// NormalizePhoneNumbers migration and by non-input call sites like GREEN-API's own number and
    /// phone-based search) rather than folded into it — see §48.1 for why conflating the two would be
    /// wrong. Known accepted consequence: Kazakhstan also uses the +7 country code and a Kazakh number
    /// (+7 7XX...) passes this check too; distinguishing them needs a maintained area-code directory,
    /// disproportionate for a temporary restriction.
    /// </summary>
    public static bool IsRussian(string canonical) => canonical.Length == 11 && canonical[0] == '7';

    /// <summary>
    /// Normalize + E.164 + the temporary Russian-only country policy, in one call — this is what every
    /// NEW-data user-input entry point (registration, phone change, adding a staff member, guest
    /// booking) calls instead of <see cref="TryNormalize"/>. Existing data, login, notes, search and
    /// GREEN-API's own number keep calling <see cref="TryNormalize"/>/<see cref="Normalize"/> unchanged
    /// (§48.2) — this restriction is about creating new rows, not about anything that already exists.
    /// </summary>
    public static bool TryNormalizeRussian(string? raw, out string canonical)
    {
        canonical = Normalize(raw);
        return IsValid(canonical) && IsRussian(canonical);
    }
}
