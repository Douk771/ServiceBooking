namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>contracts/cycle9/openapi.yaml — <c>GET /api/push/config</c> (US-118). <c>PublicKey</c> is the
/// ONLY way the VAPID public key reaches the browser (§105.2); <c>Enabled=false</c> is a normal,
/// documented state (SPEC П13), not an error.</summary>
public record PushConfigDto(bool Enabled, string? PublicKey, int MaxSubscriptionsPerUser, IReadOnlyList<PushConfigCompanyDto> Companies);

/// <summary>One company the caller is staff of, and whether that company currently wants push at all —
/// <c>StaffPushEnabled=false</c> means the frontend must not even ask for browser permission (US-118).</summary>
public record PushConfigCompanyDto(Guid CompanyId, string CompanyName, bool StaffPushEnabled);

/// <summary><c>GET /api/push/subscriptions</c>. Deliberately no keys anywhere in this shape — see
/// <see cref="PushSubscriptionDto"/>'s own doc comment.</summary>
public record PushSubscriptionListDto(IReadOnlyList<PushSubscriptionDto> Items);

/// <summary>⚠️ Ключи подписки (p256dh, auth) НЕ ОТДАЮТСЯ здесь ни в каком виде — this record has no
/// property for them at all, so leaking them would require adding one, not forgetting to redact one.</summary>
public record PushSubscriptionDto(Guid Id, string DeviceLabel, DateTime CreatedAtUtc, DateTime? LastSuccessAtUtc, bool IsCurrent);

/// <summary><c>POST /api/push/subscriptions</c> request body — no owner field anywhere in this shape
/// (US-123: "создать чужую подписку" невозможно по форме запроса, владелец берётся из токена).</summary>
public record CreatePushSubscriptionInput(string Endpoint, CreatePushSubscriptionKeysInput Keys, string? DeviceLabel);

public record CreatePushSubscriptionKeysInput(string P256dh, string Auth);

/// <summary><c>DELETE /api/push/subscriptions/current</c> request body.</summary>
public record DeleteCurrentPushSubscriptionInput(string Endpoint);

/// <summary><c>GET|PUT /api/companies/{companyId}/staff-push-settings</c> (US-117). Tariff-free,
/// channel-free — see CompanyNotificationSettings.StaffPushEnabled's own doc comment for why this is a
/// separate route rather than a field on the existing (perpetually-402) notification-settings screen.</summary>
public record StaffPushSettingsDto(bool StaffPushEnabled);
