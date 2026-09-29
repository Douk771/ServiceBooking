using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §388.2 — append-only order journal. Written exclusively by
/// ServiceBooking.API.Services.Orders.OrderEventLog (the BookingEventLog pattern).
/// </summary>
public class OrderEvent
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>Denormalized copy of Order.CompanyId — access checks and the retention rule without a join.</summary>
    public Guid CompanyId { get; set; }

    public OrderEventKind Kind { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public OrderActorKind ActorKind { get; set; }
    public string? ActorUserId { get; set; }

    /// <summary>Name snapshot of the staff member; never shown to the customer.</summary>
    public string? ActorNameSnapshot { get; set; }

    public OrderStatus? FromStatus { get; set; }
    public OrderStatus? ToStatus { get; set; }
    public string? Reason { get; set; }

    /// <summary>The shop's comment on an edit — the customer sees it.</summary>
    public string? Comment { get; set; }

    /// <summary>jsonb: edit — [{name, before, after}]; issue — actual weights and the stock write-off.</summary>
    public string? ChangesJson { get; set; }

    public decimal? TotalBefore { get; set; }
    public decimal? TotalAfter { get; set; }

    /// <summary>Statuses and edits are visible to the customer; service entries (stock write-off) are not.</summary>
    public bool VisibleToCustomer { get; set; } = true;
}
