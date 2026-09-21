using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>Platform default text per notification type (US-59) — used whenever a company has no
/// template row, or one with an empty body ("reset to platform text").</summary>
public static class DefaultTemplates
{
    public static string For(NotificationType type) => type switch
    {
        NotificationType.BookingConfirmed =>
            "Здравствуйте, {КлиентИмя}! Вы записаны: {Услуга}, {Дата} в {Время}. {Салон}, {Адрес}.",
        NotificationType.Reminder =>
            "Здравствуйте, {КлиентИмя}! Напоминаем: {Услуга} {Дата} в {Время}. {Салон}",
        NotificationType.BookingCancelled =>
            "Здравствуйте, {КлиентИмя}! Запись на {Услуга} {Дата} в {Время} отменена. Причина: {ПричинаОтмены}",
        NotificationType.BookingRescheduled =>
            "Здравствуйте, {КлиентИмя}! Ваша запись на {Услуга} перенесена на {НоваяДата} в {НовоеВремя}.",
        NotificationType.StaffBookingCreated =>
            "{Мастер}, новая запись: {Услуга}, {Дата} в {Время}, клиент {КлиентИмя}.",
        NotificationType.StaffBookingCancelled =>
            "{Мастер}, запись {Услуга} {Дата} в {Время} отменена.",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>The four types an owner may customize (US-59) — StaffBooking* templates address the
    /// company's own staff, not a client, and are not exposed on this screen.</summary>
    public static readonly IReadOnlyList<NotificationType> CustomizableTypes =
    [
        NotificationType.BookingConfirmed,
        NotificationType.Reminder,
        NotificationType.BookingCancelled,
        NotificationType.BookingRescheduled,
    ];
}

/// <summary>API_CONTRACT_CYCLE4.md §29.1 — the placeholder catalog the frontend renders buttons from
/// instead of keeping its own copy.</summary>
public sealed record TemplatePlaceholder(string Token, string Description, IReadOnlyList<string> Types);

public static class TemplatePlaceholders
{
    public static readonly IReadOnlyList<TemplatePlaceholder> All =
    [
        new("{КлиентИмя}", "Имя клиента", ["*"]),
        new("{Услуга}", "Название услуги", ["*"]),
        new("{Мастер}", "Имя мастера", ["*"]),
        new("{Дата}", "Дата визита", ["*"]),
        new("{Время}", "Время визита", ["*"]),
        new("{Салон}", "Название салона", ["*"]),
        new("{Адрес}", "Адрес салона", ["*"]),
        new("{ТелефонСалона}", "Телефон салона", ["*"]),
        new("{ПричинаОтмены}", "Причина отмены", ["BookingCancelled"]),
        new("{НоваяДата}", "Новая дата", ["BookingRescheduled"]),
        new("{НовоеВремя}", "Новое время", ["BookingRescheduled"]),
    ];

    public static readonly IReadOnlySet<string> AllTokens = All.Select(p => p.Token).ToHashSet(StringComparer.Ordinal);
}
