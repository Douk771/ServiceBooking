namespace ServiceBooking.API.Services.Shops;

public enum CatalogListingCheckCode
{
    ShopBlocked,
    NoWorkingHours,
    NoPublishedProducts,
    NotAllowedByPlan,
    HiddenByOwner
}

public sealed record CatalogListingCheck(CatalogListingCheckCode Code, string Text, bool Done);

public sealed record CatalogListingResult(bool Visible, IReadOnlyList<CatalogListingCheck> Checklist);

/// <summary>What <see cref="CatalogListingRules"/> decides from. A plain value so the rule stays pure and identical for the catalog query and the owner's checklist.</summary>
public sealed record CatalogListingInput(
    bool IsActive, bool HasWorkingHours, bool HasPublishedProduct, bool AllowedByPlan, bool ShowInPublicListing);

/// <summary>
/// ARCHITECTURE_CYCLE25.md §505.1 — the ONE rule of "a shop is visible in the goods catalog": active ∧ hours set ∧ a published product ∧ the tariff
/// allows ∧ the owner did not switch it off. The checklist lists only the applicable items (blocked and not-allowed-by-plan only when true) and
/// <c>Visible ⇔ every listed item is done</c>. Pure.
/// </summary>
public static class CatalogListingRules
{
    public const string ShopBlockedText = "Магазин заблокирован администратором";
    public const string NoWorkingHoursText = "Задайте часы работы";
    public const string NoPublishedProductsText = "Опубликуйте хотя бы один товар";
    public const string NotAllowedByPlanText = "Показ в каталоге не входит в ваш тариф";
    public const string HiddenByOwnerText = "Показ выключен в настройках";
    public const string VisibleStatusText = "Магазин виден в каталоге goods.ezbook.ru";
    public const string HiddenStatusText = "Магазина сейчас нет в каталоге";

    public static CatalogListingResult Evaluate(CatalogListingInput input)
    {
        var checklist = new List<CatalogListingCheck>();
        if (!input.IsActive) checklist.Add(new(CatalogListingCheckCode.ShopBlocked, ShopBlockedText, false));
        checklist.Add(new(CatalogListingCheckCode.NoWorkingHours, NoWorkingHoursText, input.HasWorkingHours));
        checklist.Add(new(CatalogListingCheckCode.NoPublishedProducts, NoPublishedProductsText, input.HasPublishedProduct));
        if (!input.AllowedByPlan) checklist.Add(new(CatalogListingCheckCode.NotAllowedByPlan, NotAllowedByPlanText, false));
        checklist.Add(new(CatalogListingCheckCode.HiddenByOwner, HiddenByOwnerText, input.ShowInPublicListing));
        return new CatalogListingResult(checklist.All(c => c.Done), checklist);
    }

    public static string StatusText(bool visible) => visible ? VisibleStatusText : HiddenStatusText;
}
