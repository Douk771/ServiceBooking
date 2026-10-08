using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications.Funding;

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.7.1 (Р40-Ю1, Т40-L-04) — the ONE place that decides whether a messenger option is
/// open to owners and whether it can be sold. Pure: the platform-setting value, the configuration default and the
/// catalog facts are parameters. Vectors: <c>availability</c> in contracts/cycle40/channel-vectors.json.
/// </summary>
public static class ChannelOptionAvailability
{
    /// <summary><c>open(X)</c>: the platform setting (<c>"true"</c>/<c>"false"</c>) when it holds one of those two
    /// values, otherwise (no key, empty, garbage) the configuration default.</summary>
    public static bool IsOpen(string? settingValue, bool configDefault)
    {
        if (string.Equals(settingValue?.Trim(), "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(settingValue?.Trim(), "false", StringComparison.OrdinalIgnoreCase)) return false;
        return configDefault;
    }

    /// <summary><c>sellable(X) = open ∧ IsActive ∧ PricePerMonth ≠ null ∧ LegalOptionGuards.IsPubliclySellable</c>.</summary>
    public static bool Sellable(bool open, bool isActive, decimal? pricePerMonth, bool termsOwnerPublished) =>
        open && isActive && pricePerMonth is not null && termsOwnerPublished;

    public static OptionAvailability Evaluate(
        string? settingValue, bool configDefault, bool isActive, decimal? pricePerMonth, bool termsOwnerPublished)
    {
        var open = IsOpen(settingValue, configDefault);
        return new OptionAvailability(open, Sellable(open, isActive, pricePerMonth, termsOwnerPublished));
    }

    /// <summary>Platform-setting key of a transport's availability switch (§40.7.1).</summary>
    public static string SettingKey(NotificationTransport transport) => transport switch
    {
        NotificationTransport.WhatsApp => "notifications.option.whatsapp.open",
        NotificationTransport.Max => "notifications.option.max.open",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null),
    };
}

public readonly record struct OptionAvailability(bool Open, bool Sellable);
