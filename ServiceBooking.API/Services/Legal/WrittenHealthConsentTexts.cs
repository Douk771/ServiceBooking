namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §402.4 (US-20-01) — the three fixed <c>ConsentRecord.RevokeReason</c> strings
/// for the written-health-consent cascade, one per entry point. DO NOT REWRITE without legal-counsel
/// (cycle-18 convention, §5) — these are the exact strings written to the append-only journal.
/// </summary>
public static class WrittenHealthConsentTexts
{
    public const string RevokedByStaffSubjectWithdrew = "Отзыв, отмеченный сотрудником";
    public const string RevokedByStaffMarkedByMistake = "Отметка поставлена ошибочно";
    public const string RevokedFromProfile = "Отзыв из личного кабинета";

    public const string ConfirmationRequiredMessage =
        "Для заполнения этого поля нужно письменное согласие клиента: распечатайте бланк, получите подпись и отметьте получение.";

    public const string LegacyElectronicConsentGoneMessage =
        "Согласие на обработку сведений о здоровье теперь оформляется на бумажном бланке. " +
        "Распечатайте бланк в карточке клиента и отметьте получение подписанного экземпляра.";

    public const string ProfileHealthDataPurposeGoneMessage =
        "Согласие на обработку сведений о здоровье даётся в салоне на бумажном бланке.";

    public const string TextVersionOutdatedMessage =
        "Текст бланка обновлён — распечатайте бланк заново и отметьте получение по новой редакции.";
}
