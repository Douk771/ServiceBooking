using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.8.1, API_CONTRACT_CYCLE43.md §43.31 — the messenger block of the owner's «Подписка», built from the one messaging
/// state of the account and the one line rule (the admin card shows the same status texts). Pure.
/// </summary>
public static class OwnerMessengersBlockBuilder
{
    /// <summary>A line is shown when the messenger is sellable, or the account has a row of the option, or a live number of the transport.</summary>
    public static bool IsShown(TransportState transport) => transport.Option.Sellable || transport.Row is not null || transport.Primary is not null;

    public static OwnerMessengersBlockDto? Build(AccountMessagingState state, Guid? targetCompanyId)
    {
        var lines = new List<OwnerMessengerLineDto>();
        foreach (var transport in MessengerOptionsReader.Transports.Select(state.For))
        {
            if (!IsShown(transport)) continue;
            var line = transport.Line;
            var option = transport.Option;
            var showPrice = option.Sellable || line.Payment.Paid;
            var action = MessengerChargeRules.OwnerAction(line.Status, line.IsTrial, option.Sellable, state.PlatformEnabled, targetCompanyId is not null);
            lines.Add(new OwnerMessengerLineDto(
                transport.Transport, MessengerTexts.DisplayName(transport.Transport), line.Status, line.StatusText,
                showPrice ? option.PricePerMonth : null, showPrice && option.PricePerMonth is { } price ? MessengerTexts.PriceText(price) : null,
                line.MonthlyPrice, line.PaidUntil, line.IsTrial,
                transport.Transport == Core.Enums.NotificationTransport.WhatsApp ? MessengerTexts.WhatsAppFootnote : null,
                action.Action, action.ActionText, targetCompanyId, action.NoCompanyText));
        }
        if (lines.Count == 0) return null;

        var total = lines.Sum(l => l.MonthlyPrice);
        return new OwnerMessengersBlockDto(
            MessengerTexts.OwnerBlockTitle, MessengerTexts.OwnerBlockNote, lines, total, MessengerTexts.OwnerTotalText(total),
            MessengerTexts.ConditionsUrl, MessengerTexts.ConditionsLabel);
    }
}
