using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle43;

public class MessengerPriceChangeRulesTests
{
    private static readonly MessengerPriceState Current = new(490m, true);

    [Fact]
    public void Validate_UnchangedPriceAndActivity_IsRejected() =>
        MessengerPriceChangeRules.Validate(490m, null, Current, false, null).Should().Be(MessengerTexts.PriceUnchanged);

    [Fact]
    public void Validate_SamePriceWithOtherActivity_IsAChange() =>
        MessengerPriceChangeRules.Validate(490m, false, Current, false, null).Should().BeNull();

    [Fact]
    public void Validate_RemovingThePrice_IsAChange() =>
        MessengerPriceChangeRules.Validate(null, null, Current, false, null).Should().BeNull();

    [Fact]
    public void Validate_PriceErrorComesBeforeUnchangedAndComment() =>
        MessengerPriceChangeRules.Validate(-1m, null, Current, true, "").Should().Be(MessengerTexts.PriceNegative);

    [Theory, InlineData(null), InlineData(""), InlineData("   ")]
    public void Validate_BlankComment_IsRequiredOnPut(string? comment) =>
        MessengerPriceChangeRules.Validate(590m, null, Current, true, comment).Should().Be(MessengerTexts.PriceCommentRequired);

    [Fact]
    public void Validate_CommentOver500AfterTrim_IsRejected()
    {
        MessengerPriceChangeRules.Validate(590m, null, Current, true, new string('a', 501)).Should().Be(MessengerTexts.PriceCommentTooLong);
        MessengerPriceChangeRules.Validate(590m, null, Current, true, " " + new string('a', 500) + " ").Should().BeNull();
    }

    [Fact]
    public void Validate_CommentIsNotCheckedForPreview() =>
        MessengerPriceChangeRules.Validate(590m, null, Current, false, null).Should().BeNull();

    [Fact]
    public void Validate_MissingCatalogRow_SkipsTheUnchangedCheck() =>
        MessengerPriceChangeRules.Validate(490m, null, null, false, null).Should().BeNull();

    [Theory, InlineData(1, "аккаунта"), InlineData(2, "аккаунтов"), InlineData(11, "аккаунтов"), InlineData(21, "аккаунта"), InlineData(111, "аккаунтов")]
    public void AccountsWord_FollowsTheContractRule(int n, string expected) => MessengerTexts.AccountsWord(n).Should().Be(expected);

    [Fact]
    public void Quote_AddsMonthsToTheLaterOfNowAndPaidUntil()
    {
        var now = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        MessengerChargeRules.NewPaidUntil(now.AddDays(-5), 1, now).Should().Be(now.AddMonths(1));
        MessengerChargeRules.NewPaidUntil(now.AddDays(10), 1, now).Should().Be(now.AddDays(10).AddMonths(1));
        MessengerChargeRules.NewPaidUntil(null, 2, now).Should().Be(now.AddMonths(2));
    }

    [Fact]
    public void NotSellableConflict_Texts_AreTheContractOnes()
    {
        var max = NotificationTransport.Max;
        MessengerTexts.NotSellableConflict(max, API.Services.Notifications.Funding.MessengerSaleReason.Closed)
            .Should().Be("Опция MAX закрыта для подключения. Откройте её в блоке „Подключение мессенджеров“");
        MessengerTexts.NotSellableConflict(max, API.Services.Notifications.Funding.MessengerSaleReason.Inactive)
            .Should().Be("Опция MAX неактивна. Включите её во вкладке уведомлений, блок „Подключение мессенджеров“");
        MessengerTexts.NotSellableConflict(max, API.Services.Notifications.Funding.MessengerSaleReason.NoPrice)
            .Should().Be("У MAX не задана цена. Задайте её во вкладке уведомлений, блок „Подключение мессенджеров“");
        MessengerTexts.NotSellableConflict(max, API.Services.Notifications.Funding.MessengerSaleReason.OfferNotPublished)
            .Should().Be("Оферта для владельцев не опубликована — MAX не продаётся");
        MessengerTexts.PriceChangedConflict(max, 590m).Should().Be("Цена MAX изменилась: теперь 590 ₽/мес. Проверьте сумму и повторите");
    }
}
