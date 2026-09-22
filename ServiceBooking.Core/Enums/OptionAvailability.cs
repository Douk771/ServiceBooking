namespace ServiceBooking.Core.Enums;

/// <summary>Cycle 7 (ARCHITECTURE_CYCLE7.md §43.3, §44.3, US-66) — how an option relates to a given
/// plan. Append-only, values persist in the database.</summary>
public enum OptionAvailability
{
    // No row for this (plan, option) pair means Unavailable too (AdminPlanDto.options contract) — this
    // value only appears when a rule was written explicitly to say so.
    Unavailable = 0,
    Included = 1,
    Extra = 2,
}
