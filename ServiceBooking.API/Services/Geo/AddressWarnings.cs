using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Geo;

/// <summary>
/// Pure mapping from a geocoding outcome/candidate to a warning code and a ready Russian sentence
/// (ARCHITECTURE_CYCLE13.md §209.1, API_CONTRACT_CYCLE13.md §236). The server composes the text — the
/// frontend only branches on <see cref="AddressWarning.Code"/> — so the wording lives in exactly one
/// place (the same convention as <c>NotificationTexts</c>/<c>BookingEventTexts</c>).
/// </summary>
public static class AddressWarnings
{
    public static readonly AddressWarning NotFound = new(
        "NotFound", "Карта не знает такого адреса — его можно сохранить как есть.");

    public static readonly AddressWarning Unavailable = new(
        "Unavailable", "Не удалось проверить адрес, попробуйте позже. Сохранение при этом не заблокировано.");

    /// <summary>Null for <see cref="AddressPrecision.House"/> — that precision is what makes an address
    /// Verified (§209.1), it never carries a warning of its own.</summary>
    public static AddressWarning? ForPrecision(AddressPrecision precision) => precision switch
    {
        AddressPrecision.House => null,
        AddressPrecision.Street => new AddressWarning(
            "PrecisionStreet", "Карта нашла только улицу — конкретный дом не найден, адрес не подтверждён."),
        AddressPrecision.Locality => new AddressWarning(
            "PrecisionLocality", "Карта нашла только населённый пункт или район — адрес не подтверждён."),
        AddressPrecision.Other => new AddressWarning(
            "PrecisionOther", "Карта не смогла точно определить адрес — проверку нельзя считать состоявшейся."),
        _ => new AddressWarning(
            "PrecisionOther", "Карта не смогла точно определить адрес — проверку нельзя считать состоявшейся."),
    };

    /// <summary>
    /// R6 — the candidate's own city (from the geocoder's response) vs. the company's OWN city field.
    /// Never changes <c>Company.CityId</c> itself (§209/§236): only surfaces a warning so the owner can
    /// fix it deliberately. Null when there is nothing to compare (either side unknown) — an absent
    /// company city is not itself a mismatch.
    /// </summary>
    public static AddressWarning? ForCityMismatch(string? candidateCityName, string? companyCityName)
    {
        if (string.IsNullOrWhiteSpace(candidateCityName) || string.IsNullOrWhiteSpace(companyCityName))
            return null;

        if (string.Equals(candidateCityName.Trim(), companyCityName.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;

        return new AddressWarning(
            "CityMismatch",
            "Найденный адрес относится к другому городу — от города зависят часовой пояс и раздел в " +
            "каталоге. Проверьте поле «Город».");
    }
}
