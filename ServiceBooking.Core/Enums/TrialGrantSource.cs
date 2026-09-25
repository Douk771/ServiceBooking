namespace ServiceBooking.Core.Enums;

/// <summary>
/// Cycle 18 (ARCHITECTURE_CYCLE18.md §332.4) — who granted a trial. Append-only: the numeric value of
/// <see cref="SuperAdminOverride"/> is hard-coded into a raw SQL partial-index filter
/// (<c>UX_TrialGrants_OnePerAccount</c>, <c>"Source" &lt;&gt; 2</c>) — the same warning as
/// <c>NotificationStatus</c>. Never reorder or remove a member.
/// </summary>
public enum TrialGrantSource
{
    OwnerSelfService = 0,
    SuperAdmin = 1,
    SuperAdminOverride = 2,
}
