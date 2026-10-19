using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle43;

/// <summary>API_CONTRACT_CYCLE43.md §43.26, §43.27 — the texts the subscription history gets from connect, extend and switch-off.</summary>
public class MessengerHistoryTextsTests
{
    private static readonly DateTime Until = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NewUntil = new(2027, 1, 20, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OptionsSummary_UsesTheContractWording()
    {
        MessengerTexts.OptionsSummary(NotificationTransport.Max, MessengerLineStatus.Paid, false, Until).Should().Be("MAX: оплачено до 20.10.2026");
        MessengerTexts.OptionsSummary(NotificationTransport.Max, MessengerLineStatus.Paid, false, NewUntil).Should().Be("MAX: оплачено до 20.01.2027");
        MessengerTexts.OptionsSummary(NotificationTransport.Max, MessengerLineStatus.NotConnected, false, null).Should().Be("MAX: не подключён");
        MessengerTexts.OptionsSummary(NotificationTransport.WhatsApp, MessengerLineStatus.Paid, false, null).Should().Be("WhatsApp: оплачено");
    }

    [Fact]
    public void ExtendHistoryComment_DistinguishesConnectFromRenewal_AndAppendsTheAdminsComment()
    {
        MessengerTexts.ExtendHistoryComment(NotificationTransport.Max, true, 3, 490m, "Счёт 17")
            .Should().Be("Продление MAX на 3 мес. (3 мес. × 490 ₽ = 1470 ₽). Счёт 17");
        MessengerTexts.ExtendHistoryComment(NotificationTransport.Max, false, 3, 490m, null)
            .Should().Be("Подключение MAX на 3 мес. (3 мес. × 490 ₽ = 1470 ₽).");
    }

    [Fact]
    public void DisableHistoryComment_NamesTheDateAndTheReason() =>
        MessengerTexts.DisableHistoryComment(NotificationTransport.Max, NewUntil, " по заявлению ").Should().Be("Отключение MAX с 20.01.2027. по заявлению");
}
