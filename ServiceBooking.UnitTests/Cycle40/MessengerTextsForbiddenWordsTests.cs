using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>
/// Т40-L-14 (ARCHITECTURE_CYCLE40.md §40.16): no server text about messengers may mention means of circumventing blocks.
/// Reads every public constant of <see cref="MessengerTexts"/>, every text method over both transports, and every
/// <see cref="ChannelPresentation"/> output for every state.
/// </summary>
public class MessengerTextsForbiddenWordsTests
{
    private static readonly string[] Forbidden = ["vpn", "впн", "прокси", "proxy", "обход", "обойт", "обходить"];

    private static IEnumerable<string> AllTexts()
    {
        foreach (var f in typeof(MessengerTexts).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)))
            yield return (string)f.GetRawConstantValue()!;

        foreach (var t in Enum.GetValues<NotificationTransport>())
        {
            yield return MessengerTexts.TermsTitle(t);
            yield return MessengerTexts.TermsTrialHint(t);
            yield return MessengerTexts.Unavailable(t);
            yield return MessengerTexts.OptionClosedForAdmin(t);
            yield return MessengerTexts.AlreadyHaveNumber(t);
            yield return MessengerTexts.PayFirst(t);
            yield return MessengerTexts.TestMessageBody(t);
            yield return MessengerTexts.StaffConsentLabel([t]);
            yield return ChannelPresentation.TransportConnectionNotice(t) ?? "";
            foreach (var line in MessengerTexts.QrInstruction(t)) yield return line;
            foreach (var state in Enum.GetValues<ChannelFundingState>())
                yield return MessengerTexts.FundingText(state, t, new TransportPaymentView(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), true), "+7 *** ***-45-67");

            foreach (var state in Enum.GetValues<ChannelState>())
            foreach (var reason in new ChannelStateReason?[] { null, ChannelStateReason.SecretUnavailable, ChannelStateReason.ServerCountryMismatch })
            {
                yield return ChannelPresentation.StateText(t, state, "+7 *** ***-45-67", 21, reason);
                foreach (var paid in new[] { true, false })
                foreach (var idleSince in new DateTime?[] { null, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) })
                {
                    var display = ChannelPresentation.Display(new ChannelDisplayFacts(
                        t, state, reason, "+7 *** ***-45-67", paid, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), false, false, false, false,
                        true, true, true, true, idleSince, idleSince?.AddDays(7), 14));
                    yield return display.Text ?? "";
                }
            }
        }

        foreach (var reason in new[] { "ClientDeclinedMessenger", "PlatformMessagingDisabled", "ChannelAccountMismatch", "NoProviderDeliveryConsent", "NoUsableChannel", "NotOnPaidPlan" })
            yield return MessengerTexts.DeliveryReasonText(reason) ?? "";
    }

    [Fact]
    public void NoServerMessengerTextMentionsCircumventionTools()
    {
        var texts = AllTexts().ToList();
        texts.Should().HaveCountGreaterThan(50, "the scan must really cover the texts");

        foreach (var text in texts)
            foreach (var word in Forbidden)
                text.ToLowerInvariant().Should().NotContain(word, $"«{text}» must not mention «{word}»");
    }

    [Fact]
    public void ScanDetectsAForbiddenWord() =>
        "Включите VPN".ToLowerInvariant().Should().Contain(Forbidden[0], "guards against a vacuous scan");
}
