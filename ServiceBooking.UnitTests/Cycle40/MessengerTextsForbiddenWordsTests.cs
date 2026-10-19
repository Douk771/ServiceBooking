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
            // Cycle 43 (ARCHITECTURE_CYCLE43.md §43.10): outputs of the new text methods.
            var date = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
            yield return MessengerTexts.CatalogMissing(t);
            yield return MessengerTexts.PriceChangedByOtherAdmin(t);
            yield return MessengerTexts.PriceChangedConflict(t, 490.5m);
            yield return MessengerTexts.ConfirmTitle(t);
            yield return MessengerTexts.ConsequenceStartsSelling(t);
            yield return MessengerTexts.ConsequenceStopsSelling(t);
            yield return MessengerTexts.ConsequencePaidAccounts(t, 0);
            yield return MessengerTexts.ConsequencePaidAccounts(t, 21);
            yield return MessengerTexts.ConsequenceZeroPrice(t);
            yield return MessengerTexts.AmountText(3, 490.5m);
            yield return MessengerTexts.NewPaidUntilText(date);
            yield return MessengerTexts.ExtendActionText(true, 3);
            yield return MessengerTexts.ExtendActionText(false, 3);
            yield return MessengerTexts.StatusEnding(date);
            yield return MessengerTexts.StatusTrial(null);
            yield return MessengerTexts.StatusTrial(date);
            yield return MessengerTexts.StatusPaid(null);
            yield return MessengerTexts.StatusExpired(true, date);
            yield return MessengerTexts.StatusExpired(false, null);
            yield return MessengerTexts.NumberBoundText("+7 9** ***-**-12");
            yield return MessengerTexts.PriceLineText(null);
            yield return MessengerTexts.OwnerTotalText(490m);
            yield return MessengerTexts.DisableConfirmText(t, date);
            yield return MessengerTexts.NothingToDisableNotConnected(t);
            yield return MessengerTexts.NothingToDisableExpired(t);
            yield return MessengerTexts.DisableTrialWithoutDate(t);
            yield return MessengerTexts.DisablePaymentWithoutDate(t);
            yield return MessengerTexts.DisableAlreadyScheduled(t, date);
            yield return MessengerTexts.OptionsSummary(t, true, date);
            yield return MessengerTexts.OptionsSummary(t, false, null);
            yield return MessengerTexts.ExtendHistoryComment(t, true, 3, 490m, "по счёту");
            yield return MessengerTexts.DisableHistoryComment(t, date, "по заявлению");
            foreach (var reason in Enum.GetValues<API.Services.Notifications.Funding.MessengerSaleReason>())
            {
                yield return MessengerTexts.NotSellableConflict(t, reason);
                yield return MessengerTexts.ConsequenceNotSellable(t, reason);
                yield return MessengerTexts.SaleText(reason);
            }
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
