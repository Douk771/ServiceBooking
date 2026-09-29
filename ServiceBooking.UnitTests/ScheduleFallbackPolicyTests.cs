using FluentAssertions;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>Cycle 22 D5 — the trust table GetSlots and GetAvailability share (ARCHITECTURE_CYCLE6.md §46.3).</summary>
public class ScheduleFallbackPolicyTests
{
    [Theory]
    [InlineData(false, false, false, ScheduleFallback.None)]
    [InlineData(false, true, false, ScheduleFallback.None)]
    [InlineData(true, false, false, ScheduleFallback.None)]    // manual asked by a non-staff caller: ignored
    [InlineData(true, true, false, ScheduleFallback.None)]
    [InlineData(false, false, true, ScheduleFallback.None)]    // staff without manual: the public grid
    [InlineData(false, true, true, ScheduleFallback.None)]     // extendedHours alone means nothing
    [InlineData(true, false, true, ScheduleFallback.DefaultWindow)]
    [InlineData(true, true, true, ScheduleFallback.WholeDay)]
    public void For_CoversTheWholeTable(bool manual, bool extendedHours, bool isStaff, ScheduleFallback expected) =>
        ScheduleFallbackPolicy.For(manual, extendedHours, isStaff).Should().Be(expected);
}
