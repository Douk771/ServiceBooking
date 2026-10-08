namespace ServiceBooking.API.Services.Stays;

/// <summary>ARCHITECTURE_CYCLE39.md §39.2.3, API_CONTRACT_CYCLE39.md §39.26 — what stops a service from being published. The order is the contract's. Pure.</summary>
public static class ServicePublishRules
{
    public static IReadOnlyList<StaysServiceConflictCode> Problems(bool isArchived, bool hasPriceRules, bool hasWindowsWithinHorizon)
    {
        var list = new List<StaysServiceConflictCode>();
        if (isArchived) list.Add(StaysServiceConflictCode.ServiceArchived);
        if (!hasPriceRules) list.Add(StaysServiceConflictCode.ServiceNoPrice);
        if (!hasWindowsWithinHorizon) list.Add(StaysServiceConflictCode.ServiceNoWindows);
        return list;
    }

    public static string Message(StaysServiceConflictCode code) => code switch
    {
        StaysServiceConflictCode.ServiceArchived => "Услуга в архиве",
        StaysServiceConflictCode.ServiceNoPrice => "Добавьте хотя бы одно правило цены",
        StaysServiceConflictCode.ServiceNoWindows => "Задайте свободное время: недельный шаблон или окна на даты в пределах горизонта",
        _ => string.Empty
    };
}
