using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>Billing texts. The cycle-7 funding-text tests (paid-numbers count wording) are gone with the count itself: the
/// funding text now lives in <c>MessengerTexts.FundingText</c> (ARCHITECTURE_CYCLE40.md §40.3.6), tested by <c>MessengerTextsTests</c>.</summary>
public class BillingTextsTests
{
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
