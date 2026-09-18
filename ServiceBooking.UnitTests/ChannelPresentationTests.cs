using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE4.md §25/§19.5 — the one place ChannelDto.stateText and
/// NotificationSettingsDto.channel.stateText are both built from, so the two can never say two
/// different things about the same event (this cycle's authorized contract fix, §28.1).</summary>
public class ChannelPresentationTests
{
    [Theory]
    [InlineData(ChannelState.NotConnected)]
    [InlineData(ChannelState.Connecting)]
    [InlineData(ChannelState.Disconnected)]
    [InlineData(ChannelState.Blocked)]
    [InlineData(ChannelState.DisabledByOwner)]
    [InlineData(ChannelState.Replaced)]
    public void StateText_EveryStateProducesNonEmptyText(ChannelState state)
    {
        ChannelPresentation.StateText(state, phoneMasked: null, idleDays: 3, paidUntilUtc: null)
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void StateText_Connected_IncludesMaskedPhone()
    {
        var text = ChannelPresentation.StateText(ChannelState.Connected, "+7 999 ***-**-45", 3, null);
        text.Should().Contain("+7 999 ***-**-45");
    }

    [Fact]
    public void StateText_NeedsReconnect_IsDistinctFromNotConnected()
    {
        var notConnected = ChannelPresentation.StateText(ChannelState.NotConnected, null, 3, null);
        var needsReconnect = ChannelPresentation.StateText(ChannelState.NeedsReconnect, null, 3, DateTime.UtcNow.AddDays(10));

        // SPEC US-55 p.2: "отличать это состояние от «не подключён» обязательно".
        needsReconnect.Should().NotBe(notConnected);
        needsReconnect.Should().Contain("подключите");
    }

    [Fact]
    public void StateText_NeedsReconnect_MentionsIdleDaysAndPaidUntilDate()
    {
        var paidUntil = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        var text = ChannelPresentation.StateText(ChannelState.NeedsReconnect, null, idleDays: 3, paidUntilUtc: paidUntil);

        text.Should().Contain("3").And.Contain("18.10");
    }

    [Theory]
    [InlineData(ChannelState.NotConnected, ChannelPaymentStatus.Paid, true, true)]
    [InlineData(ChannelState.NeedsReconnect, ChannelPaymentStatus.Paid, true, true)]
    [InlineData(ChannelState.Connected, ChannelPaymentStatus.Paid, true, false)] // already connected
    [InlineData(ChannelState.NotConnected, ChannelPaymentStatus.NotPaid, true, false)] // unpaid
    [InlineData(ChannelState.NotConnected, ChannelPaymentStatus.Paid, false, false)] // risk not accepted
    public void CanConnect_MatchesExpectedGate(ChannelState state, ChannelPaymentStatus paymentState, bool riskAccepted, bool expected)
    {
        ChannelPresentation.CanConnect(state, paymentState, riskAccepted).Should().Be(expected);
    }

    [Theory]
    [InlineData(ChannelState.Blocked, true)]
    [InlineData(ChannelState.Connected, false)]
    [InlineData(ChannelState.NotConnected, false)]
    public void CanReplace_OnlyTrueForBlocked(ChannelState state, bool expected)
    {
        ChannelPresentation.CanReplace(state).Should().Be(expected);
    }

    [Fact]
    public void SettingsBlockedReason_PlanDeniedTakesPriorityOverEverythingElse()
    {
        var reason = ChannelPresentation.SettingsBlockedReason(
            planAllowsChannel: false, companyHasAssignment: true, paymentState: ChannelPaymentStatus.Paid, channelState: ChannelState.Connected);

        reason.Should().Be("Недоступно на вашем тарифе");
    }

    [Fact]
    public void SettingsBlockedReason_NoAssignment_WhenPlanAllows()
    {
        var reason = ChannelPresentation.SettingsBlockedReason(
            planAllowsChannel: true, companyHasAssignment: false, paymentState: null, channelState: null);

        reason.Should().Be("Салон не привязан к каналу");
    }

    [Fact]
    public void SettingsBlockedReason_NotPaid()
    {
        var reason = ChannelPresentation.SettingsBlockedReason(
            planAllowsChannel: true, companyHasAssignment: true, paymentState: ChannelPaymentStatus.NotPaid, channelState: ChannelState.NotConnected);

        reason.Should().Be("Канал не оплачен");
    }

    [Fact]
    public void SettingsBlockedReason_PaidButNotConnected()
    {
        var reason = ChannelPresentation.SettingsBlockedReason(
            planAllowsChannel: true, companyHasAssignment: true, paymentState: ChannelPaymentStatus.Paid, channelState: ChannelState.Disconnected);

        reason.Should().Be("Канал отвалился");
    }

    [Fact]
    public void SettingsBlockedReason_EverythingOk_ReturnsNull()
    {
        var reason = ChannelPresentation.SettingsBlockedReason(
            planAllowsChannel: true, companyHasAssignment: true, paymentState: ChannelPaymentStatus.Paid, channelState: ChannelState.Connected);

        reason.Should().BeNull();
    }
}
