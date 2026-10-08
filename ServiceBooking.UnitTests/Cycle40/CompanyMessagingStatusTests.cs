using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;
using static ServiceBooking.Core.Enums.NotificationTransport;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.6.4, API_CONTRACT_CYCLE40.md §40.29, §40.33.6.</summary>
public class CompanyMessagingStatusTests
{
    private static TransportMessagingFacts T(NotificationTransport t, bool open = true, bool paid = false, bool routable = false, bool working = false) =>
        new(t, open, paid, routable, working);

    private static CompanyMessagingStatusResult Eval(bool platform, NotificationDeliveryMode mode, NotificationTransport priority, params TransportMessagingFacts[] ts) =>
        CompanyMessagingStatus.Evaluate(platform, mode, priority, ts);

    [Fact]
    public void OneWorking_ActiveWithoutChoice()
    {
        var r = Eval(true, NotificationDeliveryMode.PriorityChannel, WhatsApp, T(WhatsApp, open: false), T(Max, paid: true, routable: true, working: true));
        r.MessagingActive.Should().BeTrue();
        r.WorkingTransports.Should().Equal(Max);
        r.DeliveryChoiceVisible.Should().BeFalse();
        r.BlockedReason.Should().BeNull();
        r.InactiveText.Should().BeNull();
    }

    [Fact]
    public void BothWorking_ChoiceVisible_NoWarning()
    {
        var r = Eval(true, NotificationDeliveryMode.AllChannels, WhatsApp,
            T(WhatsApp, paid: true, routable: true, working: true), T(Max, paid: true, routable: true, working: true));
        r.WorkingTransports.Should().Equal(WhatsApp, Max);
        r.DeliveryChoiceVisible.Should().BeTrue();
        r.PriorityWarning.Should().BeNull();
    }

    [Fact]
    public void BothRoutable_PriorityNotWorking_ChoiceVisibleWithWarning()
    {
        var r = Eval(true, NotificationDeliveryMode.PriorityChannel, WhatsApp,
            T(WhatsApp, paid: true, routable: true), T(Max, paid: true, routable: true, working: true));
        r.DeliveryChoiceVisible.Should().BeTrue();
        r.PriorityWarning.Should().Be("Приоритетный номер не работает: выберите другой или „во все“");
    }

    [Fact]
    public void BothRoutable_AllChannels_OneBroken_NoChoiceNoWarning()
    {
        var r = Eval(true, NotificationDeliveryMode.AllChannels, WhatsApp,
            T(WhatsApp, paid: true, routable: true), T(Max, paid: true, routable: true, working: true));
        r.DeliveryChoiceVisible.Should().BeFalse();
        r.PriorityWarning.Should().BeNull();
    }

    [Fact]
    public void PlatformDisabled_NotActive_ReasonIsPlatform()
    {
        var r = Eval(false, NotificationDeliveryMode.PriorityChannel, WhatsApp, T(Max, paid: true, routable: true, working: true));
        r.MessagingActive.Should().BeFalse();
        r.BlockedReason.Should().Be("Рассылки временно отключены платформой");
    }

    [Fact]
    public void NothingOpenNothingPaid_ConnectionUnavailable() =>
        Eval(true, NotificationDeliveryMode.PriorityChannel, WhatsApp, T(WhatsApp, open: false), T(Max, open: false))
            .BlockedReason.Should().Be("Подключение мессенджеров сейчас недоступно");

    [Fact]
    public void NothingPaid_AskToConnect_AndInactiveTextNamesOnlyOpenMessenger()
    {
        var r = Eval(true, NotificationDeliveryMode.PriorityChannel, WhatsApp, T(WhatsApp, open: false), T(Max));
        r.BlockedReason.Should().Be("Подключите WhatsApp или MAX");
        r.InactiveText.Should().Be("Подключите MAX выше");
    }

    [Fact]
    public void BothOpen_InactiveTextNamesBoth() =>
        Eval(true, NotificationDeliveryMode.PriorityChannel, WhatsApp, T(WhatsApp), T(Max)).InactiveText.Should().Be("Подключите WhatsApp или MAX выше");

    [Fact]
    public void PaidButNotWorking_NumberNotWorking() =>
        Eval(true, NotificationDeliveryMode.PriorityChannel, WhatsApp, T(Max, paid: true, routable: true))
            .BlockedReason.Should().Be("Номер не работает — откройте блок „Номера“");

    [Fact]
    public void ShopTexts()
    {
        CompanyMessagingStatus.MessengerUnavailableText([T(Max)]).Should().Be("Подключите номер в блоке „Номера“");
        CompanyMessagingStatus.MessengerUnavailableText([T(Max, paid: true)]).Should().Be("Номер оплачен, но ещё не привязан — привяжите его в блоке „Номера“");
        CompanyMessagingStatus.MessengerUnavailableText([T(Max, paid: true, routable: true)]).Should().BeNull();
    }
}
