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

    // ---- Cycle 43 (API_CONTRACT_CYCLE43.md §43.20–§43.34): price screen, account block, owner block --------------------------------

    public const string PlanMatrixNote = "Мессенджеры WhatsApp и MAX подключаются отдельно от тарифа. Цена — во вкладке уведомлений";
    public const string PriceHint =
        "Цена действует для всех аккаунтов и всех линеек. Мессенджер продаётся, когда он открыт для владельцев, опция активна, цена задана " +
        "и оферта опубликована. Изменение цены не меняет уже оплаченные периоды";
    public const string PriceNotSet = "не задана";
    public const string PriceNotSetCard = "Цена не задана";
    public const string ConfirmButtonText = "Сохранить";

    public const string SaleSellable = "Продаётся";
    public const string SaleClosed = "Не продаётся: закрыт для владельцев";
    public const string SaleInactive = "Не продаётся: опция неактивна";
    public const string SaleNoPrice = "Не продаётся: цена не задана";
    public const string SaleOfferNotPublished = "Не продаётся: оферта не опубликована";

    public const string PriceNegative = "Цена не может быть отрицательной";
    public const string PriceTooLarge = "Цена — не больше 99 999 999,99 ₽";
    public const string PriceTooPrecise = "Цена — не больше двух знаков после запятой";
    public const string PriceUnchanged = "Цена и активность не изменились — сохранять нечего";
    public const string PriceCommentRequired = "Укажите причину изменения цены";
    public const string PriceCommentTooLong = "Причина — не длиннее 500 символов";

    public const string MonthsRange = "Срок — от 1 до 24 месяцев";
    public const string ExtendCommentTooLong = "Комментарий — не длиннее 500 символов";
    public const string ExpectedPriceMissing = "Не передана ожидаемая цена — обновите расчёт";
    public const string DisableCommentRequired = "Укажите причину отключения";
    public const string DisableCommentTooLong = "Причина — не длиннее 500 символов";
    public const string OfferNotPublishedConflict = "Оферта для владельцев не опубликована — {0} не продаётся";

    public const string NumberNone = "Номера нет";
    public const string NumberAwaitingQr = "Номер ожидает привязки по QR";
    public const string NumberBound = "Номер привязан";
    public const string StatusPendingPayment = "Ожидает подтверждения оплаты";
    public const string StatusNotConnected = "Не подключён";
    public const string ConnectAction = "Подключить";
    public const string RenewAction = "Продлить";

    public const string OwnerBlockTitle = "Сообщения клиентам в мессенджеры";
    public const string OwnerBlockNote = "Общие для всех ваших компаний";
    public const string NoCompanyText = "Сначала создайте компанию";

    public static string CatalogMissing(NotificationTransport t) => $"Опция {DisplayName(t)} отсутствует в каталоге";

    public static string PriceChangedByOtherAdmin(NotificationTransport t) =>
        $"Цену {DisplayName(t)} уже изменил другой администратор. Обновите страницу и проверьте последствия ещё раз";

    public static string PriceChangedConflict(NotificationTransport t, decimal? price) =>
        $"Цена {DisplayName(t)} изменилась: теперь {(price is { } p ? PriceText(p) : PriceNotSet)}. Проверьте сумму и повторите";

    public static string ConfirmTitle(NotificationTransport t) => $"Изменить цену {DisplayName(t)}?";

    public static string SaleText(Funding.MessengerSaleReason? reason) => reason switch
    {
        null => SaleSellable,
        Funding.MessengerSaleReason.Closed => SaleClosed,
        Funding.MessengerSaleReason.Inactive => SaleInactive,
        Funding.MessengerSaleReason.NoPrice => SaleNoPrice,
        Funding.MessengerSaleReason.OfferNotPublished => SaleOfferNotPublished,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    /// <summary>§43.25.4 — the 409 text for "the messenger is not for sale", shared by quote, extend and confirm-payment.</summary>
    public static string NotSellableConflict(NotificationTransport t, Funding.MessengerSaleReason reason) => reason switch
    {
        Funding.MessengerSaleReason.Closed => OptionClosedForAdmin(t),
        Funding.MessengerSaleReason.Inactive =>
            $"Опция {DisplayName(t)} неактивна. Включите её во вкладке уведомлений, блок „Подключение мессенджеров“",
        Funding.MessengerSaleReason.NoPrice =>
            $"У {DisplayName(t)} не задана цена. Задайте её во вкладке уведомлений, блок „Подключение мессенджеров“",
        Funding.MessengerSaleReason.OfferNotPublished => string.Format(CultureInfo.InvariantCulture, OfferNotPublishedConflict, DisplayName(t)),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    /// <summary>"Аккаунта" when N ends in 1 (except 11), otherwise "аккаунтов" — the wording fixed by API_CONTRACT_CYCLE43.md §43.23.3.</summary>
    public static string AccountsWord(int n) => n % 10 == 1 && n % 100 != 11 ? "аккаунта" : "аккаунтов";

    public static string ConsequenceStartsSelling(NotificationTransport t) =>
        $"{DisplayName(t)} начнёт продаваться: цена появится на странице тарифов и в мастере подключения";
    public static string ConsequenceStopsSelling(NotificationTransport t) =>
        $"Продажи {DisplayName(t)} остановятся. Уже оплаченные периоды продолжат действовать";
    public static string ConsequencePaidAccounts(NotificationTransport t, int n) => n > 0
        ? $"Оплата {DisplayName(t)} действует у {n} {AccountsWord(n)}: их оплаченные периоды не меняются. Новая цена — для следующих подтверждений оплаты"
        : $"Аккаунтов с действующей оплатой {DisplayName(t)} нет";
    public static string ConsequenceZeroPrice(NotificationTransport t) => $"Цена 0 ₽: {DisplayName(t)} будет подключаться бесплатно";
    public static string ConsequenceNotSellable(NotificationTransport t, Funding.MessengerSaleReason reason) =>
        $"{DisplayName(t)} не продаётся: " + reason switch
        {
            Funding.MessengerSaleReason.Closed => "закрыт для владельцев",
            Funding.MessengerSaleReason.Inactive => "опция неактивна",
            Funding.MessengerSaleReason.OfferNotPublished => "оферта не опубликована",
            _ => "цена не задана",
        } + ". Цена начнёт действовать, когда это изменится";

    /// <summary>Money without "/мес": whole rubles without kopecks, otherwise two digits after a comma.</summary>
    public static string MoneyText(decimal amount) =>
        amount == decimal.Truncate(amount)
            ? amount.ToString("0", CultureInfo.InvariantCulture)
            : amount.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    public static string AmountText(int months, decimal pricePerMonth) =>
        $"{months} мес. × {MoneyText(pricePerMonth)} ₽ = {MoneyText(months * pricePerMonth)} ₽";

    public static string NewPaidUntilText(DateTime utc) => $"Будет оплачено до {FormatDate(utc)}";
    public static string ExtendActionText(bool renew, int months) => (renew ? "Продлить" : "Подключить") + $" на {months} мес.";

    public static string StatusEnding(DateTime endsAtUtc) => $"Отключается {FormatDate(endsAtUtc)}";
    public static string StatusTrial(DateTime? ownDateUtc) => ownDateUtc is { } d ? $"Пробный период до {FormatDate(d)}" : "Пробный период";
    public static string StatusPaid(DateTime? untilUtc) => untilUtc is { } d ? $"Оплачено до {FormatDate(d)}" : "Оплачено";
    public static string StatusExpired(bool isTrial, DateTime? untilUtc) =>
        (isTrial ? "Пробный период закончился" : "Оплата закончилась") + (untilUtc is { } d ? $" {FormatDate(d)}" : "");

    public static string NumberBoundText(string? phoneMasked) => phoneMasked is { Length: > 0 } ? $"Номер {phoneMasked}" : NumberBound;

    public static string PriceLineText(decimal? price) => price is { } p ? PriceText(p) : PriceNotSetCard;
    public static string PriceHistoryText(decimal? price) => price is { } p ? PriceText(p) : PriceNotSet;

    public static string OwnerTotalText(decimal total) => $"Мессенджеры: {PriceText(total)}";

    public static string DisableConfirmText(NotificationTransport t, DateTime dateUtc) =>
        $"Деньги не возвращаются. Сообщения клиентам в {DisplayName(t)} перестанут уходить с {FormatDate(dateUtc)}";

    public static string NothingToDisableNotConnected(NotificationTransport t) => $"Нечего отключать: {DisplayName(t)} у аккаунта не подключён";
    public static string NothingToDisableExpired(NotificationTransport t) => $"Нечего отключать: оплата {DisplayName(t)} уже закончилась";
    public static string DisableTrialWithoutDate(NotificationTransport t) =>
        $"У пробного периода {DisplayName(t)} нет даты окончания — отключите сейчас";
    public static string DisablePaymentWithoutDate(NotificationTransport t) =>
        $"У оплаты {DisplayName(t)} нет даты окончания — отключите сейчас";
    public static string DisableAlreadyScheduled(NotificationTransport t, DateTime endsAtUtc) =>
        $"Отключение {DisplayName(t)} уже назначено на {FormatDate(endsAtUtc)}";

    /// <summary>Subscription-history summary of one messenger row: "MAX: оплачено до 20.10.2026" / "MAX: не подключён".</summary>
    public static string OptionsSummary(NotificationTransport t, bool paid, DateTime? paidUntilUtc) =>
        paid && paidUntilUtc is { } d ? $"{DisplayName(t)}: оплачено до {FormatDate(d)}" : paid ? $"{DisplayName(t)}: оплачено" : $"{DisplayName(t)}: не подключён";

    public static string ExtendHistoryComment(NotificationTransport t, bool renew, int months, decimal price, string? comment) =>
        $"{(renew ? "Продление" : "Подключение")} {DisplayName(t)} на {months} мес. ({AmountText(months, price)})." +
        (string.IsNullOrWhiteSpace(comment) ? "" : " " + comment.Trim());

    public static string DisableHistoryComment(NotificationTransport t, DateTime fromUtc, string comment) =>
        $"Отключение {DisplayName(t)} с {FormatDate(fromUtc)}. {comment.Trim()}";

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
