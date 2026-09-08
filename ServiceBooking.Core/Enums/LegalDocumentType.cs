namespace ServiceBooking.Core.Enums;

/// <summary>Ровно два документа, третьего не бывает (ARCHITECTURE.md §4.2). Persisted as int on
/// UserConsent and referenced by Booking's consent-snapshot fields.</summary>
public enum LegalDocumentType
{
    Privacy,
    Terms
}

/// <summary>
/// Governs how a version mismatch between a token's claims and the current document is treated by
/// LegalConsentFilter (ARCHITECTURE.md §6.3): Material blocks with 451, Editorial only shows a banner.
/// Unrecognized/missing in the manifest defaults to Material — the safe default (§4.2).
/// </summary>
public enum LegalChangeKind
{
    Material,
    Editorial
}
