using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.3.5 (О6 + Р40-Ю1): the trial grants only OPEN channel options; the tariff's option rule is not an input at all.</summary>
public class TrialOptionGrantRuleTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, true, false)]
    public void ClosedOption_IsSkipped_WhateverTheRowIs(bool open, bool exists, bool byTrial) =>
        TrialOptionGrantRule.Decide(open, exists, byTrial).Should().Be(TrialOptionGrantAction.SkipClosed);

    [Fact]
    public void OpenOption_WithoutARow_IsCreated() =>
        TrialOptionGrantRule.Decide(true, false, false).Should().Be(TrialOptionGrantAction.Create);

    [Fact]
    public void OpenOption_WithATrialRow_IsRevived() =>
        TrialOptionGrantRule.Decide(true, true, true).Should().Be(TrialOptionGrantAction.Revive);

    [Fact]
    public void OpenOption_WithABoughtRow_IsLeftAlone() =>
        TrialOptionGrantRule.Decide(true, true, false).Should().Be(TrialOptionGrantAction.LeaveAsIs);
}
