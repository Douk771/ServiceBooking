using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>API_CONTRACT_CYCLE4.md §30.1 — one row of GET /api/companies/{id}/notifications.
/// <see cref="ContentRedacted"/> is additive (T5-B8/B9, API_CONTRACT_CYCLE5.md §51): this endpoint has
/// never returned raw message text (only <see cref="RecipientPhoneMasked"/>, never a <c>body</c> field),
/// so unlike §51's illustrative diff there is no <c>body</c>/<c>recipientPhone</c> pair to null out here —
/// only the one flag a caller needs to tell "text purged by the retention sweep" apart from "recipient
/// scrubbed by account deletion", since <see cref="RecipientPhoneMasked"/> reads "получатель удалён" for
/// both once the underlying <c>RecipientPhone</c> is empty (§49.3 wipes it the same way DeleteAccount
/// already did).</summary>
public record NotificationLogItemDto(
    Guid Id,
    DateTime CreatedAt,
    NotificationType Type,
    string TypeText,
    string? RecipientName,
    string RecipientPhoneMasked,
    NotificationStatus Status,
    string StatusText,
    Guid? BookingId,
    DateTime VisitStart,
    DateTime? SentAt,
    Guid? ChannelId,
    // ARCHITECTURE_CYCLE9.md §104.5/§114.3 (US-120/US-125) — additive. In AllChannels mode one event
    // produces several rows with the same BookingId/Type and different Transport; the frontend groups
    // them so the owner doesn't read two rows as "sent twice by mistake".
    NotificationTransport Transport,
    bool ContentRedacted = false);

/// <summary>API_CONTRACT_CYCLE4.md §30.2 — GET /api/companies/{id}/notifications/summary.</summary>
public record NotificationSummaryDto(
    int Days, int Sent, int Delivered, int Read, int Failed, int Skipped, int Expired,
    DateTime? ChannelPaidUntil, IReadOnlyList<NotificationSummaryByCompanyDto>? ByCompany);

public record NotificationSummaryByCompanyDto(
    Guid CompanyId, string CompanyName, int Sent, int Delivered, int Read, int Failed, int Skipped, int Expired);

/// <summary>API_CONTRACT_CYCLE4.md §30.3 — additive field on GET /api/bookings/master and
/// GET /api/bookings/{id}.</summary>
public record ReminderStatusDto(NotificationStatus Status, string Text);
