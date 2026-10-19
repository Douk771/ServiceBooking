using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.6.6, API_CONTRACT_CYCLE43.md §43.28 — the «Мессенджеры» block of the admin account card: always exactly two
/// lines (WhatsApp, MAX), built from the one messaging state of the account and the one line rule. Pure.
/// </summary>
public static class AdminMessengerLinesBuilder
{
    public static IReadOnlyList<AdminMessengerLineDto> Build(AccountMessagingState state, DateTime nowUtc) =>
        MessengerOptionsReader.Transports.Select(t => BuildLine(state.For(t), nowUtc)).ToList();

    public static decimal Total(AccountMessagingState state) =>
        MessengerOptionsReader.Transports.Select(state.For).Where(t => t.Line.CountsInTotal).Sum(t => t.Line.MonthlyPrice);

    public static AdminMessengerLineDto BuildLine(TransportState transport, DateTime nowUtc)
    {
        var t = transport.Transport;
        var line = transport.Line;
        var option = transport.Option;
        var sale = ChannelOptionAvailability.Explain(option.Open, option.IsActive, option.PricePerMonth, option.TermsOwnerPublished);
        var canDisable = line.Status is MessengerLineStatus.Paid or MessengerLineStatus.Trial or MessengerLineStatus.Ending;

        var periodEnd = MessengerChargeRules.Disable(
            t, line.Status, transport.Row is not null, line.Payment.PaidUntil, IsTrialWithoutOwnDate(transport), line.EndsAt,
            MessengerDisableWhen.PeriodEnd, nowUtc);

        return new AdminMessengerLineDto(
            t, MessengerTexts.DisplayName(t), option.OptionId, line.Status, line.StatusText, option.PricePerMonth,
            MessengerTexts.PriceLineText(option.PricePerMonth), line.MonthlyPrice, line.CountsInTotal, line.PaidUntil, line.IsTrial, line.EndsAt,
            NumberOf(transport), MessengerSaleStateDto.From(sale), sale.Sellable,
            MessengerChargeRules.IsRenewal(line.Status) ? MessengerTexts.RenewAction : MessengerTexts.ConnectAction,
            sale.Sellable ? null : sale.Text, canDisable,
            canDisable ? MessengerTexts.DisableConfirmText(t, nowUtc) : null,
            canDisable && periodEnd.NewEndsAtUtc is { } until ? MessengerTexts.DisableConfirmText(t, until) : null);
    }

    /// <summary>A trial row that has no date of its own lives by the trial of the account: «by the end of the period» has no date to point at.</summary>
    public static bool IsTrialWithoutOwnDate(TransportState transport) =>
        transport.Line.IsTrial && transport.Row is { PaidUntilUtc: null };

    public static MessengerNumberDto NumberOf(TransportState transport)
    {
        if (transport.Primary is not { } channel) return new MessengerNumberDto(MessengerNumberState.None, MessengerTexts.NumberNone, null, null);
        if (channel.State is ChannelState.NotConnected or ChannelState.Connecting)
            return new MessengerNumberDto(MessengerNumberState.AwaitingQr, MessengerTexts.NumberAwaitingQr, null, channel.Id);
        var masked = string.IsNullOrEmpty(channel.PhoneNumber) ? null : PhoneDisplayMask.Mask(channel.PhoneNumber);
        return new MessengerNumberDto(MessengerNumberState.Bound, MessengerTexts.NumberBoundText(masked), masked, channel.Id);
    }
}
