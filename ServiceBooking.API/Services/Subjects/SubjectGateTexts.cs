using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Subjects;

/// <summary>
/// TD-03's user-facing text for a closed guest-data gate (ARCHITECTURE_CYCLE16.md §245.6 p.3). The
/// server assembles the ready-to-show Russian string, the same convention already used for
/// <c>PhoneVerificationSessionStatus.message</c> — the frontend never composes wording itself.
///
/// The real text is <c>legal-counsel</c>'s to write, under <c>uiTexts</c> key
/// <see cref="LegalTextKey.GuestDataGateNotice"/> in <c>legal.json</c> (§276.3, same mechanism as every
/// other legal-owned UI string: <see cref="LegalDocumentProvider"/>/<c>useLegalText</c> on the frontend).
///
/// ARCHITECTURE_CYCLE20.md §411 (Т20-08 п. 2) — <c>ManifestKey</c> is now the proper
/// <see cref="LegalTextKey.GuestDataGateNotice"/> constant (it used to be a hand-typed
/// <c>"guestDataGateNotice"</c>, a known lowercase-first-letter mismatch with the frontend, which already
/// asked for <c>GuestDataGateNotice</c>). The key was added to <c>LegalTextKey.All</c> in the same commit
/// (commit А) that adds the file/manifest entry, so the two land together — <see cref="Fallback"/> below
/// stays only as a defensive last resort for a snapshot that is somehow missing the section, never as
/// the everyday path any more.
/// </summary>
public static class SubjectGateTexts
{
    public const string ManifestKey = LegalTextKey.GuestDataGateNotice;

    /// <summary>
    /// Neutral fallback (§245.6 p.3, verbatim): explains why, what to do with a MAX-verified number,
    /// and where to go without one. Used only if the manifest text exists but its "Текст" section
    /// somehow doesn't parse — the ordinary path is the real, published copy.
    /// </summary>
    public const string Fallback =
        "Эти сведения доступны после подтверждения номера телефона. Если подтвердить номер невозможно, " +
        "направьте обращение субъекта персональных данных — ответ по закону даётся в установленный срок.";

    /// <summary>ARCHITECTURE_CYCLE20.md §436.3 (US-20-05, Т20-08) — resolves the PLAIN-TEXT body of the
    /// "Текст" section of the legal-owned manifest entry (no HTML, no the lawyer's internal commentary
    /// paragraph), falling back to <see cref="Fallback"/> only when the manifest doesn't have the key at
    /// all, or that key's content has no "Текст" heading. Never returns null or empty.</summary>
    public static string Resolve(LegalSnapshot? snapshot)
    {
        var html = snapshot?.GetText(ManifestKey)?.ContentHtml;
        if (string.IsNullOrEmpty(html)) return Fallback;
        return LegalSectionText.PlainSection(html, "Текст") is { Length: > 0 } plain ? plain : Fallback;
    }
}
