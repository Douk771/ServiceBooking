using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.9 (Р40-Ю3).</summary>
public class PendingRebindKeyTests
{
    private static readonly Guid Wa = Guid.NewGuid();
    private static readonly Guid Mx = Guid.NewGuid();
    private const string Key = "Reminder:7d6f:79001234567:0:WhatsApp";
    private static readonly ISet<string> NoKeys = new HashSet<string>();

    [Fact]
    public void RebindKey_ReplacesOnlyTheLastSegment() =>
        PendingRebind.RebindKey(Key, NotificationTransport.Max).Should().Be("Reminder:7d6f:79001234567:0:Max");

    [Fact]
    public void RebindKey_StaysKeyShape() =>
        PendingRebind.RebindKey("StayGuestCreated:stay:abc:WhatsApp", NotificationTransport.Max).Should().Be("StayGuestCreated:stay:abc:Max");

    [Fact]
    public void Unbind_ByDefault_Cancels_EvenWhenAnotherTransportIsRoutable()
    {
        var d = PendingRebind.Decide(PendingRebindEvent.Unbind, NotificationTransport.WhatsApp, Key,
            [new(Mx, NotificationTransport.Max)], rebindToOtherTransportAllowed: false, NoKeys);
        d.Cancel.Should().BeTrue();
    }

    [Fact]
    public void Unbind_FlagOn_MovesToOtherTransport_WithNewKey()
    {
        var d = PendingRebind.Decide(PendingRebindEvent.Unbind, NotificationTransport.WhatsApp, Key,
            [new(Mx, NotificationTransport.Max)], rebindToOtherTransportAllowed: true, NoKeys);
        d.Cancel.Should().BeFalse();
        d.ChannelId.Should().Be(Mx);
        d.Transport.Should().Be(NotificationTransport.Max);
        d.IdempotencyKey.Should().Be("Reminder:7d6f:79001234567:0:Max");
    }

    [Fact]
    public void Unbind_FlagOn_NoOtherTransport_Cancels() =>
        PendingRebind.Decide(PendingRebindEvent.Unbind, NotificationTransport.WhatsApp, Key,
            [new(Wa, NotificationTransport.WhatsApp)], true, NoKeys).Cancel.Should().BeTrue();

    [Fact]
    public void Unbind_FlagOn_TargetKeyAlreadyExists_Cancels() =>
        PendingRebind.Decide(PendingRebindEvent.Unbind, NotificationTransport.WhatsApp, Key,
            [new(Mx, NotificationTransport.Max)], true, new HashSet<string> { "Reminder:7d6f:79001234567:0:Max" }).Cancel.Should().BeTrue();

    [Fact]
    public void Replace_RebindsToNewRowOfSameTransport_KeyUnchanged()
    {
        var newRow = Guid.NewGuid();
        var d = PendingRebind.Decide(PendingRebindEvent.Replace, NotificationTransport.WhatsApp, Key,
            [new(newRow, NotificationTransport.WhatsApp)], false, new HashSet<string> { Key });
        d.Cancel.Should().BeFalse();
        d.ChannelId.Should().Be(newRow);
        d.IdempotencyKey.Should().Be(Key);
    }

    [Fact]
    public void Transfer_RebindsToSameTransportOfNewAccount()
    {
        var d = PendingRebind.Decide(PendingRebindEvent.CompanyTransfer, NotificationTransport.Max, Key,
            [new(Wa, NotificationTransport.WhatsApp), new(Mx, NotificationTransport.Max)], true, NoKeys);
        d.Cancel.Should().BeFalse();
        d.ChannelId.Should().Be(Mx);
    }

    [Fact]
    public void Transfer_NewAccountHasNoSuchTransport_Cancels_EvenWithFlagOn() =>
        PendingRebind.Decide(PendingRebindEvent.CompanyTransfer, NotificationTransport.Max, Key,
            [new(Wa, NotificationTransport.WhatsApp)], true, NoKeys).Cancel.Should().BeTrue();
}
