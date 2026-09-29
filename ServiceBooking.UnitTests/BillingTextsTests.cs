using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE7.md §47.1 — funding text is an acceptance criterion, not decoration:
/// the Unfunded text must name the cause (paid-vs-configured counts), the working number, and both
/// ways to fix it.</summary>
public class BillingTextsTests
{
    [Fact]
    public void FundingText_Funded_NoOversupply_SimpleText()
    {
        var text = BillingTexts.FundingText(ChannelFundingState.Funded, paidNumbers: 1, liveCount: 1, workingPhoneMasked: null);

        text.Should().Be("Номер оплачен и работает.");
    }

    [Fact]
    public void FundingText_Funded_ButMoreLiveThanPaid_NamesTheCount()
    {
        var text = BillingTexts.FundingText(ChannelFundingState.Funded, paidNumbers: 1, liveCount: 2, workingPhoneMasked: null);

        text.Should().Contain("1").And.Contain("2");
    }

    [Fact]
    public void FundingText_Unfunded_NamesCauseWorkingNumberAndBothFixes()
    {
        var text = BillingTexts.FundingText(
            ChannelFundingState.Unfunded, paidNumbers: 1, liveCount: 2, workingPhoneMasked: "+7 999 ***-**-45");

        // Cause: how many paid vs how many configured.
        text.Should().Contain("оплачен 1").And.Contain("заведено 2");
        // Which number works instead.
        text.Should().Contain("+7 999 ***-**-45");
        // Both fixes: buy another slot, or delete the extra number.
        text.Should().Contain("подключите ещё одну").And.Contain("удалите лишний номер");
    }

    [Fact]
    public void FundingText_NotPaid_PointsAtSubscriptionScreen()
    {
        var text = BillingTexts.FundingText(ChannelFundingState.NotPaid, paidNumbers: 0, liveCount: 1, workingPhoneMasked: null);

        text.Should().Contain("не оплачена").And.Contain("Ваша подписка");
    }

    [Fact]
    public void FundingText_NeverMentionsBillingAccount_OwnerFacingWordingRule()
    {
        foreach (var state in new[] { ChannelFundingState.Funded, ChannelFundingState.Unfunded, ChannelFundingState.NotPaid })
        {
            var text = BillingTexts.FundingText(state, paidNumbers: 1, liveCount: 2, workingPhoneMasked: "+7 999 ***-**-45");
            text.Should().NotContain("биллинг-аккаунт", "§41 п. 8 — that word is admin-only vocabulary");
        }
    }

    // ARCHITECTURE_CYCLE19.md §411/§414 — SeatLimitReached no longer breaks the limit down into
    // plan-included/purchased/bonus; `limit` is the caller-computed total (AccountLimitFormula), and the
    // text never mentions an option to buy any more.
    [Fact]
    public void SeatLimitReached_MentionsUsedAndLimit()
    {
        var text = BillingTexts.SeatLimitReached(used: 8, planName: "Базовый", limit: 8);

        text.Should().Contain("8").And.Contain("Лимит общий на все ваши точки.");
    }

    [Fact]
    public void SeatLimitReached_NamesPlanAndTotalLimit_PerContract411()
    {
        // §411's own sample text: "Занято {used} из {limit} мест — столько включено в тариф «{planName}»."
        var text = BillingTexts.SeatLimitReached(used: 8, planName: "Базовый", limit: 8);

        text.Should().Contain("8 из 8 мест");
        text.Should().Contain("включено в тариф «Базовый»");
        text.Should().Contain("выберите тариф с большим лимитом");
    }

    [Fact]
    public void SeatLimitReached_NeverMentionsBuyingAnOption()
    {
        var text = BillingTexts.SeatLimitReached(used: 5, planName: "Базовый", limit: 5);

        text.Should().NotContain("докуплено");
        text.Should().NotContain("подключите опцию");
    }

    // ── §51.1/§51.2 transfer texts ────────────────────────────────────────────

    [Fact]
    public void TransferRejectedUnlinkedOwner_NamesAllThreeWaysOut()
    {
        var text = BillingTexts.TransferRejectedUnlinkedOwner("Мария Сидорова");

        text.Should().Contain("Мария Сидорова");
        text.Should().Contain("держателем").And.Contain("сотрудником").And.Contain("без смены");
    }

    [Fact]
    public void TransferRejectedCompanyLimit_NamesPlanUsedAndLimit()
    {
        // ARCHITECTURE_CYCLE19.md §411/§414 — no more option to buy; the fix is a bigger tariff.
        var text = BillingTexts.TransferRejectedCompanyLimit("Базовый", used: 2, limit: 2);

        text.Should().Contain("Базовый").And.Contain("2").And.Contain("тариф с большим лимитом компаний");
        text.Should().NotContain("Дополнительная компания");
    }
}
