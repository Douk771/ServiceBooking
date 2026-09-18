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

/// <summary>API_CONTRACT_CYCLE4.md §28.1/§28.2 — GET/PUT /api/companies/{id}/notification-settings.</summary>
public record NotificationSettingsDto(
    IReadOnlyList<NotificationType> EnabledTypes,
    int ReminderLeadMinutes,
    int MinLeadMinutes,
    bool PlanAllowsChannel,
    SettingsChannelDto Channel,
    bool EffectiveEnabled,
    string? BlockedReason);

public record UpdateNotificationSettingsDto(
    IReadOnlyList<NotificationType> EnabledTypes, int ReminderLeadMinutes, int MinLeadMinutes);
