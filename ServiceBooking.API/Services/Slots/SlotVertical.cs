using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Slots;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.3.3 — one of the two slot verticals ("Дома", "Бани") as data. Trial terms, wording and the catalog cache
/// service join this record in their own tasks (BE-42-2, BE-42-W), when those classes exist.
/// </summary>
public sealed record SlotVertical(
    CompanyKind Kind,
    string ApiPrefix,
    GateUnit Unit,
    bool HasHouses,
    bool StandaloneOrdersAlwaysOn,
    bool RequiresCapacityToPublish,
    bool PublishLimit,
    IReadOnlyList<string> LegalKeys,
    Func<string?, SlugCheck> ValidateCompanySlug,
    Func<string?, bool> IsValidResourceSlug,
    Func<string, string, string> ResourcePagePath);

public static class SlotVerticals
{
    public static readonly SlotVertical Stays = new(
        CompanyKind.Stays, "api/stays", GateUnit.House, HasHouses: true, StandaloneOrdersAlwaysOn: false,
        RequiresCapacityToPublish: false, PublishLimit: false,
        LegalKeys: new[] { "StayServiceBookingNotice", "StayServiceBookingTerms", "StayServiceCancellationTerms" },
        ValidateCompanySlug: StaysSlugPolicy.Validate,
        IsValidResourceSlug: StaysSlugPolicy.IsValidServiceSlug,
        ResourcePagePath: (company, slug) => $"/{company}/uslugi/{slug}");

    public static readonly SlotVertical Baths = new(
        CompanyKind.Baths, "api/baths", GateUnit.Resource, HasHouses: false, StandaloneOrdersAlwaysOn: true,
        RequiresCapacityToPublish: true, PublishLimit: true,
        LegalKeys: new[] { "BathBookingNotice", "BathBookingTerms", "StayServiceCancellationTerms" },
        ValidateCompanySlug: BathsSlugPolicy.Validate,
        IsValidResourceSlug: slug => BathsSlugPolicy.ValidateResource(slug) == SlugCheck.Ok,
        ResourcePagePath: (company, slug) => $"/{company}/{slug}");

    public static SlotVertical? Find(CompanyKind kind) => kind switch
    {
        CompanyKind.Stays => Stays,
        CompanyKind.Baths => Baths,
        CompanyKind.Services => null,
        CompanyKind.Orders => null,
        _ => throw new System.Diagnostics.UnreachableException()
    };

    public static bool IsSlotKind(CompanyKind kind) => Find(kind) is not null;

    public static SlotVertical Get(CompanyKind kind) =>
        Find(kind) ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a slot vertical.");
}
