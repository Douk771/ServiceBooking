using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>API_CONTRACT_CYCLE4.md §28.1's nested <c>channel</c> object. <c>StateText</c> is this
/// cycle's one authorized contract fix (see the top of API_CONTRACT_CYCLE4.md §28.1 and this
/// developer's cover note): the frontend found that this endpoint returned <c>channel.state</c> but not
/// <c>channel.stateText</c>, so the disruption banner on the company's "Notifications" tab had to invent
/// its own wording instead of reusing the one <c>GET /api/notification-channels</c> already renders —
/// same event, two different sentences. <c>StateText</c> is built by the exact same
/// <c>ChannelPresentation.StateText</c> call <c>NotificationChannelsController</c> uses
/// for <c>ChannelDto.StateText</c>, so the two can never drift again. <see langword="null"/> only when
/// <c>Assigned</c> is <see langword="false"/> (no channel to describe).</summary>
public record SettingsChannelDto(
    bool Assigned, Guid? ChannelId, ChannelState? State, string? StateText,
    ChannelPaymentStatus? PaymentState, DateTime? PaidUntil);

/// <summary>API_CONTRACT_CYCLE4.md §28.1/§28.2 — GET/PUT /api/companies/{id}/notification-settings.
/// ARCHITECTURE_CYCLE9.md §104.5/§114.4 (US-125) adds the four delivery-mode fields at the end,
/// additively — everything above <see cref="DeliveryMode"/> is UNCHANGED, in both name and position.
/// <see cref="Channel"/> keeps describing the PRIORITY transport's channel specifically (the one
/// <see cref="PriorityTransport"/> names) — with more than one transport possibly connected, "the one
/// channel this screen's legacy fields are about" has to mean something, and the priority channel is the
/// one the rest of the settings (template gating, 402 checks) already revolve around.</summary>
public record NotificationSettingsDto(
    IReadOnlyList<NotificationType> EnabledTypes,
    int ReminderLeadMinutes,
    int MinLeadMinutes,
    bool PlanAllowsChannel,
    SettingsChannelDto Channel,
    bool EffectiveEnabled,
    string? BlockedReason,
    NotificationDeliveryMode DeliveryMode,
    NotificationTransport PriorityTransport,
    IReadOnlyList<NotificationTransport> ConnectedTransports,
    bool PriorityChannelHealthy);

/// <summary>ARCHITECTURE_CYCLE9.md §104.5/§114.4: <see cref="DeliveryMode"/>/<see cref="PriorityTransport"/>
/// are optional — "не прислали — не меняем", the same convention as every other optional PUT field in
/// this project. <see cref="PriorityTransport"/> not being one of the caller's own connected transports
/// is a 400, checked in the controller (it needs live channel data this pure DTO doesn't have).</summary>
public record UpdateNotificationSettingsDto(
    IReadOnlyList<NotificationType> EnabledTypes, int ReminderLeadMinutes, int MinLeadMinutes,
    NotificationDeliveryMode? DeliveryMode = null, NotificationTransport? PriorityTransport = null);
