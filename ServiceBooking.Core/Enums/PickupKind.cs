namespace ServiceBooking.Core.Enums;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §448.1, §451 — how the customer wants to receive an order. Persisted as a number:
/// append-only (Asap = 0 is the backfill value of every order created in cycle 23).
/// </summary>
public enum PickupKind
{
    /// <summary>"As soon as possible" — the pickup start is an estimate (creation + preparation minutes).</summary>
    Asap = 0,

    /// <summary>A slot on a working day (possibly a future one — a pre-order).</summary>
    Slot = 1
}
