namespace ServiceBooking.Core.Entities;

/// <summary>
/// Superadmin-editable platform parameters that must be changeable without a rebuild AND journaled
/// (ARCHITECTURE_CYCLE4.md §23.3) — a plain <c>appsettings</c> value satisfies neither, and a field on
/// <see cref="SubscriptionPlanConfig"/> would wrongly tie the channel price to one tariff when SPEC
/// wants a single platform-wide price. Cycle 4's keys: <c>notifications.channel.price-per-month</c> and
/// <c>notifications.channel.idle-days</c>. Absence of the price key means the option is not offered yet
/// (US-57 p.6) — it is never treated as "price 0".
/// </summary>
public class PlatformSetting
{
    public string Key { get; set; } = string.Empty; // PK, max 100
    public string Value { get; set; } = string.Empty; // max 200
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
