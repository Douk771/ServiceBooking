using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Legal;

/// <summary>One published, versioned legal document: metadata plus the HTML fragment
/// (ARCHITECTURE_CYCLE5.md §43.1, §43.3). Gate governs where a Material change blocks (§46.3); Purposes
/// is non-empty only for PdnConsent — every other document type has it empty.</summary>
public sealed record LegalDocument(
    LegalDocumentType Type,
    string Title,
    string Version,
    DateOnly EffectiveFrom,
    bool IsDraft,
    LegalChangeKind ChangeKind,
    LegalGate Gate,
    IReadOnlyList<LegalPurpose> Purposes,
    string ContentHtml,
    string ContentHash);

/// <summary>One granular purpose under PdnConsent, in manifest order (order is meaningful — it's the
/// order the consent form presents them in, ARCHITECTURE_CYCLE5.md §43.3).</summary>
public sealed record LegalPurpose(ConsentPurpose Key, string Title);

/// <summary>One published interface text (ARCHITECTURE_CYCLE5.md §43.1) — versioned and its acceptance
/// recorded where it functions as a consent/confirmation (photo, health, guardian, ad warning), but NEVER
/// gating: no LegalGate, no ChangeKind, no claim, no 451 (§43.1's "никогда" column).</summary>
public sealed record LegalUiText(string Key, string Version, bool IsDraft, string ContentHtml, string ContentHash);

/// <summary>
/// Immutable snapshot of the whole legal manifest — five documents and six interface texts, swapped
/// atomically by LegalDocumentProvider on a successful reload (ARCHITECTURE.md §4.3, extended by
/// ARCHITECTURE_CYCLE5.md §43.3 to the second `uiTexts` dictionary) — readers never see a manifest
/// half-parsed against files that don't match it yet.
/// </summary>
public sealed class LegalSnapshot(
    IReadOnlyDictionary<LegalDocumentType, LegalDocument> documents,
    IReadOnlyDictionary<string, LegalUiText> uiTexts)
{
    public IReadOnlyDictionary<LegalDocumentType, LegalDocument> Documents { get; } = documents;
    public IReadOnlyDictionary<string, LegalUiText> UiTexts { get; } = uiTexts;

    public LegalDocument? Get(LegalDocumentType type) => Documents.GetValueOrDefault(type);
    public LegalUiText? GetText(string key) => UiTexts.GetValueOrDefault(key);
}
