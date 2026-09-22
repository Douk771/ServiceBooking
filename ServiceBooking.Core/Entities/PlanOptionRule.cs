using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// Cycle 5 (ARCHITECTURE_CYCLE5.md §43.3, US-66) — whether/how an option is available on a plan.
/// One row per (PlanConfigId, OptionId); an option with no row for a given plan is treated as
/// <see cref="OptionAvailability.Unavailable"/> (the AdminPlanDto.options contract's own documented
/// default for a missing rule).
/// </summary>
public class PlanOptionRule
{
    public Guid Id { get; set; }

    public Guid PlanConfigId { get; set; }
    public SubscriptionPlanConfig PlanConfig { get; set; } = null!;

    public Guid OptionId { get; set; }
    public SubscriptionOption Option { get; set; } = null!;

    public OptionAvailability Availability { get; set; }

    // Only meaningful for Availability == Included on a Quantity option — how many units are bundled
    // free of charge; anything above that quantity is billed as Extra. Null for Toggle/Unavailable/Extra.
    public int? IncludedQuantity { get; set; }
}
