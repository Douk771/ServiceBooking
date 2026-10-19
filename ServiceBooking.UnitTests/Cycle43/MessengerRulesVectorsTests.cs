using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Enums;
using ServiceBooking.UnitTests.Cycle40;

namespace ServiceBooking.UnitTests.Cycle43;

/// <summary>ARCHITECTURE_CYCLE43.md §43.10 — every pure cycle-43 rule against contracts/cycle43/messenger-vectors.json.</summary>
public class MessengerRulesVectorsTests
{
    public static IEnumerable<object[]> Line() => MessengerVectors.Indexes("line");
    public static IEnumerable<object[]> Sale() => MessengerVectors.Indexes("sale");
    public static IEnumerable<object[]> PriceValidation() => MessengerVectors.Indexes("priceValidation");
    public static IEnumerable<object[]> Consequences() => MessengerVectors.Indexes("consequences");
    public static IEnumerable<object[]> Quote() => MessengerVectors.Indexes("quote");
    public static IEnumerable<object[]> Disable() => MessengerVectors.Indexes("disable");
    public static IEnumerable<object[]> DisableConfirmText() => MessengerVectors.Indexes("disableConfirmText");
    public static IEnumerable<object[]> OwnerAction() => MessengerVectors.Indexes("ownerAction");

    [Theory, MemberData(nameof(Line))]
    public void MessengerLineRule_MatchesVector(int index)
    {
        var name = MessengerVectors.Name("line", index);
        var i = MessengerVectors.Input("line", index);
        var e = MessengerVectors.Expect("line", index);
        var r = i.GetProperty("row");
        var s = i.GetProperty("subscription");
        OptionRowFacts? row = r.ValueKind == System.Text.Json.JsonValueKind.Null
            ? null
            : new OptionRowFacts(r.Date("endsAtUtc"), r.Date("paidUntilUtc"), r.Bool("grantedByTrial"), r.Date("activatedAtUtc"), r.GetProperty("quantity").GetInt32());

        var line = MessengerLineRule.Evaluate(new MessengerLineFacts(
            i.Enum<NotificationTransport>("transport"), row, i.Date("accountTrialEndsAtUtc"),
            new LegacySubscriptionFacts(s.Bool("usable"), s.Date("paidUntil")), i.NullableDecimal("pricePerMonth"), i.Date("requestedAtUtc"),
            MessengerVectors.Now));

        line.Status.Should().Be(e.Enum<MessengerLineStatus>("status"), name);
        line.StatusText.Should().Be(e.Str("statusText"), name);
        line.MonthlyPrice.Should().Be(e.GetProperty("monthlyPrice").GetDecimal(), name);
        line.CountsInTotal.Should().Be(e.Bool("countsInTotal"), name);
        line.PaidUntil.Should().Be(e.Date("paidUntil"), name);
        line.IsTrial.Should().Be(e.Bool("isTrial"), name);
    }

    [Theory, MemberData(nameof(Sale))]
    public void Explain_MatchesVector_AndAgreesWithSellable(int index)
    {
        var i = MessengerVectors.Input("sale", index);
        var e = MessengerVectors.Expect("sale", index);

        var result = ChannelOptionAvailability.Explain(i.Bool("open"), i.Bool("isActive"), i.NullableDecimal("pricePerMonth"), i.Bool("termsOwnerPublished"));

        result.Sellable.Should().Be(e.Bool("sellable"));
        result.Reason.Should().Be(e.NullableEnum<MessengerSaleReason>("reason"));
        result.Text.Should().Be(e.Str("text"));
        result.Sellable.Should().Be(ChannelOptionAvailability.Sellable(
            i.Bool("open"), i.Bool("isActive"), i.NullableDecimal("pricePerMonth"), i.Bool("termsOwnerPublished")));
    }

    [Theory, MemberData(nameof(PriceValidation))]
    public void ValidatePrice_MatchesVector(int index)
    {
        var price = MessengerVectors.Input("priceValidation", index).NullableDecimal("pricePerMonth");
        MessengerPriceChangeRules.ValidatePrice(price).Should().Be(MessengerVectors.Expect("priceValidation", index).Str("error"));
    }

    [Theory, MemberData(nameof(Consequences))]
    public void Consequences_MatchesVector(int index)
    {
        var name = MessengerVectors.Name("consequences", index);
        var i = MessengerVectors.Input("consequences", index);
        var e = MessengerVectors.Expect("consequences", index);
        var before = i.GetProperty("before");
        var after = i.GetProperty("after");

        var result = MessengerPriceChangeRules.Consequences(
            i.Enum<NotificationTransport>("transport"),
            new MessengerPriceState(before.NullableDecimal("pricePerMonth"), before.Bool("isActive")),
            new MessengerPriceState(after.NullableDecimal("pricePerMonth"), after.Bool("isActive")),
            i.Bool("open"), i.Bool("termsOwnerPublished"), i.GetProperty("activePaidAccounts").GetInt32());

        result.SellableBefore.Should().Be(e.Bool("sellableBefore"), name);
        result.SellableAfter.Should().Be(e.Bool("sellableAfter"), name);
        result.StartsSelling.Should().Be(e.Bool("startsSelling"), name);
        result.StopsSelling.Should().Be(e.Bool("stopsSelling"), name);
        result.IsZeroPrice.Should().Be(e.Bool("isZeroPrice"), name);
        result.Texts.Should().Equal(e.GetProperty("texts").EnumerateArray().Select(t => t.GetString()!), name);
    }

    [Theory, MemberData(nameof(Quote))]
    public void Quote_MatchesVector(int index)
    {
        var name = MessengerVectors.Name("quote", index);
        var i = MessengerVectors.Input("quote", index);
        var e = MessengerVectors.Expect("quote", index);

        var quote = MessengerChargeRules.Quote(
            i.Enum<NotificationTransport>("transport"), i.Enum<MessengerLineStatus>("status"), i.Date("currentPaidUntil"),
            i.GetProperty("months").GetInt32(), i.GetProperty("pricePerMonth").GetDecimal(), MessengerVectors.Now);

        quote.Amount.Should().Be(e.GetProperty("amount").GetDecimal(), name);
        quote.AmountText.Should().Be(e.Str("amountText"), name);
        quote.NewPaidUntil.Should().Be(e.Date("newPaidUntil"), name);
        quote.NewPaidUntilText.Should().Be(e.Str("newPaidUntilText"), name);
        quote.ActionText.Should().Be(e.Str("actionText"), name);
    }

    [Theory, MemberData(nameof(Disable))]
    public void Disable_MatchesVector(int index)
    {
        var name = MessengerVectors.Name("disable", index);
        var i = MessengerVectors.Input("disable", index);
        var e = MessengerVectors.Expect("disable", index);
        var status = i.Enum<MessengerLineStatus>("status");

        var decision = MessengerChargeRules.Disable(
            i.Enum<NotificationTransport>("transport"), status, status != MessengerLineStatus.NotConnected, i.Date("paymentPaidUntil"),
            i.Bool("isTrialWithoutOwnDate"), i.Date("endsAtUtc"), i.Enum<MessengerDisableWhen>("when"), MessengerVectors.Now);

        decision.Error.Should().Be(e.Str("error"), name);
        decision.NewEndsAtUtc.Should().Be(e.Date("newEndsAtUtc"), name);
    }

    [Theory, MemberData(nameof(DisableConfirmText))]
    public void DisableConfirmText_MatchesVector(int index)
    {
        var i = MessengerVectors.Input("disableConfirmText", index);
        MessengerTexts.DisableConfirmText(i.Enum<NotificationTransport>("transport"), i.Date("date")!.Value)
            .Should().Be(MessengerVectors.Expect("disableConfirmText", index).GetString());
    }

    [Theory, MemberData(nameof(OwnerAction))]
    public void OwnerAction_MatchesVector(int index)
    {
        var name = MessengerVectors.Name("ownerAction", index);
        var i = MessengerVectors.Input("ownerAction", index);
        var e = MessengerVectors.Expect("ownerAction", index);

        var d = MessengerChargeRules.OwnerAction(
            i.Enum<MessengerLineStatus>("status"), i.Bool("isTrial"), i.Bool("sellable"), i.Bool("platformEnabled"), i.Bool("hasLineCompany"));

        d.Action?.ToString().Should().Be(e.Str("action"), name);
        (d.Action is null).Should().Be(e.Str("action") is null, name);
        d.ActionText.Should().Be(e.Str("actionText"), name);
        d.NoCompanyText.Should().Be(e.Str("noCompanyText"), name);
    }
}
