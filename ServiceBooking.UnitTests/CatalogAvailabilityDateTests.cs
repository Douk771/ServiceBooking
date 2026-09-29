using FluentAssertions;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE24.md §452 — CatalogAvailability v2: the pickup date, the daily menu, the term of "sold out".</summary>
public class CatalogAvailabilityDateTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateOnly Tuesday = new(2026, 10, 6);

    private static Product Piece(Action<Product>? tweak = null)
    {
        var p = new Product { Id = Guid.NewGuid(), Unit = ProductUnit.Piece, IsPublished = true };
        tweak?.Invoke(p);
        return p;
    }

    private static ProductAvailability Verdict(Product p, DateOnly date, DailyMenuLookup? menu = null, bool accepting = true) =>
        CatalogAvailability.Evaluate(p, null, false, null, accepting, date, menu);

    [Fact]
    public void NoDate_BehavesLikeCycle23()
    {
        // The cycle-23 call sites pass no date: the mask and the term of the mark are simply not consulted.
        var p = Piece(x => { x.AvailableWeekdaysMask = 0; x.IsSoldOut = true; x.SoldOutForDate = new DateOnly(2000, 1, 1); });
        CatalogAvailability.Evaluate(p, null, false, null, true).Should().Be(ProductAvailability.SoldOut);
        CatalogAvailability.Evaluate(Piece(x => x.AvailableWeekdaysMask = 0), null, false, null, true).Should().Be(ProductAvailability.Available);
    }

    [Fact]
    public void WeekdayMask_DecidesWithoutAMenu()
    {
        var mondaysOnly = Piece(p => p.AvailableWeekdaysMask = WeekdayMask.FromDays([DayOfWeek.Monday]));
        Verdict(mondaysOnly, Monday).Should().Be(ProductAvailability.Available);
        Verdict(mondaysOnly, Tuesday).Should().Be(ProductAvailability.NotOnThisDate);
        Verdict(Piece(p => p.AvailableWeekdaysMask = 0), Monday).Should().Be(ProductAvailability.NotOnThisDate);
    }

    [Fact]
    public void DailyMenu_OverridesTheMask_BothWays()
    {
        var mondaysOnly = Piece(p => p.AvailableWeekdaysMask = WeekdayMask.FromDays([DayOfWeek.Monday]));
        var never = Piece(p => p.AvailableWeekdaysMask = 0);
        var menu = DailyMenuLookup.ForMenu([never.Id]);

        // The menu exists: ONLY its products are sold that day — even one whose mask forbids the weekday, and not one whose mask allows it.
        Verdict(never, Tuesday, menu).Should().Be(ProductAvailability.Available);
        Verdict(mondaysOnly, Monday, menu).Should().Be(ProductAvailability.NotOnThisDate);
    }

    [Fact]
    public void EmptyMenu_IsAMenu_NothingIsSold()
    {
        Verdict(Piece(), Monday, DailyMenuLookup.ForMenu([])).Should().Be(ProductAvailability.NotOnThisDate);
        DailyMenuLookup.None.Exists.Should().BeFalse();
        DailyMenuLookup.ForMenu([]).Exists.Should().BeTrue();
    }

    [Fact]
    public void Precedence_DateComesAfterHiddenAndBeforeSoldOut()
    {
        var soldOutAndOffDay = Piece(p => { p.IsSoldOut = true; p.AvailableWeekdaysMask = 0; });
        Verdict(soldOutAndOffDay, Monday).Should().Be(ProductAvailability.NotOnThisDate);
        CatalogAvailability.Evaluate(soldOutAndOffDay, new ProductCategory { IsHidden = true }, false, null, true, Monday).Should().Be(ProductAvailability.CategoryHidden);
    }

    [Fact]
    public void SoldOutForToday_AppliesOnlyOnThatDate()
    {
        var p = Piece(x => { x.IsSoldOut = true; x.SoldOutForDate = Monday; });
        Verdict(p, Monday).Should().Be(ProductAvailability.SoldOut);
        Verdict(p, Tuesday).Should().Be(ProductAvailability.Available); // a pre-order for tomorrow is not affected by "no more today"
    }

    [Fact]
    public void SoldOutUntilCancelled_AppliesOnEveryDate()
    {
        var p = Piece(x => x.IsSoldOut = true);
        Verdict(p, Monday).Should().Be(ProductAvailability.SoldOut);
        Verdict(p, Tuesday).Should().Be(ProductAvailability.SoldOut);
    }

    [Fact]
    public void IsSoldOutNow_ExpiredMarkReadsAsNoMark()
    {
        var yesterdayMark = Piece(x => { x.IsSoldOut = true; x.SoldOutForDate = Monday; });
        CatalogAvailability.IsSoldOutNow(yesterdayMark, Monday).Should().BeTrue();
        CatalogAvailability.IsSoldOutNow(yesterdayMark, Tuesday).Should().BeFalse();
        CatalogAvailability.IsSoldOutNow(Piece(x => x.IsSoldOut = true), Tuesday).Should().BeTrue();
        CatalogAvailability.IsSoldOutNow(Piece(), Tuesday).Should().BeFalse();
    }

    [Fact]
    public void ShopNotAccepting_IsTheLastCondition() =>
        Verdict(Piece(), Monday, accepting: false).Should().Be(ProductAvailability.ShopNotAccepting);
}
