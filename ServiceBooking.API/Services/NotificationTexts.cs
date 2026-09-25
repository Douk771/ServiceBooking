using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services;

/// <summary>
/// Russian text for the delivery log (API_CONTRACT_CYCLE4.md §30.1, US-32 p.2: "русские формулировки —
/// собирает сервер, фронт их не сочиняет"). Pure: takes an already-loaded <see cref="OutboundNotification"/>
/// row, no DB/HTTP of its own.
///
/// Placed outside <c>Services/Notifications/</c> (this cycle's file-ownership split between two backend
/// developers) even though it is the presentation counterpart of the reason codes that folder owns.
/// </summary>
public static class NotificationTexts
{
    public static string TypeText(NotificationType type) => type switch
    {
        NotificationType.BookingConfirmed => "Подтверждение записи",
        NotificationType.Reminder => "Напоминание",
        NotificationType.BookingCancelled => "Отмена записи",
        NotificationType.BookingRescheduled => "Перенос записи",
        NotificationType.StaffBookingCreated => "Уведомление сотруднику: новая запись",
        NotificationType.StaffBookingCancelled => "Уведомление сотруднику: отмена записи",
        NotificationType.StaffBookingRescheduled => "Уведомление сотруднику: перенос записи",
        _ => "Уведомление",
    };

    /// <summary>
    /// The catalog enumerated verbatim in API_CONTRACT_CYCLE4.md §30.1: «доставлено», «прочитано»,
    /// «отправлено, доставка не подтверждена», «не доставлено: у клиента нет WhatsApp», «клиент
    /// отказался от уведомлений», «визит уже начался — не отправлено», «канал недоступен», «срок
    /// действия канала истёк», «до визита осталось слишком мало времени», «временный сбой, повторим» —
    /// plus a handful this developer added for reasons the catalog doesn't explicitly cover (rejected by
    /// provider, retries exhausted, cancelled, disabled by company, waiting).
    ///
    /// <see cref="NotificationReason.NoUsableChannel"/> covers two different owner-facing situations
    /// (no channel assigned at all vs. an assigned channel whose payment lapsed) — disambiguated here by
    /// <paramref name="channelId"/>'s nullability, per the schema comment on
    /// <see cref="OutboundNotification.ChannelId"/> ("Null допустим только для Skipped-строк «канала нет»").
    /// </summary>
    public static string StatusText(NotificationStatus status, NotificationReason? reason, Guid? channelId, DateTime? readAtUtc, int attemptCount)
    {
        if (status == NotificationStatus.Delivered)
            return readAtUtc is not null ? "прочитано" : "доставлено";

        if (status == NotificationStatus.Sent)
            return "отправлено, доставка не подтверждена";

        if (status == NotificationStatus.Pending)
            return attemptCount > 0 ? "временный сбой, повторим" : "ожидает отправки";

        if (status == NotificationStatus.Cancelled)
            return "отменено";

        return reason switch
        {
            NotificationReason.RecipientHasNoWhatsApp => "не доставлено: у клиента нет WhatsApp",
            // ARCHITECTURE_CYCLE9.md §104.9 (US-120) — MAX's own equivalent of "у клиента нет WhatsApp".
            NotificationReason.RecipientNotInMax => "не доставлено: у клиента нет MAX",
            // ARCHITECTURE_CYCLE9.md §104.5 (US-125) — the priority transport wasn't usable when this
            // event was queued (unassigned/unfunded/disconnected). No автоподмена — the owner has to fix
            // it or switch modes, so the text says exactly that instead of a generic "пропущено".
            NotificationReason.PriorityChannelUnavailable => "приоритетный канал не был доступен",
            NotificationReason.RecipientOptedOut => "клиент отказался от уведомлений",
            NotificationReason.VisitAlreadyStarted => "визит уже начался — не отправлено",
            NotificationReason.NoUsableChannel => channelId is null ? "канал недоступен" : "срок действия канала истёк",
            NotificationReason.NotOnPaidPlan => "недоступно на вашем тарифе",
            NotificationReason.TypeDisabledByCompany => "уведомления этого типа отключены салоном",
            NotificationReason.BelowMinimumLeadTime => "до визита осталось слишком мало времени",
            NotificationReason.RejectedByProvider => "отклонено провайдером",
            NotificationReason.RetriesExhausted => "не удалось отправить после нескольких попыток",
            NotificationReason.BookingOrAssignmentCancelled => "отменено",
            NotificationReason.Delivered => "доставлено",
            // ARCHITECTURE_CYCLE9.md §105.8 (US-124) — push-specific reasons. Not surfaced through any
            // HTTP DTO this cycle (StaffPushNotification has no delivery-log endpoint), added here anyway
            // so a future log surface, and StaffPushDispatchTask's own diagnostic logging, reuse the same
            // single place Russian reason text is assembled (§6 convention) instead of inventing a second one.
            NotificationReason.PushSubscriptionGone => "устройство отписалось от уведомлений",
            NotificationReason.PushAuthRejected => "отклонено сервисом push-уведомлений",
            NotificationReason.PushPayloadTooLarge => "сообщение не поместилось в push-уведомление",
            NotificationReason.PushTtlExhausted => "не отправлено: истёк срок ожидания",
            NotificationReason.StaffPushDisabledByCompany => "push-уведомления сотрудникам отключены салоном",
            NotificationReason.MasterNoLongerInCompany => "сотрудник больше не работает в этой компании",
            NotificationReason.PushSubscriptionReassigned => "устройство теперь привязано к другому сотруднику",
            null => status == NotificationStatus.Failed ? "не удалось отправить" : "пропущено",
            _ => "пропущено",
        };
    }
}
