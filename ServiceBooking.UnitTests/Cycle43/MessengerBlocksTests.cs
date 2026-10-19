using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.API.DTOs.Billing;

namespace ServiceBooking.UnitTests.Cycle43;

/// <summary>ARCHITECTURE_CYCLE43.md §43.6.6, §43.8 — the admin card block and the owner block over one messaging state (no database).</summary>
public class MessengerBlocksTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly LegacySubscriptionFacts NoSubscription = new(false, null);
    private static readonly Guid CompanyId = Guid.NewGuid();

    private static TransportOptionState Sellable(decimal? price = 490m) => new(true, true, price, true, true, Guid.NewGuid());
    private static TransportOptionState NoPrice() => new(true, false, null, true, true, Guid.NewGuid());

    private static NotificationChannel Channel(NotificationTransport t, ChannelState state, string? phone = null) => new()
    {
        Id = Guid.NewGuid(), Transport = t, State = state, PhoneNumber = phone, CreatedAt = Now.AddDays(-3),
    };

    private static TransportState Transport(
        NotificationTransport t, OptionRowFacts? row, TransportOptionState option, params NotificationChannel[] channels) =>
        AccountMessagingReader.BuildTransport(t, row, null, NoSubscription, Now, channels, option, true);

    private static AccountMessagingState State(TransportState whatsApp, TransportState max, bool platformEnabled = true) =>
        new(Guid.NewGuid(), platformEnabled, whatsApp, max, []);

    private static OptionRowFacts PaidRow(DateTime until, DateTime? endsAt = null) => new(endsAt, until, false, Now.AddDays(-10));

    private static AccountMessagingState Empty(TransportOptionState wa, TransportOptionState max) =>
        State(Transport(NotificationTransport.WhatsApp, null, wa), Transport(NotificationTransport.Max, null, max));

    [Fact]
    public void AdminBlock_AlwaysHasTwoLines_WhatsAppThenMax()
    {
        var lines = AdminMessengerLinesBuilder.Build(Empty(NoPrice(), NoPrice()), Now);

        lines.Select(l => l.Transport).Should().Equal(NotificationTransport.WhatsApp, NotificationTransport.Max);
        lines.Should().OnlyContain(l => l.Status == MessengerLineStatus.NotConnected && !l.CanExtend && !l.CanDisable);
        lines[1].ExtendDisabledText.Should().Be("Не продаётся: цена не задана");
        lines[1].PriceText.Should().Be("Цена не задана");
        lines[1].Number.State.Should().Be(MessengerNumberState.None);
        lines[1].Number.Text.Should().Be("Номера нет");
    }

    [Fact]
    public void AdminBlock_PaidSellableLine_CanBeExtendedAndDisabled_WithServerTexts()
    {
        var until = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var state = State(Transport(NotificationTransport.WhatsApp, null, Sellable(390m)), Transport(NotificationTransport.Max, PaidRow(until), Sellable()));

        var line = AdminMessengerLinesBuilder.Build(state, Now)[1];

        line.Status.Should().Be(MessengerLineStatus.Paid);
        line.StatusText.Should().Be("Оплачено до 20.11.2026");
        line.MonthlyPrice.Should().Be(490m);
        line.CountsInTotal.Should().BeTrue();
        line.CanExtend.Should().BeTrue();
        line.ExtendActionText.Should().Be("Продлить");
        line.ExtendDisabledText.Should().BeNull();
        line.CanDisable.Should().BeTrue();
        line.DisableNowConfirmText.Should().Be("Деньги не возвращаются. Сообщения клиентам в MAX перестанут уходить с 20.10.2026");
        line.DisablePeriodEndConfirmText.Should().Be("Деньги не возвращаются. Сообщения клиентам в MAX перестанут уходить с 20.11.2026");
        AdminMessengerLinesBuilder.Total(state).Should().Be(490m);
    }

    [Fact]
    public void AdminBlock_EndingLine_CannotBeScheduledAgain_ButCanBeSwitchedOffNow()
    {
        var until = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var state = State(Transport(NotificationTransport.WhatsApp, null, Sellable(390m)), Transport(NotificationTransport.Max, PaidRow(until, until), Sellable()));

        var line = AdminMessengerLinesBuilder.Build(state, Now)[1];

        line.Status.Should().Be(MessengerLineStatus.Ending);
        line.CanDisable.Should().BeTrue();
        line.DisableNowConfirmText.Should().NotBeNull();
        line.DisablePeriodEndConfirmText.Should().BeNull();
    }

    [Fact]
    public void AdminBlock_ExpiredLine_OfferedAsConnect_AndCannotBeDisabled()
    {
        var state = State(Transport(NotificationTransport.WhatsApp, null, Sellable(390m)),
            Transport(NotificationTransport.Max, PaidRow(Now.AddDays(-3)), Sellable()));

        var line = AdminMessengerLinesBuilder.Build(state, Now)[1];

        line.Status.Should().Be(MessengerLineStatus.Expired);
        line.ExtendActionText.Should().Be("Подключить");
        line.CanDisable.Should().BeFalse();
        line.DisableNowConfirmText.Should().BeNull();
        line.MonthlyPrice.Should().Be(0m);
        line.CountsInTotal.Should().BeFalse();
    }

    [Fact]
    public void AdminBlock_NumberStates_FollowTheFirstLiveChannel()
    {
        NumberOf(Channel(NotificationTransport.Max, ChannelState.NotConnected)).State.Should().Be(MessengerNumberState.AwaitingQr);
        NumberOf(Channel(NotificationTransport.Max, ChannelState.Connecting)).Text.Should().Be("Номер ожидает привязки по QR");
        var bound = NumberOf(Channel(NotificationTransport.Max, ChannelState.Connected, "79991234567"));
        bound.State.Should().Be(MessengerNumberState.Bound);
        bound.PhoneMasked.Should().NotBeNullOrEmpty().And.NotContain("79991234567");
        bound.Text.Should().Be($"Номер {bound.PhoneMasked}");
        NumberOf(Channel(NotificationTransport.Max, ChannelState.Disconnected)).Text.Should().Be("Номер привязан");
    }

    private static MessengerNumberDto NumberOf(NotificationChannel channel) =>
        AdminMessengerLinesBuilder.NumberOf(Transport(NotificationTransport.Max, null, Sellable(), channel));

    [Fact]
    public void OwnerBlock_NothingToShow_IsNull() =>
        OwnerMessengersBlockBuilder.Build(Empty(NoPrice(), NoPrice()), CompanyId).Should().BeNull();

    [Fact]
    public void OwnerBlock_SellableNotConnected_OffersConnect_WithTheTargetCompany()
    {
        var block = OwnerMessengersBlockBuilder.Build(Empty(NoPrice(), Sellable()), CompanyId)!;

        block.Lines.Should().ContainSingle();
        var line = block.Lines[0];
        line.Transport.Should().Be(NotificationTransport.Max);
        line.Status.Should().Be(MessengerLineStatus.NotConnected);
        line.PriceText.Should().Be("490 ₽/мес");
        line.Action.Should().Be(OwnerMessengerAction.Connect);
        line.ActionText.Should().Be("Подключить");
        line.TargetCompanyId.Should().Be(CompanyId);
        line.NoCompanyText.Should().BeNull();
        line.Footnote.Should().BeNull();
        block.Title.Should().Be("Сообщения клиентам в мессенджеры");
        block.MonthlyTotalText.Should().Be("Мессенджеры: 0 ₽/мес");
    }

    [Fact]
    public void OwnerBlock_WithoutACompanyOfTheLine_SaysToCreateOne()
    {
        var line = OwnerMessengersBlockBuilder.Build(Empty(NoPrice(), Sellable()), null)!.Lines[0];

        line.Action.Should().Be(OwnerMessengerAction.Connect);
        line.TargetCompanyId.Should().BeNull();
        line.NoCompanyText.Should().Be("Сначала создайте компанию");
    }

    [Fact]
    public void OwnerBlock_PaidLine_NoButton_TotalIsThePrice_WhatsAppHasTheFootnote()
    {
        var until = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var state = State(Transport(NotificationTransport.WhatsApp, PaidRow(until), Sellable(390m)), Transport(NotificationTransport.Max, null, NoPrice()));

        var block = OwnerMessengersBlockBuilder.Build(state, CompanyId)!;

        var line = block.Lines.Should().ContainSingle().Subject;
        line.Status.Should().Be(MessengerLineStatus.Paid);
        line.Action.Should().BeNull();
        line.ActionText.Should().BeNull();
        line.Footnote.Should().Be("Доступ к WhatsApp в России ограничен: сообщения могут не доходить");
        block.MonthlyTotal.Should().Be(390m);
        block.MonthlyTotalText.Should().Be("Мессенджеры: 390 ₽/мес");
    }

    [Fact]
    public void OwnerBlock_ExpiredAndNoLongerSold_ShowsTheStatusButNoPriceAndNoButton()
    {
        var state = State(Transport(NotificationTransport.WhatsApp, null, NoPrice()),
            Transport(NotificationTransport.Max, PaidRow(Now.AddDays(-3)), NoPrice(), Channel(NotificationTransport.Max, ChannelState.Connected)));

        var line = OwnerMessengersBlockBuilder.Build(state, CompanyId)!.Lines.Should().ContainSingle().Subject;

        line.Status.Should().Be(MessengerLineStatus.Expired);
        line.PricePerMonth.Should().BeNull();
        line.PriceText.Should().BeNull();
        line.Action.Should().BeNull();
    }

    [Fact]
    public void OwnerBlock_PlatformMessagingOff_NoButtons()
    {
        var state = State(Transport(NotificationTransport.WhatsApp, null, Sellable(390m)), Transport(NotificationTransport.Max, null, Sellable()), platformEnabled: false);

        OwnerMessengersBlockBuilder.Build(state, CompanyId)!.Lines.Should().OnlyContain(l => l.Action == null);
    }
}
