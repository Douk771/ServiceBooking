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

    // ARCHITECTURE_CYCLE20.md §411, §412 (Т20-08) — five new keys added by the cycle 20 commit A, together
    // with the matching legal-drafts/*.html files and the legal.json manifest entries. Same convention as
    // the other members: read-only reference texts, none of them gate the 451 flow.
    public const string GuestDataGateNotice = "GuestDataGateNotice";
    public const string GuestDataGateDeleteNotice = "GuestDataGateDeleteNotice";
    public const string GuestDataGateRevokeNotice = "GuestDataGateRevokeNotice";
    public const string HealthDataWrittenConsentForm = "HealthDataWrittenConsentForm";
    public const string CompanyPhotoPeopleNotice = "CompanyPhotoPeopleNotice";
    // ARCHITECTURE_CYCLE23.md §398.3 — the line under the "Заказать" button on goods. A constant, but
    // DELIBERATELY NOT in All: All feeds LegalDocumentProvider's fail-fast, which would block the deploy until
    // legal-counsel supplies the text (cycle 20 moved GuestDataGateNotice into All once its text existed —
    // do the same here when the lawyer's text lands). The frontend reads
    // GET /api/legal/texts/OrderCheckoutNotice and shows a neutral fallback on 404.
    public const string OrderCheckoutNotice = "OrderCheckoutNotice";

    // ARCHITECTURE_CYCLE24.md §457.2, §461 [legal L9, L13] — two more placeholders, DELIBERATELY NOT in All for the
    // same reason: the deploy must not wait for legal-counsel. The frontend reads GET /api/legal/texts/<key> and shows
    // its own SPEC fallback (messenger consent) or nothing (pre-order notice) on 404.
    public const string OrderMessengerConsent = "OrderMessengerConsent";
    public const string OrderPreorderNotice = "OrderPreorderNotice";

    // ARCHITECTURE_CYCLE25.md §497.3, §508 [legal L17] — the line under the customer-note field. DELIBERATELY NOT in All
    // (same reason as OrderCheckoutNotice): the deploy must not wait for legal-counsel; the frontend shows its fallback on 404.
    public const string ShopCustomerNoteNotice = "ShopCustomerNoteNotice";

    // ARCHITECTURE_CYCLE28.md §572.2, §583.2 [L28-1, L28-3] — neutral placeholders (no lawyer engaged, customer decision 30.09),
    // DELIBERATELY NOT in All (same reason as OrderCheckoutNotice): the deploy must not wait for legal text. The frontend reads
    // GET /api/legal/texts/<key> and shows its own fallback (API_CONTRACT_CYCLE28.md §600) on 404.
    public const string ShowcaseNotice = "ShowcaseNotice";
    public const string ShowcaseBookingClosed = "ShowcaseBookingClosed";
    public const string DemoBanner = "DemoBanner";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BookingNotice, TemplateAdWarning, UnsubscribePage, PhotoConsent, HealthDataConsent, GuardianConfirmation,
        PublicAddressNotice,
        GuestDataGateNotice, GuestDataGateDeleteNotice, GuestDataGateRevokeNotice, HealthDataWrittenConsentForm,
        CompanyPhotoPeopleNotice
    };
}
