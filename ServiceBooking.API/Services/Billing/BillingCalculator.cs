using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>Cycle 7, stage 5 (ARCHITECTURE_CYCLE7.md §44.3) — pure, DB-free arithmetic and status
/// rules shared by the owner subscription screen and the admin billing-account screen. Kept separate
/// from any controller/DbContext-bound service so it is unit-testable without a database, the same
/// convention as <see cref="CompanyTransferCalculator"/> and <see cref="ChannelFunding"/>.</summary>
public static class BillingCalculator
{
    /// <summary>§44.3 п. 4: an <see cref="OptionAvailability.Included"/> option costs 0 up to
    /// <paramref name="includedQuantity"/> (default 1 when null, i.e. a Toggle-shaped inclusion) —
    /// anything requested above that is billed as Extra at <paramref name="pricePerUnit"/>. An
    /// <see cref="OptionAvailability.Extra"/> option is billed for every unit. An
    /// <see cref="OptionAvailability.Unavailable"/> option contributes nothing (the caller shouldn't be
    /// showing it as subscribed at all, but the formula is still total, not partial, for safety).</summary>
    public static decimal MonthlyPriceFor(
        OptionAvailability availability, int quantity, decimal pricePerUnit, int? includedQuantity)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));

        return availability switch
        {
            OptionAvailability.Unavailable => 0m,
            OptionAvailability.Included => Math.Max(0, quantity - (includedQuantity ?? 1)) * pricePerUnit,
            OptionAvailability.Extra => quantity * pricePerUnit,
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
        };
    }

    /// <summary>Total monthly price shown on the owner screen — the contract's own invariant:
    /// plan price plus the sum of every subscribed option's monthly price.</summary>
    public static decimal TotalMonthlyPrice(decimal planPricePerMonth, IEnumerable<decimal> optionMonthlyPrices) =>
        planPricePerMonth + optionMonthlyPrices.Sum();

    /// <summary>§43.4/US-68 — days until <paramref name="paidUntilUtc"/>, floor-rounded, or null when
    /// there's nothing to expire (no paid-until date at all, i.e. a plan that doesn't require payment).</summary>
    public static int? ExpiresInDays(DateTime? paidUntilUtc, DateTime nowUtc) =>
        paidUntilUtc is { } paidUntil ? (int)Math.Floor((paidUntil - nowUtc).TotalDays) : null;

    /// <summary>Whether the subscription counts as "expiring soon" for warning purposes
    /// (US-68) — within <paramref name="notifyDaysBefore"/> days of <paramref name="paidUntilUtc"/>,
    /// but not yet past it (past-due is <c>Expired</c>, a different status, not a warning).</summary>
    public static bool IsExpiringSoon(DateTime? paidUntilUtc, int notifyDaysBefore, DateTime nowUtc)
    {
        if (paidUntilUtc is not { } paidUntil) return false;
        var daysLeft = (paidUntil - nowUtc).TotalDays;
        return daysLeft >= 0 && daysLeft <= notifyDaysBefore;
    }
}
