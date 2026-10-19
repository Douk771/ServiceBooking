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

    /// <summary>
    /// ARCHITECTURE_CYCLE43.md §43.0 (Д-1), API_CONTRACT_CYCLE43.md §43.22.1 — the same decision as <see cref="Sellable"/> with the first failed
    /// check named. <c>Sellable == (Reason == null)</c> always. Vectors: <c>sale</c> in contracts/cycle43/messenger-vectors.json.
    /// </summary>
    public static MessengerSaleState Explain(bool open, bool isActive, decimal? pricePerMonth, bool termsOwnerPublished)
    {
        MessengerSaleReason? reason =
            !open ? MessengerSaleReason.Closed :
            !isActive ? MessengerSaleReason.Inactive :
            pricePerMonth is null ? MessengerSaleReason.NoPrice :
            !termsOwnerPublished ? MessengerSaleReason.OfferNotPublished :
            null;
        return new MessengerSaleState(reason is null, reason, MessengerTexts.SaleText(reason));
    }

    public static OptionAvailabilityState Evaluate(
        string? settingValue, bool configDefault, bool isActive, decimal? pricePerMonth, bool termsOwnerPublished)
    {
        var open = IsOpen(settingValue, configDefault);
        return new OptionAvailabilityState(open, Sellable(open, isActive, pricePerMonth, termsOwnerPublished));
    }

    /// <summary>Platform-setting key of a transport's availability switch (§40.7.1).</summary>
    public static string SettingKey(NotificationTransport transport) => PlatformSettings.OptionOpenKey(transport);
}

public readonly record struct OptionAvailabilityState(bool Open, bool Sellable);

/// <summary>Why a messenger is not for sale — the first failed check, in the order of the enum (API_CONTRACT_CYCLE43.md §43.22.1).</summary>
public enum MessengerSaleReason { Closed, Inactive, NoPrice, OfferNotPublished }

public readonly record struct MessengerSaleState(bool Sellable, MessengerSaleReason? Reason, string Text);
