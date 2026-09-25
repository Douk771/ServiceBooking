using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.API.Services.Subjects;

/// <summary>
/// TD-03's user-facing text for a closed guest-data gate (ARCHITECTURE_CYCLE16.md §245.6 p.3). The
/// server assembles the ready-to-show Russian string, the same convention already used for
/// <c>PhoneVerificationSessionStatus.message</c> — the frontend never composes wording itself.
///
/// The real text is <c>legal-counsel</c>'s to write, under <c>uiTexts</c> key
/// <c>"guestDataGateNotice"</c> in <c>legal.json</c> (§276.3, same mechanism as every other legal-owned
/// UI string: <see cref="LegalDocumentProvider"/>/<c>useLegalText</c> on the frontend). That key is
/// deliberately NOT added to <c>LegalTextKey.All</c> by this change — doing so would make
/// <see cref="LegalDocumentProvider"/>'s fail-fast startup check (missing required uiTexts key) reject
/// every deployment until legal-counsel's content exists, which would block the whole cycle on someone
/// else's timeline. Until the key is present in the manifest, <see cref="Fallback"/> below is what ships
/// — a neutral, legally uncommitted placeholder so the screen is never blank, per §245.6 p.3.
/// </summary>
public static class SubjectGateTexts
{
    public const string ManifestKey = "guestDataGateNotice";

    /// <summary>
    /// Neutral fallback (§245.6 p.3, verbatim): explains why, what to do with a MAX-verified number,
    /// and where to go without one. Replaced automatically the moment legal-counsel's manifest key
    /// appears — no code change, no redeploy.
    /// </summary>
    public const string Fallback =
        "Эти сведения доступны после подтверждения номера телефона. Если подтвердить номер невозможно, " +
        "направьте обращение субъекта персональных данных — ответ по закону даётся в установленный срок.";

    /// <summary>Resolves the text that should accompany a closed gate: the legal-owned manifest entry
    /// if present, otherwise <see cref="Fallback"/>. Never returns null or empty.</summary>
    public static string Resolve(LegalSnapshot? snapshot) =>
        snapshot?.GetText(ManifestKey)?.ContentHtml is { Length: > 0 } text ? text : Fallback;
}
