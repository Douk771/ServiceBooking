using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §388.2 — a product of a shop. Soft-deleted (<see cref="DeletedAtUtc"/>): orders
/// reference the row and cycle 3's reports need it.
/// </summary>
public class Product
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>null → the "Другое" block.</summary>
    public Guid? CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Public storage class, area "products" (like the logo).
    public string? ImageUrl { get; set; }
    public string? ThumbnailUrl { get; set; }

    /// <summary>Immutable after creation.</summary>
    public ProductUnit Unit { get; set; }

    /// <summary>Per piece (Piece) or per 1 kg (Weight).</summary>
    public decimal Price { get; set; }

    /// <summary>Piece only: "300 г".</summary>
    public string? PortionText { get; set; }

    /// <summary>Weight only, 10..5000 grams.</summary>
    public int? WeightStepGrams { get; set; }

    /// <summary>Weight only: the smallest orderable weight in grams, a multiple of the step.</summary>
    public int? MinQuantityGrams { get; set; }

    public int Position { get; set; }
    public bool IsPublished { get; set; }
    public bool IsSoldOut { get; set; }

    /// <summary>
    /// ARCHITECTURE_CYCLE24.md §452.2 — the shop day a "sold out for today" mark was put on; null = "until cancelled".
    /// A mark with a date before the shop's current day has no effect (computed, no background task).
    /// </summary>
    public DateOnly? SoldOutForDate { get; set; }

    /// <summary>
    /// ARCHITECTURE_CYCLE24.md §452.4 — weekdays the product is sold on: bit 0 = Monday … bit 6 = Sunday. 127 = every day.
    /// 0 is valid: "never by the weekly rule", the product is sold only through a daily menu.
    /// </summary>
    public int AvailableWeekdaysMask { get; set; } = 127;

    /// <summary>[legal L3] The only food-information field of cycle 1 (API: inside <c>foodInfo</c>).</summary>
    public string? CompositionAndAllergens { get; set; }

    /// <summary>Stock in pieces/grams; null = not tracked for this product.</summary>
    public int? StockOnHand { get; set; }

    public DateTime? DeletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
