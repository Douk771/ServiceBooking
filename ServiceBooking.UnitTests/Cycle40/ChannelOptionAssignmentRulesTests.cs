using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.13, API_CONTRACT_CYCLE40.md §40.31.6 — one line of a channel option in the admin assignment.</summary>
public class ChannelOptionAssignmentRulesTests
{
    private static readonly DateOnly Day = new(2026, 12, 31);
    private static readonly DateTime SameDayInstant = new(2026, 12, 31, 20, 59, 59, DateTimeKind.Utc);

    private static ChannelOptionLineVerdict Check(DateOnly? paidUntil, bool open, ExistingChannelOptionRow? existing = null) =>
        ChannelOptionAssignmentRules.Evaluate(paidUntil, open, existing);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoPaidUntil_IsAlwaysRefused_OpenOrClosed(bool open) =>
        Check(null, open).Should().Be(ChannelOptionLineVerdict.PaidUntilRequired);

    [Fact]
    public void OpenOption_PassesWhetherCreatedOrExtended()
    {
        Check(Day, open: true).Should().Be(ChannelOptionLineVerdict.Ok);
        Check(Day, open: true, new ExistingChannelOptionRow(SameDayInstant.AddDays(-30), null)).Should().Be(ChannelOptionLineVerdict.Ok);
    }

    [Fact]
    public void ClosedOption_CreatingTheRow_IsRefused() =>
        Check(Day, open: false).Should().Be(ChannelOptionLineVerdict.ClosedForConnection);

    [Fact]
    public void ClosedOption_ReviveAnEndedRow_IsRefused() =>
        Check(Day, open: false, new ExistingChannelOptionRow(SameDayInstant, SameDayInstant.AddDays(-1))).Should().Be(ChannelOptionLineVerdict.ClosedForConnection);

    [Fact]
    public void ClosedOption_ExtendingTheDate_IsRefused() =>
        Check(Day, open: false, new ExistingChannelOptionRow(SameDayInstant.AddDays(-10), null)).Should().Be(ChannelOptionLineVerdict.ClosedForConnection);

    [Fact]
    public void ClosedOption_RowWithoutOwnDate_AnyDateCountsAsExtension() =>
        Check(Day, open: false, new ExistingChannelOptionRow(null, null)).Should().Be(ChannelOptionLineVerdict.ClosedForConnection);

    [Fact]
    public void ClosedOption_UnchangedRow_Passes_EvenWithADifferentTimeOfDay() =>
        Check(Day, open: false, new ExistingChannelOptionRow(SameDayInstant, null)).Should().Be(ChannelOptionLineVerdict.Ok);

    [Fact]
    public void ClosedOption_ShorteningTheDate_Passes() =>
        Check(Day, open: false, new ExistingChannelOptionRow(SameDayInstant.AddDays(20), null)).Should().Be(ChannelOptionLineVerdict.Ok);

    [Fact]
    public void MissingDate_WinsOverTheClosedRefusal() =>
        Check(null, open: false).Should().Be(ChannelOptionLineVerdict.PaidUntilRequired);
}
