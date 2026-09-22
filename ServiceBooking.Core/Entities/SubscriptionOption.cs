using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// Cycle 7 catalog of purchasable options (ARCHITECTURE_CYCLE7.md §43.3) — "extra companies", "extra
/// employees", "WhatsApp channel", etc. Scope note: only the columns the public/admin pricing screen
/// (§48, GET /api/pricing, GET /api/admin/pricing/preview) actually reads are wired up by consumers
/// today. <see cref="CapabilityKey"/>, <see cref="MaxQuantity"/> and the per-plan availability rule
/// table (<c>PlanOptionRule</c>) are part of the same architecture section but belong to the
/// billing-owner/billing-admin endpoints, which are a separate, not-yet-implemented slice of this
/// cycle — see the backend report for cycle 07.
/// </summary>
public class SubscriptionOption
{
    public Guid Id { get; set; }

    // Machine code (e.g. "extra-companies"). Immutable after creation (400 on attempted change,
    // ARCHITECTURE_CYCLE7.md §38.2) — enforced by the admin write endpoint, not by the database.
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public OptionKind Kind { get; set; }

    // What the option grants (§44.2 capability resolution) — null means it's a pure price-list line
    // that includes nothing. Not consumed by anything yet in this slice; see class remarks.
    public string? CapabilityKey { get; set; }

    // null = not for sale — same "absence, never zero" convention as
    // PlatformSettings.ChannelPricePerMonthKey (cycle 4). Options without a price never appear on the
    // public price list regardless of IsPublic.
    public decimal? PricePerMonth { get; set; }

    // Required for Kind == Quantity, forbidden for Kind == Toggle (400) — validated by the admin write
    // endpoint, not by the database.
    public string? UnitName { get; set; }

    public int? MaxQuantity { get; set; }

    // Ready-made showcase phrase ("дополнительная компания — 490 ₽/мес", ARCHITECTURE_CYCLE7.md §48) —
    // admin-authored because the exact Russian wording per option is business copy, not something a
    // formula over UnitName can reconstruct ("номер для рассылок" isn't "номер" prefixed the same way
    // "дополнительная компания" is). Not listed as its own column in §43.3's table; kept here as an
    // additive field so PricingCatalogBuilder has a real value to serve instead of a generic filler
    // ("<unitName> — <price> ₽/мес") once the admin write endpoint (a later slice) starts populating
    // it. Null falls back to that generic phrasing.
    public string? UnitPriceText { get; set; }

    public bool IsPublic { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
