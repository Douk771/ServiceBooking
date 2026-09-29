namespace ServiceBooking.API.Services.Orders;

/// <summary>ARCHITECTURE_CYCLE23.md §401.5 — the "Orders" configuration section (committed defaults in appsettings.json).</summary>
public sealed class OrdersOptions
{
    public const string SectionName = "Orders";

    public int MaxProductsPerShop { get; set; } = 1000;
    public int MaxCategoriesPerShop { get; set; } = 100;

    /// <summary>Most lines in one order / cart.</summary>
    public int MaxLines { get; set; } = 50;

    // ── Cycle 24 (ARCHITECTURE_CYCLE24.md §463.4) ──

    /// <summary>A message about an order that has not left the queue this many minutes after it was queued is expired as "outdated" (§457.1).</summary>
    public int CustomerMessageTtlMinutes { get; set; } = 120;

    /// <summary>Browsers one order can hold web-push subscriptions for; the oldest is dropped silently past it (§456.1).</summary>
    public int MaxPushSubscriptionsPerOrder { get; set; } = 5;

    /// <summary>How far ahead a special day can be set (§449.4).</summary>
    public int SpecialDaysHorizonDays { get; set; } = 90;

    /// <summary>The daily-menu calendar covers at least this many days ahead, even with a shorter pre-order horizon (§452.3).</summary>
    public int DailyMenuMinHorizonDays { get; set; } = 7;

    public PhoneLimitsOptions PhoneLimits { get; set; } = new();

    public sealed class PhoneLimitsOptions
    {
        /// <summary>Active (New/Accepted/Ready) orders of one phone in one shop.</summary>
        public int MaxActivePerShop { get; set; } = 5;

        /// <summary>Orders of one phone created in the last 24 hours across the whole platform.</summary>
        public int MaxPerDay { get; set; } = 20;
    }
}
