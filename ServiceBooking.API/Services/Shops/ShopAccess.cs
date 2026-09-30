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
    ManageStaff,

    /// <summary>ARCHITECTURE_CYCLE24.md §460 — pause / the "stop" switch and the acceptance status (owner and staff).</summary>
    ManageAcceptance,

    /// <summary>§460 — the daily menu and "sold out" with a term (owner and staff; "sold out" moved here from <see cref="ManageStock"/>).</summary>
    ManageAvailability,

    /// <summary>§460 — working hours, special days, pickup settings, weekdays of products, notification settings (owner only; the role check is <see cref="EditSettings"/>'s).</summary>
    ManageShop,

    /// <summary>ARCHITECTURE_CYCLE25.md §506 — order history, the pick list, the customer card and reading the customer note (owner and staff).</summary>
    ViewOrderReports,

    /// <summary>§506 — writing the customer note (owner and staff).</summary>
    EditCustomerNotes,

    /// <summary>§506 — the day/period summary (owner and SuperAdmin only; staff get 403).</summary>
    ViewSummary
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
        ShopRole.Staff => permission is ShopPermission.ManageOrders or ShopPermission.ManageStock or ShopPermission.ViewShop
            or ShopPermission.ManageAcceptance or ShopPermission.ManageAvailability
            or ShopPermission.ViewOrderReports or ShopPermission.EditCustomerNotes,
        _ => false
    };
}
