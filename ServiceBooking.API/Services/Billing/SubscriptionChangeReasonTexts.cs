using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// US-20-02 (ARCHITECTURE_CYCLE20.md §403.1, API_CONTRACT_CYCLE20.md §433.2/§433.3) — the one place that
/// turns <see cref="SubscriptionChangeReason"/> into a title a human reads, in the admin history and in
/// the dropdown's list. The frontend deliberately does not keep its own copy of these strings (§441
/// "не должен … держать свои подписи оснований тарифа").
/// </summary>
public static class SubscriptionChangeReasonTexts
{
    public static string Title(SubscriptionChangeReason reason) => reason switch
    {
        SubscriptionChangeReason.OperatorErrorCorrection => "Исправление ошибки оператора",
        SubscriptionChangeReason.TrialReissue => "Перевыдача пробного периода",
        _ => reason.ToString(),
    };

    /// <summary>§433.3 — the closed list for the dropdown, in a fixed, deliberate order (the manually
    /// assignable one first). <c>AssignableManually</c> is false only for <c>TrialReissue</c>: it shows
    /// up in history (regrant writes it) but is never itself selectable on the manual-assignment screen —
    /// ManualPlanAssignmentPolicy.Validate rejects it there unconditionally. <c>DetailsRequired</c> is
    /// true for both today (OperatorErrorCorrection per ManualPlanAssignmentPolicy.Validate;
    /// TrialReissue per Billing_RegrantTrialInput's own mandatory Reason) — kept as data, not a computed
    /// rule, so a future third reason with optional details doesn't need a code change here.</summary>
    public static readonly IReadOnlyList<(SubscriptionChangeReason Code, bool DetailsRequired, bool AssignableManually)> All =
    [
        (SubscriptionChangeReason.OperatorErrorCorrection, true, true),
        (SubscriptionChangeReason.TrialReissue, true, false),
    ];
}
