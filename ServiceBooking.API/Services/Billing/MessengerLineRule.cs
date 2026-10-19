using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

public enum MessengerLineStatus { NotConnected, Trial, Paid, Ending, Expired, PendingPayment }

// RequestedAtUtc: request of the first live number of the transport (null — none).
public sealed record MessengerLineFacts(
    NotificationTransport Transport, OptionRowFacts? Row, DateTime? AccountTrialEndsAtUtc, LegacySubscriptionFacts Subscription,
    decimal? PricePerMonth, DateTime? RequestedAtUtc, DateTime NowUtc);

public sealed record MessengerLine(
    MessengerLineStatus Status, string StatusText, decimal MonthlyPrice, bool CountsInTotal, DateTime? PaidUntil, bool IsTrial,
    DateTime? EndsAt, TransportPayment Payment);

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.4 — the ONE rule of the status and the monthly amount of one messenger of one account, on top of the unchanged
/// <see cref="ChannelOptionFunding"/>. Called by the account list, the account card, the profile and the owner's «Подписка».
/// Pure. Vectors: <c>line</c> in contracts/cycle43/messenger-vectors.json.
/// </summary>
public static class MessengerLineRule
{
    public static MessengerLine Evaluate(MessengerLineFacts f)
    {
        var payment = ChannelOptionFunding.Evaluate(f.Row, f.AccountTrialEndsAtUtc, f.Subscription, f.NowUtc);
        var (monthly, counts) = Amount(payment, f.Row, f.PricePerMonth);
        var endsAt = f.Row?.EndsAtUtc;
        var requestNewer = f.RequestedAtUtc is { } requested && (payment.LastPaymentAt is not { } last || requested > last);

        MessengerLine Make(MessengerLineStatus status, string text, decimal price, bool counted) =>
            new(status, text, price, counted, payment.PaidUntil, payment.IsTrial, endsAt, payment);

        if (payment.Paid && endsAt is { } ending)
            return Make(MessengerLineStatus.Ending, MessengerTexts.StatusEnding(ending), monthly, counts);
        if (payment.Paid && payment.IsTrial)
            return Make(MessengerLineStatus.Trial, MessengerTexts.StatusTrial(f.Row?.PaidUntilUtc), monthly, counts);
        if (payment.Paid)
            return Make(MessengerLineStatus.Paid, MessengerTexts.StatusPaid(payment.PaidUntil), monthly, counts);
        if (requestNewer)
            return Make(MessengerLineStatus.PendingPayment, MessengerTexts.StatusPendingPayment, 0, false);
        if (f.Row is null)
            return Make(MessengerLineStatus.NotConnected, MessengerTexts.StatusNotConnected, 0, false);
        return Make(MessengerLineStatus.Expired, MessengerTexts.StatusExpired(payment.IsTrial, payment.PaidUntil), 0, false);
    }

    public static OptionRowFacts FactsOf(Core.Entities.AccountSubscriptionOption row) =>
        new(row.EndsAtUtc, row.PaidUntilUtc, row.GrantedByTrial, row.ActivatedAtUtc, row.Quantity);

    /// <summary>Monthly amount of a line and whether it takes part in the total: a paid line costs the catalog price (no price counts as 0),
    /// a trial line is free but counted, everything else is 0 and not counted.</summary>
    public static (decimal Monthly, bool Counts) Amount(TransportPayment payment, OptionRowFacts? row, decimal? price)
    {
        if (!payment.Paid) return (0m, false);
        return (payment.IsTrial ? 0m : price ?? 0m, true);
    }
}
