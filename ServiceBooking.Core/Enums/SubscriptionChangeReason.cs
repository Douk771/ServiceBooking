namespace ServiceBooking.Core.Enums;

/// <summary>
/// US-20-02 (ARCHITECTURE_CYCLE20.md §403.1, D3 п. 6.13.15.4) — the closed list of grounds a
/// superadmin may cite when assigning a plan the storefront doesn't offer (hidden/removed/trial), or
/// otherwise changing a subscription's composition without a request from the subscriber. Append-only,
/// stored as <c>int</c>. `0` is deliberately unused: "no reason given" is <c>null</c>, not a member —
/// unlike a real reason, absence must be representable without occupying a value that a later member
/// could otherwise take.
/// </summary>
public enum SubscriptionChangeReason
{
    OperatorErrorCorrection = 1,
    TrialReissue = 2,
}
