namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE17.md §307/§317 — the two strings this cycle's B-4 needs from `legal-counsel`
/// (п. 6.13.15.3 <c>legal-drafts/03-terms-owner.html</c>). Deliberately isolated in ONE file so the
/// moment real wording lands, exactly this file changes — no controller/service edit needed.
///
/// <b>Wording below is legal-counsel's final text (cycle 17); it is what the owner actually sees.</b>
/// Do NOT reword, shorten or "fix the style" without legal-counsel: both strings exist to discharge a
/// contractual duty (п. 6.13.15.3 <c>03-terms-owner.html</c>, п. П2.8.6 <c>13-payment-terms.html</c>),
/// and a notice that stops saying "вернуться будет нельзя" turns that clause into a trap. Plain text,
/// no markup, no clause numbers — the front end prints them as-is.
///
/// Third obligatory place, dormant today: п. П2.8.6 requires the same warning at the moment the owner
/// switches OFF auto-renewal on a withdrawn plan. Auto-renewal is not offered yet (п. 6.13.14 — four
/// preconditions unmet), so there is nothing to hook into; when card auto-renewal ships, that screen
/// takes its text from this file too (call legal-counsel for it) rather than inventing its own.
/// </summary>
internal static class LegalNotices
{
    /// <summary>ARCHITECTURE_CYCLE17.md §307.1, API_CONTRACT_CYCLE17.md §325.1 (US-17-08) —
    /// SubscriptionRequestDto.IrreversibilityNotice, shown on a plan-change request against a
    /// snapshot-off-sale current plan. Single line, no markup (§317 п.1).</summary>
    public const string PendingPlanChangeIrreversibilityNotice =
        "Ваш нынешний тариф снят с продажи: пока заявка не исполнена, тариф не меняется, " +
        "но после перехода на другой тариф вернуться на нынешний будет нельзя — ни сразу, ни позже.";

    /// <summary>ARCHITECTURE_CYCLE17.md §307.2, API_CONTRACT_CYCLE17.md §325.2 (US-17-09) — appended to
    /// the existing "Expiring soon" warning.Text when the current plan is snapshot off sale.
    /// legal-counsel's call on §317 п.2: <b>AUGMENT, not replace.</b> The base sentence ("продлите её…")
    /// is the one true thing the owner must act on and п. 6.13.15.1 keeps renewal on a withdrawn plan
    /// available — replacing it would read as "renewal is pointless", which is the opposite of the
    /// contract. Appending also keeps the public-plan string byte-identical, which §307.2 requires.
    /// Joined with a single space by BuildWarning, so this constant starts with a letter, not a space.</summary>
    public const string SubscriptionExpiringOnWithdrawnPlanSuffix =
        "Ваш тариф снят с продажи: если оплаченный период закончится и подписка перейдёт " +
        "на бесплатный тариф, вернуться на прежний будет нельзя.";
}
