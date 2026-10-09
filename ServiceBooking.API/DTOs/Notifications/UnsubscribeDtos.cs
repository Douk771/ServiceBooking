namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>API_CONTRACT_CYCLE4.md §32.1 — cabinet preferences.</summary>
// Cycle 40 (ARCHITECTURE_CYCLE40.md §40.11.4, Т40-L-09): ProviderDeliveryConsent = the account has a current PdnConsent/ProviderDelivery (the frontend pre-ticks the
// messenger mark only when Enabled ∧ ProviderDeliveryConsent).
public record NotificationPreferencesDto(bool Enabled, bool ProviderDeliveryConsent = false);

public record UpdateNotificationPreferencesDto(bool Enabled);

/// <summary>API_CONTRACT_CYCLE4.md §32.2 — public unsubscribe page.</summary>
public record UnsubscribePageDto(string PhoneMasked, bool AlreadyOptedOut);
