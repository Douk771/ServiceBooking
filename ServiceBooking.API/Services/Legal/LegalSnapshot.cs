namespace ServiceBooking.API.Services.Legal;

/// <summary>One published document: metadata plus the HTML fragment (ARCHITECTURE.md §4.2).</summary>
public sealed record LegalDocument(
    LegalDocumentType Type,
    string Title,
    string Version,
    DateOnly EffectiveFrom,
    bool IsDraft,
    LegalChangeKind ChangeKind,
    string ContentHtml);

/// <summary>
/// Immutable snapshot of both legal documents, swapped atomically by LegalDocumentProvider on a
/// successful reload (ARCHITECTURE.md §4.3) — readers never see a manifest half-parsed against files
/// that don't match it yet.
/// </summary>
public sealed class LegalSnapshot(IReadOnlyDictionary<LegalDocumentType, LegalDocument> documents)
{
    public IReadOnlyDictionary<LegalDocumentType, LegalDocument> Documents { get; } = documents;

    public LegalDocument? Get(LegalDocumentType type) => Documents.GetValueOrDefault(type);
}
