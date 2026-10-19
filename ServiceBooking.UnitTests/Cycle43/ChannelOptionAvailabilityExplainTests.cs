using FluentAssertions;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.UnitTests.Cycle40;

namespace ServiceBooking.UnitTests.Cycle43;

/// <summary>ARCHITECTURE_CYCLE43.md §43.4.5 — <c>Explain</c> must agree with <c>Sellable</c> on every case of the FROZEN cycle-40 vectors
/// (read only, never edited).</summary>
public class ChannelOptionAvailabilityExplainTests
{
    public static IEnumerable<object[]> Availability() => ChannelVectors.Cases("availability");

    [Theory, MemberData(nameof(Availability))]
    public void Explain_SellableEqualsTheFrozenVectorAndSellable(int index, string name)
    {
        var i = ChannelVectors.Input("availability", index);
        var open = ChannelOptionAvailability.IsOpen(i.Str("settingValue"), i.Bool("configDefault"));

        var explained = ChannelOptionAvailability.Explain(open, i.Bool("isActive"), i.NullableDecimal("pricePerMonth"), i.Bool("termsOwnerPublished"));

        explained.Sellable.Should().Be(ChannelVectors.Expect("availability", index).Bool("sellable"), name);
        (explained.Reason is null).Should().Be(explained.Sellable, name);
    }
}
