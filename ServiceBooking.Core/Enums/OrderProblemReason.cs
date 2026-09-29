namespace ServiceBooking.Core.Enums;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §412, §419 — why a cart/edit line cannot be ordered as it is. Never persisted; serialized by
/// name (a closed table of the API contract — append-only).
/// </summary>
public enum OrderProblemReason
{
    NotFound,
    Unpublished,
    SoldOut,
    CategoryHidden,
    InsufficientStock,
    BelowMinimum,
    InvalidQuantity,
    PriceChanged
}
