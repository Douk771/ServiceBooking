using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Legal;

/// <summary>API_CONTRACT_CYCLE5.md §48.1 — POST /api/subject-requests (anonymous).</summary>
public record SubmitSubjectRequestDto(string? Kind, string? Phone, string? ContactValue, string? Message, string? CaptchaToken);

/// <summary>The response is IDENTICAL whether or not the phone is known to the system — §50.1's central
/// guarantee. Nothing here may ever vary based on lookup results.</summary>
public record SubjectRequestAcceptedDto(string Reference, int ResponseDueByWorkingDays);

/// <summary>API_CONTRACT_CYCLE5.md §48.2 — GET /api/admin/subject-requests row shape. `PhoneMasked`, not
/// the raw phone — same masking convention as OutboundNotification's admin-facing views.</summary>
public record SubjectRequestDto(
    Guid Id, string Reference, string Kind, string Status, string PhoneMasked, string ContactValue,
    string Message, DateTime ReceivedAt, DateTime DueAt, string DueState, DateTime? AnsweredAt,
    string? HandlerName, string? Resolution);

/// <summary>API_CONTRACT_CYCLE5.md §48.3 — POST /api/admin/subject-requests/{id}/status.</summary>
public record UpdateSubjectRequestStatusDto(SubjectRequestStatus Status, string? Resolution);
