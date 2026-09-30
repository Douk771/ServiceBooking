using FluentAssertions;
using ServiceBooking.API.Services.Orders.Notifications;
using ServiceBooking.API.Services.StaffMax;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE25.md §525 [legal L15] — fixed MAX texts; never a customer name or phone.</summary>
public class StaffMaxTextsTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);
    private const string Url = "https://goods.ezbook.ru/cabinet/S/orders?order=O";

    private static OrderTextFacts Facts(PickupKind kind = PickupKind.Slot, DateOnly? date = null, string shop = "Шаурма на Ленина", int items = 3) =>
        new(shop, 27, kind, date ?? Today, 750, Today, items, 540m);

    [Fact]
    public void OrderCreated_MatchesTheContractText() =>
        StaffMaxTexts.OrderCreated(Facts(), Url).Should().Be(
            "Новый заказ № 27 · к 12:30 · 3 позиции · ≈ 540 ₽ · Шаурма на Ленина\nОткрыть: " + Url);

    [Fact]
    public void OrderCreated_AsapAndOtherDay()
    {
        StaffMaxTexts.OrderCreated(Facts(PickupKind.Asap), Url).Should().Contain("· как можно скорее ·");
        StaffMaxTexts.OrderCreated(Facts(date: new DateOnly(2026, 10, 2)), Url).Should().Contain("пт 2 окт, к 12:30");
    }

    [Fact]
    public void OrderCancelled_MatchesTheContractText() =>
        StaffMaxTexts.OrderCancelledByCustomer(Facts(), Url).Should().Be(
            "Покупатель отменил заказ № 27 (к 12:30) · Шаурма на Ленина\nОткрыть: " + Url);

    [Fact]
    public void LongShopName_IsCutTo60()
    {
        var text = StaffMaxTexts.OrderCreated(Facts(shop: new string('Ш', 200)), Url);
        text.Should().Contain(new string('Ш', 59) + "…");
        text.Should().NotContain(new string('Ш', 60));
    }

    [Fact]
    public void LimitWarning_BothForms()
    {
        StaffMaxTexts.OwnerOrderLimitWarning(false, 120, 150, new DateOnly(2026, 10, 1), "https://goods.ezbook.ru/cabinet/subscription")
            .Should().Be("Использовано 80 % лимита заказов: 120 из 150 заказов в октябре\nПодписка: https://goods.ezbook.ru/cabinet/subscription");
        StaffMaxTexts.OwnerOrderLimitWarning(true, 150, 150, new DateOnly(2026, 10, 1), "U")
            .Should().Be("Лимит заказов исчерпан: 150 из 150 в октябре. Новые заказы не принимаются до 1 ноября или смены тарифа\nПодписка: U");
    }

    [Fact]
    public void Linked_ListsUpToFiveShops_ThenAndMore()
    {
        StaffMaxTexts.Linked(["Шаурма на Ленина", "Пекарня"]).Should().Contain("«Шаурма на Ленина», «Пекарня».");
        var many = StaffMaxTexts.Linked(Enumerable.Range(1, 8).Select(i => $"М{i}").ToList());
        many.Should().Contain("«М5» и ещё 3.").And.NotContain("«М6»");
    }

    [Fact]
    public void BotReplies_AreTheContractTexts()
    {
        StaffMaxTexts.AlreadyLinked.Should().Be("Этот чат уже подключён к заказам ezbook.");
        StaffMaxTexts.LinkExpired.Should().StartWith("Ссылка устарела.");
        StaffMaxTexts.Disabled.Should().Be("Сообщения о заказах в MAX пока не включены.");
        StaffMaxTexts.NoShops.Should().Be("Вы больше не состоите ни в одном магазине — подключать нечего.");
    }
}
