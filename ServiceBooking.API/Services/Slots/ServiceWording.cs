using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Slots;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.4.4 — the words of a vertical in the strings of the slot engine («заказ» at «Дома», «бронь» at «Бани»).
/// <see cref="Stays"/> returns the strings of cycles 39–41 byte for byte (they are produced by the legacy <see cref="ServiceTexts"/> members);
/// <see cref="Baths"/> speaks of a «бронь», signs the push «EZBOOK Бани» and marks the guest's time as local.
/// </summary>
public sealed record ServiceWording(
    CompanyKind Kind,
    string GuestPushTitle,
    string RefundTerminal,
    string OrderAlreadyCancelled,
    string ServiceOrderDone,
    string TooManyHeldOrders,
    string TooManyOrdersPerDay,
    Func<string?, string> HoldExpired,
    Func<string, string> ProofNotAllowed,
    Func<string, string> StatusText,
    Func<StayServiceOrderEventKind, string> StaffEventText,
    Func<StayBookingStatus, string?, string?, bool, string> OutcomeText,
    Func<string?, string> GuestTimeSuffix)
{
    public static readonly ServiceWording Stays = new(
        CompanyKind.Stays,
        StayNotificationTexts.GuestPushTitle,
        ServiceTexts.RefundTerminal,
        ServiceTexts.OrderAlreadyCancelled,
        ServiceTexts.ServiceOrderDone,
        ServiceTexts.TooManyHeldOrders,
        ServiceTexts.TooManyOrdersPerDay,
        phone => ServiceTexts.HoldExpired(phone),
        status => ServiceTexts.ProofNotAllowed(status),
        display => ServiceTexts.StatusText(display),
        kind => ServiceTexts.StaffEventText(kind),
        (status, reason, phone, prepaid) => ServiceTexts.OutcomeText(status, reason, phone, prepaid),
        _ => string.Empty);

    public static readonly ServiceWording Baths = new(
        CompanyKind.Baths,
        "EZBOOK Бани",
        "Бронь уже завершена — отменять нечего",
        "Бронь уже отменена",
        "Бронь завершена — уведомления не нужны",
        "Слишком много неоплаченных броней. Оплатите или отмените текущую бронь",
        "Слишком много броней с этого номера. Попробуйте позже",
        phone => "Время на оплату истекло, бронь снята. Если вы уже оплатили — свяжитесь с компанией" + (string.IsNullOrWhiteSpace(phone) ? "." : $": {phone}"),
        status => $"Бронь уже {status.ToLowerInvariant()} — подтверждение оплаты не нужно",
        BathsStatusText,
        BathsStaffEventText,
        BathsOutcomeText,
        city => string.IsNullOrWhiteSpace(city) ? " (время местное)" : $" (время местное, {city.Trim()})");

    /// <summary>The wording of a company's vertical; kinds that are not slot verticals fall back to «Дома» (their legacy strings).</summary>
    public static ServiceWording For(CompanyKind kind) => kind switch { CompanyKind.Baths => Baths, _ => Stays };

    private static string BathsStatusText(string displayStatus) => displayStatus switch
    {
        "Held" => "Ожидает оплаты",
        "AwaitingPaymentCheck" => "Ожидает проверки оплаты",
        "Confirmed" => "Подтверждена",
        "Completed" => "Завершена",
        "ExpiredUnpaid" => "Снята: не оплачена",
        "PaymentRejected" => "Оплата не подтверждена",
        "CancelledByGuest" => "Отменена гостем",
        "CancelledByOwner" => "Отменена компанией",
        _ => displayStatus
    };

    private static string BathsStaffEventText(StayServiceOrderEventKind kind) => kind switch
    {
        StayServiceOrderEventKind.Created => "Бронь создана",
        StayServiceOrderEventKind.PaymentProofUploaded => "Приложено подтверждение оплаты",
        StayServiceOrderEventKind.PaymentProofViewed => "Просмотрено подтверждение оплаты",
        StayServiceOrderEventKind.PaymentConfirmed => "Оплата подтверждена",
        StayServiceOrderEventKind.HoldExpired => "Время на оплату истекло",
        StayServiceOrderEventKind.PaymentRejected => "Оплата отклонена",
        StayServiceOrderEventKind.CancelledByGuest => "Гость отменил бронь",
        StayServiceOrderEventKind.CancelledByOwner => "Компания отменила бронь",
        StayServiceOrderEventKind.PaymentProofsPurged => "Подтверждения оплаты удалены по сроку хранения",
        _ => "Персональные данные удалены"
    };

    private static string BathsOutcomeText(StayBookingStatus status, string? reason, string? phone, bool prepaid)
    {
        var contact = string.IsNullOrWhiteSpace(phone) ? "" : $" Контакт компании: {phone}.";
        var why = string.IsNullOrWhiteSpace(reason) ? "" : $" Причина: {reason}.";
        return status switch
        {
            StayBookingStatus.PaymentRejected => $"Компания не подтвердила оплату.{why} Если вы платили, компания обязана вернуть деньги или восстановить бронь, если время свободно.{contact}",
            StayBookingStatus.CancelledByOwner when prepaid =>
                $"Компания отменила бронь.{why} Внесённая предоплата должна быть возвращена полностью. Вы также вправе требовать возмещения убытков.{contact}",
            StayBookingStatus.CancelledByOwner => $"Компания отменила бронь.{why} Оплата за сеанс не вносилась.{contact}",
            StayBookingStatus.ExpiredUnpaid => $"Время на оплату истекло, бронь снята. Если вы успели оплатить — свяжитесь с компанией.{contact}",
            StayBookingStatus.CancelledByGuest => "Вы отменили бронь.",
            _ => ""
        };
    }
}
