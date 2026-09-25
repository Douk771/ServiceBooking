using System.ComponentModel.DataAnnotations;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Bookings;

public record BookingDto(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    // US-06 + Q12: lets the client cabinet build a "book again" link without a second lookup.
    string CompanySlug,
    Guid ServiceId,
    string ServiceName,
    string MasterId,
    string MasterName,
    string? ClientId,
    string ClientName,
    string? ClientPhone,
    string? ClientEmail,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    BookingStatus Status,
    PaymentStatus PaymentStatus,
    // US-06 + Q12: a snapshot of Service.Price at booking time (already on the entity — no migration).
    decimal Price,
    // US-06: null unless the booking was cancelled WITH a reason.
    string? CancellationReason,
    string? Notes,
    DateTime CreatedAt,
    // US-37, ARCHITECTURE.md §5.2: filled by the server, guest bookings only — null on staff manual
    // bookings and on authenticated-client bookings (API_CONTRACT.md §7.2).
    string? ConsentPrivacyVersion,
    string? ConsentTermsVersion,
    DateTime? ConsentAcceptedAt,
    // US-39: true once the client who made this booking has deleted their account (§7.4) — the booking
    // itself was anonymized, not deleted.
    bool ClientDeleted,
    // API_CONTRACT_CYCLE4.md §30.3: additive, staff-visible only (GET /api/bookings/master,
    // GET /api/bookings/{id}) — null on every other endpoint that reuses this DTO (Create,
    // GET /api/bookings/client) and whenever the booking has no notification queued at all.
    ReminderStatusDto? ReminderStatus = null,
    // US-67 (API_CONTRACT_CYCLE6.md §43.2): new, additive. Always non-empty, including for bookings
    // created before this cycle (backfilled to a single row). Σ services[].price == Price,
    // Σ services[].durationMinutes == TotalDurationMinutes, services[0].serviceId == ServiceId.
    int TotalDurationMinutes = 0,
    List<BookingServiceItemDto>? Services = null,
    // ARCHITECTURE_CYCLE5.md §44.5, API_CONTRACT_CYCLE5.md §46.2 — filled by the server from the
    // snapshot in effect at creation time, never from the request body.
    string? BookingNoticeVersion = null,
    bool BookedForOther = false,
    DateTime? GuardianConfirmedAt = null,
    // ARCHITECTURE_CYCLE10.md §123, API_CONTRACT_CYCLE10.md §123: additive. Filled ONLY on
    // GET /api/bookings/master and GET /api/bookings/{id} when the caller is staff of this company or
    // SuperAdmin — computed in one grouping query per page/call, never N+1 (ReminderStatusesForAsync's
    // pattern). Always null on GET /api/bookings/client and the Create response: not filtered on the
    // frontend, simply never computed there (П8). null means "server didn't count", not "zero events".
    int? HistoryEventCount = null,
    // ARCHITECTURE_CYCLE15.md §257.8/§286, API_CONTRACT_CYCLE15.md §286 — computed ONLY on
    // GET /api/bookings/client; null everywhere else ("server didn't compute this for this caller", the
    // project-wide convention, not "not allowed"). ClientRescheduleAllowed mirrors the exact rule set
    // PATCH .../reschedule applies for a ClientOwner caller (status, AllowSelfBooking, plan online-
    // booking gate, reschedule window) — a hint for the UI, re-checked by the server on the PATCH
    // itself, never trusted as a standing permission.
    bool? ClientRescheduleAllowed = null,
    int? ClientRescheduleMinHours = null,
    int? CompanyBookingHorizonDays = null
);

// US-67 (API_CONTRACT_CYCLE6.md §43.2): one line per service in the visit, in visit order.
public record BookingServiceItemDto(Guid ServiceId, string Name, int DurationMinutes, decimal Price);

public record OccupiedRangeDto(TimeOnly Start, TimeOnly End);

public record RescheduleDto(DateOnly Date, TimeOnly StartTime);

// ARCHITECTURE_CYCLE5.md §44.5, API_CONTRACT_CYCLE5.md §46.1 — the guardian-confirmation form (D12).
// TextVersion is verified against the live uiTexts.GuardianConfirmation snapshot server-side, never
// trusted as-is (same "server verifies, body only carries what the caller says they saw" rule as every
// other version check in this cycle).
public record GuardianConfirmationDto(string TextVersion, bool Confirmed);

public record CreateBookingDto(
    Guid CompanyId,
    Guid ServiceId,
    string MasterId,
    DateOnly Date,
    TimeOnly StartTime,
    // Attributes go directly on the positional record parameter, not as `[property: ...]` — see
    // ServiceDto.cs for why: on this runtime (net8.0, Microsoft.AspNetCore.App 8.0.3) the `[property:
    // ...]` form throws InvalidOperationException out of ASP.NET Core's record model-binding.
    [MaxLength(2000)] string? Notes,
    // Guest fields (used when not authenticated)
    [MaxLength(200)] string? GuestName,
    [MaxLength(32)] string? GuestPhone,
    [MaxLength(256)] string? GuestEmail,
    string? CaptchaToken,
    // US-67 (API_CONTRACT_CYCLE6.md §43.1): optional, 1..5, no duplicates. When present, ServiceId must
    // equal ServiceIds[0]. When absent (or empty), behavior is exactly the pre-cycle single-service path.
    List<Guid>? ServiceIds = null,
    // ARCHITECTURE_CYCLE5.md §44.5, US-78 п. 1 — appended at the end with defaults so every existing
    // positional CreateBookingDto(...) call in the codebase (and in the functional test suite) keeps
    // compiling unchanged: BookedForOther defaults false, requiring no confirmation, exactly today's
    // behavior (API_CONTRACT_CYCLE5.md §46.1's "bookedForOther: false — поведение ровно как сегодня").
    bool BookedForOther = false,
    GuardianConfirmationDto? GuardianConfirmation = null
);
