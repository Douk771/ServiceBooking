using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE5.md §44.3 — the owner subscription screen's invariant
/// (totalMonthlyPrice == plan price + Σ option prices) and the Included/Extra pricing split rest on
/// these pure functions; covered directly, no DB/HTTP.</summary>
public class BillingCalculatorTests
{
    [Fact]
    public void MonthlyPriceFor_Unavailable_IsAlwaysZero() =>
        BillingCalculator.MonthlyPriceFor(OptionAvailability.Unavailable, quantity: 5, pricePerUnit: 100, includedQuantity: null).Should().Be(0);

    [Fact]
    public void MonthlyPriceFor_Extra_BillsEveryUnit() =>
        BillingCalculator.MonthlyPriceFor(OptionAvailability.Extra, quantity: 3, pricePerUnit: 100, includedQuantity: null).Should().Be(300);

    [Fact]
    public void MonthlyPriceFor_Included_DefaultsToOneFreeUnitWhenIncludedQuantityIsNull() =>
        BillingCalculator.MonthlyPriceFor(OptionAvailability.Included, quantity: 1, pricePerUnit: 100, includedQuantity: null).Should().Be(0);

    [Fact]
    public void MonthlyPriceFor_Included_BillsOnlyTheExcessOverIncludedQuantity() =>
        BillingCalculator.MonthlyPriceFor(OptionAvailability.Included, quantity: 5, pricePerUnit: 100, includedQuantity: 2).Should().Be(300);

    [Fact]
    public void MonthlyPriceFor_Included_NeverGoesNegativeWhenUnderTheIncludedQuantity() =>
        BillingCalculator.MonthlyPriceFor(OptionAvailability.Included, quantity: 1, pricePerUnit: 100, includedQuantity: 5).Should().Be(0);

    [Fact]
    public void TotalMonthlyPrice_IsPlanPricePlusSumOfOptionPrices() =>
        BillingCalculator.TotalMonthlyPrice(990m, [100m, 250m, 0m]).Should().Be(1340m);

    [Fact]
    public void ExpiresInDays_NullPaidUntil_ReturnsNull() =>
        BillingCalculator.ExpiresInDays(null, DateTime.UtcNow).Should().BeNull();

    [Fact]
    public void ExpiresInDays_FloorsPartialDays()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var paidUntil = now.AddDays(5).AddHours(23);

        BillingCalculator.ExpiresInDays(paidUntil, now).Should().Be(5);
    }

    [Fact]
    public void IsExpiringSoon_NullPaidUntil_IsFalse() =>
        BillingCalculator.IsExpiringSoon(null, notifyDaysBefore: 7, DateTime.UtcNow).Should().BeFalse();

    [Fact]
    public void IsExpiringSoon_WithinWindow_IsTrue()
    {
        var now = DateTime.UtcNow;
        BillingCalculator.IsExpiringSoon(now.AddDays(3), notifyDaysBefore: 7, now).Should().BeTrue();
    }

    [Fact]
    public void IsExpiringSoon_AlreadyPastDue_IsFalse_BecauseThatsExpiredNotExpiring()
    {
        var now = DateTime.UtcNow;
        BillingCalculator.IsExpiringSoon(now.AddDays(-1), notifyDaysBefore: 7, now).Should().BeFalse();
    }

    [Fact]
    public void IsExpiringSoon_OutsideWindow_IsFalse()
    {
        var now = DateTime.UtcNow;
        BillingCalculator.IsExpiringSoon(now.AddDays(30), notifyDaysBefore: 7, now).Should().BeFalse();
    }
}
