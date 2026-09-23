using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE9.md §104.5 (US-125, B6) — pure function, no DB/HTTP/clock.</summary>
public class NotificationRoutingTests
{
    private static readonly Guid WhatsAppChannelId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MaxChannelId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void SelectTargets_NoCandidates_ReturnsEmpty_NoReason()
    {
        var result = NotificationRouting.SelectTargets(
            NotificationDeliveryMode.PriorityChannel, NotificationTransport.WhatsApp, []);

        result.Targets.Should().BeEmpty();
        result.SkipReason.Should().BeNull();
        result.UnavailableChannelId.Should().BeNull();
    }

    // ── PriorityChannel ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PriorityChannel_MatchExistsAndUsable_ReturnsExactlyOneTarget()
    {
        var candidates = new[]
        {
            new NotificationRouting.Candidate(WhatsAppChannelId, NotificationTransport.WhatsApp, IsUsable: true),
            new NotificationRouting.Candidate(MaxChannelId, NotificationTransport.Max, IsUsable: true),
        };

        var result = NotificationRouting.SelectTargets(
            NotificationDeliveryMode.PriorityChannel, NotificationTransport.WhatsApp, candidates);

        result.Targets.Should().ContainSingle();
        result.Targets[0].ChannelId.Should().Be(WhatsAppChannelId);
        result.Targets[0].Transport.Should().Be(NotificationTransport.WhatsApp);
        result.SkipReason.Should().BeNull();
    }

    [Fact]
    public void PriorityChannel_MatchExistsButUnusable_ReturnsZeroTargets_WithUnavailableChannelId()
    {
        var candidates = new[]
        {
            new NotificationRouting.Candidate(WhatsAppChannelId, NotificationTransport.WhatsApp, IsUsable: false),
        };

        var result = NotificationRouting.SelectTargets(
            NotificationDeliveryMode.PriorityChannel, NotificationTransport.WhatsApp, candidates);

        result.Targets.Should().BeEmpty();
        result.SkipReason.Should().Be(NotificationReason.PriorityChannelUnavailable);
        result.UnavailableChannelId.Should().Be(WhatsAppChannelId);
    }

    [Fact]
    public void PriorityChannel_MatchDoesNotExist_OtherTransportConnected_ReturnsZeroTargets_NoChannelId()
    {
        // §104.5: "Нет вовсе, но подключён один другой → ноль целей, та же причина." No silent fallback
        // to the OTHER (MAX) channel even though one is usable.
        var candidates = new[]
        {
            new NotificationRouting.Candidate(MaxChannelId, NotificationTransport.Max, IsUsable: true),
        };

        var result = NotificationRouting.SelectTargets(
            NotificationDeliveryMode.PriorityChannel, NotificationTransport.WhatsApp, candidates);

        result.Targets.Should().BeEmpty();
        result.SkipReason.Should().Be(NotificationReason.PriorityChannelUnavailable);
        result.UnavailableChannelId.Should().BeNull();
    }

    // ── AllChannels ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllChannels_TwoUsableCandidates_ReturnsBothAsTargets()
    {
        var candidates = new[]
        {
            new NotificationRouting.Candidate(WhatsAppChannelId, NotificationTransport.WhatsApp, IsUsable: true),
            new NotificationRouting.Candidate(MaxChannelId, NotificationTransport.Max, IsUsable: true),
        };

        var result = NotificationRouting.SelectTargets(
            NotificationDeliveryMode.AllChannels, NotificationTransport.WhatsApp, candidates);

        result.Targets.Should().HaveCount(2);
        result.Targets.Select(t => t.Transport).Should().BeEquivalentTo([NotificationTransport.WhatsApp, NotificationTransport.Max]);
        result.SkipReason.Should().BeNull();
    }

    [Fact]
    public void AllChannels_OneUsableOneNot_ReturnsOnlyTheUsableOne()
    {
        var candidates = new[]
        {
            new NotificationRouting.Candidate(WhatsAppChannelId, NotificationTransport.WhatsApp, IsUsable: true),
            new NotificationRouting.Candidate(MaxChannelId, NotificationTransport.Max, IsUsable: false),
        };

        var result = NotificationRouting.SelectTargets(
            NotificationDeliveryMode.AllChannels, NotificationTransport.WhatsApp, candidates);

        result.Targets.Should().ContainSingle();
        result.Targets[0].Transport.Should().Be(NotificationTransport.WhatsApp);
    }

    [Fact]
    public void AllChannels_NoneUsable_ReturnsEmpty_NoPriorityReason()
    {
        // AllChannels has no concept of a single "priority" transport — the zero-target reason is left
        // for the caller to fill with its own generic "nothing usable" reason, not
        // PriorityChannelUnavailable (which is specifically about a NAMED transport).
        var candidates = new[]
        {
            new NotificationRouting.Candidate(WhatsAppChannelId, NotificationTransport.WhatsApp, IsUsable: false),
            new NotificationRouting.Candidate(MaxChannelId, NotificationTransport.Max, IsUsable: false),
        };

        var result = NotificationRouting.SelectTargets(
            NotificationDeliveryMode.AllChannels, NotificationTransport.WhatsApp, candidates);

        result.Targets.Should().BeEmpty();
        result.SkipReason.Should().BeNull();
    }

    [Fact]
    public void SingleConnectedTransport_BothModes_GiveTheSameSingleTarget()
    {
        // §104.5 / US-125: "Единственный подключённый канал: обе ветки дают один и тот же результат."
        var candidates = new[]
        {
            new NotificationRouting.Candidate(WhatsAppChannelId, NotificationTransport.WhatsApp, IsUsable: true),
        };

        var priorityResult = NotificationRouting.SelectTargets(
            NotificationDeliveryMode.PriorityChannel, NotificationTransport.WhatsApp, candidates);
        var allChannelsResult = NotificationRouting.SelectTargets(
            NotificationDeliveryMode.AllChannels, NotificationTransport.WhatsApp, candidates);

        priorityResult.Targets.Should().BeEquivalentTo(allChannelsResult.Targets);
        priorityResult.Targets.Should().ContainSingle();
    }
}
