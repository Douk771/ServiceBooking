namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE17.md §307/§317 — the two strings this cycle's B-4 needs from `legal-counsel`
/// (п. 6.13.15.3 <c>legal-drafts/03-terms-owner.html</c>). Deliberately isolated in ONE file so the
/// moment real wording lands, exactly this file changes — no controller/service edit needed.
///
/// <b>THIS IS NOT SHIPPABLE COPY.</b> Per §307.1/§317: "Заготовка-заглушка в коде не допускается: пока
/// текста нет, задача не считается закрытой" — US-17-08/US-17-09 stay OPEN until legal-counsel's actual
/// wording replaces the two constants below. The values here are intentionally obviously-a-placeholder
/// (not plausible Russian legal prose) so nobody mistakes a green build for "text is final", and so a
/// character-for-character diff of the whole file makes the eventual substitution a one-line review.
/// </summary>
internal static class LegalNotices
{
    /// <summary>ARCHITECTURE_CYCLE17.md §307.1, API_CONTRACT_CYCLE17.md §325.1 (US-17-08) —
    /// SubscriptionRequestDto.IrreversibilityNotice, shown on a plan-change request against a
    /// snapshot-off-sale current plan. Single line, no markup (§317 п.1).</summary>
    public const string PendingPlanChangeIrreversibilityNotice =
        "[ТРЕБУЕТСЯ ТЕКСТ ОТ LEGAL-COUNSEL — US-17-08, ARCHITECTURE_CYCLE17.md §317 п.1: " +
        "предупреждение о необратимости смены снятого с продажи тарифа]";

    /// <summary>ARCHITECTURE_CYCLE17.md §307.2, API_CONTRACT_CYCLE17.md §325.2 (US-17-09) — appended to
    /// the existing "Expiring soon" warning.Text when the current plan is snapshot off sale. Whether
    /// this AUGMENTS or REPLACES the base sentence is legal-counsel's call (§317 п.2) — appending is
    /// this scaffold's working assumption, easy to change to a full replacement in BuildWarning.</summary>
    public const string SubscriptionExpiringOnWithdrawnPlanSuffix =
        "[ТРЕБУЕТСЯ ТЕКСТ ОТ LEGAL-COUNSEL — US-17-09, ARCHITECTURE_CYCLE17.md §317 п.2: " +
        "упоминание «вернуться на этот тариф будет нельзя» в напоминании об окончании периода]";
}
