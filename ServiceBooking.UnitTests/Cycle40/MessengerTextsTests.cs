using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>API_CONTRACT_CYCLE40.md §40.33 — wording that no vector covers.</summary>
public class MessengerTextsTests
{
    [Theory]
    [InlineData(1, "день")] [InlineData(2, "дня")] [InlineData(5, "дней")] [InlineData(11, "дней")] [InlineData(21, "день")] [InlineData(14, "дней")]
    public void StateText_NeedsReconnect_DeclinesDays(int days, string word) =>
        ChannelPresentation.StateText(NotificationTransport.Max, ChannelState.NeedsReconnect, null, days).Should()
            .StartWith($"Номер был отключён, потому что им {days} {word} никто не пользовался.");

    [Fact]
    public void StateText_NamesTheRightMessenger()
    {
        ChannelPresentation.StateText(NotificationTransport.Max, ChannelState.Disconnected, null, 0).Should().StartWith("Связь с MAX разорвана");
        ChannelPresentation.StateText(NotificationTransport.WhatsApp, ChannelState.Blocked, null, 0).Should().StartWith("WhatsApp заблокировал");
        ChannelPresentation.StateText(NotificationTransport.Max, ChannelState.Connected, "+7 ***", 0).Should().Be("Номер работает, сообщения уходят с номера +7 ***");
        ChannelPresentation.StateText(NotificationTransport.Max, ChannelState.Connected, null, 0).Should().Be("Номер работает");
    }

    [Fact]
    public void FundingText_ThreeStates()
    {
        var until = new DateTime(2026, 11, 20, 21, 30, 0, DateTimeKind.Utc); // 21.11 in Moscow
        MessengerTexts.FundingText(ChannelFundingState.NotPaid, NotificationTransport.Max, new(null, false), null).Should().Be("Номер MAX не оплачен");
        MessengerTexts.FundingText(ChannelFundingState.Funded, NotificationTransport.Max, new(until, false), null).Should().Be("Оплачено до 21.11.2026");
        MessengerTexts.FundingText(ChannelFundingState.Funded, NotificationTransport.Max, new(until, true), null).Should().Be("Пробный период до 21.11.2026");
        MessengerTexts.FundingText(ChannelFundingState.Unfunded, NotificationTransport.WhatsApp, new(until, false), "+7 *** ***-45-67").Should()
            .Be("Лишний номер WhatsApp: сообщения уходят с +7 *** ***-45-67. Отвяжите этот номер");
    }

    [Fact]
    public void NumbersText_JoinsShownTransports()
    {
        var until = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        MessengerTexts.NumbersText([(NotificationTransport.WhatsApp, false, false, null), (NotificationTransport.Max, true, true, until)])
            .Should().Be("WhatsApp: не подключён; MAX: пробный до 20.11.2026");
    }

    [Fact]
    public void Display_UsesMoscowDates()
    {
        var f = new ChannelDisplayFacts(NotificationTransport.Max, ChannelState.NotConnected, null, null, false,
            new DateTime(2026, 10, 19, 22, 0, 0, DateTimeKind.Utc), false, false, false, false, true, true, true, true, null, null, null);
        ChannelPresentation.Display(f).Text.Should().Be("Оплата закончилась 20.10.2026");
    }

    [Fact]
    public void Display_NeedsReconnect_WithoutIdleDays_DoesNotInventANumber()
    {
        var f = new ChannelDisplayFacts(NotificationTransport.Max, ChannelState.NeedsReconnect, null, null, true, null, false, false, false, false,
            true, true, true, true, null, null, null);
        ChannelPresentation.Display(f).Text.Should().NotMatchRegex(@"\d");
    }

    [Fact]
    public void PriceText_WholeAndFractional()
    {
        MessengerTexts.PriceText(490m).Should().Be("490 ₽/мес");
        MessengerTexts.PriceText(490.5m).Should().Be("490,50 ₽/мес");
    }

    [Fact]
    public void AddonsNoteTexts_AreStable()
    {
        MessengerTexts.ConditionsUrl.Should().Be("/offer-channel");
        MessengerTexts.ConditionsLabel.Should().Be("Условия");
    }
}
