namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>API_CONTRACT_CYCLE4.md §32.1 — cabinet preferences.</summary>
public record NotificationPreferencesDto(bool Enabled);

public record UpdateNotificationPreferencesDto(bool Enabled);

/// <summary>API_CONTRACT_CYCLE4.md §32.2 — public unsubscribe page.</summary>
public record UnsubscribePageDto(string PhoneMasked, bool AlreadyOptedOut);
