using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Companies;

/// <summary>API_CONTRACT_CYCLE26.md §560.2 — gallery texts that depend on the company kind. Salon strings are byte-for-byte the cycle-10 ones.</summary>
public static class CompanyPhotoTexts
{
    public static string LimitReached(CompanyKind kind) =>
        $"В галерее {Noun(kind)} может быть не больше 10 фотографий";

    public static string ReorderMismatch(CompanyKind kind) =>
        $"Список должен содержать все фотографии {Noun(kind)} ровно по одному разу";

    private static string Noun(CompanyKind kind) => kind switch
    {
        CompanyKind.Orders => "магазина",
        CompanyKind.Stays => "компании «Дома»",
        _ => "салона"
    };
}
