namespace ServiceBooking.Core.Enums;

/// <summary>
/// The five VERSIONED legal documents that gate access at some level (ARCHITECTURE_CYCLE5.md §43.1,
/// §44.1). This is a closed set on purpose: a type here means "there is a manifest entry, a gate
/// policy, and a claim-comparison branch for it" — the six interface texts (LegalTextKey) that are NOT
/// versioned documents in this sense deliberately do NOT get a member here, so that adding one of them
/// is a manifest edit, not a migration (§43.1 "побочная выгода").
///
/// `Terms = 1` (cycle 3) is RENAMED to `TermsClient` here, numeric value preserved — every existing
/// persisted/serialized `1` keeps meaning the same document; only the JSON string representation
/// changes ("Terms" → "TermsClient", API_CONTRACT_CYCLE5.md §38.3, breaking change №1).
/// </summary>
public enum LegalDocumentType
{
    Privacy = 0,
    TermsClient = 1,
    TermsOwner = 2,
    PdnConsent = 3,
    ChannelRiskNotice = 4
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

/// <summary>
/// Who/where a Material change to a document blocks (ARCHITECTURE_CYCLE5.md §43.3, §46.3). Missing or
/// unrecognized in the manifest defaults to Global — the same "safe default" precedent as
/// LegalChangeKind above: an operator typo in the manifest must fail closed (block more), not open.
/// </summary>
public enum LegalGate
{
    Global,
    OwnerScope,
    None
}
