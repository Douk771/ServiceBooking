using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.6.4, §37.13.4 — texts a person reads, assembled on the server. ЮР-1: the words
/// "задаток", "невозвратный", "депозит" are never used (a unit test scans every constant and method result).
/// </summary>
public static class StaysTexts
{
    public const string StaysRefusalText = "Это компания «Дома»: записи, услуги и расписание для неё недоступны.";
    public const string GalleryRefusalText = "У компании «Дома» фото добавляются к домам.";
    public const string NotAcceptingGuest = "Бронирование временно недоступно";
    public const string PageUnavailable = "Страница недоступна";
    public const string HouseUnavailable = "Дом недоступен для бронирования";
    public const string RefundNothingPaid = "Бронь не оплачена — отмена без последствий";
    /// <summary>The refund line of a booking in a final status (expired, payment rejected, cancelled): nothing to cancel, no amount to promise.</summary>
    public const string RefundNotApplicable = "Бронь уже не действует — отменять нечего";
    public const string DatesUnavailable = "Эти даты уже заняты. Выберите другие";
    public const string CannotCancelAlready = "Бронь уже отменена";

    /// <summary>Fallback of the owner's attestation (ЮР-2, customer decision: houses WITHOUT a registry number are published under the owner's assurance, any kind).</summary>
    public const string RegistryNoticeFallback =
        "Подтверждаю, что сведения о доме достоверны. Если дом является гостевым домом или иным средством размещения, " +
        "номер из реестра указан; если номера нет, я заверяю, что дом не требует внесения в реестр (например, жилое помещение), " +
        "и принимаю на себя ответственность за публикацию дома без номера.";

    public static string RegistryNoticeVersion() => "fallback:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RegistryNoticeFallback))).ToLowerInvariant();

    public static string Plural(int n, string one, string few, string many)
    {
        var a = Math.Abs(n) % 100;
        var b = a % 10;
        if (a is >= 11 and <= 19) return many;
        return b == 1 ? one : b is >= 2 and <= 4 ? few : many;
    }

    public static string Rub(int amount) => amount.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")).Replace(' ', ' ').Replace(' ', ' ') + " ₽";

    public static string NightsText(int n) => $"{n} {Plural(n, "ночь", "ночи", "ночей")}";

    public static string RefundFull(int prepayRub) => $"К возврату не меньше {Rub(prepayRub)} — предоплата возвращается полностью.";

    public static string RefundPartial(int refundRub, int maxDeductionRub) =>
        $"К возврату не меньше {Rub(refundRub)}. Компания вправе удержать из предоплаты не больше {Rub(maxDeductionRub)} (стоимость первой ночи), остальное возвращается.";

    public static string OwnerCancelRefund(int prepayRub) => $"Гостю нужно вернуть предоплату полностью: {Rub(prepayRub)}";

    public static string CancellationSummary(StayCancellationPolicy policy) => policy switch
    {
        StayCancellationPolicy.Standard => "Отмена до 00:00 дня заезда — предоплата возвращается полностью. При отмене в день заезда, опоздании или незаезде компания вправе удержать из предоплаты не больше стоимости первой ночи, остальное возвращается.",
        StayCancellationPolicy.Flexible => "Отмена до времени заезда в день заезда — предоплата возвращается полностью. При более поздней отмене или незаезде компания вправе удержать из предоплаты не больше стоимости первой ночи, остальное возвращается.",
        _ => "При отмене до времени заезда предоплата возвращается полностью."
    };

    public static string CannotCancel(string? phone) =>
        "Время заезда наступило — по вопросам отмены свяжитесь с компанией" + (string.IsNullOrWhiteSpace(phone) ? "." : $": {phone}");

    public static string HoldExpiredMessage(string? phone) =>
        "Время на оплату истекло, бронь снята. Если вы уже оплатили — свяжитесь с компанией" + (string.IsNullOrWhiteSpace(phone) ? "." : $": {phone}");

    public static string StatusText(string displayStatus) => displayStatus switch
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

    public static string OutcomeText(StayBookingStatus status, string? reason, string? phone)
    {
        var contact = string.IsNullOrWhiteSpace(phone) ? "" : $" Контакт компании: {phone}.";
        var why = string.IsNullOrWhiteSpace(reason) ? "" : $" Причина: {reason}.";
        return status switch
        {
            StayBookingStatus.PaymentRejected => $"Компания не подтвердила оплату.{why} Если вы платили, компания обязана вернуть деньги или восстановить бронь, если даты свободны.{contact}",
            StayBookingStatus.CancelledByOwner => $"Компания отменила бронь.{why} Внесённая предоплата должна быть возвращена полностью. Вы также вправе требовать возмещения убытков.{contact}",
            StayBookingStatus.ExpiredUnpaid => $"Время на оплату истекло, бронь снята. Если вы успели оплатить — свяжитесь с компанией.{contact}",
            StayBookingStatus.CancelledByGuest => "Вы отменили бронь.",
            _ => ""
        };
    }

    public static string BeyondHorizon(DateOnly lastDate) => $"Бронирование открыто до {lastDate:dd.MM.yyyy}";

    public static string RefusalMessage(StayRefusalCode code, int maxNights = 0, int minNights = 0, DateOnly? horizonEnd = null, int maxGuests = 0, bool withExtraBeds = false) => code switch
    {
        StayRefusalCode.InvalidDates => "Дата выезда должна быть позже даты заезда",
        StayRefusalCode.CheckInInPast => "Дата заезда уже прошла",
        StayRefusalCode.SameDayNotAllowed => "Заезд в день бронирования недоступен — выберите дату с завтрашнего дня",
        StayRefusalCode.BeyondHorizon => BeyondHorizon(horizonEnd ?? DateOnly.MinValue),
        StayRefusalCode.MaxNightsExceeded => $"Максимальный срок проживания — {NightsText(maxNights)}",
        StayRefusalCode.DatesUnavailable => DatesUnavailable,
        StayRefusalCode.MinNightsNotMet => $"Минимальный срок проживания — {NightsText(minNights)}",
        StayRefusalCode.TooManyGuests => $"В доме помещается не больше {maxGuests} {Plural(maxGuests, "гостя", "гостей", "гостей")}" + (withExtraBeds ? ", включая доп. места" : ""),
        StayRefusalCode.DogsNotAllowed => "В этом доме нельзя проживать с собаками",
        StayRefusalCode.CotNotAvailable => "В этом доме нет детской кроватки",
        StayRefusalCode.NoPriceForNights => "На часть выбранных ночей нет цены — выберите другие даты",
        StayRefusalCode.NotAcceptingBookings => NotAcceptingGuest,
        StayRefusalCode.PriceChanged => "Стоимость изменилась. Проверьте и подтвердите бронь ещё раз",
        _ => ""
    };

    public static string PriceChanged(int totalRub) => $"Стоимость изменилась: {Rub(totalRub)}. Проверьте и подтвердите бронь ещё раз";

    public static string ObjectKindLabel(HouseObjectKind k) => k switch
    {
        HouseObjectKind.GuestHouse => "Гостевой дом",
        HouseObjectKind.OtherAccommodation => "Иное средство размещения",
        _ => "Жилое помещение"
    };

    public static string ProviderStatusLabel(StayProviderStatus s) => s switch
    {
        StayProviderStatus.Organization => "Организация",
        StayProviderStatus.IndividualEntrepreneur => "Индивидуальный предприниматель",
        StayProviderStatus.SelfEmployed => "Плательщик налога на профессиональный доход",
        _ => "Физическое лицо"
    };

    public static string BlockKindText(HouseBlockKind k) => k switch
    {
        HouseBlockKind.Repair => "Ремонт",
        HouseBlockKind.Personal => "Личное пользование",
        _ => "Другое"
    };

    public static string AmenityLabel(HouseAmenity a) => a switch
    {
        HouseAmenity.Wifi => "Wi-Fi",
        HouseAmenity.Kitchen => "Кухня",
        HouseAmenity.Parking => "Парковка",
        HouseAmenity.Sauna => "Баня / сауна",
        HouseAmenity.Bbq => "Мангал",
        HouseAmenity.WashingMachine => "Стиральная машина",
        HouseAmenity.Tv => "Телевизор",
        HouseAmenity.Fireplace => "Камин",
        HouseAmenity.GearDryer => "Сушилка для снаряжения",
        HouseAmenity.SkiStorage => "Хранение лыж",
        HouseAmenity.LiftTransfer => "Трансфер до подъёмника",
        HouseAmenity.Dishwasher => "Посудомоечная машина",
        _ => "Терраса"
    };

    public static string EventText(StayBookingEventKind k) => k switch
    {
        StayBookingEventKind.Created => "Бронь создана",
        StayBookingEventKind.PaymentProofUploaded => "Приложено подтверждение оплаты",
        StayBookingEventKind.PaymentProofViewed => "Просмотрено подтверждение оплаты",
        StayBookingEventKind.PaymentConfirmed => "Оплата подтверждена",
        StayBookingEventKind.HoldExpired => "Время на оплату истекло",
        StayBookingEventKind.PaymentRejected => "Оплата отклонена",
        StayBookingEventKind.CancelledByGuest => "Гость отменил бронь",
        StayBookingEventKind.CancelledByOwner => "Компания отменила бронь",
        StayBookingEventKind.CheckInInfoReleased => "Открыта информация к заселению",
        StayBookingEventKind.PaymentProofsPurged => "Подтверждения оплаты удалены по сроку хранения",
        _ => "Персональные данные удалены"
    };
}
