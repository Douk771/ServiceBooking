namespace ServiceBooking.Core.Enums;

/// <summary>ARCHITECTURE_CYCLE43.md §43.2.1 — which path changed the price or activity of a catalog option (journal
/// SubscriptionOptionPriceChangeLogs). Stored as int; values are fixed, only appended.</summary>
public enum OptionPriceChangeSource
{
    MessengerPriceScreen = 0,
    OptionsCatalogApi = 1
}
