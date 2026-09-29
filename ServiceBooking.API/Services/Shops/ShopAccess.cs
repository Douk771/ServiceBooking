namespace ServiceBooking.API.Services.Shops;

public enum ShopRole
{
    Owner,
    Staff,
    SuperAdmin
}

public enum ShopPermission
{
    /// <summary>Order board, card, journal, transitions, edit, issue.</summary>
    ManageOrders,

    /// <summary>"Sold out" and stock figures.</summary>
    ManageStock,

    /// <summary>Read the catalog and settings in the cabinet, the QR code.</summary>
    ViewShop,

    /// <summary>Products, prices, categories, photos, order.</summary>
    EditCatalog,

    /// <summary>Acceptance rules, seller details, the shop's address, company profile, logo.</summary>
    EditSettings,

    /// <summary>Add/remove staff.</summary>
    ManageStaff
}

/// <summary>
/// ARCHITECTURE_CYCLE23.md §392.2 — the rights table of a shop, in ONE place. The membership half of "who is the
/// owner / staff" is <c>CompanyMembership</c>; this class only says what each role may do. A staff member on an
/// owner route gets 403 with an empty body (access is refused explicitly).
/// </summary>
public static class ShopAccess
{
    public static bool Allows(ShopRole role, ShopPermission permission) => role switch
    {
        ShopRole.Owner or ShopRole.SuperAdmin => true,
        ShopRole.Staff => permission is ShopPermission.ManageOrders or ShopPermission.ManageStock or ShopPermission.ViewShop,
        _ => false
    };
}
