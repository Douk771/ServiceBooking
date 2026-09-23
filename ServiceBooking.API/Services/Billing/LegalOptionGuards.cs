using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Code -&gt; required-document map for options whose public sale depends on a legal document being
/// published (ARCHITECTURE_CYCLE11.md §102.10, Q11). Today this holds exactly one entry —
/// <c>notifications.whatsapp</c> requires <c>TermsOwner</c> (the channel offer, D9, is an appendix to
/// it, ARCHITECTURE_CYCLE11.md §102.2) — because that option can't legally be sold to the public until
/// the operator has published the offer it's contingent on.
///
/// Deliberately a static map in code, not a database table (§102.10): "which document is required to
/// sell what" is a decision that goes through code review, not something a SuperAdmin should be able to
/// toggle from an admin screen without anyone else looking at it.
/// </summary>
public static class LegalOptionGuards
{
    private static readonly IReadOnlyDictionary<string, LegalDocumentType> RequiredDocumentByOptionCode =
        new Dictionary<string, LegalDocumentType>(StringComparer.OrdinalIgnoreCase)
        {
            ["notifications.whatsapp"] = LegalDocumentType.TermsOwner,
        };

    /// <summary>True when the option's public-sale legal condition is satisfied — either the option has
    /// no such condition, or its required document is published (not a draft, and the legal snapshot is
    /// actually loaded). A missing snapshot fails closed: an option that depends on document text we
    /// can't currently read is treated exactly like a draft (ARCHITECTURE_CYCLE11.md §112 A7).</summary>
    public static bool IsPubliclySellable(string optionCode, LegalSnapshot? snapshot)
    {
        if (!RequiredDocumentByOptionCode.TryGetValue(optionCode, out var requiredType))
            return true;

        var doc = snapshot?.Get(requiredType);
        return doc is not null && !doc.IsDraft;
    }
}
