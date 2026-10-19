using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

public enum MessengerDisableWhen { Now, PeriodEnd }

public enum OwnerMessengerAction { Connect, Renew }

public sealed record MessengerQuoteCalc(
    decimal Amount, string AmountText, DateTime NewPaidUntil, string NewPaidUntilText, string ActionText);

public sealed record MessengerDisableDecision(string? Error, DateTime? NewEndsAtUtc);

public sealed record OwnerActionDecision(OwnerMessengerAction? Action, string? ActionText, string? NoCompanyText);

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.6, §43.8.2 — the pure parts of connecting, extending and disabling a messenger of an account and of the
/// owner's button. Vectors: <c>quote</c>, <c>disable</c>, <c>disableConfirmText</c>, <c>ownerAction</c> in contracts/cycle43/messenger-vectors.json.
/// </summary>
public static class MessengerChargeRules
{
    public const int MinMonths = 1;
    public const int MaxMonths = 24;

    public static bool IsRenewal(MessengerLineStatus status) => status is MessengerLineStatus.Paid or MessengerLineStatus.Ending;

    /// <summary>The new end of the paid period: <c>max(now, currentPaidUntil ?? now) + months</c>. A switched-off or expired remainder is never returned.</summary>
    public static DateTime NewPaidUntil(DateTime? currentPaidUntil, int months, DateTime nowUtc)
    {
        var baseDate = currentPaidUntil is { } until && until > nowUtc ? until : nowUtc;
        return baseDate.AddMonths(months);
    }

    public static MessengerQuoteCalc Quote(
        NotificationTransport transport, MessengerLineStatus status, DateTime? currentPaidUntil, int months, decimal pricePerMonth, DateTime nowUtc)
    {
        _ = transport;
        var newUntil = NewPaidUntil(currentPaidUntil, months, nowUtc);
        return new MessengerQuoteCalc(
            months * pricePerMonth, MessengerTexts.AmountText(months, pricePerMonth), newUntil, MessengerTexts.NewPaidUntilText(newUntil),
            MessengerTexts.ExtendActionText(IsRenewal(status), months));
    }

    /// <summary>§43.27 — may the line be switched off and from when. <paramref name="hasRow"/> tells "never connected" from "payment ended".</summary>
    public static MessengerDisableDecision Disable(
        NotificationTransport transport, MessengerLineStatus status, bool hasRow, DateTime? paymentPaidUntil, bool isTrialWithoutOwnDate,
        DateTime? endsAtUtc, MessengerDisableWhen when, DateTime nowUtc)
    {
        if (status is not (MessengerLineStatus.Paid or MessengerLineStatus.Trial or MessengerLineStatus.Ending))
            return new(hasRow ? MessengerTexts.NothingToDisableExpired(transport) : MessengerTexts.NothingToDisableNotConnected(transport), null);
        if (when == MessengerDisableWhen.Now) return new(null, nowUtc);

        if (isTrialWithoutOwnDate) return new(MessengerTexts.DisableTrialWithoutDate(transport), null);
        if (paymentPaidUntil is not { } until) return new(MessengerTexts.DisablePaymentWithoutDate(transport), null);
        if (endsAtUtc is { } scheduled) return new(MessengerTexts.DisableAlreadyScheduled(transport, scheduled), null);
        return new(null, until);
    }

    /// <summary>§43.8.2 — the owner's button. Renewal of a still paid transport is not offered (the wizard refuses such a request).</summary>
    public static OwnerActionDecision OwnerAction(MessengerLineStatus status, bool isTrial, bool sellable, bool platformEnabled, bool hasLineCompany)
    {
        if (!platformEnabled || !sellable) return new(null, null, null);
        OwnerMessengerAction? action = status switch
        {
            MessengerLineStatus.NotConnected => OwnerMessengerAction.Connect,
            MessengerLineStatus.Expired => isTrial ? OwnerMessengerAction.Connect : OwnerMessengerAction.Renew,
            _ => null,
        };
        if (action is null) return new(null, null, null);
        return new(action, action == OwnerMessengerAction.Connect ? MessengerTexts.ConnectAction : MessengerTexts.RenewAction,
            hasLineCompany ? null : MessengerTexts.NoCompanyText);
    }
}
