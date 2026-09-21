using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>API_CONTRACT_CYCLE4.md §30.1 — one row of GET /api/companies/{id}/notifications.</summary>
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
    Guid? ChannelId);

/// <summary>API_CONTRACT_CYCLE4.md §30.2 — GET /api/companies/{id}/notifications/summary.</summary>
public record NotificationSummaryDto(
    int Days, int Sent, int Delivered, int Read, int Failed, int Skipped, int Expired,
    DateTime? ChannelPaidUntil, IReadOnlyList<NotificationSummaryByCompanyDto>? ByCompany);

public record NotificationSummaryByCompanyDto(
    Guid CompanyId, string CompanyName, int Sent, int Delivered, int Read, int Failed, int Skipped, int Expired);

/// <summary>API_CONTRACT_CYCLE4.md §30.3 — additive field on GET /api/bookings/master and
/// GET /api/bookings/{id}.</summary>
public record ReminderStatusDto(NotificationStatus Status, string Text);
