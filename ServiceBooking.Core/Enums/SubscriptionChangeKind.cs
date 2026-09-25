namespace ServiceBooking.Core.Enums;

/// <summary>Cycle 7 (ARCHITECTURE_CYCLE7.md §43.4, §43.5) — what kind of event a
/// <c>SubscriptionChangeLog</c> row records. Append-only, values persist in the database.
/// <c>Legacy</c> is the default backfilled onto every row written before this column existed — it
/// honestly means "a change from before cycle 7, kind unknown", not "nothing changed".
/// <c>PayerChanged</c> from an earlier draft of this cycle was removed before it ever shipped: a
/// change of payer isn't a subscription event any more (§43.5) — see <see cref="CompanyTransferred"/>
/// instead, which is written by <c>CompanyTransferService</c>.</summary>
public enum SubscriptionChangeKind
{
    Legacy = 0,
    Plan = 1,
    Options = 2,
    Payment = 3,
    CompanyTransferred = 4,
    Migration = 5,

    // Cycle 18 (ARCHITECTURE_CYCLE18.md §332.6, §337.1). Append-only, like everything above.
    TrialGranted = 6,        // activation — by the owner, by a superadmin, or an emergency regrant
    TrialExpired = 7,        // the materialized transition trial → system Free
    TrialMailingWindow = 8,  // the mailing window opened or closed (details in Comment)
}
