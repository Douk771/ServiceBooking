using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.16 / API_CONTRACT_CYCLE40.md §40.39 — every pure cycle-40 rule against the shared
/// vectors contracts/cycle40/channel-vectors.json (the same file the frontend runs).</summary>
public class ChannelVectorsTests
{
    public static IEnumerable<object[]> Availability() => ChannelVectors.Cases("availability");
    public static IEnumerable<object[]> OptionFunding() => ChannelVectors.Cases("optionFunding");
    public static IEnumerable<object[]> Routing() => ChannelVectors.Cases("routing");
    public static IEnumerable<object[]> Consent() => ChannelVectors.Cases("consent");
    public static IEnumerable<object[]> Display() => ChannelVectors.Cases("display");
    public static IEnumerable<object[]> Wizard() => ChannelVectors.Cases("wizardStep");
    public static IEnumerable<object[]> Offer() => ChannelVectors.Cases("offer");
    public static IEnumerable<object[]> Addons() => ChannelVectors.Cases("addons");

    [Theory, MemberData(nameof(Availability))]
    public void ChannelOptionAvailability_MatchesVector(int index, string name)
    {
        var i = ChannelVectors.Input("availability", index);
        var e = ChannelVectors.Expect("availability", index);

        var result = ChannelOptionAvailability.Evaluate(
            i.Str("settingValue"), i.Bool("configDefault"), i.Bool("isActive"), i.NullableDecimal("pricePerMonth"), i.Bool("termsOwnerPublished"));

        result.Open.Should().Be(e.Bool("open"), name);
        result.Sellable.Should().Be(e.Bool("sellable"), name);
    }

    [Theory, MemberData(nameof(OptionFunding))]
    public void ChannelOptionFunding_MatchesVector(int index, string name)
    {
        var i = ChannelVectors.Input("optionFunding", index);
        var e = ChannelVectors.Expect("optionFunding", index);
        var r = i.GetProperty("row");
        var s = i.GetProperty("subscription");

        var row = r.Bool("exists")
            ? new OptionRowFacts(r.Date("endsAtUtc"), r.Date("paidUntilUtc"), r.Bool("grantedByTrial"), r.Date("activatedAtUtc"))
            : null;
        var result = ChannelOptionFunding.Evaluate(row, i.Date("trialEndsAtUtc"), new LegacySubscriptionFacts(s.Bool("usable"), s.Date("paidUntil")), ChannelVectors.Now);

        result.Paid.Should().Be(e.Bool("paid"), name);
        result.PaidUntil.Should().Be(e.Date("paidUntil"), name);
        result.IsTrial.Should().Be(e.Bool("isTrial"), name);
    }

    [Theory, MemberData(nameof(Routing))]
    public void NotificationRouting_MatchesVector(int index, string name)
    {
        var i = ChannelVectors.Input("routing", index);
        var candidates = i.GetProperty("candidates").EnumerateArray().Select(c => new NotificationRouting.TransportCandidate(
            c.Enum<NotificationTransport>("transport"), c.Bool("paid"), c.Bool("funded"), c.Bool("suspended"), c.Enum<ChannelState>("state"))).ToList();

        var targets = NotificationRouting.SelectRoutedTargets(i.Enum<NotificationDeliveryMode>("mode"), i.Enum<NotificationTransport>("priority"), candidates);

        targets.Should().Equal(ChannelVectors.Expect("routing", index).Transports("targets"), name);
    }

    [Theory, MemberData(nameof(Consent))]
    public void MessengerConsent_MatchesVector(int index, string name)
    {
        var i = ChannelVectors.Input("consent", index);
        var e = ChannelVectors.Expect("consent", index);
        var origin = i.Enum<MessengerConsentOrigin>("source");
        var notify = i.NullableBool("notifyByMessenger");
        var hasConsent = i.Bool("accountHasProviderDeliveryConsent");

        var stored = MessengerConsentRule.Store(origin, notify);
        var decision = MessengerConsentRule.Evaluate(stored.NotifyByMessenger, i.Bool("recipientIsAccount"), hasConsent);
        // The unsubscribe is the gate's business, not the rule's (customer decision): run the gate to get the skip reason.
        var gate = NotificationGate.Evaluate(
            NotificationType.BookingConfirmed, new MessagingAvailability(true, true, true), null,
            i.Bool("optedOut"), decision, ChannelVectors.Now, ChannelVectors.Now.AddDays(1));

        stored.NotifyByMessenger.Should().Be(e.GetProperty("stored").NullableBool("notifyByMessenger"), name);
        stored.ConsentByStaff.Should().Be(e.GetProperty("stored").Bool("consentByStaff"), name);
        MessengerConsentRule.WritesConsentLedger(origin, notify, hasConsent).Should().Be(e.Bool("writesConsentLedger"), name);
        gate.Reason?.ToString().Should().Be(e.Str("skipReason"), name);
        if (e.Str("decision") is { } expectedDecision)
            decision.ToString().Should().Be(expectedDecision, name);
        else
            i.Bool("optedOut").Should().BeTrue("a vector without a rule decision is an unsubscribe case handled by the gate");
    }

    private static ChannelDisplayFacts Facts(System.Text.Json.JsonElement i) => new(
        i.Enum<NotificationTransport>("transport"), i.Enum<ChannelState>("state"), i.NullableEnum<ChannelStateReason>("lastStateReason"),
        i.Str("phoneMasked"), i.Bool("paid"), i.Date("paidUntil"), i.Bool("isTrial"), i.Bool("requestNewerThanPayment"),
        i.Bool("isDuplicate"), i.Bool("suspended"), i.Bool("platformEnabled"), i.Bool("optionOpen"), i.Bool("optionSellable"),
        i.Bool("termsAccepted"), i.Date("idleSinceUtc"), i.Date("idleDeadlineUtc"), i.NullableInt("idleDays"));

    [Theory, MemberData(nameof(Display))]
    public void ChannelPresentationDisplay_MatchesVector(int index, string name)
    {
        var e = ChannelVectors.Expect("display", index);

        var result = ChannelPresentation.Display(Facts(ChannelVectors.Input("display", index)));

        result.Status?.ToString().Should().Be(e.Str("displayStatus"), name);
        result.Text.Should().Be(e.Str("displayText"), name);
        result.Action?.ToString().Should().Be(e.Str("action"), name);
    }

    [Theory, MemberData(nameof(Wizard))]
    public void ChannelPresentationWizardStep_MatchesVector(int index, string name)
    {
        var i = ChannelVectors.Input("wizardStep", index);
        var e = ChannelVectors.Expect("wizardStep", index);
        var facts = Facts(i);

        ChannelPresentation.WizardStep(facts, i.Bool("hasChannel")).ToString().Should().Be(e.Str("wizardStep"), name);
        ChannelPresentation.CanRequestPayment(facts).Should().Be(e.Bool("canRequestPayment"), name);
    }

    [Theory, MemberData(nameof(Offer))]
    public void CustomerMessagingOfferRule_MatchesVector(int index, string name)
    {
        var i = ChannelVectors.Input("offer", index);
        var e = ChannelVectors.Expect("offer", index);
        var transports = i.GetProperty("transports").EnumerateObject().Select(p => new OfferTransportFacts(
            Enum.Parse<NotificationTransport>(p.Name), p.Value.Bool("routable"), p.Value.Bool("working"))).ToList();

        var result = CustomerMessagingOfferRule.Evaluate(new CustomerMessagingOfferInput(
            i.Bool("platformEnabled"), i.Enum<CompanyKind>("kind"), i.Bool("companyFlag"), i.Bool("showcase"), i.Bool("companyActive"),
            i.Enum<NotificationDeliveryMode>("mode"), i.Enum<NotificationTransport>("priority"), transports));

        result.Offered.Should().Be(e.Bool("offered"), name);
        result.Transports.Should().Equal(e.Transports("transports"), name);
        result.CheckboxLabel.Should().Be(e.Str("checkboxLabel"), name);
    }

    [Theory, MemberData(nameof(Addons))]
    public void MessengerAddonsBuilder_MatchesVector(int index, string name)
    {
        var i = ChannelVectors.Input("addons", index);
        var e = ChannelVectors.Expect("addons", index);
        var options = i.GetProperty("options").EnumerateArray().Select(o => new MessengerAddonOptionFacts(
            o.Str("code")!, o.Bool("open"), o.Bool("isActive"), o.NullableDecimal("pricePerMonth"), o.Bool("legallySellable"))).ToList();

        var result = MessengerAddonsBuilder.Build(options);

        result.NoteShown.Should().Be(e.Bool("noteShown"), name);
        var expected = e.GetProperty("messengerAddons").EnumerateArray().ToList();
        result.Addons.Should().HaveCount(expected.Count, name);
        for (var k = 0; k < expected.Count; k++)
        {
            result.Addons[k].Transport.ToString().Should().Be(expected[k].Str("transport"), name);
            result.Addons[k].Label.Should().Be(expected[k].Str("label"), name);
            result.Addons[k].PricePerMonth.Should().Be(expected[k].NullableDecimal("pricePerMonth"), name);
            result.Addons[k].Text.Should().Be(expected[k].Str("text"), name);
            result.Addons[k].Footnote.Should().Be(expected[k].Str("footnote"), name);
        }
    }
}
