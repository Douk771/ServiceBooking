namespace ServiceBooking.Core.Enums;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404.1/§404.2 — who a <see cref="Entities.PlatformNotice"/> is addressed to.
/// Computed at READ time against the caller's current facts (§404.2), not snapshotted at publish time.
/// </summary>
public enum NoticeAudienceType
{
    AllOwners = 0,
    OwnersOnPlans = 1,
    BillingAccount = 2,
    AllClients = 3,
}
