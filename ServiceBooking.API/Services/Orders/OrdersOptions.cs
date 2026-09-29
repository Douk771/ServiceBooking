namespace ServiceBooking.API.Services.Orders;

/// <summary>ARCHITECTURE_CYCLE23.md §401.5 — the "Orders" configuration section (committed defaults in appsettings.json).</summary>
public sealed class OrdersOptions
{
    public const string SectionName = "Orders";

    public int MaxProductsPerShop { get; set; } = 1000;
    public int MaxCategoriesPerShop { get; set; } = 100;

    /// <summary>Most lines in one order / cart.</summary>
    public int MaxLines { get; set; } = 50;

    public PhoneLimitsOptions PhoneLimits { get; set; } = new();

    public sealed class PhoneLimitsOptions
    {
        /// <summary>Active (New/Accepted/Ready) orders of one phone in one shop.</summary>
        public int MaxActivePerShop { get; set; } = 5;

        /// <summary>Orders of one phone created in the last 24 hours across the whole platform.</summary>
        public int MaxPerDay { get; set; } = 20;
    }
}
