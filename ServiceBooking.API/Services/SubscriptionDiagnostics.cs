using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services;

/// <summary>Overall subscription state, shown verbatim in the diagnostics endpoint (API_CONTRACT_CYCLE6.md §42.2).</summary>
public enum SubscriptionStatus
{
    NoSubscription,
    Active,
    Expired,
    Deactivated,
    PlanRetired,
}

/// <summary>
/// Per-company reason online booking is (or isn't) available, distinguishing "the plan doesn't allow
/// it" (402 at booking time) from "the owner turned it off themselves" (403) — same symptom to the
/// customer, different fix (ARCHITECTURE_CYCLE6.md §43.2).
/// </summary>
public enum PlanNotAppliedReason
{
    None,
    NoSubscription,
    SubscriptionInactive,
    SubscriptionExpired,
    PlanRetired,
    PlanDisallowsOnlineBooking,
    SelfBookingDisabledByOwner,
}

/// <summary>
/// Pure mapping from an <see cref="AccountSubscription"/> + "now" to a human status and, per company,
/// the precise reason online booking is blocked — the two pieces of §43.2's diagnostic endpoint that
/// don't touch the database and are therefore unit-testable without EF (ARCHITECTURE_CYCLE6.md §51,
/// "чистая логика ... обязана лежать вне контроллеров").
/// </summary>
public static class SubscriptionDiagnostics
{
    private static readonly System.Globalization.CultureInfo Ru = new("ru-RU");

    public static (SubscriptionStatus Status, string StatusText) Describe(AccountSubscription? sub, DateTime nowUtc)
    {
        if (sub is null)
            return (SubscriptionStatus.NoSubscription, "Тариф не назначен");

        if (!sub.IsActive)
            return (SubscriptionStatus.Deactivated, "Подписка деактивирована администратором");

        if (sub.PlanConfig is not { IsActive: true })
            return (SubscriptionStatus.PlanRetired, $"Тариф «{sub.PlanConfig?.Name ?? "—"}» снят с продажи");

        if (sub.PaidUntil.HasValue && sub.PaidUntil.Value < nowUtc)
            return (SubscriptionStatus.Expired, $"Истёк {sub.PaidUntil.Value.ToString("d MMMM yyyy", Ru)}");

        var untilText = sub.PaidUntil.HasValue ? $" до {sub.PaidUntil.Value.ToString("d MMMM yyyy", Ru)}" : "";
        return (SubscriptionStatus.Active, $"Тариф «{sub.PlanConfig.Name}» действует{untilText}");
    }

    public static PlanNotAppliedReason BlockingReasonFor(
        AccountSubscription? sub, EffectivePlan effective, bool allowSelfBooking, DateTime nowUtc)
    {
        if (sub is null) return PlanNotAppliedReason.NoSubscription;
        if (!sub.IsActive) return PlanNotAppliedReason.SubscriptionInactive;
        if (sub.PlanConfig is not { IsActive: true }) return PlanNotAppliedReason.PlanRetired;
        if (sub.PaidUntil.HasValue && sub.PaidUntil.Value < nowUtc) return PlanNotAppliedReason.SubscriptionExpired;
        if (!effective.AllowOnlineBooking) return PlanNotAppliedReason.PlanDisallowsOnlineBooking;
        if (!allowSelfBooking) return PlanNotAppliedReason.SelfBookingDisabledByOwner;
        return PlanNotAppliedReason.None;
    }
}
