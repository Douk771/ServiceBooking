using ServiceBooking.API.DTOs.Stays;

namespace ServiceBooking.API.Services.Stays;

/// <summary>API_CONTRACT_CYCLE37.md §37.27.3-§37.27.5 — validation of the settings, requisites and provider inputs. Pure: an error is the Russian 400 text.</summary>
public static class StaysSettingsRules
{
    public static string? Validate(StaysSettingsDto d, out TimeOnly checkIn, out TimeOnly checkOut, out TimeOnly infoSend)
    {
        checkIn = checkOut = infoSend = default;
        if (!StayFormat.TryParseTime(d.CheckInTime, out checkIn) || !StayFormat.TryParseTime(d.CheckOutTime, out checkOut) ||
            !StayFormat.IsHalfHour(checkIn) || !StayFormat.IsHalfHour(checkOut))
            return "Время — с шагом 30 минут";
        if (checkOut > checkIn) return "Время выезда не может быть позже времени заезда";
        if (d.MinNights is < 1 or > 30) return "Минимум ночей — от 1 до 30";
        if (d.MaxNights is < 1 or > 90) return "Максимум ночей — от 1 до 90";
        if (d.MinNights > d.MaxNights) return "Минимум не может быть больше максимума";
        if (d.HorizonDays is < 30 or > 730) return "Горизонт бронирования — от 30 до 730 дней";
        if (d.HoldMinutes is < 10 or > 180) return "Время на оплату — от 10 до 180 минут";
        if (d.PrepayPercent is < 0 or > 100) return "Предоплата — от 0 до 100 %";
        if (!Enum.IsDefined(d.CancellationPolicy)) return "Неизвестный шаблон отмены";
        if (d.DogFeeRub is < 0 or > 100_000 || d.CotFeeRub is < 0 or > 100_000) return "Сумма — от 0 до 100 000 ₽";
        if (!StayFormat.TryParseTime(d.CheckInInfoSendTime, out infoSend) || !StayFormat.IsHalfHour(infoSend)) return "Время — с шагом 30 минут";
        if (d.CheckInInfoText is { Length: > 2000 }) return "Текст к заселению — не длиннее 2000 символов";
        return null;
    }

    public static string? ValidatePaymentDetails(PaymentDetailsDto d)
    {
        if (d.PaymentDetails is { Length: > 1000 }) return "Реквизиты — не длиннее 1000 символов";
        if (d.PaymentPurpose is { Length: > 200 }) return "Назначение платежа — не длиннее 200 символов";
        return null;
    }

    public static string? ValidateProvider(ProviderInput i, out string? name, out string? inn, out string? ogrn, out string? address)
    {
        name = Trim(i.Name);
        inn = Trim(i.Inn);
        ogrn = Trim(i.Ogrn);
        address = Trim(i.ClaimsAddress);
        if (i.Status is null || !Enum.IsDefined(i.Status.Value)) return "Укажите статус исполнителя";
        var status = i.Status.Value;
        if (name is null) return "Укажите наименование или ФИО";
        if (name.Length > 300) return "Наименование — не длиннее 300 символов";
        var isOrg = status == Core.Enums.StayProviderStatus.Organization;
        if (inn is null || !InnValidator.IsValid(inn) || (isOrg && inn.Length != 10) || (!isOrg && inn.Length != 12)) return "Неверный ИНН";
        switch (status)
        {
            case Core.Enums.StayProviderStatus.Organization:
                if (ogrn is null) return "Укажите ОГРН";
                if (!IsDigits(ogrn, 13)) return "Неверный ОГРН";
                break;
            case Core.Enums.StayProviderStatus.IndividualEntrepreneur:
                if (ogrn is null) return "Укажите ОГРНИП";
                if (!IsDigits(ogrn, 15)) return "Неверный ОГРН";
                break;
            default:
                if (ogrn is not null) return "ОГРН указывается только для организации и ИП";
                break;
        }
        if (address is null) return "Укажите адрес для претензий";
        if (address.Length > 500) return "Адрес — не длиннее 500 символов";
        return null;
    }

    private static bool IsDigits(string s, int length) => s.Length == length && s.All(char.IsAsciiDigit);

    public static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
