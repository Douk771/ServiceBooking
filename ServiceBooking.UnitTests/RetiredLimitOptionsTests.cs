using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE19.md §382/§384.1/§391.1 — the ONE definition of "retired limit option":
/// CapabilityKey, after Trim()+ToLowerInvariant(), is "employees" or "companies". Only the pure
/// (no-DB) predicates are unit-tested here; the IQueryable filters/LiveRetiredRows translate to SQL and
/// are exercised by the functional LIM19-020 gate-parity test, not here (no real DbContext in a unit
/// test).</summary>
public class RetiredLimitOptionsTests
{
    [Theory]
    [InlineData("employees")]
    [InlineData("Companies")]
    [InlineData(" employees ")]
    [InlineData("COMPANIES")]
    public void IsRetiredCapability_MatchesAfterTrimAndLowercase(string key)
    {
        RetiredLimitOptions.IsRetiredCapability(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("notifications.whatsapp")]
    [InlineData(null)]
    [InlineData("employee")]
    [InlineData("companiesx")]
    [InlineData("")]
    public void IsRetiredCapability_DoesNotMatchAnythingElse(string? key)
    {
        RetiredLimitOptions.IsRetiredCapability(key).Should().BeFalse();
    }

    [Fact]
    public void IsRetired_DelegatesToOptionsCapabilityKey()
    {
        var option = new SubscriptionOption { CapabilityKey = "employees" };

        RetiredLimitOptions.IsRetired(option).Should().BeTrue();
    }

    [Fact]
    public void IsRetired_False_ForOrdinaryOption()
    {
        var option = new SubscriptionOption { CapabilityKey = "notifications.whatsapp" };

        RetiredLimitOptions.IsRetired(option).Should().BeFalse();
    }
}
