using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// The one "type/route/anchor" map for the legal document surface (ARCHITECTURE_CYCLE11.md §102.6,
/// §104.3), shared in spirit with <c>contracts/cycle11/legal-routes.json</c> — that file is the
/// cross-team artifact CI, the CLI and the frontend read; this class is the backend's typed copy of the
/// same facts, kept in sync by <c>LegalRoutesTests</c> (ServiceBooking.UnitTests), which parses the JSON
/// file and asserts it against this map so the two can never quietly diverge.
///
/// Replaces the private `LegalController.Urls` dictionary (ARCHITECTURE_CYCLE11.md §102.6) — that
/// dictionary is a strict subset of this one: it has no entry for `/offer-channel`, because that route is
/// an alias into `/terms-owner`, not a document type of its own.
/// </summary>
public static class LegalRoutes
{
    /// <summary>Document type → the SPA route that serves it.</summary>
    public static readonly IReadOnlyDictionary<LegalDocumentType, string> Documents = new Dictionary<LegalDocumentType, string>
    {
        [LegalDocumentType.Privacy] = "/privacy",
        [LegalDocumentType.TermsClient] = "/terms",
        [LegalDocumentType.TermsOwner] = "/terms-owner",
        [LegalDocumentType.PdnConsent] = "/pdn-consent",
        [LegalDocumentType.ChannelRiskNotice] = "/channel-risk",
    };

    /// <summary>Route alias → the real route (with an in-page anchor) it resolves to. The channel-offer
    /// text (D9) is spliced into D3 at build time (ARCHITECTURE_CYCLE11.md §103.2), so it has no route of
    /// its own — links to it point at this alias instead.</summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
    {
        ["/offer-channel"] = "/terms-owner#offer-channel",
    };

    /// <summary>Route → the in-page anchor(s) its content is required to contain. Used by `legal links`
    /// and by <c>LegalRoutesTests</c> to catch the case fixed by this cycle: an alias pointing at an
    /// anchor the target document doesn't actually have (ARCHITECTURE_CYCLE11.md §108.1).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Anchors = new Dictionary<string, IReadOnlyList<string>>
    {
        ["/terms-owner"] = ["offer-channel"],
    };

    public static string UrlFor(LegalDocumentType type) => Documents.GetValueOrDefault(type, "");
}
