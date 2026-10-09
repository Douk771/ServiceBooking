using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Companies;

/// <summary>ARCHITECTURE_CYCLE42.md §42.3.2 — properties of a company kind as DATA (one row per kind).</summary>
public sealed record CompanyKindTrait(
    bool IsSalon,
    string? SalonRefusalText,
    bool VisibleOnSalonPublicPage,
    bool HasCompanyGallery,
    bool UsesStaffPositions,
    bool HasSalonSeatLimit,
    bool CityFixed,
    CompanyKind TariffLine,
    string KindLabel,
    string PhotoOwnerLabel,
    string CompanyPagePathPattern);

/// <summary>A new <see cref="CompanyKind"/> without a row throws here; CompanyKindTraitsTests walks every enum value.</summary>
public static class CompanyKindTraits
{
    public const string BathsRefusalText = "Это компания «Бани»: записи, услуги и расписание для неё недоступны.";

    private static readonly CompanyKindTrait Services = new(true, null, true, true, false, true, false,
        CompanyKind.Services, "Записи", "салона", "/company/{slug}");

    private static readonly CompanyKindTrait Orders = new(false, CompanyKindGuard.ShopRefusalText, true, true, false, false, false,
        CompanyKind.Orders, "Заказы", "магазина", "/{slug}");

    private static readonly CompanyKindTrait Stays = new(false, CompanyKindGuard.StaysRefusalText, true, false, true, false, true,
        CompanyKind.Stays, "Дома", "компании «Дома»", "/{slug}");

    private static readonly CompanyKindTrait Baths = new(false, BathsRefusalText, false, true, true, false, false,
        CompanyKind.Baths, "Бани", "компании «Бани»", "/{slug}");

    public static CompanyKindTrait For(CompanyKind kind) => kind switch
    {
        CompanyKind.Services => Services,
        CompanyKind.Orders => Orders,
        CompanyKind.Stays => Stays,
        CompanyKind.Baths => Baths,
        _ => throw new System.Diagnostics.UnreachableException()
    };

    public static string CompanyPagePath(CompanyKind kind, string slug) => For(kind).CompanyPagePathPattern.Replace("{slug}", slug);
}
