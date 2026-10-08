using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// API_CONTRACT_CYCLE37.md §37.28 — overlap of price periods (end INCLUSIVE). Two long periods may not overlap; two one-day periods may not share a date;
/// a one-day period MAY lie inside a long one (it overrides its price, SPEC §4.4). The database holds the same rule in EX_HousePricePeriods_NoOverlap
/// and UX_HousePricePeriods_SingleDay; this is the readable verdict with the conflicting period for the message.
/// </summary>
public static class HousePricePeriodRules
{
    public static HousePricePeriod? FindConflict(IEnumerable<HousePricePeriod> others, DateOnly start, DateOnly end, Guid? exceptId = null)
    {
        var single = start == end;
        foreach (var o in others)
        {
            if (exceptId == o.Id) continue;
            var otherSingle = o.StartDate == o.EndDate;
            if (single && otherSingle && o.StartDate == start) return o;
            if (!single && !otherSingle && start <= o.EndDate && o.StartDate <= end) return o;
        }
        return null;
    }

    public static string? Validate(DateOnly? start, DateOnly? end, int priceRub)
    {
        if (start is null || end is null) return "Укажите даты периода";
        if (end < start) return "Дата окончания не может быть раньше начала";
        if (end.Value.DayNumber - start.Value.DayNumber > 730) return "Период — не длиннее двух лет";
        if (priceRub is < 1 or > HouseService.MaxPriceRub) return "Цена — от 1 до 1 000 000 ₽";
        return null;
    }

    public static string OverlapText(HousePricePeriod conflict) =>
        $"Период пересекается с {StayFormat.Date(conflict.StartDate)}–{StayFormat.Date(conflict.EndDate)} ({StaysTexts.Rub(conflict.PriceRub)})";
}
