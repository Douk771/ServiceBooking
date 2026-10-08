using System.Globalization;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// API_CONTRACT_CYCLE40.md §40.33 — the cycle-40 owner-facing and customer-facing messenger texts that are not
/// part of a state table, collected in ONE place so <c>MessengerTextsForbiddenWordsTests</c> (Т40-L-14: no word
/// about circumventing blocks) can read every one of them. Pure. Dates are shown in Moscow time.
/// </summary>
public static class MessengerTexts
{
    public const string PlatformDisabled = "Рассылки временно отключены платформой";
    public const string NumbersNote = "Номера общие для всех ваших компаний";
    public const string StatusNotice = "Мессенджеры подключаются только тем, кто указал статус ИП, организации или самозанятого";
    public const string PaymentPendingStep =
        "Заявка отправлена. Администратор свяжется с вами для оплаты и подтвердит её — после этого привяжите номер здесь";
    public const string PaymentUnderReview = "Оплата на проверке";
    public const string WhatsAppFootnote = "Доступ к WhatsApp в России ограничен: сообщения могут не доходить";
    public const string ConditionsUrl = "/offer-channel";
    public const string ConditionsLabel = "Условия";
    public const string PriorityWarning = "Приоритетный номер не работает: выберите другой или „во все“";
    public const string TermsChangedConflict = "Условия подключения обновились — примите их заново";
    public const string RiskTextChanged = "Текст изменился, прочитайте заново";
    public const string OptedOutInProfile = "Уведомления в мессенджеры выключены в профиле";

    public const string BlockedSettingsNoPaid = "Подключите WhatsApp или MAX";
    public const string BlockedSettingsNotWorking = "Номер не работает — откройте блок „Номера“";
    public const string BlockedSettingsNothingOpen = "Подключение мессенджеров сейчас недоступно";

    public static string DisplayName(NotificationTransport transport) => ChannelPresentation.TransportDisplayName(transport);

    public static string TermsTitle(NotificationTransport t) => $"Условия подключения {DisplayName(t)}";
    public static string TermsTrialHint(NotificationTransport t) =>
        $"Пробный период уже включает {DisplayName(t)}. Перед привязкой номера примите условия";
    public static string Unavailable(NotificationTransport t) => $"Подключение {DisplayName(t)} сейчас недоступно";
    public static string OptionClosedForAdmin(NotificationTransport t) =>
        $"Опция {DisplayName(t)} закрыта для подключения. Откройте её в блоке „Подключение мессенджеров“";
    public static string AlreadyHaveNumber(NotificationTransport t) => $"У вас уже есть номер {DisplayName(t)}";
    public static string PayFirst(NotificationTransport t) => $"Сначала отправьте заявку на оплату {DisplayName(t)}";
    public static string StaffConsentLabel(NotificationTransport[] transports) =>
        $"Клиент согласился получать сообщения об этой записи в {TransportsPhrase(transports)}";

    public static string TestMessageBody(NotificationTransport t) =>
        $"Проверка номера {DisplayName(t)} для уведомлений ezbook.ru: если вы видите это сообщение, номер работает.";

    public static string[] QrInstruction(NotificationTransport t) => t switch
    {
        NotificationTransport.WhatsApp =>
        [
            "Откройте WhatsApp на телефоне с номером, который будет отправлять сообщения",
            "Настройки → Связанные устройства → Привязка устройства",
            "Наведите камеру на QR-код на этом экране",
        ],
        NotificationTransport.Max =>
        [
            "Отключите пароль входа в настройках MAX",
            "Откройте MAX на телефоне с номером, который будет отправлять сообщения",
            "Профиль → Устройства → Подключить устройство",
            "Наведите камеру на QR-код на этом экране",
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };

    /// <summary>§40.33.4 / §40.3.6.</summary>
    public static string FundingText(Billing.ChannelFundingState state, NotificationTransport t, TransportPaymentView payment, string? workingPhoneMasked) =>
        state switch
        {
            Billing.ChannelFundingState.NotPaid => $"Номер {DisplayName(t)} не оплачен",
            Billing.ChannelFundingState.Funded => payment.PaidUntil is { } until
                ? (payment.IsTrial ? $"Пробный период до {FormatDate(until)}" : $"Оплачено до {FormatDate(until)}")
                : "Оплачено",
            Billing.ChannelFundingState.Unfunded =>
                $"Лишний номер {DisplayName(t)}: сообщения уходят с {workingPhoneMasked ?? "другого номера"}. Отвяжите этот номер",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

    /// <summary>§40.33.8: one entry per shown transport, joined by "; ".</summary>
    public static string NumbersText(IEnumerable<(NotificationTransport Transport, bool Paid, bool IsTrial, DateTime? PaidUntil)> shown) =>
        string.Join("; ", shown.Select(s =>
            $"{DisplayName(s.Transport)}: " + (s.Paid && s.PaidUntil is { } until
                ? (s.IsTrial ? $"пробный до {FormatDate(until)}" : $"оплачено до {FormatDate(until)}")
                : "не подключён")));

    /// <summary>§40.33.9 — delivery-journal reasons new in cycle 40 (and the two reworded ones).</summary>
    public static string? DeliveryReasonText(string reasonName) => reasonName switch
    {
        "ClientDeclinedMessenger" => "клиент не выбрал сообщения в мессенджер",
        "PlatformMessagingDisabled" => "рассылки отключены платформой",
        "ChannelAccountMismatch" => "номер принадлежит другому аккаунту",
        "NoProviderDeliveryConsent" => "нет согласия клиента на сообщения в мессенджер",
        "NoUsableChannel" => "номер не привязан или отключён",
        "NotOnPaidPlan" => "номер не оплачен",
        _ => null,
    };

    /// <summary>"WhatsApp", "MAX" or "WhatsApp и MAX".</summary>
    public static string TransportsPhrase(IEnumerable<NotificationTransport> transports) =>
        string.Join(" и ", transports.Distinct().OrderBy(t => (int)t).Select(DisplayName));

    /// <summary>Moscow calendar date, <c>dd.MM.yyyy</c> (Russia has no DST since 2014 — a fixed +3 offset).</summary>
    public static string FormatDate(DateTime utc) => MoscowTime(utc).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string FormatDayMonth(DateTime utc) => MoscowTime(utc).ToString("dd.MM", CultureInfo.InvariantCulture);

    private static DateTime MoscowTime(DateTime utc) => utc.ToUniversalTime().AddHours(3);

    /// <summary>"490 ₽/мес", fractional "490,50 ₽/мес".</summary>
    public static string PriceText(decimal pricePerMonth) =>
        (pricePerMonth == decimal.Truncate(pricePerMonth)
            ? pricePerMonth.ToString("0", CultureInfo.InvariantCulture)
            : pricePerMonth.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')) + " ₽/мес";
}

/// <summary>Payment facts <see cref="MessengerTexts.FundingText"/> needs.</summary>
public readonly record struct TransportPaymentView(DateTime? PaidUntil, bool IsTrial);
