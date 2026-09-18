namespace ServiceBooking.API.Services;

/// <summary>
/// The single version marker for the risk-acceptance screen's text (US-53 p.7, API_CONTRACT_CYCLE4.md
/// §21/§23) — "рыба" pending legal review (ARCHITECTURE_CYCLE4.md §31.5). <c>GET
/// /api/notification-channels/offer</c> hands this to the frontend as <c>riskTextVersion</c>; <c>POST
/// .../accept-risk</c> compares the caller's submitted version against it and rejects a stale one with
/// 400 ("текст изменился, прочитайте заново") — the same shape as the existing legal-consent version
/// check in cycle 3's <c>LegalConsentFilter</c>, without a full document-provider mechanism for what is,
/// this cycle, one static paragraph.
/// </summary>
public static class NotificationRiskText
{
    public const string CurrentVersion = "2026-09-18-draft";
}
