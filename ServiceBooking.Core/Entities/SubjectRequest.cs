using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// A subject's public request under 152-ФЗ (access/rectification/erasure/consent-withdrawal/complaint) —
/// US-74, ARCHITECTURE_CYCLE5.md §44.4. Created anonymously; answered by a human (SuperAdmin), never
/// automated (ПЛ4).
/// </summary>
public class SubjectRequest
{
    public Guid Id { get; set; }

    // Human-readable, shown to the anonymous requester so they can reference their own request in
    // follow-up correspondence — never used to look anything up on their behalf (§50.1: the response to
    // POST is identical whether the phone is known to the system or not).
    public string Reference { get; set; } = string.Empty;

    public SubjectRequestKind Kind { get; set; }
    public string SubjectPhone { get; set; } = string.Empty;
    public string ContactValue { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public SubjectRequestStatus Status { get; set; } = SubjectRequestStatus.Received;

    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    // Computed and STORED at intake time, from the config value in effect THEN — never recomputed live
    // from the current config (ARCHITECTURE_CYCLE5.md §44.4: a later change to SubjectRequests:
    // ResponseWorkingDays must not silently reset the promise already made to this specific person).
    public DateTime DueAtUtc { get; set; }

    public DateTime? AnsweredAtUtc { get; set; }
    public string? HandlerUserId { get; set; }
    public string? Resolution { get; set; }
}
