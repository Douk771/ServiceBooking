using FluentAssertions;
using ServiceBooking.API.Services.Orders.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE24.md §486 — the fixed texts of order notifications; never a name or a phone [legal L10, L11].</summary>
public class OrderNotificationTextsTests
{
    private static readonly Guid Shop = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Order = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly Today = new(2026, 9, 30);

    private static OrderTextFacts Facts(
        PickupKind kind = PickupKind.Slot, DateOnly? date = null, int startMinutes = 750, int items = 3, decimal total = 540m,
        string? reason = null, int? previous = null) =>
        new("Шаурма на Ленина", 27, kind, date ?? Today, startMinutes, Today, items, total, reason, previous);

    // ── staff push ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StaffOrderCreated_Slot()
    {
        var p = OrderNotificationTexts.StaffOrderCreated(Facts(), Shop, Order);
        p.Title.Should().Be("Новый заказ № 27");
        p.Body.Should().Be("к 12:30 · 3 позиции · ≈ 540 ₽ · Шаурма на Ленина");
        p.Tag.Should().Be($"o-{Order}");
        p.Url.Should().Be($"/cabinet/{Shop}/orders?order={Order}");
    }

    [Fact]
    public void StaffOrderCreated_AsapAndPreorder()
    {
        OrderNotificationTexts.StaffOrderCreated(Facts(PickupKind.Asap), Shop, Order).Body.Should().StartWith("как можно скорее · ");
        OrderNotificationTexts.StaffOrderCreated(Facts(date: new DateOnly(2026, 10, 2)), Shop, Order).Body.Should().StartWith("пт 2 окт, к 12:30 · ");
    }

    [Theory]
    [InlineData(1, "1 позиция")]
    [InlineData(2, "2 позиции")]
    [InlineData(5, "5 позиций")]
    [InlineData(11, "11 позиций")]
    [InlineData(21, "21 позиция")]
    [InlineData(22, "22 позиции")]
    public void StaffOrderCreated_PluralOfItems(int items, string expected) =>
        OrderNotificationTexts.StaffOrderCreated(Facts(items: items), Shop, Order).Body.Should().Contain(expected);

    [Fact]
    public void StaffOrderCancelledByCustomer()
    {
        var p = OrderNotificationTexts.StaffOrderCancelledByCustomer(Facts(), Shop, Order);
        p.Title.Should().Be("Покупатель отменил заказ № 27");
        p.Body.Should().Be("к 12:30 · Шаурма на Ленина");
        p.Tag.Should().Be($"o-{Order}");
    }

    [Fact]
    public void OwnerOrderLimitWarning_80AndReached()
    {
        var month = new DateOnly(2026, 10, 1);
        var warn = OrderNotificationTexts.OwnerOrderLimitWarning(false, 120, 150, month);
        (warn.Title, warn.Body, warn.Tag, warn.Url).Should().Be(("Использовано 80 % лимита заказов", "120 из 150 заказов в октябре", "limit-2026-10", "/cabinet/subscription"));
        var reached = OrderNotificationTexts.OwnerOrderLimitWarning(true, 150, 150, month);
        reached.Title.Should().Be("Лимит заказов исчерпан");
        reached.Body.Should().Be("150 из 150 в октябре. Новые заказы не принимаются до 1 ноября или смены тарифа");
    }

    // ── customer ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Customer_TimePhrases_AreLowerCase()
    {
        OrderNotificationTexts.CustomerTimePhrase(Facts(PickupKind.Asap)).Should().Be("как можно скорее");
        OrderNotificationTexts.CustomerTimePhrase(Facts()).Should().Be("сегодня к 12:30");
        OrderNotificationTexts.CustomerTimePhrase(Facts(date: Today.AddDays(1))).Should().Be("завтра к 12:30");
        OrderNotificationTexts.CustomerTimePhrase(Facts(date: new DateOnly(2026, 10, 2))).Should().Be("пт 2 окт, к 12:30");
    }

    [Theory]
    [InlineData(NotificationType.OrderAccepted, "Заказ № 27 принят. Получение: сегодня к 12:30.")]
    [InlineData(NotificationType.OrderReady, "Заказ № 27 готов к выдаче.")]
    [InlineData(NotificationType.OrderRejected, "Заказ № 27 отклонён.")]
    [InlineData(NotificationType.OrderCancelledByShop, "Заказ № 27 отменён магазином.")]
    [InlineData(NotificationType.OrderEditedByShop, "Магазин изменил заказ № 27, итог ≈ 540 ₽. Подробности по ссылке.")]
    [InlineData(NotificationType.OrderPickupChanged, "Изменено время получения заказа № 27: сегодня к 12:30.")]
    public void CustomerSentence_PerType(NotificationType type, string expected) =>
        OrderNotificationTexts.CustomerSentence(type, Facts()).Should().Be(expected);

    [Fact]
    public void Reason_IsAppendedOnlyWhenGiven()
    {
        OrderNotificationTexts.CustomerSentence(NotificationType.OrderRejected, Facts(reason: "нет продуктов")).Should().Be("Заказ № 27 отклонён. Причина: нет продуктов");
        OrderNotificationTexts.CustomerSentence(NotificationType.OrderCancelledByShop, Facts(reason: "  ")).Should().Be("Заказ № 27 отменён магазином.");
    }

    [Fact]
    public void PickupChanged_NamesTheNewNumberOnlyWhenItChanged()
    {
        var facts = Facts(date: new DateOnly(2026, 10, 2), startMinutes: 840, previous: 5) with { Number = 12 };
        OrderNotificationTexts.CustomerSentence(NotificationType.OrderPickupChanged, facts)
            .Should().Be("Изменено время получения заказа № 5 (теперь № 12): пт 2 окт, к 14:00.");
        OrderNotificationTexts.CustomerSentence(NotificationType.OrderPickupChanged, facts with { Number = 5 })
            .Should().Be("Изменено время получения заказа № 5: пт 2 окт, к 14:00.");
    }

    [Fact]
    public void NonCustomerType_IsAProgrammingError() =>
        FluentActions.Invoking(() => OrderNotificationTexts.CustomerSentence(NotificationType.StaffOrderCreated, Facts()))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void CustomerWebPush_ShopIsTheTitle_TokenOnlyInTheUrl()
    {
        var p = OrderNotificationTexts.CustomerWebPush(NotificationType.OrderReady, Facts(), Order, "tok-123");
        (p.Title, p.Body, p.Tag, p.Url).Should().Be(("Шаурма на Ленина", "Заказ № 27 готов к выдаче.", $"co-{Order}", "/o/tok-123"));
    }

    [Fact]
    public void Messenger_ShopPrefix_OrderLink_UnsubscribeLine()
    {
        var text = OrderNotificationTexts.Messenger(NotificationType.OrderAccepted, Facts(), "https://goods.ezbook.ru/o/tok", "https://ezbook.ru/u/abc");
        text.Should().Be(
            "Шаурма на Ленина: заказ № 27 принят. Получение: сегодня к 12:30.\nЗаказ: https://goods.ezbook.ru/o/tok\nОтказаться от уведомлений: https://ezbook.ru/u/abc");
    }

    [Fact]
    public void Messenger_WithoutUnsubscribeKey_OmitsTheLine() =>
        OrderNotificationTexts.Messenger(NotificationType.OrderReady, Facts(), "https://goods.ezbook.ru/o/tok", null)
            .Should().NotContain("Отказаться");

    [Fact]
    public void NoText_EverCarriesAPhoneOrAName()
    {
        // The facts have no such field; this guards against adding one into a text by string concatenation.
        var all = Enum.GetValues<NotificationType>().Where(t => t is >= NotificationType.OrderAccepted and <= NotificationType.OrderPickupChanged)
            .Select(t => OrderNotificationTexts.Messenger(t, Facts(reason: "r"), "https://goods.ezbook.ru/o/tok", "https://ezbook.ru/u/abc"));
        foreach (var text in all) text.Should().NotMatchRegex(@"\+?7\d{10}").And.NotContain("Иван");
    }
}
