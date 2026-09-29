namespace ServiceBooking.API.Services.Shops;

/// <summary>ARCHITECTURE_CYCLE24.md §449.3 — the acceptance state of a shop, computed (an expired pause reads as Accepting). Serialized by name.</summary>
public enum ShopAcceptanceMode
{
    Accepting,
    Paused,
    Stopped
}

/// <summary>How long a pause lasts (PUT …/acceptance). Serialized by name.</summary>
public enum PauseDuration
{
    Minutes15,
    Minutes30,
    Hour1,
    EndOfDay
}

/// <summary>
/// ARCHITECTURE_CYCLE24.md §450 — why a shop does not take orders right now: the FIRST condition of <see cref="ShopOrderingGate"/> that
/// fires. Closed table of the contract, serialized by name, append-only.
/// </summary>
public enum ShopNotAcceptingCode
{
    Blocked,
    NoWorkingHours,
    Stopped,
    Paused,
    NotAllowedByPlan,
    MonthlyLimitReached,
    NoPickupTimeAvailable
}

/// <summary>Level of the monthly order-limit warning (§459.6). Serialized by name.</summary>
public enum OrderLimitWarningLevel
{
    None,
    Warning80,
    Reached
}

/// <summary>Scope of a "sold out" mark (§452.2). Serialized by name.</summary>
public enum SoldOutScope
{
    Today,
    UntilCancelled
}

/// <summary>Why a storefront product is shown greyed (§477.1). Serialized by name.</summary>
public enum StorefrontUnavailableReason
{
    SoldOut,
    InsufficientStock,
    ShopNotAccepting
}
