namespace ServiceBooking.Core.Enums;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §388.2 — how a product is sold. Persisted as a number: append-only. Immutable after
/// the product is created (stock and the quantities of existing orders would change meaning).
/// </summary>
public enum ProductUnit
{
    /// <summary>Price per piece; quantities are whole pieces.</summary>
    Piece = 0,

    /// <summary>Price per 1 kg; quantities are whole grams.</summary>
    Weight = 1
}
