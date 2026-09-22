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

    [Fact]
    public void SeatLimitReached_MentionsUsedAndLimit()
    {
        var text = BillingTexts.SeatLimitReached(used: 8, planName: "Базовый", planIncluded: 5, purchased: 3, bonus: 0);

        text.Should().Contain("8").And.Contain("Лимит общий на все ваши точки.");
    }

    [Fact]
    public void SeatLimitReached_BreaksDownPlanIncludedVsPurchased_PerSample53_4()
    {
        // §53.4's own sample text: "Занято 8 из 8 мест: 5 включено в тариф «Базовый», 3 докуплено."
        var text = BillingTexts.SeatLimitReached(used: 8, planName: "Базовый", planIncluded: 5, purchased: 3, bonus: 0);

        text.Should().Contain("8 из 8 мест");
        text.Should().Contain("5 включено в тариф «Базовый»");
        text.Should().Contain("3 докуплено");
        text.Should().Contain("подключите опцию «Дополнительные сотрудники»");
    }

    [Fact]
    public void SeatLimitReached_NoPurchasedOptions_OmitsTheDokuplenoClause()
    {
        var text = BillingTexts.SeatLimitReached(used: 5, planName: "Базовый", planIncluded: 5, purchased: 0, bonus: 0);

        text.Should().NotContain("докуплено");
        text.Should().Contain("5 включено в тариф «Базовый»");
    }

    [Fact]
    public void SeatLimitReached_GrandfatheredBonus_FoldedIntoIncludedFigure_NeverNamedByItself()
    {
        // The bonus is an internal migration artifact (§54.4) never surfaced to the owner by name —
        // it silently widens "included in the plan" instead of appearing as its own line item.
        var text = BillingTexts.SeatLimitReached(used: 6, planName: "Базовый", planIncluded: 5, purchased: 0, bonus: 1);

        text.Should().Contain("6 включено в тариф «Базовый»");
        text.Should().NotContain("бонус");
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
        var text = BillingTexts.TransferRejectedCompanyLimit("Базовый", used: 2, limit: 2);

        text.Should().Contain("Базовый").And.Contain("2").And.Contain("Дополнительная компания");
    }
}
