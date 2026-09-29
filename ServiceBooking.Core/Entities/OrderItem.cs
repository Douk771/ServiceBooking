using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE23.md §388.2 — a line of an order: a snapshot of the product at order time.</summary>
public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int Position { get; set; }

    /// <summary>Reference to the (possibly soft-deleted) product.</summary>
    public Guid? ProductId { get; set; }

    public string NameSnapshot { get; set; } = string.Empty;
    public ProductUnit Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public string? PortionTextSnapshot { get; set; }

    /// <summary>Weight only — edit and issue validate against this snapshot.</summary>
    public int? WeightStepGrams { get; set; }

    public int QuantityOrdered { get; set; }

    /// <summary>Set at issue: grams for Weight, equal to the ordered quantity for Piece.</summary>
    public int? QuantityActual { get; set; }

    public decimal LineTotalEstimated { get; set; }
    public decimal? LineTotalFinal { get; set; }

    /// <summary>
    /// Does this line reserve stock: true only if, when it was created/edited, the shop tracked stock and the
    /// product had a stock figure (US-23-17: switching tracking back on starts from the current numbers).
    /// </summary>
    public bool ReservesStock { get; set; }
}
