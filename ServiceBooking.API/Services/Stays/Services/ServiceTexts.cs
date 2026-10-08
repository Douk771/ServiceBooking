using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// API_CONTRACT_CYCLE39.md §39.22–§39.27 — the sentences a person reads in the answers about services, assembled on the server. The words «задаток», «невозвратный»
/// and «депозит» never appear (Т39-14: <c>StaysTextsForbiddenWordsTests</c> scans every constant and method result of this class); a sum of «не меньше 0 ₽» is never
/// produced (Т39-02).
/// </summary>
public static class ServiceTexts
{
    public const string NotAcceptingGuest = StaysTexts.NotAcceptingGuest;
    public const string NotAvailable = "Услуга недоступна для бронирования";
    public const string NoStartsText = "На эту дату свободного времени нет";
    public const string NotOrderingText = "Можно добавить к брони дома";
    public const string PayOnSite = "Оплата на месте, в компании. Через сервис оплата не производится";
    public const string OrdersDisabled = "Компания не принимает заказы услуг без проживания";
    public const string NotAvailableForStays = "Эту услугу нельзя добавить к брони";
    public const string DateInPast = "Эта дата уже прошла";
    public const string StartUnavailable = "Это время недоступно. Выберите другое";
    public const string NoPriceForHours = "На часть выбранного времени нет цены — выберите другое время";
    public const string SlotTaken = "Это время уже занято. Выберите другое";
    public const string BookingNotActive = "Бронь завершена — добавить услугу нельзя";
    public const string ProviderRequiredForServiceOrders = "Заполните сведения об исполнителе — без них заказы услуг не принимаются";
    public const string RefundTerminal = "Заказ уже завершён — отменять нечего";
    public const string RefundNothingPaid = "Сеанс не оплачен — отмена без последствий";
    public const string SessionAlreadyCancelled = "Сеанс уже отменён";
    public const string OrderAlreadyCancelled = "Заказ уже отменён";
    public const string BookingFinished = "Бронь завершена";
    public const string CheckOutPassed = "Время выезда наступило";
    public const string ServiceOrderDone = "Заказ завершён — уведомления не нужны";
    public const string HeldHint = "Сеанс сохранится, если бронь будет оплачена";
    public const string TooManyHeldOrders = "Слишком много неоплаченных заказов. Оплатите или отмените текущий заказ";
    public const string TooManyOrdersPerDay = "Слишком много заказов с этого номера. Попробуйте позже";
    public const string ListMismatch = "Список услуг не совпадает с текущим";
    public const string PastDate = "Нельзя менять прошедшие даты";
    public const string BasisRequired = "Укажите, как гость попросил услугу";
    public const string ReasonRequired = "Укажите причину — гость её увидит";
    public const string VersionMismatch = "Сеанс уже изменён — проверьте актуальное состояние";

    public static string RefundFullByRule(int prepayRub) => $"По правилу сеанса вам должны вернуть всю предоплату — {StaysTexts.Rub(prepayRub)}";

    public static string RefundFullByOwner(int prepayRub) => $"Предоплата возвращается полностью: {StaysTexts.Rub(prepayRub)}";

    public static string RefundPartialAtLeast(int refundRub, int maxDeductionRub) =>
        $"К возврату не меньше {StaysTexts.Rub(refundRub)}. Компания вправе удержать только фактические расходы на подготовку — не больше {StaysTexts.Rub(maxDeductionRub)}";

    public static string RefundCostsOnly(int maxDeductionRub) =>
        $"Компания вправе удержать только фактические расходы на подготовку, не больше {StaysTexts.Rub(maxDeductionRub)}. Остальное она обязана вернуть";

    public static string CancellationSummary(StayServiceCancellationPolicy policy, int boundaryHours) => policy == StayServiceCancellationPolicy.NoDeductions
        ? "Без удержаний: отмена до начала — вся предоплата возвращается"
        : $"Расходы на подготовку: отмена не позднее чем за {boundaryHours} ч до начала — вся предоплата; позже — компания вправе удержать только фактические расходы на подготовку, не больше стоимости первого часа";

    public static string PolicyName(StayServiceCancellationPolicy policy) => policy == StayServiceCancellationPolicy.NoDeductions ? "Без удержаний" : "Расходы на подготовку";

    public static string Hours(int n) => $"{n} {StaysTexts.Plural(n, "час", "часа", "часов")}";

    public static string BeyondHorizon(DateOnly lastDate) => StaysTexts.BeyondHorizon(lastDate);

    public static string HoursOutOfRange(int min, int max) => $"Длительность — от {min} до {max} {StaysTexts.Plural(max, "часа", "часов", "часов")}";

    public static string OutsideStay(string fromLabel, string toLabel) => $"Время должно быть в пределах проживания: с {fromLabel} до {toLabel}";

    /// <summary>«1 час», «30 минут», «2 часа», «1 час 30 минут».</summary>
    public static string LeadText(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        var parts = new List<string>();
        if (h > 0) parts.Add($"{h} {StaysTexts.Plural(h, "час", "часа", "часов")}");
        if (m > 0 || h == 0) parts.Add($"{m} {StaysTexts.Plural(m, "минуту", "минуты", "минут")}");
        return string.Join(" ", parts);
    }

    public static string TooEarly(int minLeadMinutes) => $"Забронировать можно не позже чем за {LeadText(minLeadMinutes)} до начала";

    public static string ItemUnavailable(string name) => $"Позиция «{name}» больше недоступна";

    public static string ItemQuantityExceeded(string name, int max) => $"«{name}» — не больше {max} на сеанс";

    public static string TooManySessions(int max) => $"К брони можно добавить не больше {max} {StaysTexts.Plural(max, "услуги", "услуг", "услуг")}";

    public static string PriceChanged(int totalRub) => $"Стоимость изменилась: {StaysTexts.Rub(totalRub)}. Проверьте и подтвердите ещё раз";

    public static string SlotUnavailableInForm(string serviceName, string timeLabel) =>
        $"Это время уже занято: {serviceName}, {timeLabel}. Выберите другое время или бронируйте без услуги";

    public static string HoldExpired(string? phone) =>
        "Время на оплату истекло, заказ снят. Если вы уже оплатили — свяжитесь с компанией" + (string.IsNullOrWhiteSpace(phone) ? "." : $": {phone}");

    public static string AlreadyStarted(string? phone) =>
        "Сеанс уже начался — по вопросам свяжитесь с компанией" + (string.IsNullOrWhiteSpace(phone) ? "." : $": {phone}");

    public static string ProofNotAllowed(string statusText) => $"Заказ уже {statusText.ToLowerInvariant()} — подтверждение оплаты не нужно";

    public static string StatusText(string displayStatus) => displayStatus switch
    {
        "Held" => "Ожидает оплаты",
        "AwaitingPaymentCheck" => "Ожидает проверки оплаты",
        "Confirmed" => "Подтверждён",
        "Completed" => "Завершён",
        "ExpiredUnpaid" => "Снят: не оплачен",
        "PaymentRejected" => "Оплата не подтверждена",
        "CancelledByGuest" => "Отменён гостем",
        "CancelledByOwner" => "Отменён компанией",
        _ => displayStatus
    };

    public static string SessionStateText(StayServiceSessionState state) => state switch
    {
        StayServiceSessionState.Active => "Запланирован",
        StayServiceSessionState.CancelledByGuest => "Отменён гостем",
        StayServiceSessionState.CancelledByOwner => "Отменён компанией",
        StayServiceSessionState.ReleasedWithBooking => "Снят вместе с бронью",
        _ => "Снят вместе с заказом"
    };

    public static string OutcomeText(StayBookingStatus status, string? reason, string? phone, int prepayRub)
    {
        var contact = string.IsNullOrWhiteSpace(phone) ? "" : $" Контакт компании: {phone}.";
        var why = string.IsNullOrWhiteSpace(reason) ? "" : $" Причина: {reason}.";
        return status switch
        {
            StayBookingStatus.PaymentRejected => $"Компания не подтвердила оплату.{why} Если вы платили, компания обязана вернуть деньги или восстановить заказ, если время свободно.{contact}",
            StayBookingStatus.CancelledByOwner when prepayRub > 0 =>
                $"Компания отменила заказ.{why} Внесённая предоплата должна быть возвращена полностью. Вы также вправе требовать возмещения убытков.{contact}",
            StayBookingStatus.CancelledByOwner => $"Компания отменила заказ.{why} Оплата за сеанс не вносилась.{contact}",
            StayBookingStatus.ExpiredUnpaid => $"Время на оплату истекло, заказ снят. Если вы успели оплатить — свяжитесь с компанией.{contact}",
            StayBookingStatus.CancelledByGuest => "Вы отменили заказ.",
            _ => ""
        };
    }

    public static string OwnerCancelRefund(int prepayRub) => $"Гостю нужно вернуть предоплату полностью: {StaysTexts.Rub(prepayRub)}";

    public static string StaffEventText(StayServiceOrderEventKind kind) => kind switch
    {
        StayServiceOrderEventKind.Created => "Заказ создан",
        StayServiceOrderEventKind.PaymentProofUploaded => "Приложено подтверждение оплаты",
        StayServiceOrderEventKind.PaymentProofViewed => "Просмотрено подтверждение оплаты",
        StayServiceOrderEventKind.PaymentConfirmed => "Оплата подтверждена",
        StayServiceOrderEventKind.HoldExpired => "Время на оплату истекло",
        StayServiceOrderEventKind.PaymentRejected => "Оплата отклонена",
        StayServiceOrderEventKind.CancelledByGuest => "Гость отменил заказ",
        StayServiceOrderEventKind.CancelledByOwner => "Компания отменила заказ",
        StayServiceOrderEventKind.PaymentProofsPurged => "Подтверждения оплаты удалены по сроку хранения",
        _ => "Персональные данные удалены"
    };

    public static string BasisText(StayServiceRequestBasis basis) => basis switch
    {
        StayServiceRequestBasis.Phone => "по телефону",
        StayServiceRequestBasis.InPerson => "лично",
        _ => "в мессенджере"
    };

    public static string SessionsOutsideWarning(int count) =>
        $"{count} {StaysTexts.Plural(count, "сеанс", "сеанса", "сеансов")} вне нового расписания остаются в силе";

    public const string AddedByStaffGuestText =
        "По вашей просьбе к брони добавлена услуга. Если вы этого не просили — отмените её здесь, без последствий.";
}
