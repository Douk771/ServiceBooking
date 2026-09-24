namespace ServiceBooking.Core.Enums;

/// <summary>
/// The closed set of "interface text" keys (ARCHITECTURE_CYCLE5.md §43.1, §44.1; extended to a seventh
/// key, PublicAddressNotice, by ARCHITECTURE_CYCLE13.md §220.1) — deliberately a
/// static class of strings, NOT an enum: these values travel into the database (ConsentRecord.DocumentKey,
/// shared with LegalDocumentType's five members as plain strings) and into the manifest's `uiTexts[].key`
/// as JSON strings, and adding a seventh key is meant to be a manifest edit, not a migration or a
/// recompile of every switch that pattern-matches an enum. None of these participate in the 451 gate and
/// none of them are ever compared against a JWT claim — they are read-only reference texts, shown, not
/// enforced (§43.1's "разнесение проходит не по линии «документ / не документ», а по линии «блокирует
/// вход / не блокирует»").
/// </summary>
public static class LegalTextKey
{
    public const string BookingNotice = "BookingNotice";
    public const string TemplateAdWarning = "TemplateAdWarning";
    public const string UnsubscribePage = "UnsubscribePage";
    public const string PhotoConsent = "PhotoConsent";
    public const string HealthDataConsent = "HealthDataConsent";
    public const string GuardianConfirmation = "GuardianConfirmation";

    // ARCHITECTURE_CYCLE13.md §220.1 (LEGAL_REVIEW.md §16.4) — predisposition-to-the-owner warning shown
    // next to the "Адрес" field, before saving, on both screens where an address is entered. Not a
    // LegalDocumentType (doesn't gate the 451 flow), read-only reference text like the other six.
    public const string PublicAddressNotice = "PublicAddressNotice";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BookingNotice, TemplateAdWarning, UnsubscribePage, PhotoConsent, HealthDataConsent, GuardianConfirmation,
        PublicAddressNotice
    };
}
