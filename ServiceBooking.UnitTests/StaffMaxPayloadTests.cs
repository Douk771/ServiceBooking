using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.API.Services.StaffMax;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §498.2–§498.3 — the staff link payload and its prefix routing.</summary>
public class StaffMaxPayloadTests
{
    [Fact]
    public void Generate_HasPrefix_FitsMaxStartLimit_AndIsUrlSafe()
    {
        var payload = StaffMaxPayload.Generate();
        payload.Should().StartWith("sm1.");
        payload.Length.Should().Be(47);
        payload.Length.Should().BeLessThan(PayloadGenerator.MaxLength);
        payload.Should().MatchRegex("^[A-Za-z0-9._-]+$");
    }

    [Fact]
    public void Generate_IsRandom() =>
        Enumerable.Range(0, 50).Select(_ => StaffMaxPayload.Generate()).Distinct().Should().HaveCount(50);

    [Theory]
    [InlineData("sm1.abc", true)]
    [InlineData("v1.abc", false)]
    [InlineData("SM1.abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsStaffPayload_RoutesByPrefixOnly(string? payload, bool expected) => StaffMaxPayload.IsStaffPayload(payload).Should().Be(expected);

    [Fact]
    public void PhoneVerificationPayload_IsNeverClaimedAsStaff() => StaffMaxPayload.IsStaffPayload(PayloadGenerator.Generate()).Should().BeFalse();

    [Fact]
    public void Hash_IsTheSharedSha256() => StaffMaxPayload.Hash("sm1.x").Should().Be(PayloadGenerator.Hash("sm1.x")).And.HaveLength(64);
}
