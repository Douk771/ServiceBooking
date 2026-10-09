using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.13 — the journal covers exactly the two channel options.</summary>
public class ChannelOptionLogTests
{
    [Theory]
    [InlineData("notifications.whatsapp", true)]
    [InlineData("notifications.max", true)]
    [InlineData("analytics", false)]
    [InlineData("extra-companies", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsChannelOption_IsTrueOnlyForTheTwoMessengerOptions(string? code, bool expected) =>
        ChannelOptionLog.IsChannelOption(code).Should().Be(expected);

    [Fact]
    public void Write_ForANonChannelOption_WritesNothing_AndNeverTouchesTheContext()
    {
        // db is null on purpose: the early return must happen before anything is added to the change tracker.
        ChannelOptionLog.Write(null!, Guid.NewGuid(), "analytics", ServiceBooking.Core.Enums.ChannelOptionChangeSource.AdminBillingAccount,
            null, null, null, null, "admin").Should().BeFalse();
    }
}
