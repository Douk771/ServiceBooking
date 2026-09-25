namespace ServiceBooking.API.Services.Billing;

/// <summary>Cycle 18 (ARCHITECTURE_CYCLE18.md §337.1) — the synthetic actor id the trial-lifecycle
/// background task writes as <c>SubscriptionChangeLog.ChangedByUserId</c> for the rows it writes
/// itself (no human triggered them). <c>AdminBillingController.GetSubscriptionHistory</c> maps this id
/// to the display name "Система" instead of showing the raw id when it fails to resolve a user.</summary>
public static class TrialActors
{
    public const string System = "system:trial-lifecycle";
}
