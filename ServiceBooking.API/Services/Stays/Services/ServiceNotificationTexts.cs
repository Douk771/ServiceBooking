using ServiceBooking.API.Services.Orders.Notifications;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>What the texts of a service notification are made of — assembled once by the planner from the session/order, never from the request.</summary>
public sealed record ServiceTextFacts(
    string CompanyName, string ServiceName, string? HouseName, DateOnly BusinessDate, int StartMinute, int Hours, string? CompanyPhone, string? Reason,
    int PrepayRub, bool Paid, string? PaymentDetails, string? PaymentPurpose, DateTime? HoldExpiresLocal, string? Address, string? CityName = null);

/// <summary>
/// API_CONTRACT_CYCLE39.md §39.34 — fixed texts of the notifications about services. Staff messages carry NO name or phone of the guest and use the staff form of time;
/// a guest reads calendar dates (ЮР39-8); a guest's web-push carries no personal data; the requisites go ONLY into the messenger message of this very order (Т37-04).
/// </summary>
public static class ServiceNotificationTexts
{
    public static PushPayload StaffPush(NotificationType type, ServiceTextFacts f, Guid sessionId, string cabinetUrl, ServiceWording? wording = null)
    {
        var baths = wording?.Kind == CompanyKind.Baths;
        var time = ServiceTimeFormat.Staff(f.BusinessDate, f.StartMinute, f.Hours);
        var body = type switch
        {
            NotificationType.StaffStaySessionAdded => $"Услуга к брони · «{f.HouseName}» · {f.ServiceName}, {time}",
            NotificationType.StaffServiceOrderCreated when baths => $"Новая бронь · {f.ServiceName}, {time}",
            NotificationType.StaffServiceOrderCreated => $"Новый заказ услуги · {f.ServiceName}, {time}",
            NotificationType.StaffServiceOrderPaymentProofUploaded => $"Приложено подтверждение оплаты · {f.ServiceName}, {time}",
            _ => $"Гость отменил сеанс · {f.ServiceName}, {time}",
        };
        return new PushPayload(f.CompanyName, body, $"ss-{sessionId}", cabinetUrl);
    }

    public static string StaffMax(NotificationType type, ServiceTextFacts f, Guid sessionId, string cabinetUrl, ServiceWording? wording = null)
    {
        var p = StaffPush(type, f, sessionId, cabinetUrl, wording);
        return $"{p.Title} · {p.Body}\nОткрыть: {cabinetUrl}";
    }

    public static PushPayload GuestPush(NotificationType type, Guid subjectId, string url, bool forOrder, ServiceWording? wording = null)
    {
        var w = wording ?? ServiceWording.Stays;
        var baths = w.Kind == CompanyKind.Baths;
        var body = type switch
        {
            NotificationType.ServiceGuestHoldExpiring => "Осталось 10 минут, чтобы приложить подтверждение оплаты",
            NotificationType.StayGuestSessionAdded => "Услуга добавлена к брони — откройте бронь",
            _ when baths => "Статус вашей брони изменился",
            _ => forOrder ? "Статус вашего заказа изменился" : "Статус вашей брони изменился",
        };
        return new PushPayload(w.GuestPushTitle, body, forOrder ? $"so-{subjectId}" : $"sg-{subjectId}", url);
    }

    public static string Messenger(NotificationType type, ServiceTextFacts f, string pageUrl, string? unsubscribeUrl, ServiceWording? wording = null)
    {
        var w = wording ?? ServiceWording.Stays;
        if (w.Kind == CompanyKind.Baths) return BathsMessenger(type, f, pageUrl, unsubscribeUrl, w);
        var time = ServiceTimeFormat.Guest(f.BusinessDate, f.StartMinute, f.Hours);
        var contact = string.IsNullOrWhiteSpace(f.CompanyPhone) ? string.Empty : $" Телефон компании: {f.CompanyPhone}.";
        var why = string.IsNullOrWhiteSpace(f.Reason) ? string.Empty : $" Причина: {f.Reason}.";
        var text = type switch
        {
            NotificationType.ServiceGuestOrderCreated when f.PrepayRub > 0 =>
                $"{f.CompanyName}: заказ «{f.ServiceName}», {time} создан. Чтобы он сохранился, внесите предоплату {StaysTexts.Rub(f.PrepayRub)}" +
                (f.HoldExpiresLocal is { } until ? $" до {until:HH:mm dd.MM}" : string.Empty) +
                (string.IsNullOrWhiteSpace(f.PaymentDetails) ? string.Empty : $" по реквизитам: {f.PaymentDetails}") +
                (string.IsNullOrWhiteSpace(f.PaymentPurpose) ? string.Empty : $". Назначение платежа: {f.PaymentPurpose}") +
                $". Затем приложите подтверждение оплаты на странице заказа: {pageUrl}",
            NotificationType.ServiceGuestOrderCreated => $"{f.CompanyName}: заказ «{f.ServiceName}», {time} подтверждён. Оплата на месте. Страница заказа: {pageUrl}",
            NotificationType.ServiceGuestHoldExpiring => $"{f.CompanyName}: осталось 10 минут, чтобы приложить подтверждение оплаты заказа «{f.ServiceName}»: {pageUrl}",
            NotificationType.ServiceGuestHoldExpired =>
                $"{f.CompanyName}: время на оплату истекло, заказ «{f.ServiceName}» снят. Если вы успели оплатить — свяжитесь с компанией.{contact} {pageUrl}",
            NotificationType.ServiceGuestConfirmed =>
                $"{f.CompanyName}: оплата подтверждена. «{f.ServiceName}», {time}." + (string.IsNullOrWhiteSpace(f.Address) ? string.Empty : $" Адрес: {f.Address}.") + $" Заказ: {pageUrl}",
            NotificationType.ServiceGuestPaymentRejected =>
                $"{f.CompanyName}: оплата заказа «{f.ServiceName}» не подтверждена.{why} Если вы платили, компания обязана вернуть деньги или восстановить заказ.{contact} {pageUrl}",
            NotificationType.ServiceGuestCancelledByOwner when f.Paid =>
                $"{f.CompanyName}: заказ «{f.ServiceName}», {time} отменён компанией.{why} Предоплата возвращается полностью; вы вправе требовать возмещения убытков.{contact} {pageUrl}",
            NotificationType.ServiceGuestCancelledByOwner =>
                $"{f.CompanyName}: заказ «{f.ServiceName}», {time} отменён компанией.{why} Оплата за сеанс не вносилась.{contact} {pageUrl}",
            NotificationType.StayGuestSessionAdded =>
                $"{f.CompanyName}: по вашей просьбе к брони «{f.HouseName}» добавлена услуга «{f.ServiceName}», {time}. Оплата на месте. Если вы этого не просили, отмените на странице брони — без последствий: {pageUrl}",
            NotificationType.StayGuestSessionCancelledByOwner =>
                $"{f.CompanyName}: сеанс «{f.ServiceName}», {time} отменён компанией.{why} Оплата за сеанс не вносилась.{contact} {pageUrl}",
            _ => $"{f.CompanyName}: статус вашего заказа изменился: {pageUrl}",
        };
        return string.IsNullOrEmpty(unsubscribeUrl) ? text : $"{text}\n\nОтписаться от сообщений: {unsubscribeUrl}";
    }

    /// <summary>Messenger texts of «Бани» (§42.9): a «бронь», the guest's time marked as local, no house.</summary>
    private static string BathsMessenger(NotificationType type, ServiceTextFacts f, string pageUrl, string? unsubscribeUrl, ServiceWording w)
    {
        var time = ServiceTimeFormat.Guest(f.BusinessDate, f.StartMinute, f.Hours) + w.GuestTimeSuffix(f.CityName);
        var contact = string.IsNullOrWhiteSpace(f.CompanyPhone) ? string.Empty : $" Телефон компании: {f.CompanyPhone}.";
        var why = string.IsNullOrWhiteSpace(f.Reason) ? string.Empty : $" Причина: {f.Reason}.";
        var text = type switch
        {
            NotificationType.ServiceGuestOrderCreated when f.PrepayRub > 0 =>
                $"{f.CompanyName}: бронь «{f.ServiceName}», {time} создана. Чтобы она сохранилась, внесите предоплату {StaysTexts.Rub(f.PrepayRub)}" +
                (f.HoldExpiresLocal is { } until ? $" до {until:HH:mm dd.MM}" : string.Empty) +
                (string.IsNullOrWhiteSpace(f.PaymentDetails) ? string.Empty : $" по реквизитам: {f.PaymentDetails}") +
                (string.IsNullOrWhiteSpace(f.PaymentPurpose) ? string.Empty : $". Назначение платежа: {f.PaymentPurpose}") +
                $". Затем приложите подтверждение оплаты на странице брони: {pageUrl}",
            NotificationType.ServiceGuestOrderCreated => $"{f.CompanyName}: бронь «{f.ServiceName}», {time} подтверждена. Оплата на месте. Страница брони: {pageUrl}",
            NotificationType.ServiceGuestHoldExpiring => $"{f.CompanyName}: осталось 10 минут, чтобы приложить подтверждение оплаты брони «{f.ServiceName}»: {pageUrl}",
            NotificationType.ServiceGuestHoldExpired =>
                $"{f.CompanyName}: время на оплату истекло, бронь «{f.ServiceName}» снята. Если вы успели оплатить — свяжитесь с компанией.{contact} {pageUrl}",
            NotificationType.ServiceGuestConfirmed =>
                $"{f.CompanyName}: оплата подтверждена. «{f.ServiceName}», {time}." + (string.IsNullOrWhiteSpace(f.Address) ? string.Empty : $" Адрес: {f.Address}.") + $" Бронь: {pageUrl}",
            NotificationType.ServiceGuestPaymentRejected =>
                $"{f.CompanyName}: оплата брони «{f.ServiceName}» не подтверждена.{why} Если вы платили, компания обязана вернуть деньги или восстановить бронь.{contact} {pageUrl}",
            NotificationType.ServiceGuestCancelledByOwner when f.Paid =>
                $"{f.CompanyName}: бронь «{f.ServiceName}», {time} отменена компанией.{why} Предоплата возвращается полностью; вы вправе требовать возмещения убытков.{contact} {pageUrl}",
            NotificationType.ServiceGuestCancelledByOwner =>
                $"{f.CompanyName}: бронь «{f.ServiceName}», {time} отменена компанией.{why} Оплата за сеанс не вносилась.{contact} {pageUrl}",
            _ => $"{f.CompanyName}: статус вашей брони изменился: {pageUrl}",
        };
        return string.IsNullOrEmpty(unsubscribeUrl) ? text : $"{text}\n\nОтписаться от сообщений: {unsubscribeUrl}";
    }
}
