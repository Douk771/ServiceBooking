namespace ServiceBooking.API.Services.Showcase;

/// <summary>The two products that share one demo instance (ARCHITECTURE_CYCLE35.md §35.7): "Запись" (<c>services</c>) and "Заказы" (<c>orders</c>).</summary>
public enum DemoProduct
{
    Services,
    Orders,
}

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.4, ARCHITECTURE_CYCLE35.md §35.7 — the demo roles of <see cref="ShowcaseProfile.Demo"/> (three of the salon product, three of the
/// goods product "Заказы") and how to find them again. The accounts are ordinary rows of the
/// generated graph with stable UUIDv5 ids (<see cref="ShowcaseIds"/>), so a token issued BEFORE the nightly reset still names an existing user AFTER it
/// (same id, same security stamp). The wire names (<c>owner</c>, <c>master</c>, <c>client</c>) are the contract of <c>POST /api/demo/login</c>.
/// </summary>
public static class ShowcaseDemoRoles
{
    public const string Owner = "owner";
    public const string Master = "master";
    public const string Client = "client";

    // ARCHITECTURE_CYCLE35.md §35.7.1 — the three roles of "Заказы". Wire names are unique across both products, so POST /api/demo/login needs no product parameter.
    public const string ShopOwner = "shop-owner";
    public const string ShopStaff = "shop-staff";
    public const string ShopCustomer = "shop-customer";

    /// <summary>The flagship salon: the first company of the dataset, six masters, open for booking.</summary>
    public const string FlagshipCompanyKey = "lavanda";

    // Keys inside the generated ids: «owner key» of the spec, «company:m<i>» of the first master, and one extra client made only for the demo profile.
    public const string OwnerUserKey = "lavanda-owner";
    public const string MasterUserKey = "lavanda:m0";
    public const string ClientUserKey = "demo-client";

    /// <summary>The demo shop: the coffee shop (first of the five, §35.9.2). Its owner is the role <see cref="ShopOwner"/>, its first employee the role <see cref="ShopStaff"/>.</summary>
    public const string FlagshipShopKey = "kofeinya";

    // Keys inside the generated ids (ShowcaseIds.For("demo", "user", key)): «shop:<shop key>-owner» is the owner of every shop, «shop:<shop key>:s<i>» its i-th employee.
    public const string ShopOwnerUserKey = "shop:kofeinya-owner";
    public const string ShopStaffUserKey = "shop:kofeinya:s0";
    public const string ShopCustomerUserKey = "demo-shop-customer";

    /// <summary>The shops in which the demo customer has orders (coffee shop and bakery, both in Moscow; §35.9.3).</summary>
    public static readonly IReadOnlyList<string> CustomerShopKeys = [FlagshipShopKey, "pekarnya"];

    /// <summary>The companies in which the demo client has visits (the flagship and two more open ones), so "Мои визиты" shows several companies.</summary>
    public static readonly IReadOnlyList<string> ClientCompanyKeys = [FlagshipCompanyKey, "zhemchug", "vzglyad"];

    public static readonly IReadOnlyList<(string Role, string Label)> All =
    [
        (Owner, "Войти как владелец салона"),
        (Master, "Войти как мастер"),
        (Client, "Войти как клиент"),
        (ShopOwner, "Войти как владелец магазина"),
        (ShopStaff, "Войти как сотрудник магазина"),
        (ShopCustomer, "Войти как покупатель"),
    ];

    public static bool IsKnown(string? role) => role is Owner or Master or Client or ShopOwner or ShopStaff or ShopCustomer;

    /// <summary>The product a role belongs to; null for an unknown role.</summary>
    public static DemoProduct? ProductOf(string? role) => role switch
    {
        Owner or Master or Client => DemoProduct.Services,
        ShopOwner or ShopStaff or ShopCustomer => DemoProduct.Orders,
        _ => null,
    };

    /// <summary>The three roles of one product, in the order of the login page: owner, employee, client/customer.</summary>
    public static IReadOnlyList<(string Role, string Label)> ForProduct(DemoProduct product) =>
        All.Where(r => ProductOf(r.Role) == product).ToList();

    /// <summary>Parses the <c>product</c> query of <c>GET /api/demo/status</c>: absent or empty means <see cref="DemoProduct.Services"/> (the answer of cycle 28),
    /// anything but <c>services</c>/<c>orders</c> (case-insensitive) is not a product.</summary>
    public static bool TryParseProduct(string? value, out DemoProduct product)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) { product = DemoProduct.Services; return true; }
        if (string.Equals(trimmed, "services", StringComparison.OrdinalIgnoreCase)) { product = DemoProduct.Services; return true; }
        if (string.Equals(trimmed, "orders", StringComparison.OrdinalIgnoreCase)) { product = DemoProduct.Orders; return true; }
        product = DemoProduct.Services;
        return false;
    }

    private static readonly Lazy<IReadOnlySet<string>> DemoUserIds = new(() =>
        All.Select(r => UserIdOf(r.Role)!).ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="userId"/> is one of the six demo accounts (decided by the user, not by anything the token claims).</summary>
    public static bool IsDemoUserId(string? userId) => userId is not null && DemoUserIds.Value.Contains(userId);

    /// <summary>The id of the demo account of a role; null for an unknown role.</summary>
    public static string? UserIdOf(string? role) => role switch
    {
        Owner => ShowcaseIds.For(ShowcaseProfile.Demo.Name, "user", OwnerUserKey).ToString(),
        Master => ShowcaseIds.For(ShowcaseProfile.Demo.Name, "user", MasterUserKey).ToString(),
        Client => ShowcaseIds.For(ShowcaseProfile.Demo.Name, "user", ClientUserKey).ToString(),
        ShopOwner => ShowcaseIds.For(ShowcaseProfile.Demo.Name, "user", ShopOwnerUserKey).ToString(),
        ShopStaff => ShowcaseIds.For(ShowcaseProfile.Demo.Name, "user", ShopStaffUserKey).ToString(),
        ShopCustomer => ShowcaseIds.For(ShowcaseProfile.Demo.Name, "user", ShopCustomerUserKey).ToString(),
        _ => null,
    };
}
