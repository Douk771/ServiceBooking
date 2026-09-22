namespace ServiceBooking.API.Services.Billing;

/// <summary>Cycle 6 §43.3.6, still in force after the cycle 7 rework of the assignment endpoint
/// (ARCHITECTURE_CYCLE6.md §43.3.6, §43.5): a paid plan without a `PaidUntil` date must never reach
/// `AccountSubscriptions` — that's the exact hole that produced never-expiring paid rows on the
/// stand, later cleaned up by the `BackfillSubscriptionPaidUntil` migration. `AdminBillingController
/// .AssignSubscription` must call this before any write to the subscription row or the change
/// log.</summary>
public static class SubscriptionAssignmentValidator
{
    public const string MissingPaidUntilError = "Укажите дату окончания подписки";

    /// <summary>True when the request assigns a paid plan (`planId` set) without an end date.
    /// Removing a plan (`planId == null`, i.e. Free) never requires a date.</summary>
    public static bool RequiresPaidUntil(Guid? planId, DateOnly? paidUntil) =>
        planId.HasValue && paidUntil is null;
}
