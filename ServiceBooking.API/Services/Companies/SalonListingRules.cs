using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.API.Services.Companies;

/// <summary>What <see cref="SalonListingRules"/> decides from — a plain value, identical for the checklist and (via its SQL twin) the catalog.</summary>
public sealed record SalonListingInput(bool IsActive, bool AllowedByPlan, bool ShowInPublicListing);

/// <summary>
/// ARCHITECTURE_CYCLE31.md §31.5.1 — the ONE rule of "a salon is visible in the ezbook.ru catalog".
/// Visible ⇔ active ∧ allowed by plan ∧ owner opted in. Checklist: SalonBlocked (only if !active, never done) →
/// NotAllowedByPlan (only if !allowed, never done) → HiddenByOwner (always, done = ShowInPublicListing). Pure.
/// </summary>
public static class SalonListingRules
{
    public const string SalonBlockedText = "Салон заблокирован администратором";
    public const string VisibleStatusText = "Салон виден в каталоге ezbook.ru";
    public const string HiddenStatusText = "Салона сейчас нет в каталоге";
    public const string NotAllowedByPlanHintText = "Показ в каталоге не входит в ваш тариф — повысьте тариф, чтобы включить";
    public const string MissingValueText = "Не указано, показывать ли салон в каталоге";

    public static CatalogListingResult Evaluate(SalonListingInput input)
    {
        var checklist = new List<CatalogListingCheck>();
        if (!input.IsActive) checklist.Add(new(CatalogListingCheckCode.SalonBlocked, SalonBlockedText, false));
        if (!input.AllowedByPlan) checklist.Add(new(CatalogListingCheckCode.NotAllowedByPlan, CatalogListingRules.NotAllowedByPlanText, false));
        checklist.Add(new(CatalogListingCheckCode.HiddenByOwner, CatalogListingRules.HiddenByOwnerText, input.ShowInPublicListing));
        return new CatalogListingResult(checklist.All(c => c.Done), checklist);
    }

    public static string StatusText(bool visible) => visible ? VisibleStatusText : HiddenStatusText;
}
