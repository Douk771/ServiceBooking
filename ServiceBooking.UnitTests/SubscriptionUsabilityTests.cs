using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

/// <summary>Cycle 22 D1 — the one "subscription in force right now" rule, in both of its forms.</summary>
public class SubscriptionUsabilityTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<bool, DateTime?, bool> Cases => new()
    {
        { true, null, true },                  // active, open-ended
        { true, Now, true },                   // ends exactly now — inclusive, as every former copy had it
        { true, Now.AddTicks(1), true },
        { true, Now.AddTicks(-1), false },     // ended a tick ago
        { true, Now.AddDays(-30), false },
        { false, null, false },                // deactivated
        { false, Now.AddDays(30), false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void IsUsable_AndUsableAt_Agree(bool isActive, DateTime? paidUntil, bool expected)
    {
        var sub = new AccountSubscription { IsActive = isActive, PaidUntil = paidUntil };

        SubscriptionUsability.IsUsable(sub, Now).Should().Be(expected);
        SubscriptionUsability.UsableAt(Now).Compile()(sub).Should().Be(expected);
    }

    [Fact]
    public void IsUsable_NoSubscription_IsFalse() =>
        SubscriptionUsability.IsUsable(null, Now).Should().BeFalse();
}
