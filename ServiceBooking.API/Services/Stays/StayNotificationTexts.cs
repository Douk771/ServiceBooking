using ServiceBooking.API.Services.Orders.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>What the texts of a notification are made of — assembled once by the planner from the booking, never from the request.</summary>
public sealed record StayTextFacts(
    string CompanyName, string HouseName, DateOnly CheckIn, DateOnly CheckOut, int Nights, int PrepayRub, int DueRub, string? Address,
    string? CompanyPhone, string? Reason, string? PaymentDetails, string? PaymentPurpose, DateTime? HoldExpiresLocal, string CheckInTime,
    string? CheckInInfoFull);

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.12, API_CONTRACT_CYCLE37.md §37.33 — fixed texts of every notification of «Дома». Staff messages carry NO name or phone of the guest;
/// a guest's web-push carries no personal data, address or access code (Т37-08) — only "something changed"; the requisites go ONLY into the messenger message of
/// this very booking (Т37-04); a check-in text goes into a messenger only when the owner switched "send the full text" on (ЮР-4).
/// </summary>
public static class StayNotificationTexts
{
    private static readonly string[] Months = ["янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];

    public const string GuestPushTitle = "ezbook · Дома";

    public static string Short(DateOnly d) => $"{d.Day} {Months[d.Month - 1]}";

    public static string Range(DateOnly from, DateOnly to) => $"{Short(from)} – {Short(to)}";

    // ── staff ──

    public static PushPayload StaffPush(NotificationType type, StayTextFacts f, Guid bookingId, string cabinetUrl)
    {
        var tail = $"«{f.HouseName}» · {Range(f.CheckIn, f.CheckOut)}";
        var body = type switch
        {
            NotificationType.StaffStayCreated => $"Новая бронь · {tail} · {StaysTexts.NightsText(f.Nights)}",
            NotificationType.StaffStayPaymentProofUploaded => $"Приложено подтверждение оплаты · {tail}",
            _ => $"Гость отменил бронь · {tail}",
        };
        return new PushPayload(f.CompanyName, body, $"s-{bookingId}", cabinetUrl);
    }

    public static string StaffMax(NotificationType type, StayTextFacts f, Guid bookingId, string cabinetUrl)
    {
        var p = StaffPush(type, f, bookingId, cabinetUrl);
        return $"{p.Title} · {p.Body}\nОткрыть: {cabinetUrl}";
    }

    // ── guest web-push (no personal data at all) ──

    public static PushPayload GuestPush(NotificationType type, Guid bookingId, string token)
    {
        var body = type switch
        {
            NotificationType.StayGuestHoldExpiring => "Осталось 10 минут, чтобы приложить подтверждение оплаты",
            NotificationType.StayGuestCheckInInfo => "Информация к заселению готова",
            NotificationType.StayGuestArrivalReminder => "Завтра заезд — откройте бронь",
            _ => "Статус вашей брони изменился",
        };
        return new PushPayload(GuestPushTitle, body, $"sg-{bookingId}", $"/b/{token}");
    }

    // ── guest messenger ──

    public static string Messenger(NotificationType type, StayTextFacts f, string bookingUrl, string? unsubscribeUrl)
    {
        var dates = $"{Range(f.CheckIn, f.CheckOut)}, {StaysTexts.NightsText(f.Nights)}";
        var contact = string.IsNullOrWhiteSpace(f.CompanyPhone) ? string.Empty : $" Телефон компании: {f.CompanyPhone}.";
        var why = string.IsNullOrWhiteSpace(f.Reason) ? string.Empty : $" Причина: {f.Reason}.";
        var text = type switch
        {
            NotificationType.StayGuestCreated when f.PrepayRub > 0 =>
                $"{f.CompanyName}: бронь «{f.HouseName}», {dates} создана. Чтобы она сохранилась, внесите предоплату {StaysTexts.Rub(f.PrepayRub)}" +
                (f.HoldExpiresLocal is { } until ? $" до {until:HH:mm dd.MM}" : string.Empty) +
                (string.IsNullOrWhiteSpace(f.PaymentDetails) ? string.Empty : $" по реквизитам: {f.PaymentDetails}") +
                (string.IsNullOrWhiteSpace(f.PaymentPurpose) ? string.Empty : $". Назначение платежа: {f.PaymentPurpose}") +
                $". Затем приложите подтверждение оплаты на странице брони: {bookingUrl}",
            NotificationType.StayGuestCreated => $"{f.CompanyName}: бронь «{f.HouseName}», {dates} подтверждена. Страница брони: {bookingUrl}",
            NotificationType.StayGuestHoldExpiring => $"{f.CompanyName}: осталось 10 минут, чтобы приложить подтверждение оплаты брони «{f.HouseName}»: {bookingUrl}",
            NotificationType.StayGuestHoldExpired =>
                $"{f.CompanyName}: время на оплату истекло, бронь «{f.HouseName}» снята. Если вы успели оплатить — свяжитесь с компанией.{contact} {bookingUrl}",
            NotificationType.StayGuestConfirmed =>
                $"{f.CompanyName}: оплата подтверждена. «{f.HouseName}», {dates}. Заезд с {f.CheckInTime}." +
                (string.IsNullOrWhiteSpace(f.Address) ? string.Empty : $" Адрес: {f.Address}.") + $" Бронь: {bookingUrl}",
            NotificationType.StayGuestPaymentRejected =>
                $"{f.CompanyName}: оплата брони «{f.HouseName}» не подтверждена.{why} Если вы платили, компания обязана вернуть деньги или восстановить бронь.{contact} {bookingUrl}",
            NotificationType.StayGuestCancelledByOwner =>
                $"{f.CompanyName}: бронь «{f.HouseName}», {dates} отменена компанией.{why} Предоплата возвращается полностью.{contact} {bookingUrl}",
            NotificationType.StayGuestArrivalReminder =>
                $"{f.CompanyName}: завтра заезд в «{f.HouseName}» — {Short(f.CheckIn)} с {f.CheckInTime}." +
                (string.IsNullOrWhiteSpace(f.Address) ? string.Empty : $" Адрес: {f.Address}.") +
                (f.DueRub > 0 ? $" К оплате при заселении: {StaysTexts.Rub(f.DueRub)}." : string.Empty) + $" Бронь: {bookingUrl}",
            NotificationType.StayGuestCheckInInfo when !string.IsNullOrWhiteSpace(f.CheckInInfoFull) =>
                $"{f.CompanyName}: информация к заселению в «{f.HouseName}».\n{f.CheckInInfoFull}\nБронь: {bookingUrl}",
            NotificationType.StayGuestCheckInInfo => $"{f.CompanyName}: информация к заселению готова: {bookingUrl}",
            _ => $"{f.CompanyName}: статус вашей брони изменился: {bookingUrl}",
        };
        return string.IsNullOrEmpty(unsubscribeUrl) ? text : $"{text}\n\nОтписаться от сообщений: {unsubscribeUrl}";
    }
}
