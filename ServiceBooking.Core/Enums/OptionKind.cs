namespace ServiceBooking.Core.Enums;

/// <summary>Cycle 5 (ARCHITECTURE_CYCLE5.md §43.3, §43.5) — whether a <c>SubscriptionOption</c> is a
/// simple on/off switch or sold in a countable quantity. Values are append-only and must not be
/// renumbered once shipped (they persist in the database).</summary>
public enum OptionKind
{
    Toggle = 0,
    Quantity = 1,
}
